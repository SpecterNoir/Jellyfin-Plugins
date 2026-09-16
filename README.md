# Jellyfin Plugins

A collection of focused Jellyfin plugins.

## Repository URL

Add this URL once in **Jellyfin Dashboard → Plugins → Repositories**:

```text
https://raw.githubusercontent.com/SpecterNoir/Jellyfin-Plugins/main/manifest.json
```

After saving the repository, install plugins from Jellyfin's normal **Catalog**. Future releases are published to GitHub Releases and added to this catalog automatically.

## Season Identifier

Season Identifier lets a local Jellyfin season folder use metadata from a different TV title without changing the local folder structure or season number.

Initial use case:

- Keep `JoJo's Bizarre Adventure/Season 3` as Season 3 locally.
- Identify it as the external title `JoJo's Bizarre Adventure: Stardust Crusaders`.
- Flatten the selected title's numbered seasons into the local season episode sequence.
- Ignore external Season 0 / Specials.

See [`SeasonIdentifier/README.md`](SeasonIdentifier/README.md) for current scope.
