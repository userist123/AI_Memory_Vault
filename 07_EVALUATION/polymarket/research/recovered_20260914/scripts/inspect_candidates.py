import json

with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/corpus_depth_results.json", "r", encoding="utf-8") as f:
    d = json.load(f)

markets = d["f5_qualifying_markets"]
print(f"Total qualifying markets: {len(markets)}")
print("First 10 qualifying markets:")
for m in markets[:10]:
    print(f"  ID: {m['id']} | Era: {m['era']} | Points: {m['points_count']} | Q: {m['question'][:70]}")
