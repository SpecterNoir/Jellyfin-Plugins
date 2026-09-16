using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SeasonIdentifier.Configuration;

/// <summary>
/// Season Identifier configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets season mappings.
    /// </summary>
    public List<SeasonMapping> SeasonMappings { get; set; } = new();
}

/// <summary>
/// Maps a local Jellyfin season to an external TV title.
/// </summary>
public class SeasonMapping
{
    public Guid LocalSeasonId { get; set; }

    public string LocalSeriesName { get; set; } = string.Empty;

    public string LocalSeasonName { get; set; } = string.Empty;

    public int? LocalSeasonNumber { get; set; }

    public string ExternalTitleName { get; set; } = string.Empty;

    public int? ExternalYear { get; set; }

    public string SearchProviderName { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public string Mode { get; set; } = "Title";

    public int? ExternalSeasonNumber { get; set; }

    public List<ProviderIdEntry> ProviderIds { get; set; } = new();
}

/// <summary>
/// XML-serializer-friendly provider id pair.
/// </summary>
public class ProviderIdEntry
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
