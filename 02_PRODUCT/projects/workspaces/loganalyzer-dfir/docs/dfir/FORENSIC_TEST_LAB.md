# LogAnalyzer — Laboratorul de testare forensică

Starea la 2026-10-05. Cod: `LogAnalyzer.Dfir.Tests/Corpus.cs`, `ForensicValidationTests.cs`, `Corpus/NanAgentCase/targets.json`.

## Corpusul

- Corpusul real (incidentul NanAgent de pe LAB-PC, 19.09.2026) este probă locală. **Nu se adaugă niciodată în repository.**
- Testele îl găsesc prin `LADFIR_CORPUS` (implicit `D:\FORENSIC_CASE`).
- `targets.json` conține doar căile relative și valorile așteptate, extrase manual din investigație:
  - nume, număr de rulări, ore;
  - bytes SRUM;
  - ID-uri de eveniment;
  - fluxuri PCAPNG;
  - numărul de intrări Amcache.
- Codul de producție nu citește acest fișier.

## Trei stări, niciodată confundate

| Situație | Testele pe corpus | Raport |
|---|---|---|
| corpusul e prezent integral | rulează | `FORENSIC VALIDATION = AVAILABLE (n/n …)` |
| corpusul lipsește parțial | secțiunile lipsă sunt SĂRITE | `FORENSIC VALIDATION = PARTIAL (k/n …)` + „rezultatul verde NU constituie validare forensică” |
| corpusul lipsește | toate SĂRITE | `FORENSIC VALIDATION = UNAVAILABLE (0/n …)` + aceeași mențiune |

Starea se scrie la fiecare rulare:
- testul `Forensic_validation_state_is_reported` nu e sărit niciodată și scrie `forensic_validation.txt` lângă binarele de test;
- în CI, pasul „Report forensic validation state” o pune în rezumatul jobului și emite un avertisment când nu e `AVAILABLE`.

Motivul fiecărui test sărit conține și el „FORENSIC VALIDATION = UNAVAILABLE”.

## Rulare de validare

```powershell
$env:LADFIR_CORPUS = "D:\FORENSIC_CASE"
$env:LADFIR_REQUIRE_CORPUS = "1"
dotnet test LogAnalyzer.Dfir.Tests
```

Cu `LADFIR_REQUIRE_CORPUS=1`, testele pe corpus nu mai sunt sărite. Dacă lipsește vreo secțiune, rularea **eșuează**: o validare nu poate fi verde fără corpus. Verificat: fără corpus, 9 eșecuri; cu corpus, totul trece.

## Ce acoperă corpusul azi

| Secțiune | Parser | Ce se verifică |
|---|---|---|
| prefetch, prefetchMsiexec | PrefetchParser | SETUP.EXE: 4 rulări, ultima la 14:56:26 UTC; MSIEXEC face referire la BOOTSTRAP_7D57.CMD |
| srum | SrumNetworkParser | msbuild.exe: 7.764.746 B trimiși, 100.581.358 B primiți, la 15:15 UTC |
| defenderEvtx, msiEvtx | EvtxParser | 1116 GenCodeInjected pe NanAgent32.exe; 1033 MsiInstaller CxUtilSvc Helper |
| pcapng | PcapngParser | peste 700 de fluxuri, DNS napps-1.com, SNI chatgpt.com |
| systemHive | SystemHiveExecutionParser, RawRegistry | BAM: 72 valori comparate cu `reg query` (70 identice, 2 mai noi în fereastra de 7 s dintre capturi); AppCompatCache identic octet cu octet cu `reg query` |
| amcache | AmcacheParser | peste 6000 de intrări, 0 corupte, SHA-1 pe peste 90% |
| (investigație) | pipeline | lanțul INCIDENT-CHAIN critic din 19.09.2026; proveniență completă; 0 constatări respinse |

## Ce lipsește (planificat)

- **Validare diferențială** cu un instrument independent (P12), de exemplu ieșirea EZTools sau Plaso pe aceleași fișiere.
- **Laborator anti-forensic** (P13): jurnale șterse, timestomping, Prefetch dezactivat, cu rezultate așteptate.
- **Teste de corupție** pentru fiecare parser. EVTX are `EvtxRepairTests`; celelalte nu au încă.
