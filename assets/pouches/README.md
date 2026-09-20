# Pouch assets for SPT

The kits themselves are kept outside the repository (see `.gitignore`); this file describes
what one contains and how it reaches the game.

The 13 pouch models used by the mod are prepared as separate kits. The supplied source `.blend`
files and source textures were not modified. Every kit has the five current colourways: Coyote,
Olive, Multicam, Black and EMR Summer.

| Model | Color variants | LOD0 / LOD1 triangles | Bundle key |
| --- | --- | ---: | --- |
| `magpouch_07` | coyote, olive, multicam, black, emr_summer | 8,000 / 1,000 | `modularvests/magpouch_07_{variant}.bundle` |
| `magpouch_02_opentop` | coyote, olive, multicam, black, emr_summer | 8,000 / 999 | `modularvests/magpouch_02_opentop_{variant}.bundle` |
| `magpouch_03_flapper_close` | coyote, olive, multicam, black, emr_summer | 7,999 / 999 | `modularvests/magpouch_03_flapper_close_{variant}.bundle` |
| `pouch_01_gadget` | coyote, olive, multicam, black, emr_summer | 7,999 / 999 | `modularvests/pouch_01_gadget_{variant}.bundle` |
| `pouch_03_admin` | coyote, olive, multicam, black, emr_summer | 8,000 / 1,000 | `modularvests/pouch_03_admin_{variant}.bundle` |
| `pouch_06_survival` | coyote, olive, multicam, black, emr_summer | 8,000 / 1,000 | `modularvests/pouch_06_survival_{variant}.bundle` |
| `frag_grenade_pouch` | coyote, olive, multicam, black, emr_summer | 7,999 / 998 | `modularvests/frag_grenade_pouch_{variant}.bundle` |
| `small_pouch` | coyote, olive, multicam, black, emr_summer | 8,000 / 1,000 | `modularvests/small_pouch_{variant}.bundle` |
| `grenade_pouch` | coyote, olive, multicam, black, emr_summer | 8,000 / 1,000 | `modularvests/grenade_pouch_{variant}.bundle` |
| `vertical_pouch` | coyote, olive, multicam, black, emr_summer | 8,000 / 1,000 | `modularvests/vertical_pouch_{variant}.bundle` |
| `bottle_pouch` | coyote, olive, multicam, black, emr_summer | 8,000 / 1,000 | `modularvests/bottle_pouch_{variant}.bundle` |
| `med_pouch` | coyote, olive, multicam, black, emr_summer | 8,000 / 1,000 | `modularvests/med_pouch_{variant}.bundle` |
| `utility_pouch` | coyote, olive, multicam, black, emr_summer | 8,000 / 1,000 | `modularvests/utility_pouch_{variant}.bundle` |

`pouch_01_gadget` ends one triangle under each nominal target because of the source topology and Blender's edge-collapse decimation. Its counts were checked after FBX reimport and in Unity.

`magpouch_07` has its own kit notes (`magpouch_07/README.md`). The current mod integration
is defined by `build/build-bundles.ps1`.

`frag_grenade_pouch` has its own kit documentation (`frag_grenade_pouch/README.md`), including its EFT shader maps. The instructions below describe the legacy Unity packages kept with the earlier kits.

Each model folder contains:

- `<model>_LODs_8k_1k.blend`: two UV-mapped meshes, all available Blender materials, and external relative links to `textures/`. LOD0 is visible when opened; enable the hidden LOD1 in the Outliner to inspect it.
- `<model>_lod_meshes.fbx`: both meshes with names ending `_LOD0` and `_LOD1`, centered pivot, metres, Unity Y-up.
- `textures/`: prepared RGBA albedos, shared normal/specular maps, the ID reference map and source references.
- `<model>_lod_prefabs.unitypackage`: variant prefabs, materials, the shared 8k/1k FBX, and texture dependencies.
- `Unity/Assets/`: the same prefabs and dependencies as individual Unity assets with their `.meta` files.
- `previews/` and `manifest.json`: visual checks and machine-readable mesh/variant details.

## Import utility pouches into the EFT SDK

1. Import each desired `.unitypackage`. Alternatively, copy each model folder's `Unity/Assets/` contents into the SDK project's `Assets/`, preserving `.meta` files. Use one method for each model.
2. Copy the shared `Unity/Editor/UtilityPouchLODSetup.cs` from this directory into the SDK's `Assets/Editor/`. Select a `<model>_lod_meshes.fbx` and run **Tools > Modular Vests > Create selected utility pouch variants**. This rebuilds that model's prefabs using EFT's `Bumped Specular Smap` shader if it is available.
3. Inspect the model's orientation, LOD switching, materials, and scale against the rig. Set up EFT `PreviewPivot` and icon for each prefab. Adjust the basic BoxCollider if necessary.
4. Build the AssetBundle in the SDK for Windows and the matching EFT Unity version. Use the bundle key in the table and each prefab's own name as its `rcid`.

Every prefab has one root `LODGroup`: LOD0 at screen-relative height `0.05`, LOD1 down to `0.002`, Fade Mode `None`. These thresholds are starting values for a small pouch; tune them after checking the item preview and a worn rig. The prefabs use static `MeshRenderer` objects. They do not provide a rigged `Skin` renderer.

The legacy `.unitypackage` files use Unity Standard as a placeholder shader. The mod's current
builder produced 78 Windows bundles (65 colour bundles plus 13 shared map bundles) in Unity
2022.3.43f1; the server files are under `server/bundles/` and `server/bundles.json`.
