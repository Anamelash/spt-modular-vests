# Vest recolour asset sources

This directory contains deterministic recolour inputs and review artifacts for
the five Modular Vests colour keys: `coyote`, `olive`, `black`, `multicam`, and
`emr_summer`.

The recolour inputs, the review renders and the scripts under `_work/` are kept outside
the repository (see `.gitignore`); this file describes the workflow that produced the textures
the rig bundles are built from. Game and WTT source files are extracted into each carrier's
`source/` directory and are never committed.

Run the scripts with the repository-local `uv` workflow, because the host has no system Python:

```powershell
uv run --no-project --with UnityPy --with pillow --with numpy python assets/vests/_work/extract.py --carrier trooper
uv run --no-project --with pillow --with numpy --with scipy python assets/vests/_work/recolor.py --carrier trooper --all
```

`status.json` is the handoff gate. A colour remains `review` until the author
explicitly approves the corresponding review sheet and its current hashes.

