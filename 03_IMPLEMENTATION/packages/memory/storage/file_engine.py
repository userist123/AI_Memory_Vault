import os
import tempfile
import sys
from typing import Dict, Any, List, Optional, Tuple
# Lazy import of Lifecycle moved inside query method to avoid circular import
from memory_controller.audit.logger import audit_event
from .serializer import serialize, deserialize
from .path_resolver import resolve_path
from vault_access.links import is_link

#: Folders scanned when indexing notes from disk.
#:
#: This is the UNION of two taxonomies that the restructuring left divergent,
#: and the union is deliberate:
#:
#: * The content roots, equal to ``retrieval.vault_index.DEFAULT_ROOTS``. The
#:   engine did not scan these at all. On a fresh checkout its old list --
#:   "00_CORE", "02_PROJECTS", "03_PROCEDURES" and friends -- matched 0 files,
#:   so the production storage layer loaded zero notes while 663 id-bearing
#:   notes sat in the vault, 633 of them in 01_ARCHITECTURE alone.
#:
#: * The legacy write targets, which ``storage/path_resolver.py`` still uses:
#:   a note of type "knowledge" is written to 01_KNOWLEDGE, "procedure" to
#:   03_PROCEDURES, and so on. Reads must keep covering them or the engine
#:   would stop seeing what it itself writes.
#:
#: KNOWN GAP: the write path was never migrated to the numbered content roots,
#: so new notes still land in the legacy tree while the corpus lives in the new
#: one. Unifying them is a data migration and an architecture decision, not a
#: constant change; it is deliberately NOT done here.
#:
#: Nothing caught the read-side divergence because the suite exercises the
#: in-memory StorageEngine with fixtures. `test_storage_canonical_roots.py`
#: now runs against the real vault.
CONTENT_ROOTS = (
    "01_ARCHITECTURE",
    "02_PRODUCT",
    "10_DOCUMENTATION",
    "00_GOVERNANCE",
)

LEGACY_WRITE_ROOTS = (
    "00_CORE",
    "01_KNOWLEDGE",
    "02_PROJECTS",
    "03_PROCEDURES",
    "04_MEMORY",
    "05_RESOURCES",
    "99_SYSTEM",
)

CANONICAL_FOLDERS = CONTENT_ROOTS + LEGACY_WRITE_ROOTS

#: Index-time size cap for one note. Larger files are skipped ("too_large"), never read.
MAX_NOTE_BYTES = 8 * 1024 * 1024

#: Imported skills are untrusted data, never canonical notes (vault-relative; vault_domains.yaml
#: marks EXTERNAL_SKILLS `trust: untrusted`). Closed by LOCATION only.
_EXCLUDED_IMPORT_DIRS = (
    "01_KNOWLEDGE/EXTERNAL_SKILLS",
    "01_ARCHITECTURE/knowledge/EXTERNAL_SKILLS",
)

#: skip reasons that are policy decisions, not corruption.
_NON_CORRUPT_REASONS = ("excluded_import", "too_large", "link_not_followed")


class NoteTooLarge(Exception):
    """A note file is over MAX_NOTE_BYTES (checked before open and again on the bytes read)."""


def read_note_bounded(filepath: str) -> str:
    """The one bounded note reader (index scan and get()): size check before open, then at most
    MAX_NOTE_BYTES+1 bytes; newlines normalised as text mode did. Raises NoteTooLarge."""
    if os.path.getsize(filepath) > MAX_NOTE_BYTES:
        raise NoteTooLarge(filepath)
    with open(filepath, 'rb') as f:
        raw = f.read(MAX_NOTE_BYTES + 1)
    if len(raw) > MAX_NOTE_BYTES:
        raise NoteTooLarge(filepath)  # grew between stat and read
    return raw.decode('utf-8').replace("\r\n", "\n").replace("\r", "\n")


def _real(path: str) -> str:
    return os.path.normcase(os.path.realpath(path))


def _inside(real: str, folder: str) -> bool:
    return real == folder or real.startswith(folder + os.sep)

