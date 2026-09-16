using System.Text;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeasonIdentifier.Web;

/// <summary>
/// Intercepts only the Jellyfin Web bundle that contains canIdentify and adds Season support in-memory.
/// No Jellyfin Web file is modified on disk.
/// </summary>
public sealed class SeasonIdentifyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SeasonIdentifyWebState _state;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<SeasonIdentifyMiddleware> _logger;

    public SeasonIdentifyMiddleware(
        RequestDelegate next,
        SeasonIdentifyWebState state,
        IApplicationPaths applicationPaths,
        ILogger<SeasonIdentifyMiddleware> logger)
    {
        _next = next;
        _state = state;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        _state.Discover(_applicationPaths, _logger);

        var target = _state.TargetRelativePath;
        var requestPath = context.Request.Path.Value ?? string.Empty;
        var relativePath = GetRelativeWebPath(requestPath);

        if (target is null
            || relativePath is null
            || !string.Equals(relativePath, target, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var originalBody = context.Response.Body;
        var originalAcceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
        using var buffer = new MemoryStream();

        // Ask Jellyfin for an uncompressed body so the JavaScript can be transformed safely.
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
            string contents;
            using (var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            {
                contents = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            var patched = SeasonIdentifyTransformation.Patch(contents);
            if (string.Equals(contents, patched, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Season Identifier intercepted {WebBundle}, but the Identify expression was not patched.",
                    target);
            }
            else
            {
                _logger.LogInformation(
                    "Season Identifier patched native Identify support for Season items in {WebBundle}.",
                    target);
            }

            var bytes = Encoding.UTF8.GetBytes(patched);

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

    private static string? GetRelativeWebPath(string requestPath)
    {
        var webIndex = requestPath.IndexOf("/web/", StringComparison.OrdinalIgnoreCase);
        if (webIndex < 0)
        {
            return null;
        }

        return requestPath[(webIndex + 5)..].TrimStart('/');
    }
}
