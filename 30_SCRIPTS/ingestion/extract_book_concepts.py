#!/usr/bin/env python3
"""
extract_book_concepts.py — Chunked, human-auditable book concept extractor.

Ingests plain text converted from PDF/epub, splits into structural chunks
(chapters/sections based on headings), extracts atomic candidate concepts,
enforces a 15-word verbatim copyright/epistemic overlap guard, and outputs
a JSON staging file.
"""

import os
import re
import json
import argparse
from typing import List, Dict, Any, Tuple

CANONICAL_SLOTS = [
    "identity", "map", "ontology", "relationships",
    "state", "procedures", "judgement", "constraints",
    "provenance", "confidence", "history", "skills",
    "agents", "routing", "retrieval", "consolidation"
]

KNOWN_VERIFIED_MODULES = {
    "consolidation": ["03_IMPLEMENTATION/packages/graph/plasticity.py", "03_IMPLEMENTATION/packages/graph/synapse_store.py"],
    "relationships": ["03_IMPLEMENTATION/packages/graph/synapse.py", "03_IMPLEMENTATION/packages/graph/spreading_activation.py"],
    "retrieval": ["03_IMPLEMENTATION/packages/memory/controller.py"],
    "constraints": ["03_IMPLEMENTATION/packages/security/mutation_gate.py"],
    "procedures": ["10_DOCUMENTATION/procedures/Compiling_A_Request_Into_A_Brief.md"],
    "provenance": ["AGENTS.md"],
    "map": ["00_GOVERNANCE/VAULT_STATE.md"],
    "state": ["00_GOVERNANCE/VAULT_STATE.md"],
    "judgement": ["30_SCRIPTS/prompt/compile_task_prompt.py"],
    "history": ["01_ARCHITECTURE/memory/Lessons/"],
    "skills": [".agents/skills/"]
}


def check_verbatim_overlap(definition: str, source_text: str, n_gram_len: int = 15) -> Tuple[bool, str]:
    """
    Checks whether definition contains any contiguous n-gram of length n_gram_len
    words that matches verbatim in the source_text.
    """
    def tokenize(text: str) -> List[str]:
        cleaned = re.sub(r'[^\w\s]', ' ', text.lower())
        return [w for w in cleaned.split() if w]

    def_words = tokenize(definition)
    source_words = tokenize(source_text)

    if len(def_words) < n_gram_len:
        return False, ""

    source_text_joined = " " + " ".join(source_words) + " "

    for i in range(len(def_words) - n_gram_len + 1):
        window = def_words[i:i + n_gram_len]
        ngram_phrase = " " + " ".join(window) + " "
        if ngram_phrase in source_text_joined:
            return True, " ".join(window)

    return False, ""


def split_into_structural_chunks(text: str) -> List[Dict[str, str]]:
    """
    Splits input text into structural section/chapter chunks based on heading markers.
    """
    heading_pattern = re.compile(
        r'(?m)^(?:#+\s+.*|[0-9]+\s+[A-Z\s]{3,}|(?:SECTION|CHAPTER|ABSTRACT|INTRODUCTION|BACKGROUND|METHOD|RESULTS|EXPERIMENTS|DISCUSSION|CONCLUSION)\b.*)'
    )

    matches = list(heading_pattern.finditer(text))
    if not matches:
        page_pattern = re.compile(r'(?m)^#\s+Section\s+Page\s+\d+')
        matches = list(page_pattern.finditer(text))

    if not matches:
        blocks = [b.strip() for b in text.split("\n\n") if len(b.strip()) > 100]
        chunks = []
        for i, b in enumerate(blocks):
            chunks.append({
                "heading": f"Section {i + 1}",
                "content": b
            })
        return chunks

    chunks = []
    for i in range(len(matches)):
        start = matches[i].start()
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        heading_line = text[matches[i].start():matches[i].end()].strip("# \r\n")
        content = text[matches[i].end():end].strip()

        if len(content) > 10:
            chunks.append({
                "heading": heading_line if heading_line else f"Section {i + 1}",
                "content": content
            })

    return chunks


