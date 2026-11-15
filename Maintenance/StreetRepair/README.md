# Saved street surface repair

The playable scene is `Unity_Walk/Assets/Scenes/Street.unity`. All changes are
already baked into that scene and its referenced meshes. Opening the scene or
building it does **not** run these tools. Never run the archived street generator.

`Street_LeafStudy.unity` keeps its existing layout, surfaces and lighting. Its
shared leaf assets receive the same original silhouettes. Repaired main-scene
terrain/grass/paving has separate GUIDs, so it cannot punch holes in the study.

## Validation without Unity

Install `requirements.txt` into a virtual environment, then run:

```sh
python Maintenance/StreetRepair/validate.py
python Maintenance/StreetRepair/validate_parking_network.py
```

The validator compares against base `89336538c120b488cb93af6a6d46692a84ee5f69`
on `codex/painted-district`. It checks saved asset data, object preservation,
road/crosswalk invariants, walk widths, leaf budgets and ramp collisions. It
does not compile C#, import shaders, measure frame times or capture gameplay.

## Reproducing the bake

These scripts are a one-time migration from that exact base, not an idempotent
map generator. Use a **separate clean checkout at the base commit**, and copy
this Maintenance directory and the updated C#/shader sources with their `.meta`
files into it first.
Do not run them on a checkout containing the repaired assets.

Run `python Maintenance/StreetRepair/bake.py`. Its guard refuses an edited scene
or a different source commit. It runs the leaf, surface, garage, batching,
surface-following, isolation and validation steps in order.

`garages.py:SITES` records provisional positions, separately from the shared
shared entrance geometry. The supplied photos establish shape and the 5/7
address, but do not establish survey coordinates, count or ramp dimensions.
The two entrances are the complete existing provisional pair in the north 5/7
corridor. Their clear separation is now 3.21 m; rising asphalt connects to a
flat raised courtyard, with a rear return to original grade. Do not describe
this placement as a surveyed reconstruction.

## Follow-up from the initial PR #3 head

`revise_garages.py` applies only the garage follow-up to the unchanged saved
scene at `00f63cb0102b1dd800d7a11a0a519ba42034c58f`. It refuses an edited scene.
Use a separate checkout at that commit with the revised scripts copied in;
run it once, then apply the red-paver material change and run both validators.
Do not run `follow_surfaces.py`: the follow-up preserves every leaf asset.

The archived `validate_garage_revision.py` checks the two-entrance revision at
`2fb3b79` against `00f63cb`; use the network validator for the current scene.
The archived revision validator independently checks
existing entrances, tangent bases/winding, ramp/deck collisions, exact courtyard
coverage and holes, material colors, red paving, immutable leaves/lights/road,
and the geometry/submesh budget. Use `--output <path.json>` to save its result.

## Connected courts and the opposite-side pair

`connect_parking.py` is a guarded one-time follow-up from the unchanged scene
at `2fb3b792ddba4966e3aceb8c74866f81827a36bc`. The revised shared body/canopy
assets must already be present. It fills the missing north connections to the
existing ring drive, creates a west-side pair and its courtyard/mouth detour,
and cuts matching ground/collision holes. Existing red paving, house poses,
street profile, leaf assets and lighting are preserved.

The new pair uses the same entrance model at `(-108,67)` and `(-108,75.2)`,
rotation Y=90 degrees, on the west side of the main street. This is a checked
free placement, not a literal geographic reflection into the existing woods.
Panoramas 20–24 and overhead reference 25 guide connectivity; positions remain
estimates. `batch_garages.py` bakes four entrances into the four existing
renderer objects; the two new objects contain only collider/metadata.

`validate_parking_network.py` checks the current saved scene against `2fb3b79`.
It independently verifies all four ramp collisions, actual paved connectivity
to the main street/courts, preserved objects/materials, and resource budgets.

## Asphalt020S source maps and material-only follow-up

The Asphalt020S follow-up starts from
`57691931f92fac05831e3246239ae93489372fce` on the same PR branch. It changes
existing asphalt materials and adds the exact source maps plus a dedicated
shader; it does not regenerate the scene or any mesh. Keep the saved street
scene, red paving, white concrete, leaves, lighting and street profile intact.

