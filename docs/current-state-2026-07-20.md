# EutherWire current state and continuation handoff

Date: 2026-07-20

## Resume point

The handoff baseline was:

```text
c9f0dd4 Improve 3D elevation and wall opening controls
branch: main
remote: https://github.com/NichlasEk/EutherWire.git
```

Start future work by reading this file, `README.md`, `garage-3d-plan.md`, and
`mobile-installation-ar-plan.md`. Check `git status` before changing the
WaylandForge submodule because another WaylandForge task may be using the same
working tree.

## Product direction

EutherWire is a WaylandForge-native installation planner for electrical,
network, conduit, cable, and future home-technology work. It is deliberately
not a general CAD program. The document model is the product: plan view, 3D
view, exports, analysis, CLI editing, and the future phone installation mode
all operate on the same stable object IDs and coordinates.

The first real project is Garage Draft. It models a garage room, distribution
board, PoE switch, camera, conduit, CAT6 cable, and a garage-door opening.

## Implemented foundation

### Document and persistence

- Schema version 5 uses deterministic TOML under a `.eutherwire` directory.
- Devices, typed ports, cable routes, conduits, annotations, room geometry,
  and wall openings are first-class objects.
- Positions are document-space millimetres rather than screen pixels.
- Devices and route vertices carry X, Y, and Z coordinates.
- Devices have named mounting surfaces: free, floor, inner/outer ceiling, and
  each inner/outer wall.
- The room stores origin, width, depth, height, wall thickness, and ceiling
  thickness.
- Commands provide undo/redo for document edits.
- Stable semantic handles make geometry and properties editable by the GUI,
  CLI, and later by agents without relying on screen coordinates.
- Save/load/save is intended to remain byte-stable and atomic.

### Desktop editor

- Native WaylandForge window with plan and isometric 3D views.
- Grid, pointer-centred zoom, pan, selection, placement, text, cable routing,
  conduit routing, and opening tools.
- Right mouse orbits the 3D camera, middle mouse pans, and the wheel zooms.
- Camera presets cycle through ISO, NORTH, EAST, SOUTH, and WEST.
- The active drawing surface is outlined in yellow.
- DEV, WIRE, and PIPE place or route directly on the active 3D surface.
- Exact room dimensions are editable in the inspector.
- Exact device X/Y/Z values are editable in the inspector.
- Selected devices have a dedicated orange one-axis elevation handle and
  `Z -100` / `Z +100` controls.
- Elevation edits preserve X/Y, use undo/redo, and propagate to connected
  cable and conduit endpoints.
- Numeric fields accept the main number row and numeric keypad; Enter applies
  the value.
- WALL is an orthographic elevation over a selected N/S/E/W inner or outer
  wall. It renders a 500 mm grid, openings, mounted devices, and wall-local
  cable/conduit segments over the same X/Y/Z data used by PLAN and 3D.
- WALL supports placement, selection, hit testing, and spatial handle editing.
- Selected wall devices and openings show finished-floor height and nearest
  visible-corner offset.
- WALL has 23 pointer-anchored zoom levels from 20% to 3,200% plus pan.
- WALL masks the grid inside building openings so they read as actual cutouts.
- FREE/300/1100/2200/2400 mm height profiles snap new mounted devices, new
  wall routes, and dragged device handles to common finished-floor heights.
- Editable cable/conduit vertices have stable indexed elevation handles in
  WALL and 3D. They preserve X/Y, update only Z, propagate through contained
  cable geometry, and remain command-based and undoable.

### Garage geometry and openings

- The renderer shows floor, inner and outer wall shells, ceiling, devices,
  elevated routes, and openings.
- Garage doors, ordinary doors, windows, and penetrations store wall surface,
  3D centre, width, height, label, and stable handles.
- The OPEN palette has explicit `N`, `S`, `E`, `W`, `INSIDE`, and `OUTSIDE`
  wall selection.
- OPEN places exactly one opening and then automatically returns to SEL. This
  prevents a sequence of ordinary clicks from creating many doors.
- An opening can be selected by clicking its orange frame and removed with
  DELETE. The most recent accidental placement can also be removed with UNDO.
- Selected openings expose exact width and height plus named
  `resize:start` and `resize:end` handles.
