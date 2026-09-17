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
/// Runs a focused cleanup pass for one Series, Season, or Episode.
/// </summary>
[ApiController]
[Route("LibraryCleanup/api")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class TargetedCleanupController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;

    public TargetedCleanupController(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
    }

    [HttpPost("items/{itemId:guid}/scan-clean")]
    public async Task<ActionResult<TargetedCleanupResultDto>> ScanAndClean(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is not Series && item is not Season && item is not Episode)
        {
            return BadRequest(new { message = "Scan & Clean currently supports Series, Seasons, and Episodes." });
        }

        var episodes = GetEpisodes(item);
        var seasons = GetSeasons(item);
        var issuesFound = CountIssues(episodes, seasons);
        var repaired = 0;

        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await RepairHierarchyAsync(episode, cancellationToken).ConfigureAwait(false))
            {
                repaired++;
                QueueDefaultRefresh(episode);
            }
        }

        // Always queue a normal Jellyfin refresh for the selected scope after the cleanup pass.
        // This is intentionally less destructive than replacing all metadata/images.
        QueueDefaultRefresh(item);

        var remaining = CountIssues(episodes, seasons);
        return Ok(new TargetedCleanupResultDto(
            item.Id,
            item.Name ?? item.GetBaseItemKind().ToString(),
            item.GetBaseItemKind().ToString(),
            episodes.Length,
            seasons.Length,
            issuesFound,
            repaired,
            remaining,
            true));
    }

    private static Episode[] GetEpisodes(BaseItem item)
        => item switch
        {
            Episode episode => [episode],
            Folder folder => folder.GetRecursiveChildren(child => child is Episode).OfType<Episode>().ToArray(),
            _ => []
        };

    private static Season[] GetSeasons(BaseItem item)
        => item switch
        {
            Season season => [season],
            Series series => series.GetRecursiveChildren(child => child is Season).OfType<Season>().ToArray(),
            _ => []
        };

    private static int CountIssues(IEnumerable<Episode> episodes, IEnumerable<Season> seasons)
    {
        var episodeArray = episodes.ToArray();
        var seasonArray = seasons.ToArray();
        var count = 0;

        count += episodeArray
            .Where(episode => !string.IsNullOrWhiteSpace(episode.Path))
            .GroupBy(episode => NormalizePath(episode.Path!), StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);

        count += episodeArray
            .Where(episode => !episode.IsMissingEpisode && episode.IndexNumber.HasValue && episode.ParentIndexNumber.HasValue)
            .GroupBy(episode => new
            {
                SeriesId = PhysicalSeriesId(episode),
                Season = episode.ParentIndexNumber!.Value,
                Episode = episode.IndexNumber!.Value
            })
            .Count(group => group
                .Select(episode => NormalizePath(episode.Path))
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1);

        count += seasonArray
            .Where(season => season.IndexNumber.HasValue)
            .Select(season => new { Season = season, Series = season.FindParent<Series>() })
            .Where(pair => pair.Series is not null)
            .GroupBy(pair => new { pair.Series!.Id, Number = pair.Season.IndexNumber!.Value })
            .Count(group => group.Count() > 1);

        count += seasonArray.Count(season => season.IsVirtualItem);

        foreach (var episode in episodeArray)
        {
            if (HasHierarchyMismatch(episode))
            {
                count++;
            }

            if (episode.IsMissingEpisode)
            {
                count++;
                continue;
            }

            if (!episode.IndexNumber.HasValue || !episode.ParentIndexNumber.HasValue)
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(episode.Path) && !System.IO.File.Exists(episode.Path))
            {
                count++;
            }
        }

        return count;
    }

    private async Task<bool> RepairHierarchyAsync(Episode episode, CancellationToken cancellationToken)
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
        return true;
    }

    private static bool HasHierarchyMismatch(Episode episode)
    {
        var physicalSeason = episode.FindParent<Season>();
        if (physicalSeason is null)
        {
            return false;
        }

        var physicalSeries = physicalSeason.FindParent<Series>() ?? episode.FindParent<Series>();
        return episode.SeasonId != physicalSeason.Id
            || (physicalSeries is not null && episode.SeriesId != physicalSeries.Id)
            || (physicalSeason.IndexNumber.HasValue && episode.ParentIndexNumber != physicalSeason.IndexNumber);
    }

    private static Guid PhysicalSeriesId(Episode episode)
    {
        var season = episode.FindParent<Season>();
        return season?.FindParent<Series>()?.Id
            ?? episode.FindParent<Series>()?.Id
            ?? episode.SeriesId;
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

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public sealed record TargetedCleanupResultDto(
    Guid ItemId,
    string ItemName,
    string ItemType,
    int EpisodesScanned,
    int SeasonsScanned,
    int IssuesFound,
    int RepairedCount,
    int RemainingIssues,
    bool RefreshQueued);
