using System.Globalization;
using Jellyfin.Plugin.SeasonIdentifier.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SeasonIdentifier;

/// <summary>
/// Jellyfin plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Season Identifier";

    public override Guid Id => Guid.Parse("5d908510-bd5f-48b9-9f91-519877f4de31");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var resourcePrefix = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Configuration.",
            GetType().Namespace);

        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = Name,
                EnableInMainMenu = false,
                MenuIcon = "video_library",
                EmbeddedResourcePath = resourcePrefix + "configPage.html"
            },
            new PluginPageInfo
            {
                Name = "SeasonIdentifier_admin.js",
                EmbeddedResourcePath = resourcePrefix + "admin.js"
            }
        ];
    }
}
