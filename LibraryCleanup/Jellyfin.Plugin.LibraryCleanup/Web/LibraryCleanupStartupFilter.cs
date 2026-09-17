using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.LibraryCleanup.Web;

/// <summary>
/// Installs the in-memory Jellyfin Web hook for Scan & Clean.
/// </summary>
public sealed class LibraryCleanupStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<LibraryCleanupWebMiddleware>();
            next(app);
        };
    }
}