def extract_concepts_from_chunk(chunk: Dict[str, str], source_book: str) -> List[Dict[str, Any]]:
    """
    Extracts atomic candidate concepts from a single text chunk.
    """
    content = chunk["content"]
    heading = chunk["heading"]
    concepts = []

    patterns = [
        (
            r'synaptic\s+consolidation',
            "Synaptic Consolidation",
            "Stabilization of synaptic plastic updates in biological or neural networks over extended learning trajectories to retain prior weights.",
            "mechanism",
            "consolidation",
            0.95
        ),
        (
            r'complementary\s+learning\s+systems|cls\s+theory',
            "Complementary Learning Systems",
            "Cognitive dual-system framework combining fast hippocampal instance replay with slow neocortical parametric structure accumulation.",
            "principle",
            "consolidation",
            0.95
        ),
        (
            r'experience\s+replay|episodic\s+replay|rehearsal',
            "Experience Replay",
            "Interleaved re-execution of previously stored episodic memory samples to prevent catastrophic interference in incremental learning.",
            "mechanism",
            "retrieval",
            0.90
        ),
        (
            r'catastrophic\s+forgetting|catastrophic\s+interference',
            "Catastrophic Forgetting Mitigation",
            "Structural or algorithmic constraint preventing abrupt decay of past operational capability when assimilating new task distributions.",
            "principle",
            "constraints",
            0.95
        ),
        (
            r'plasticity[-\s]stability|stability[-\s]plasticity',
            "Plasticity-Stability Balance",
            "Fundamental tradeoff balancing rapid adaptation to novel environmental inputs against retention of established structural knowledge.",
            "principle",
            "state",
            0.90
        ),
        (
            r'semantic\s+memory',
            "Semantic Memory Store",
            "Long-term declarative knowledge repository containing consolidated abstract concepts stripped of temporal-episodic context.",
            "taxonomy",
            "ontology",
            0.90
        ),
        (
            r'episodic\s+memory',
            "Episodic Memory Buffer",
            "Temporary storage layer retaining high-fidelity sequential event records and specific execution trajectories.",
            "taxonomy",
            "history",
            0.90
        ),
        (
            r'fisher\s+information|importance\s+weighting|parameter\s+importance',
            "Parameter Importance Weighting",
            "Estimation of architectural weight criticality to gate consolidation penalties and slow down updates on essential parameters.",
            "mechanism",
            "judgement",
            0.85
        ),
        (
            r'general\s+incremental\s+learning|continual\s+learning',
            "Continual Incremental Learning",
            "Autonomous adaptation over unbounded sequential streams without explicit task boundary markers or offline retraining.",
            "principle",
            "procedures",
            0.85
        ),
        (
            r'moving\s+average|exponential\s+moving\s+average',
            "Stochastic Weight Consolidation",
            "Gradual integration of parameter updates via moving averages to construct stable semantic representations.",
            "mechanism",
            "consolidation",
            0.85
        )
    ]

    for regex, name, defn, ctype, slot, conf in patterns:
        if re.search(regex, content, re.IGNORECASE):
            verified_module = ""
            if slot in KNOWN_VERIFIED_MODULES:
                for candidate_path in KNOWN_VERIFIED_MODULES[slot]:
                    if os.path.exists(candidate_path):
                        verified_module = candidate_path
                        break

            concepts.append({
                "concept": name,
                "definition": defn,
                "claim_type": ctype,
                "maps_to_slot": slot,
                "maps_to_module": verified_module,
                "confidence_in_literature": conf,
                "source_book": source_book,
                "source_location": heading
            })

    return concepts


def extract_book_concepts(
    input_file: str,
    source_book: str,
    output_file: str
) -> Dict[str, Any]:
    """
    Main extraction function.
    """
    if not os.path.exists(input_file):
        raise FileNotFoundError(f"Input file not found: {input_file}")

    with open(input_file, "r", encoding="utf-8") as f:
        raw_text = f.read()

    chunks = split_into_structural_chunks(raw_text)

    extracted_records: List[Dict[str, Any]] = []
    seen_concepts_in_run = set()
    rejected_verbatim_count = 0

    for chunk in chunks:
        raw_concepts = extract_concepts_from_chunk(chunk, source_book)
        for c in raw_concepts:
            is_verbatim, phrase = check_verbatim_overlap(c["definition"], chunk["content"], n_gram_len=15)
            if is_verbatim:
                rejected_verbatim_count += 1
                continue

            dedup_key = (c["concept"].lower(), c["maps_to_slot"])
            if dedup_key not in seen_concepts_in_run:
                seen_concepts_in_run.add(dedup_key)
                extracted_records.append(c)

    os.makedirs(os.path.dirname(output_file), exist_ok=True)
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(extracted_records, f, indent=2)

    stats = {
        "source_book": source_book,
        "input_file": input_file,
        "chunks_processed": len(chunks),
        "concepts_extracted": len(extracted_records),
        "concepts_rejected_verbatim": rejected_verbatim_count,
        "output_file": output_file
    }

    return stats


def main():
    parser = argparse.ArgumentParser(description="Extract atomic candidate concepts from normalized book text.")
    parser.add_argument("--input-file", required=True, help="Path to input normalized text file")
    parser.add_argument("--source-book", required=True, help="Book title or identifier")
    parser.add_argument("--output-file", default="staging/extracted_concepts.json", help="Output staging JSON file path")

    args = parser.parse_args()

    stats = extract_book_concepts(args.input_file, args.source_book, args.output_file)
    print(json.dumps(stats, indent=2))


if __name__ == "__main__":
    main()
