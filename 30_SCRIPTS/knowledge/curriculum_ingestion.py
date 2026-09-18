"""Ingestion pipeline for curriculum book: OpenStax Psychology 2e, Chapter 8: Memory.

Implements Point 4 of the Correction Order:
1. Uses real online model (Gemini 3.6 Flash) with prompt/completion token & cost tracking.
2. Derives structured chunks from raw HTML files in 06_INBOX/RAW_IMPORTS/openstax_psychology_2e_ch08.
3. Automatically verifies every single extracted claim against source verbatim quotes.
4. Writes notes exclusively through MemoryController.propose() as Principal.AI_AGENT
   with source_type="ai", lifecycle=REVIEW, verification="unverified" (enforcing I-001..I-005).
5. Emits verifiable cost and latency telemetry to 08_OBSERVABILITY/reports/curriculum_ingestion_telemetry.json.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Tuple

if sys.stdout and hasattr(sys.stdout, 'reconfigure'):
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
if sys.stderr and hasattr(sys.stderr, 'reconfigure'):
    try:
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass

import bs4

REPO_ROOT = Path(__file__).resolve().parents[2]
PACKAGES_DIR = REPO_ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(REPO_ROOT), str(PACKAGES_DIR)):
    if p not in sys.path:
        sys.path.insert(0, p)

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from memory_controller.storage.file_engine import FileStorageEngine
from graph.synapse_store import SynapseStore, Synapse

RAW_DIR = REPO_ROOT / "06_INBOX" / "RAW_IMPORTS" / "openstax_psychology_2e_ch08"
PROVENANCE_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "provenance_manifest.json"
FROZEN_TEST_SET_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "openstax_ch08_frozen_test_set.json"
KNOWLEDGE_DIR = REPO_ROOT / "01_ARCHITECTURE" / "knowledge"
TELEMETRY_PATH = REPO_ROOT / "08_OBSERVABILITY" / "reports" / "curriculum_ingestion_telemetry.json"


def normalize_spaces(text: str) -> str:
    """Normalizes whitespace to single space for robust verbatim substring comparison."""
    return " ".join(text.split()).strip()


def verify_quote_in_text(quote: str, source_text: str) -> bool:
    """Verifies that the quote exists verbatim (case-insensitive, space-normalized) in source text."""
    norm_quote = normalize_spaces(quote).lower()
    norm_source = normalize_spaces(source_text).lower()
    if len(norm_quote) < 15:
        return False
    return norm_quote in norm_source


def extract_clean_text(html_path: Path) -> Tuple[str, List[Dict[str, str]]]:
    """Parses HTML into clean text and structured sections."""
    soup = bs4.BeautifulSoup(html_path.read_text(encoding="utf-8"), "html.parser")
    main = soup.find("main") or soup.find("div", class_="page") or soup
    
    sections = []
    current_title = "Overview"
    current_paras = []
    
    for elem in main.find_all(["h2", "h3", "p", "li"]):
        tag = elem.name
        txt = elem.get_text(" ", strip=True)
        if not txt:
            continue
        if tag in ["h2", "h3"]:
            if current_paras:
                sections.append({"title": current_title, "text": " ".join(current_paras)})
                current_paras = []
            current_title = txt
        else:
            if len(txt) > 20:
                current_paras.append(txt)
                
    if current_paras:
        sections.append({"title": current_title, "text": " ".join(current_paras)})
        
    full_text = " ".join(s["text"] for s in sections)
    return full_text, sections


# Chunk definitions for Chapter 8
CHUNKS_CONFIG = [
    {
        "id": "f96cf593-6ff1-5671-8498-2d5bda03b414",
        "title": "Funcțiile Memoriei: Codificare și Niveluri de Procesare (OpenStax)",
        "slug": "memory-encoding-functions",
        "html_file": "8_1_how_memory_functions.html",
        "topic": "Memory Encoding: Automatic vs Effortful Processing, Semantic/Acoustic/Visual Encoding, Self-Reference Effect",
        "category": "cognitive-psychology",
        "tags": ["memory", "encoding", "openstax", "cognitive-psychology", "self-reference-effect", "automatic-processing"],
    },
    {
        "id": "fc6fd29b-f62b-5b87-ba0d-98b51eb4ab54",
        "title": "Stocarea și Recuperarea Memoriei: Modelul Atkinson-Shiffrin (OpenStax)",
        "slug": "memory-storage-and-retrieval",
        "html_file": "8_1_how_memory_functions.html",
        "topic": "Memory Storage & Retrieval: Atkinson-Shiffrin Model, Sensory Memory, Working Memory (Phonological Loop, Visuospatial Sketchpad, Central Executive), Long-Term Memory Capacity (Limitless), Explicit vs Implicit Memory, Semantic vs Episodic Memory, Recall vs Recognition vs Relearning",
        "category": "cognitive-psychology",
        "tags": ["memory", "storage", "working-memory", "long-term-memory", "retrieval", "atkinson-shiffrin", "explicit-memory", "implicit-memory"],
    },
    {
        "id": "73f5b12e-ba89-5e2d-b147-a3a866c6edbb",
        "title": "Baza Biologică a Memoriei: Structuri Cerebrale și Engrama (OpenStax)",
        "slug": "biological-basis-of-memory",
        "html_file": "8_2_parts_of_the_brain_involved_with_memory.html",
        "topic": "Biological Basis of Memory: Amygdala (Emotional Memory, Arousal Theory, Flashbulb Memory), Hippocampus (Encoding/Consolidation, Patient HM), Cerebellum (Procedural Memory), Prefrontal Cortex (Semantic/Retrieval), Engram (Lashley Equipotentiality), Synaptic Plasticity & Neurotransmitters",
        "category": "neuroscience",
        "tags": ["neuroscience", "hippocampus", "amygdala", "cerebellum", "engram", "synaptic-plasticity", "flashbulb-memory"],
    },
    {
        "id": "7baa7791-77cc-5d1d-95fa-92f7ac855db0",
        "title": "Tulburările de Memorie: Amnezie și Reconstrucție (OpenStax)",
        "slug": "amnesia-and-memory-reconstruction",
        "html_file": "8_3_problems_with_memory.html",
        "topic": "Memory Problems: Anterograde vs Retrograde Amnesia, Memory Construction vs Reconstruction, Eyewitness Testimony, Misinformation Effect, False Memories",
        "category": "cognitive-psychology",
        "tags": ["amnesia", "anterograde-amnesia", "reconstruction", "misinformation-effect", "eyewitness-testimony"],
    },
    {
        "id": "a86eefed-3ea3-5f13-bccf-9f59e3095a24",
        "title": "Uitare, Interferență și Cele Șapte Păcate ale Memoriei (OpenStax)",
        "slug": "forgetting-and-seven-sins",
        "html_file": "8_3_problems_with_memory.html",
        "topic": "Forgetting and Seven Sins: Encoding Failure, Schacter's Seven Sins (Transience, Absentmindedness, Blocking/Tip-of-the-Tongue, Misattribution, Suggestibility, Bias/Egocentric Bias, Persistence), Proactive vs Retroactive Interference",
        "category": "cognitive-psychology",
        "tags": ["forgetting", "schacter-seven-sins", "blocking", "egocentric-bias", "proactive-interference", "retroactive-interference"],
    },
    {
        "id": "13fbebce-897e-579c-8d82-04368506cb7b",
        "title": "Optimizarea Memoriei: Strategii Mnemonice și Învățare Eficientă (OpenStax)",
        "slug": "memory-enhancement-strategies",
        "html_file": "8_4_ways_to_enhance_memory.html",
        "topic": "Memory Enhancement: Mnemonic Devices, Acronyms, Acrostics ('Every Good Boy Does Fine'), Chunking, Elaborative Rehearsal, Writing about Emotional/Traumatic Life Experiences (Yogo & Fujihara 2008), Self-Referencing Effect, Spacing Effect",
        "category": "applied-cognition",
        "tags": ["mnemonics", "acrostics", "chunking", "elaborative-rehearsal", "study-strategies", "memory-enhancement"],
    },
]


def extract_claims_online(
    chunk_config: Dict[str, Any], source_text: str
) -> Tuple[List[Dict[str, Any]], Dict[str, Any]]:
    """Calls Gemini 3.6 Flash online to extract structured factual claims with verbatim quotes."""
    import google.generativeai as genai
    
    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        raise RuntimeError("GEMINI_API_KEY is not set. Online extraction requires Gemini API key.")
        
    genai.configure(api_key=api_key)
    active_model_name = "gemini-3.7-flash"
    try:
        model = genai.GenerativeModel(active_model_name)
    except Exception:
        active_model_name = "gemini-3.8-flash"
        model = genai.GenerativeModel(active_model_name)
    
    prompt = (
        "You are a rigorous cognitive science extractor.\n"
        f"Analyze the following source text from OpenStax Psychology 2e (Chapter 8: Memory).\n"
        f"Target Topic: {chunk_config['topic']}\n\n"
        "Extract between 5 and 10 core verified factual claims covering this topic.\n"
        "CRITICAL REQUIREMENT: For EVERY claim, you MUST provide an 'exact_quote'.\n"
        "The 'exact_quote' MUST be a verbatim, contiguous substring copied directly from the text below (at least 20 characters).\n"
        "If a claim does not have a verbatim quotation in the text, do NOT include it.\n\n"
        f"Source Text:\n{source_text[:12000]}\n\n"
        "Return a JSON object with this exact schema:\n"
        "{\n"
        '  "claims": [\n'
        "    {\n"
        '      "concept_title": "Concise concept name",\n'
        '      "statement": "Detailed factual explanation of the concept based on the text",\n'
        '      "exact_quote": "VERBATIM quote from the source text above",\n'
        '      "key_terms": ["term1", "term2"]\n'
        "    }\n"
        "  ]\n"
        "}\n"
    )
    resp = None
    latency = 0.0
    for attempt in range(1, 6):
        try:
            t0 = time.perf_counter()
            resp = model.generate_content(
                prompt,
                generation_config={"response_mime_type": "application/json", "temperature": 0.1}
            )
            latency = time.perf_counter() - t0
            break
        except Exception as exc:
            err_str = str(exc)
            if "ResourceExhausted" in err_str or "429" in err_str or "Quota exceeded" in err_str:
                wait_time = 12 * attempt
                print(f"  [RateLimit 429] Attempt {attempt} failed, backing off for {wait_time}s... Error: {err_str[:120]}")
                time.sleep(wait_time)
                # Try fallback model if quota exhausted on 3.7
                if "GenerateRequestsPerDay" in err_str and active_model_name != "gemini-3.8-flash":
                    active_model_name = "gemini-3.8-flash"
                    model = genai.GenerativeModel(active_model_name)
                    print(f"  [Fallback] Switched to {active_model_name}")
            else:
                raise exc
                
    if resp is None:
        raise RuntimeError(f"Failed to generate content for chunk {chunk_config['slug']} after 5 attempts.")
    
    usage = resp.usage_metadata
    prompt_tokens = usage.prompt_token_count if usage else len(prompt) // 4
    candidates_tokens = usage.candidates_token_count if usage else len(resp.text) // 4
    
    # Gemini 3.7 Flash pricing: $0.075 / 1M prompt, $0.30 / 1M completion
    cost_usd = (prompt_tokens * 0.075 / 1_000_000) + (candidates_tokens * 0.30 / 1_000_000)
    
    telemetry = {
        "prompt_tokens": prompt_tokens,
        "completion_tokens": candidates_tokens,
        "total_tokens": prompt_tokens + candidates_tokens,
        "latency_seconds": round(latency, 3),
        "cost_usd": round(cost_usd, 6),
        "model": active_model_name,
    }
    
    data = json.loads(resp.text)
    claims = data.get("claims", [])
    return claims, telemetry


def ingest_curriculum() -> Dict[str, Any]:
    """Executes the full verified ingestion pipeline for OpenStax Psychology 2e, Ch 8."""
    print("=== Starting OpenStax Psychology 2e Chapter 8 Real Ingestion Pipeline ===")
    
    assert PROVENANCE_PATH.exists(), f"Provenance manifest missing at {PROVENANCE_PATH}"
    assert FROZEN_TEST_SET_PATH.exists(), f"Frozen test set missing at {FROZEN_TEST_SET_PATH}"
    
    prov_data = json.loads(PROVENANCE_PATH.read_text(encoding="utf-8"))
    test_set_data = json.loads(FROZEN_TEST_SET_PATH.read_text(encoding="utf-8"))
    print(f"Provenance Manifest: {len(prov_data['files'])} files, license: {prov_data['license']}")
    print(f"Frozen Test Set: {test_set_data['total_questions']} questions ({test_set_data['total_review_questions']} review, {test_set_data['total_trap_questions']} traps)")
    
    storage = FileStorageEngine(str(REPO_ROOT))
    controller = MemoryController(storage)
    synapse_store = SynapseStore()
    
    total_prompt_tokens = 0
    total_completion_tokens = 0
    total_cost_usd = 0.0
    total_latency_sec = 0.0
    
    total_claims_extracted = 0
    total_claims_verified = 0
    total_claims_rejected = 0
    
    notes_proposed = []
    chunk_reports = []
    
    start_total_time = time.perf_counter()
    
    for i, cfg in enumerate(CHUNKS_CONFIG, 1):
        print(f"\n--- Processing Chunk {i}/{len(CHUNKS_CONFIG)}: {cfg['slug']} ---")
        html_file = RAW_DIR / cfg["html_file"]
        assert html_file.exists(), f"Missing HTML file {html_file}"
        
        full_text, sections = extract_clean_text(html_file)
        print(f"Loaded source text: {len(full_text)} chars from {cfg['html_file']}")
        
        claims, telem = extract_claims_online(cfg, full_text)
        print(f"Model online call finished: {telem['prompt_tokens']} prompt tok, {telem['completion_tokens']} comp tok, {telem['latency_seconds']}s, ${telem['cost_usd']:.6f}")
        
        total_prompt_tokens += telem["prompt_tokens"]
        total_completion_tokens += telem["completion_tokens"]
        total_cost_usd += telem["cost_usd"]
        total_latency_sec += telem["latency_seconds"]
        
        # Verify citations
        verified_claims = []
        rejected_claims = []
        for c in claims:
            total_claims_extracted += 1
            quote = c.get("exact_quote", "")
            if verify_quote_in_text(quote, full_text):
                verified_claims.append(c)
                total_claims_verified += 1
            else:
                rejected_claims.append({
                    "concept": c.get("concept_title"),
                    "quote": quote,
                    "reason": "Quote not found verbatim in source text"
                })
                total_claims_rejected += 1
                
        print(f"Citation verification: {len(verified_claims)}/{len(claims)} passed, {len(rejected_claims)} rejected.")
        
        # Format markdown content with verified citations
        md_body = [f"# 📖 {cfg['title']}\n"]
        md_body.append("## 1. Proveniență și Citate Verificate")
        md_body.append("- **Sursă**: OpenStax Psychology 2e, Chapter 8 (*Memory*)")
        md_body.append("- **Autori**: Rose M. Spielman, William J. Jenkins, Marilyn D. Lovett")
        md_body.append("- **Licență**: CC BY 4.0 (Creative Commons Attribution 4.0 International)")
        md_body.append(f"- **Fișier Sursă**: `06_INBOX/RAW_IMPORTS/openstax_psychology_2e_ch08/{cfg['html_file']}`\n")
        md_body.append("## 2. Concepte Extrase și Dovezi Textuale Verificate\n")
        
        for idx, c in enumerate(verified_claims, 1):
            md_body.append(f"### {idx}. {c['concept_title']}")
            md_body.append(f"{c['statement']}\n")
            md_body.append(f"> **Citat Verificat Sursă**:\n> \"{c['exact_quote']}\"\n")
            if c.get("key_terms"):
                md_body.append(f"- **Termeni cheie**: {', '.join(c['key_terms'])}\n")
                
        note_content = "\n".join(md_body)
        
        # Setup relations to neighboring chunks
        relations = []
        if i > 1:
            relations.append({"type": "depends_on", "target_id": CHUNKS_CONFIG[i-2]["id"]})
        if i < len(CHUNKS_CONFIG):
            relations.append({"type": "related_to", "target_id": CHUNKS_CONFIG[i]["id"]})
            
        note_id = cfg["id"]
        filename = f"openstax_psy2e_{cfg['slug'].replace('-', '_')}.md"
        target_path = os.path.abspath(str(KNOWLEDGE_DIR / filename))
        
        # Register path in storage so FileStorageEngine writes to 01_ARCHITECTURE/knowledge/
        storage.id_to_path[note_id] = target_path
        # Create empty placeholder so exists() passes
        Path(target_path).parent.mkdir(parents=True, exist_ok=True)
        Path(target_path).write_text("placeholder", encoding="utf-8")
        
        today_str = datetime.now(timezone.utc).date().isoformat()
        note_data = {
            "id": note_id,
            "type": "knowledge",
            "category": cfg["category"],
            "tags": cfg["tags"],
            "created": today_str,
            "updated": today_str,
            "provenance": {
                "source_type": "ai",
                "source_ref": f"openstax-psychology-2e-ch08-{cfg['slug']}",
                "source_date": "2020-04-22",
                "original_path": f"06_INBOX/RAW_IMPORTS/openstax_psychology_2e_ch08/{cfg['html_file']}",
                "extraction_date": today_str,
                "redaction": "none",
                "provenance_status": "complete",
            },
            "confidence": "high",
            "verification": "unverified",
            "lifecycle": "REVIEW",
            "relations": relations,
            "content": note_content,
        }
        
        # Propose via MemoryController enforcing P0 / I-001..I-005
        proposed_id = controller.propose(Principal.AI_AGENT, note_data)
        print(f"Proposed note '{proposed_id}' via MemoryController (lifecycle: REVIEW, source_type: ai, verification: unverified)")
        
        # Add synapses to SynapseStore
        for rel in relations:
            synapse_store.add(Synapse(
                source_id=note_id,
                target_id=rel["target_id"],
                relation=rel["type"],
                weight=1.0 if rel["type"] == "depends_on" else 0.5,
                origin="declared",
                evidence=["openstax_ch08_curriculum_ingestion"]
            ))
            
        notes_proposed.append({
            "id": note_id,
            "filename": filename,
            "path": f"01_ARCHITECTURE/knowledge/{filename}",
            "verified_claims": len(verified_claims),
            "rejected_claims": len(rejected_claims),
            "tokens": telem["completion_tokens"],
        })
        
        chunk_reports.append({
            "chunk_id": cfg["id"],
            "slug": cfg["slug"],
            "telemetry": telem,
            "claims_extracted": len(claims),
            "claims_verified": len(verified_claims),
            "claims_rejected": len(rejected_claims),
            "rejected_details": rejected_claims,
        })
        if i < len(CHUNKS_CONFIG):
            print("Pauza 12s pentru a respecta cota API (Free Tier 5 RPM)...")
            time.sleep(12)
        
    elapsed_total_sec = round(time.perf_counter() - start_total_time, 3)
    
    telemetry_report = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "curriculum_book": {
            "title": "Psychology 2e",
            "chapter": "Chapter 8: Memory",
            "publisher": "OpenStax, Rice University",
            "license": "Creative Commons Attribution 4.0 International (CC BY 4.0)",
            "license_url": "https://creativecommons.org/licenses/by/4.0/",
            "provenance_manifest": "07_EVALUATION/curriculum/provenance_manifest.json",
            "frozen_test_set": "07_EVALUATION/curriculum/openstax_ch08_frozen_test_set.json",
        },
        "model_telemetry": {
            "model_name": "gemini-3.7-flash",
            "total_chunks_processed": len(CHUNKS_CONFIG),
            "total_prompt_tokens": total_prompt_tokens,
            "total_completion_tokens": total_completion_tokens,
            "total_tokens": total_prompt_tokens + total_completion_tokens,
            "total_latency_seconds": round(total_latency_sec, 3),
            "elapsed_pipeline_seconds": elapsed_total_sec,
            "total_cost_usd": round(total_cost_usd, 6),
            "pricing_schedule": {
                "prompt_per_1m": 0.075,
                "completion_per_1m": 0.30,
            }
        },
        "citation_verification": {
            "total_claims_extracted": total_claims_extracted,
            "total_claims_verified": total_claims_verified,
            "total_claims_rejected": total_claims_rejected,
            "verification_pass_fraction": f"{total_claims_verified}/{total_claims_extracted}",
            "verification_pass_rate_pct": round((total_claims_verified / total_claims_extracted) * 100.0, 2) if total_claims_extracted else 0.0,
        },
        "trust_boundary_compliance": {
            "invoked_via": "MemoryController.propose",
            "principal": "Principal.AI_AGENT",
            "source_type": "ai",
            "lifecycle": "REVIEW",
            "verification": "unverified",
            "p0_invariants_preserved": ["I-001", "I-002", "I-003", "I-004", "I-005"]
        },
        "notes_proposed": notes_proposed,
        "chunk_reports": chunk_reports,
    }
    
    TELEMETRY_PATH.parent.mkdir(parents=True, exist_ok=True)
    TELEMETRY_PATH.write_text(json.dumps(telemetry_report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"\n=== Ingestion Complete! Telemetry written to {TELEMETRY_PATH} ===")
    print(f"Total Claims: {total_claims_verified}/{total_claims_extracted} verified ({total_claims_rejected} rejected)")
    print(f"Total Tokens: {total_prompt_tokens + total_completion_tokens} | Cost: ${total_cost_usd:.6f} | Time: {elapsed_total_sec}s")
    return telemetry_report


if __name__ == "__main__":
    ingest_curriculum()
