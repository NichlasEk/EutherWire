# Installation records

EutherWire schema 12 gives every installable object one shared, revisioned field record.
Devices, openings, conduits, and cables therefore appear in the same task list
and retain the same stable object ID from desktop planning through mobile work.

An installation record stores:

- `object_id`, `status`, and monotonically increasing `revision`;
- optional `updated_at`, note, and actual 3D position;
- optional actual length and test result;
- zero or more project-relative photo references.

Records live in `project.toml` as `[[installation_records]]`. Loading schema 11
or older projects creates planned records automatically and migrates legacy
cable status and actual length. Saving upgrades the project to schema 12.

The document owns record lifecycle. Adding an installable object creates its
record, deleting it removes the record, and undo restores the complete record
including evidence. `SetInstallationRecordCommand` makes changes undoable and
keeps legacy cable fields synchronized during the transition.

The desktop/CLI surface exposes `installation_status` and `installation_note`
as stable properties on all installable objects. `actual_length_mm` remains a
cable property. The `install` CLI command accepts any installable object ID,
while `tasks` lists all four object kinds.

## Offline event journal

The record is current state; `installation-events.jsonl` is the append-only
audit and synchronization log. Every JSON line carries a unique event ID,
stable object ID, base revision, timestamp, author/device ID, operation, and a
complete installation-record payload.

Applied event IDs are persisted under `[sync]` in `project.toml`. Applying the
same event twice returns `duplicate`. An event whose base revision does not
match current state returns `conflict`, remains in the local journal, and never
overwrites the project silently. Reusing an event ID with different JSON
content is rejected as an ID collision.

```sh
dotnet run --project src/EutherWire.Cli -- journal-create \
  examples/garage.eutherwire phone-events.jsonl camera-north phone-garage installed
dotnet run --project src/EutherWire.Cli -- journal-apply \
  examples/garage.eutherwire phone-events.jsonl
```

`install` also creates and records a `desktop-cli` event. The next data-layer
slice is a portable project snapshot containing `project.toml`, journal, and
explicitly captured project-relative evidence files.
