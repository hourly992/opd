# Poly Haven — CC0 resources

Selected surface maps, foliage texture atlases, a pure-sky HDRI and the source for the optimized broadleaf tree are from **Poly Haven**.

- License: **CC0 1.0 Universal** — https://creativecommons.org/publicdomain/zero/1.0/
- Provider's license declaration: https://polyhaven.com/license
- Exact asset pages, authors, download URLs, original MD5 checksums and SHA-256 checksums: `SOURCES.json`.
- New 2K maps/packed derivatives: `Graphics2K/SOURCES.json`; optimized model geometry: `OptimizedTrees/PROVENANCE.json`.
- Replacement facade maps: `Facades4K/SOURCES.json` — **Red Brick** by Rob Tuytel and **Brick Wall 003** by Dimitrios Savva / Rob Tuytel, CC0-1.0. The 4K albedo and OpenGL normal JPEGs are preserved byte-for-byte; only AO/roughness/height are packed into a GPU mask. Preparation: repository-root `prepare_facade_materials.py`.
- Distinct road/parking/path surfaces: `RoadSurfaces2K/SOURCES.json` — **Asphalt 01** by Charlotte Baglioni (photography) / Dario Barresi (processing), and **Clean Asphalt** by Dimitrios Savva, CC0-1.0. The 2K albedo and OpenGL normal JPEGs are preserved byte-for-byte; only AO/roughness/height are packed. The existing **Asphalt 02** by Rob Tuytel remains the separate cracked main-road scan. Preparation: repository-root `prepare_road_materials.py`.
- Download/preparation script: `../../../../fetch_environment_assets.py` (repository root).

CC0 permits use, modification and redistribution, including commercial use. No third-party game models, screenshots, example renders or website graphics are included. The user's game screenshot is a visual reference only.

The original `tree_small_02` and `fir_tree_01` folders contain **texture atlases only**. Their first generated trees/hedges were built in Unity. `Foliage.png` combines original albedo RGB with the author's opacity map. `Leaf.png` and `Sprig.png` isolate appropriate atlas regions; leaf opacity is clipped to exclude unrelated UV islands. Corresponding normal-map crops, coordinates and checksums are recorded in the original `SOURCES.json`; those original downloaded maps are unchanged.

The global graphics upgrade additionally uses **Tree Small 02 by Rico Cilliers**, https://polyhaven.com/a/tree_small_02 (CC0-1.0). The checksum-verified original Blender file stays in the ignored repository-root `tools/asset-cache/`, not in Git or the Unity build. `prepare_vegetation_models.py` exports three reduced meshes from the author's LOD1: wood is decimated independently, original leaf UV islands become oriented cards (curved/compound islands keep their topology), with area-compensated sampling for distant LODs. Material partitions, leaf/trunk UVs and separate branch UV map/scale are retained. Zero-area faces are discarded; the model is recentered on the low trunk for gameplay colliders and converted to Unity coordinates. Geometry provenance/checksums are in `OptimizedTrees/PROVENANCE.json`. New 2K albedo/OpenGL normals and packed AO/roughness/height maps are in `Graphics2K/`. Leaf RGB outside valid alpha is dilated without white bake padding to prevent mip bleed; atlas islands are not rearranged. This is visual dressing, not surveyed or botanical verification of local planting.

The game does not use the live Poly Haven API. Optional preparation scripts use the public API and identify themselves as `OPD-EnvironmentAssetPrep/1.0` or `OPD-EnvironmentAssetPrep/2.0`. Resource selection and API are provided by **Poly Haven**, without implying endorsement. Project-authored maps and the room/curtain atlas have separate provenance in `Assets/Art/Textures/PROVENANCE.json` and are not attributed to Poly Haven.
