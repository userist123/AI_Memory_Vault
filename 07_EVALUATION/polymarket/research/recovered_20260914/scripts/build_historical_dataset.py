import socket
import urllib.request
import urllib.parse
import json
import time
import math
import hashlib
from datetime import datetime, timezone

orig_getaddrinfo = socket.getaddrinfo
def patched_getaddrinfo(host, port, *args, **kwargs):
    if "polymarket.com" in host:
        return orig_getaddrinfo("172.64.153.51", port, *args, **kwargs)
    return orig_getaddrinfo(host, port, *args, **kwargs)
socket.getaddrinfo = patched_getaddrinfo

scratch_path = "C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch"
with open(f"{scratch_path}/corpus_depth_results.json", "r", encoding="utf-8") as f:
    depth_res = json.load(f)

qualifying = depth_res["f5_qualifying_markets"]
with open(f"{scratch_path}/sampled_500_markets.json", "r", encoding="utf-8") as f:
    sampled = json.load(f)
raw_by_id = {str(m["id"]): m for m in sampled}

target_50 = qualifying[:50]
all_token_ids = []
for item in target_50:
    for tok in item["tokens"]:
        if tok not in all_token_ids:
            all_token_ids.append(tok)

history_by_token = {}
for i in range(0, len(all_token_ids), 20):
    chunk = all_token_ids[i:i+20]
    time.sleep(0.3)
    req = urllib.request.Request(
        "https://clob.polymarket.com/batch-prices-history",
        data=json.dumps({"markets": chunk, "interval": "all"}).encode("utf-8"),
        headers={"Accept": "application/json", "Content-Type": "application/json", "User-Agent": "ai-memory-vault-research"},
        method="POST"
    )
    with urllib.request.urlopen(req, timeout=15) as resp:
        d = json.loads(resp.read().decode("utf-8"))
        history_by_token.update(d.get("history", {}))

def clean_iso(ts_str: str) -> str:
    # Handles strings like "2026-09-13 20:46:54+00" or "2026-09-13T20:46:54Z"
    ts = ts_str.strip().replace(" ", "T")
    if ts.endswith("+00"):
        ts = ts[:-3] + "Z"
    elif ts.endswith("+00:00"):
        ts = ts[:-6] + "Z"
    elif not ts.endswith("Z") and "+" not in ts:
        ts = ts + "Z"
    # validate by parsing
    dt = datetime.fromisoformat(ts.replace("Z", "+00:00")).astimezone(timezone.utc)
    return dt.isoformat().replace("+00:00", "Z")

now_utc = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
dataset_markets = []
provenance_markets = []

for q in target_50:
    m_id = str(q["id"])
    raw = raw_by_id[m_id]
    outcomes = json.loads(raw["outcomes"]) if isinstance(raw["outcomes"], str) else raw["outcomes"]
    token_ids = json.loads(raw["clobTokenIds"]) if isinstance(raw["clobTokenIds"], str) else raw["clobTokenIds"]
    outcome_prices = json.loads(raw["outcomePrices"]) if isinstance(raw["outcomePrices"], str) else raw["outcomePrices"]
    
    parsed_prices = [float(p) for p in outcome_prices]
    winner_idx = [i for i, p in enumerate(parsed_prices) if p >= 0.95][0]
    winning_token_id = token_ids[winner_idx]
    
    res_time_raw = raw.get("closedTime") or raw.get("endDate")
    res_time = clean_iso(res_time_raw) if res_time_raw else now_utc
            
    points = []
    earliest_t = None
    latest_t = None
    
    for tok in token_ids:
        raw_pts = history_by_token.get(tok, [])
        for pt in raw_pts:
            if "t" not in pt or "p" not in pt:
                continue
            t_val = float(pt["t"])
            p_val = float(pt["p"])
            if not (0.0 < p_val < 1.0):
                continue
            obs_iso = datetime.fromtimestamp(t_val, tz=timezone.utc).isoformat().replace("+00:00", "Z")
            if earliest_t is None or t_val < earliest_t:
                earliest_t = t_val
            if latest_t is None or t_val > latest_t:
                latest_t = t_val
            points.append({
                "market_id": m_id,
                "outcome_id": tok,
                "observed_at": obs_iso,
                "price": p_val,
                "source_ref": f"https://clob.polymarket.com/prices-history?market={tok}",
                "acquired_at": None,
                "known_as_of": None
            })
            
    points.sort(key=lambda p: (p["observed_at"], p["outcome_id"]))
    
    market_record = {
        "market_id": m_id,
        "question": raw["question"],
        "slug": raw.get("slug"),
        "outcomes": outcomes,
        "outcome_ids": token_ids,
        "resolution_outcome_ids": [winning_token_id],
        "resolution_known_at": res_time,
        "metadata_source_ref": f"https://gamma-api.polymarket.com/markets?id={m_id}",
        "price_history": points
    }
    dataset_markets.append(market_record)
    
    prov_record = {
        "market_id": m_id,
        "question": raw["question"],
        "gamma_url": f"https://gamma-api.polymarket.com/markets?id={m_id}",
        "clob_urls": [f"https://clob.polymarket.com/prices-history?market={t}&interval=all" for t in token_ids],
        "acquired_at_utc": now_utc,
        "points_count": len(points),
        "interval_covered": {
            "t_min": earliest_t,
            "t_max": latest_t,
            "start_iso": datetime.fromtimestamp(earliest_t, tz=timezone.utc).isoformat().replace("+00:00", "Z") if earliest_t else None,
            "end_iso": datetime.fromtimestamp(latest_t, tz=timezone.utc).isoformat().replace("+00:00", "Z") if latest_t else None,
        }
    }
    provenance_markets.append(prov_record)

final_dataset = {
    "schema_version": "polymarket-historical-dataset.v1",
    "market_count": len(dataset_markets),
    "total_price_points": sum(len(m["price_history"]) for m in dataset_markets),
    "markets": dataset_markets
}

dataset_file = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json"
with open(dataset_file, "w", encoding="utf-8") as f:
    json.dump(final_dataset, f, indent=2)

with open(dataset_file, "rb") as f:
    dataset_sha256 = hashlib.sha256(f.read()).hexdigest()

final_provenance = {
    "target_file": "historical_dataset_50markets.json",
    "sha256": dataset_sha256,
    "market_count": len(dataset_markets),
    "total_price_points": sum(len(m["price_history"]) for m in dataset_markets),
    "acquired_at_utc": now_utc,
    "markets": provenance_markets
}

provenance_file = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.provenance.json"
with open(provenance_file, "w", encoding="utf-8") as f:
    json.dump(final_provenance, f, indent=2)

print(f"Successfully re-saved dataset (SHA-256: {dataset_sha256})")
