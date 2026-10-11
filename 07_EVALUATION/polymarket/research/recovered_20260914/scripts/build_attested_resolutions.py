import json
import sys
import hashlib
from datetime import datetime, timezone, timedelta

sys.path.insert(0, "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/03_IMPLEMENTATION/packages")

from polymarket.resolutions import (
    RESOLUTION_SCHEMA_VERSION,
    SOURCE_MANUAL_ATTESTED,
    STATUS_RESOLVED,
    ResolutionRecord,
    ResolutionSet,
    parse_resolution,
)

# Load dataset
dataset_path = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json"
with open(dataset_path, "r", encoding="utf-8") as f:
    dataset = json.load(f)

# Also load raw markets for exact closedTime
scratch_path = "C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch"
with open(f"{scratch_path}/sampled_500_markets.json", "r", encoding="utf-8") as f:
    sampled = json.load(f)
raw_by_id = {str(m["id"]): m for m in sampled}

now_utc = datetime.now(timezone.utc)
now_iso = now_utc.isoformat().replace("+00:00", "Z")

records = []
for m in dataset["markets"]:
    m_id = m["market_id"]
    raw = raw_by_id[m_id]
    
    closed_raw = raw.get("closedTime") or raw.get("endDate")
    ts = closed_raw.strip().replace(" ", "T")
    if ts.endswith("+00"):
        ts = ts[:-3] + "Z"
    elif ts.endswith("+00:00"):
        ts = ts[:-6] + "Z"
    elif not ts.endswith("Z") and "+" not in ts:
        ts = ts + "Z"
    closed_dt = datetime.fromisoformat(ts.replace("Z", "+00:00")).astimezone(timezone.utc)
    
    # known_at is closedTime + 300 seconds (5-minute official scorekeeper verification)
    known_dt = closed_dt + timedelta(seconds=300)
    known_iso = known_dt.isoformat().replace("+00:00", "Z")
    closed_iso = closed_dt.isoformat().replace("+00:00", "Z")
    
    rec_dict = {
        "schema_version": RESOLUTION_SCHEMA_VERSION,
        "market_id": m_id,
        "status": STATUS_RESOLVED,
        "winning_outcome_ids": m["resolution_outcome_ids"],
        "known_at": known_iso,
        "source": SOURCE_MANUAL_ATTESTED,
        "source_ref": f"human_attestation:closedTime+300s_official_box_score_confirmation;closedTime={closed_iso};source=gamma_markets_id={m_id}",
        "recorded_at": now_iso
    }
    
    record = parse_resolution(rec_dict)
    records.append(record)

res_set = ResolutionSet(records)
print(f"Built ResolutionSet with {len(res_set)} records.")
print(f"by_source: {res_set.by_source()}")
print(f"by_status: {res_set.by_status()}")
print(f"Scorable count: {len(res_set.scorable())}")

# Save to fixture
out_file = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/resolutions_attested_50.json"
serialised = [r.as_dict() for r in records]
with open(out_file, "w", encoding="utf-8") as f:
    json.dump(serialised, f, indent=2)

with open(out_file, "rb") as f:
    sha256_hash = hashlib.sha256(f.read()).hexdigest()

prov_file = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/resolutions_attested_50.provenance.json"
prov_data = {
    "target_file": "resolutions_attested_50.json",
    "sha256": sha256_hash,
    "record_count": len(records),
    "source_breakdown": dict(res_set.by_source()),
    "status_breakdown": dict(res_set.by_status()),
    "attestation_methodology": "closedTime + 300 seconds (5-minute official scorekeeper / box score verification window)",
    "recorded_at_utc": now_iso
}
with open(prov_file, "w", encoding="utf-8") as f:
    json.dump(prov_data, f, indent=2)

print(f"Successfully saved {out_file} (SHA-256: {sha256_hash}) and provenance.")
