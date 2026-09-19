"""No Python module may write into the ontology slots without being reviewed.

A verdict manifest gates merge_candidate_concepts.py and a disposition manifest
gates apply_row_disposition.py, but a third script (promote_candidate_concept.py)
wrote the same files with neither. This test scans every Python file and fails on
any module that refers to the slots directory and writes files but is not in the
committed list of reviewed writers, each with its gate.
"""
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
DISPOSITIONS = REPO_ROOT / "20_TESTS" / "fixtures" / "ontology_slot_writers.json"

_spec = importlib.util.spec_from_file_location(
    "ontology_write_paths", REPO_ROOT / "30_SCRIPTS" / "verification" / "ontology_write_paths.py")
owp = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(owp)


class TestKnownWriters(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows = owp.scan(REPO_ROOT)
        cls.dispositions = owp.load_dispositions(DISPOSITIONS)

    def test_scan_finds_the_known_gated_writers(self):
        # Guard against a vacuous pass: the scanner must see the real writers.
        found = {r["file"] for r in self.rows}
        for path in ("30_SCRIPTS/ingestion/merge_candidate_concepts.py",
                     "30_SCRIPTS/ingestion/apply_row_disposition.py",
                     "30_SCRIPTS/ingestion/purge_rejected_rows.py",
                     "30_SCRIPTS/ingestion/promote_candidate_concept.py"):
            self.assertIn(path, found)

    def test_no_unreviewed_writer(self):
        self.assertEqual(owp.unlisted(self.rows, self.dispositions), [],
                         "module refers to the slots and writes files but is not in "
                         "20_TESTS/fixtures/ontology_slot_writers.json: review it and add its gate")

    def test_no_stale_entry(self):
        self.assertEqual(owp.stale(self.rows, self.dispositions), [])

    def test_every_entry_states_role_gate_and_conclusion(self):
        for path, entry in self.dispositions.items():
            self.assertIn(entry["role"], {"writer", "read_only", "test"}, path)
            self.assertTrue(entry["gate"].strip() and entry["conclusion"].strip(), path)

    def test_every_writer_names_a_manifest_gate_or_is_marked_open(self):
        for path, entry in self.dispositions.items():
            if entry["role"] != "writer":
                continue
            self.assertTrue(entry["conclusion"].startswith("OPEN") or "manifest" in entry["gate"].lower(), path)


class TestNegativeControls(unittest.TestCase):
    """The scan and the list must be able to fail."""

    def _repo(self, files):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = Path(tmp.name)
        for rel, text in files.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        return root

    def test_a_new_module_writing_the_slots_is_reported_as_unlisted(self):
        root = self._repo({"30_SCRIPTS/rogue.py": (
            "from pathlib import Path\n"
            "def clobber(text):\n"
            "    Path('01_ARCHITECTURE/ontology/slots/06_procedures.md').write_text(text)\n")})
        rows = owp.scan(root)
        self.assertEqual([r["file"] for r in rows], ["30_SCRIPTS/rogue.py"])
        self.assertEqual(owp.unlisted(rows, self.dispositions_of_real_repo()), ["30_SCRIPTS/rogue.py"])

    def test_a_writer_using_a_slots_dir_parameter_is_reported(self):
        root = self._repo({"03_IMPLEMENTATION/packages/x/rogue.py": (
            "def go(slots_dir, text):\n"
            "    f = open(slots_dir + '/a.md', 'w')\n"
            "    f.write(text)\n")})
        self.assertEqual(len(owp.scan(root)), 1)

    def test_readers_and_unrelated_writers_are_not_reported(self):
        root = self._repo({
            "30_SCRIPTS/reader.py": (
                "from pathlib import Path\n"
                "def read():\n"
                "    return Path('01_ARCHITECTURE/ontology/slots/a.md').read_text()\n"),
            "30_SCRIPTS/unrelated.py": (
                "from pathlib import Path\n"
                "def out(t):\n"
                "    Path('report.md').write_text(t)\n"),
        })
        self.assertEqual(owp.scan(root), [])

    def test_a_listed_module_that_disappears_is_reported_as_stale(self):
        self.assertEqual(owp.stale([], {"gone.py": {}}), ["gone.py"])

    @staticmethod
    def dispositions_of_real_repo():
        return owp.load_dispositions(DISPOSITIONS)


if __name__ == "__main__":
    unittest.main()
