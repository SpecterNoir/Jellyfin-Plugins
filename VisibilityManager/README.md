# Visibility Manager

Visibility Manager is a Jellyfin 12 plugin for hiding unwanted library entries from normal browsing and search without deleting or moving media.

Typical uses include:

- Jellyfin-created or otherwise unwanted virtual seasons.
- Duplicate series or season entries.
- Duplicate movie titles.
- Individual episodes you temporarily do not want exposed in clients.

## Safety model

Visibility Manager is intentionally not a deletion tool.

It does **not**:

- delete files;
- move or rename files or folders;
- call Jellyfin media deletion endpoints;
- remove items from Jellyfin's database;
- use recycle-bin behavior.

The first build uses a reversible Jellyfin metadata marker (`__visibility_manager_hidden__`) and Jellyfin's native blocked-tag visibility filtering. Restoring visibility simply removes that marker from the selected item.

When a parent such as a Series or Season is hidden, Jellyfin's inherited-tag filtering also hides its descendants. The plugin's hidden-items list shows only the entries that were explicitly marked, not every inherited child.

## First build

Version `0.1.0.0` supports Series, Seasons (including virtual seasons), Movies, and Episodes through a dedicated Dashboard plugin page with Search, Hide from Jellyfin, and Restore visibility actions.
