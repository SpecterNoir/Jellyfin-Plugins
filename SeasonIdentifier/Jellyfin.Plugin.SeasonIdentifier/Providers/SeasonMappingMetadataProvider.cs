using Jellyfin.Plugin.SeasonIdentifier.Services;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.SeasonIdentifier.Providers;

/// <summary>
/// Applies external title metadata to a mapped local season after Jellyfin's normal providers run.
/// </summary>
public class SeasonMappingMetadataProvider : ICustomMetadataProvider<Season>
{
    private readonly SeasonMappingService _mappings;
    private readonly IProviderManager _providerManager;
    private readonly ILibraryManager _libraryManager;

    public SeasonMappingMetadataProvider(
        SeasonMappingService mappings,
        IProviderManager providerManager,
        ILibraryManager libraryManager)
    {
        _mappings = mappings;
        _providerManager = providerManager;
        _libraryManager = libraryManager;
    }

    public string Name => "Season Identifier";

    public async Task<ItemUpdateType> FetchAsync(
        Season item,
        MetadataRefreshOptions options,
        CancellationToken cancellationToken)
    {
        var mapping = _mappings.Get(item.Id);
        if (mapping is null || !string.Equals(mapping.Mode, "Title", StringComparison.OrdinalIgnoreCase))
        {
            return ItemUpdateType.None;
        }

        var series = item.Series;
        if (series is null)
        {
            return ItemUpdateType.None;
        }

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
            if (!result.HasMetadata)
            {
                continue;
            }

            MetadataCopy.ApplySeriesToSeason(result.Item, item);

            if (result.People is { Count: > 0 })
            {
                await _libraryManager.UpdatePeopleAsync(item, result.People, cancellationToken).ConfigureAwait(false);
            }

            return ItemUpdateType.MetadataDownload;
        }

        return ItemUpdateType.None;
    }
}
