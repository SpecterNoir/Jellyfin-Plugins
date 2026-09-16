using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SeasonIdentifier.Configuration;
using Jellyfin.Plugin.SeasonIdentifier.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SeasonIdentifier.Api;

/// <summary>
/// Administration API for season mappings.
/// </summary>
[ApiController]
[Route("SeasonIdentifier/api")]
[Authorize(Policy = Policies.RequiresElevation)]
public class SeasonIdentifierController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly SeasonMappingService _mappings;

    public SeasonIdentifierController(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        SeasonMappingService mappings)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _mappings = mappings;
    }

    [HttpGet("seasons")]
    public ActionResult<IEnumerable<object>> GetSeasons(
        [FromQuery] string? q = null,
        [FromQuery] int limit = 250)
    {
        var items = _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            Recursive = true,
            IsVirtualItem = false,
            IncludeItemTypes = [BaseItemKind.Season],
            Limit = 2000
        }).Items.OfType<Season>();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            items = items.Where(x =>
                (x.Series?.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Path?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return Ok(items
            .OrderBy(x => x.Series?.SortName ?? x.Series?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.IndexNumber)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(x => new
            {
                id = x.Id,
                seriesName = x.Series?.Name ?? x.SeriesName ?? "(Unknown series)",
                seasonName = x.Name,
                seasonNumber = x.IndexNumber,
                path = x.Path,
                mapped = _mappings.Get(x.Id) is not null
            }));
    }

    [HttpGet("search-titles")]
    public async Task<ActionResult<IEnumerable<object>>> SearchTitles(
        [FromQuery] string q,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Ok(Array.Empty<object>());
        }

        var query = new RemoteSearchQuery<SeriesInfo>
        {
            SearchInfo = new SeriesInfo
            {
                Name = q.Trim(),
                IsAutomated = false
            },
            IncludeDisabledProviders = false
        };

        var results = await _providerManager
            .GetRemoteSearchResults<Series, SeriesInfo>(query, cancellationToken)
            .ConfigureAwait(false);

        return Ok(results.Take(Math.Clamp(limit, 1, 50)).Select(x => new
        {
            name = x.Name,
            year = x.ProductionYear,
            imageUrl = x.ImageUrl,
            searchProviderName = x.SearchProviderName,
            providerIds = x.ProviderIds
        }));
    }

    [HttpGet("mappings")]
    public ActionResult<IEnumerable<SeasonMapping>> GetMappings()
        => Ok(_mappings.GetAll());

    [HttpPost("mappings")]
    public async Task<ActionResult<SeasonMapping>> SaveMapping(
        [FromBody] SaveSeasonMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        var season = _libraryManager.GetItemById(request.LocalSeasonId) as Season;
        if (season is null || season.IsVirtualItem)
        {
            return NotFound(new { message = "The selected local season could not be found." });
        }

        if (string.IsNullOrWhiteSpace(request.ExternalTitleName) || request.ProviderIds.Count == 0)
        {
            return BadRequest(new { message = "Choose an external title before saving." });
        }

        var mapping = new SeasonMapping
        {
            LocalSeasonId = season.Id,
            LocalSeriesName = season.Series?.Name ?? season.SeriesName ?? string.Empty,
            LocalSeasonName = season.Name ?? $"Season {season.IndexNumber}",
            LocalSeasonNumber = season.IndexNumber,
            ExternalTitleName = request.ExternalTitleName.Trim(),
            ExternalYear = request.ExternalYear,
            SearchProviderName = request.SearchProviderName?.Trim() ?? string.Empty,
            ImageUrl = request.ImageUrl,
            Mode = "Title",
            ProviderIds = request.ProviderIds
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => new ProviderIdEntry { Key = x.Key.Trim(), Value = x.Value.Trim() })
                .ToList()
        };

        _mappings.Upsert(mapping);

        if (!string.IsNullOrWhiteSpace(mapping.ImageUrl))
        {
            try
            {
                await _providerManager
                    .SaveImage(season, mapping.ImageUrl, ImageType.Primary, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Metadata mapping is still valid if poster download fails.
            }
        }

        QueueMappedRefresh(season);
        return Ok(mapping);
    }

    [HttpDelete("mappings/{seasonId:guid}")]
    public ActionResult DeleteMapping(Guid seasonId)
    {
        return _mappings.Remove(seasonId) ? NoContent() : NotFound();
    }

    [HttpPost("mappings/{seasonId:guid}/refresh")]
    public ActionResult RefreshMapping(Guid seasonId)
    {
        var season = _libraryManager.GetItemById(seasonId) as Season;
        if (season is null || _mappings.Get(seasonId) is null)
        {
            return NotFound();
        }

        QueueMappedRefresh(season);
        return NoContent();
    }

    private void QueueMappedRefresh(Season season)
    {
        _providerManager.QueueRefresh(season.Id, CreateRefreshOptions(), RefreshPriority.High);

        foreach (var episode in season.GetEpisodes().OfType<Episode>().Where(x => !x.IsVirtualItem))
        {
            _providerManager.QueueRefresh(episode.Id, CreateRefreshOptions(), RefreshPriority.High);
        }
    }

    private MetadataRefreshOptions CreateRefreshOptions()
    {
        return new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = MetadataRefreshMode.None,
            ReplaceAllMetadata = false,
            ReplaceAllImages = false,
            RemoveOldMetadata = false,
            ForceSave = true,
            IsAutomated = false
        };
    }
}

/// <summary>
/// Request to map a local season to an external title.
/// </summary>
public class SaveSeasonMappingRequest
{
    public Guid LocalSeasonId { get; set; }

    public string ExternalTitleName { get; set; } = string.Empty;

    public int? ExternalYear { get; set; }

    public string? SearchProviderName { get; set; }

    public string? ImageUrl { get; set; }

    public List<ProviderIdEntry> ProviderIds { get; set; } = new();
}
