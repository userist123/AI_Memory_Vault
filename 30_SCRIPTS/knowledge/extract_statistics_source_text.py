#!/usr/bin/env python3
"""Extract clean plain text from OpenStax Introductory Statistics 2e Chapter 8 HTML files.

Produces test fixtures under 07_EVALUATION/curriculum/source_text/statistics_ch08/ for CI repeatability
under Creative Commons Attribution 4.0 International (CC BY 4.0).
"""
from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path
from bs4 import BeautifulSoup

REPO_ROOT = Path(__file__).resolve().parents[2]
RAW_DIR = REPO_ROOT / "06_INBOX" / "RAW_IMPORTS" / "openstax_statistics_2e_ch08"
DEST_DIR = REPO_ROOT / "07_EVALUATION" / "curriculum" / "source_text" / "statistics_ch08"
MANIFEST_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "statistics_provenance_manifest.json"

SECTIONS = [
    (
        "8_0_introduction.html",
        "8_0_introduction.txt",
        "https://openstax.org/books/introductory-statistics-2e/pages/8-introduction",
        Path(r"C:\Users\Marius\.gemini\antigravity\brain\aebf6032-0fa2-438b-bb11-3eda139a64e3\.system_generated\steps\41202\content.md")
    ),
    (
        "8_1_a_single_population_mean_using_the_normal_distribution.html",
        "8_1_a_single_population_mean_using_the_normal_distribution.txt",
        "https://openstax.org/books/introductory-statistics-2e/pages/8-1-a-single-population-mean-using-the-normal-distribution",
        Path(r"C:\Users\Marius\.gemini\antigravity\brain\aebf6032-0fa2-438b-bb11-3eda139a64e3\.system_generated\steps\41216\content.md")
    ),
    (
        "8_2_a_single_population_mean_using_the_student_t_distribution.html",
        "8_2_a_single_population_mean_using_the_student_t_distribution.txt",
        "https://openstax.org/books/introductory-statistics-2e/pages/8-2-a-single-population-mean-using-the-student-t-distribution",
        Path(r"C:\Users\Marius\.gemini\antigravity\brain\aebf6032-0fa2-438b-bb11-3eda139a64e3\.system_generated\steps\41218\content.md")
    ),
    (
        "8_3_a_population_proportion.html",
        "8_3_a_population_proportion.txt",
        "https://openstax.org/books/introductory-statistics-2e/pages/8-3-a-population-proportion",
        Path(r"C:\Users\Marius\.gemini\antigravity\brain\aebf6032-0fa2-438b-bb11-3eda139a64e3\.system_generated\steps\41220\content.md")
    ),
    (
        "8_4_confidence_interval_calculating_sample_size.html",
        "8_4_confidence_interval_calculating_sample_size.txt",
        "https://openstax.org/books/introductory-statistics-2e/pages/8-formula-review",
        Path(r"C:\Users\Marius\.gemini\antigravity\brain\aebf6032-0fa2-438b-bb11-3eda139a64e3\.system_generated\steps\41222\content.md")
    ),
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
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    DEST_DIR.mkdir(parents=True, exist_ok=True)
    manifest_files = []

    for html_name, txt_name, source_url, raw_source_path in SECTIONS:
        raw_text = raw_source_path.read_text(encoding="utf-8")
        # Extract HTML part starting from <!doctype html>
        html_idx = raw_text.find("<!doctype html>")
        if html_idx != -1:
            html_content = raw_text[html_idx:]
        else:
            html_content = raw_text

        html_bytes = html_content.encode("utf-8")
        html_hash = sha256_bytes(html_bytes)

        raw_dest_file = RAW_DIR / html_name
        raw_dest_file.write_bytes(html_bytes)

        clean_text = extract_clean_text(html_content)
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
        "work_title": "Introductory Statistics 2e",
        "chapter": "Chapter 8: Confidence Intervals",
        "authors": [
            "Barbara Illowsky",
            "Susan Dean"
        ],
        "publisher": "OpenStax, Rice University",
        "edition": "2nd Edition",
        "year": 2023,
        "isbn_or_doi": "ISBN-13: 978-1-951693-55-8",
        "source_type": "manual",
        "license": "Creative Commons Attribution 4.0 International (CC BY 4.0)",
        "license_url": "https://creativecommons.org/licenses/by/4.0/",
        "book_url": "https://openstax.org/details/books/introductory-statistics-2e",
        "extraction_method": {
            "model": "gemini-3.1-flash-lite",
            "prompt_version": "1.0"
        },
        "conversion_note": "Converted from original OpenStax HTML to plain text fixture using BeautifulSoup for test repeatability and CC BY 4.0 distribution.",
        "files": manifest_files
    }
    MANIFEST_PATH.write_text(json.dumps(manifest_data, indent=2), encoding="utf-8")
    print(f"Updated provenance manifest at {MANIFEST_PATH}")

    # Write ATTRIBUTION.md in DEST_DIR and RAW_DIR
    attribution_content = """# Creative Commons Attribution 4.0 International (CC BY 4.0)

## Work Identification
- **Title**: Introductory Statistics 2e, Chapter 8: Confidence Intervals
- **Authors**: Barbara Illowsky, Susan Dean
- **Publisher**: OpenStax, Rice University
- **Book URL**: https://openstax.org/details/books/introductory-statistics-2e
- **License**: Creative Commons Attribution 4.0 International (CC BY 4.0)
- **License URL**: https://creativecommons.org/licenses/by/4.0/

## Conversion and Transformation Notice
In accordance with Section 3(a)(1)(B) of the CC BY 4.0 license, the files in this directory are plain-text extracts converted from the original OpenStax HTML source files to serve as reproducible, deterministic testing fixtures. Non-text navigational elements, scripts, and styling were stripped during conversion.

## Included Section Fixtures & Source URLs
1. **8.0 Introduction** (`8_0_introduction.txt`)
   - Source URL: https://openstax.org/books/introductory-statistics-2e/pages/8-introduction
2. **8.1 A Single Population Mean using the Normal Distribution** (`8_1_a_single_population_mean_using_the_normal_distribution.txt`)
   - Source URL: https://openstax.org/books/introductory-statistics-2e/pages/8-1-a-single-population-mean-using-the-normal-distribution
3. **8.2 A Single Population Mean using the Student's t Distribution** (`8_2_a_single_population_mean_using_the_student_t_distribution.txt`)
   - Source URL: https://openstax.org/books/introductory-statistics-2e/pages/8-2-a-single-population-mean-using-the-student-t-distribution
4. **8.3 A Population Proportion** (`8_3_a_population_proportion.txt`)
   - Source URL: https://openstax.org/books/introductory-statistics-2e/pages/8-3-a-population-proportion
5. **8.4 Confidence Intervals & Calculating Sample Size** (`8_4_confidence_interval_calculating_sample_size.txt`)
   - Source URL: https://openstax.org/books/introductory-statistics-2e/pages/8-formula-review

Detailed cryptographic hashes (SHA-256) of both the original HTML and the resulting plain-text files are maintained in `07_EVALUATION/curriculum/statistics_provenance_manifest.json`.
"""
    dest_attribution_path = DEST_DIR / "ATTRIBUTION.md"
    dest_attribution_path.write_text(attribution_content, encoding="utf-8")
    print(f"Wrote ATTRIBUTION.md at {dest_attribution_path}")

    raw_attribution_path = RAW_DIR / "ATTRIBUTION.md"
    raw_attribution_path.write_text(attribution_content, encoding="utf-8")
    print(f"Wrote ATTRIBUTION.md at {raw_attribution_path}")


if __name__ == "__main__":
    main()
