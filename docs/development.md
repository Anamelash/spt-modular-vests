# Development

Notes for building the mod from this repository. Nothing here is needed to play with it —
that is `README.md`.

## Layout

- `server/ModularVests.Server` (net10) — registers the pouch and rig templates from
  `server/mod-files/items.jsonc`, the mod's trader from `server/mod-files/trader/`, the
  default rig presets, and hands the rigs to bots and loot by `server/mod-files/bots.jsonc`.
- `client/ModularVests.Client` (net471, BepInEx) — the rig window with the pouch grids, the
  cell bones on the model, slot blocking, the raid rules and reaching into a pouch.
- `client/ModularVests.DevTools` (net471) — the in-game editor of the cell layout and the
  pouch mounts. A development tool: `build/deploy.ps1` installs it next to the mod, the
  release archive never contains it, and the client does not know about it.
- `shared/ClusterGrid.cs` — the slot grammar and the cluster geometry, linked into both halves.
- `tests/ModularVests.Server.Tests` (net10) — the server unit tests plus the pure client and
  DevTools files linked into them.

References to the game's assemblies come from `client/GameReferences.props`; the installation
path is `SptGameDir` in `Directory.Build.props`.

## Commands

```powershell
# Back up the profiles, build Release and lay the mod out in the game (close the game and the server first)
pwsh -File build/deploy.ps1
# A bones.json in the game that differs from the one in the repository is pulled back into it;
# -OverwriteBones does the opposite. Editing bones.json outside the game means deploying with
# -OverwriteBones, or the edit is overwritten by the game's copy.
pwsh -File build/deploy.ps1 -OverwriteBones

# Tests
pwsh -File build/test.ps1

# Pouch model bundles (Unity 2022.3.43f1): assets/pouches -> unity/ -> server/bundles + server/bundles.json
pwsh -File build/build-bundles.ps1
# Rig bundles: a clone of the donor's bundle with a recoloured albedo, no Unity needed
pwsh -File build/build-vest-bundles.ps1 [-Carrier otv]
# Front/back/side/top views of a model -> unity/BundleOutput/previews
pwsh -File build/build-bundles.ps1 -Previews

# The catalogue of the mod's strings for translators -> localization/strings.json.
# Generated: translate server/mod-files/items.jsonc and trader/trader.jsonc instead.
pwsh -File build/dump-strings.ps1

# Release archive (version from Directory.Build.props; fails without server/bundles.json)
pwsh -File build/package.ps1
```

The system `dotnet` (C:\Program Files) is SDK 10, `~/.dotnet` is 9.0 and does not build net10;
the scripts pick the right one.

## Files outside git

The art sources and the built bundles are kept out of the repository (see `.gitignore`), so a
fresh clone builds the two DLLs and runs the tests, but not the model bundles:

- `assets/pouches/*` — the model kits (`.blend`, 8k textures, previews) that
  `build/build-bundles.ps1` reads.
- `assets/vests/*` — the recolour inputs per carrier. Textures extracted from the game and from
  WTT live in each carrier's `source/` and are never committed.
- `server/bundles/` and `server/bundles.json` — the output of the two bundle scripts, and what
  `build/package.ps1` puts in the release archive.

Keep a working copy of these next to the repository to cut a release.

## Version

The version lives in three places and they must agree:

- `Directory.Build.props` (`ModVersion`)
- `server/ModularVests.Server/ModMetadata.cs`
- `client/ModularVests.Client/Plugin.cs`

## Invariants

- **The id algorithm never changes**: the first 12 bytes of SHA-1 over `modularvests:` and the
  key. Renaming a key in `items.jsonc` orphans every copy of that item in existing profiles.
  The trader id goes into profiles as well (`TradersInfo`).
- **Pouch cells are only ever added.** `mod_pouch_N` is cluster `(N-1)/4+1`, cell `(N-1)%4+1`;
  the cells of a cluster read `1 2 / 3 4` for somebody facing the wearer.
- **Items may only be registered before `SaveCallbacks`.**
- Grid names inside a pouch appear in profiles: renaming or removing one loses whatever the
  players keep there.
