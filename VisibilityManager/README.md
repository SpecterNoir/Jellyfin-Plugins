# Visibility Manager

Visibility Manager is a Jellyfin 12 plugin for removing unwanted library entries from normal browsing and search without deleting or moving media.

Typical uses include:

- Jellyfin-created or otherwise unwanted virtual seasons.
- Duplicate series or season entries.
- Duplicate movie titles.
- Individual episodes or other normal library items you do not want exposed in clients.

## Safety model

Visibility Manager is intentionally not a deletion tool.

It does **not**:

- delete files;
- move or rename files or folders;
- call Jellyfin media deletion endpoints;
- remove items from Jellyfin's database;
- use recycle-bin behavior.

Visibility Manager uses a reversible Jellyfin metadata marker (`__visibility_manager_hidden__`) and Jellyfin's native blocked-tag visibility filtering. Restoring visibility simply removes that marker from the selected item.

When a parent such as a Series or Season is removed from view, Jellyfin's inherited-tag filtering also hides its descendants. The plugin's hidden-items list shows only the entries that were explicitly marked, not every inherited child.

## Normal workflow

Version `0.2.1.0` adds **Remove** directly to Jellyfin's normal three-dot item menu on cards, lists, and item detail pages.

The menu action is intentionally labeled with the secondary text **Hide from Jellyfin only — files stay untouched**. Selecting it also shows a confirmation explaining that the media file is not deleted, moved, renamed, or recycled.

The Dashboard plugin page remains available primarily as a recovery/management screen so previously removed items can be restored.
