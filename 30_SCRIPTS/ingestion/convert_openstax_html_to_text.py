#!/usr/bin/env python3
"""convert_openstax_html_to_text.py — Prepares OpenStax Chapter 8 HTML into normalized text for chunking."""

import glob
import os
import re
from bs4 import BeautifulSoup

INPUT_DIR = "06_INBOX/RAW_IMPORTS/openstax_psychology_2e_ch08"
OUTPUT_FILE = "06_INBOX/RAW_IMPORTS/openstax_psychology_2e_ch08.txt"

sections = [
    "8_1_how_memory_functions.html",
    "8_2_parts_of_the_brain_involved_with_memory.html",
    "8_3_problems_with_memory.html",
    "8_4_ways_to_enhance_memory.html",
]

full_text = []

for sec in sections:
    path = os.path.join(INPUT_DIR, sec)
    if not os.path.exists(path):
        continue
    soup = BeautifulSoup(open(path, encoding="utf-8"), "html.parser")
    
    # Extract main content
    title = sec.replace(".html", "").replace("_", " ").title()
    full_text.append(f"# {title}\n")
    
    # Process headings and paragraphs
    for tag in soup.find_all(["h1", "h2", "h3", "p", "blockquote"]):
        text = tag.get_text().strip()
        if not text:
            continue
        # Skip site navigation artifacts
        if "Skip to Content" in text or "Keyboard s" in text or "Live Content" in text:
            continue
        if tag.name == "h1":
            full_text.append(f"\n# {text}\n")
        elif tag.name == "h2":
            full_text.append(f"\n## {text}\n")
        elif tag.name == "h3":
            full_text.append(f"\n### {text}\n")
        else:
            full_text.append(f"{text}\n")

combined = "\n".join(full_text)
with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
    f.write(combined)

print(f"Generated {OUTPUT_FILE}: {len(combined):,} characters.")
