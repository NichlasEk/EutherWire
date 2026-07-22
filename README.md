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
The planned structured electrical model for CAT6, EKRK, RK/FK, conductor area,
phase/neutral/earth assignments, switched lives, and conduit contents is in
[docs/electrical-cable-model-plan.md](docs/electrical-cable-model-plan.md).

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
3D endpoints, optional label, and IDs in `project.toml` schema 7.

Active tools explain their next step beside the pointer. DIM shows `1/2` and
`2/2`; WIRE and PIPE show the next route point, and placement tools identify
the expected surface click. Press `Esc` to cancel any unfinished dimension,
route, placement tool, or handle drag. Cancelling a drag restores the original
geometry. Visible handles are larger and use a forgiving 28 × 28 pixel hit
area, which also prepares the editor for touch input.

With `SEL` active, select an object and press the keyboard `Delete` key to use
the same undoable deletion as the inspector's `DELETE` button. Delete is ignored
while an inspector text field is focused or another drawing operation is active.

Hold `Ctrl` while clicking to add or remove objects from a multi-selection.
Every selected object is outlined in yellow while the inspector identifies the
primary object. `Delete` removes the complete selection as one dependency-safe
undo operation. `Ctrl+D` duplicates it with new stable IDs and unique `COPY`
labels; references between selected devices, cables, and conduits are remapped
to their copies. WALL copies move 300 mm along the active wall, while PLAN/3D
copies move 300 mm in X and Y.

Drag from empty canvas with `SEL` to create a blue box selection; hold `Ctrl`
to add the enclosed objects to the current selection. Selection uses visible
object anchors in PLAN, 3D, and the active WALL surface. To move a group, drag
the primary object's normal move handle. Devices, openings, annotations,
complete cable/conduit routes, and both wall-dimension endpoints translate as
one undoable command. `Esc` during the drag restores the entire group.

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
to edit its properties or delete it through the inspector. Devices, openings,
conduits, and cables share installation status and notes; cable status and
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
dotnet run --project src/EutherWire.Cli -- set-property examples/garage.eutherwire camera-north-pipe:property:installation_note "Leave pull wire"
dotnet run --project src/EutherWire.Cli -- validate examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- report examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- tasks examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- insert-vertex examples/garage.eutherwire camera-north-pipe 1 6500 -1000
dotnet run --project src/EutherWire.Cli -- delete-vertex examples/garage.eutherwire camera-north-pipe 1
dotnet run --project src/EutherWire.Cli -- configure examples/garage.eutherwire 10 1000
dotnet run --project src/EutherWire.Cli -- install examples/garage.eutherwire camera-north-cat6 tested 8200
dotnet run --project src/EutherWire.Cli -- install examples/garage.eutherwire camera-north installed
dotnet run --project src/EutherWire.Cli -- journal-create examples/garage.eutherwire phone-events.jsonl camera-north phone-garage installed
dotnet run --project src/EutherWire.Cli -- journal-apply examples/garage.eutherwire phone-events.jsonl
dotnet run --project src/EutherWire.Cli -- snapshot-export examples/garage.eutherwire garage.eutherwire-snapshot
dotnet run --project src/EutherWire.Cli -- snapshot-import garage.eutherwire-snapshot imported-garage.eutherwire
dotnet run --project src/EutherWire.Cli -- export-svg examples/garage.eutherwire garage.svg
dotnet run --project src/EutherWire.Cli -- export-png examples/garage.eutherwire garage.png
```

`project.toml` writes are deterministic and atomic. A save/load/save round trip
must produce no diff. Planning slack, service-loop length, unified installation
records, and measured installed length live in the same versioned model so a
future mobile installation client does not need a second source of truth.
Geometry and properties both have stable semantic addresses. For example,
`camera-north-pipe:vertex:1` moves a route point while
`camera-north-cat6:property:actual_length_mm` edits installation data through
the same undoable command layer used by the desktop UI.
Schema 12 stores one revisioned installation record per device, opening, conduit, and
cable, including optional timestamp, field position, note, test result, and
photo references. Offline updates use the append-only
`installation-events.jsonl`; duplicate event IDs are harmless and stale base
revisions become visible conflicts. See
[installation-records.md](docs/installation-records.md).

Portable `.eutherwire-snapshot` files contain canonical `project.toml`, the
installation journal when present, and every explicitly referenced photo. A
deterministic manifest records lengths and SHA-256 hashes. Import refuses an
existing target directory and validates all paths and contents before moving a
staged project into place. See
[portable-snapshots.md](docs/portable-snapshots.md).

The first native Android checklist imports those snapshots, lists every
installable object, edits status/note/actual length offline, recovers pending
events after interruption, and exports JSONL for conflict-aware desktop merge.
It also starts a measured project on the phone through explicit Survey,
Design, and Install modes: numeric room construction, wall-mounted fixed
openings, and full project snapshot export share the desktop document model.
Design includes a touch-driven 2D plan and rotatable 3D room review built from
that same measured shell; drag pans or rotates and pinch controls zoom. Mobile
Design can place distribution boards, junction boxes, outlets, lights, cameras, PoE
switches, and access points with mounting surfaces, exact XYZ fallback fields,
and large 2D move handles that keep mounted objects constrained to the shell.
It can also draw typed cables and 16/20/25 mm conduits directly in the plan.
Cable endpoints snap to real device ports, free taps create bend points, and
every saved route exposes large numbered vertices for 100 mm step editing. The
same routes render in 2D and 3D and immediately become Install tasks.
The cable editor can assign a cable to any drawn conduit; the cable then follows
the conduit geometry. Design shows contained cable names, physical fill against
the 40% planning limit, and `PASS`, `WARNING`, `VERIFY GROUPING`, or `UNKNOWN`
thermal state from the shared electrical analysis. Unknown data stays unknown
until traceable current-capacity inputs have been supplied. For power presets,
the same mobile cable dialog now records `Ib`, protective-device current and
characteristic, loaded conductor count, verified reference capacity, ambient/
grouping/insulation factors, and the human-readable source. It previews the
corrected `Ib <= In <= Iz` relation live and preserves the cable product and
conductor list when only design evidence changes.

```sh
./eutherwire.sh mobile-build
./eutherwire.sh mobile-install
```

See [mobile-checklist.md](docs/mobile-checklist.md) for the field workflow.

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

Schema 10 also separates a traceable conduit product, nominal size, actual mechanical fill, and thermal circuit sizing.
Loose FK/RK conductors use their insulated outside diameters, while the thermal
planning result remains `UNKNOWN` until explicit current, protection,
correction factors, and a traceable reference capacity are supplied. See
[docs/swedish-electrical-sizing.md](docs/swedish-electrical-sizing.md).
The desktop cable inspector exposes this through its `DESIGN` page; conduit
controls keep nominal product size separate from the manufacturer-specified
inner diameter used by the fill calculation.
The conduit inspector additionally exposes installation method and contained
cable/conductor load; shared power circuits trigger an explicit grouping check.

## Product principles

- One document model feeds both physical plan and logical schematic views.
- Devices have typed ports; cables connect ports.
- Conduits and cables are separate objects.
- Stable object IDs and a versioned, diff-friendly project format exist from
  the first release.
- Editing is command-based so undo and redo are fundamental, not retrofits.
- Document coordinates never depend on pixels or the current viewport.
- Deterministic rendering and export make projects testable.
