# Mobile installation and AR plan

Date: 2026-07-19

## Goal

Installation mode turns the EutherWire model into a spatial checklist over the
real garage. A phone user can walk through the building with the camera open,
see where a box, lamp, conduit, or cable is planned, open the corresponding
installation task, record the actual result, and mark it installed or tested.

The mobile view must use the same object IDs, 3D coordinates, mounting
surfaces, and installation states as the desktop. It must not create a second
AR-only drawing that can drift away from the project.

## Product modes

The phone starts with three explicit modes over the same project model:

1. **Survey / Measure** is the first-run mode. Create a room by entering its
   length, width, wall height, ceiling shape, and wall thickness. Then add and
   measure every fixed opening and boundary feature: doors, garage doors,
   windows, passages, penetrations, floor level changes, and known service
   entries. The app shows a completeness checklist before the room becomes the
   planning baseline.
2. **Design / Plan** uses that measured shell to place everything that does not
   exist yet: boards, boxes, outlets, lights, conduits, cables, network devices,
   and routes. It edits the same document as the desktop renderer; users can
   move between phone and desktop without conversion or duplicate drawings.
3. **Install / Verify** locks ordinary plan geometry, guides the user through
   planned objects with camera and orientation sensors, and records installed,
   changed, blocked, and tested states together with actual measurements and
   evidence.

Later specialist modes such as inspection or maintenance may reuse the same
objects and event journal. They must not fork the room model.

The first survey implementation should be numeric and handle-driven rather
than pretending that a phone camera is a precision rangefinder. Camera-assisted
measurement may propose dimensions later, but the user confirms the values.

## Field workflow

1. Measure or open the room shell on the phone.
2. Confirm fixed openings and service entries, then plan the missing objects.
3. Choose the room and start installation mode.
4. Calibrate the virtual room against known physical anchors.
5. Walk around while camera motion and phone orientation update the view.
6. See planned objects and routes overlaid on walls, floor, and ceiling.
7. Tap an overlay to see label, dimensions, offsets, cable endpoints, and task
   status.
8. Mark the task installed, changed, blocked, or tested.
9. Record actual cable length, notes, and optional before/after photographs.
10. Synchronize those changes back into the same EutherWire project.

## Calibration

Gyroscope orientation alone is not enough to place an object accurately on a
wall. The first usable version combines phone motion tracking with explicit
project anchors.

Initial calibration can use three or more known points, for example:

- the lower-left and lower-right corners of the garage door;
- a measured floor corner;
- the centre of the distribution board;
- a printed EutherWire marker attached at a known project coordinate.

Each anchor binds a physical camera observation to an EutherWire `Point3`.
The app then solves the transform between phone tracking space and project
space. It displays calibration confidence and asks for recalibration when
tracking drift becomes too large.

AR overlays are installation guidance, not certified measurement instruments.
Exact drilling positions remain backed by numeric offsets and dimensions from
known building surfaces.

## Overlay model

The mobile renderer should distinguish:

- planned objects: cyan outline;
- selected installation task: yellow;
- installed objects: green;
- tested objects: solid green with check mark;
- changed in field: orange;
- blocked or conflicting: red;
- concealed conduit and cable: dashed or translucent;
- objects behind the current wall: hidden unless an X-ray toggle is active.

Labels should remain readable without covering the physical mounting point.
Distance-based simplification can hide ports and minor labels until the user
approaches or selects the object.

## Installation records

Implemented in desktop document schema 12: every device, opening, conduit, and
cable owns an installation record addressed by its stable object ID. It
contains:

```text
object_id
status
updated_at
actual_position_or_offset (optional)
actual_length_mm (optional)
note (optional)
photo_references (optional)
test_result (optional)
```

Calibration session IDs belong to future AR capture events rather than the
base installation record. Synchronization now uses an append-only
`installation-events.jsonl` beside the project. Each event carries a unique ID
and the object revision observed by its author. Reapplying an ID is harmless;
an event from an older revision is retained and reported as a conflict without
overwriting current work.

