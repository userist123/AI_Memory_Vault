"""Tests verifying that specific terms in curriculum trap questions do not exist in Chapter 8 text.

Includes a negative control demonstrating that the absence check correctly detects present terms.
"""
import json
import unittest
from pathlib import Path
from bs4 import BeautifulSoup

REPO_ROOT = Path(__file__).resolve().parents[1]
TEST_SET_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "openstax_ch08_frozen_test_set.json"
RAW_CHAPTER_DIR = REPO_ROOT / "06_INBOX" / "RAW_IMPORTS" / "openstax_psychology_2e_ch08"


class TestCurriculumTrapsAbsence(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.full_text = ""
        for html_file in sorted(RAW_CHAPTER_DIR.glob("8_*.html")):
            soup = BeautifulSoup(html_file.read_text(encoding="utf-8"), "html.parser")
            cls.full_text += " " + soup.get_text(separator=" ")
        cls.full_text_lower = cls.full_text.lower()
        cls.test_set = json.loads(TEST_SET_PATH.read_text(encoding="utf-8"))

    def test_all_trap_specific_terms_are_absent_from_chapter_text(self):
        """Prove that specific terms defining each trap do NOT exist in Chapter 8 source text."""
        traps = [q for q in self.test_set["questions"] if q.get("unanswerable")]
        self.assertGreaterEqual(len(traps), 10, "Must have at least 10 trap questions")

        for trap in traps:
            specific_terms = trap.get("specific_terms", [])
            self.assertGreater(len(specific_terms), 0, f"Trap {trap['id']} must declare specific_terms")
            for term in specific_terms:
                count = self.full_text_lower.count(term.lower())
                self.assertEqual(
                    count,
                    0,
                    f"Trap {trap['id']} term '{term}' unexpectedly found {count} times in Chapter 8 text"
                )

    def test_negative_control_known_terms_are_detected(self):
        """Negative control: prove that the absence check would fail if terms were present."""
        known_present_terms = ["hippocampus", "ebbinghaus", "loftus", "amnesia", "encoding"]
        for term in known_present_terms:
            count = self.full_text_lower.count(term)
            self.assertGreater(
                count,
                0,
                f"Negative control sanity: '{term}' should be present in Chapter 8"
            )
            # Verify that our assertion would fail on a present term
            with self.assertRaises(AssertionError):
                self.assertEqual(count, 0)


if __name__ == "__main__":
    unittest.main()
