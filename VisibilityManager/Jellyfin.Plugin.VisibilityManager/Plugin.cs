using System.Globalization;
using Jellyfin.Plugin.VisibilityManager.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.VisibilityManager;

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

    public override string Name => "Visibility Manager";

    public override Guid Id => Guid.Parse("b731d013-e6e6-4c1c-949c-244c76719434");

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
                MenuIcon = "visibility_off",
                EmbeddedResourcePath = resourcePrefix + "configPage.html"
            },
            new PluginPageInfo
            {
                Name = "VisibilityManager_admin.js",
                EmbeddedResourcePath = resourcePrefix + "admin.js"
            }
        ];
    }
}
