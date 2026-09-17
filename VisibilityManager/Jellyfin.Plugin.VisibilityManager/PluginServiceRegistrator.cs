using Jellyfin.Plugin.VisibilityManager.Services;
using Jellyfin.Plugin.VisibilityManager.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.VisibilityManager;

/// <summary>
/// Registers Visibility Manager services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        _ = applicationHost;
        serviceCollection.AddSingleton<VisibilityPolicyService>();
        serviceCollection.AddTransient<IStartupFilter, VisibilityMenuStartupFilter>();
    }
}
