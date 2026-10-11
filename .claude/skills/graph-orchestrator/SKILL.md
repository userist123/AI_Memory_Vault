---
name: graph-orchestrator
description: Owner's standing directive for auditing, analysing, cloning or rebuilding an existing app or site - run it as a graph, not a loop. Fan out parallel subagents (recon of screens and flows, architecture and data, market reviews and complaints, prior decisions from memory), fan in to one synthesis, then rebuild one slice first with the replica-* skills. Rebuild function and logic only, never the target's code, brand, logo, copy or colours. Apply automatically; the owner does not type commands.
when_to_use: Any request to audit, analyse, reverse-engineer, clone, replicate or rebuild an app, SaaS or website, or to beat a competitor's product. Romanian triggers - "cloneaza", "fa ca aplicatia X", "analizeaza aplicatia", "reconstruieste", "replica", "audit".
---

# graph-orchestrator

The owner names a target (URL, app name or store link); you run the rest. Never ask them to
type `/replica-*` commands. Ask only for what is theirs: the target, budget, what to build
first if the synthesis leaves two equal candidates, and anything that publishes or spends money.

## 0. Where it runs

Always in a separate project repository for the new product, never inside the AI_Memory_Vault
repository. First action there: add `replica/screens/` to `.gitignore` (screenshots of the
target are never committed).

## 1. Fan-out (one message, parallel subagents)

Launch these together, each with a self-contained brief and a compact output contract
(`findings / evidence (URL or file:line) / gaps / confidence`):

| Node | Job | Subagent | Skills |
|---|---|---|---|
| 1 Recon | screens, layout, navigation, critical flows, feature list | `Explore` (haiku) for reading, general-purpose with `chrome-devtools` for live pages | `replica-recon` |
| 2 Architecture | data entities, API surface, states, auth, integrations | general-purpose (sonnet) | `replica-architect` |
| 3 Market | real user complaints, bugs, pricing friction, missing features (store reviews, forums, Reddit) | general-purpose (sonnet) with web search | `replica-entrepreneur` |
| 4 Memory | earlier decisions, brand rules, lessons for this owner | you or `Explore`, via the Vault (`memory_search`) | - |

Each node loops on its own until its checklist is complete or it reports the gap it cannot
close. Public sources only: no logging in as someone else, no paywall or robots.txt bypass,
no scraping that breaks a site's terms. The owner's own account in the target app is used
only when they say so for that session. Node 3 reads reviews through official feeds/APIs or
pages it opens one by one and quotes briefly; no bulk scraping of review sites; if that leaves
too few, ask the owner for an export.

## 2. Fan-in

Merge the four outputs into `RECON.md` (map) and the top of `PLAN.md`: target summary,
feature map with priority, data model, the complaints worth beating and how the architecture
removes each, open questions. Resolve conflicts between nodes explicitly.

## 3. Rebuild

- **One slice first**: the single flow users value most, end to end, tested, before anything
  else (`replica-build`). Then slice by slice, each with its test written first.
- **Entrepreneur pass**: every slice states which user complaint it fixes.
- **Own identity**: new name, logo, copy, palette and design system (`replica-brand`, plus
  `creative-web-director` for the visual standard). Never copy the
  target's source code, assets, text, trademarks or colour scheme; `replica-brand` sweep must
  come back clean before anything is shown or shipped. Replace every colour role measured from
  the target (neutrals, surfaces and borders too, not only the accent) and do not reuse its
  signature layout; match function, not look.
- Publishing, payments, store listings, `npm`/browser installs and any live purchase need the
  owner's explicit go for that action.

## 4. Procedures and state

Turn any procedure you repeat (support replies, payments, PR validation) into a short skill
under the project's `.claude/skills/` and say so. Keep `PLAN.md` current every iteration; it is
the resume point after `/clear`. Durable decisions go to the Vault (`memory_propose`), not a
second memory store.
