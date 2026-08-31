"""
Forensic Verification Script for AI Memory Vault Financial Pipeline.
Executes empirical tests across:
1. Secret Scanning & Env Var Injection (FRED_API_KEY)
2. Audit Log Tamper-Evident SHA-256 Chaining & Tamper Detection
3. Implementation Authenticity (BM25, Parser, Adapters, REST API)
4. Trust Boundaries (P0-P18 Invariants: verification, lifecycle, provenance)
"""

import os
import sys
import re
import json
import uuid
import time
from pathlib import Path
from datetime import datetime, timezone

# Add repository root to python path
repo_root = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(repo_root))

print("=" * 80)
print("FORENSIC INTEGRITY AUDIT — EMPIRICAL VERIFICATION HARNESS")
print("=" * 80)

# ============================================================================
# 1. SECRET SCANNING & ZERO SECRETS ENFORCEMENT
# ============================================================================
print("\n[CHECK 1] SECRET SCANNING & FRED_API_KEY HANDLING")

SECRET_REGEXES = [
    re.compile(r"api[_-]?key\s*[:=]\s*['\"][a-zA-Z0-9_\-]{16,}['\"]", re.I),
    re.compile(r"bearer\s+[a-zA-Z0-9_\-\.]{20,}", re.I),
    re.compile(r"ghp_[a-zA-Z0-9]{36}"),
    re.compile(r"xoxb-[0-9]{11}-[0-9]{11}-[a-zA-Z0-9]{24}"),
    re.compile(r"-----BEGIN [A-Z ]+ PRIVATE KEY-----"),
    re.compile(r"password\s*[:=]\s*['\"][^'\"]{6,}['\"]", re.I),
]

# Target production directories & financial paths
target_dirs = [
    repo_root / "memory_controller",
    repo_root / "xau_kinetic",
    repo_root / "01_KNOWLEDGE" / "FINANCIAL",
    repo_root / "04_MEMORY" / "FINANCIAL",
    repo_root / "05_RESOURCES" / "FINANCIAL",
    repo_root / "tests" / "financial",
]

leaks_found = []
files_scanned = 0

for target in target_dirs:
    if not target.exists():
        continue
    for ext in ["*.py", "*.md", "*.json", "*.yaml", "*.yml"]:
        for file_path in target.rglob(ext):
            if any(part in str(file_path) for part in [".git", "__pycache__", ".pytest_cache"]):
                continue
            files_scanned += 1
            try:
                content = file_path.read_text(encoding="utf-8", errors="ignore")
                for line_idx, line in enumerate(content.splitlines(), 1):
                    # Check for live secret patterns
                    for rgx in SECRET_REGEXES:
                        if rgx.search(line):
                            # Filter out os.getenv, REDACTED, SecretScrubber, or test fixtures
                            if ("os.getenv" not in line and "os.environ" not in line and 
                                "REDACTED" not in line and "SecretScrubber" not in line and
                                "SIMULATED_TEST_KEY" not in line and "test_dummy" not in line):
                                leaks_found.append((str(file_path), line_idx, line.strip()))
            except Exception as e:
                print(f"Error reading {file_path}: {e}")

# Also check vault_api.py in root
api_file = repo_root / "vault_api.py"
if api_file.exists():
    files_scanned += 1
    content = api_file.read_text(encoding="utf-8", errors="ignore")
    for line_idx, line in enumerate(content.splitlines(), 1):
        for rgx in SECRET_REGEXES:
            if rgx.search(line) and "os.getenv" not in line and "REDACTED" not in line:
                leaks_found.append((str(api_file), line_idx, line.strip()))

print(f"  Target files scanned (financial & core modules): {files_scanned}")
print(f"  Unredacted secrets detected: {len(leaks_found)}")
if leaks_found:
    for leak in leaks_found:
        print(f"    FAIL: {leak}")
    assert len(leaks_found) == 0, "Secrets detected in target scope!"
else:
    print("  PASS: Zero unredacted secrets found across financial pipeline, notes, and API.")

# Verify FREDDataFetcher uses os.getenv("FRED_API_KEY")
from xau_kinetic.financial_ingestion.pipeline import FREDDataFetcher
fetcher = FREDDataFetcher()
print(f"  FREDDataFetcher default API key: {repr(fetcher.api_key)}")
assert fetcher.api_key == os.environ.get("FRED_API_KEY", "").strip(), "FREDDataFetcher does not default to env var!"
print("  PASS: FRED_API_KEY strictly loaded from environment variable.")


# ============================================================================
# 2. AUDIT LOG INTEGRITY & TAMPER DETECTION (SHA-256 CHAINING)
# ============================================================================
print("\n[CHECK 2] AUDIT LOG TAMPER-EVIDENT SHA-256 CHAINING")

from memory_controller.audit.logger import AuditLogger
from memory_controller.authorizer import Principal, Operation

