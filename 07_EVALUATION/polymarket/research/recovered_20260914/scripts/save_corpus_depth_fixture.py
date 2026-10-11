import json
import hashlib
from datetime import datetime, timezone

scratch_path = "C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch"
with open(f"{scratch_path}/sampled_500_markets.json", "r", encoding="utf-8") as f:
    markets_500 = json.load(f)

with open(f"{scratch_path}/corpus_depth_results.json", "r", encoding="utf-8") as f:
    depth_res = json.load(f)

target_fixture = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.json"
with open(target_fixture, "w", encoding="utf-8") as f:
    json.dump(markets_500, f, indent=2)

with open(target_fixture, "rb") as f:
    sha256_hash = hashlib.sha256(f.read()).hexdigest()

target_prov = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.provenance.json"
prov_data = {
    "target_file": "corpus_depth_sample_500.json",
    "sha256": sha256_hash,
    "sample_size": len(markets_500),
    "acquired_at_utc": datetime.now(timezone.utc).isoformat(),
    "slices": [
        {"name": "era_2020_offset0", "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset=0", "count": 100, "first_id": "12"},
        {"name": "era_2022_offset500", "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset=500", "count": 100, "first_id": "213594"},
        {"name": "era_2023_offset1000", "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset=1000", "count": 100, "first_id": "238945"},
        {"name": "era_2024_offset1500", "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset=1500", "count": 100, "first_id": "239606"},
        {"name": "era_2026_recent", "url": "https://gamma-api.polymarket.com/markets?closed=true&limit=100&order=id&ascending=false", "count": 100, "first_id": "4537965"}
    ],
    "cascade_summary": depth_res["global_cascade"],
    "era_breakdown": depth_res["era_breakdown"]
}

with open(target_prov, "w", encoding="utf-8") as f:
    json.dump(prov_data, f, indent=2)

print(f"Written fixture ({len(markets_500)} markets, SHA-256: {sha256_hash}) and provenance.")
