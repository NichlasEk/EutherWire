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

- Project schema 5 stores a `SpaceVolume` with origin, width, depth, height,
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

The current renderer is deliberately a clear technical installation view, not
a photorealistic game renderer. It uses WaylandForge's deterministic software
canvas and remains useful on systems without a GPU API configured.

## Next 3D slices

### Height and surface handles

- wall-local horizontal and vertical handles;
- snap to floor, ceiling, corners, existing routes, ports, and configured
  installation heights;
- explicit one-axis Z handles for route vertices and touch use.

All of these remain command-based and addressable without screen coordinates.

### Garage geometry

- beams, pillars, workbench, and vehicle clearance volumes;
- imported measured floor plan as a calibration layer;
- subtract opening areas from filled wall rendering;
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
