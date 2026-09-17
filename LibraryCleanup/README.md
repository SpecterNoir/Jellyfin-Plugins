# Library Cleanup

Library Cleanup is a Jellyfin 12.x administration plugin for diagnosing and conservatively repairing common TV library inconsistencies.

It is intentionally separate from Season Identifier and Visibility Manager.

## 0.1 features

- Scans seasons and episodes from Jellyfin's library database.
- Detects multiple episode database records pointing at the same physical media path.
- Detects duplicate episode numbers assigned to different media paths in one series/season.
- Detects multiple season records using the same season number in one series.
- Detects virtual / phantom seasons and episodes.
- Detects episode records whose media path no longer exists from Jellyfin's point of view.
- Detects missing season/episode numbering.
- Detects stale cached `SeasonId`, `SeriesId`, or season-number links when they disagree with the episode's physical folder hierarchy.
- Provides a **Safe Repair** action for stale hierarchy links. Safe Repair writes the physical parent season/series relationship back to the episode record and queues a normal metadata refresh.
- Provides a manual **Full metadata refresh** action that uses Jellyfin's native refresh queue and replaces metadata/images for the selected item.

## Safety model

Version 0.1 does **not** automatically delete duplicate database rows. Duplicate database records are reported so they can be investigated without risking a destructive or foreign-key-sensitive database operation.

The plugin never deletes, moves, renames, or recycles media files.

## Planned cleanup areas

Future builds can add guarded repair strategies for confirmed duplicate database records, stale virtual items, bad parent/child relationships, and other repeatable Jellyfin library inconsistencies as they are encountered in real libraries.
