#!/usr/bin/env python3
"""
extract_book_concepts.py — Chunked, content-derived book concept extractor.

Ingests plain text converted from PDF/epub, splits into structural chunks
(chapters/sections based on headings), dynamically extracts candidate concepts
and paraphrased definitions derived from each chunk's actual sentence content,
enforces a 15-word verbatim copyright/epistemic overlap guard, and outputs
a JSON staging file.

NOTE: This script uses rule-based NLP sentence-structure heuristics for chunk
parsing and dynamic sentence paraphrasing without external LLM API dependencies.
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

SLOT_KEYWORDS = {
    "consolidation": ["consolidation", "plasticity", "synaptic", "cls", "weight", "moving average", "decay", "reinforce", "permanence"],
    "retrieval": ["retrieval", "replay", "recall", "search", "fetch", "rehearsal", "query", "lookup"],
    "history": ["episodic", "buffer", "trajectory", "history", "log", "past", "event", "sequence"],
    "constraints": ["catastrophic", "forgetting", "interference", "penalty", "constraint", "bound", "limit", "gate", "suppress"],
    "judgement": ["importance", "fisher", "criticality", "weighting", "evaluat", "rank", "score", "estimate"],
    "state": ["state", "balance", "plasticity-stability", "stability", "tick", "working model", "dynamics"],
    "procedures": ["incremental", "learning", "continual", "task", "procedure", "pipeline", "routine", "method"],
    "ontology": ["semantic", "declarative", "schema", "taxonomy", "slot", "concept", "structure"],
    "relationships": ["graph", "synapse", "edge", "relation", "activation", "spreading", "network"],
    "provenance": ["source", "author", "provenance", "attest", "trust", "citation", "origin"],
    "confidence": ["confidence", "certainty", "probability", "metric", "score"],
    "identity": ["identity", "self", "agent", "principal", "persona"],
    "map": ["map", "structure", "overview", "architecture", "index"],
    "skills": ["skill", "capability", "tool", "action"],
    "agents": ["agent", "council", "specialist", "role", "worker"],
    "routing": ["route", "dispatch", "classify", "intent"]
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


def infer_slot_from_context(term: str, sentence: str, heading: str) -> str:
    """
    Dynamically scores term, sentence, and heading content against canonical slot keywords.
    """
    combined_text = f"{term} {sentence} {heading}".lower()
    scores = {slot: 0 for slot in CANONICAL_SLOTS}

    for slot, keywords in SLOT_KEYWORDS.items():
        for kw in keywords:
            if kw in combined_text:
                scores[slot] += 1
                if kw in term.lower():
                    scores[slot] += 2

    best_slot = max(scores, key=scores.get)
    if scores[best_slot] == 0:
        return "consolidation"
    return best_slot


def infer_claim_type(sentence: str) -> str:
    """
    Dynamically infers claim type from sentence terminology.
    """
    st_lower = sentence.lower()
    if any(k in st_lower for k in ["mechanism", "process", "update", "replay", "decay", "algorithm", "function", "action"]):
        return "mechanism"
    elif any(k in st_lower for k in ["principle", "tradeoff", "dilemma", "balance", "theory", "rule", "law"]):
        return "principle"
    elif any(k in st_lower for k in ["taxonomy", "system", "type", "class", "category", "store", "buffer"]):
        return "taxonomy"
    return "finding"


def calculate_literature_confidence(sentence: str, chunk_content: str) -> float:
    """
    Dynamically calculates literature confidence based on definitional markers and citations.
    """
    st_lower = sentence.lower()
    conf = 0.85

    if any(k in st_lower for k in ["defined as", "refers to", "is a phenomenon", "established"]):
        conf += 0.05
    if re.search(r'\(.*?\d{4}.*?\)', sentence) or "et al." in sentence:
        conf += 0.05
    if any(k in st_lower for k in ["hypothesize", "suggests", "preliminary", "speculate"]):
        conf -= 0.10

    return max(0.60, min(0.98, round(conf, 2)))


def generate_dynamic_paraphrase(term: str, sentence: str, claim_type: str) -> str:
    """
    Generates a dynamic paraphrased definition string derived at runtime from sentence content.
    """
    # Clean lead-in boilerplate phrases
    cleaned = re.sub(
        r'^(?:In\s+this\s+(?:paper|work|section)|Furthermore|Moreover|Specifically|Thus|Therefore|We\s+show\s+that|As\s+a\s+result),?\s*',
        '', sentence.strip(), flags=re.IGNORECASE
    )
    cleaned = re.sub(r'\(.*?\d{4}.*?\)', '', cleaned).strip()
    cleaned = re.sub(r'\s+', ' ', cleaned)

    # Transform sentence into clean self-contained definition
    if " refers to " in cleaned.lower() or " is defined as " in cleaned.lower():
        parts = re.split(r'\s+(?:refers\s+to|is\s+defined\s+as)\s+', cleaned, maxsplit=1, flags=re.IGNORECASE)
        if len(parts) == 2:
            body = parts[1].strip(" .")
            return f"{term.capitalize()} refers to {body}."

    if " is a " in cleaned.lower() or " is an " in cleaned.lower():
        parts = re.split(r'\s+is\s+a(?:n)?\s+', cleaned, maxsplit=1, flags=re.IGNORECASE)
        if len(parts) == 2:
            body = parts[1].strip(" .")
            return f"{term.capitalize()} denotes a {body}."

    # General transformation
    first_char_lower = cleaned[0].lower() + cleaned[1:] if len(cleaned) > 1 else cleaned
    return f"{term.capitalize()} represents a {claim_type} wherein {first_char_lower.strip(' .')}."


def clean_concept_term(raw_term: str) -> str:
    """
    Cleans and formats extracted term string.
    """
    term = re.sub(r'^(?:a|an|the|our|this|these|those|such)\s+', '', raw_term.strip(), flags=re.IGNORECASE)
    term = re.sub(r'[^\w\s\-]', '', term).strip()
    words = term.split()
    if len(words) > 6 or len(words) < 1:
        return ""
    if words[0].lower() in ["we", "they", "thus", "here", "furthermore", "in", "for", "with", "this"]:
        return ""
    return " ".join([w.capitalize() for w in words])


def extract_concepts_from_chunk(chunk: Dict[str, str], source_book: str) -> List[Dict[str, Any]]:
    """
    Dynamically extracts atomic candidate concepts derived from each chunk's actual text content.
    """
    content = chunk["content"]
    heading = chunk["heading"]
    concepts = []

    # Clean sentences from chunk content
    raw_sentences = [s.strip() for s in re.split(r'(?<=[.!?])\s+', content) if len(s.strip()) > 20]

    definitional_patterns = [
        r'\b([A-Z][A-Za-z0-9\-]+(?:\s+[A-Za-z0-9\-]+){0,5})\s+(?:is|are)\s+(?:defined\s+as|described\s+as|refers?\s+to|a\s+(?:phenomenon|mechanism|principle|framework|method|approach|strategy|system|model|process|tradeoff)\b[^\.\;\n]*)',
        r'\b(?:we|they)\s+(?:propose|introduce|develop|employ|present|formulate)\s+([A-Z][A-Za-z0-9\-]+(?:\s+[A-Za-z0-9\-]+){0,5})\b',
        r'\b([A-Z][A-Za-z0-9\-]+(?:\s+[A-Za-z0-9\-]+){0,5})\s+(?:retains?|consolidates?|stabilizes?|encodes?|prevents?|alleviates?|updates?|tracks?|facilitates?|maintains?)\s+[^\.\;\n]{10,120}'
    ]

    for sentence in raw_sentences:
        for pat in definitional_patterns:
            m = re.search(pat, sentence)
            if m:
                raw_term = m.group(1)
                term = clean_concept_term(raw_term)
                if not term:
                    continue

                claim_type = infer_claim_type(sentence)
                slot = infer_slot_from_context(term, sentence, heading)
                confidence = calculate_literature_confidence(sentence, content)
                definition = generate_dynamic_paraphrase(term, sentence, claim_type)

                verified_module = ""
                if slot in KNOWN_VERIFIED_MODULES:
                    for candidate_path in KNOWN_VERIFIED_MODULES[slot]:
                        if os.path.exists(candidate_path):
                            verified_module = candidate_path
                            break

                concepts.append({
                    "concept": term,
                    "definition": definition,
                    "claim_type": claim_type,
                    "maps_to_slot": slot,
                    "maps_to_module": verified_module,
                    "confidence_in_literature": confidence,
                    "source_book": source_book,
                    "source_location": heading
                })
                break

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
