# EutherWire

EutherWire is a WaylandForge-native drawing and planning tool for electrical,
network, conduit, cable, and home-technology installations.

The project deliberately starts smaller than a general CAD system. Its first
real document is a garage installation with a distribution board, PoE switch,
conduits, cameras, and cable routes. Objects and connections are structured
data rather than unrelated pixels and lines.

## Bootstrap

WaylandForge is pinned as a Git submodule:

```sh
git clone --recurse-submodules https://github.com/NichlasEk/EutherWire.git
```

For an existing clone:

```sh
git submodule update --init --recursive
```

The easiest safe test is:

```sh
./eutherwire.sh
```

To open the editable Garage Draft directly in the 3D installation view:

```sh
./eutherwire.sh 3d
```

To open it directly as an orthographic wall elevation:

```sh
./eutherwire.sh wall
```

The wall demo starts on the south inside wall so Garage Draft's garage door is
immediately visible.

Use the `PLAN`/`3D` control to switch views. In 3D, the button beside it cycles
through the floor, inner/outer ceiling, and every inner/outer wall. The yellow
outline is the active drawing surface. Choose `DEV` to place a box or lamp on
it, or `WIRE`/`PIPE` to route directly on it. With nothing selected, the right
panel edits room width, depth, height, wall thickness, and ceiling thickness.
Selecting a device exposes exact X/Y/Z fields, while its move handle follows
the named surface in 3D and remains undoable. Use the right mouse button to
orbit, middle mouse button to pan, and wheel to zoom. The `CAMERA` control or
`F10` cycles ISO, NORTH, EAST, SOUTH, and WEST views. See
[docs/garage-3d-plan.md](docs/garage-3d-plan.md) for the remaining roadmap.

Selected devices also have an orange vertical elevation handle and `Z -100` /
`Z +100` controls. Exact numeric fields accept both the main number row and
the numeric keypad; press Enter to apply the typed millimetres.

Every editable cable/conduit vertex also has an orange one-axis elevation
handle in WALL and 3D. For example, `camera-north-pipe:elevation:1` changes only
that vertex's Z value while preserving X/Y; contained cable geometry follows
and the move remains undoable. Elevation handles are hidden in PLAN to avoid
overlapping the ordinary blue X/Y vertex handles.

`DIM` creates persistent installation dimensions in `WALL`: click the first
mounting point and then the second. The dimension snaps to 100 mm, displays its
true wall-local length, and immediately returns to `SEL`. Select the green line
to drag its stable `resize:start` and `resize:end` handles, rename it in the
inspector, delete it, or undo/redo the edit. Dimensions retain their wall face,
3D endpoints, optional label, and IDs in `project.toml` schema 6.

Garage Draft now includes a first-class 5,000 × 2,200 mm garage-door opening
on the south wall. Openings retain their wall, 3D centre, dimensions, stable
handles, and IDs through TOML, SVG, and PNG export.

Select `OPEN`, pick `PORT`, `DÖRR`, `FÖNSTER`, or `GENOMF`, then choose the
wall with `N`, `S`, `E`, or `W` and `INSIDE`/`OUTSIDE`. Click the wall once to
place the opening; EutherWire immediately returns to `SEL` so additional clicks
cannot accidentally create more doors. Selecting the opening exposes exact width
and height fields plus named `resize:start` and `resize:end` handles.

The `WALL` view shows one selected wall straight on. Use `N`/`S`/`E`/`W` and
`INSIDE`/`OUTSIDE` to choose the face. Openings, mounted devices, and route
segments on that wall share their exact coordinates with PLAN and 3D. Selected
devices and openings show offsets from finished floor and the nearest visible
corner. Middle/right drag pans; the wheel steps through 23 pointer-anchored zoom
levels from 20% overview to 3200% detail.

Wall openings mask the metric grid so doors, windows, garage doors, and
penetrations read as real cutouts. The persistent `HEIGHT` row offers `FREE`,
`300`, `1100`, `2200`, and `2400` mm mounting profiles. A profile snaps newly
placed wall devices, new WIRE/PIPE points, and dragged device handles to that
finished-floor height; `FREE` returns to the ordinary 100 mm grid.

