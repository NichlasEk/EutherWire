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

This initializes WaylandForge if needed, builds the solution, and opens a
writable Garage Draft under `.eutherwire-work/`. The tracked example is never
modified. Other useful modes are:

```sh
./eutherwire.sh check
./eutherwire.sh report
./eutherwire.sh properties
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
dotnet run --project src/EutherWire.Cli -- set-property examples/garage.eutherwire camera-north-cat6:property:installation_status tested
dotnet run --project src/EutherWire.Cli -- validate examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- report examples/garage.eutherwire
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