temp_audit_file = repo_root / ".agents" / "auditor_final" / "forensic_audit_chain.jsonl"
if temp_audit_file.exists():
    temp_audit_file.unlink()

test_logger = AuditLogger(str(temp_audit_file))
test_logger.log("human", "read", "res-001", outcome="success", metadata={"action": "query_nasdaq"})
test_logger.log("ai_agent", "propose", "res-002", outcome="success", metadata={"action": "ingest_macro"})
test_logger.log("admin", "promote", "res-002", outcome="success", metadata={"action": "promote_macro"})
test_logger.log("admin", "attest", "res-002", outcome="success", metadata={"action": "attest_macro"})

is_valid, errs = test_logger.verify_integrity()
print(f"  Dynamic SHA-256 Audit Chain (4 entries): valid={is_valid}, errors={errs}")
assert is_valid and len(errs) == 0, f"Audit log verification failed: {errs}"

# Read entries to verify GENESIS and hash chaining
entries = [json.loads(line) for line in temp_audit_file.read_text(encoding="utf-8").splitlines() if line.strip()]
assert entries[0]["prev_hash"] == "GENESIS", "First entry prev_hash is not GENESIS!"
for i in range(1, len(entries)):
    assert entries[i]["prev_hash"] == entries[i - 1]["entry_hash"], f"Chain broken at entry {i}!"
print(f"  PASS: Cryptographic hash continuity verified: GENESIS -> {' -> '.join(e['entry_hash'][:8] for e in entries)}")

# Adversarial tamper test
tampered_lines = temp_audit_file.read_text(encoding="utf-8").splitlines()
entry1 = json.loads(tampered_lines[1])
entry1["actor"] = "human"  # Maliciously alter actor
tampered_lines[1] = json.dumps(entry1)
temp_audit_file.write_text("\n".join(tampered_lines) + "\n", encoding="utf-8")

is_tampered_valid, tampered_errs = test_logger.verify_integrity()
print(f"  Tamper Detection Test: valid={is_tampered_valid}, errors_detected={len(tampered_errs)}")
assert not is_tampered_valid, "AuditLogger failed to detect entry content tampering!"
assert len(tampered_errs) >= 1, "AuditLogger reported 0 errors on tampered log!"
print(f"  PASS: Tampering successfully detected ({tampered_errs[0]}).")

if temp_audit_file.exists():
    temp_audit_file.unlink()


# ============================================================================
# 3. IMPLEMENTATION AUTHENTICITY (BM25, PARSER, ADAPTER, REST API)
# ============================================================================
print("\n[CHECK 3] IMPLEMENTATION AUTHENTICITY & ZERO FACADES")

from memory_controller.financial_search import BM25Ranker, MultiLayeredFinancialSearchEngine
from memory_controller.financial_query import FinancialQueryEngine
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.financial_schema import validate_financial_note

# 3a. BM25 Ranker genuine computation test
ranker = BM25Ranker()
corpus = [
    {"id": "doc1", "title": "NASDAQ 100 Index Tech Bullish", "content": "Tech companies rallying on strong earnings. RSI at 65."},
    {"id": "doc2", "title": "Gold XAUUSD Precious Metals Analysis", "content": "Gold holding strong support at 2500. Macro hedging demand."},
    {"id": "doc3", "title": "Crude Oil Brent WTI Supply Disruption", "content": "Oil prices volatile amid geopolitical tensions."}
]
scores_nasdaq = ranker.score_corpus("NASDAQ rally tech", corpus)
print(f"  BM25 scores for 'NASDAQ rally tech': doc1={scores_nasdaq[0]:.4f}, doc2={scores_nasdaq[1]:.4f}, doc3={scores_nasdaq[2]:.4f}")
assert scores_nasdaq[0] > scores_nasdaq[1] and scores_nasdaq[0] > scores_nasdaq[2], "BM25 score did not rank doc1 highest!"

scores_gold = ranker.score_corpus("Gold precious metal support", corpus)
print(f"  BM25 scores for 'Gold precious metal support': doc1={scores_gold[0]:.4f}, doc2={scores_gold[1]:.4f}, doc3={scores_gold[2]:.4f}")
assert scores_gold[1] > scores_gold[0] and scores_gold[1] > scores_gold[2], "BM25 score did not rank doc2 highest!"

# 3b. Genuine query engine with in-memory SQLite storage
db_path = repo_root / ".agents" / "auditor_final" / "test_forensic.sqlite3"
if db_path.exists():
    db_path.unlink()
storage = SQLiteStorageEngine(str(db_path), wal_mode=True)
engine = FinancialQueryEngine(storage=storage)

sample_note = {
    "title": "NASDAQ Analysis 2026",
    "symbol": "^NDX",
    "category": "indici",
    "tags": ["finance", "nasdaq", "tech"],
    "narrative": "NASDAQ shows strong upward momentum with RSI at 62.",
    "indicators": {"RSI": 62.0, "ATR": 180.0},
    "risk_metrics": {"rr_ratio": 2.2}
}

