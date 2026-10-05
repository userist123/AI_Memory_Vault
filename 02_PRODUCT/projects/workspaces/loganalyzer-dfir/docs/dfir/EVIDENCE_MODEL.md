# LogAnalyzer — Modelul probei și integritatea ei

Starea la 2026-10-05 (P0). Codul este sursa de adevăr: `LogAnalyzer.Dfir.Core/Model`, `Case/CaseWorkspace.cs`, `Integrity/EvidencePreflight.cs`.

## Ciclul unei probe

1. **Achiziție sau import** (`CaseWorkspace.ImportFile` / `RegisterStored`):
   - fișierul este copiat în `Raw/`, originalul nu este mutat;
   - se calculează SHA-256 și mărimea, iar copia devine ReadOnly;
   - se adaugă un rând în `Evidence/evidence_index.jsonl` și o intrare de custodie (`Logs/chain_of_custody.{csv,jsonl}`).
2. **Preflight** (`EvidencePreflight.Check`), înainte ca vreun parser să citească proba:

   | Verificare | Rezultat la eșec | Cod |
   |---|---|---|
   | fișierul există | `Missing` | `EVIDENCE_MISSING` |
   | se poate citi | `Unreadable` | `EVIDENCE_UNREADABLE` |
   | SHA-256 și mărimea sunt cele de la achiziție | `HashMismatch` / `SizeMismatch` | `EVIDENCE_MUTATED` |
   | formatul recunoscut din conținut (`EvidenceFingerprint`) e cel declarat | `FormatMismatch` | `EVIDENCE_FORMAT_MISMATCH` |

   Numai `Ok` permite parsarea. Altfel:
   - `ParseResult.Status = FAILED`, cu codul în `Error`;
   - se adaugă un `EvidenceGap`;
   - jurnalul de audit primește `evidence.mutated` sau `evidence.preflight_failed`.

   Proba **nu** este parsată, pentru că rezultatele nu ar proveni din sursa achiziționată.
3. **Parsare** (`EvidenceParserBase.Parse`): o excepție devine `FAILED` (sau `PARTIAL` dacă s-au extras înregistrări), niciodată `EMPTY`.
4. **Post-verificare**: SHA-256 se recalculează după parsare. Dacă sursa s-a schimbat în timpul citirii:
   - evenimentele extrase din ea sunt eliminate din cronologie;
   - rezultatul devine `FAILED` cu `EVIDENCE_MUTATED în timpul parsării`.
5. **Custodie pentru derivate**: `RecordTransformation` înregistrează parserul, versiunea și hash-ul fișierului derivat.

## Ce se păstrează în `ParseResult` (Analysis/parsing.json)

| Câmp | Semnificație |
|---|---|
| `Parser`, `ParserVersion` | identitatea parserului care a produs datele |
| `ExpectedSha256` | SHA-256 de la achiziție |
| `SourceSha256Before` / `SourceSha256After` | SHA-256 imediat înainte și imediat după parsare; trebuie să fie egale cu `ExpectedSha256` |
| `SourceFingerprint` | formatul recunoscut din conținut: `evtx`, `prefetch`, `prefetch_mam`, `ese`, `regf`, `pcapng`, `json`, `unknown`, `empty` |
| `Status` | `SUCCESS` / `EMPTY` / `FAILED` / `NOT_AVAILABLE` / `PARTIAL` / `SKIPPED_BY_DESIGN` |
| `Records`, `MalformedRecords`, `Gaps` | ce s-a extras și ce lipsește |

## Timpul

- `Timestamp` păstrează valoarea brută și semnificația ei (`TimeSemantics`: „event recorded”, „last execution (BAM)”, „file last modified (ShimCache) — not execution”).
- Timpul lipsă este `Timestamp.Unknown()`. **Ora curentă nu înlocuiește niciodată ora unei probe.**
- În aplicația moștenită (`ParsedEvent.TimeCreated`, de tip `DateTime`), timpul lipsă rămâne `default`. Exportul și interfața îl afișează ca „-” sau „necunoscut”.

## Fiecare eveniment trimite înapoi la probă

`TimelineEvent` are `EvidenceId` + `Locator`. Exemple de locator:
- EventRecordID;
- rând SRUM;
- cadru PCAPNG;
- cale în hive.

`Finding.SupportingEvidence` este lista de `EvidenceRef(EvidenceId, Locator, Description)`. O constatare fără probă nu este emisă.

## Limite cunoscute

- Hash-ul de referință este cel calculat la intrarea în caz. Dacă sursa fusese deja modificată înainte de achiziție, preflight-ul nu poate ști.
- Fișierele derivate (`Analysis/*`) sunt rescrise la fiecare rulare. Integritatea lor se urmărește prin custodie, nu prin ReadOnly.
- Modelul canonic unificat (P1: EvidenceItem/EvidenceEvent/Finding/Gap/Relationship/ParserResult/PolicyResult/ControlResult cu proveniență comună) nu este încă implementat.
