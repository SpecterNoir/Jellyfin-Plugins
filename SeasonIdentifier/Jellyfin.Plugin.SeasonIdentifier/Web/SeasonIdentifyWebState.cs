using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeasonIdentifier.Web;

/// <summary>
/// Locates the Jellyfin Web bundle that owns the native Identify eligibility check.
/// </summary>
public sealed class SeasonIdentifyWebState
{
    private readonly object _sync = new();
    private bool _scanned;

    public string? TargetRelativePath { get; private set; }

    public bool Discover(IApplicationPaths applicationPaths, ILogger logger)
    {
        lock (_sync)
        {
            if (_scanned)
            {
                return TargetRelativePath is not null;
            }

            _scanned = true;

            if (!Directory.Exists(applicationPaths.WebPath))
            {
                logger.LogWarning(
                    "Season Identifier could not locate the Jellyfin Web directory: {WebPath}",
                    applicationPaths.WebPath);
                return false;
            }

            foreach (var file in Directory.EnumerateFiles(applicationPaths.WebPath, "*.js", SearchOption.AllDirectories))
            {
                string contents;
                try
                {
                    contents = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (!SeasonIdentifyTransformation.CanPatch(contents))
                {
                    continue;
                }

                TargetRelativePath = Path
                    .GetRelativePath(applicationPaths.WebPath, file)
                    .Replace('\\', '/');

                logger.LogInformation(
                    "Season Identifier located Jellyfin Web's Identify bundle: {WebBundle}",
                    TargetRelativePath);
                return true;
            }

            logger.LogWarning(
                "Season Identifier could not locate Jellyfin Web's Identify bundle. " +
                "The installed Jellyfin Web build may use a layout this plugin does not recognize.");
            return false;
        }
    }
}
