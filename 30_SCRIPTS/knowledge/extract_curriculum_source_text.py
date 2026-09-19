#!/usr/bin/env python3
"""Extract clean plain text from OpenStax Psychology 2e Chapter 8 HTML files.

Produces test fixtures under 07_EVALUATION/curriculum/source_text/ for CI repeatability
under Creative Commons Attribution 4.0 International (CC BY 4.0).
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
from bs4 import BeautifulSoup

REPO_ROOT = Path(__file__).resolve().parents[2]
RAW_DIR = REPO_ROOT / "06_INBOX" / "RAW_IMPORTS" / "openstax_psychology_2e_ch08"
DEST_DIR = REPO_ROOT / "07_EVALUATION" / "curriculum" / "source_text"
MANIFEST_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "provenance_manifest.json"

SECTIONS = [
    ("8_0_introduction.html", "8_0_introduction.txt", "https://openstax.org/books/psychology-2e/pages/8-introduction"),
    ("8_1_how_memory_functions.html", "8_1_how_memory_functions.txt", "https://openstax.org/books/psychology-2e/pages/8-1-how-memory-functions"),
    ("8_2_parts_of_the_brain_involved_with_memory.html", "8_2_parts_of_the_brain_involved_with_memory.txt", "https://openstax.org/books/psychology-2e/pages/8-2-parts-of-the-brain-involved-with-memory"),
    ("8_3_problems_with_memory.html", "8_3_problems_with_memory.txt", "https://openstax.org/books/psychology-2e/pages/8-3-problems-with-memory"),
    ("8_4_ways_to_enhance_memory.html", "8_4_ways_to_enhance_memory.txt", "https://openstax.org/books/psychology-2e/pages/8-4-ways-to-enhance-memory"),
]


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def extract_clean_text(html_content: str) -> str:
    soup = BeautifulSoup(html_content, "html.parser")
    # Target the main content container if available
    page = soup.find("div", {"data-type": "page"}) or soup.find("main") or soup
    # Remove scripts, styles, forms, navigation
    for elem in page(["script", "style", "nav", "footer", "header"]):
        elem.decompose()
    text = page.get_text(separator="\n")
    # Normalize whitespace: trim lines and remove empty blank line runs
    cleaned_lines = []
    for line in text.splitlines():
        trimmed = " ".join(line.split())
        if trimmed:
            cleaned_lines.append(trimmed)
    return "\n\n".join(cleaned_lines)


def main() -> None:
    DEST_DIR.mkdir(parents=True, exist_ok=True)
    manifest_files = []

    for html_name, txt_name, source_url in SECTIONS:
        html_path = RAW_DIR / html_name
        if not html_path.exists():
            raise FileNotFoundError(f"Source HTML not found: {html_path}")
        html_bytes = html_path.read_bytes()
        html_hash = sha256_bytes(html_bytes)

        clean_text = extract_clean_text(html_bytes.decode("utf-8"))
        txt_bytes = clean_text.encode("utf-8")
        txt_hash = sha256_bytes(txt_bytes)

        dest_file = DEST_DIR / txt_name
        dest_file.write_bytes(txt_bytes)
        print(f"Extracted {txt_name}: {len(clean_text)} chars, SHA-256: {txt_hash[:12]}...")

        manifest_files.append({
            "html_filename": html_name,
            "text_filename": txt_name,
            "source_url": source_url,
            "html_bytes": len(html_bytes),
            "html_sha256": html_hash,
            "text_bytes": len(txt_bytes),
            "text_sha256": txt_hash,
        })

    # Write provenance manifest
    manifest_data = {
        "work_title": "Psychology 2e",
        "chapter": "Chapter 8: Memory",
        "authors": [
            "Rose M. Spielman",
            "William J. Jenkins",
            "Marilyn D. Lovett"
        ],
        "publisher": "OpenStax, Rice University",
        "license": "Creative Commons Attribution 4.0 International (CC BY 4.0)",
        "license_url": "https://creativecommons.org/licenses/by/4.0/",
        "book_url": "https://openstax.org/details/books/psychology-2e",
        "conversion_note": "Converted from original OpenStax HTML to plain text fixture using BeautifulSoup for test repeatability and CC BY 4.0 distribution.",
        "files": manifest_files
    }
    MANIFEST_PATH.write_text(json.dumps(manifest_data, indent=2), encoding="utf-8")
    print(f"Updated provenance manifest at {MANIFEST_PATH}")

    # Write ATTRIBUTION.md
    attribution_content = """# Creative Commons Attribution 4.0 International (CC BY 4.0)

## Work Identification
- **Title**: Psychology 2e, Chapter 8: Memory
- **Authors**: Rose M. Spielman, William J. Jenkins, Marilyn D. Lovett
- **Publisher**: OpenStax, Rice University
- **Book URL**: https://openstax.org/details/books/psychology-2e
- **License**: Creative Commons Attribution 4.0 International (CC BY 4.0)
- **License URL**: https://creativecommons.org/licenses/by/4.0/

## Conversion and Transformation Notice
In accordance with Section 3(a)(1)(B) of the CC BY 4.0 license, the files in this directory are plain-text extracts converted from the original OpenStax HTML source files to serve as reproducible, deterministic testing fixtures. Non-text navigational elements, scripts, and styling were stripped during conversion.

## Included Section Fixtures & Source URLs
1. **8.0 Introduction** (`8_0_introduction.txt`)
   - Source URL: https://openstax.org/books/psychology-2e/pages/8-introduction
2. **8.1 How Memory Functions** (`8_1_how_memory_functions.txt`)
   - Source URL: https://openstax.org/books/psychology-2e/pages/8-1-how-memory-functions
3. **8.2 Parts of the Brain Involved with Memory** (`8_2_parts_of_the_brain_involved_with_memory.txt`)
   - Source URL: https://openstax.org/books/psychology-2e/pages/8-2-parts-of-the-brain-involved-with-memory
4. **8.3 Problems with Memory** (`8_3_problems_with_memory.txt`)
   - Source URL: https://openstax.org/books/psychology-2e/pages/8-3-problems-with-memory
5. **8.4 Ways to Enhance Memory** (`8_4_ways_to_enhance_memory.txt`)
   - Source URL: https://openstax.org/books/psychology-2e/pages/8-4-ways-to-enhance-memory

Detailed cryptographic hashes (SHA-256) of both the original HTML and the resulting plain-text files are maintained in `07_EVALUATION/curriculum/provenance_manifest.json`.
"""
    attribution_path = DEST_DIR / "ATTRIBUTION.md"
    attribution_path.write_text(attribution_content, encoding="utf-8")
    print(f"Wrote ATTRIBUTION.md at {attribution_path}")


if __name__ == "__main__":
    main()