- Garage Draft contains one intended 5,000 x 2,200 mm garage door on the south
  wall.

### Analysis, installation data, and export

- Cable and conduit length includes vertical travel.
- Project analysis reports recommended cable order length, conduit fill,
  materials, errors, and warnings.
- Cable state can be planned, installed, tested, changed, or blocked.
- Actual installed cable length is separate from planned length.
- CLI commands expose validation, reports, tasks, semantic handles, 2D/3D
  moves, property editing, route-vertex insertion/deletion, installation state,
  and configuration.
- SVG and PNG exports are deterministic and independent of the current GUI
  viewport.
- The PNG button writes into the open project's `exports/` directory.

## How to run and verify

The easiest safe editable demo is a copy under `.eutherwire-work/`; the tracked
example is not modified.

```sh
./eutherwire.sh
./eutherwire.sh 3d
./eutherwire.sh wall
```

Useful non-interactive checks:

```sh
./eutherwire.sh check
./eutherwire.sh report
./eutherwire.sh properties
./eutherwire.sh tasks
./eutherwire.sh export
./eutherwire.sh png
```

The latest full check completed with zero build warnings and zero build errors.
Document checks passed. Garage Draft reported schema 5, three devices, one
opening, one conduit, one cable, 22 semantic handles, and no analysis errors or
warnings.

## Current limitations

- The 3D renderer is a deterministic technical installation view, not a
  photorealistic room renderer.
- WALL has visual opening cutouts; future filled 3D walls still need the same
  subtraction treatment.
- Wall elevations do not yet have printable, repositionable dimension objects.
- Route vertices have one-axis Z handles; mobile-sized hit targets and gestures
  still need to be designed.
- Snapping does not yet cover every corner, existing route, port, configured
  mounting height, or clearance rule.
- Beams, pillars, workbenches, and vehicle clearance volumes are not modeled.
- Imported floor-plan calibration, PDF export, logical schematic view,
  symbol editor, autosave journal, and crash recovery remain future work.
- Mobile installation mode and camera/gyroscope AR are designed but not yet
  implemented. The staged design is in `mobile-installation-ar-plan.md`.

## Wall-elevation slice

The first wall-elevation editing mode now provides:

1. N/S/E/W and inside/outside selection;
2. an orthographic wall with floor/ceiling edges and metric grid;
3. doors, windows, penetrations, mounted devices, and wall-local routes;
4. placement and drag editing in wall horizontal/elevation coordinates;
5. nearest-corner and finished-floor offsets;
6. command-based semantic-handle edits over the shared model.

The next recommended slice is to add printable wall dimensions and bind common
mounting heights to symbol/library metadata, then add route-vertex Z handles
and collision/clearance diagnostics. Future filled 3D walls should reuse the
WALL opening masks. The wall-local coordinate system is also the basis for
phone calibration and AR overlays.

## Mobile direction

The phone is intended to become an installation client over the same project,
not a second editor format. The staged path is:

1. offline mobile checklist and task status;
2. camera background plus gyroscope orientation;
3. marker- or point-assisted room calibration;
4. anchored wall/floor/ceiling overlays;
5. field photos, actual dimensions, notes, and synchronization.

Gyroscope data alone is not accurate enough for installation placement. The AR
mode must bind tracking space to known project anchors and continue to show
numeric offsets for drilling and mounting decisions.

## WaylandForge submodule note

EutherWire commit `c9f0dd4` pins `vendor/WaylandForge` to:

```text
e11c9e9 Support numeric keypad in text fields
```

That commit is published in the WaylandForge repository on branch
`eutherwire-keypad`. WaylandForge `main` moved forward concurrently and already
contains equivalent keypad support along with unrelated Stormakt work. At the
time of this handoff, the shared submodule working tree also contains active,
uncommitted Stormakt assets and code from that other task. Do not reset, clean,
rebase, or overwrite the submodule merely to make EutherWire's parent status
look clean. A fresh recursive clone remains the reproducible EutherWire test
environment.

## Continuation checklist

```sh
git status --short --branch
git log -1 --oneline
git submodule status
./eutherwire.sh check
./eutherwire.sh 3d
```

Preserve unrelated WaylandForge changes, keep EutherWire commits narrowly
scoped, run the full check before publishing a functional slice, and push a
working checkpoint after each meaningful milestone.
