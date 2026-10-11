import os
import hashlib
import subprocess

files_to_check = [
    # Etapa 1
    "07_EVALUATION/polymarket/PRICE_TAPE_FINDINGS.md",
    "07_EVALUATION/polymarket/fixtures/real_clob_prices_history_4504950.json",
    "07_EVALUATION/polymarket/fixtures/real_clob_prices_history_4504950.provenance.json",
    # Etapa 2
    "07_EVALUATION/polymarket/CORPUS_DEPTH_FINDINGS.md",
    "07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.json",
    "07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.provenance.json",
    # Etapa 3
    "07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json",
    "07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.provenance.json",
    # Etapa 4
    "07_EVALUATION/polymarket/MECHANICAL_CONTROL_RESULTS.md",
    # Etapa 5
    "07_EVALUATION/polymarket/LEAKAGE_ADVERSARIAL_AUDIT.md",
    # Etapa 6
    "07_EVALUATION/polymarket/fixtures/resolutions_attested_50.json",
    "07_EVALUATION/polymarket/fixtures/resolutions_attested_50.provenance.json",
]

base_dir = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY"

print("=== File Manifest (Repo Additions) ===")
file_table = []
for rel_path in files_to_check:
    abs_path = os.path.join(base_dir, rel_path)
    if os.path.exists(abs_path):
        size = os.path.getsize(abs_path)
        with open(abs_path, "rb") as f:
            h = hashlib.sha256(f.read()).hexdigest()
        file_table.append((rel_path, size, h))
        print(f"File: {rel_path}\n  Size: {size:,} bytes\n  SHA-256: {h}\n")
    else:
        print(f"MISSING: {rel_path}")

print("\n=== Scratch Scripts Added ===")
scratch_dir = "C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch"
for fname in sorted(os.listdir(scratch_dir)):
    fpath = os.path.join(scratch_dir, fname)
    if os.path.isfile(fpath) and fname.endswith(".py"):
        size = os.path.getsize(fpath)
        with open(fpath, "rb") as f:
            h = hashlib.sha256(f.read()).hexdigest()
        print(f"Scratch: scratch/{fname} ({size:,} bytes, SHA-256: {h})")

# Check git diffs
print("\n=== Git Invariants ===")
diff1 = subprocess.check_output(["git", "diff", "--stat", "03_IMPLEMENTATION/packages/polymarket/"], cwd=base_dir).decode("utf-8")
print(f"git diff --stat 03_IMPLEMENTATION/packages/polymarket/: '{diff1.strip()}' (Empty: {len(diff1.strip()) == 0})")

diff2 = subprocess.check_output(["git", "diff", "--stat", "01_ARCHITECTURE/ontology/slots/"], cwd=base_dir).decode("utf-8")
print(f"git diff --stat 01_ARCHITECTURE/ontology/slots/: '{diff2.strip()}' (Empty: {len(diff2.strip()) == 0})")
