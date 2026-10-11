# LogAnalyzer — cercetare și plan de lucru

Data: 2026-10-04. Documentul pleacă de la cerințele proprietarului, formulate după investigația NanAgent de pe MARIUS-PC. Scopul aplicației este ca acea investigație să poată fi refăcută cu aplicația, pe orice stație, conectată sau izolată.

## 1. Cerințele, în ordinea în care au fost cerute

| # | Cerința | Status |
|---|---|---|
| 1 | O singură aplicație. Detectează dacă PC-ul are Internet: dacă are, rulează Network; dacă nu, rulează AirGapped, cu tot ce trebuie pe o stație izolată | **Făcut** (`LogAnalyzer.App`, `Core/Services/Connectivity`) |
| 2 | Când detectează un proces terț suspect, îi taie accesul la Internet **doar lui**, nu întregului PC. Îl izolează, îl scanează și verifică tot ce voia să facă. Datele se stochează pe evenimentul respectiv, iar la click se pot exporta ca PDF, inclusiv unde voia să ajungă procesul | **Făcut** (Etapa A: fila „Izolare procese suspecte”) |
| 3 | Cercetarea altor suite de forensics, pentru rețea și pentru PC-uri conectate | **Acest document** |
| 4 | Într-un domeniu: cercetare pe utilizatori, e-mailuri și alte module | **Făcut** (Etapa D: fila „Investigație domeniu și e-mail”) |
| 5 | Pe o stație izolată (un singur PC, fără domeniu): ce pot face utilizatorii și administratorii, pentru verificările de control | **Făcut** (Etapa C: fila „Control stație”, raport PDF de control) |
| 6 | Click pe orice eveniment deschide fișa lui completă, cu toate detaliile, ca în analiza făcută manual | **Făcut** (Etapa B: dublu-click pe orice rând → fișa completă, cu PDF) |
| 7 | Aplicația să poată reface investigația de pe MARIUS-PC: achiziție, parsare, timeline, corelare, findings, raport | **Făcut** (Etapa E: fila „Investigație completă”, validată pe corpusul NanAgent) |

## 2. Ce fac alte suite și ce preluăm

| Suită | Ce face bine | Licență | Ce preluăm |
|---|---|---|---|
| **Velociraptor** (Rapid7) | Colectare și hunt pe mii de endpoint-uri, cu artefacte descrise în VQL | AGPL v3; există și variantă comercială | **Ideea, nu codul.** Fiecare colector este un „artefact” declarativ (ce citește, de unde, ce status returnează). AGPL ne-ar obliga să publicăm aplicația, deci nu încorporăm cod |
| **KAPE** (Kroll) | Triere rapidă pe ținte: Prefetch, registry, EVTX, browser | Gratuit doar necomercial; interzis în produse comerciale | Lista de ținte ca referință, nu binarul |
| **Hayabusa** (Yamato Security) | Timeline EVTX rapid, singurul cu suport Sigma complet, inclusiv corelări v2 | AGPL v3; regulile sub DRL 1.1 | Formatul timeline-ului și nivelurile de severitate. Regulile Sigma se pot folosi cu atribuire (DRL), nu și binarul |
| **Chainsaw** (WithSecure) | Căutare rapidă în EVTX, Sigma prin motorul Tau | GPL v3 | Ca la Hayabusa: preluăm ideile; avem propriul parser EVTX |
| **PingCastle** | Scor de risc pentru Active Directory: configurări greșite, privilegii | Non-Profit OSL 3.0. Gratuit pentru auditul propriu; în produse comerciale cere licență | Lista de verificări AD (admini inactivi, delegări, politici de parole, SPN-uri). O implementăm noi, prin LDAP |
| **BloodHound CE** | Graful drumurilor de atac în AD / Entra ID | Apache 2.0 | Se poate integra legal. Modelul „cine poate ajunge la Domain Admins” |
| **Microsoft 365 / Exchange** | Message trace (`Get-MessageTraceV2`, `Start-HistoricalSearch`), cu date păstrate 90 de zile | API Microsoft | Modulul de e-mail din modul Network, prin conectorul M365 existent |
| **Magnet AXIOM, EnCase, X-Ways** | Analiză completă de disc și memorie, rapoarte pentru instanță | Comerciale | Modelul de raport: probă, hash, custodie, constatare, nivel de încredere |

**Concluzie despre licențe.** Pentru o aplicație licențiată și distribuită (LogAnalyzer), uneltele AGPL/GPL nu se pot încorpora fără a publica sursa. Le folosim ca referință de design, iar implementarea rămâne a noastră, cum s-a făcut deja cu parserele Prefetch, SRUM, EVTX și PCAPNG.

## 3. Etapele

### Etapa A — Izolarea procesului suspect (cerința 2)

Fluxul, într-un singur „Incident de proces”:

