# EutherWire development plan

Date: 2026-07-19

## Goal

Build a focused WaylandForge-native installation drawing tool for electrical,
network, cable, conduit, and future home-technology planning. EutherWire is not
intended to become a general-purpose CAD system.

The first real project is **Garage Draft**: a distribution board, garage PoE
switch, 25 mm conduits, camera boxes, devices, and routed CAT6 cables.

## Verified starting point

The current WaylandForge checkout already provides:

- a low-level Wayland client and `wl_shm` ARGB8888 presentation;
- pointer and keyboard input;
- frame-callback-driven repaint and resize handling;
- a reusable software canvas with lines, rectangles, text, clipping, and image
  blitting;
- immediate-mode panels, buttons, text boxes, scrolling, themes, and a file
  picker.

It does **not** yet provide a reusable application host. The Wayland window,
native build, main loop, and input plumbing are internal to the emulator host,
whose executable also depends on Saturn and external-core projects. EutherWire
must not reference that executable as its GUI framework.

## Checkpoint 0: make WaylandForge application-ready

Extract a small reusable WaylandForge app boundary, ideally as projects owned
by the WaylandForge repository:

```text
SystemRegisIII.WaylandForge.Native
SystemRegisIII.WaylandForge.App
SystemRegisIII.WaylandForge.Ui
```

The app API only needs to expose:

```csharp
public interface IForgeApplication
{
    void Resize(int width, int height);
    void Pointer(in ForgePointerEvent input);
    void Key(in ForgeKeyEvent input);
    void Render(SoftwareCanvas canvas, TimeSpan elapsed);
}
```

The exact API may differ, but ownership must remain clear:

- WaylandForge owns the Wayland connection, buffers, frame pacing, input, and
  native protocol glue.
- EutherWire owns document state, tools, camera transforms, drawing, commands,
  persistence, and product UI.

Acceptance criteria:

- a separate minimal sample opens a WaylandForge window without depending on
  emulator or game projects;
- resize, pointer, keyboard, wheel, clipboard-ready text input, and close are
  delivered through a stable app API;
- the reusable projects build from a clean checkout;
- EutherWire consumes a pinned WaylandForge version rather than copying source
  files or relying permanently on a sibling-directory path.

WaylandForge is currently pinned under `vendor/WaylandForge` as a Git submodule.
This gives clean clones an exact framework revision while its reusable app host
is extracted. The EutherWire document model must not depend on the submodule.

## Checkpoint 1: headless document kernel

Build this independently of Wayland:

```text
src/EutherWire.Document/
  Model/
  Geometry/
  Commands/
  Serialization/
  Migrations/
tests/EutherWire.Document.Tests/
```

Initial entities:

- `ProjectDocument`, drawing sheets, and layers;
- `Device`, `Port`, `SymbolInstance`, and text annotations;
- `Polyline`, `CableRoute`, and `Conduit`;
- typed connections between stable object/port IDs;
- unit-aware positions and dimensions in millimetres;
- schema version and project metadata.

All edits go through commands. The first commands are add, remove, move,
rotate, change property, and replace polyline points. Commands must support
undo and redo and preserve stable IDs.

Persistence target:

```text
garage.eutherwire/
  project.toml
  symbols/
  assets/
  autosave/
  exports/
```

TOML is the human-facing format. Parsing must reject dangling IDs and invalid
geometry with useful diagnostics. Round trips must be deterministic so a save
without changes produces no diff.

Acceptance criteria:

- create Garage Draft in memory;
- save, load, validate, and save it byte-identically;
- move a device, undo, and redo with deterministic state hashes;
- calculate exact polyline lengths in document units;
- test duplicate IDs, missing ports, and malformed coordinates.

## Checkpoint 2: first native canvas

The first GUI deliberately contains only:

- a WaylandForge window;
- central canvas, left tool strip, right inspector, and status bar;
- document-to-screen camera transform;
- pan and zoom around the pointer;
- adaptive grid and snapping;
- select, place symbol, move, rotate, polyline, and text tools;
- multi-selection and basic hit testing;
- save/load Garage Draft;
- deterministic PNG export.

Render order:

1. background;
2. grid;
3. imported floorplan;
4. conduits;
5. cable routes;
6. devices and ports;
7. labels;
8. selection and active-tool overlays;
9. application UI.

Polyline routing starts with free, 45-degree, and 90-degree segments. Curves,
automatic routing, and a spatial index wait until real project size proves they
are needed.

Acceptance criteria:

- place and manipulate at least six Garage Draft symbols;
- route and edit a conduit polyline;
- close and reopen without data loss;
- zoom remains anchored under the pointer;
- rendering the same document and viewport produces the same pixel hash.

Deterministic document-space SVG and PNG export are implemented. PNG uses the
shared WaylandForge software canvas headlessly and is available from the CLI,
helper script, and desktop inspector. Byte-determinism is covered by the
document checks and Garage Draft has a committed SHA-256 reference render hash.

## Checkpoint 3: intelligent connections

- typed device ports;
- cables connected from port to port;
- conduits containing cable IDs;
- cable and conduit inspector forms;
- length plus configurable slack;
- compatibility checks and visible diagnostics;
- material list for the Garage Draft project.

The initial rule engine reports, but does not silently repair:

