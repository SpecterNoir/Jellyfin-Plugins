using System.Collections.Concurrent;
using Jellyfin.Plugin.SeasonIdentifier.Configuration;
using Jellyfin.Plugin.SeasonIdentifier.Services;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.SeasonIdentifier.Providers;

/// <summary>
/// Translates a local episode sequence into the numbered seasons of a mapped external title.
/// </summary>
public class EpisodeMappingMetadataProvider : ICustomMetadataProvider<Episode>
{
    private const int MaxExternalSeasons = 50;
    private readonly SeasonMappingService _mappings;
    private readonly IProviderManager _providerManager;
    private readonly ILibraryManager _libraryManager;
    private readonly TmdbMappedSupplementService _tmdbSupplement;
    private readonly ConcurrentDictionary<string, int> _seasonEpisodeCounts = new(StringComparer.Ordinal);

    public EpisodeMappingMetadataProvider(
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
        Episode item,
        MetadataRefreshOptions options,
        CancellationToken cancellationToken)
    {
        if (!item.IndexNumber.HasValue || item.IndexNumber.Value < 1)
        {
            return ItemUpdateType.None;
        }

        // SeasonId is not guaranteed to be populated on every Episode object. Jellyfin's own
        // Episode.Season accessor falls back to the folder hierarchy, so do the same here.
        var localSeasonId = item.SeasonId;
        if (localSeasonId == Guid.Empty)
        {
            localSeasonId = item.Season?.Id ?? Guid.Empty;
        }

        if (localSeasonId == Guid.Empty)
        {
            return ItemUpdateType.None;
        }

        var mapping = _mappings.Get(localSeasonId);
        if (mapping is null || !string.Equals(mapping.Mode, "Title", StringComparison.OrdinalIgnoreCase))
        {
            return ItemUpdateType.None;
        }

        var providers = _providerManager
            .GetMetadataProviders<Episode>(item, _libraryManager.GetLibraryOptions(item))
            .OfType<IRemoteMetadataProvider<Episode, EpisodeInfo>>()
            .OrderBy(x => string.Equals(x.Name, mapping.SearchProviderName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToArray();

        if (providers.Length == 0)
        {
            return ItemUpdateType.None;
        }

        foreach (var provider in providers)
        {
            var resolved = await ResolveAsync(
                provider,
                mapping,
                item,
                item.IndexNumber.Value,
                cancellationToken).ConfigureAwait(false);

            if (resolved is null)
            {
                continue;
            }

            MetadataCopy.ApplyEpisode(resolved.Result.Item, item);

            if (resolved.Result.People is { Count: > 0 })
            {
                await _libraryManager.UpdatePeopleAsync(item, resolved.Result.People, cancellationToken).ConfigureAwait(false);
            }

            var updateType = ItemUpdateType.MetadataDownload;

            // TMDb's normal image provider looks at the real parent Series id, which intentionally
            // remains the local series. Ask TMDb with the mapped title identity instead and use the
            // same response as a fallback for missing descriptions.
            var supplement = await _tmdbSupplement.GetEpisodeSupplementAsync(
                mapping,
                resolved.ExternalSeason,
                resolved.ExternalEpisode,
                item,
                cancellationToken).ConfigureAwait(false);

            if (supplement is not null)
            {
                if (string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(supplement.Name))
                {
                    item.Name = supplement.Name;
                }

                if (string.IsNullOrWhiteSpace(item.Overview) && !string.IsNullOrWhiteSpace(supplement.Overview))
                {
                    item.Overview = supplement.Overview;
                }

                if (!item.PremiereDate.HasValue && supplement.PremiereDate.HasValue)
                {
                    item.PremiereDate = supplement.PremiereDate;
                }

                if (!item.ProductionYear.HasValue && supplement.ProductionYear.HasValue)
                {
                    item.ProductionYear = supplement.ProductionYear;
                }

                if (!item.CommunityRating.HasValue && supplement.CommunityRating.HasValue)
                {
                    item.CommunityRating = supplement.CommunityRating;
                }

                if (await _tmdbSupplement.ApplyEpisodeImageAsync(item, supplement, cancellationToken).ConfigureAwait(false))
                {
                    updateType |= ItemUpdateType.ImageUpdate;
                }
            }

            return updateType;
        }

        return ItemUpdateType.None;
    }

    private async Task<ResolvedEpisode?> ResolveAsync(
        IRemoteMetadataProvider<Episode, EpisodeInfo> provider,
        SeasonMapping mapping,
        Episode localEpisode,
        int localEpisodeNumber,
        CancellationToken cancellationToken)
    {
        var remaining = localEpisodeNumber;

        for (var externalSeason = 1; externalSeason <= MaxExternalSeasons; externalSeason++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var direct = await FetchRemoteAsync(
                provider,
                mapping,
                localEpisode,
                externalSeason,
                remaining,
                cancellationToken).ConfigureAwait(false);

            if (direct.HasMetadata && direct.Item is not null)
            {
                return new ResolvedEpisode(direct, externalSeason, remaining);
            }

            var cacheKey = BuildCountCacheKey(mapping, provider.Name, externalSeason);
            if (!_seasonEpisodeCounts.TryGetValue(cacheKey, out var count))
            {
                count = await FindSeasonEpisodeCountAsync(
                    provider,
                    mapping,
                    localEpisode,
                    externalSeason,
                    remaining - 1,
                    cancellationToken).ConfigureAwait(false);

                _seasonEpisodeCounts[cacheKey] = count;
            }

            if (count <= 0 || remaining <= count)
            {
                return null;
            }

            remaining -= count;
        }

        return null;
    }

    private async Task<int> FindSeasonEpisodeCountAsync(
        IRemoteMetadataProvider<Episode, EpisodeInfo> provider,
        SeasonMapping mapping,
        Episode localEpisode,
        int externalSeason,
        int upperBound,
        CancellationToken cancellationToken)
    {
        if (upperBound < 1)
        {
            return 0;
        }

        var first = await FetchRemoteAsync(
            provider,
            mapping,
            localEpisode,
            externalSeason,
            1,
            cancellationToken).ConfigureAwait(false);

        if (!first.HasMetadata)
        {
            return 0;
        }

        var low = 1;
        var high = upperBound;

        while (low < high)
        {
            var mid = low + ((high - low + 1) / 2);
            var candidate = await FetchRemoteAsync(
                provider,
                mapping,
                localEpisode,
                externalSeason,
                mid,
                cancellationToken).ConfigureAwait(false);

            if (candidate.HasMetadata)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    private static Task<MetadataResult<Episode>> FetchRemoteAsync(
        IRemoteMetadataProvider<Episode, EpisodeInfo> provider,
        SeasonMapping mapping,
        Episode localEpisode,
        int externalSeason,
        int externalEpisode,
        CancellationToken cancellationToken)
    {
        var info = new EpisodeInfo
        {
            SeriesProviderIds = SeasonMappingService.ToProviderDictionary(mapping),
            ParentIndexNumber = externalSeason,
            IndexNumber = externalEpisode,
            MetadataLanguage = localEpisode.GetPreferredMetadataLanguage(),
            MetadataCountryCode = localEpisode.GetPreferredMetadataCountryCode(),
            IsAutomated = false
        };

        return provider.GetMetadata(info, cancellationToken);
    }

    private static string BuildCountCacheKey(SeasonMapping mapping, string providerName, int seasonNumber)
    {
        var idPart = string.Join(
            "|",
            mapping.ProviderIds
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"{x.Key}={x.Value}"));

        return $"{providerName}|{idPart}|{seasonNumber}";
    }

    private sealed record ResolvedEpisode(
        MetadataResult<Episode> Result,
        int ExternalSeason,
        int ExternalEpisode);
}
