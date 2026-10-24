# Vault routing — direct-route resolution, measured

Produced by `python 30_SCRIPTS/routing/measure_route_resolution.py --out 07_EVALUATION/vault_routing/route_resolution.json`
on the working tree of `claude/vault-universal-access` (base commit in the JSON), as the owner so policy hides nothing.

| Measure | Value |
|---|---:|
| Routes (files reachable by `vault://`) | 4433 |
| Domains (`04_CONFIG/vault_domains.yaml`, `expand: subdirs` included) | 114 (113 before the Casa3D sub-directory landed on main: `expand: subdirs` adds one domain per sub-directory). `VAULT_STATE.md` states the current count (4433 routes, 117 domains; by URI 4433/4433, re-measured 2026-10-10) and a test checks it; the by-name rows below were re-measured 2026-10-10 on 4433 routes |
| `vault_resolve(uri)` returns that route | 4433 / 4433 |
| `vault_resolve(<last slug segment>)` returns that route | 4199 / 4433 (94.7%) |
| … returns AMBIGUOUS (name shared by several files) | 234 (readme 116, agent 22, current 10, …) |
| … RESOLVED to a different file | **0** |

What this does and does not show:

- Every routed file has a direct route, and a name never silently resolves to the wrong file: shared names
  (README, CURRENT, AGENT) come back AMBIGUOUS with candidates, and the full URI always resolves.
- It does not measure topical questions. `vault_resolve` matches names, titles, aliases, headings and domain
  keywords; it does not read note bodies. Put through `vault_resolve`, the 20 work questions in
  `07_EVALUATION/memory_usage/work_queries.json` returned their `source_file` 0/20 times, at top-1 and in the top 5.
  Those questions are about topics, and the file a task is listed in is not where its answer lives.
  Content questions remain `memory_search` / `vault_search`, whose measured quality is in `VAULT_STATE.md` §5.
- Ambiguous names with the requested file outside the top 5 candidates: 129 (mostly `readme`).
  `vault_list(<domain>)` or the full URI reaches them.
