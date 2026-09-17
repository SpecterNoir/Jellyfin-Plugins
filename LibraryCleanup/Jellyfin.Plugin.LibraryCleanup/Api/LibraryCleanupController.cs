using System.IO;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LibraryCleanup.Api;

/// <summary>
/// Diagnostics and conservative repair API for common TV library inconsistencies.
/// </summary>
[ApiController]
[Route("LibraryCleanup/api")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class LibraryCleanupController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;

    public LibraryCleanupController(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
    }

    [HttpGet("scan")]
    public ActionResult<CleanupScanDto> Scan()
    {
        var episodes = Query(BaseItemKind.Episode).OfType<Episode>().ToArray();
        var seasons = Query(BaseItemKind.Season).OfType<Season>().ToArray();
        var issues = new List<CleanupIssueDto>();

        AddDuplicatePathIssues(episodes, issues);
        AddDuplicateEpisodeNumberIssues(episodes, issues);
        AddDuplicateSeasonNumberIssues(seasons, issues);
        AddEpisodeStateIssues(episodes, issues);
        AddSeasonStateIssues(seasons, issues);

        var ordered = issues
            .OrderByDescending(issue => SeverityRank(issue.Severity))
            .ThenBy(issue => issue.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.SeasonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new CleanupScanDto(
            episodes.Length,
            seasons.Length,
            ordered.Length,
            ordered.Count(issue => issue.SafeRepairAvailable),
            ordered));
    }

    [HttpPost("items/{itemId:guid}/repair-links")]
    public async Task<ActionResult<RepairResultDto>> RepairLinks(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        if (_libraryManager.GetItemById(itemId) is not Episode episode)
        {
            return NotFound();
        }

        var changed = await RepairEpisodeLinksAsync(episode, cancellationToken).ConfigureAwait(false);
        return Ok(new RepairResultDto(changed ? 1 : 0, changed ? "Episode hierarchy links repaired." : "No safe hierarchy-link change was needed."));
    }

    [HttpPost("repair-safe")]
    public async Task<ActionResult<RepairResultDto>> RepairAllSafe(
        CancellationToken cancellationToken = default)
    {
        var episodes = Query(BaseItemKind.Episode).OfType<Episode>().ToArray();
        var repaired = 0;

        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await RepairEpisodeLinksAsync(episode, cancellationToken).ConfigureAwait(false))
            {
                repaired++;
            }
        }

        return Ok(new RepairResultDto(
            repaired,
            repaired == 1
                ? "Repaired hierarchy links for 1 episode."
                : $"Repaired hierarchy links for {repaired} episodes."));
    }

    [HttpPost("items/{itemId:guid}/refresh")]
    public ActionResult RefreshItem([FromRoute] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        QueueFullRefresh(item);
        return NoContent();
    }

    private BaseItem[] Query(BaseItemKind kind)
        => _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = [kind],
            Limit = 100000
        }).Items;

    private static void AddDuplicatePathIssues(IEnumerable<Episode> episodes, ICollection<CleanupIssueDto> issues)
    {
        foreach (var group in episodes
                     .Where(episode => !string.IsNullOrWhiteSpace(episode.Path))
                     .GroupBy(episode => NormalizePath(episode.Path!), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var entries = group.ToArray();
            var first = entries[0];
            var context = GetContext(first);
            issues.Add(new CleanupIssueDto(
                $"duplicate-path:{group.Key}",
                "DuplicatePath",
                "Critical",
                "Multiple Jellyfin episode records point to the same file",
                $"Jellyfin currently has {entries.Length} episode records for one physical path. The plugin will not delete either record automatically in this build.",
                first.Id,
                first.Name ?? "(Unnamed episode)",
                context.SeriesName,
                context.SeasonName,
                first.Path ?? string.Empty,
                false,
                true,
                entries.Select(entry => entry.Id).ToArray()));
        }
    }

    private static void AddDuplicateEpisodeNumberIssues(IEnumerable<Episode> episodes, ICollection<CleanupIssueDto> issues)
    {
        var candidates = episodes.Where(episode =>
            !episode.IsMissingEpisode
            && episode.IndexNumber.HasValue
            && episode.ParentIndexNumber.HasValue);

        foreach (var group in candidates.GroupBy(episode =>
                 {
                     var context = GetContext(episode);
                     return new EpisodeSlotKey(context.SeriesId, episode.ParentIndexNumber!.Value, episode.IndexNumber!.Value);
                 }))
        {
            var entries = group.ToArray();
            if (entries.Length < 2)
            {
                continue;
            }

            var distinctPaths = entries
                .Select(entry => NormalizePath(entry.Path))
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (distinctPaths.Length <= 1)
            {
                continue;
            }

            var first = entries[0];
            var context = GetContext(first);
            issues.Add(new CleanupIssueDto(
                $"duplicate-episode-slot:{group.Key.SeriesId}:{group.Key.SeasonNumber}:{group.Key.EpisodeNumber}",
                "DuplicateEpisodeNumber",
                "Warning",
                $"Episode number S{group.Key.SeasonNumber:00}E{group.Key.EpisodeNumber:00} appears more than once",
                $"{entries.Length} different media paths are assigned to the same season/episode number. This may be intentional, but it often explains duplicate episodes in Jellyfin.",
                first.Id,
                first.Name ?? "(Unnamed episode)",
                context.SeriesName,
                context.SeasonName,
                first.Path ?? string.Empty,
                false,
                true,
                entries.Select(entry => entry.Id).ToArray()));
        }
    }

    private static void AddDuplicateSeasonNumberIssues(IEnumerable<Season> seasons, ICollection<CleanupIssueDto> issues)
    {
        var candidates = seasons
            .Select(season => (Season: season, Series: season.FindParent<Series>()))
            .Where(pair => pair.Series is not null && pair.Season.IndexNumber.HasValue);

        foreach (var group in candidates.GroupBy(pair => new SeasonSlotKey(pair.Series!.Id, pair.Season.IndexNumber!.Value)))
        {
            var entries = group.Select(pair => pair.Season).ToArray();
            if (entries.Length < 2)
            {
                continue;
            }

            var first = entries[0];
            var series = group.First().Series!;
            issues.Add(new CleanupIssueDto(
                $"duplicate-season-slot:{group.Key.SeriesId}:{group.Key.SeasonNumber}",
                "DuplicateSeasonNumber",
                "Critical",
                $"Season {group.Key.SeasonNumber} appears more than once",
                $"Jellyfin has {entries.Length} separate season records with the same season number under {series.Name}. This is a likely phantom or duplicate season condition.",
                first.Id,
                first.Name ?? $"Season {group.Key.SeasonNumber}",
                series.Name ?? string.Empty,
                first.Name ?? string.Empty,
                first.Path ?? string.Empty,
                false,
                true,
                entries.Select(entry => entry.Id).ToArray()));
        }
    }

    private static void AddEpisodeStateIssues(IEnumerable<Episode> episodes, ICollection<CleanupIssueDto> issues)
    {
        foreach (var episode in episodes)
        {
            var context = GetContext(episode);
            if (TryGetHierarchyMismatch(episode, out var mismatchDetail))
            {
                issues.Add(new CleanupIssueDto(
                    $"stale-links:{episode.Id}",
                    "StaleHierarchyLinks",
                    "Critical",
                    "Episode is linked to the wrong cached season or series",
                    mismatchDetail,
                    episode.Id,
                    episode.Name ?? "(Unnamed episode)",
                    context.SeriesName,
                    context.SeasonName,
                    episode.Path ?? string.Empty,
                    true,
                    true,
                    [episode.Id]));
            }

            if (episode.IsMissingEpisode)
            {
                issues.Add(new CleanupIssueDto(
                    $"virtual-episode:{episode.Id}",
                    "VirtualEpisode",
                    "Warning",
                    "Virtual / missing episode record",
                    "This is a Jellyfin virtual episode with no local media file. It may be intentional, or it may be a phantom entry left by metadata/scanning.",
                    episode.Id,
                    episode.Name ?? "(Unnamed episode)",
                    context.SeriesName,
                    context.SeasonName,
                    episode.Path ?? string.Empty,
                    false,
                    true,
                    [episode.Id]));
                continue;
            }

            if (!episode.IndexNumber.HasValue || !episode.ParentIndexNumber.HasValue)
            {
                issues.Add(new CleanupIssueDto(
                    $"missing-number:{episode.Id}",
                    "MissingEpisodeNumber",
                    "Warning",
                    "Episode numbering is incomplete",
                    "Jellyfin is missing the season number, episode number, or both for this episode.",
                    episode.Id,
                    episode.Name ?? "(Unnamed episode)",
                    context.SeriesName,
                    context.SeasonName,
                    episode.Path ?? string.Empty,
                    false,
                    true,
                    [episode.Id]));
            }

            if (!string.IsNullOrWhiteSpace(episode.Path) && !File.Exists(episode.Path))
            {
                issues.Add(new CleanupIssueDto(
                    $"missing-file:{episode.Id}",
                    "MissingFile",
                    "Critical",
                    "Jellyfin episode record points to a file that is not present",
                    "The database record has a media path, but the server cannot currently see a file at that path.",
                    episode.Id,
                    episode.Name ?? "(Unnamed episode)",
                    context.SeriesName,
                    context.SeasonName,
                    episode.Path,
                    false,
                    true,
                    [episode.Id]));
            }
        }
    }

    private static void AddSeasonStateIssues(IEnumerable<Season> seasons, ICollection<CleanupIssueDto> issues)
    {
        foreach (var season in seasons.Where(season => season.IsVirtualItem))
        {
            var series = season.FindParent<Series>();
            issues.Add(new CleanupIssueDto(
                $"virtual-season:{season.Id}",
                "VirtualSeason",
                "Warning",
                "Virtual / phantom season record",
                "This season is marked virtual by Jellyfin. Virtual seasons can be legitimate, but they are also a common source of made-up or duplicate season entries.",
                season.Id,
                season.Name ?? "(Unnamed season)",
                series?.Name ?? string.Empty,
                season.Name ?? string.Empty,
                season.Path ?? string.Empty,
                false,
                true,
                [season.Id]));
        }
    }

    private async Task<bool> RepairEpisodeLinksAsync(Episode episode, CancellationToken cancellationToken)
    {
        var physicalSeason = episode.FindParent<Season>();
        if (physicalSeason is null)
        {
            return false;
        }

        var physicalSeries = physicalSeason.FindParent<Series>() ?? episode.FindParent<Series>();
        var changed = false;

        if (episode.SeasonId != physicalSeason.Id)
        {
            episode.SeasonId = physicalSeason.Id;
            changed = true;
        }

        if (physicalSeries is not null && episode.SeriesId != physicalSeries.Id)
        {
            episode.SeriesId = physicalSeries.Id;
            changed = true;
        }

        if (physicalSeason.IndexNumber.HasValue && episode.ParentIndexNumber != physicalSeason.IndexNumber)
        {
            episode.ParentIndexNumber = physicalSeason.IndexNumber;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        episode.SeasonName = physicalSeason.Name;
        if (physicalSeries is not null)
        {
            episode.SeriesName = physicalSeries.Name;
        }

        await episode.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        QueueDefaultRefresh(episode);
        return true;
    }

    private static bool TryGetHierarchyMismatch(Episode episode, out string detail)
    {
        detail = string.Empty;
        var physicalSeason = episode.FindParent<Season>();
        if (physicalSeason is null)
        {
            return false;
        }

        var physicalSeries = physicalSeason.FindParent<Series>() ?? episode.FindParent<Series>();
        var mismatches = new List<string>();

        if (episode.SeasonId != physicalSeason.Id)
        {
            mismatches.Add("cached SeasonId does not match the physical parent season");
        }

        if (physicalSeries is not null && episode.SeriesId != physicalSeries.Id)
        {
            mismatches.Add("cached SeriesId does not match the physical parent series");
        }

        if (physicalSeason.IndexNumber.HasValue && episode.ParentIndexNumber != physicalSeason.IndexNumber)
        {
            mismatches.Add($"cached season number is {episode.ParentIndexNumber?.ToString() ?? "unknown"} but the physical season is {physicalSeason.IndexNumber}");
        }

        if (mismatches.Count == 0)
        {
            return false;
        }

        detail = "Jellyfin's cached hierarchy is stale: " + string.Join("; ", mismatches) + ". Safe Repair will realign these links to the physical folder hierarchy and queue a normal metadata refresh.";
        return true;
    }

    private void QueueDefaultRefresh(BaseItem item)
    {
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.Default,
            ImageRefreshMode = MetadataRefreshMode.Default,
            ForceSave = true,
            IsAutomated = false
        };

        _providerManager.QueueRefresh(item.Id, options, RefreshPriority.High);
    }

    private void QueueFullRefresh(BaseItem item)
    {
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = MetadataRefreshMode.FullRefresh,
            ReplaceAllMetadata = true,
            ReplaceAllImages = true,
            RemoveOldMetadata = true,
            ForceSave = true,
            IsAutomated = false
        };

        _providerManager.QueueRefresh(item.Id, options, RefreshPriority.High);
    }

    private static ItemContext GetContext(Episode episode)
    {
        var physicalSeason = episode.FindParent<Season>();
        var physicalSeries = physicalSeason?.FindParent<Series>() ?? episode.FindParent<Series>();
        var season = physicalSeason ?? episode.Season;
        var series = physicalSeries ?? episode.Series;

        return new ItemContext(
            series?.Id ?? episode.SeriesId,
            series?.Name ?? episode.SeriesName ?? string.Empty,
            season?.Name ?? episode.SeasonName ?? string.Empty);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static int SeverityRank(string severity)
        => severity switch
        {
            "Critical" => 3,
            "Warning" => 2,
            _ => 1
        };

    private readonly record struct ItemContext(Guid SeriesId, string SeriesName, string SeasonName);
    private readonly record struct EpisodeSlotKey(Guid SeriesId, int SeasonNumber, int EpisodeNumber);
    private readonly record struct SeasonSlotKey(Guid SeriesId, int SeasonNumber);
}

public sealed record CleanupScanDto(
    int EpisodeCount,
    int SeasonCount,
    int IssueCount,
    int SafeRepairCount,
    IReadOnlyList<CleanupIssueDto> Issues);

public sealed record CleanupIssueDto(
    string Key,
    string Kind,
    string Severity,
    string Title,
    string Detail,
    Guid ItemId,
    string ItemName,
    string SeriesName,
    string SeasonName,
    string Path,
    bool SafeRepairAvailable,
    bool RefreshAvailable,
    IReadOnlyList<Guid> RelatedItemIds);

public sealed record RepairResultDto(int RepairedCount, string Message);
