# LogAnalyzer DFIR — starea planului master (2026-11-08)

Planul este „LogAnalyzer DFIR Master Research and Implementation Plan”, secțiunea 6, modulele 6.1–6.17.

**Metoda de lucru:**
- Faza 0 a fost un inventar al codului existent, făcut cu căutări în cod, cu stările REAL / PARTIAL / MISSING.
- Apoi s-a construit ce lipsea, modul cu modul.
- Fiecare modul are teste. Codul cu risc a trecut printr-o revizuire independentă (Opus), în cel mult două runde.
- Fiecare modul a fost verificat în laboratorul izolat LA-Lab, cu capturi de ecran.

**Edițiile din laborator:**

| Ediție | Ce reprezintă | Unde rulează |
|---|---|---|
| Insula | stație izolată (air-gap) | LAB-WS02 |
| Arhipelag | rețea izolată (network air-gap) | LAB-WS01 |
| Continent | rețea cu Internet | LAB-WS01 |

## Starea pe module

| Modul | Înainte | Acum | Ce s-a făcut în această etapă | Verificat în laborator |
|---|---|---|---|---|
| 6.1 Gestionarea cazului | MISSING | **REAL** | Etape, roluri, sarcini, decizii pe constatări, lista de închidere, regula celor patru ochi, jurnal înlănțuit (`CASE_WORKFLOW.md`) | Insula, Arhipelag, Continent |
| 6.2 Integritate și custodie | REAL | REAL | — (hash la import, custodie înlănțuită, reverificare, read-only) | — |
| 6.3 Artefacte Windows | REAL | REAL | — (EVTX, registru, Prefetch, Amcache/ShimCache, LNK/Jump Lists, SRUM, USN, browsere, sarcini, servicii, SetupAPI) | — |
| 6.4 Anti-forensics și acoperire | PARTIAL | **REAL** | Acoperirea în timp: hartă sursă × zi, lacune doar CANDIDATE (`COVERAGE_TIMELINE.md`) | Insula, Continent |
| 6.5 Rețea | PARTIAL | **REAL (fără Zeek)** | JA3 pe ClientHello, indicatori `ja3` (`NETWORK_JA3.md`) | test de unitate (laboratorul nu are trafic TLS) |
| 6.6 Memorie | PARTIAL | **PARTIAL+** | Parser pentru ieșirile JSON ale Volatility 3, candidați psscan-only (`MEMORY_VOLATILITY.md`) | Arhipelag (ieșiri sintetice) |
| 6.7 Triaj binare | PARTIAL | **REAL** | Cititor PE mărginit, indicatori explicați, Authenticode al stației (`PE_TRIAGE.md`) | Arhipelag |
| 6.8 Ingineria detecției | PARTIAL | **REAL (laborator)** | Laboratorul de reguli: test pe caz, comparare cu referința, adăugare în caz (`RULE_LAB.md`) | Arhipelag |
| 6.9 Cronologie avansată | PARTIAL | **REAL** | Marcaje, note, vederi salvate, compararea vederilor (`TIMELINE_NOTES.md`) | Insula, Arhipelag, Continent |
| 6.10 Identitate și AD | PARTIAL | PARTIAL | — (Kerberos/NTLM, A19 pe DC, delta AD în motorul vechi) | — |
| 6.11 Graf și contradicții | REAL | **REAL+** | Constatările contrazise intră obligatoriu în revizuire | test (laboratorul nu are constatări contrazise) |
| 6.12 TI offline | MISSING | **REAL** | Pachete semnate: așteptare, aprobare, revenire, reverificare la fiecare analiză (`THREAT_INTEL_PACKAGES.md`) | Insula (refuz); calea pozitivă în test cu CMS real |
| 6.13 Politici și conformitate | REAL | REAL | — | — |
| 6.14 Răspuns controlat | PARTIAL (inventar) | **REAL** | Confirmat: aprobare legată de amprenta textului (politici) și de identitatea exactă a procesului (izolare: PID, cale, pornire, SHA-256) | — |
| 6.15 Raportare și export | PARTIAL | PARTIAL | — (manifest, verificator, mascare PII la export) | — |
| 6.16 Reluare și diferențe | MISSING | **REAL** | Rulări imuabile, reanaliză, diferența cu sursa probabilă (`ANALYSIS_RUNS.md`) | Insula, Arhipelag, Continent |
| 6.17 AI ancorat în probe | REAL | REAL | — (citări cu locator, UNPROVEN până la verificare, detectarea injecției din probe) | — |

## Revizuiri independente

Fiecare modul cu risc a avut un revizor separat, cu probe reproduse. Defectele găsite au fost corectate și au teste de regresie.

| Modul | Ce a găsit revizorul |
|---|---|
| 6.1 | 4 ocoliri HIGH ale regulii celor patru ochi și ale integrității jurnalului |
| 6.16 | un ID de regulă duplicat care bloca rulările; o linie de audit falsificată acceptată |
| 6.9 | coliziunea prefixului de audit cu exportul mascat |
| 6.12 | două defecte HIGH: fișier de reguli nelistat folosit fără verificare; depozit fără ACL; plus trunchierea istoricului și TOCTOU |
| 6.7 | o tabelă de importuri care producea un milion de funcții; după 12.000 de mutații, nicio excepție |

## Reziduuri acceptate (de confirmat de proprietar)

- **6.1:** trunchierea simultană, de aceeași lungime, a jurnalului fluxului și a auditului cazului nu se vede. Auditul cazului nu este sigilat cu cheie. Ce rămâne ca detecție: capătul fluxului trecut în registrul de închidere.
- **6.12:** un administrator poate restaura o stare veche, completă și sigilată a depozitului de pachete; sigiliul nu are contor monoton.
- **6.16:** deciziile din fluxul de lucru sunt legate de numărul constatării (F-nnnn), care se poate schimba la reanaliză.

## Laboratorul

- **VM-uri:** DC01, SRV01, WS01, WS02, pe un comutator Hyper-V privat, fără Internet.
- **Licențele Windows de evaluare au expirat:** VM-urile se opresc singure din oră în oră, iar watchdog-ul le repornește. Testele s-au programat în ferestrele de funcționare; licențierea nu a fost ocolită.
- **Driverul UIA de laborator:** `settext` acceptă acum `\n` (unealtă de laborator, nu cod de produs).
- **Capturile:** sunt în `E:\LA-Lab\shots\LAB-WS0x\`, în afara depozitului, pentru că arată conturi de laborator.

## Suitele de teste (ultima rulare)

| Suită | Teste trecute |
|---|---|
| Dfir | 2.808 |
| App | 1.026 |
| UI | 368 |
| Edition | 45 |
