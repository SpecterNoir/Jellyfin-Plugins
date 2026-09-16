using Jellyfin.Plugin.SeasonIdentifier.Configuration;

namespace Jellyfin.Plugin.SeasonIdentifier.Services;

/// <summary>
/// Stores and resolves season mappings.
/// </summary>
public class SeasonMappingService
{
    private readonly object _sync = new();

    public SeasonMapping? Get(Guid localSeasonId)
    {
        lock (_sync)
        {
            return Clone(GetConfiguration().SeasonMappings.FirstOrDefault(x => x.LocalSeasonId == localSeasonId));
        }
    }

    public IReadOnlyList<SeasonMapping> GetAll()
    {
        lock (_sync)
        {
            return GetConfiguration().SeasonMappings
                .OrderBy(x => x.LocalSeriesName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.LocalSeasonNumber)
                .Select(Clone)
                .Where(x => x is not null)
                .Cast<SeasonMapping>()
                .ToArray();
        }
    }

    public void Upsert(SeasonMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        lock (_sync)
        {
            var plugin = Plugin.Instance ?? throw new InvalidOperationException("Season Identifier is not initialized.");
            var config = GetConfiguration();
            var existingIndex = config.SeasonMappings.FindIndex(x => x.LocalSeasonId == mapping.LocalSeasonId);
            var stored = Clone(mapping) ?? throw new InvalidOperationException("Unable to clone mapping.");

            if (existingIndex >= 0)
            {
                config.SeasonMappings[existingIndex] = stored;
            }
            else
            {
                config.SeasonMappings.Add(stored);
            }

            plugin.SaveConfiguration();
        }
    }

    public bool Remove(Guid localSeasonId)
    {
        lock (_sync)
        {
            var plugin = Plugin.Instance ?? throw new InvalidOperationException("Season Identifier is not initialized.");
            var config = GetConfiguration();
            var removed = config.SeasonMappings.RemoveAll(x => x.LocalSeasonId == localSeasonId) > 0;

            if (removed)
            {
                plugin.SaveConfiguration();
            }

            return removed;
        }
    }

    public static Dictionary<string, string> ToProviderDictionary(SeasonMapping mapping)
    {
        return mapping.ProviderIds
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static PluginConfiguration GetConfiguration()
        => Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Season Identifier is not initialized.");

    private static SeasonMapping? Clone(SeasonMapping? source)
    {
        if (source is null)
        {
            return null;
        }

        return new SeasonMapping
        {
            LocalSeasonId = source.LocalSeasonId,
            LocalSeriesName = source.LocalSeriesName,
            LocalSeasonName = source.LocalSeasonName,
            LocalSeasonNumber = source.LocalSeasonNumber,
            ExternalTitleName = source.ExternalTitleName,
            ExternalYear = source.ExternalYear,
            SearchProviderName = source.SearchProviderName,
            ImageUrl = source.ImageUrl,
            Mode = source.Mode,
            ExternalSeasonNumber = source.ExternalSeasonNumber,
            ProviderIds = source.ProviderIds
                .Select(x => new ProviderIdEntry { Key = x.Key, Value = x.Value })
                .ToList()
        };
    }
}