## Privacy and offline operation

Camera frames should stay on the phone by default. A project may store photos
only when the user explicitly captures them. Installation mode must remain
useful without internet access; project snapshots and pending task events can
synchronize when a local EutherWire host becomes reachable again.

## Delivery milestones

### M1: Mobile checklist

- implemented: native responsive task list using existing installation states;
- implemented: object details, planned dimensions, and actual cable length;
- implemented: offline status/note/length changes and JSONL synchronization;
- implemented: interruption recovery and idempotent pending queue;
- intentionally no camera overlay yet.

### M1.5: Survey shell

- implemented: mode selector for Survey, Design, and Install;
- implemented: create a room from length, width, wall height, wall thickness,
  and ceiling thickness;
- implemented: place doors, garage doors, windows, and penetrations on a selected
  wall using exact width, height, sill, and corner offset;
- implemented: survey completeness checklist and full portable snapshot export;
- implemented: save through the shared command/document model so measured
  openings immediately become installation tasks;
- implemented: review the measured room as a touch-driven 2D plan and rotatable
  3D shell with labelled, colour-coded fixed openings;
- implemented: place common installation objects with smart surface/elevation
  defaults, exact XYZ fallback fields, and large constrained 2D move handles;
- implemented: render placed objects in 2D/3D and expose them immediately as
  installation tasks through the shared document model;
- implemented: draw typed cables and real 16/20/25 mm conduit profiles with
  touch-friendly bend points and snapping to actual device ports;
- implemented: select complete routes, drag numbered vertices in 100 mm steps,
  edit metadata, delete routes, render them in 2D/3D, and list them in Install;
- implemented: assign cables to conduits so they share route geometry and remain
  synchronized when conduit vertices move;
- implemented: show conduit contents, physical fill against the 40% planning
  limit, grouped-circuit warnings, and honest pass/warning/unknown thermal state;
- implemented: enter traceable circuit current, protective-device current and
  characteristic, loaded conductor count, correction factors, verified
  reference capacity, and source with a live corrected `Ib <= In <= Iz` result;
- implemented: choose or capture project-linked field photos, store them under
  collision-safe content-addressed names, journal their references offline,
  preview them on the task, and include them in integrity-checked snapshots;
- next: begin calibrated camera/orientation guidance without claiming spatial
  precision that the phone has not established.

### M2: Camera orientation preview

- live camera background;
- phone orientation controls a simplified 3D project view;
- manual room origin and heading calibration;
- selected-object direction and distance guidance.

### M3: Anchored room overlay

- three-point or marker-assisted calibration;
- stable wall, floor, and ceiling overlays;
- calibration confidence and drift warning;
- tap overlays to open their installation tasks.

### M4: Field verification

- before/after photographs linked to object IDs;
- measured offsets and actual positions;
- changed-versus-plan visualization on desktop;
- tested state, notes, and installation report export.

### M5: Assisted survey

- capture additional room anchors while walking;
- compare observed surfaces with configured room dimensions;
- propose, but never silently apply, corrections to walls and object positions;
- support multiple rooms and saved calibration sessions.

## Desktop prerequisites

The desktop/model foundations now present are:

- doors, windows, garage doors, and penetrations as real geometry;
- wall elevation views and reliable room-local coordinates;
- camera orbit;
- exact dimensions and corner offsets;
- a portable project snapshot format.

Saved viewpoints remain useful before anchored AR but do not block the first
offline checklist. Beams need their own geometry if the actual installation
requires them; doors, windows, garage doors, and penetrations already share the
opening model.

## First acceptance target

Standing in the calibrated garage, a user can point the phone at the north
wall, see a planned junction box at the correct wall and approximate height,
tap it, read exact offsets from floor and corner, photograph the completed
installation, mark it installed, and see that same status on the desktop
without creating a duplicate object.
