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
| 6.5 Rețea | PARTIAL | **REAL** | JA3 pe ClientHello, indicatori `ja3` (`NETWORK_JA3.md`); jurnalele Zeek conn/dns/http/ssl/notice, TSV și JSON, cu precizie de microsecunde și jurnal trunchiat semnalat (`NETWORK_ZEEK.md`); beaconing, scanări și procesul din spatele conexiunii prin tuplul exact cu Sysmon 3 (`NETWORK_HEURISTICS.md`) | JA3: test de unitate (laboratorul nu are trafic TLS); Zeek și euristicile: Insula, cu jurnale sintetice (beacon la 5,1 min, scanare de 30 de porturi) |
| 6.6 Memorie | PARTIAL | **REAL** | Parser pentru ieșirile JSON ale Volatility 3; reguli MEM-HIDDEN, MEM-INJECT, MEM-NETPROC, MEM-PATH; corelarea memorie–disc MEM-NODISK; o ieșire goală este „incompletă”, nu succes; rulare controlată din aplicație a unui Volatility aprobat de administrator (folderul uneltei și depozitul de simboluri fixate prin manifeste cu amprente, foldere doar pentru administratori, fără rețea, ieșiri ca probe derivate) (`MEMORY_VOLATILITY.md`) | Arhipelag și Continent (ieșiri sintetice); rularea controlată: Insula, Arhipelag, Continent, cu o unealtă TEST în locul Volatility |
| 6.7 Triaj binare | PARTIAL | **REAL** | Cititor PE mărginit, indicatori explicați, Authenticode al stației (`PE_TRIAGE.md`) | Arhipelag |
| 6.8 Ingineria detecției | PARTIAL | **REAL** | Laboratorul de reguli: test pe caz, comparare cu referința, adăugare în caz (`RULE_LAB.md`). Pachetele semnate pentru toată stația sunt pachetele 6.12: `sigma/` și `yara/` din pachetul activ intră în motorul de detecție la fiecare analiză (`DetectionEngine.Load`). Regulile de corelare Sigma (event_count, value_count, temporal, temporal_ordered), cu ferestre deterministe; absența („mai puțin de N”) și înlănțuirea sunt refuzate explicit. | Arhipelag și Continent: corelarea pe jurnalul real al DC01 găsește 3 rafale de autentificări eșuate pentru același cont |
| 6.9 Cronologie avansată | PARTIAL | **REAL** | Marcaje, note, vederi salvate, compararea vederilor (`TIMELINE_NOTES.md`) | Insula, Arhipelag, Continent |
| 6.10 Identitate și AD | PARTIAL | **REAL** | Identități după SID între controlere, servere și stații: redenumiri, istoric SID ca legătură, sesiuni pe stație, grupuri privilegiate, modificări de director și GPO legate de autor, lacune pe domeniu când lipsesc jurnalele DC; calea contului stație → sesiune → proces → resursă, legată doar prin stație + LogonId și ProcessGuid, cu lacunele numărate; secțiunea „Conturi, sesiuni și căi” în raport (`IDENTITY_AD.md`) | Insula, Arhipelag, Continent (jurnalele Security reale ale DC01 și SRV01: 70 de identități, 5.762 de sesiuni, 133 de procese legate de sesiunile lor) |
| 6.11 Graf și contradicții | REAL | **REAL+** | Constatările contrazise intră obligatoriu în revizuire | test (laboratorul nu are constatări contrazise) |
| 6.12 TI offline | MISSING | **REAL** | Pachete semnate: așteptare, aprobare, revenire, reverificare la fiecare analiză (`THREAT_INTEL_PACKAGES.md`) | Insula (refuz); calea pozitivă în test cu CMS real |
| 6.13 Politici și conformitate | REAL | REAL | — | — |
| 6.14 Răspuns controlat | PARTIAL (inventar) | **REAL** | Confirmat: aprobare legată de amprenta textului (politici) și de identitatea exactă a procesului (izolare: PID, cale, pornire, SHA-256) | — |
| 6.15 Raportare și export | PARTIAL | **REAL** | Politica de redactare pe categorii, reversibilă (tabela DPAPI a contului) sau ireversibilă; previzualizare; înregistrarea fiecărui export legată de starea cazului; dezvăluire cu motiv în audit (`REDACTION_POLICY.md`) | Insula (previzualizare), Arhipelag, Continent (export reversibil, înregistrare, dezvăluire, audit fără valoarea originală) |
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
| 6.10 | 3 defecte HIGH: un istoric SID plantat ascundea activitatea administratorului; deduplicarea pierdea evenimente după golirea jurnalului; un nume era atribuit SID-ului din alt domeniu. Runda 2: lacuna de controler ascunsă de un tichet cross-realm; privilegii 4672 puse pe o sesiune veche. Calea contului (2 runde): un acces dat procesului care a refolosit PID-ul; evenimente nelegate care dispăreau fără să fie numărate; un proces închis doar în înregistrarea Security, nu și în cea Sysmon; o sesiune fără delogare care trecea peste repornire |
| 6.5 / 6.8 (corelare, euristici) | HIGH: un `timespan` uriaș într-o regulă de caz oprea toată analiza. MEDIUM: beacon-ul numea un singur proces pentru conexiuni ale mai multor procese; un câmp group-by lipsă grupa toate conturile; fereastra scanării era pătratică; limita de constatări era tăcută. Runda 2: o oră lângă anul 9999 oprea corelarea. În laborator: avertisment fals despre câmpurile `product` și `service`; textele „cineva … a deschis” |
| 6.15 | un nume de tabelă luat din înregistrare (cale UNC); UPN-ul și domeniul din cale scăpau nemascate; înregistrare în caz sigilat. În laborator: exportul mascat cădea când un UPN era învățat de două ori (corectat, cu test) |

