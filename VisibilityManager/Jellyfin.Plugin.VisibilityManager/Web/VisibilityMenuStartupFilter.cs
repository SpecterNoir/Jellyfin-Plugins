using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.VisibilityManager.Web;

/// <summary>
/// Adds the Visibility Manager web middleware to Jellyfin's request pipeline.
/// </summary>
public sealed class VisibilityMenuStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<VisibilityMenuMiddleware>();
            next(app);
        };
}
