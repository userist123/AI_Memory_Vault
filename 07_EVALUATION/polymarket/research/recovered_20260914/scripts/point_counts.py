import json

with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/corpus_depth_results.json", "r", encoding="utf-8") as f:
    d = json.load(f)

markets = d["f5_qualifying_markets"]
counts = [m["points_count"] for m in markets]
print(f"Min points: {min(counts)}, Max points: {max(counts)}, Mean: {sum(counts)/len(counts):.1f}")
counts_sorted = sorted(counts, reverse=True)
print("Top 10 highest point counts:")
for m in sorted(markets, key=lambda x: x["points_count"], reverse=True)[:10]:
    print(f"  ID: {m['id']} | Points: {m['points_count']} | Q: {m['question']}")
