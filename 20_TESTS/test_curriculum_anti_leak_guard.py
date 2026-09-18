"""Tests for the curriculum ingestion anti-leak guard.

Verifies:
1. The fixed generic extraction template and HTML-derived section titles contain no test answers
   and no 4-word question sequences from the frozen test set.
2. Negative control 1: Proves that a prompt containing 'Limitless' is rejected.
3. Negative control 2: Proves that a prompt containing a 4-word question sequence is rejected.
"""
import json
import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
TEST_SET_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "openstax_ch08_frozen_test_set.json"


PROMPT_TEMPLATE = """You are a cognitive knowledge extraction engine for an AI Memory Vault.
Analyze the following textbook section and extract its core factual concepts, theories, empirical findings, and definitions.

Section Title: {section_title}

Source Text:
{section_text}

Extraction Requirements:
1. Identify the essential factual concepts described in this section.
2. For each concept, provide:
   - title: A concise, standard psychological term or concept title.
   - summary: A factual summary (2-4 sentences) explaining the mechanism, definition, or finding.
   - exact_quote: A verbatim character-by-character quote from the Source Text supporting the concept. The quote MUST exist identically in the Source Text.
3. Return output strictly in valid JSON format matching this schema:
{{
  "concepts": [
    {{
      "title": "...",
      "summary": "...",
      "exact_quote": "..."
    }}
  ]
}}
Do NOT invent information. Extract only what is substantiated by the text.
"""

HTML_SECTION_TITLES = [
    "8.1How Memory Functions - Overview",
    "Encoding",
    "Storage",
    "Retrieval",
    "8.2Parts of the Brain Involved with Memory - Overview",
    "The Amygdala",
    "The Hippocampus",
    "The Cerebellum and Prefrontal Cortex",
    "Neurotransmitters",
    "8.3Problems with Memory - Overview",
    "Amnesia",
    "Memory Construction and Reconstruction",
    "Forgetting",
    "8.4Ways to Enhance Memory - Overview",
    "Memory-Enhancing Strategies",
    "How to Study Effectively"
]


def check_prompt_anti_leak(non_book_text: str, test_set_path: Path = TEST_SET_PATH) -> bool:
    """Verifies that non_book_text contains no test answers and no 4-word question sequences."""
    data = json.loads(test_set_path.read_text(encoding="utf-8"))
    text_lower = non_book_text.lower()

    prohibited_answers = set()
    for q in data["questions"]:
        ans = q.get("correct_answer", "")
        if ans and ans != "NOT_IN_CHAPTER":
            prohibited_answers.add(ans.lower())
    # Mandated specific target
    prohibited_answers.add("limitless")

    for ans in prohibited_answers:
        pattern = r"\b" + re.escape(ans) + r"\b"
        if re.search(pattern, text_lower):
            raise AssertionError(f"Leak detected: prohibited answer '{ans}' found in non-book prompt text")

    # Check 4-word sequences from questions
    for q in data["questions"]:
        words = re.findall(r"\b\w+\b", q["question"].lower())
        for i in range(len(words) - 3):
            four_gram = " ".join(words[i:i+4])
            pattern = r"\b" + re.escape(four_gram) + r"\b"
            if re.search(pattern, text_lower):
                raise AssertionError(f"Leak detected: 4-word question sequence '{four_gram}' from Q '{q['id']}' found in non-book prompt text")
    return True


class TestCurriculumAntiLeakGuard(unittest.TestCase):
    def test_all_production_prompts_pass_guard(self):
        """Prove that none of the 16 HTML-derived section prompts leak test answers or 4-grams."""
        for title in HTML_SECTION_TITLES:
            prompt_non_book = PROMPT_TEMPLATE.format(section_title=title, section_text="")
            self.assertTrue(
                check_prompt_anti_leak(prompt_non_book),
                f"Prompt for section '{title}' must pass anti-leak guard"
            )

    def test_negative_control_answer_limitless_is_rejected(self):
        """Negative control: prove that a prompt template containing 'Limitless' raises AssertionError."""
        leaky_template = PROMPT_TEMPLATE.replace(
            "Analyze the following textbook section",
            "Analyze the following textbook section (Limitless capacity)"
        )
        leaky_prompt = leaky_template.format(section_title="Storage", section_text="")
        with self.assertRaises(AssertionError) as ctx:
            check_prompt_anti_leak(leaky_prompt)
        self.assertIn("limitless", str(ctx.exception).lower())

    def test_negative_control_question_4gram_is_rejected(self):
        """Negative control: prove that a prompt containing a 4-word question sequence raises AssertionError."""
        # 4-gram from q02: 'storage capacity of long'
        leaky_template = PROMPT_TEMPLATE.replace(
            "Section Title:",
            "Focus on the storage capacity of long term items in Section Title:"
        )
        leaky_prompt = leaky_template.format(section_title="Storage", section_text="")
        with self.assertRaises(AssertionError) as ctx:
            check_prompt_anti_leak(leaky_prompt)
        self.assertIn("storage capacity", str(ctx.exception).lower())


if __name__ == "__main__":
    unittest.main()
