using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LibraryCleanup.Api;

/// <summary>
/// Serves the small Jellyfin Web client hook used for Scan & Clean.
/// </summary>
[ApiController]
[Route("LibraryCleanup")]
public sealed class ClientAssetController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("client.js")]
    public IActionResult GetClientScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.LibraryCleanup.Web.client.js");
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, "application/javascript; charset=utf-8");
    }
}
