# AI Memory Vault — Remediation v7

## FAZA 0 — Recunoaștere (read-only)

- [ ] F0.1 Inventar complet al fișierelor: count, extensii, top-level, dimensiuni
- [ ] F0.2 Tracked vs ignored; top 20 obiecte din istoricul Git după dimensiune
- [ ] F0.3 Hartă reală pentru `cognitive_core/`, `memory_controller/`, `vault_api.py`
- [ ] F0.4 Identificare module `DEAD_OR_ORPHAN`
- [ ] F0.5 Coverage reală per modul cu `pytest --cov`
- [ ] F0.6 Contradicții README „Memory V6” vs cod
- [ ] F0.7 Contradicții README „Arhitectura Cognitivă” vs cod
- [ ] F0.8 Verificare stării branch-ului și CI ca baseline
- [ ] F0.9 Scriere `tasks/audit-report.md` cu dovezi
- [ ] F0.10 **GATE: audit report prezentat pentru review uman**

## FAZA 1 — Securitate și igienă

- [ ] F1.1 Scanare `gitleaks detect --no-git=false`
- [ ] F1.2 Scanare manuală regex a artefactelor și întregului istoric
- [ ] F1.3 Raportare completă a eventualelor scurgeri înainte de curățare
- [ ] F1.4 Propunere tracking cleanup + justificare per intrare
- [ ] F1.5 Verificare dacă directoarele XAU sunt copii/divergențe
- [ ] F1.6 Pregătire `scripts/extract_xau.sh` fără rulare `filter-repo`
- [ ] F1.7 Pregătire nota `02_PROJECTS/XAU_Kinetic.md`
- [ ] F1.8 **GATE: orice ștergere/rescriere de istoric necesită aprobare explicită**

## FAZA 2 — Hardening ingestie

- [ ] F2.1 Izolare RAW din retrieval implicit
- [ ] F2.2 Test `ranked_search` fără `--include-raw` și cu flag explicit
- [ ] F2.3 Sanitizare NFKC + zero-width + Unicode Tags + bidi + homoglyph detection
- [ ] F2.4 `_SANITIZATION_REPORT.md` cu dovezi per fișier
- [ ] F2.5 Detector instructiv + 20 teste pozitive + 10 negative
- [ ] F2.6 Wrapper obligatoriu pentru RAW în context
- [ ] F2.7 Teste pentru toate căile RAW → context
- [ ] F2.8 Anti-laundering: `source_reviewed` + `source_sha256`
- [ ] F2.9 Trust decay pentru traversări către/dinspre non-official
- [ ] F2.10 Test graf sintetic poisoned node
- [ ] F2.11 **GATE: controale validate prin teste și/sau output verificabil**

## FAZA 3 — Evaluare empirică

- [ ] F3.1 Construire `evals/vault_eval.jsonl` 100–150 întrebări reale
- [ ] F3.2 Human-review marking pentru candidații generați automat
- [ ] F3.3 Smoke test LoCoMo/non-regression numai dacă este relevant
- [ ] F3.4 Baseline BM25/grep
- [ ] F3.5 + embeddings
- [ ] F3.6 + ACT-R activation decay
- [ ] F3.7 + spreading activation
- [ ] F3.8 Fiecare graf individual izolat
- [ ] F3.9 Full stack
- [ ] F3.10 P@5, R@5, MRR, tokens/query, p50 latency + Wilson intervals
- [ ] F3.11 Analiză accuracy + cost
- [ ] F3.12 Analiză `DORMANT_THRESHOLD = -2.0` și decay `d` la ±50%
- [ ] F3.13 Verdict obiectiv pentru fiecare modul cognitiv
- [ ] F3.14 Propunere mutare în `experimental/` pentru module nedemonstrate
- [ ] F3.15 Scriere `EVALUATION.md`

## FAZA 4 — Conformitate plugin

- [ ] F4.1 Determinare model repo: plugin / marketplace / ambele
- [ ] F4.2 `.claude-plugin/` conține numai `plugin.json`
- [ ] F4.3 Restructurare commands/agents/skills/hooks
- [ ] F4.4 Kebab-case + manifest consistency
- [ ] F4.5 `${CLAUDE_PLUGIN_ROOT}` pentru path-uri
- [ ] F4.6 `CHANGELOG.md`
- [ ] F4.7 Aliniere version cu `v7.0.0`
- [ ] F4.8 Git tag `v7.0.0`
- [ ] F4.9 `claude plugin validate .` curat

## FAZA 5 — README onest + DoD

- [ ] F5.1 Eliminare frontmatter auto-atestare
- [ ] F5.2 Repo positioning RAW→VERIFIED / provenance / canonical memory
- [ ] F5.3 Experimental claims numai cu rezultate din `EVALUATION.md`
- [ ] F5.4 Exemple reale din vault
- [ ] F5.5 `scripts/check_dod.sh`
- [ ] F5.6 Eliminare criterii DoD neverificabile automat
- [ ] F5.7 Păstrare doar badge-uri reflectate de CI real

## VERIFICARE FINALĂ

- [ ] V1 `tasks/audit-report.md` final
- [ ] V2 `tasks/lessons.md` actualizat
- [ ] V3 `EVALUATION.md` final
- [ ] V4 `SECURITY.md` final
- [ ] V5 `pytest --cov` post-remediere
- [ ] V6 `claude plugin validate .` post-remediere
- [ ] V7 CI relevant green
- [ ] V8 Breaking changes documentate în PR
- [ ] V9 Un singur PR pe `remediation/v7`
- [ ] V10 Review final uman

## REVIEW

Nicio fază nu este considerată completă fără dovadă verificabilă. Nu se execută
`git filter-repo`, ștergeri ireversibile sau rescrieri de istoric fără aprobare
explicită.