1. **Detectare.** Pornește dintr-o alertă live (modul Network), dintr-o constatare după scanare (ambele moduri) sau la cererea operatorului, cu click dreapta pe un proces.
2. **Izolare doar a programului, nu a PC-ului.**
   - O regulă Windows Firewall **outbound Block** pe calea executabilului: API-ul `INetFwPolicy2` (COM `HNetCfg.FwPolicy2`), pe toate profilurile. Plus o regulă inbound Block.
   - Regula primește un nume unic (`LogAnalyzer-Containment-<incident>-<sha256 scurt>`). Se poate anula cu un click și apare în jurnalul de audit.
   - Opțional, procesul se suspendă (`NtSuspendProcess`), cu acordul explicit al operatorului; se poate relua.
   - Restul PC-ului rămâne conectat.
3. **Unde voia să ajungă.** Pentru conexiunile blocate, Windows scrie evenimentul **5157** („The Windows Filtering Platform has blocked a connection”), cu calea aplicației, IP-ul și portul destinație. Aplicația activează auditarea „Filtering Platform Connection” (failure), cu acordul operatorului, și citește aceste evenimente. Le completează cu cache-ul DNS și cu conexiunile active ale procesului.
4. **Scanare.**
   - SHA-256 și semnătura Authenticode (semnatarul).
   - Scanare Microsoft Defender a fișierului (`MpCmdRun -Scan -ScanType 3 -File`).
   - Antet PE și importuri, șiruri de caractere (URL, IP, domenii, căi, chei de registry), prin `IocExtractor`.
   - Procesul părinte și linia de comandă.
   - Persistență: Run keys, servicii, task-uri programate și scripturi care referă executabilul.
   - Fișiere create sau modificate recent în același folder. Prefetch: de câte ori a rulat și când.
5. **Stocare.** Totul se salvează în cazul curent, ca **EvidenceItem**-uri cu SHA-256 și custodie, legate de incident. Fiecare constatare are clasificarea din spec (DIRECT / CORRELATED / CANDIDATE …) și nu primește niciodată ora curentă ca timestamp.
6. **Raport PDF** per incident, generat cu QuestPDF (deja în proiect):
   - rezumatul;
   - identitatea fișierului;
   - destinațiile încercate, fiecare cu sursa ei (5157 / DNS / conexiune);
   - persistența găsită;
   - verdictul scanării;
   - acțiunile de izolare, cu ora;
   - lanțul de custodie;
   - golurile de probă.

### Etapa B — Fișa completă a evenimentului (cerința 6)

- Dublu-click pe orice eveniment, alertă, element din timeline sau incident deschide fereastra **Detalii**, cu mai multe secțiuni:
  - sursa (fișier, canal, RecordID, SHA-256-ul probei);
  - toate câmpurile din EventData;
  - XML-ul brut;
  - explicația (ce înseamnă, MITRE);
  - evenimentele corelate (același proces, utilizator sau IP, ±N minute);
  - probele care o susțin;
  - nivelul de încredere.
- Din aceeași fereastră se poate face export PDF.

### Etapa C — Audit pentru stație izolată (cerința 5, modul AirGapped)

Un raport „Control stație”, gândit pentru verificări de conformitate (de exemplu HG 585/2002), care răspunde la întrebarea **ce a făcut și ce poate face fiecare utilizator și administrator**:

- conturile locale și membrii grupurilor privilegiate: când au fost create, modificate sau dezactivate (4720/4722/4725/4726/4728/4732/4738);
- autentificările: interactive, RDP, eșuate, în afara programului (4624/4625/4634/4648/4778), plus utilizarea privilegiilor (4672);
- ștergerea jurnalelor (1102/104), modificarea politicii de audit (4719), schimbarea orei (4616);
- dispozitive USB și medii amovibile: USBSTOR, MountedDevices, 6416, Partition/Diagnostic;
- software instalat sau dezinstalat (MsiInstaller 11707/11724), servicii noi (7045), task-uri programate (4698/4702);
- execuție (Prefetch, BAM, Amcache, ShimCache), PowerShell (4104, ConsoleHost_history);
- politici locale: parole, blocare cont, UAC, BitLocker, Defender (stare și excluderi);
- **conexiuni de rețea apărute pe o stație care ar trebui să fie izolată**: profilurile WLAN, istoricul adaptoarelor (NetworkList), SRUM network usage.

Fiecare verificare are un status (CONFORM / NECONFORM / NEDETERMINAT), probele pe care se sprijină și explicația. Raportul se exportă ca PDF.

### Etapa D — Investigație în domeniu (cerința 4, modul Network)

- **Utilizatori și grupuri (LDAP, `System.DirectoryServices`):**
  - conturi privilegiate, admini inactivi, parole care nu expiră;
  - delegări și SPN-uri (Kerberoasting), adminCount;
  - verificări în stilul PingCastle, implementate de noi.
- **Activitate de pe controlerele de domeniu:** autentificări, blocări de cont (4740), Kerberos (4768/4769/4771), modificări de grupuri, GPO.
- **E-mail:**
  - Exchange Online: message trace, sign-in-uri, reguli de inbox suspecte (forward extern), prin conectorul M365 existent, cu consimțământul administratorului de tenant;
  - Exchange on-prem: `Get-MessageTrackingLog`.
