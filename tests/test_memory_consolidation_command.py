"""Tests for memory consolidation workflow and CLI command."""
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
PACKAGES_DIR = REPO_ROOT / "03_IMPLEMENTATION" / "packages"


class TestMemoryConsolidationCommand(unittest.TestCase):
    def test_cli_consolidate_generates_valid_report(self):
        with tempfile.TemporaryDirectory() as tmpdir:
            out_file = Path(tmpdir) / "test_report.json"
            env = os.environ.copy()
            env["PYTHONPATH"] = str(PACKAGES_DIR)
            
            cmd = [
                sys.executable,
                "-m",
                "cognitive_core.memory_v6_cli",
                "consolidate",
                "--output",
                str(out_file),
            ]
            
            proc = subprocess.run(
                cmd,
                cwd=str(REPO_ROOT),
                env=env,
                capture_output=True,
                text=True,
            )
            self.assertEqual(proc.returncode, 0, f"CLI failed: {proc.stderr}")
            self.assertTrue(out_file.exists(), "Report file was not created")
            
            data = json.loads(out_file.read_text(encoding="utf-8"))
            self.assertIn("generated_at", data)
            self.assertIn("dormant_candidates", data)
            self.assertIn("stale_review_candidates", data)
            self.assertIn("conflict_pairs", data)
            self.assertIn("stats", data)
            
            stats = data["stats"]
            self.assertGreater(stats.get("total_notes", 0), 0)
            self.assertIn("processed_notes", stats)

    def test_sleep_consolidator_is_read_only(self):
        from memory_controller.controller import MemoryController
        from memory_controller.storage.file_engine import FileStorageEngine
        from cognitive_core.sleep_consolidation import SleepConsolidator
        
        storage = FileStorageEngine(str(REPO_ROOT))
        initial_keys = set(storage.id_to_path.keys())
        
        controller = MemoryController(storage)
        consolidator = SleepConsolidator(controller, max_items_per_run=10)
        report = consolidator.run()
        
        # Verify storage remains completely unmutated
        final_keys = set(storage.id_to_path.keys())
        self.assertEqual(initial_keys, final_keys, "Storage note keys mutated during consolidation")
        self.assertIsInstance(report.stats, dict)


if __name__ == "__main__":
    unittest.main()