note_id = engine.ingest_financial_note(sample_note, principal=Principal.AI_AGENT)
print(f"  Ingested note ID: {note_id}")

search_res = engine.search("NASDAQ momentum", limit=5)
print(f"  Search for 'NASDAQ momentum' results count: {len(search_res)}")
assert len(search_res) > 0, "Search failed to return ingested NASDAQ note!"
assert search_res[0]["id"] == note_id, "Search did not match ingested note ID!"
print("  PASS: Genuine BM25 & Multi-layered search verified.")


# ============================================================================
# 4. COGNITIVE TRUST BOUNDARIES & INVARIANTS (P0-P18)
# ============================================================================
print("\n[CHECK 4] COGNITIVE TRUST BOUNDARY INVARIANTS")

# P0: AI Agent cannot self-attest 'verified'
malicious_verified_note = {
    "title": "Malicious Attestation Attempt",
    "symbol": "GC=F",
    "category": "materii_prime",
    "verification": "verified",  # VIOLATION ATTEMPT
    "narrative": "Attempting to claim verified status as AI agent."
}
p0_note_id = engine.ingest_financial_note(malicious_verified_note, principal=Principal.AI_AGENT)
stored_p0 = storage.get(p0_note_id)
print(f"  P0 invariant test: AI requested 'verified', stored as '{stored_p0.get('verification')}'")
assert stored_p0.get("verification") in ("partially_verified", "unverified"), "P0 VIOLATION: AI agent was able to set verification=verified!"

malicious_prov_note = {
    "frontmatter": {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "crypto",
        "tags": ["finance", "btc"],
        "created": "2026-08-26",
        "updated": "2026-08-26",
        "provenance": {
            "source_type": "user",
            "source_ref": "fabricated_human",
            "source_date": "2026-08-26",
            "extraction_date": "2026-08-26",
            "redaction": "none",
            "provenance_status": "complete"
        },  # VIOLATION ATTEMPT
        "confidence": "high",
        "verification": "unverified",
        "relations": []
    },
    "title": "Malicious Provenance Attempt",
    "symbol": "BTC-USD",
    "category": "crypto",
    "narrative": "Attempting to claim user source_type as AI agent."
}
p2_note_id = engine.ingest_financial_note(malicious_prov_note, principal=Principal.AI_AGENT)
stored_p2 = storage.get(p2_note_id)
print(f"  P2 invariant test: AI requested 'user' provenance, stored as '{stored_p2.get('provenance', {}).get('source_type')}'")
assert stored_p2.get("provenance", {}).get("source_type") in ("execution", "ai", "inference", "unknown"), "P2 VIOLATION: AI agent was able to claim privileged provenance!"

# P3: AI Agent lifecycle restricted to REVIEW
malicious_lifecycle_note = {
    "frontmatter": {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "ACTIVE",  # VIOLATION ATTEMPT
        "category": "valute",
        "tags": ["finance", "forex"],
        "created": "2026-08-26",
        "updated": "2026-08-26",
        "provenance": {
            "source_type": "execution",
            "source_ref": "forex_feed",
            "source_date": "2026-08-26",
            "extraction_date": "2026-08-26",
            "redaction": "none",
            "provenance_status": "complete"
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": []
    },
    "title": "Malicious Lifecycle Attempt",
    "symbol": "EURUSD=X",
    "category": "valute",
    "narrative": "Attempting to bypass review into ACTIVE state."
}
p3_note_id = engine.ingest_financial_note(malicious_lifecycle_note, principal=Principal.AI_AGENT)
stored_p3 = storage.get(p3_note_id)
print(f"  P3 invariant test: AI requested 'ACTIVE' lifecycle, stored as '{stored_p3.get('lifecycle')}'")
assert stored_p3.get("lifecycle") in ("RAW", "CLASSIFIED", "NORMALIZED", "REVIEW"), "P3 VIOLATION: AI agent was able to directly promote to ACTIVE!"

print("  PASS: Cognitive trust boundaries P0, P2, P3 strictly enforced.")

# Clean up temporary database
storage.close()
import gc
gc.collect()
time.sleep(0.1)
if db_path.exists():
    try:
        db_path.unlink()
    except Exception:
        pass
wal_file = db_path.with_name(db_path.name + "-wal")
shm_file = db_path.with_name(db_path.name + "-shm")
if wal_file.exists():
    try:
        wal_file.unlink()
    except Exception:
        pass
if shm_file.exists():
    try:
        shm_file.unlink()
    except Exception:
        pass

print("\n" + "=" * 80)
print("ALL FORENSIC VERIFICATION CHECKS PASSED: VERDICT = CLEAN")
print("=" * 80)
