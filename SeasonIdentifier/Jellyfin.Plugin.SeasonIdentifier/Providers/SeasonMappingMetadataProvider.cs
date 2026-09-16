using Jellyfin.Plugin.SeasonIdentifier.Configuration;
using Jellyfin.Plugin.SeasonIdentifier.Services;
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
public class SeasonMappingMetadataProvider : ICustomMetadataProvider<Season>
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

        // Provider IDs for the external title belong to the plugin mapping, not to this local
        // Season object. Leaving them here would let a later ordinary refresh reinterpret it.
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

        // Even if a provider returned only sparse text metadata, keep the selected title as the
        // visible season identity rather than falling back to a generic "Season N" label.
        if (string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(mapping.ExternalTitleName))
        {
            item.Name = mapping.ExternalTitleName;
            updateType |= ItemUpdateType.MetadataEdit;
        }

        if (await _tmdbSupplement.ApplySeasonImagesAsync(item, mapping, cancellationToken).ConfigureAwait(false))
        {
            updateType |= ItemUpdateType.ImageUpdate;
        }

        // Re-identifying or manually running a full refresh on the mapped season should repair its
        // children too. Automated library scans do not cascade, so this does not create scan storms.
        if (isManualIdentify
            || (!options.IsAutomated && options.MetadataRefreshMode == MetadataRefreshMode.FullRefresh))
        {
            QueueEpisodeRefreshes(item, options);
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

    private void QueueEpisodeRefreshes(Season season, MetadataRefreshOptions sourceOptions)
    {
        foreach (var episode in season.GetEpisodes().OfType<Episode>().Where(x => !x.IsVirtualItem))
        {
            var options = new MetadataRefreshOptions(sourceOptions)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                // Season Identifier saves mapped episode stills itself. Running Jellyfin's normal
                // image provider here would use the real parent Series id and can fetch the wrong art.
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
