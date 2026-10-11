import os
import hashlib
import subprocess

files_to_check = [
    "07_EVALUATION/polymarket/PRICE_TAPE_FINDINGS.md",
    "07_EVALUATION/polymarket/fixtures/real_clob_prices_history_4504950.json",
    "07_EVALUATION/polymarket/fixtures/real_clob_prices_history_4504950.provenance.json",
    "07_EVALUATION/polymarket/CORPUS_DEPTH_FINDINGS.md",
    "07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.json",
    "07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.provenance.json",
    "07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json",
    "07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.provenance.json",
    "07_EVALUATION/polymarket/MECHANICAL_CONTROL_RESULTS.md",
    "07_EVALUATION/polymarket/LEAKAGE_ADVERSARIAL_AUDIT.md",
    "07_EVALUATION/polymarket/fixtures/resolutions_attested_50.json",
    "07_EVALUATION/polymarket/fixtures/resolutions_attested_50.provenance.json",
]

base_dir = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY"
for f in files_to_check:
    p = os.path.join(base_dir, f)
    size = os.path.getsize(p)
    h = hashlib.sha256(open(p, "rb").read()).hexdigest()
    print(f"{f} | {size:,} bytes | {h}")
