import json

with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/in_play_breakdown.json", "r", encoding="utf-8") as f:
    results = json.load(f)

in_play = [r for r in results if r["is_in_play"]]
print(f"In-play markets count: {len(in_play)}")

progress = [r["game_progress_pct_at_entry"] for r in in_play if r["game_progress_pct_at_entry"] is not None]
print(f"Game progress at entry for in-play markets:")
print(f"  Min: {min(progress):.1%}")
print(f"  Max: {max(progress):.1%}")
print(f"  Mean: {sum(progress)/len(progress):.1%}")
print(f"  Median: {sorted(progress)[len(progress)//2]:.1%}")

# Look at their win rates
won_count = sum(1 for r in in_play if r["won"])
print(f"\nIn-play markets won count: {won_count} / {len(in_play)} ({won_count/len(in_play):.1%})")

# Non-in-play markets
pre_game = [r for r in results if not r["is_in_play"]]
print(f"\nPre-game markets count: {len(pre_game)}")
pre_won = sum(1 for r in pre_game if r["won"])
print(f"Pre-game markets won count: {pre_won} / {len(pre_game)} ({pre_won/len(pre_game):.1%})")
for r in pre_game:
    print(f"  ID {r['id']} | won={r['won']} | p={r['entry_price']} | Q: {r['question'][:50]}")