- dangling cable ends;
- incompatible port/cable types;
- duplicate labels;
- cables assigned to missing conduits;
- overfilled conduits;
- PoE devices connected to non-PoE sources.

The first analysis slice is implemented in the shared document library and is
available both in the desktop inspector and through `eutherwire report`.
Conduit fill currently uses an explicit approximate outside-diameter catalogue
for planning; product-specific cable data remains to be added before the result
can be treated as an installation calculation. Cable
slack and service-loop margins are project data rather than constants, while
each cable can carry a planned/installed/tested/changed/blocked state and its
measured installed length.

## Later milestones

- imported PNG floorplans, measurement, and scale calibration;
- SVG/PDF export after the internal vector scene is stable;
- plan and schematic views over the same model;
- symbol editor and user libraries;
- autosave journal and crash recovery;
- local-agent editing through the versioned project format.

The 3D garage has moved forward from a later visualization idea into an active
product track. Schema 3, spatial route vertices, device elevations, garage
volume, and the first editable isometric view are described in
[garage-3d-plan.md](garage-3d-plan.md).

## Mobile installation mode

Mobile is a first-class field workflow over the same project model, not a
scaled-down desktop editor. It comes after document synchronization and the
desktop editing kernel are stable.

Installation mode prioritises:

- large touch targets and one-handed navigation;
- room/route/device task lists generated from the drawing;
- mark planned objects as installed, tested, changed, or blocked;
- scan QR/barcode cable and device labels;
- capture photos, voice notes, measurements, and as-built deviations at the
  selected object;
- enter actual cable length, reserve, port, breaker, and test result;
- work fully offline on site, then merge an append-only field journal;
- show only installation-safe actions by default so a stray touch cannot alter
  the underlying plan geometry;
- optional laser/rangefinder and camera-assisted measurements later.

The shared analysis layer now emits installation tasks with stable object IDs
for devices, openings, conduits, and cables. Schema 12 keeps common status,
timestamp, note, actual position/length, test result, and photo references in
one installation record. Desktop properties and `eutherwire tasks` consume the
same model; the mobile UI does not need to invent a separate task database.

The append-only JSONL event journal is implemented with unique event IDs,
per-object base revisions, persisted idempotency, and non-destructive conflict
reporting. Deterministic portable snapshot import/export now packages the
canonical project, journal, and referenced evidence with integrity checks. The
data-layer prerequisites are complete. The native Android M1 checklist now
imports snapshots, persists/filter tasks, edits status/notes/actual lengths
offline, recovers pending events, and exports JSONL for desktop merge. Physical
device testing remains the first verification step. The mobile Survey shell is
now implemented with Survey, Design, and Install modes; numeric room, wall, and
ceiling dimensions; fixed openings by wall/offset/sill; portable project export;
and a touch-driven 2D plan plus rotatable 3D shell review. Measured openings
immediately appear as installation tasks. Mobile Design now also places common
devices with mounting-surface constraints, exact XYZ fields, and large 2D move
handles; the same objects render in 3D and appear in Install. Mobile Design now
also draws typed cables and 16/20/25 mm conduits, snaps cable endpoints to real
ports, and edits saved routes through large numbered vertices. Routes render in
2D/3D and appear in Install. Cables can now be assigned to conduits, inherit
their geometry, and feed live conduit-content, physical-fill, grouping, and
thermal-status feedback in Mobile Design. Power cable dialogs now persist
traceable Ib/In/Iz evidence, protection characteristic, loaded conductors,
correction factors, and source through the shared electrical model while
previewing the corrected relation live. Project-linked photo capture is the
next product slice; camera/gyro guidance follows that measured baseline.

The mobile client should share document, validation, command, and migration
code with desktop. WaylandForge remains the Linux desktop surface; mobile gets
a platform-specific shell around the same core instead of forcing a Wayland
window onto Android.

## Symbol library: first pack

- distribution board and sub-board;
- junction and apparatus boxes;
- wall outlet and switch;
- network outlet, camera, access point, router, PoE switch, and patch panel;
- server, cable entry, buried conduit, and spare conduit.

Each definition owns vector geometry, nominal size, anchor, ports, default label
position, and a typed property schema. Inspector forms are generated from that
schema rather than hardcoded per symbol.

## Decisions made now

- Product and repository name: **EutherWire**.
- Runtime: .NET 10 on Linux/Wayland.
- UI/render host: WaylandForge, after extraction of a reusable app boundary.
- Project coordinates: millimetres stored independently from screen pixels.
- Project format: versioned TOML directory format.
- Editing: command-based from the first implementation.
- Every editable feature exposes stable semantic handles shared by mouse,
  touch, keyboard commands, and agent-driven edits. Handles identify roles such
  as `move`, `rotate`, `port:eth0`, and `vertex:2`; screen pixels are never part
  of their identity. Non-geometric values use property handles such as
  `camera-north-cat6:property:installation_status`, with discoverable types and
  allowed choices.
- First proving document: Garage Draft.
- No general CAD scope, Bezier router, huge IEC library, AI layer, or schematic
  auto-layout before the core editing loop is proven.

## Immediate next work

1. Extract and verify the reusable WaylandForge application host.
2. Create the EutherWire solution and headless document project.
3. Land stable IDs, geometry primitives, commands, and deterministic tests.
4. Add the minimal WaylandForge window and camera/grid canvas.
5. Encode Garage Draft as the first end-to-end fixture.
