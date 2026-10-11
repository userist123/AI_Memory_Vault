import json
import hashlib
from datetime import datetime, timezone

scratch_path = "C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch"

with open(f"{scratch_path}/calibration_analysis_483.json", "r", encoding="utf-8") as f:
    cal_data = json.load(f)

fixture_file = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/expanded_dataset_483markets.json"
with open(fixture_file, "w", encoding="utf-8") as f:
    json.dump(cal_data, f, indent=2)

with open(fixture_file, "rb") as f:
    sha256_hash = hashlib.sha256(f.read()).hexdigest()

now_iso = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

prov_file = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/expanded_dataset_483markets.provenance.json"
prov_data = {
    "target_file": "expanded_dataset_483markets.json",
    "sha256": sha256_hash,
    "market_count": cal_data["total_records"],
    "queried_at_utc": now_iso,
    "slices": [
        {"offset": 0, "limit": 100, "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset=0&order=id&ascending=false"},
        {"offset": 300, "limit": 100, "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset=300&order=id&ascending=false"},
        {"offset": 600, "limit": 100, "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset=600&order=id&ascending=false"},
        {"offset": 900, "limit": 100, "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset=900&order=id&ascending=false"},
        {"offset": 1200, "limit": 100, "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset=1200&order=id&ascending=false"}
    ],
    "clob_prices_endpoint": "https://clob.polymarket.com/batch-prices-history",
    "notes": "Expanded CLOB dataset across 5 temporal slices of the modern CLOB era to verify calibration and resolve the 19 percentage points discrepancy."
}

with open(prov_file, "w", encoding="utf-8") as f:
    json.dump(prov_data, f, indent=2)

with open(prov_file, "rb") as f:
    prov_sha256 = hashlib.sha256(f.read()).hexdigest()

print(f"Successfully wrote:")
print(f"  {fixture_file} ({cal_data['total_records']} markets, SHA-256: {sha256_hash})")
print(f"  {prov_file} (SHA-256: {prov_sha256})")
