# Season Identifier

Season Identifier fixes a metadata problem caused by intentionally non-standard season layouts.

Jellyfin derives seasons from folders. If a local series contains a season number that does not exist under that series in the configured metadata provider, Jellyfin can attach incorrect metadata. Season Identifier lets the normal Jellyfin **Identify** workflow map that local season to a different external TV title.

## 0.2.0 scope

The primary workflow is now native Jellyfin Identify:

1. Open a Season in Jellyfin Web.
2. Open its menu and choose **Identify**.
3. Search for the external TV title that season represents.
4. Select the result and apply it using Jellyfin's normal Identify dialog.

The plugin stores that choice as the season's title mapping while preserving the local series, season number, and episode numbering.

Example:

```text
Local
JoJo's Bizarre Adventure/
└── Season 3/
    ├── S03E01
    ├── ...
    └── S03E48

Identify Season 3 as:
JoJo's Bizarre Adventure: Stardust Crusaders
├── Season 1 (24 episodes)
└── Season 2 (24 episodes)
```

The local numbering stays intact. Episode metadata is translated as:

```text
S03E01 -> Stardust Crusaders S01E01
...
S03E24 -> Stardust Crusaders S01E24
S03E25 -> Stardust Crusaders S02E01
...
S03E48 -> Stardust Crusaders S02E24
```

Season 0 / Specials are intentionally ignored in title mode.

The older configuration page remains available only as an advanced/fallback mapping view. It is no longer the primary workflow and is no longer added to Jellyfin's main navigation.

## Dependency

Native Season Identify uses **File Transformation** by IAmParadox27 to expose Jellyfin Web's existing Identify command on Season items without modifying Jellyfin's installed web files. The Jellyfin plugin catalog entry declares this dependency automatically.

## Not yet implemented

- Mapping to one specific external season.
- Manual per-episode remapping.
- Special-placement rules.
- Modifying or renaming folders.

Specific-season mode is planned after the native title-identification path is validated.