This initializes WaylandForge if needed, builds the solution, and opens a
writable Garage Draft under `.eutherwire-work/`. The tracked example is never
modified. Other useful modes are:

```sh
./eutherwire.sh check
./eutherwire.sh 3d
./eutherwire.sh wall
./eutherwire.sh report
./eutherwire.sh properties
./eutherwire.sh tasks
./eutherwire.sh export
./eutherwire.sh png
./eutherwire.sh run path/to/project.eutherwire
```

Build and run the current native prototype inside a Wayland session:

```sh
dotnet build src/EutherWire.App/EutherWire.App.csproj
dotnet run --project src/EutherWire.App --no-build -- examples/garage.eutherwire
```

Use the middle or right mouse button to pan and the wheel to zoom around the
pointer. Yellow object handles and blue route-vertex handles are draggable.
`DEV`, `WIRE`, `PIPE`, and `TEXT` create real document objects; select an object
to edit its properties or delete it through the inspector. Cable status and
actual installed length are editable there as undoable commands. Diagnostic
rows select the affected object, so a reported problem leads back to its
geometry and semantic handles.

The CLI uses the same semantic handles as the UI:

```sh
dotnet run --project src/EutherWire.Cli -- handles examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- properties examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- move examples/garage.eutherwire camera-north-pipe:vertex:1 7000 -3000
dotnet run --project src/EutherWire.Cli -- move3d examples/garage.eutherwire camera-north-pipe:elevation:1 6500 -1000 2700
dotnet run --project src/EutherWire.Cli -- set-property examples/garage.eutherwire camera-north-cat6:property:installation_status tested
dotnet run --project src/EutherWire.Cli -- validate examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- report examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- tasks examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- insert-vertex examples/garage.eutherwire camera-north-pipe 1 6500 -1000
dotnet run --project src/EutherWire.Cli -- delete-vertex examples/garage.eutherwire camera-north-pipe 1
dotnet run --project src/EutherWire.Cli -- configure examples/garage.eutherwire 10 1000
dotnet run --project src/EutherWire.Cli -- install examples/garage.eutherwire camera-north-cat6 tested 8200
dotnet run --project src/EutherWire.Cli -- export-svg examples/garage.eutherwire garage.svg
dotnet run --project src/EutherWire.Cli -- export-png examples/garage.eutherwire garage.png
```

`project.toml` writes are deterministic and atomic. A save/load/save round trip
must produce no diff. Planning slack, service-loop length, cable installation
state, and measured installed length live in the same versioned model so a
future mobile installation client does not need a second source of truth.
Geometry and properties both have stable semantic addresses. For example,
`camera-north-pipe:vertex:1` moves a route point while
`camera-north-cat6:property:actual_length_mm` edits installation data through
the same undoable command layer used by the desktop UI.

SVG and PNG exports are generated directly from document coordinates with stable object
IDs and deterministic ordering. It is independent of the current desktop zoom,
selection, and window size, making it suitable for Git diffs and later PDF
output. The desktop `PNG` button writes `exports/plan.png` inside the open
project directory.

## Status

The native prototype now covers the document kernel, semantic editing handles,
TOML persistence, interactive cable and conduit routing, and the first
checkpoint-3 analysis rules. The inspector and `report` command show exact
route lengths, recommended cable order length, estimated conduit fill,
materials, and connection diagnostics. See
[docs/development-plan.md](docs/development-plan.md).

## Product principles

- One document model feeds both physical plan and logical schematic views.
- Devices have typed ports; cables connect ports.
- Conduits and cables are separate objects.
- Stable object IDs and a versioned, diff-friendly project format exist from
  the first release.
- Editing is command-based so undo and redo are fundamental, not retrofits.
- Document coordinates never depend on pixels or the current viewport.
- Deterministic rendering and export make projects testable.
