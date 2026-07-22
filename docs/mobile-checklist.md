# Android installation checklist

EutherWire Mobile M1 is a native .NET Android app using the same document,
analysis, snapshot, and synchronization assemblies as the desktop. It needs no
network connection and requests no broad storage permission.

## Build and install

The repository expects the .NET 10 Android workload, Android SDK, Java 17 or
newer, and optionally `adb`. On the current development machine:

```sh
./eutherwire.sh mobile-build
./eutherwire.sh mobile-install
```

The first command builds a complete ARM64 test APK with all managed runtime
assemblies embedded, signs it with the machine's stable Android debug key,
verifies the APK signature with Android's own `apksigner`, and prints the path.
`mobile-install` installs it on the USB-connected device. Android's normal APK
sideloading also works. The complete package is intentionally larger than an
optimized store build, but is the reliable field-test artifact. This test
signature supports repeatable updates from this development machine; a
protected EutherWire release key replaces it before production distribution.

## Field workflow

The app opens in `SURVEY`. A new project can be measured directly on the phone:
enter room length, width, wall height, wall thickness, and ceiling thickness,
then add each door, garage door, window, and penetration by wall, corner offset,
sill, width, and height. `EXPORT PROJECT` writes a complete portable snapshot
that opens in the desktop app. Every measured opening uses the shared document
model and therefore also appears automatically under `INSTALL`.

`DESIGN` shows the measured shell as both a 2D plan and a simple 3D room. Drag
to pan the plan or rotate the room, pinch to zoom, and use `RESET VIEW` to
recover the fitted overview. Doors, garage doors, windows, and penetrations are
colour-coded and labelled in both views.

Use `+ PLACE DEVICE` to add a junction box, outlet, distribution board, light,
camera, PoE switch, or access point. The form suggests a useful mounting surface
and elevation, while allowing exact X/Y/Z input. Tap the resulting symbol in the
2D plan and drag its large yellow handle to move it in 100 mm steps. A mounted
object remains constrained to its wall, floor, or ceiling and is saved when the
drag ends. `EDIT SELECTED` changes its label, mounting surface, or exact position
and can delete it. The object is immediately available as a planned task in
`INSTALL` and is also rendered in the 3D review.

Use `+ DRAW CABLE` to choose CAT6, CAT6A, EKRK, fibre, coax, or 12 V DC.
Available device ports turn green: tap the source port, add any free bend
points, and tap the target port to finish. Use `+ DRAW CONDUIT` to choose a
16, 20, or 25 mm conduit and tap at least two route points. `UNDO POINT`,
`CANCEL ROUTE`, and `FINISH ROUTE` keep unfinished work explicit. Tap a saved
route to select it, then drag its numbered yellow vertices; each point snaps to
100 mm and is persisted when released. `EDIT SELECTED` changes route metadata
or deletes the whole route. Cables and conduits render in both plan and 3D and
appear as planned tasks in `INSTALL`.

To place a cable inside a conduit, select the cable, choose `EDIT SELECTED`, and
set `RUN CABLE IN`. The cable adopts the conduit route and follows later conduit
vertex changes. Selecting that conduit shows its cable contents, a fill bar and
the physical fill percentage against the 40% planning limit. Thermal feedback
is deliberately explicit: `PASS` and `WARNING` require traceable Ib/In/Iz data;
`VERIFY GROUPING` flags multiple power circuits; `UNKNOWN` means the electrician
still needs to supply or verify sizing inputs. This is planning support, not an
automatic approval of an electrical installation.

For an EKRK power cable, the same dialog exposes `TRACEABLE CIRCUIT DESIGN`.
Enter `Ib`, `In`, protective characteristic, loaded conductor count, verified
reference capacity, all three correction factors, and a recognizable source.
The relation at the bottom updates live: green is a complete pass, red is a
complete warning, and yellow means incomplete/unknown evidence. Saving changes
only the shared `CircuitDesign`; it does not replace the cable product or its
conductors. Reopen the cable after saving to confirm the values persisted.

1. On desktop, create a snapshot:

   ```sh
   ./eutherwire.sh snapshot garage.eutherwire garage.eutherwire-snapshot
   ```

2. Copy the snapshot to the phone and choose `IMPORT SNAPSHOT` in EutherWire.
3. Filter all, unfinished, completed, or blocked tasks.
4. Tap a device, opening, conduit, or cable. Set status, field note, and actual
   length where applicable. Use `CHOOSE PHOTO` or `TAKE PHOTO` to attach field
   evidence; the task immediately shows its photo count and latest preview.
   Then choose `SAVE OFFLINE` for any remaining form edits.
5. Choose `EXPORT EVENTS` and save the pending JSONL file.
6. On desktop, merge it:

   ```sh
   dotnet run --project src/EutherWire.Cli -- journal-apply \
     garage.eutherwire phone-events.jsonl
   ```

7. Export a refreshed snapshot and import it on the phone. The fresh project
   has the merged revisions and no phone-local pending queue.

The phone app writes each change to its pending journal before applying it to
the local project. Startup replays interrupted pending work idempotently. An
event exported more than once is harmless on desktop, and stale revisions are
reported as conflicts instead of overwriting another installer's work.

## Current M1 boundary

M1 contains snapshot import, persistent task overview, filters, task details,
offline status/note/length/photo editing, recovery, and event export. M1.5 now adds
the three-mode shell, numeric room/opening survey, project snapshot export,
touch-driven 2D/3D review, device placement, and typed cable/conduit routing
with port snapping and editable vertices. It deliberately has no live camera
overlay, gyroscope guidance, AR overlay, QR scan, or network sync yet. Cable-to-
conduit assignment, live fill/thermal feedback, and traceable mobile circuit-
design input are implemented. Project-linked field photo choosing/capture,
content-addressed project storage, offline journaling, task preview, and
snapshot integrity are implemented. Calibrated camera orientation preview is
next in M2.
