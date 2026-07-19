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

## Field workflow

1. Open an EutherWire project on the phone.
2. Choose the room and start installation mode.
3. Calibrate the virtual room against known physical anchors.
4. Walk around while camera motion and phone orientation update the view.
5. See planned objects and routes overlaid on walls, floor, and ceiling.
6. Tap an overlay to see label, dimensions, offsets, cable endpoints, and task
   status.
7. Mark the task installed, changed, blocked, or tested.
8. Record actual cable length, notes, and optional before/after photographs.
9. Synchronize those changes back into the same EutherWire project.

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

Every field update targets a stable document object ID. A mobile installation
record should contain at least:

```text
object_id
status
timestamp
actual_position_or_offset (optional)
actual_length_mm (optional)
note (optional)
photo_references (optional)
calibration_session_id
```

The first synchronization format can be an append-only event log beside the
project. Desktop code applies those events through the same document commands
used by the inspector and CLI. This preserves undoability and makes conflicting
offline edits detectable.

## Privacy and offline operation

Camera frames should stay on the phone by default. A project may store photos
only when the user explicitly captures them. Installation mode must remain
useful without internet access; project snapshots and pending task events can
synchronize when a local EutherWire host becomes reachable again.

## Delivery milestones

### M1: Mobile checklist

- responsive task list using existing installation states;
- object details, planned dimensions, and actual cable length;
- offline status changes and synchronization;
- no camera overlay yet.

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

Before anchored AR is worth implementing, the desktop model needs:

- doors, windows, garage doors, beams, and penetrations as real geometry;
- wall elevation views and reliable room-local coordinates;
- camera orbit and saved viewpoints;
- exact dimensions and corner offsets;
- a portable project snapshot plus installation-event interchange format.

## First acceptance target

Standing in the calibrated garage, a user can point the phone at the north
wall, see a planned junction box at the correct wall and approximate height,
tap it, read exact offsets from floor and corner, photograph the completed
installation, mark it installed, and see that same status on the desktop
without creating a duplicate object.
