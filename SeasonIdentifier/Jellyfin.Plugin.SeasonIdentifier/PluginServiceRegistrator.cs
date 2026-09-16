using Jellyfin.Plugin.SeasonIdentifier.Services;
using Jellyfin.Plugin.SeasonIdentifier.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SeasonIdentifier;

/// <summary>
/// Registers Season Identifier services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        _ = applicationHost;

        serviceCollection.AddSingleton<SeasonMappingService>();
        serviceCollection.AddSingleton<SeasonIdentifyWebState>();
        serviceCollection.AddTransient<IStartupFilter, SeasonIdentifyStartupFilter>();
    }
}
