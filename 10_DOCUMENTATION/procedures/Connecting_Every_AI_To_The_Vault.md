---
id: "78884547-f37a-4fab-811d-5d20e63626ce"
type: procedure
lifecycle: REVIEW
category: vault-access
tags: [procedure, mcp, routing, access-policy, ollama, telegram, codex, antigravity, gemini, claude]
created: 2026-10-06
updated: 2026-10-06
provenance:
  source_type: ai
  source_ref: "claude/vault-universal-access, 2026-10-06"
confidence: medium
verification: unverified
aliases: [conectare AI la vault, rute directe, vault access, vault routes]
---

# Conectarea fiecărui AI la Vault — rute directe, aceeași politică

Un singur nucleu (`03_IMPLEMENTATION/packages/vault_access/`), o singură tabelă de rute și o singură
politică. Fiecare AI ajunge la orice fișier din orice domeniu prin `vault://<domeniu>/<slug>`.
Ce poate citi depinde de **canalul pe care pleacă textul**, nu de cât de „de încredere” pare modelul.

## 1. Ce e unde

| Fișier | Rol | Cine îl editează |
|---|---|---|
| `04_CONFIG/vault_domains.yaml` | domeniile și rădăcinile lor; din el se construiește tabela de rute | owner |
| `04_CONFIG/access_policy.yaml` | principali, canale, clasificări, denylist, limite, allowlist Telegram | owner |
| `04_CONFIG/local_llm.json` | modelul Ollama, `num_ctx` explicit, bugetul de context | owner |
| `30_SCRIPTS/routing/build_route_manifest.py --check` | validează registrul (rulat în CI) | — |
| `30_SCRIPTS/routing/export_public_vault.py` | exportul PUBLIC pentru AI-urile web | — |

Tabela de rute **nu e comisă**: se construiește la rulare din rădăcinile declarate, iar metadatele
(titlu, alias-uri, secțiuni) se păstrează într-un cache per utilizator, în afara depozitului.

## 2. Configurare per client

Toți clienții pornesc același server MCP stdio, `vault-memory`, din rădăcina depozitului.
Principalul trimis cu `--principal` poate doar **coborî** accesul: plafonul canalului nu se poate
depăși, iar o interfață MCP nu poate pretinde niciodată că e owner-ul.

| Client | Fișier | Principal |
|---|---|---|
| Claude Code | `.mcp.json` (deja în depozit) | `cloud_cli.claude_code` |
| Codex CLI / IDE | `.codex/config.toml` (se încarcă doar dacă proiectul e marcat trusted în Codex) | `cloud_cli.codex` |
| Antigravity | `.agents/mcp_config.json` | `cloud_cli.antigravity` |
| Gemini CLI | `.gemini/settings.json` (+ `GEMINI.md`, care trimite la `AGENTS.md`) | `cloud_cli.gemini_cli` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json`, cu cale absolută (vezi mai jos) | `cloud_cli.claude_desktop` |
| Ollama / Telegram | `python -m cognitive_core.telegram_vault_bot` | `telegram.bot` |
| claude.ai, ChatGPT, Perplexity | export static PUBLIC | `cloud_web.export` |

Claude Desktop pornește serverul din alt director, deci are nevoie de căi absolute:

```json
{
  "mcpServers": {
    "vault-memory": {
      "command": "python",
      "args": ["<CALEA_DEPOZITULUI>\\03_IMPLEMENTATION\\packages\\interfaces\\memory_mcp_server.py",
               "--principal", "cloud_cli.claude_desktop"]
    }
  }
}
```

Prima utilizare pe o mașină rămâne `python -m cognitive_core.recall_cli --init-secret`. Fără secret,
`vault_resolve`, `vault_list`, `vault_read` și `vault_get_metadata` funcționează în continuare.
`vault_search` trece atunci pe metadatele rutelor (`mode: routing_metadata_fallback`).

## 3. Instrumentele (identice în MCP, CLI, Ollama, Telegram)

| Tool | Ce face |
|---|---|
| `vault_resolve(query)` | URI, nume de fișier, titlu sau text liber → ruta directă (`RESOLVED`), `AMBIGUOUS` cu candidați sau `NOT_FOUND` |
| `vault_list("*")` / `vault_list(domeniu)` | domeniile vizibile principalului / rutele unui domeniu |
| `vault_read(uri, section, line_start, line_end)` | text verbatim + `sha256` al fișierului + linii exacte + `cite_as`; `attestation: match/drift/none` față de `07_EVALUATION/integrity/active_notes.sha256.json` |
| `vault_search(query)` | `MemoryController.search()`, filtrat la rutele permise |
| `vault_get_metadata(uri)` | frontmatter, `sha256`, ancorele secțiunilor |
| `vault_check_quotes(citations)` | fiecare citat trebuie să existe verbatim în liniile citate |

CLI: `python -m cognitive_core.vault_cli resolve "VAULT_STATE"`, `read vault://governance/vault_state --text`,
`domains`, `ls <domeniu>`. Implicit rulează ca `cloud_cli.unknown`. Owner-ul folosește `--principal owner`,
care cere un terminal interactiv și confirmarea `OWNER`: shell-ul unui agent nu are terminal, deci un agent nu poate
pretinde că e owner. Prin CLI și MCP nu se poate pretinde nici canalul local (`local_llm.ollama`).

## 4. Politica (rezumat)

- **Canale:** `local_only` (Ollama, nimic nu iese) ≤ SENSITIVE; `cloud_model` (CLI-uri de coding) ≤ INTERNAL;
  `telegram` ≤ INTERNAL (mesajele trec prin cloud-ul Telegram); `cloud_web` ≤ PUBLIC.
