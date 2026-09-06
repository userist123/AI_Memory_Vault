# Imported projects

Raw project material promoted from `06_INBOX/proiecte/` on 2026-09-06.
Not yet curated: these are working copies kept here to be worked on later,
deliberately separate from the reviewed projects in
`02_PRODUCT/projects/workspaces/`. Promote individually once a project has
been reviewed.

## What was filtered out

The source tree was 3101 files / 2466 MB. What landed here is 1075 files /
27 MB. Excluded, and excluded permanently by `.gitignore`:

| Excluded | Size |
|---|---|
| `node_modules/` | 1287 MB |
| `bin/` (incl. a 134 MB `RegistruTransferuri.exe`, over GitHub's 100 MB limit) | 872 MB |
| `.next/`, `obj/`, `build/`, `__pycache__/` | 108 MB |
| Third-party release archives (`open-webui-0.8.12.zip` 53 MB, SDCardFormatter, WOB_ART) | 72 MB |
| Private WhatsApp conversation archives | 92 MB |

Two nested git repositories (`bot/jarvis-trader-ui`, `aplicatie-transfer/.../registru-transferuri`)
were imported as plain files; their original history remains in the untracked
inbox copy on disk.

## Credentials

Two live API keys were hardcoded across 8 files in the source material. They
were replaced with environment-variable reads BEFORE the first commit, so they
never entered git history:

- `CEREBRAS_API_KEY` — `bot/trade/cerebras.ps1`, `bot/trade/cerebras-agent/agent.py`
- `FRED_API_KEY` — `market-analysis/`, `nu-sterge/` scripts

Set them in your environment before running those scripts. Rotate both keys:
they sat in plaintext on disk, so treat them as exposed even though they were
never published.

## Known duplication

Several projects here appear to be earlier or parallel versions of what is
already in `02_PRODUCT/projects/workspaces/` — `eventloganalyzer` vs
`loganalyzer-dfir`, `aplicatie-transfer` vs `registru-transferuri`, `bot/jarvis*`
vs the `jarvis_*` workspaces. `market-analysis/` and `nu-sterge/` also hold
four near-identical variants of the same market-analysis script. A lineage
pass is needed before any of this is promoted.
