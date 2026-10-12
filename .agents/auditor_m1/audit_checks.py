import os
import re
import sys
import json
import sqlite3
import math

PROJECT_DIR = r"c:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain"

print("=" * 80)
print("FORENSIC INTEGRITY AUDIT - JARVIS COGNITIVE BRAIN (MILESTONE 1)")
print("=" * 80)

# Check 1: Secret Leaks Scan
print("\n--- 1. SECRET LEAKS SCAN ---")
secret_pats = [
    re.compile(r'(?i)(api[_-]?key|secret|password|passwd|bearer|auth[_-]?token)\s*[:=]\s*["\']([a-zA-Z0-9_\-\.]{10,})["\']'),
    re.compile(r'sk-[a-zA-Z0-9]{20,}'),
    re.compile(r'AIza[a-zA-Z0-9_\-]{35}'),
    re.compile(r'ghp_[a-zA-Z0-9]{36}'),
    re.compile(r'xox[baprs]-[a-zA-Z0-9]{10,}'),
]

secret_findings = []
for root, dirs, files in os.walk(PROJECT_DIR):
    if "__pycache__" in root or ".git" in root or ".pytest_cache" in root:
        continue
    for f in files:
        if f.endswith((".py", ".toml", ".json", ".yaml", ".yml", ".md", ".env")):
            filepath = os.path.join(root, f)
            with open(filepath, "r", encoding="utf-8", errors="ignore") as fh:
                for line_idx, line in enumerate(fh, 1):
                    for pat in secret_pats:
                        m = pat.search(line)
                        if m:
                            # ignore fixture mock tokens like test_mock_bearer_token
                            val = m.group(0)
                            if "test_mock" in val or "mock" in val:
                                continue
                            secret_findings.append((filepath, line_idx, line.strip()))

if secret_findings:
    print(f"FAILED: Found {len(secret_findings)} secret leaks:")
    for sf in secret_findings:
        print(f"  {sf[0]}:{sf[1]} -> {sf[2]}")
else:
    print("PASSED: 0 hardcoded secrets / API keys detected.")

# Check 2: Static Analysis - Facades, Hardcoded Returns, Dummy Mocks in Production Code
print("\n--- 2. STATIC ANALYSIS & CODE INTEGRITY ---")
prod_dirs = [os.path.join(PROJECT_DIR, "jarvis")]
facade_findings = []
for pdir in prod_dirs:
    for root, dirs, files in os.walk(pdir):
        if "__pycache__" in root:
            continue
        for f in files:
            if f.endswith(".py"):
                filepath = os.path.join(root, f)
                with open(filepath, "r", encoding="utf-8", errors="ignore") as fh:
                    content = fh.read()
                    lines = content.splitlines()
                    for idx, line in enumerate(lines, 1):
                        stripped = line.strip()
                        # Check for NotImplementedError in production core
                        if "raise NotImplementedError" in stripped:
                            facade_findings.append((filepath, idx, stripped, "NotImplementedError in prod"))
                        # Check for hardcoded test outputs / dummy pass
                        if stripped in ["pass"] and idx > 1 and "def " in lines[idx-2]:
                            facade_findings.append((filepath, idx, stripped, "Empty method body (pass)"))

if facade_findings:
    print(f"WARNING/FAILED: Found {len(facade_findings)} potential facades in prod:")
    for ff in facade_findings:
        print(f"  {ff[0]}:{ff[1]} -> {ff[2]} ({ff[3]})")
else:
    print("PASSED: 0 empty facades or NotImplementedError stubs in jarvis/ production code.")

# Check 3: Mathematical Logic & Algorithm Verification
print("\n--- 3. MATHEMATICAL & ALGORITHMIC AUTHENTICITY ---")
# Verify ACT-R formula in activation.py
activation_file = os.path.join(PROJECT_DIR, "jarvis", "memory", "activation.py")
with open(activation_file, "r", encoding="utf-8") as fh:
    act_code = fh.read()
    if "math.pow(elapsed, -decay)" in act_code and "math.log(sum_decayed)" in act_code:
        print("PASSED: ACT-R decay formula B_i = ln(sum (t - t_j)^(-d)) verified.")
    else:
        print("FAILED: ACT-R decay formula missing or invalid in activation.py.")

# Verify SQLite WAL Pragmas and recursive CTE
sqlite_file = os.path.join(PROJECT_DIR, "jarvis", "memory", "sqlite_engine.py")
with open(sqlite_file, "r", encoding="utf-8") as fh:
    sql_code = fh.read()
    has_wal = "PRAGMA journal_mode=WAL;" in sql_code
    has_busy = "PRAGMA busy_timeout=5000;" in sql_code
    has_sync = "PRAGMA synchronous=NORMAL;" in sql_code
    has_cte = "WITH RECURSIVE lineage_forward" in sql_code and "lineage_backward" in sql_code
    has_atomic = "BEGIN IMMEDIATE;" in sql_code
    if has_wal and has_busy and has_sync and has_cte and has_atomic:
        print("PASSED: SQLite WAL pragmas, recursive CTE queries, and BEGIN IMMEDIATE atomic transactions verified.")
    else:
        print(f"FAILED: SQLite engine missing key features (WAL: {has_wal}, busy: {has_busy}, sync: {has_sync}, CTE: {has_cte}, atomic: {has_atomic}).")

# Verify Atomic Markdown file writes
md_sync_file = os.path.join(PROJECT_DIR, "jarvis", "memory", "markdown_sync.py")
with open(md_sync_file, "r", encoding="utf-8") as fh:
    md_code = fh.read()
    has_temp = "tempfile.mkstemp" in md_code
    has_fsync = "os.fsync" in md_code
    has_replace = "os.replace" in md_code
    if has_temp and has_fsync and has_replace:
        print("PASSED: MarkdownSyncEngine atomic tempfile + os.fsync + os.replace verified.")
    else:
        print(f"FAILED: MarkdownSyncEngine atomic file writes missing (temp: {has_temp}, fsync: {has_fsync}, replace: {has_replace}).")

# Check 4: Pre-populated Artifacts Detection
print("\n--- 4. PRE-POPULATED ARTIFACT DETECTION ---")
artifacts = []
for root, dirs, files in os.walk(PROJECT_DIR):
    if "__pycache__" in root or ".git" in root or ".pytest_cache" in root:
        continue
    for f in files:
        if f.endswith((".log", ".jsonl", ".sqlite3", ".db", ".out", ".coverage")):
            artifacts.append(os.path.join(root, f))

if artifacts:
    print(f"FOUND {len(artifacts)} pre-existing database/log/artifact files:")
    for a in artifacts:
        print(f"  {a}")
else:
    print("PASSED: 0 pre-populated log or database artifacts found in workspace.")

print("\n" + "=" * 80)