Use the specifically requested ambientCG **Asphalt020S**, licensed CC0 1.0:
<https://ambientcg.com/view?id=Asphalt020S> and
<https://docs.ambientcg.com/license/>. The four required original filenames are:

- `Asphalt020S_2K-JPG_Color.jpg`
- `Asphalt020S_2K-JPG_NormalGL.jpg`
- `Asphalt020S_2K-JPG_Roughness.jpg`
- `Asphalt020S_2K-JPG_AmbientOcclusion.jpg`

Each must be a decodable 2048 × 2048 JPEG. The installer validates all four
before changing any Unity asset. Use exactly one input mode:

```sh
python Maintenance/StreetRepair/install_asphalt020s.py --archive /path/to/Asphalt020S_4maps.zip
# Or the directory containing the four unchanged original JPEGs:
python Maintenance/StreetRepair/install_asphalt020s.py --maps-dir /path/to/extracted-four-original-maps
python Maintenance/StreetRepair/install_asphalt020s.py --verify
```

The supplied `Asphalt020S_4maps.zip` is a user-repacked four-map container,
not the complete official `Asphalt020S_2K-JPG.zip`. Its SHA-256 identifies that
supplied container only. `SOURCE.json` records this distinction, the official
asset/download/license URLs, delivery mode and each original map checksum.
The archive is not committed. With `--maps-dir`, the installer records no
archive checksum. No substitute texture is selected or downloaded.

The original four JPEGs are saved byte-for-byte. `AO_Roughness.png` stores
original AO red-channel values in R and original roughness red-channel values
in G, with B=0; no resampling or repainting is performed. The runtime shader
reads only color, NormalGL and this packed mask. All seven active asphalt
materials share these maps at a physical repeat of 1.85 × 1.85 m, with mipmaps,
repeat wrap and instancing. Source JPEGs and the packed PNG remain 2048².
Unity imports the runtime color at up to 2048² and the normal/packed mask at up
to 1024²: only the imported runtime versions of those two maps are downscaled.
The packed PNG still contains every original AO/roughness value at 2048².
This bounds the asphalt level-zero runtime texel count at 6,291,456, compared
with 10,223,616 in the former active asphalt map set; it does not measure VRAM.
The complete main-scene/prefab material closure has 124 → 116 unique texture
references and 199,305,528 → 194,062,648 effective level-zero texels, including
the three old WalkEdge maps still referenced by other materials. These are
saved-reference/import-limit counts, not actual GPU memory measurements.
The previous general shader used additional texture fetches in its asphalt
path; three source texture reads in this shader are a static code count,
not a measurement of FPS, GPU time or generated lighting/shadow-pass cost.

The installed-source verifier checks source SHA-256, all packed pixels,
importer settings, shader source and material references without writing
assets. The independent report is
`Reports/StreetRepair/asphalt020s-validation.json`; the current status is
`Reports/StreetRepair/asphalt020s-status.json`. Reproduce that read-only audit
with the supplied archive (only the optional JSON report is written):

```sh
python Maintenance/StreetRepair/validate_asphalt020s.py --archive /path/to/Asphalt020S_4maps.zip --output Reports/StreetRepair/asphalt020s-validation.json
```

Rebuild its separate PDF with
system Python (`reportlab` and `pypdf` required):

```sh
python Maintenance/StreetRepair/report_asphalt020s.py
```

The PDF reads the installed `SOURCE.json`, status and independent report;
it does not rerun the original migration or alter the historical PDFs.
Unity shader import, a game build and frame-time checks remain separate work.

The offline cutter uses constrained triangulation with holes and interpolates
the original vertex channels. Render and collision meshes use the same cut.
The isolation step restores shared historical assets from the base and points
only the main scene at repaired copies. No existing GameObject is removed.

## Required Unity follow-up

Open with Unity **6000.6.7f1**, allow asset/shader import, build the saved scene
and run `-streetQA -qaOutput <directory>` plus the painted-district QA flow.
Capture paired views at house 5, court 7, eye-level street, south/east edges,
all other affected sectors and close-ups of paving, leaves and ramp mouths.
Check driveway transitions, no collision over ramp lanes, and frame times/
draw calls on the same hardware and camera paths. No Unity run is claimed here.
