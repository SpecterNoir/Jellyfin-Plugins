# Jellyfin Plugins

A collection of focused Jellyfin plugins distributed through a Jellyfin-compatible plugin catalog.

## Repository URL

Add this URL once in **Jellyfin Dashboard → Plugins → Repositories**:

```text
https://raw.githubusercontent.com/SpecterNoir/Jellyfin-Plugins/main/manifest.json
```

After saving the repository, install and update plugins from Jellyfin's normal **Catalog**.

## Season Identifier

Season Identifier extends Jellyfin's normal **Identify** workflow to Season items. A local season can be identified as a different external TV title while its local series, season number, folder structure, and episode numbering remain unchanged.

Primary use case:

- Keep `JoJo's Bizarre Adventure/Season 3` as Season 3 locally.
- Open Season 3 and choose **Identify**.
- Identify it as `JoJo's Bizarre Adventure: Stardust Crusaders`.
- Flatten the external title's numbered seasons into the local season episode sequence.
- Ignore external Season 0 / Specials.

See [`SeasonIdentifier/README.md`](SeasonIdentifier/README.md) for current scope.

## Visibility Manager

Visibility Manager reversibly hides selected Series, Seasons, Movies, and Episodes from normal Jellyfin browsing without deleting or moving media files.

See [`VisibilityManager/README.md`](VisibilityManager/README.md) for current scope.

## Library Cleanup

Library Cleanup diagnoses common Jellyfin season/episode inconsistencies such as duplicate database paths, duplicate numbering, virtual/phantom entries, missing media paths, incomplete numbering, and stale cached season/series links.

Version 0.1 provides conservative Safe Repair for stale hierarchy links and Jellyfin-native metadata refresh actions. It does not automatically purge duplicate database rows and never deletes, moves, renames, or recycles media files.

See [`LibraryCleanup/README.md`](LibraryCleanup/README.md) for current scope.
