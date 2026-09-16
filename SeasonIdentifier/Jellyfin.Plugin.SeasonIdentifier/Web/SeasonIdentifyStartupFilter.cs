using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeasonIdentifier.Web;

/// <summary>
/// Installs the in-memory Jellyfin Web patch used to expose native Identify for Season items.
/// </summary>
public sealed class SeasonIdentifyStartupFilter : IStartupFilter
{
    private readonly SeasonIdentifyWebState _state;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<SeasonIdentifyStartupFilter> _logger;

    public SeasonIdentifyStartupFilter(
        SeasonIdentifyWebState state,
        IApplicationPaths applicationPaths,
        ILogger<SeasonIdentifyStartupFilter> logger)
    {
        _state = state;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            _state.Discover(_applicationPaths, _logger);
            app.UseMiddleware<SeasonIdentifyMiddleware>();
            next(app);
        };
    }
}
