# EutherWire garage 3D plan

Date: 2026-07-19

## Product direction

The 3D garage is an editing view over the same installation document as the
plan view. A camera, conduit, or cable must never be copied into a separate 3D
file that can drift away from the drawing.

The intended workflow is:

1. define or measure the garage volume;
2. choose floor, wall, or ceiling as the active drawing surface;
3. place boxes, devices, and penetrations directly on that surface;
4. route conduit and cable in 3D with X/Y/Z vertex handles;
5. switch to plan view for printable documentation;
6. use installation mode on mobile to measure and confirm the same objects.

## Implemented foundation

- Project schema 6 stores a `SpaceVolume` with origin, width, depth, height,
  wall thickness, and ceiling thickness.
- Room dimensions are editable in the 3D inspector and through stable property
  handles; edits use the shared undo/redo command history.
- Devices store a mounting elevation in millimetres.
- Devices store a named mounting surface: floor, inner/outer ceiling, or one
  of the four inner/outer walls.
- Every cable and conduit vertex stores X, Y, and Z.
- Route lengths include vertical distance.
- Device and route elevations have stable semantic property handles.
- Contained cable geometry follows its conduit in all three dimensions.
- The desktop can switch between PLAN and an isometric 3D garage view.
- The 3D camera supports right-button orbit, middle-button pan, wheel zoom,
  and ISO/NORTH/EAST/SOUTH/WEST presets through the UI or `F10`.
- Surface unprojection follows the rotated camera, so editing remains active
  after changing viewpoint.
- Garage doors, ordinary doors, windows, and penetrations are first-class
  wall-opening kinds with wall surface, 3D centre, width, height, label,
  stable move/property handles, and deterministic TOML/SVG/PNG output.
- Garage Draft contains a 5,000 × 2,200 mm garage door on the south wall.
- The `OPEN` tool has explicit `N`/`S`/`E`/`W` and `INSIDE`/`OUTSIDE` wall
  selectors, then places one garage door, door, window, or penetration before
  automatically returning to `SEL`. Two named 3D corner handles resize width and
  height while preserving the opposite corner; exact dimensions remain
  available in the inspector and through property handles.
- The renderer shows distinct inner and outer wall shells and the ceiling slab.
- The active mounting surface has a yellow 3D outline.
- Existing devices and elevated routes render inside the garage shell.
- Selection and ordinary X/Y handles work in both views.
- Device and route-vertex move handles edit X/Y/Z together in 3D, snap to
  100 mm, propagate connected cable/conduit endpoints, and use undo/redo.
- Selected devices expose exact numeric X/Y/Z fields in the inspector.
- Selected devices expose a dedicated orange vertical elevation handle plus
  100 mm step controls. Moving it keeps X/Y fixed and propagates the new Z to
  connected cable and conduit endpoints through undoable commands.
- `handles` reports X/Y/Z for every stable handle and `move3d` moves a device
  or free route vertex by semantic handle ID, which gives agents the same
  precise editing path as the desktop.
- DEV, WIRE, and PIPE draw on the selected 3D surface with surface-local
  snapping. The symbol palette includes ceiling lights as well as boxes,
  outlets, central equipment, cameras, switches, and access points.
- `./eutherwire.sh 3d` creates and opens a safe writable 3D Garage Draft.
- `./eutherwire.sh wall` opens a safe orthographic wall elevation over the same
  document. It has persistent N/S/E/W and inside/outside selectors, a 500 mm
  grid with 1,000 mm major lines, mounted-object filtering, wall-local route
  rendering, pointer-anchored pan/zoom, selection, placement, and spatial
  handle editing.
- Selected wall devices and openings show exact height above finished floor
  and distance from the nearest visible corner.
- WALL zoom has 23 discrete levels from 20% to 3,200% so both whole-wall
  overview and small penetration details remain practical.
- WALL masks its metric grid inside openings, giving doors, windows, garage
  doors, and penetrations a clear cutout instead of drawing them over the wall.
- Persistent FREE/300/1100/2200/2400 mm mounting profiles snap new devices,
  new wall routes, and device drag handles to common finished-floor heights.
- Every editable route vertex has a stable indexed one-axis elevation handle,
  such as `camera-north-pipe:elevation:1`. Orange vertical handles render in
  WALL and 3D, preserve vertex X/Y, propagate conduit height into contained
  cables, and use the shared undo/redo command path.

The current renderer is deliberately a clear technical installation view, not
a photorealistic game renderer. It uses WaylandForge's deterministic software
canvas and remains useful on systems without a GPU API configured.

## Next 3D slices

### Electrical cable contents

The structured cable/conductor model is specified in
[`electrical-cable-model-plan.md`](electrical-cable-model-plan.md). It covers
CAT6, EKRK, RK/FK, conductor area, conductor count, L1/L2/L3, neutral,
protective earth, switched lives, circuit presets, conduit fill, validation,
and material-list output. Its implementation follows multi-selection and
duplication so repeated outlets and devices can be planned efficiently first.

### Height and surface handles

- wall-local horizontal and vertical handles;
- snap to floor, ceiling, corners, existing routes, ports, and configured
  installation heights;
- larger touch hit targets and mobile gestures for the implemented one-axis
  route Z handles.

All of these remain command-based and addressable without screen coordinates.

### Wall elevations

The first wall-elevation slice is implemented. Persistent dimension annotations
can now be created with two clicks, repositioned through stable endpoint
handles, and saved in schema 6. The next refinement is printable wall-elevation
export and snap profiles tied to symbol/library metadata. Indexed route Z
handles and the first manual common-height profile row are available in WALL.
Tool-local pointer hints expose multi-click progress and `Esc` cancellation,
while larger handle hit targets make WALL editing less dependent on
pixel-perfect mouse control and provide a starting point for touch interaction.

### Garage geometry

- beams, pillars, workbench, and vehicle clearance volumes;
- imported measured floor plan as a calibration layer;
- extend WALL's opening masks into future filled 3D wall surfaces;
- collision and clearance diagnostics;
- translucent wall hiding so routes behind a wall remain understandable.

### Camera navigation

- orbit around the pointer or selected object rather than only the room centre;
- top and selected-surface presets;
- section box for inspecting concealed routes;
- saved named viewpoints for installation photos and documentation.

### Mobile installation mode

The phone view will consume the same 3D coordinates. Selecting a wall or
scanning an object label can show height, offsets from corners, route direction,
actual cable length, status, and attached field photos without translating from
a second drawing. The staged camera, gyroscope, calibration, offline checklist,
and field-verification design lives in
[mobile-installation-ar-plan.md](mobile-installation-ar-plan.md).

## Acceptance target

The first complete 3D milestone is reached when a user can create a garage,
place a camera on a wall at an exact height, route a conduit along wall and
ceiling with a vertical drop, reopen the project without geometry drift, and
produce matching plan, 3D, SVG, and PNG documentation from the same objects.
