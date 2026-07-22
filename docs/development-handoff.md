# Development handoff

Last updated: 2026-07-22

## Safe checkpoint

- Branch: `main`
- Latest implementation commit: `dba3237 Add mobile conduit fill planning`
- GitHub is pushed through `dba3237`.
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

Mobile version 0.1.4 adds cable-to-conduit assignment. In the cable editor,
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

## Verified state

The full solution built with zero warnings and zero errors. The document check
suite passed. Emulator testing verified:

- assigning `EKRK 3G2.5-01` to `RÖR-01`;
- persisted `conduit = "69b8eaeb97e741f7925c33c34aa10366"` in project TOML;
- cable route geometry changed to the conduit geometry;
- the 20 mm conduit reported 40.1% fill, above the 40% planning threshold;
- the fill warning rendered red;
- the power cable reported `THERMAL UNKNOWN` because Ib/In/Iz evidence was not
  supplied;
- no app fatal exception occurred.

The signed ARM64 field-test artifact is Mobile 0.1.4, version code 5:

- SHA-256: `c7e89a56e805cbd61c6cf93a50c4c9984878d0d073a9ed46de6b5fedb4f65472`
- Size: `81304064` bytes
- Public Apps URL:
  `https://apothictech.se/downloads/EutherWire-0.1.0-debug.apk`
- Server backup of the previous artifact:
  `/tmp/EutherWire-0.1.3-mobile-routing.previous.apk`

The public URL returned HTTP 200, Android APK MIME type, and the matching byte
length after deployment.

## Next implementation slice

Bring traceable circuit-design inputs into the mobile cable editor so a power
cable can progress honestly from `THERMAL UNKNOWN` to `PASS` or `WARNING`.

For power presets, add fields for:

- design current `Ib` in amperes;
- protective-device current `In` and characteristic;
- loaded conductor count;
- verified reference current-carrying capacity;
- ambient correction factor;
- grouping correction factor;
- thermal-insulation correction factor;
- human-readable reference source.

Reuse `CircuitDesign`, `ElectricalCableSpec`, `ElectricalRuleProfile`,
`SetCableElectricalCommand`, and `ProjectAnalyzer`. Do not add a mobile-only
electrical model. Preserve the existing conductor list and cable product when
only design values change. Blank or incomplete evidence must remain unknown;
do not insert optimistic defaults for ampacity or source.

Show the corrected capacity and relation directly in the dialog:

`Ib <= In <= Iz corrected`

Use green only for a complete passing relation, red for a complete failing
relation, and yellow for incomplete/unknown data. Continue showing the conduit
grouping warning when more than one power circuit shares a conduit.

Add document checks covering:

- a complete passing relation;
- a complete failing relation;
- incomplete inputs remaining unknown;
- assignment to a conduit preserving an existing detailed electrical design;
- grouping two power circuits producing the visible verification warning;
- TOML round-trip of every entered value and reference source.

After that slice, the next product step is project-linked field photo capture,
followed by calibrated camera/gyroscope guidance.

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
