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

Build and run the current native prototype inside a Wayland session:

```sh
dotnet build src/EutherWire.App/EutherWire.App.csproj
dotnet run --project src/EutherWire.App --no-build -- examples/garage.eutherwire
```

Use the middle or right mouse button to pan and the wheel to zoom around the
pointer. Yellow object handles and blue route-vertex handles are draggable.
`DEV`, `WIRE`, `PIPE`, and `TEXT` create real document objects; select an object
to edit its properties or delete it through the inspector.

The CLI uses the same semantic handles as the UI:

```sh
dotnet run --project src/EutherWire.Cli -- handles examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- move examples/garage.eutherwire camera-north-pipe:vertex:1 7000 -3000
dotnet run --project src/EutherWire.Cli -- validate examples/garage.eutherwire
dotnet run --project src/EutherWire.Cli -- insert-vertex examples/garage.eutherwire camera-north-pipe 1 6500 -1000
dotnet run --project src/EutherWire.Cli -- delete-vertex examples/garage.eutherwire camera-north-pipe 1
```

`project.toml` writes are deterministic and atomic. A save/load/save round trip
must produce no diff.

## Status

The repository has entered checkpoint 1: architecture is fixed and the
headless document kernel is being built. See
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
