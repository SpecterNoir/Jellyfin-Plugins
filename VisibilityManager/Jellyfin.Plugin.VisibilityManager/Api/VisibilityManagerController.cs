using Jellyfin.Plugin.VisibilityManager.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.VisibilityManager.Api;

/// <summary>
/// Administration API for reversible library visibility changes.
/// </summary>
[ApiController]
[Route("VisibilityManager/api")]
[Authorize(Policy = Policies.RequiresElevation)]
public class VisibilityManagerController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly VisibilityPolicyService _visibilityPolicy;

    public VisibilityManagerController(
        ILibraryManager libraryManager,
        VisibilityPolicyService visibilityPolicy)
    {
        _libraryManager = libraryManager;
        _visibilityPolicy = visibilityPolicy;
    }

    [HttpGet("items")]
    public async Task<ActionResult<IEnumerable<VisibilityItemDto>>> GetItems(
        [FromQuery] string? q = null,
        [FromQuery] int limit = 100)
    {
        await _visibilityPolicy.EnsureAllUsersBlockMarkerAsync().ConfigureAwait(false);

        var items = QueryManageableItems()
            .Where(item => !VisibilityPolicyService.IsEffectivelyHidden(item));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            items = items.Where(item => Matches(item, term));
        }
        else
        {
            items = items.Take(250);
        }

        return Ok(items
            .OrderBy(item => item.GetParent()?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 250))
            .Select(ToDto));
    }

    [HttpGet("hidden")]
    public async Task<ActionResult<IEnumerable<VisibilityItemDto>>> GetHiddenItems()
    {
        await _visibilityPolicy.EnsureAllUsersBlockMarkerAsync().ConfigureAwait(false);

        return Ok(QueryManageableItems()
            .Where(VisibilityPolicyService.IsExplicitlyHidden)
            .OrderBy(item => item.GetParent()?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto));
    }

    [HttpPost("items/{itemId:guid}/hide")]
    public async Task<ActionResult<VisibilityItemDto>> HideItem(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        if (!IsManageableItem(item))
        {
            return BadRequest(new { message = "This Jellyfin system container cannot be removed from view." });
        }

        if (!VisibilityPolicyService.IsExplicitlyHidden(item))
        {
            item.Tags = item.Tags
                .Append(VisibilityPolicyService.HiddenTag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await item
                .UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
        }

        await _visibilityPolicy.EnsureAllUsersBlockMarkerAsync().ConfigureAwait(false);
        return Ok(ToDto(item));
    }

    [HttpPost("items/{itemId:guid}/restore")]
    public async Task<ActionResult<VisibilityItemDto>> RestoreItem(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        if (VisibilityPolicyService.IsExplicitlyHidden(item))
        {
            item.Tags = item.Tags
                .Where(tag => !string.Equals(
                    tag,
                    VisibilityPolicyService.HiddenTag,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            await item
                .UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
        }

        return Ok(ToDto(item));
    }

    private IEnumerable<BaseItem> QueryManageableItems()
        => _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            Recursive = true,
            Limit = 50000
        }).Items.Where(IsManageableItem);

    private static bool IsManageableItem(BaseItem item)
    {
        // Prevent hiding Jellyfin's structural/root containers while allowing normal media,
        // virtual seasons, people, genres, playlists, collections, music, books, etc.
        var kind = item.GetBaseItemKind().ToString();
        return !string.Equals(kind, "AggregateFolder", StringComparison.Ordinal)
            && !string.Equals(kind, "UserRootFolder", StringComparison.Ordinal)
            && !string.Equals(kind, "CollectionFolder", StringComparison.Ordinal)
            && !string.Equals(kind, "Folder", StringComparison.Ordinal);
    }

    private static bool Matches(BaseItem item, string term)
    {
        if (item.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (item.Path?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var parent = item.GetParent();
        if (parent?.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var grandParent = parent?.GetParent();
        return grandParent?.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static VisibilityItemDto ToDto(BaseItem item)
    {
        var parent = item.GetParent();
        var grandParent = parent?.GetParent();
        var context = grandParent is not null
            ? grandParent.Name + " / " + parent?.Name
            : parent?.Name;

        return new VisibilityItemDto(
            item.Id,
            item.Name ?? "(Unnamed item)",
            item.GetBaseItemKind().ToString(),
            context ?? string.Empty,
            item.IndexNumber,
            item.IsVirtualItem,
            item.Path ?? string.Empty);
    }
}

public sealed record VisibilityItemDto(
    Guid Id,
    string Name,
    string Type,
    string Context,
    int? IndexNumber,
    bool IsVirtual,
    string Path);
