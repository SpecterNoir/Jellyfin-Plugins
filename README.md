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

Native Season Identify uses **File Transformation** by IAmParadox27 to expose Jellyfin Web's existing Identify command on Season items without modifying Jellyfin's installed web files. The dependency is included in this catalog.

See [`SeasonIdentifier/README.md`](SeasonIdentifier/README.md) for current scope.
