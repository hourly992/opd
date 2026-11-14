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
python Maintenance/StreetRepair/validate_garage_revision.py
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

The revision validator compares against `00f63cb`, independently checking all
existing entrances, tangent bases/winding, ramp/deck collisions, exact courtyard
coverage and holes, material colors, red paving, immutable leaves/lights/road,
and the geometry/submesh budget. Use `--output <path.json>` to save its result.

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