- **Niciodată servite agenților:** `inbox` (06_INBOX), `archive` (80_ARCHIVE), `knowledge.external_skills` (RAW),
  notele cu lifecycle ARCHIVED/RAW/QUARANTINED/DEPRECATED/REJECTED și orice cale din denylist (chei, token-uri, `.env`, `secrets/`).
  Notele controllerului (cu `id` în frontmatter) trec și prin verdictul `MemoryController`.
- **Depozitul e public.** INTERNAL înseamnă „agenții owner-ului îl pot citi”, nu „confidențial”. Ce e cu
  adevărat sensibil stă în overlay-ul privat: setezi `AI_MEMORY_VAULT_PRIVATE_ROOT` către un folder din afara
  git. Domeniul `private` îl expune doar principalilor locali (`local_llm.ollama`, `owner`). CI-ul pică dacă o
  notă din depozit declară `classification: SENSITIVE` sau `RESTRICTED`.
- **Un refuz arată ca o absență:** pentru agenți, o rută interzisă întoarce `NOT_FOUND`, la fel ca una
  inexistentă, ca să nu se poată sonda ce există în inbox, arhivă sau overlay. Motivul real rămâne în audit.
- **O notă își poate ridica eticheta, nu și coborî.** `classification:` mai mic decât al domeniului e ignorat.
  Un frontmatter ilizibil (YAML invalid, neterminat, BOM, prea lung) sau un lifecycle necunoscut fac nota
  RESTRICTED/arhivată (fail-closed).
- **Limita reală:** politica guvernează instrumentele vault. Un agent cu shell care rulează sub același cont
  Windows poate citi fișierele direct. Pentru conținut cu adevărat sensibil, garanția vine din OS: overlay-ul
  privat ținut în afara accesului contului sub care rulează agenții, secretele în DPAPI, iar botul sub contul
  read-only `svc_vaultreader`.
- **Audit:** fiecare apel scrie o linie înlănțuită prin hash în `vault_access_audit.jsonl` (directorul per
  utilizator, lângă `usage.jsonl`). Se păstrează doar un HMAC al argumentelor (cu sare locală), niciodată textul
  întrebării sau al notei. Scrierile din mai multe procese se serializează printr-un lock de fișier. Ștergerea
  finalului jurnalului nu se vede din jurnalul însuși: `AuditLog.head()` dă ultimul hash, ca să poată fi ancorat în altă parte.

## 5. Ollama și Telegram (anti-halucinație)

1. Pornești Ollama și verifici modelul din `04_CONFIG/local_llm.json` (`ollama pull <model>`).
2. Pui id-ul numeric Telegram în `telegram.allowed_user_ids` (`04_CONFIG/access_policy.yaml`) sau în
   `VAULT_TELEGRAM_ALLOWED_IDS`. Cu allowlist-ul gol, botul nu pornește.
3. Token-ul vine din `VAULT_TELEGRAM_TOKEN` sau din `telegram.token` în directorul per utilizator, niciodată din depozit.
4. `powershell -File 30_SCRIPTS/run/start_telegram_vault_bot.ps1`. Opțional, rulezi botul sub un cont
   read-only: `30_SCRIPTS/run/Install-VaultReaderAccount.ps1`.

**Ce s-a schimbat față de botul care a inventat `VAULT_STATE.md`:**
- „citește X”, `/read X` și un `vault://` sunt **extractive**: răspunsul e textul verbatim plus citarea, fără niciun apel la model.
- Întrebările merg pe `/api/chat` nativ, cu `num_ctx` explicit, temperatură 0 și schemă JSON.
- Trunchierea se detectează din `prompt_eval_count`.
- Fiecare citat trebuie să fie verbatim în fragmentul numit, iar numerele și identificatorii din răspuns trebuie să apară în fragmentele citate.
- Dacă verificarea pică de două ori, botul arată fragmentele reale în loc de răspuns.
- `/debug on` afișează pașii `[ROUTER]` / `[TOOL_CALL]` / `[TOOL_RESULT]` / `[OLLAMA]` / `[VERIFY]` (coduri și hash-uri, fără conținut).

## 6. AI-uri web (claude.ai, ChatGPT, Perplexity)

Rulează în cloud-ul furnizorului și nu pot ajunge la un server MCP local fără expunere publică.
Primesc doar exportul PUBLIC:

    python 30_SCRIPTS/routing/export_public_vault.py --out <folder în afara depozitului>

Exportul conține fișierele, `llms.txt` (indexul) și `MANIFEST.json` (sha256 per fișier). Îl încarci într-un
Perplexity Space, într-un Project claude.ai sau într-un proiect ChatGPT. Pentru lucru pe date INTERNAL
folosești echivalentul CLI local (Claude Code, Codex CLI, Gemini CLI).

## 7. Adăugarea unui domeniu nou

1. Adaugi intrarea în `04_CONFIG/vault_domains.yaml`: `title`, `roots`, `keywords`, opțional `classification`/`trust`/`expand: subdirs`.
2. Dacă trebuie, actualizezi `domains` la principalii din `04_CONFIG/access_policy.yaml`.
3. Rulezi `python 30_SCRIPTS/routing/build_route_manifest.py --check --summary`.

## Still open

- `vault_resolve` e lexical, pe metadate (nume, titlu, alias-uri, titluri de secțiuni, cuvinte-cheie de domeniu).
  Nu caută în corpul notelor; pentru asta e `vault_search` (MemoryController).
- Suportul exact al clienților pentru fișierele de configurare de proiect (`.codex/config.toml`,
  `.agents/mcp_config.json`) urmează documentația lor din octombrie 2026 și trebuie confirmat pe mașina owner-ului.
- Separarea principalilor locali pe o mașină cu un singur utilizator e declarativă. Garanția reală vine din
  plafonul canalului, secretele ținute în afara arborelui și contul read-only al botului.
