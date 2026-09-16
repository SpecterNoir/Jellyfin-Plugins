# Season Identifier

Season Identifier fixes a metadata problem caused by intentionally non-standard season layouts.

Jellyfin derives seasons from folders. If a local series contains a season number that does not exist under that series in the configured metadata provider, Jellyfin can attach incorrect metadata. Season Identifier lets an administrator explicitly map that local season to a different external TV title.

## Install

Add the repository URL once in **Jellyfin Dashboard → Plugins → Repositories**:

```text
https://raw.githubusercontent.com/SpecterNoir/Jellyfin-Plugins/main/manifest.json
```

Then open the normal Jellyfin **Plugin Catalog**, find **Season Identifier**, install it, and restart Jellyfin when prompted. Updates will appear through the same catalog.

## 0.1.0.0 scope

This first build implements **Entire title** mapping.

Example:

```text
Local
JoJo's Bizarre Adventure/
└── Season 3/
    ├── S03E01
    ├── ...
    └── S03E48

Metadata source
JoJo's Bizarre Adventure: Stardust Crusaders
├── Season 1 (24 episodes)
└── Season 2 (24 episodes)
```

The local numbering stays intact. Metadata is translated as:

```text
S03E01 -> Stardust Crusaders S01E01
...
S03E24 -> Stardust Crusaders S01E24
S03E25 -> Stardust Crusaders S02E01
...
S03E48 -> Stardust Crusaders S02E24
```

Season 0 / Specials are intentionally ignored.

### Not in 0.1.0.0

- Mapping to one specific external season.
- Manual per-episode remapping.
- Special-placement rules.
- Modifying or renaming folders.

Those can be added after the title-mapping path is proven reliable.

## Building

Requires the .NET 10 SDK.

```bash
dotnet build Jellyfin-Plugins.slnx -c Release
```
