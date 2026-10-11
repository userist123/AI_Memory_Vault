---
type: evaluation_report
category: skill_intake
id: "external-skill-intake-replica-2026-10-11"
document_status: active
provenance_status: complete
trust_level: REVIEWED
created: 2026-10-11
---

# External skill intake: Replica suite and owner directives (2026-10-11)

The owner asked for the Replica suite (`npx skills add https://github.com/Jakeschincariol/replica-skill`), only the parts that are needed, plus three standing directives: web/app design, security lockdown before release, and graph orchestration for clone/rebuild work.

Source: `Jakeschincariol/replica-skill` @ `77c9436fb3d18c3d58169efb8caf4fe906b0dc51`, MIT, 39 files. It was shallow-cloned and nothing in it was executed.

Checks:
- The `untrusted_content_guard` rules found 0 hits.
- No hooks, no executables.
- The helper `.py` files import only the standard library: no network, no subprocess.
- An independent read-only Opus review gave an overall verdict of SAFE-WITH-CONDITIONS. No skill was rejected.

## What is installed and why

| Skill | Kept | Reason |
|---|---|---|
| replica-recon | yes | maps screens, flows and features of the target |
| replica-architect | yes | data model, API and slice order |
| replica-entrepreneur | yes | real user complaints, turned into fixes |
| replica-build | yes | rebuilds screen by screen, never copies code |
| replica-brand | yes | own identity, plus a sweep for anything left from the original |
| replica-design, replica-test | no | already covered by creative-web-director, frontend-design and browser verification |
| replica-diff | no | nice to have, not required |
| replica-backend | no | add later, when payments are in scope |
| replica-deploy, replica-launch | no | they publish and price, so they wait for an explicit owner request |

## Conditions from the review and where they are enforced

| Review finding | Enforced in |
|---|---|
| Upstream trigger phrases are too broad ("how does X work", "map out X") | The installer prepends a gate to each installed Replica description: use only when the owner explicitly asks to clone, rebuild or reverse-engineer. The vendored copy stays byte-identical to the source, so `PROVENANCE.json` hashes keep matching. |
| Agent-driven bulk review scraping | `graph-orchestrator`: official feeds/APIs or pages opened one by one, no bulk scraping, otherwise ask the owner for an export |
| Screenshots of the target could be committed | `graph-orchestrator`: separate project repo, never the Vault; `replica/screens/` goes into `.gitignore` first |
| Only the accent colour gets replaced | `graph-orchestrator`: replace every measured colour role and do not reuse the signature layout |
| Installs, purchases, publishing | `graph-orchestrator` and `creative-web-director`: the owner's explicit go is required for each |

## Owner directives (written for this owner, English inside the files)

- `creative-web-director`: the visual standard for web and now desktop/mobile UIs; "done" requires `security-lockdown` to return YES.
- `security-lockdown`: 19 checks before release, applied to web, API, desktop, mobile and CLI. It does not interrupt normal work and writes `SECURITY_AUDIT.md`.
- `graph-orchestrator`: four parallel nodes, fan-in to `RECON.md` and `PLAN.md`, one slice first.

## Install

`30_SCRIPTS/setup/install_design_stack.py --apply`, stack version `2026-10-11.2`. A PC marked `2026-10-11.1` re-runs the install once. Tests: `20_TESTS/test_install_design_stack.py`.
