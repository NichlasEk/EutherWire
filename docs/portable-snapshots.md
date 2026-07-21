# Portable project snapshots

`.eutherwire-snapshot` is EutherWire's deterministic ZIP-based interchange
format for moving a field project between desktop and phone storage.

Each snapshot contains:

- canonical schema-12 `project.toml`;
- `installation-events.jsonl` when the project has a journal;
- every project-relative photo referenced by an installation record;
- `snapshot.json`, which declares format version, project schema, byte length,
  and SHA-256 for every payload file.

Only referenced evidence is selected. Autosaves, exports, caches, and unrelated
files do not leak into a snapshot. Symbolic links, absolute paths, backslashes,
empty path components, and `..` are rejected. Import also limits individual
and total uncompressed sizes to reduce ZIP-bomb risk.

## CLI

```sh
dotnet run --project src/EutherWire.Cli -- snapshot-export \
  garage.eutherwire garage.eutherwire-snapshot
dotnet run --project src/EutherWire.Cli -- snapshot-import \
  garage.eutherwire-snapshot garage-from-phone.eutherwire
```

The helper script exposes the same operations:

```sh
./eutherwire.sh snapshot garage.eutherwire garage.eutherwire-snapshot
./eutherwire.sh import-snapshot garage.eutherwire-snapshot garage-from-phone.eutherwire
```

Export and import refuse to overwrite existing paths. Import reads and hashes
the complete archive in a staging directory, parses the project and journal,
checks that referenced photos are present, and only then renames the staged
directory to its final project name.

SHA-256 detects corruption and accidental modification; it is not an author
signature or encryption. A future trust layer can sign the deterministic
manifest without changing the project model.
