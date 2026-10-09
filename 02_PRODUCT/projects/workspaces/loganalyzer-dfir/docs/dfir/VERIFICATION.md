# Stratul de verificare v1 (WP4)

Cod: `LogAnalyzer.Verification/` (asamblaj separat, referă doar `LogAnalyzer.Dfir.Core`). Punct de intrare:
`CaseVerifier.Verify(ws, options, ct)`. Ieșire: `Analysis/verification.json` (versionat, înregistrat în custodie),
intrare de audit `verification.run`.

## Ce este și ce nu este
- **Este:** o verificare automată, deterministă, care recitește cazul de pe disc (index de probe, `findings.json`,
  `graph.json`, `dependencies.json`, `timeline.csv`, reverificarea WP3b) și dă fiecărei constatări și fiecărei afirmații AI
  un verdict. Nu apelează codul care a produs constatările (reguli, corelare, parsere) și nu rescrie `findings.json`.
- **Nu este:** o verificare externă, independentă organizațional sau umană. Rulează în aceeași aplicație, pe aceleași date.
  Rapoartele scriu explicit „verificare automată, nu externă”.

## Verificări (id stabil → ce dovedește / ce nu dovedește)
| Id | Ce verifică | Rezultat la eșec | Ce NU dovedește |
|---|---|---|---|
| `UNSUPPORTED` | constatarea are cel puțin o probă de susținere | REJECTED | că probele sunt relevante |
| `PROVENANCE` | fiecare probă e în index, cu parser înregistrat și hash intact (nu MODIFIED/MISSING la ultima reverificare) | REJECTED (UNKNOWN dacă nu se poate confirma) | că sursa originală nu a fost alterată înainte de achiziție |
| `DEPENDENCIES` | `dependencies.json` listează aceleași probe (a doua cale) | UNKNOWN | — |
| `SUFFICIENCY` | tipul semantic are tipurile de probă cerute (ex.: execuție → Prefetch / Amcache / BAM / 4688 / Sysmon 1) | UNPROVEN | că execuția a avut efectul descris |
| `TEMPORAL` | început ≤ sfârșit, niciun timp după achiziție (toleranță de ceas 5 min), evenimentele în fereastra declarată | CONTRADICTED (UNKNOWN fără timpi) | ora reală când ceasul sursei a fost greșit |
| `GRAPH` | nodurile și muchiile constatării există în `graph.json` și duc la probe existente | UNKNOWN | — |
| `CONTRADICTIONS` | reguli de contradicție (v1: execuție înainte de crearea fișierului; execuția unui fișier absent; golire de jurnal cu înregistrări care acoperă momentul golirii) | CONTRADICTED | lipsa unei contradicții nu dovedește că nu există |
| `MISSING_EVIDENCE` | tipurile de artefact așteptate care lipsesc din caz (completează lista regulii) | informativ | — |
| `AI_CLAIMS` | fiecare afirmație AI: niciodată VERIFIED; contrazisă dacă citează o constatare REJECTED/CONTRADICTED | CONTRADICTED / UNPROVEN | corectitudinea raționamentului modelului |

## Verdict (cel mai sever câștigă)
REJECTED → CONTRADICTED → UNPROVEN → UNKNOWN → SUPPORTED → VERIFIED; NOT_ASSESSED când nicio verificare nu se aplică.
- **SUPPORTED:** suficient, proveniență intactă, consecvent, dintr-un singur tip de sursă.
- **VERIFIED:** la fel, dar cel puțin două tipuri independente de artefact concordă. Înseamnă doar atât; nu este o
  dovadă în sens juridic.
- O constatare de tip CORRELATION rămâne cel mult SUPPORTED (corelația nu este dovadă).

## Porți și afișare
- Exportul în Vault refuză propunerile REJECTED / CONTRADICTED (motivul în `vault_refused.json`); UNPROVEN / UNKNOWN se
  exportă marcate „neverificat”; verdictul călătorește cu propunerea.
- Raportul PDF al investigației, raportul de control și pagina de investigație arată o linie cu numărul de verdicte și,
  dacă există CONTRADICTED / REJECTED, un avertisment.

## Limite cunoscute
- Regulile de contradicție sunt puține (3 în v1); se adaugă prin `IContradictionRule`.
- Suficiența depinde de tipul semantic declarat de regulă; o constatare veche fără tip semantic primește NOT_ASSESSED.
- Un caz vechi fără `graph.json` / `dependencies.json` primește UNKNOWN cu motivul, nu o eroare.
