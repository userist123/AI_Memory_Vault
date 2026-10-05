# LogAnalyzer — Contractul parserelor

Starea la 2026-10-05 (P2). Cod: `LogAnalyzer.Dfir.Core/Parsing/IEvidenceParser.cs`, `ParserRegistry.cs`, `LogAnalyzer.Dfir.Windows/Parsers/WindowsParsers.cs`.

## Interfața

```csharp
public interface IEvidenceParser
{
    ParserDescriptor Descriptor { get; }
    PreflightResult Preflight(EvidenceItem item, string fullPath);
    ParseResult Parse(EvidenceItem item, string fullPath, IEventSink sink, CancellationToken ct);
}
```

`EvidenceParserBase` impune semantica erorilor (spec §5):
- excepție → `FAILED`, sau `PARTIAL` dacă s-au extras deja înregistrări;
- anulare → `PARTIAL`;
- zero înregistrări fără eroare → `EMPTY`;
- fișier lipsă → `NOT_AVAILABLE`.

`Preflight` = `EvidencePreflight.Check` cu formatele din descriptor (vezi `EVIDENCE_MODEL.md`).

## Descriptorul

| Câmp | Semnificație |
|---|---|
| `ParserId`, `Version` | identitatea, scrisă în fiecare eveniment, `ParseResult` și custodie |
| `Artifact` | ce citește, în cuvinte |
| `SourceTypes`, `FileNames` | ce probe îi revin după tipul declarat (`EventLog:*` = prefix) sau după nume |
| `Fingerprints` | formatele de conținut pe care le poate citi (`evtx`, `prefetch`, `prefetch_mam`, `ese`, `regf`, `pcapng`) |
| `SupportedOs`, `FormatVersions` | unde rulează și ce versiuni de format înțelege |
| `Limitations` | ce **nu** face; apare în `Analysis/parsers.json` al fiecărui caz |
| `Status` | `VALIDATED` (regresie pe corpus real) / `TESTED` (doar date sintetice, valori exacte) / `EXPERIMENTAL` (fără teste) |
| `Validation` | testele care susțin statusul |

Statusul vine din testele existente, nu din ce pretinde parserul.

## Selecția (`ParserRegistry.Select`)

1. **Candidații:** parserele al căror descriptor acceptă tipul sau numele probei.
2. **Fără candidat:** proba apare în `parsing.json` ca `SKIPPED_BY_DESIGN`, cu motivul („niciun parser înregistrat pentru tipul …”). Excepție: fotografiile live, care sunt citite de `LiveStateAnalyzer`. Nicio probă nu este ignorată în tăcere.
3. **Preflight** cu reuniunea formatelor candidaților. Dacă eșuează (lipsă, blocată, modificată, alt format), parsarea este refuzată: `FAILED` + gol + audit.
4. **Alegerea:** rulează parserul al cărui descriptor conține formatul recunoscut din conținut. Dacă sunt mai mulți, rezultatul e `FAILED` „ambiguu”; nu se alege la întâmplare.
5. **Post-verificare:** după parsare, `parser.Preflight` încă o dată. Dacă sursa s-a schimbat, evenimentele ei sunt eliminate.

Registrul refuză doi parseri cu același `ParserId`.

## Parserele înregistrate

| ParserId | Artefact | Formate | Status | Validare |
|---|---|---|---|---|
| EvtxParser 1.0 | EVTX | EVTX 3.x | VALIDATED | corpus (Defender, Application), EvtxRepairTests |
| PrefetchParser 1.0 | Prefetch | SCCA v30/v31, MAM | VALIDATED | corpus (SETUP.EXE, MSIEXEC.EXE) |
| SrumNetworkParser 1.0 | SRUM Network Data Usage | ESE | VALIDATED | corpus (msbuild.exe, bytes exacți) |
| PcapngParser 1.0 | PCAPNG | PCAPNG 1.0 | VALIDATED | corpus (fluxuri, DNS, SNI) |
| SystemHiveExecutionParser 1.0 | BAM, ShimCache | BAM Win10 1709+, ShimCache 10ts | TESTED | doar hive-uri sintetice |
| AmcacheParser 1.0 | Amcache InventoryApplicationFile | Win10/11 | VALIDATED | Amcache.hve real (>6000 intrări) + sintetic |

Limitările complete sunt în descriptori și în `Analysis/parsers.json`.

## Adăugarea unui parser

Parserul se adaugă doar cu:
- format real documentat;
- descriptor complet;
- teste cu valori exacte;
- test negativ (format greșit);
- test de corupție;
- corpus, când există.

Statusul pornește de la `EXPERIMENTAL` și urcă doar odată cu testele. Parserele vechi din `LogAnalyzer.Infrastructure/Parsers` nu sunt în registru (vezi `REALITY_AUDIT.md`, FACADE).
