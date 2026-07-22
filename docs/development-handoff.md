# Development handoff

Last updated: 2026-07-22

## Safe checkpoint

- Branch: `main`
- Latest implementation commit: `1be2042 Add offline mobile field photos`.
- GitHub is pushed through `1be2042`.
- The only remaining worktree change is the pre-existing modified
  `vendor/WaylandForge` submodule. It belongs to separate work and must not be
  staged, reset, or overwritten while continuing EutherWire.
- Android emulator `pixel_x86_64` was stopped cleanly after testing.

## Current product state

EutherWire Mobile has three connected field modes over the shared document:

1. Survey creates room dimensions and fixed openings.
2. Design places devices, draws typed cables and 16/20/25 mm conduits, snaps
   cables to real ports, and edits routes with large touch handles in 2D/3D.
3. Install exposes devices, openings, conduits, and cables as offline tasks.

Mobile version 0.1.4 added cable-to-conduit assignment. In the cable editor,
`RUN CABLE IN` assigns the cable to a real conduit ID. The cable adopts the
conduit geometry and follows later conduit vertex changes through the shared
document command layer. Assigned cable routes do not expose independent vertex
handles because their geometry is controlled by the conduit.

Selecting a conduit now shows:

- contained cable labels;
- physical fill and a progress bar against the 40% planning threshold;
- thermal state: `PASS`, `WARNING`, `VERIFY GROUPING`, `UNKNOWN`, or `N/A`;
- route length and real nominal/inner conduit profile.

The analysis deliberately reports `UNKNOWN` when traceable sizing inputs are
missing. It must never infer that an installation is approved merely from cable
area and conduit size.

Mobile version 0.1.5 adds `TRACEABLE CIRCUIT DESIGN` to the power-cable dialog.
It persists design current `Ib`, protective-device current `In` and
characteristic, loaded conductor count, verified reference current-carrying
capacity, ambient/grouping/thermal-insulation factors, and a human-readable
reference source through the shared `CircuitDesign` model. The dialog previews
the corrected `Ib <= In <= Iz` relation live: green is a complete pass, red is
a complete failure/warning, and yellow is incomplete/unknown. Editing these
values preserves the existing cable product and conductor list.

Mobile version 0.1.6 adds project-linked field evidence to every installation
task. `CHOOSE PHOTO` uses Android's document picker without broad storage or
camera permission. `TAKE PHOTO` delegates to an installed camera app through a
scoped AndroidX FileProvider. Imported bytes are stored under `photos/` with a
stable object-ID/content-hash name, referenced through the shared installation
command and durable event journal, and shown as an inline latest-photo preview.
Selecting identical bytes again is a no-op rather than another revision.

## Verified state

The full solution built with zero warnings and zero errors. The expanded
document check suite passed. It covers complete PASS and WARNING relations,
incomplete evidence remaining UNKNOWN, conduit assignment preserving detailed
electrical design, grouped power-circuit diagnostics, and TOML round-trip of
every design input.

Android x86_64 emulator testing verified Mobile 0.1.6, version code 7:

- the system image picker attached a real 1.43 MB PNG to `KAM-N`;
- the copied project file had the same SHA-256 as the selected source;
- project TOML and both pending/durable journals carried the project-relative
  reference through the shared installation event;
- the task showed `1 PHOTO ATTACHED` plus a decoded inline preview;
- a force-stop/restart retained the reference and preview;
- choosing the identical source again kept one reference and one journal line;
- the emulator image had no camera app, and the explicit unavailable-camera
  path returned safely without a fatal exception;
- the manifest requested neither `CAMERA` nor `INTERNET` permission;
- no fatal exception or `UnsatisfiedLinkError` appeared.

The document checks additionally verify collision-safe imports, byte-identical
deduplication, path-escape rejection, photo bytes across snapshot export/import,
and rejection when a referenced photo is missing. Existing checks cover
snapshot hash tampering and duplicate journal replay.

The earlier Mobile 0.1.5 electrical emulator verification remains valid:

- the app started without a fatal exception;
- an EKRK 3G2.5 cable initially showed `THERMAL UNKNOWN`;
- all nine traceability inputs rendered in the scrollable cable dialog;
- blank inputs showed yellow `UNKNOWN / INCOMPLETE EVIDENCE`;
- entering Ib 13 A, In 16 A, characteristic B, two loaded conductors,
  reference capacity 20 A, factors 1/1/1, and source `SEK Handbok 421`
  immediately showed green `PASS`;
- SAVE persisted every entered field under `[cables.design]` in project TOML;
- no fatal exception or `UnsatisfiedLinkError` appeared.

The previous conduit/fill emulator verification remains valid:

- assigning `EKRK 3G2.5-01` to `RÖR-01`;
- persisted `conduit = "69b8eaeb97e741f7925c33c34aa10366"` in project TOML;
- cable route geometry changed to the conduit geometry;
- the 20 mm conduit reported 40.1% fill, above the 40% planning threshold;
- the fill warning rendered red;
- the power cable reported `THERMAL UNKNOWN` because Ib/In/Iz evidence was not
  supplied;
- no app fatal exception occurred.

The locally signed ARM64 field-test artifact is Mobile 0.1.6, version code 7:

- SHA-256: `e8c45b4b8b970f71a2e7f42da3d47fa7e355de9c1e6d2e109a5740ad9d51e933`
- Size: `90773290` bytes
- Local path:
  `src/EutherWire.Mobile/bin/Debug/net10.0-android/android-arm64/publish/se.eutherwire.mobile-Signed.apk`
- Public Apps URL:
  `https://apothictech.se/downloads/EutherWire-0.1.0-debug.apk`
- Server backup of 0.1.5:
  `/home/nichlas/EutherWire-0.1.5-circuit-design.previous.apk`

The public file was replaced atomically on 2026-07-22 after the server staging
hash matched the local artifact. The active server path has the matching
SHA-256 and 90,773,290-byte length; the backup has the previous 0.1.5 hash and
81,304,064-byte length. A public HTTPS GET returned HTTP 200, Android APK MIME
type, the expected attachment filename, no-store caching, and the matching
90,773,290-byte content length. The first 9,388,032 public bytes also matched
the local artifact byte-for-byte. Two attempted full public downloads were
reset/truncated by the Cloudflare-facing route, so the full hash was verified
on the active server file rather than claimed from the external transfer.

## Next implementation slice

Begin the narrow calibrated camera/orientation preview from
`mobile-installation-ar-plan.md`. Keep project geometry authoritative and make
calibration quality visible: manual room origin and heading first, phone
orientation driving a simplified 3D view, and selected-object direction and
distance guidance. Do not imply centimetre accuracy from uncalibrated sensors.

Before expanding the slice, verify full `TAKE PHOTO` capture/cancel behavior on
a physical Android phone because the current x86_64 emulator image does not
include a camera activity.

## Resume and verify

From the repository root:

```sh
git status --short
git log -3 --oneline
dotnet restore EutherWire.slnx --nologo --disable-build-servers -m:1
dotnet build EutherWire.slnx --nologo --no-restore --disable-build-servers -m:1
dotnet run --no-build --project tests/EutherWire.Document.Checks
```

Build the phone artifact with:

```sh
./eutherwire.sh mobile-build
```

Keep commits narrow and push each completed working slice. Never include the
dirty `vendor/WaylandForge` submodule in an EutherWire checkpoint.
