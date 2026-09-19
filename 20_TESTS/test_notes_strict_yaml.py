"""Every file the storage engine scans must be readable, or be listed with a reason.

`FileStorageEngine` silently skips (audit entry `storage_error`) any file whose
frontmatter its parser rejects; for the controller such a file does not exist.
These tests keep that set explicit:

* a file that cannot be read and is not in the committed allowlist fails the suite;
* an allowlist entry whose file has become readable fails the suite (stale entry);
* notes repaired for syntax must read, strictly, exactly the values that a
  line-by-line reading (no YAML parser) found in the file before the repair.
"""
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
ALLOWLIST = REPO_ROOT / "20_TESTS" / "fixtures" / "unreadable_notes_allowlist.json"
EVIDENCE = REPO_ROOT / "07_EVALUATION" / "vault_hygiene" / "repaired_notes_evidence.json"

_spec = importlib.util.spec_from_file_location(
    "unreadable_notes", REPO_ROOT / "30_SCRIPTS" / "verification" / "unreadable_notes.py")
un = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(un)


def unlisted_and_stale(found, allowlist):
    listed = {e["path"]: e for e in allowlist["entries"]}
    current = {f["path"]: f["kind"] for f in found}
    unlisted = sorted(p for p in current if p not in listed)
    stale = sorted(p for p in listed if p not in current)
    kind_changed = sorted(p for p in current if p in listed and listed[p]["kind"] != current[p])
    return unlisted, stale, kind_changed


class TestVaultNotesAreReadable(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.allowlist = json.loads(ALLOWLIST.read_text(encoding="utf-8"))
        cls.found = un.find_unreadable(REPO_ROOT)

    def test_every_entry_has_a_reason(self):
        for entry in self.allowlist["entries"]:
            self.assertTrue(entry.get("reason", "").strip(), entry["path"])
            self.assertIn(entry.get("category"), {"plain_document", "owner_decision", "intentional_fixture"})

    def test_no_unlisted_unreadable_file(self):
        unlisted, _, _ = unlisted_and_stale(self.found, self.allowlist)
        self.assertEqual(unlisted, [], "files the storage engine cannot read, not on the allowlist")

    def test_no_stale_allowlist_entry(self):
        _, stale, _ = unlisted_and_stale(self.found, self.allowlist)
        self.assertEqual(stale, [], "allowlisted files that are readable now: remove the entry")

    def test_listed_failure_kind_is_unchanged(self):
        _, _, changed = unlisted_and_stale(self.found, self.allowlist)
        self.assertEqual(changed, [])


class TestNegativeControls(unittest.TestCase):
    """The checks above must be able to fail."""

    def _vault(self, files):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = Path(tmp.name)
        for rel, text in files.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8", newline="")
        return root

    def test_broken_yaml_note_is_found_and_unlisted(self):
        root = self._vault({
            "01_ARCHITECTURE/good.md": "---\nid: a\ntitle: fine\n---\nbody\n",
            "01_ARCHITECTURE/bad.md": "---\nid: b\ntitle: x: y: z\n---\nbody\n",
        })
        found = un.find_unreadable(root, roots=("01_ARCHITECTURE",))
        self.assertEqual([(f["path"], f["kind"]) for f in found], [("01_ARCHITECTURE/bad.md", "yaml_error")])
        unlisted, _, _ = unlisted_and_stale(found, {"entries": []})
        self.assertEqual(unlisted, ["01_ARCHITECTURE/bad.md"])

    def test_unclosed_and_missing_frontmatter_are_found(self):
        root = self._vault({
            "01_ARCHITECTURE/unclosed.md": "---\nid: c\nstatus: x\n\n## links\n- [[a]]\n",
            "01_ARCHITECTURE/plain.md": "# just a document\n",
        })
        kinds = {f["path"]: f["kind"] for f in un.find_unreadable(root, roots=("01_ARCHITECTURE",))}
        self.assertEqual(kinds, {"01_ARCHITECTURE/unclosed.md": "unclosed_frontmatter",
                                 "01_ARCHITECTURE/plain.md": "no_frontmatter"})

    def test_stale_entry_is_detected(self):
        allow = {"entries": [{"path": "01_ARCHITECTURE/gone.md", "kind": "no_frontmatter"}]}
        _, stale, _ = unlisted_and_stale([], allow)
        self.assertEqual(stale, ["01_ARCHITECTURE/gone.md"])


class TestRepairedNotesKeepTheirValues(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.notes = json.loads(EVIDENCE.read_text(encoding="utf-8"))["notes"]

    def test_repaired_notes_read_strictly_and_identically(self):
        self.assertGreaterEqual(len(self.notes), 1)
        for rel, evidence in self.notes.items():
            text = (REPO_ROOT / rel).read_text(encoding="utf-8")
            self.assertIsNone(un.classify(text), f"{rel} is still unreadable")
            self.assertEqual(un.diff_readings(evidence["tolerant_before"], un.strict_fields(text)), [], rel)

    def test_negative_control_a_changed_value_is_reported(self):
        rel, evidence = next(iter(self.notes.items()))
        tampered = dict(evidence["tolerant_before"])
        key = next(k for k, v in tampered.items() if isinstance(v, str))
        tampered[key] = tampered[key] + " (edited)"
        text = (REPO_ROOT / rel).read_text(encoding="utf-8")
        self.assertNotEqual(un.diff_readings(tampered, un.strict_fields(text)), [])

    def test_negative_control_a_value_the_parser_reinterprets_is_reported(self):
        # An unquoted list item containing ': ' silently becomes a mapping.
        text = "---\nrisks:\n  - keep this: as text\n---\n"
        tolerant = un.tolerant_fields(text)
        self.assertEqual(tolerant["risks"], ["keep this: as text"])
        self.assertNotEqual(un.diff_readings(tolerant, un.strict_fields(text)), [])


if __name__ == "__main__":
    unittest.main()
