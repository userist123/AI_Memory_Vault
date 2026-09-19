"""Tests verifying that specific terms in statistics curriculum trap questions do not exist in Chapter 8 text.

Uses committed plain text fixtures in 07_EVALUATION/curriculum/source_text/statistics_ch08/.
Includes a negative control demonstrating that the absence check correctly detects present terms.
"""
import json
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
TEST_SET_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "openstax_statistics_frozen_test_set.json"
SOURCE_TEXT_DIR = REPO_ROOT / "07_EVALUATION" / "curriculum" / "source_text" / "statistics_ch08"


class TestStatisticsTrapsAbsence(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.full_text = ""
        txt_files = sorted(SOURCE_TEXT_DIR.glob("*.txt"))
        if not txt_files:
            raise RuntimeError(f"No text fixtures found in {SOURCE_TEXT_DIR}")
        for txt_file in txt_files:
            cls.full_text += " " + txt_file.read_text(encoding="utf-8")
        cls.full_text_lower = cls.full_text.lower()
        cls.test_set = json.loads(TEST_SET_PATH.read_text(encoding="utf-8"))

    def test_test_set_structure(self):
        """Verify the test set has exactly 22 questions (12 review, 10 traps)."""
        questions = self.test_set["questions"]
        self.assertEqual(len(questions), 22)
        review = [q for q in questions if not q.get("unanswerable")]
        traps = [q for q in questions if q.get("unanswerable")]
        self.assertEqual(len(review), 12)
        self.assertEqual(len(traps), 10)

    def test_all_trap_specific_terms_are_absent_from_chapter_text(self):
        """Prove that specific terms defining each trap do NOT exist in Statistics Chapter 8 source text."""
        traps = [q for q in self.test_set["questions"] if q.get("unanswerable")]
        self.assertEqual(len(traps), 10, "Must have exactly 10 trap questions")

        for trap in traps:
            specific_terms = trap.get("specific_terms", [])
            self.assertGreater(len(specific_terms), 0, f"Trap {trap['id']} must declare specific_terms")
            for term in specific_terms:
                count = self.full_text_lower.count(term.lower())
                self.assertEqual(
                    count,
                    0,
                    f"Trap {trap['id']} term '{term}' unexpectedly found {count} times in Statistics Chapter 8 text"
                )

    def test_negative_control_known_terms_are_detected(self):
        """Negative control: prove that the absence check would fail if terms were present."""
        known_present_terms = [
            "confidence interval",
            "student's t",
            "degrees of freedom",
            "error bound",
            "population mean",
            "sample size"
        ]
        for term in known_present_terms:
            count = self.full_text_lower.count(term.lower())
            self.assertGreater(
                count,
                0,
                f"Negative control sanity: '{term}' should be present in Statistics Chapter 8"
            )
            # Verify that our assertion would fail on a present term
            with self.assertRaises(AssertionError):
                self.assertEqual(count, 0)


if __name__ == "__main__":
    unittest.main()
