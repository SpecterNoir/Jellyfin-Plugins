using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LibraryCleanup.Web;

/// <summary>
/// Injects the Library Cleanup client hook into Jellyfin Web in-memory.
/// Jellyfin's installed web files are never modified on disk.
/// </summary>
public sealed class LibraryCleanupWebMiddleware
{
    private const string ScriptTag = "<script src=\"../LibraryCleanup/client.js\"></script>";

    private readonly RequestDelegate _next;
    private readonly ILogger<LibraryCleanupWebMiddleware> _logger;

    public LibraryCleanupWebMiddleware(RequestDelegate next, ILogger<LibraryCleanupWebMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!IsWebIndex(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var originalBody = context.Response.Body;
        var originalAcceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
        using var buffer = new MemoryStream();

        context.Request.Headers.Remove("Accept-Encoding");
        context.Response.Body = buffer;

        try
        {
            await _next(context).ConfigureAwait(false);

            if (context.Response.StatusCode != StatusCodes.Status200OK)
            {
                buffer.Position = 0;
                context.Response.Body = originalBody;
                await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
                return;
            }

            buffer.Position = 0;
            string html;
            using (var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            {
                html = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            if (!html.Contains("LibraryCleanup/client.js", StringComparison.OrdinalIgnoreCase))
            {
                var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                if (bodyEnd >= 0)
                {
                    html = html.Insert(bodyEnd, ScriptTag);
                    _logger.LogInformation("Library Cleanup injected Scan & Clean into Jellyfin Web.");
                }
                else
                {
                    _logger.LogWarning("Library Cleanup could not find </body> in Jellyfin Web index.");
                }
            }

            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.Body = originalBody;
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("ETag");
            context.Response.Headers.Remove("Last-Modified");
            context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
            if (string.IsNullOrEmpty(originalAcceptEncoding))
            {
                context.Request.Headers.Remove("Accept-Encoding");
            }
            else
            {
                context.Request.Headers.AcceptEncoding = originalAcceptEncoding;
            }
        }
    }

    private static bool IsWebIndex(string path)
        => path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/web", StringComparison.OrdinalIgnoreCase);
}
