# Runtime Write-Path Inventory

## Purpose

Provide a reproducible, read-only inventory of Python call sites that may mutate
memory storage or write files. This is an audit instrument, not an authorization
layer.

## Evidence state

`UNVERIFIED` until `30_SCRIPTS/verification/write_path_audit.py` is executed on
the exact commit under review and its output is preserved as audit evidence.

## Command

```bash
python 30_SCRIPTS/verification/write_path_audit.py --root .
```

Machine-readable form:

```bash
python 30_SCRIPTS/verification/write_path_audit.py --root . --json
```

## Detection coverage

The scanner identifies:

- `*.storage.set` / `*.storage.delete` calls;
- `*.store.set` / `*.store.delete` calls;
- generic `.set()` / `.delete()` calls;
- file `.write()` / `.writelines()` calls;
- `open()` file creation/access that may write;
- `Path.write_text()` / `Path.write_bytes()`;
- `Path.unlink()` / `Path.rmdir()`;
- `os.remove()` / `os.unlink()` / `os.rmdir()`;
- common `shutil.copy*()` / `shutil.move()` / `shutil.rmtree()` mutations.

Known canonical boundaries are classified separately for review. A finding is
not automatically a vulnerability: each result must be classified as canonical,
trusted infrastructure, test-only, or an unintended direct mutation path.

## Current architectural boundary

Canonical runtime mutation paths currently include the Memory Controller and the
Consolidator reconsolidation boundary. Direct writes outside those boundaries
remain an explicit audit item until inventory evidence is generated and reviewed.

## Non-goals

- no storage mutation;
- no lifecycle promotion;
- no corpus cleanup;
- no retrieval integration;
- no `PROJECT_BRAIN/PROJECT_STATE.md` changes.
