# LogAnalyzer — Modelul probei și integritatea ei

Starea la 2026-10-05 (P0 + P1). Codul este sursa de adevăr: `LogAnalyzer.Dfir.Core/Model`, `Case/CaseWorkspace.cs`, `Integrity/EvidencePreflight.cs`.

## Ciclul unei probe

1. **Achiziție sau import** (`CaseWorkspace.ImportFile` / `RegisterStored`):
   - fișierul este copiat în `Raw/`, originalul nu este mutat;
   - se calculează SHA-256 și mărimea, iar copia devine ReadOnly;
   - se adaugă un rând în `Evidence/evidence_index.jsonl` și o intrare de custodie (`Logs/chain_of_custody.{csv,jsonl}`).
2. **Preflight** (`EvidencePreflight.Check`), înainte ca vreun parser să citească proba:

   | Verificare | Rezultat la eșec | Cod | Status spec §4 |
   |---|---|---|---|
   | fișierul există | `Missing` | `EVIDENCE_MISSING` | `NO_EVIDENCE` |
   | nu e blocat de alt proces | `Locked` | `EVIDENCE_LOCKED` | `READ_ERROR` |
   | se poate citi | `Unreadable` | `EVIDENCE_UNREADABLE` | `READ_ERROR` |
   | SHA-256 și mărimea sunt cele de la achiziție | `HashMismatch` / `SizeMismatch` | `EVIDENCE_MUTATED` | `MUTATED` |
   | formatul recunoscut din conținut (`EvidenceFingerprint`) e cel declarat | `FormatMismatch` | `EVIDENCE_FORMAT_MISMATCH` | `UNVERIFIED` |
   | există un hash de referință | `Unverified` (se parsează, marcat) | `EVIDENCE_UNVERIFIED` | `UNVERIFIED` |

   Totul în regulă = `Ok` / `AVAILABLE`. Se păstrează și `FileTimeUtc` (LastWriteTime al copiei).
   Numai `Ok` și `Unverified` permit parsarea. Altfel:
   - `ParseResult.Status = FAILED`, cu codul în `Error`;
   - se adaugă un `EvidenceGap`;
   - jurnalul de audit primește `evidence.mutated` sau `evidence.preflight_failed`.

   Proba **nu** este parsată, pentru că rezultatele nu ar proveni din sursa achiziționată.
3. **Parsare** (`EvidenceParserBase.Parse`): o excepție devine `FAILED` (sau `PARTIAL` dacă s-au extras înregistrări), niciodată `EMPTY`.
4. **Post-verificare**: SHA-256 se recalculează după parsare. Dacă sursa s-a schimbat în timpul citirii:
   - evenimentele extrase din ea sunt eliminate din cronologie;
   - rezultatul devine `FAILED` cu `EVIDENCE_MUTATED în timpul parsării`.
5. **Legarea provenienței** (`ProvenanceBinder`):
   - fiecare eveniment din cronologie primește `SourceSha256`, `ParserId`, `ParserVersion` (și în `timeline.csv`);
   - fiecare `EvidenceRef` dintr-o constatare primește SHA-256 al probei;
   - o constatare fără probă sau cu o referință către o probă care nu e în caz este **respinsă**: nu apare în raport, se scrie în `findings.json` la `RejectedFindings` și în jurnalul de audit (`finding.rejected`).
6. **Reverificare la raport** (`ReportIntegrity.Check`): toate probele cazului sunt verificate din nou când se generează PDF-ul. Dacă vreuna s-a schimbat după analiză, raportul începe cu „REZULTATE INVALIDE …”, iar secțiunea 5 listează starea fiecărei probe.
7. **Custodie pentru derivate**: `RecordTransformation` înregistrează parserul, versiunea și hash-ul fișierului derivat.

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
- `EvidenceItem` are acum `AcquisitionMethod` („acquired”/„imported”) și `ReadOnly`. Câmpurile din spec §3 corespund astfel: SourcePath=`OriginalPath`, SourceSha256=`Sha256`, AcquisitionTimestampUtc=`AcquiredAtUtc`, CollectorId=`Collector`, OriginalSize=`Size`. `Relationship` (graful, P4), `PolicyResult` (P6) și `ControlResult` (P8) nu sunt încă aduse la același contract; nu au fost create tipuri fără consumator.
