# -*- coding: utf-8 -*-
"""Context compression utilities.

Provides simple summarization and claim extraction for notes.
"""

from typing import List, Dict

def summarize_note(note: Dict[str, any], max_chars: int = 200) -> str:
    """Return a truncated summary of the note content.
    If the content is shorter than max_chars, return it unchanged.
    """
    content = note.get("content", "")
    return content[:max_chars].rstrip()

def extract_claims(note: Dict[str, any]) -> List[str]:
    """Very naive claim extraction: split sentences and return those ending with a period.
    In real system this would use NLP; here we keep it simple.
    """
    content = note.get("content", "")
    sentences = [s.strip() for s in content.split('.') if s.strip()]
    # Return first few sentences as claims
    return sentences[:3]
