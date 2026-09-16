using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SeasonIdentifier.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.SeasonIdentifier.Services;

/// <summary>
/// Registers the tiny jellyfin-web transform that exposes Jellyfin's existing Identify command on Season items.
/// </summary>
public sealed class SeasonIdentifyStartupTask : IScheduledTask
{
    private static readonly Guid TransformationId = Guid.Parse("2a38af9a-c93c-49fb-8dc2-e8df63183237");
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<SeasonIdentifyStartupTask> _logger;

    public SeasonIdentifyStartupTask(
        IApplicationPaths applicationPaths,
        ILogger<SeasonIdentifyStartupTask> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    public string Name => "Enable Season Identify";

    public string Key => "SeasonIdentifierEnableNativeIdentify";

    public string Description => "Enables Jellyfin's native Identify command for Season items.";

    public string Category => "Season Identifier";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);

        var fileTransformationAssembly = AssemblyLoadContext.All
            .SelectMany(x => x.Assemblies)
            .FirstOrDefault(x => string.Equals(
                x.GetName().Name,
                "Jellyfin.Plugin.FileTransformation",
                StringComparison.Ordinal));

        if (fileTransformationAssembly is null)
        {
            _logger.LogWarning(
                "Season Identifier could not enable native Season Identify because File Transformation is not loaded.");
            progress.Report(100);
            return Task.CompletedTask;
        }

        var interfaceType = fileTransformationAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
        var registerMethod = interfaceType?.GetMethod(
            "RegisterTransformation",
            BindingFlags.Public | BindingFlags.Static);

        if (registerMethod is null)
        {
            _logger.LogWarning("Season Identifier could not find File Transformation's registration API.");
            progress.Report(100);
            return Task.CompletedTask;
        }

        if (!Directory.Exists(_applicationPaths.WebPath))
        {
            _logger.LogWarning("Jellyfin web path does not exist: {WebPath}", _applicationPaths.WebPath);
            progress.Report(100);
            return Task.CompletedTask;
        }

        foreach (var file in Directory.EnumerateFiles(_applicationPaths.WebPath, "*.js", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            var relativePath = Path.GetRelativePath(_applicationPaths.WebPath, file).Replace('\\', '/');
            var filePattern = "(^|.*/)" + Regex.Escape(relativePath) + "$";
            var callbackAssembly = typeof(SeasonIdentifyTransformation).Assembly.FullName
                ?? throw new InvalidOperationException("Season Identifier assembly name is unavailable.");

            var payload = new JObject
            {
                ["id"] = TransformationId,
                ["fileNamePattern"] = filePattern,
                ["callbackAssembly"] = callbackAssembly,
                ["callbackClass"] = typeof(SeasonIdentifyTransformation).FullName,
                ["callbackMethod"] = nameof(SeasonIdentifyTransformation.Patch)
            };

            registerMethod.Invoke(null, [payload]);

            _logger.LogInformation(
                "Season Identifier enabled native Identify for Season items in Jellyfin Web ({WebFile}).",
                relativePath);
            progress.Report(100);
            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "Season Identifier could not locate Jellyfin Web's canIdentify bundle. Native Season Identify was not enabled.");
        progress.Report(100);
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
    }
}
