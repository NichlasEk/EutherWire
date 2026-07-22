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

1. On desktop, create a snapshot:

   ```sh
   ./eutherwire.sh snapshot garage.eutherwire garage.eutherwire-snapshot
   ```

2. Copy the snapshot to the phone and choose `IMPORT SNAPSHOT` in EutherWire.
3. Filter all, unfinished, completed, or blocked tasks.
4. Tap a device, opening, conduit, or cable. Set status, field note, and actual
   length where applicable, then choose `SAVE OFFLINE`.
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
offline status/note/length editing, recovery, and event export. M1.5 now adds
the three-mode shell, numeric room/opening survey, project snapshot export, and
touch-driven 2D/3D review of the measured shell. It deliberately has no camera,
gyroscope, AR overlay, photo capture, QR scan, or network sync yet. The next
useful design slice is handle-driven placement of installation objects; camera
orientation preview remains M2.
