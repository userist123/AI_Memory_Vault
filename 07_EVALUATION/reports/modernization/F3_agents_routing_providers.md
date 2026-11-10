---
id: "99647d78-e9c8-4540-ad11-2bc8ac3c9650"
title: "F3 - agenți, rutare, provideri: garanții fixate prin teste și decizia pentru agent_bridge (WP-11)"
type: "decision"
lifecycle: "REVIEW"
created: "2026-11-09T19:00:00Z"
updated: "2026-11-09T19:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "20_TESTS/test_provider_offline_default.py"
  created_by: "claude-code"
tags:
  - "modernization"
  - "agents"
  - "decision"
---

# F3 — agenți, rutare, provideri (WP-11)

Ramura `modernize/f3-wp11-agents` (peste `modernize/f3-wp10-lifecycle`). Doar teste și documentație; niciun cod de runtime nu s-a schimbat.

## 1. Decizia pentru `agent_bridge`: dormant
- Statut: **dormant**. `packages/agent_bridge` (X25519, pachete semnate, token de politică) nu are consumator de producție. Îl folosesc doar `20_TESTS/test_agent_bridge_hardening.py`, `test_secure_agent_bridge.py`, `test_secure_bridge_handler.py`, `.github/workflows/agent-routing-tests.yml` și `pyproject.toml`. Singura legătură cu `routing` este `bridge.py` care importă `routing.models.WorkPacket`; `routing/route_cli.py` și `routing/dispatcher.py` nu îl importă.
- Rămân codul, testele de securitate și CI-ul. Regula Production-Consumer din CLAUDE.md interzice orice strat nou peste el până la cablarea în `routing.dispatcher`.
- Varianta respinsă: arhivarea. Ar elimina acoperirea testelor de securitate; arhivarea se poate face oricând, reconstruirea costă.
- Testul de straturi nu detectează dormanța (consemnat în `unenforced`); o detectează `20_TESTS/test_vault_state_accuracy.py`, care cere zero importatori de producție pentru `agent_bridge`.

### Ce ar însemna cablarea (schiță, fără implementare)
1. `routing.dispatcher` importă `agent_bridge.client.SecureBridgeClient` și îl folosește pentru pachetele destinate agenților externi.
2. Dispatcher-ul construiește `WorkPacket`, îl semnează (Ed25519) și îl criptează (X25519) înainte de trimitere.
3. Fiecare trimitere cere un `CapabilityToken` emis de `BridgePolicy`; fără token, fail-closed.
4. `ReplayGuard` rămâne activ pe partea receptorului; cheile vin din configurare, nu din cod.
5. Un test de integrare dispatcher -> bridge înlocuiește simpla existență a testelor izolate; abia apoi dormanța se ridică.

## 2. Teste noi și rezultate
Rulare unică: `57 passed` (cele 5 fișiere din acceptare). `30_SCRIPTS/verification/architecture_boundaries.py` a ieșit cu 0.
- `test_provider_offline_default.py`: (a) fiecare tier din `04_CONFIG/model_tiers.json` e `fake`/`deterministic`/`local`; schema (`{tier: {provider, model}}`) nu are câmp de activare explicită, deci orice provider extern trebuie listat în `EXPLICITLY_APPROVED_EXTERNAL_PROVIDERS` (azi goală; schimbare de politică, cere aprobarea proprietarului). Pe o copie în `tmp_path` cu `openai` (și `anthropic`) ca provider implicit, verificarea pică (parametrizată pe calea config-ului; fișierul real nu se modifică). (b) `AgentModelExecutor()` are modul implicit `deterministic`. (c) într-un interpretor nou, cu orice conexiune blocată la nivel de socket (`socket.connect`, `create_connection`) și fără `OPENAI_API_KEY`, importul și construcția `OpenAIProvider` trec; cheia lipsă dă `OpenAIAuthenticationError` doar la rezolvare. Subprocesul înlocuiește `importlib.reload`, care rupea `pytest.raises` în alte fișiere (review r1, H); blocarea la socket prinde și `http.client` (review r1, M).
- `test_usage_log_no_pii.py`: `vault_runtime.log_usage` cu interogare-santinelă într-un director temporar (`AI_MEMORY_VAULT_HOME`). Jurnalul **nu** conține textul interogării, `getpass.getuser()` sau `Path.home()` (nici posix, nici cu escape JSON). Rezultat: PASS, fără xfail, deci fără constatare H. Câmpul `client` nu e validat (se scrie ce primește apelantul); testul nu îl acoperă.

## 3. Provideri externi și condiția de activare
| Provider | Locul | Condiție de activare |
|---|---|---|
| `openai` (`providers/openai_provider.py`, `api.openai.com`) | `urlopen` la apel, nu la import/construcție | Mod `openai` cerut explicit în `AgentModelExecutor`, sau tier cu `provider: openai` în `model_tiers.json` plus factory înregistrat, **și** lărgirea explicită a `DEFAULT_ALLOWED_PROVIDERS = ("fake", "local")` din `executive_model_execution_bridge.py:10` (nucleu protejat; altfel refuzat), și `OPENAI_API_KEY` (sau `api_key` explicit). Fără cheie: fail-closed. |

Locali (nu externi): `local_provider.py` și `ollama_extractor.py` (`localhost:11434`), `fake`, `deterministic`. Toate tier-urile au azi `fake`.

## Review
- r1 (`vault-reviewer`, Opus): RESPINS. H: `importlib.reload` în proces rupea 4 teste din `test_openai_provider.py` la altă ordine de rulare → verificările mutate în subproces. M: doar `urlopen` era blocat → blocare la socket (mutația `http.client.HTTPSConnection.connect()` în `__init__` pică acum). L: notele despre dormanță și condiția de activare `openai` corectate. Pentru proprietar: câmpul `client` din `log_usage` vine din `clientInfo.name` al clientului MCP sau e literalul `"cli"`; local, nevalidat ca lungime, fără risc PII azi.
