using System.Globalization;
using Jellyfin.Plugin.LibraryCleanup.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LibraryCleanup;

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

    public override string Name => "Library Cleanup";

    public override Guid Id => Guid.Parse("d3f1a24e-8d86-4a28-9c5b-6a4e9024f5b1");

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
                MenuIcon = "cleaning_services",
                EmbeddedResourcePath = resourcePrefix + "configPage.html"
            },
            new PluginPageInfo
            {
                Name = "LibraryCleanup_admin.js",
                EmbeddedResourcePath = resourcePrefix + "admin.js"
            }
        ];
    }
}