## Reziduuri acceptate (de confirmat de proprietar)

- **6.1:** trunchierea simultană, de aceeași lungime, a jurnalului fluxului și a auditului cazului nu se vede. Auditul cazului nu este sigilat cu cheie. Ce rămâne ca detecție: capătul fluxului trecut în registrul de închidere.
- **6.12:** un administrator poate restaura o stare veche, completă și sigilată a depozitului de pachete; sigiliul nu are contor monoton.
- **6.16:** deciziile din fluxul de lucru sunt legate de numărul constatării (F-nnnn), care se poate schimba la reanaliză.

- **6.10:** același controler scris o dată cu numele scurt și altă dată cu FQDN apare ca două stații. Domeniile se compară după prima etichetă. Rândurile CSV identice din probe diferite se numără de două ori.
- **6.8:** două reguli de bază fără `id` în același fișier au același RuleId (numele fișierului); amprentele diferă. `generate` este acceptat, dar regulile de bază raportează mereu și propriile potriviri.
- **6.5:** pragurile euristicilor sunt fixe (8 conexiuni, ±20%, 20 de destinații în 60 s); un beacon cu jitter mare sau o scanare lentă nu se văd.
- **6.15:** cu utilizatorii păstrați, o persoană din registru se pune la loc cu numele complet. Ordinea în care masca învață numele poate face ca un câmp și rezumatul aceluiași rând să difere. Lista redactărilor nu reverifică amprenta din audit.

## Laboratorul

- **VM-uri:** DC01, SRV01, WS01, WS02, pe un comutator Hyper-V privat, fără Internet.
- **Licențele Windows de evaluare au expirat:** VM-urile se opresc singure din oră în oră, iar watchdog-ul le repornește. Testele s-au programat în ferestrele de funcționare; licențierea nu a fost ocolită.
- **Driverul UIA de laborator:** `settext` acceptă acum `\n` (unealtă de laborator, nu cod de produs).
- **Capturile:** sunt în `E:\LA-Lab\shots\LAB-WS0x\`, în afara depozitului, pentru că arată conturi de laborator.

## Suitele de teste (ultima rulare)

| Suită | Teste trecute |
|---|---|
| Dfir | 2.904 |
| App | 1.026 |
| UI | 368 |
| Edition | 45 |