_TREE_LABELS = {root: "content root" for root in CONTENT_ROOTS}
_TREE_LABELS.update({root: "legacy write root" for root in LEGACY_WRITE_ROOTS})


class FileStorageEngine:
    def __init__(self, vault_root: str):
        self.vault_root = vault_root
        self.id_to_path: Dict[str, str] = {}
        self._cache: Dict[str, Tuple[float, Dict[str, Any]]] = {}
        #: What the last index build saw: every .md file scanned, how many were
        #: indexed, and one entry per file skipped (relative path, reason, exception
        #: type -- never the content). Each skip also leaves a ``storage_skipped``
        #: audit event. Before this existed, unreadable or id-less notes vanished
        #: from the index without a trace (D-01).
        #: Directories/roots not entered (links, junctions, realpath outside the vault) are listed
        #: in ``links_not_followed``, NOT in ``skipped``: they are not files, so they are not
        #: counted in ``scanned`` and scanned == indexed + len(skipped) keeps holding.
        self.load_report: Dict[str, Any] = {"scanned": 0, "indexed": 0, "skipped": [],
                                            "links_not_followed": []}
        self._real_vault = _real(vault_root)
        self._excluded_real = tuple(_real(os.path.join(vault_root, d)) for d in _EXCLUDED_IMPORT_DIRS)
        self._read_too_large: Dict[str, str] = {}
        #: Indexed notes whose file could not be read/parsed at the last
        #: query()/store read (id -> relative path). Lets a caller tell a degraded
        #: result from a genuinely empty one.
        self._read_skips: Dict[str, str] = {}
        self._initialize_index()

    def _note_read_skip(self, note_id: str) -> None:
        filepath = self.id_to_path.get(note_id)
        if not filepath or not os.path.exists(filepath):
            return
        try:
            rel = os.path.relpath(filepath, self.vault_root)
        except ValueError:
            rel = os.path.basename(filepath)
        self._read_skips[note_id] = rel.replace("\\", "/")

    def skip_report(self, cap: int = 20) -> Dict[str, Any]:
        """Corrupt/unreadable note files seen so far: load-time skips plus
        read-time failures. Paths only, capped; never raises."""
        entries = self.load_report.get("skipped", [])
        paths = [e.get("path") for e in entries if e.get("reason") not in _NON_CORRUPT_REASONS]
        paths += [p for p in self._read_skips.values() if p not in paths]

        def count(reason: str) -> int:
            return sum(1 for e in entries if e.get("reason") == reason)

        return {"skipped_corrupt": len(paths), "skipped_paths": paths[:cap],
                "skipped_excluded_import": count("excluded_import"),
                "skipped_too_large": count("too_large") + len(self._read_too_large),
                "skipped_link_not_followed": count("link_not_followed")
                + len(self.load_report.get("links_not_followed", []))}

    def _rel(self, filepath: str) -> str:
        try:
            rel = os.path.relpath(filepath, self.vault_root)
        except ValueError:
            rel = os.path.basename(filepath)
        return rel.replace("\\", "/")

    def _record_link_skip(self, path: str) -> None:
        entry = {"path": self._rel(path), "reason": "link_not_followed", "exception": None}
        self.load_report["links_not_followed"].append(entry)
        audit_event("storage_skipped", "system", "unknown", success=False, details=dict(entry))

    def _record_skip(self, filepath: str, reason: str, exc: Optional[BaseException]) -> None:
        try:
            rel = os.path.relpath(filepath, self.vault_root)
        except ValueError:
            rel = os.path.basename(filepath)
        rel = rel.replace("\\", "/")
        entry = {"path": rel, "reason": reason,
                 "exception": type(exc).__name__ if exc is not None else None}
        self.load_report["skipped"].append(entry)
        audit_event("storage_skipped", "system", "unknown", success=False, details=dict(entry))

    def _tree_label(self, filepath: str) -> str:
        """Which of CONTENT_ROOTS / LEGACY_WRITE_ROOTS `filepath` falls under,
        for the duplicate-UUID diagnostic below. Content roots are the
        intended canonical source (see the module docstring); legacy roots
        are scanned only because path_resolver.py still writes new notes
        there. This does not decide which specific copy is correct -- a
        content-root path is only canonical if it is genuine corpus content,
        not a test fixture that landed there by accident (see WP-0,
        01_ARCHITECTURE/knowledge/test_00000000.md moved out for exactly that
        reason) -- it only tells the reader which rule applies to each path.
        """
        try:
            rel = os.path.relpath(filepath, self.vault_root)
        except ValueError:
            return "unrecognized root"
        top = rel.replace("\\", "/").split("/", 1)[0]
        label = _TREE_LABELS.get(top)
        return f"{label} ({top})" if label else f"unrecognized root ({top})"

    def _is_external_import(self, filepath: str) -> bool:
        """True for anything under an EXTERNAL_SKILLS import folder. Compared on the resolved real
        path, so a link elsewhere pointing into the folder cannot bypass it; the lexical
        vault-relative prefix (case-insensitive) is kept as well."""
        if any(_inside(_real(filepath), e) for e in self._excluded_real):
            return True
        try:
            rel = os.path.relpath(filepath, self.vault_root)
        except ValueError:
            return False
        rel = rel.replace("\\", "/").lower() + "/"
        return any(rel.startswith(d.lower() + "/") for d in _EXCLUDED_IMPORT_DIRS)

    def _scan_markdown(self, folder_path: str) -> List[str]:
        """Sorted .md files under `folder_path`, never descending into directory
        symlinks or junctions (each skipped directory is reported once as
        ``link_not_followed``). Dot-prefixed names are skipped, as glob did."""
        found: List[str] = []
        for dirpath, dirnames, filenames in os.walk(folder_path, followlinks=False):
            keep = []
            for d in sorted(dirnames):
                full = os.path.join(dirpath, d)
                if d.startswith("."):
                    continue
                if "RAW_IMPORTS" in full or "Obsidian" in full:
                    continue
                if is_link(full) or not _inside(_real(full), self._real_vault):
                    self._record_link_skip(full)
                    continue
                keep.append(d)
            dirnames[:] = keep
            for name in filenames:
                if name.startswith("."):
                    continue
                if name.endswith(".md") or (os.name == "nt" and name.lower().endswith(".md")):
                    found.append(os.path.join(dirpath, name))
        return sorted(found)

    def _initialize_index(self):
        # Scan canonical folders to build the UUID -> Path index
        # EXPLICIT EXCLUSIONS: "06_INBOX" and "90_TEMPLATES" are NOT included
        canonical_folders = list(CANONICAL_FOLDERS)
        for folder in canonical_folders:
            folder_path = os.path.join(self.vault_root, folder)
            if not os.path.lexists(folder_path):
                continue
            if is_link(folder_path) or not _inside(_real(folder_path), self._real_vault):
                self._record_link_skip(folder_path)
                continue

            for filepath in self._scan_markdown(folder_path):
                if "RAW_IMPORTS" in filepath or "Obsidian" in filepath:
                    continue
                self.load_report["scanned"] += 1
                if os.path.islink(filepath):
                    self._record_skip(filepath, "link_not_followed", None)
                    continue
                if self._is_external_import(filepath):
                    self._record_skip(filepath, "excluded_import", None)
                    continue
                try:
                    content = read_note_bounded(filepath)
                except NoteTooLarge:
                    self._record_skip(filepath, "too_large", None)
                    continue
                except UnicodeDecodeError as e:
                    self._record_skip(filepath, "decode_error", e)
                    continue
                except OSError as e:
                    self._record_skip(filepath, "read_error", e)
                    continue
                try:
                    data = deserialize(content)
                    note_id = data.get("id")
                    if not note_id:
                        self._record_skip(filepath, "missing_id", None)
                        continue
                    try:
                        hash(note_id)
                    except TypeError:
                        # An unhashable id (a YAML list or mapping) used to die on the
                        # dict lookup below as a silent TypeError; keep that exception
                        # name for the report.
                        raise TypeError(f"invalid id type {type(note_id).__name__}") from None
                    if note_id in self.id_to_path:
                        existing_path = self.id_to_path[note_id]
                        raise ValueError(
                            f"Duplicate UUID found: {note_id}\n"
                            f"  existing: {existing_path} -- {self._tree_label(existing_path)}\n"
                            f"  new:      {filepath} -- {self._tree_label(filepath)}\n"
                            "Content roots (01_ARCHITECTURE, 02_PRODUCT, 10_DOCUMENTATION, "
                            "00_GOVERNANCE) are the canonical corpus; legacy write roots are "
                            "scanned only because path_resolver.py still writes new notes "
                            "there (see storage/path_resolver.py). Two notes cannot share an "
                            "id -- remove or renumber whichever copy is not meant to be "
                            "canonical."
                        )
                    self.id_to_path[note_id] = filepath
                    mtime = os.path.getmtime(filepath)
                    self._cache[note_id] = (mtime, data)
                    self.load_report["indexed"] += 1
                except Exception as e:
                    if "Duplicate UUID" in str(e):
                        raise e
                    if "Malformed YAML" in str(e):
                        audit_event("storage_error", "system", "unknown", success=False,
                                    details={"error": "Malformed YAML", "path": filepath, "message": str(e)})
                        self._record_skip(filepath, "malformed_yaml", e)
                        continue
                    if isinstance(e, TypeError) and "invalid id" in str(e):
                        self._record_skip(filepath, "invalid_id", e)
                        continue
                    self._record_skip(filepath, "other", e)
                    continue

    def get(self, note_id: str) -> Optional[Dict[str, Any]]:
        filepath = self.id_to_path.get(note_id)
        if not filepath or not os.path.exists(filepath):
            self._cache.pop(note_id, None)
            return None
        try:
            mtime = os.path.getmtime(filepath)
            cached = self._cache.get(note_id)
            if cached and cached[0] == mtime:
                return dict(cached[1])
            content = read_note_bounded(filepath)
            data = deserialize(content)
            self._cache[note_id] = (mtime, dict(data))
            self._read_too_large.pop(note_id, None)
            return dict(data)
        except NoteTooLarge:
            # same outcome as a vanished file: not available; the skip is recorded
            self._cache.pop(note_id, None)
            if note_id not in self._read_too_large:
                self._read_too_large[note_id] = self._rel(filepath)
                audit_event("storage_skipped", "system", "unknown", success=False,
                            details={"path": self._rel(filepath), "reason": "too_large", "exception": None})
            return None
        except Exception:
            return None

    def _target_path_for(self, note_id: str, data: Dict[str, Any]) -> str:
        """Where this note should be written.

        `resolve_path()` maps a note TYPE to the legacy tree (knowledge ->
        01_KNOWLEDGE, procedure -> 03_PROCEDURES) and derives the file name
        from its category. Applied unconditionally to an existing note living
        in a content root, it writes a copy into the legacy tree and `set()`
        then deletes the canonical original as a stale duplicate.

        That path was inert only because the engine used to load zero notes:
        `id_to_path` never held a canonical note, so the delete never fired.
        Making the engine see the vault armed it, which is why this is settled
        here rather than deferred to the pending migration decision.

        An existing note under a content root therefore keeps its exact path,
        directory and file name both. The file name is not cosmetic there: it
        is the note's identity in the graph, since Obsidian and
        `VaultIndex.by_slug` both resolve `[[links]]` by file name. Renaming a
        canonical note because its category changed would silently break every
        link pointing at it.

        Notes in the legacy tree keep the previous behaviour, where the file
        name tracks the category. New notes are placed by `resolve_path()`
        unchanged; moving the existing corpus remains a separate, explicit
        migration decision.
        """
        resolved = resolve_path(self.vault_root, data)
        existing_path = self.id_to_path.get(note_id)
        if not existing_path or not os.path.exists(existing_path):
            return resolved
        existing_dir = os.path.dirname(os.path.abspath(existing_path))
        root = os.path.abspath(self.vault_root)
        try:
            rel = os.path.relpath(existing_dir, root)
        except ValueError:
            return resolved
        top = rel.replace("\\", "/").split("/")[0]
        if top in CONTENT_ROOTS:
            return existing_path
        # An existing note in the legacy tree keeps its legacy destination: new notes go
        # to the content tree (path_resolver.CONTENT_TREE_FOR_TYPE), old ones are not moved
        # by an update.
        return resolve_path(self.vault_root, data, prefer_content_tree=False)

    def set(self, note_id: str, data: Dict[str, Any]) -> None:
        yaml_id = data.get("id")
        if str(note_id) != str(yaml_id):
            raise ValueError(f"ID mismatch: storage key '{note_id}' must equal YAML id '{yaml_id}'")
        target_path = self._target_path_for(note_id, data)
        # LF on every platform (.gitattributes `* text=auto eol=lf`): normalise CRLF/CR and write
        # with newline='' so Windows text mode does not turn "\r\n" into "\r\r\n"
        # (20_TESTS/test_file_engine_writes_lf.py).
        serialized_content = serialize(data).replace("\r\n", "\n").replace("\r", "\n")
        os.makedirs(os.path.dirname(target_path), exist_ok=True)
        dir_name = os.path.dirname(target_path)
        fd, temp_path = tempfile.mkstemp(dir=dir_name, prefix=".tmp_")
        try:
            with os.fdopen(fd, 'w', encoding='utf-8', newline='') as f:
                f.write(serialized_content)
                f.flush()
                os.fsync(f.fileno())
            os.replace(temp_path, target_path)
        except Exception as e:
            if os.path.exists(temp_path):
                os.remove(temp_path)
            raise e
        existing_path = self.id_to_path.get(note_id)
        if existing_path and existing_path != target_path:
            if os.path.exists(existing_path):
                os.remove(existing_path)
        self.id_to_path[note_id] = target_path
        mtime = os.path.getmtime(target_path)
        cached = dict(data)
        if isinstance(cached.get("content"), str):
            # the file holds LF only: cache what a reload would return
            cached["content"] = cached["content"].replace("\r\n", "\n").replace("\r", "\n")
        self._cache[note_id] = (mtime, cached)

    def delete(self, note_id: str) -> None:
        filepath = self.id_to_path.get(note_id)
        if filepath:
            if "06_INBOX" in filepath:
                raise ValueError("Cannot delete from RAW_IMPORTS")
            if os.path.exists(filepath):
                os.remove(filepath)
            del self.id_to_path[note_id]
            self._cache.pop(note_id, None)

    def query(self, intent: str, lifecycle: List[str] = None, types: List[str] = None) -> List[Dict[str, Any]]:
        """Query notes, excluding RAW notes."""
        from memory_controller.controller import Lifecycle
        results = []
        self._read_skips = {}
        for note_id in list(self.id_to_path.keys()):
            try:
                note = self.get(note_id)
                if not note:
                    self._note_read_skip(note_id)
                    continue
                if note.get('lifecycle') == Lifecycle.RAW.value:
                    continue
                if lifecycle and note.get('lifecycle') not in lifecycle:
                    continue
                if types and note.get('type') not in types:
                    continue
                results.append(note)
            except Exception:
                self._note_read_skip(note_id)
                continue
        return results

    def all_notes(self) -> List[Dict[str, Any]]:
        """Return all non-RAW notes for read-only indexing consumers."""
        return self.query("graph")

    @property
    def store(self) -> Dict[str, Any]:
        """Read-only view of notes mapping id -> note dict for compatibility."""
        res = {}
        self._read_skips = {}
        for nid in list(self.id_to_path.keys()):
            note = self.get(nid)
            if note:
                res[nid] = note
            else:
                self._note_read_skip(nid)
        return res
