# Installation records

EutherWire schema 11 gives every installable object one shared field record.
Devices, openings, conduits, and cables therefore appear in the same task list
and retain the same stable object ID from desktop planning through mobile work.

An installation record stores:

- `object_id` and `status`;
- optional `updated_at`, note, and actual 3D position;
- optional actual length and test result;
- zero or more project-relative photo references.

Records live in `project.toml` as `[[installation_records]]`. Loading schema 10
or older projects creates planned records automatically and migrates legacy
cable status and actual length. Saving upgrades the project to schema 11.

The document owns record lifecycle. Adding an installable object creates its
record, deleting it removes the record, and undo restores the complete record
including evidence. `SetInstallationRecordCommand` makes changes undoable and
keeps legacy cable fields synchronized during the transition.

The desktop/CLI surface exposes `installation_status` and `installation_note`
as stable properties on all installable objects. `actual_length_mm` remains a
cable property. The `install` CLI command accepts any installable object ID,
while `tasks` lists all four object kinds.

## Next synchronization slice

The record is current state, not an audit log. Mobile synchronization will add
an append-only event file with a unique event ID, object ID, base revision,
timestamp, author/device ID, operation, and payload. Applying the same event
twice must be harmless. Conflicting offline edits must be retained for review,
not resolved silently. A portable project snapshot and that journal are the
last data-layer prerequisites for the first phone checklist.
