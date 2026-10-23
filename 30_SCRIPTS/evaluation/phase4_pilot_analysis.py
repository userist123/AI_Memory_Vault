"""Phase-4 pilot analysis: per task x arm table, deltas vs base_old with Welch CI95 and PAIRED bootstrap CI95 over
(task, repeat) pairs. Usage: python phase4_pilot_analysis.py <out_dir> <ARM,...>   (first arm = reference).
Writes <out_dir>/ANALYSIS.md. No claude invocation. Seeded bootstrap (seed 4, 20000 resamples, percentile CI)."""
import json, math, random, statistics as st, sys
from pathlib import Path

CATS = [("tok_input", "input"), ("tok_cache_read", "cache_read"), ("tok_cache_creation", "cache_creation"),
        ("tok_output", "output"), ("total_tokens", "total_tokens")]


def ms(xs, d=0):
    if not xs:
        return "-"
    m = st.mean(xs)
    s = st.stdev(xs) if len(xs) > 1 else float("nan")
    return f"{m:.{d}f} ± {s:.{d}f}"


def tcrit(df):
    try:
        from scipy import stats
        return stats.t.ppf(0.975, df)
    except Exception:
        return 2.0


def welch(b, o):
    """CI95 of mean(o)-mean(b) and of the reduction% (delta method on the difference / mean_b)."""
    if len(b) < 2 or len(o) < 2:
        return None
    d = st.mean(o) - st.mean(b)
    se = math.sqrt(st.variance(o) / len(o) + st.variance(b) / len(b))
    t = tcrit(min(len(o), len(b)) - 1)
    return d - t * se, d + t * se


def red(b, o):
    mb = st.mean(b)
    return 100 * (mb - st.mean(o)) / mb if mb else float("nan")


def boot(pairs, fn, n=20000, seed=4):
    """pairs: list of (base_row, other_row); fn(list_of_pairs)->statistic. Percentile CI95."""
    rnd = random.Random(seed)
    vals = sorted(fn([pairs[rnd.randrange(len(pairs))] for _ in pairs]) for _ in range(n))
    return vals[int(0.025 * n)], vals[int(0.975 * n) - 1]


