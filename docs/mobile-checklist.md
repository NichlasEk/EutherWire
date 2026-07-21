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

The first command prints the signed debug APK path. `mobile-install` installs
it on the USB-connected device. Android's normal APK sideloading also works.

## Field workflow

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
offline status/note/length editing, recovery, and event export. It deliberately
has no camera, gyroscope, AR overlay, photo capture, QR scan, or network sync
yet. The next useful slice is device testing followed by project-linked photo
capture; camera orientation preview remains M2.