- **Stații din domeniu:** colectare la distanță prin pachetul `Dfir.Windows/Acquisition/RemoteCollection` (cerere autorizată, hash pe țintă, import cu custodie). Rezultatele intră în caz ca probe.

### Etapa E — Investigația completă (cerința 7)

Fazele 3–10 din master spec, rulate pe corpusul real al cazului NanAgent, unde se știe deja ce trebuie găsit:

- **colectoarele**, modelate după `AuditCollector.ps1`, dar cu statusuri reale;
- **timeline-ul unificat** (EVTX, Prefetch, SRUM, PCAPNG, registry);
- **corelarea**, inclusiv detectarea alterării jurnalelor, preluată din prototip;
- **findings**;
- **raportul final**, cu golurile de probă.

## 4. Principii care se aplică tuturor etapelor

- **Nimic nu pleacă de pe stație** decât prin funcțiile modului Network, iar acestea trec prin `NetworkPolicy`.
- **Acțiunile care schimbă stația cer confirmarea operatorului**, se pot anula și sunt jurnalizate. Exemple: reguli de firewall, suspendarea unui proces, activarea auditului.
- **Probele brute sunt read-only** și au SHA-256. Ce lipsește se raportează ca gol de probă, nu se ascunde.
- **Fără date simulate.** Unele ecrane moștenite afișează exemple fixe (de exemplu, o alertă „Cobalt Strike” scrisă direct în `MainViewModel`). Pe măsură ce etapele A–E înlocuiesc ecranele respective, exemplele dispar; nimic din raport nu provine din ele.

## Surse

- [Rapid7 — Velociraptor](https://www.rapid7.com/products/velociraptor/)
- [Yamato Security — Hayabusa](https://github.com/Yamato-Security/hayabusa)
- [LibHunt — Chainsaw](https://www.libhunt.com/r/chainsaw)
- [PingCastle — Download și licență](https://www.pingcastle.com/download/)
- [Netwrix — PingCastle vs BloodHound](https://netwrix.com/en/resources/blog/pingcastle-vs-bloodhound/)
- [Start with Identity — unelte open-source pentru AD (2026)](https://startwithidentity.com/articles/top-7-open-source-active-directory-security-tools/)
- [Microsoft Learn — Message trace în Exchange Online](https://learn.microsoft.com/en-us/exchange/monitoring/trace-an-email-message/message-trace-modern-eac)
- [Microsoft Learn — reguli outbound pentru programe în Windows Firewall](https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/create-an-outbound-program-or-service-rule)
- [text/plain — Windows Filtering Platform](https://textslashplain.com/2025/03/31/defensive-technology-windows-filtering-platform/)

## 5. Starea fazelor din specificația REAL DFIR v2 (2026-10-06)

| Fază | Stare | Document |
|---|---|---|
| P0 integritate, P1 model canonic și proveniență, P2 registru de parsere | făcut | `REALITY_AUDIT.md`, `EVIDENCE_MODEL.md`, `PARSER_CONTRACT.md` |
| P3 acoperire de artefacte | făcut pentru artefactele din `FORENSIC_TEST_LAB.md`; USN, $MFT, ADS nu sunt colectate | `FORENSIC_TEST_LAB.md` |
| P4 Evidence Graph | făcut; explorator tabelar în aplicație | `EVIDENCE_GRAPH.md` |
| P5 detecție (IOC, hash, YARA-lite, Sigma-lite, corelare) | făcut | `DETECTION.md` |
| P6 motor de politici, P7 execuție | făcut pentru registru; audit, cont și servicii doar citire | `POLICY_ENGINE.md` |
| P8 conformitate / OSCAL | făcut; schema NIST nevalidată; importul de cataloage OSCAL nu există | `COMPLIANCE_MODEL.md` |
| P9 domeniu în graf | făcut; doar date sintetice | `EVIDENCE_GRAPH.md` |
| P10 DFIR la distanță | făcut prin pachet; fără execuție la distanță din aplicație | `REALITY_AUDIT.md` |
| P11 laborator, P12 validare diferențială | făcut (`LADFIR_LAB=1`) | `FORENSIC_TEST_LAB.md` |
| P13 anti-forensics | făcut (13/16 tehnici observabile) | `ANTI_FORENSICS_TESTING.md` |
| P14 Memory Vault | propuneri prin poarta vault-ului; trimiterea rămâne la operator | `MEMORY_VAULT_INTEGRATION.md` |
| P15 AI pe probe | făcut, model local | `AI_FORENSIC_REASONING.md` |

Următorii pași, în ordinea valorii:
1. $MFT (închide AF08, timestomp). USN este citit din exportul `fsutil` (2026-10-07) și închide AF06 și AF07;
2. validarea exportului OSCAL cu schema oficială NIST;
3. testele ca administrator: aplicare HKLM, audit vs `auditpol`;
4. desenul grafului: azi, în fila „Graf de probe”, există căutare, relații cu probă sau derivare și drum între entități, în formă tabelară.