def main(out, arms):
    out = Path(out)
    rows = [json.loads(l) for l in (out / "results.jsonl").read_text(encoding="utf-8").splitlines() if l.strip()]
    rows = [r for r in rows if isinstance(r.get("total_tokens"), (int, float))]
    for r in rows:
        r["_cost"] = r.get("total_cost_usd") or 0.0
        r["_tpr"] = r["total_tokens"] / r["requests"] if r.get("requests") else float("nan")
    tasks = sorted({r["task"] for r in rows})
    L = [f"# Phase-4 pilot analysis ({out.name})", "", "Arms: " + ", ".join(arms) + f" (reference = {arms[0]}). "
         "total_tokens = sum over deduplicated requests (main + subagents) of input+cache_read+cache_creation+output "
         "(+ unattributed output residual, equals modelUsage). tokens/request = total_tokens / requests.", "",
         "## Per task x arm (mean ± sd)", "",
         "| task | arm | n | correct | input | cache_read | cache_creation | output | total_tokens | requests | tokens/request | turns | delegations | cost USD | first-request prefix |",
         "|" + "---|" * 15]
    for t in tasks:
        for a in arms:
            rs = [r for r in rows if r["task"] == t and r["condition"] == a]
            if not rs:
                continue
            L.append(f"| {t} | {a} | {len(rs)} | {sum(bool(r['correct']) for r in rs)}/{len(rs)} | "
                     + " | ".join(ms([r[c] for r in rs]) for c, _ in CATS) + f" | {ms([r['requests'] for r in rs], 1)} | "
                     f"{ms([r['_tpr'] for r in rs], 0)} | {ms([r['turns'] for r in rs], 1)} | {ms([r['delegations'] for r in rs], 1)} | "
                     f"{ms([r['_cost'] for r in rs], 4)} | {ms([r.get('first_request_prefix_tokens') for r in rs if r.get('first_request_prefix_tokens')], 0)} |")
    L += ["", "## Pooled per arm", "", "| arm | n | correct | total_tokens | tokens/request | cost USD | first-request prefix |", "|---|---|---|---|---|---|---|"]
    for a in arms:
        rs = [r for r in rows if r["condition"] == a]
        L.append(f"| {a} | {len(rs)} | {sum(bool(r['correct']) for r in rs)}/{len(rs)} | {ms([r['total_tokens'] for r in rs])} | "
                 f"{ms([r['_tpr'] for r in rs])} | {ms([r['_cost'] for r in rs], 4)} | "
                 f"{ms([r['first_request_prefix_tokens'] for r in rs if r.get('first_request_prefix_tokens')])} |")
    for a in arms[1:]:
        L += ["", f"## {a} vs {arms[0]} (positive reduction = {a} smaller)", ""]
        # pairs by (task, repeat)
        idx = {(r["task"], r["repeat"], r["condition"]): r for r in rows}
        pairs = [(idx[(t, k, arms[0])], idx[(t, k, a)]) for (t, k, c) in sorted(idx) if c == a and (t, k, arms[0]) in idx]
        L.append(f"Pairs (task, repeat) with both arms: {len(pairs)}. Welch = pooled runs, Welch SE, t(0.975, df=min(n)-1); "
                 "paired bootstrap = resample pairs, statistic = 100*(sum base - sum arm)/sum base (ratio of sums), percentile CI95.")
        L += ["", "| scope | metric | n pairs | reduction % | Welch CI95 of mean delta (arm-base) | paired-bootstrap CI95 of reduction % | CI excludes 0 (saving dir.)? |",
              "|---|---|---|---|---|---|---|"]
        scopes = [("ALL", pairs)] + [(t, [p for p in pairs if p[0]["task"] == t]) for t in tasks]
        mets = [(c, lab) for c, lab in CATS] + [("_tpr", "tokens/request"), ("_cost", "cost USD")]
        for sc, ps in scopes:
            if len(ps) < 2:
                continue
            for c, lab in mets:
                b = [p[0][c] for p in ps]; o = [p[1][c] for p in ps]
                w = welch(b, o)
                if c == "_tpr":
                    fn = lambda s: 100 * (st.mean(x[0]["_tpr"] for x in s) - st.mean(x[1]["_tpr"] for x in s)) / st.mean(x[0]["_tpr"] for x in s)
                else:
                    fn = lambda s, c=c: 100 * (sum(x[0][c] for x in s) - sum(x[1][c] for x in s)) / sum(x[0][c] for x in s) if sum(x[0][c] for x in s) else 0.0
                lo, hi = boot(ps, fn)
                pt = fn(ps)
                wtxt = f"[{w[0]:.4g}, {w[1]:.4g}]" if w else "n/a"
                ex = "YES" if lo > 0 else ("opposite" if hi < 0 else "no")
                L.append(f"| {sc} | {lab} | {len(ps)} | {pt:.1f} | {wtxt} | [{lo:.1f}, {hi:.1f}] | {ex} |")
        cb = sum(bool(p[0]["correct"]) for p in pairs); ca = sum(bool(p[1]["correct"]) for p in pairs)
        L.append(f"\nCorrectness parity ({len(pairs)} pairs): {arms[0]} {cb}/{len(pairs)} vs {a} {ca}/{len(pairs)}.")
    bad = [f"{r['task']}_{r['condition']}_{r['repeat']}" for r in rows if r.get("residual_negative") or r.get("attribution_mismatch") or r.get("cost_policy_flag")]
    L += ["", f"Reconciliation/cost flags: {bad if bad else 'none'}."]
    (out / "ANALYSIS.md").write_text("\n".join(L) + "\n", encoding="utf-8")
    print(out / "ANALYSIS.md")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2].split(","))
