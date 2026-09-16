using Jellyfin.Plugin.SeasonIdentifier.Configuration;
using Jellyfin.Plugin.SeasonIdentifier.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.SeasonIdentifier.Providers;

/// <summary>
/// Applies external title metadata to a mapped local season after Jellyfin's normal providers run.
/// Native Jellyfin Identify selections are captured here and converted into persistent title mappings.
/// </summary>
public class SeasonMappingMetadataProvider : ICustomMetadataProvider<Season>, IHasItemChangeMonitor
{
    private readonly SeasonMappingService _mappings;
    private readonly IProviderManager _providerManager;
    private readonly ILibraryManager _libraryManager;
    private readonly TmdbMappedSupplementService _tmdbSupplement;

    public SeasonMappingMetadataProvider(
        SeasonMappingService mappings,
        IProviderManager providerManager,
        ILibraryManager libraryManager,
        TmdbMappedSupplementService tmdbSupplement)
    {
        _mappings = mappings;
        _providerManager = providerManager;
        _libraryManager = libraryManager;
        _tmdbSupplement = tmdbSupplement;
    }

    public string Name => "Season Identifier";

    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        _ = directoryService;

        if (item is not Season season)
        {
            return false;
        }

        var mapping = _mappings.Get(season.Id);
        return mapping is not null
            && string.Equals(mapping.Mode, "Title", StringComparison.OrdinalIgnoreCase)
            && HasBrokenEpisodeLinks(season, mapping);
    }

    public async Task<ItemUpdateType> FetchAsync(
        Season item,
        MetadataRefreshOptions options,
        CancellationToken cancellationToken)
    {
        var mapping = _mappings.Get(item.Id);
        var isManualIdentify = IsManualIdentify(options.SearchResult);

        if (isManualIdentify)
        {
            mapping = CreateMapping(item, options.SearchResult!);
            _mappings.Upsert(mapping);
        }

        if (mapping is null || !string.Equals(mapping.Mode, "Title", StringComparison.OrdinalIgnoreCase))
        {
            return ItemUpdateType.None;
        }

        var hasBrokenEpisodeLinks = HasBrokenEpisodeLinks(item, mapping);

        item.ProviderIds.Clear();

        var updateType = ItemUpdateType.None;
        var series = item.Series;

        if (series is not null)
        {
            var lookup = new SeriesInfo
            {
                Name = mapping.ExternalTitleName,
                Year = mapping.ExternalYear,
                MetadataLanguage = item.GetPreferredMetadataLanguage(),
                MetadataCountryCode = item.GetPreferredMetadataCountryCode(),
                ProviderIds = SeasonMappingService.ToProviderDictionary(mapping),
                IsAutomated = false
            };

            var providers = _providerManager
                .GetMetadataProviders<Series>(series, _libraryManager.GetLibraryOptions(series))
                .OfType<IRemoteMetadataProvider<Series, SeriesInfo>>()
                .OrderBy(x => string.Equals(x.Name, mapping.SearchProviderName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToArray();

            foreach (var provider in providers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await provider.GetMetadata(lookup, cancellationToken).ConfigureAwait(false);
                if (!result.HasMetadata || result.Item is null)
                {
                    continue;
                }

                MetadataCopy.ApplySeriesToSeason(result.Item, item);

                if (result.People is { Count: > 0 })
                {
                    await _libraryManager.UpdatePeopleAsync(item, result.People, cancellationToken).ConfigureAwait(false);
                }

                updateType |= ItemUpdateType.MetadataDownload;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(mapping.ExternalTitleName))
        {
            item.Name = mapping.ExternalTitleName;
            updateType |= ItemUpdateType.MetadataEdit;
        }

        if (await _tmdbSupplement.ApplySeasonImagesAsync(item, mapping, cancellationToken).ConfigureAwait(false))
        {
            updateType |= ItemUpdateType.ImageUpdate;
        }

        // A broken local episode link is itself enough reason to cascade, including during the
        // default "Scan for new and updated files" mode. Once repaired, HasChanged becomes false,
        // so automated scans do not keep re-fetching already healthy mapped seasons forever.
        if (isManualIdentify
            || hasBrokenEpisodeLinks
            || (!options.IsAutomated && options.MetadataRefreshMode == MetadataRefreshMode.FullRefresh))
        {
            await QueueEpisodeRefreshesAsync(item, mapping, options, cancellationToken).ConfigureAwait(false);
        }

        return updateType;
    }

    private static bool IsManualIdentify(RemoteSearchResult? result)
        => result is not null
            && result.ProviderIds is { Count: > 0 }
            && !string.IsNullOrWhiteSpace(result.Name);

    private static SeasonMapping CreateMapping(Season item, RemoteSearchResult result)
    {
        return new SeasonMapping
        {
            LocalSeasonId = item.Id,
            LocalSeriesName = item.Series?.Name ?? item.SeriesName ?? string.Empty,
            LocalSeasonName = item.Name ?? $"Season {item.IndexNumber}",
            LocalSeasonNumber = item.IndexNumber,
            ExternalTitleName = result.Name ?? string.Empty,
            ExternalYear = result.ProductionYear,
            SearchProviderName = result.SearchProviderName ?? string.Empty,
            ImageUrl = result.ImageUrl,
            Mode = "Title",
            ProviderIds = result.ProviderIds
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => new ProviderIdEntry { Key = x.Key, Value = x.Value })
                .ToList()
        };
    }

    private static bool HasBrokenEpisodeLinks(Season season, SeasonMapping mapping)
    {
        var localSeasonNumber = season.IndexNumber ?? mapping.LocalSeasonNumber;
        var localSeries = season.Series;

        return season
            .GetRecursiveChildren(i => i is Episode)
            .OfType<Episode>()
            .Where(x => !x.IsVirtualItem)
            .Any(episode =>
                episode.SeasonId != season.Id
                || (localSeasonNumber.HasValue && episode.ParentIndexNumber != localSeasonNumber.Value)
                || (localSeries is not null && episode.SeriesId != localSeries.Id));
    }

    private async Task QueueEpisodeRefreshesAsync(
        Season season,
        SeasonMapping mapping,
        MetadataRefreshOptions sourceOptions,
        CancellationToken cancellationToken)
    {
        var episodes = season
            .GetRecursiveChildren(i => i is Episode)
            .OfType<Episode>()
            .Concat(season.GetEpisodes().OfType<Episode>())
            .Where(x => !x.IsVirtualItem)
            .DistinctBy(x => x.Id)
            .ToArray();

        var localSeasonNumber = season.IndexNumber ?? mapping.LocalSeasonNumber;
        var localSeries = season.Series;

        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var repairedLocalIdentity = false;

            if (episode.SeasonId != season.Id)
            {
                episode.SeasonId = season.Id;
                repairedLocalIdentity = true;
            }

            if (localSeasonNumber.HasValue && episode.ParentIndexNumber != localSeasonNumber.Value)
            {
                episode.ParentIndexNumber = localSeasonNumber.Value;
                repairedLocalIdentity = true;
            }

            if (localSeries is not null && episode.SeriesId != localSeries.Id)
            {
                episode.SeriesId = localSeries.Id;
                repairedLocalIdentity = true;
            }

            if (repairedLocalIdentity)
            {
                await episode.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            }

            var options = new MetadataRefreshOptions(sourceOptions)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.None,
                ReplaceAllMetadata = false,
                ReplaceAllImages = false,
                RemoveOldMetadata = false,
                ForceSave = true,
                IsAutomated = false,
                SearchResult = null
            };

            _providerManager.QueueRefresh(episode.Id, options, RefreshPriority.High);
        }
    }
}
