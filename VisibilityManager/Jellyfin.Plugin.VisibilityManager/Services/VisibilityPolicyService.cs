using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.VisibilityManager.Services;

/// <summary>
/// Applies the reversible visibility marker used by Visibility Manager.
/// </summary>
public sealed class VisibilityPolicyService
{
    /// <summary>
    /// Reserved Jellyfin tag used only as a visibility marker.
    /// </summary>
    public const string HiddenTag = "__visibility_manager_hidden__";

    private readonly IUserManager _userManager;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public VisibilityPolicyService(IUserManager userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Ensures Jellyfin's native blocked-tag visibility filter recognizes the plugin marker for every user.
    /// Existing blocked tags are preserved exactly.
    /// </summary>
    public async Task EnsureAllUsersBlockMarkerAsync()
    {
        await _syncLock.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var user in _userManager.GetUsers())
            {
                var blockedTags = user.GetPreference(PreferenceKind.BlockedTags);
                if (blockedTags.Contains(HiddenTag, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                user.SetPreference(
                    PreferenceKind.BlockedTags,
                    blockedTags.Append(HiddenTag).ToArray());

                await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public static bool IsExplicitlyHidden(BaseItem item)
        => item.Tags.Contains(HiddenTag, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the item itself or one of its parents carries the visibility marker.
    /// This mirrors Jellyfin's inherited-tag behavior and prevents redundant child entries in the admin picker.
    /// </summary>
    public static bool IsEffectivelyHidden(BaseItem item)
    {
        BaseItem? current = item;
        while (current is not null)
        {
            if (IsExplicitlyHidden(current))
            {
                return true;
            }

            current = current.GetParent();
        }

        return false;
    }
}
