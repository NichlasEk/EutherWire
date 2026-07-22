# Development handoff

Last updated: 2026-07-22

## Safe checkpoint

- Branch: `main`
- Latest implementation commit: `39ad4c4 Add mobile circuit design inputs`
- GitHub is pushed through `39ad4c4`.
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

## Verified state

The full solution built with zero warnings and zero errors. The expanded
document check suite passed. It covers complete PASS and WARNING relations,
incomplete evidence remaining UNKNOWN, conduit assignment preserving detailed
electrical design, grouped power-circuit diagnostics, and TOML round-trip of
every design input.

Android x86_64 emulator testing verified Mobile 0.1.5, version code 6:

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

The locally verified signed ARM64 field-test artifact is Mobile 0.1.5, version
code 6:

- SHA-256: `bb179ed23f7c025059292732db262b0d7ae05f1ad965342407cebad241f2012a`
- Size: `81304064` bytes
- Local path:
  `src/EutherWire.Mobile/bin/Debug/net10.0-android/android-arm64/publish/se.eutherwire.mobile-Signed.apk`

This 0.1.5 artifact has not yet replaced the public 0.1.4 Apps download. Do not
claim public deployment until the server file, MIME type, byte length, and
hash have been checked.

## Next implementation slice

Implement project-linked field photo capture. Reuse schema 12 installation
records and their existing photo-reference list rather than adding a mobile-only
attachment model.

The first narrow slice should:

- capture or choose a photo from an object/task detail view;
- store it under the active project with a stable collision-safe file name;
- add a project-relative photo reference through the shared installation
  command/journal path;
- show attached-photo count and a simple preview/open action on that same task;
- include the file in portable snapshot export and prove integrity checking on
  import;
- remain fully offline and avoid camera permission when the user only chooses
  an existing image;
- handle cancelled capture, missing files, duplicate event replay, and app
  restart without losing the reference.

After field-photo capture, continue with calibrated camera/gyroscope guidance.

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
