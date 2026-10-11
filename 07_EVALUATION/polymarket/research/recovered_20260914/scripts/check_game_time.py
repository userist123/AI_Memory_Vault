import json
from datetime import datetime, timezone

with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/sampled_500_markets.json", "r", encoding="utf-8") as f:
    sampled = json.load(f)

# Look at the recent 50 markets
for m in sampled[400:415]:
    print("ID:", m.get("id"))
    print("  Q:", m.get("question"))
    print("  createdAt:", m.get("createdAt"))
    print("  startDate:", m.get("startDate"))
    print("  gameStartTime:", m.get("gameStartTime"))
    print("  closedTime:", m.get("closedTime"))
    print("  endDate:", m.get("endDate"))
