# Cercetare 2026-10-11: concurența LogAnalyzer (DFIR + control de conformitate pe stații izolate)

Stare: cercetare web. Fiecare rând cu sursă e `DOCUMENT_VERIFIED`, dar verificat **doar la nivelul rezumatului paginii întors de motorul de căutare**: paginile nu au fost citite integral și niciun produs nu a fost rulat. Nimic de aici nu e `RUNTIME_VERIFIED`.

Marcaje:
- **[NESIGUR]**: sursă secundară (agregator, oglindă, rezumat generat de AI pe G2), sursă contradictorie, versiune veche sau afirmație din cunoștințe generale fără sursă găsită în sesiune.
- **[DE VERIFICAT]**: în coloana LogAnalyzer, acolo unde starea nu poate fi stabilită din documentele proiectului fără a rula aplicația.
- `*` lângă un simbol din matrice: valoare din cunoștințe generale, fără sursă în această sesiune, adică **[NESIGUR]**.

Coloana LogAnalyzer citează documente din `docs/dfir/` (`REALITY_AUDIT.md`, `DETECTION.md`, `AI_FORENSIC_REASONING.md`, `UNWIRED_COMPONENTS.md`, `EDITIONS.md`) și mesajele commit-urilor recente. Asta e `DOCUMENT_VERIFIED` intern, nu dovadă de rulare.

Depozitul e public: documentul nu conține identificatori de organizație, doar nume de produse comerciale și surse publice.

---

## 0. Produsele și abrevierile

| Abreviere | Produs(e) | Categorie |
|---|---|---|
| AXM | Magnet AXIOM / AXIOM Cyber / Magnet Response / Magnet REVIEW (+ Magnet Copilot) | suită forensică |
| BLK | Belkasoft X (+ BelkaGPT) | suită forensică |
| FTK | Exterro FTK (Forensic Toolkit, FTK Imager) | suită forensică |
| CEL | Cellebrite Inspector (+ Digital Collector) | suită forensică (computere) |
| ENC | OpenText EnCase Forensic | suită forensică |
| XWF | X-Ways Forensics | suită forensică |
| AUT | Autopsy (The Sleuth Kit) | suită forensică, open source |
| VR | Velociraptor | triaj / IR, open source |
| KAPE | Kroll KAPE | triaj (colectare + parsare) |
| CT | Cyber Triage | triaj / IR |
| BIN | Binalyze AIR (+ DRONE, Investigation Hub) | triaj / IR |
| ORC | DFIR-ORC (ANSSI) | colector |
| NWX | Netwrix Auditor | audit / conformitate |
| MEA | ManageEngine ADAudit Plus / Log360 | audit / SIEM |
| MED | ManageEngine Device Control Plus | control dispozitive |
| LEP | Lepide Data Security Platform | audit |
| NES | Tenable Nessus (audituri de conformitate) | conformitate |
| CIS | CIS-CAT Pro (Assessor + Dashboard) | conformitate |
| MSPA | Microsoft Security Compliance Toolkit: Policy Analyzer + LGPO | conformitate (gratuit) |
| MDVM | Microsoft Defender Vulnerability Management (Security baselines assessment) | conformitate (cloud) |
| IVN | Ivanti Device and Application Control | control dispozitive / aplicații |
| EPP | Endpoint Protector (acum Netwrix) | control dispozitive |
| WAZ | Wazuh | SIEM-lite, open source |
| GRL | Graylog (Open / Security) | SIEM-lite |
| ELS | Elastic Security | SIEM |
| MSC | Microsoft Security Copilot | asistent AI (cloud), doar la secțiunea AI |
| LA | LogAnalyzer | produsul nostru |

Legendă matrice: **●** = are (documentat), **◐** = parțial sau cu condiții, **○** = nu are sau nu e destinat, **?** = necunoscut în această sesiune.

---

## 1. Matricea de funcții

Matricea e împărțită în trei tabele, ca să rămână lizibilă: A (DFIR, 33 de rânduri), B (control și conformitate, 23 de rânduri), C (transversal: offline, AI, licențiere, utilizare, 12 rânduri). În total 68 de funcții. Sursele sunt numerotate `[Sn]` în §6.

### 1A. DFIR: colectare, parsare, analiză, raportare

| # | Funcție | AXM | BLK | FTK | CEL | ENC | XWF | AUT | VR | KAPE | CT | BIN | ORC | LogAnalyzer: are / parțial / lipsește — verifică | Surse |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| A1 | Imagine completă de disc | ◐ (imagistică slabă după un utilizator) | ●* | ●* | ● (Digital Collector) | ●* | ●* | ○ (doar analiză după colectare) | ○ (nu e deaddisk) | ○ | ?| ●* | ○ | lipsește [DE VERIFICAT]; nici nu e necesar pe stația clasificată | S1, S41, S34, S12 |
| A2 | Colectare de triaj live (fișiere blocate, NTFS brut) | ● (Response) | ? | ◐* | ? | ◐* | ◐* | ○ | ● | ● | ●* | ● | ● | parțial [DE VERIFICAT] (citește jurnale/hive-uri de pe stația locală; acces NTFS brut neconfirmat) | S42, S12, S14, S18, S19 |
| A3 | Colector offline portabil (un singur exe, USB, „sneakernet”) | ● (Response, sub 2 MB) | ? | ○* | ? | ? | ◐* (rulează de pe stick) | ○ | ● | ● | ●* | ● | ● | lipsește ca exe separat [DE VERIFICAT] | S42, S12, S15, S18, S19, S36 |
| A4 | Captură RAM | ● (DumpIt) | ●* | ◐* | ● (analiză memorie Windows) | ●* | ○* | ○ | ●* | ◐ (prin module) | ?| ●* | ● [NESIGUR] | lipsește (`VolatilityBridgeParser` e FACADE, `UNWIRED_COMPONENTS.md`) | S42, S41, S14, S19 |
| A5 | Criptarea colecției cu cheie publică (X509/PGP) | ? | ? | ? | ? | ? | ? | ○ | ● | ? | ? | ? | ? | lipsește [DE VERIFICAT] | S13 |
| A6 | Hash la achiziție și reverificare ulterioară | ● (hash încorporat) | ●* | ●* | ●* | ●* | ●* | ●* | ●* | ◐* | ●* | ●* | ?| are (`INTEGRITY_RECHECK.md`; analiza AI pornește doar dacă hash-ul mai corespunde) | S42 |
| A7 | Parsare EVTX | ●* | ●* | ●* | ●* | ●* | ●* | ●* | ● | ● | ●* | ●* | ● (colectare) | are (locator RecordID, `REALITY_AUDIT.md`) | S15, S19 |
| A8 | Prefetch | ●* | ●* | ●* | ●* | ●* | ●* | ●* | ●* | ● | ●* | ●* | ● (colectare) | are (`REALITY_AUDIT.md`, teste pe corpus) | S19 |
| A9 | Amcache / ShimCache / BAM | ●* | ●* | ●* | ● | ●* | ◐* | ◐* | ●* | ● | ●* | ●* | ● (colectare) | are; distinge „prezență” de „execuție” (`REALITY_AUDIT.md`) | S82 |
| A10 | SRUM | ●* | ●* | ?| ● | ? | ? | ◐* | ●* | ● | ?| ? | ? | are (`SrumNetworkParser`, `REALITY_AUDIT.md`) | S41 |
| A11 | $MFT și jurnal USN | ●* | ●* | ●* | ●* | ●* | ●* | ●* | ● (parser NTFS) | ● | ●* | ●* | ● | parțial: USN din export `fsutil`; `MftParser` e FACADE | S13, S15, S19 |
| A12 | Shellbags / Jumplists / LNK | ●* | ●* | ●* | ● | ●* | ●* | ◐* | ●* | ● | ●* | ●* | ?| lipsește în producție (`LnkParserPlugin` FACADE) | S41 |
| A13 | Volume Shadow Copies | ●* | ●* | ●* | ● | ●* | ●* | ? | ●* | ● | ? | ? | ● [NESIGUR] | [DE VERIFICAT] | S41 |
| A14 | Istoric browser | ●* | ●* | ●* | ● | ●* | ●* | ●* | ●* | ● | ●* | ●* | ?| are (Chromium, regula DOWNLOAD-THEN-EXEC) | S41 |
| A15 | Istoric forensic USB / medii (registru, setupapi) | ●* | ●* | ●* | ● (AmCache: dispozitive) | ●* | ●* | ●* | ●* | ● | ●* | ●* | ● | are în pagina de medii live (commit 911333941); `UsbForensicsParser` vechi e FACADE [DE VERIFICAT] | S41 |
| A16 | Dispozitive mobile | ● | ● | ●* | ◐ (Inspector e pentru computere) | ●* | ◐* | ○ | ○ | ○ | ○* | ◐* | ○ | lipsește (în afara scopului) | S1, S33, S41 |
| A17 | Achiziție cloud (M365, Google Workspace) | ● | ●* | ◐* | ? | ◐* | ○ | ○ | ◐* | ○ | ◐ (automatizare cloud) | ◐ (Tornado) | ○ | lipsește (`M365EntraIdLogsParser` FACADE); irelevant pe stație izolată | S3, S10, S16 |
| A18 | Cronologie unificată (super-timeline) | ● | ● | ●* | ● | ●* | ●* | ● | ●* | ◐ (CSV) | ●* | ● | ○ (nu analizează) | are (cronologie + export) | S59, S34, S41, S19 |
| A19 | Reguli Sigma pe EVTX | ?| ? | ? | ? | ? | ? | ◐* (modul) | ●* | ◐* (modul Hayabusa/Chainsaw) | ?| ● | ○ | parțial: SigmaLite, subset, fără `timeframe`/`count` (`DETECTION.md`) | S16, S70 |
| A20 | YARA | ●* | ●* | ●* | ? | ●* | ◐* | ●* | ●* | ◐* | ●* | ● | ●* | parțial: YaraLite, subset (`DETECTION.md`) | S16 |
| A21 | Potrivire IOC / hash (NSRL, KFF, CAID, Project VIC) | ●* | ●* | ● (CAID, Project VIC) | ●* | ●* | ●* | ●* | ●* | ○ | ●* | ●* | ○ | are (`IocMatcher`); fără seturi NSRL/KFF incluse [DE VERIFICAT] | S75b |
| A22 | Scor automat de suspiciune / verdict | ◐* | ◐* | ? | ? | ? | ○ | ◐* | ○* | ○ | ● (determinist) | ● (High/Medium/Low/Matched) | ○ | are (severitate pe carduri de constatare) [DE VERIFICAT] | S11, S16 |
| A23 | Mapare MITRE ATT&CK | ?| ? | ? | ? | ? | ○ | ○* | ●* | ○ | ●* | ● | ○ | are (149 de tehnici explicate, commit 911333941) | S17 |
| A24 | Detecție anti-forensics (ștergere jurnal, timestomp, VSS șterse) | ◐* | ◐* | ◐* | ? | ◐* | ◐* | ◐* | ◐* | ○ | ●* | ●* | ○ | are pentru 15 din 16 tehnici; $MFT timestomp și ADS UNDETERMINED (`REALITY_AUDIT.md`) | – |
| A25 | Lanțuri de incident (corelare multi-artefact) | ◐* (Connections) | ◐* | ◐* | ? | ○* | ○ | ○* | ◐* (VQL) | ○ | ◐* | ◐ | ○ | are (ex.: descărcare → execuție în 6 ore) | S17 |
| A26 | Card de constatare cu probă, locator, hash | ◐* | ◐* | ◐* | ◐* | ◐* | ○ | ◐* | ○ | ○ | ◐ (note și scoruri pe element) | ◐ (findings + verdict) | ○ | are (`FINDING_CARD.md`, `FINDING_CONTRACT.md`) | S10, S17 |
| A27 | Lanț de custodie documentat | ? (portable case nu documentează custodia) | ?| ●* | ? | ●* | ◐* | ◐* | ◐* | ◐* (jurnal) | ?| ●* | ◐* | are [DE VERIFICAT] | S69, S73 |
| A28 | Raport de goluri de probă / acoperire audit | ? | ? | ? | ? | ? | ? | ? | ? | ? | ? | ? | ? | are (`AuditCoverage`: NOT_AVAILABLE / PARTIAL intră în raport) | – |
| A29 | Izolare / containment pe endpoint | ○* | ○* | ○* | ○ | ◐* (Endpoint Investigator) | ○ | ○ | ●* (carantină) | ○ | ○* | ●* | ○ | are, cu confirmare explicită; izolarea automată e oprită implicit | – |
| A30 | Raport pe audiențe / caz portabil pentru non-tehnici | ● (portable case) | ●* | ●* | ● (portable case review) | ● (șabloane, rapoarte pe audiențe) | ◐* | ● | ◐* | ○ | ● | ●* | ○ | are (rapoarte pe audiență) [DE VERIFICAT] | S69, S1, S41, S39, S33, S57 |
| A31 | Revizuire colaborativă în browser | ● (Magnet REVIEW) | ◐ (BelkaGPT Hub) | ◐ (FTK Central) [NESIGUR] | ? | ○* | ○ | ◐* (multi-user) | ● (GUI web) | ○ | ◐* | ● | ○ | lipsește; contrar modelului offline pe stație izolată | S43, S5, S37b |
| A32 | Extindere prin scripturi / parsere proprii | ● (parsere comunitare) | ◐* | ◐* | ? | ● (EnScript) | ●* (X-Tensions) | ●* (module Python) | ● (VQL) | ● (Targets/Modules) | ○* | ●* | ● (config XML) | parțial: folderul `Rules/` al cazului (IOC, YARA, Sigma) | S2, S39, S79, S14, S19 |
| A33 | Investigație domeniu / mail | ● (email) [NESIGUR] | ●* | ●* | ● (mail de pe MacBook) | ●* | ●* | ◐* | ○ | ○ | ○* | ? | ○ | are (investigație domeniu/mail) [DE VERIFICAT] | S41b |

### 1B. Control și conformitate pe stație

| # | Funcție | NWX | MEA | MED | LEP | NES | CIS | MSPA | MDVM | IVN | EPP | WAZ | GRL | ELS | LogAnalyzer: are / parțial / lipsește — verifică | Surse |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| B1 | Audit conturi locale (creare, ștergere, grupuri) | ◐ (logon cu conturi locale nu e raportat) | ● (stații) | ○ | ●* | ◐ (verificare de configurație) | ◐ | ○ | ◐ | ○ | ○ | ●* | ◐* | ●* | are | S21, S75 |
| B2 | Logon / logoff pe stație | ◐ (logoff de pe stații nu e raportat) | ● | ○ | ●* | ○ | ○ | ○ | ○ | ○ | ○ | ●* | ◐* | ●* | are [DE VERIFICAT] | S21, S75 |
| B3 | Funcționează pe stație standalone, fără AD și fără server | ○* | ○* | ○ (agent + server; shadowing doar on-prem) | ○* | ◐ (scanner instalat; plugin-uri care cer internet) | ● (rulează local) | ● | ○ (cloud) | ◐* | ◐ (OTP fără conexiune la server) | ○ (manager) | ○ | ○ | are (offline-first, două ediții) | S22, S23, S26, S28, S31, S56 |
| B4 | Inventar politici locale (audit, securitate, registru) | ◐* | ◐* | ○ | ◐* | ● | ● | ● | ● | ○ | ○ | ● (SCA) [NESIGUR] | ○ | ◐* | are | S25, S27, S56, S76 |
| B5 | Comparare cu baseline (CIS / STIG / Microsoft) | ○* | ○* | ○ | ○* | ● | ● | ● | ● (CIS, STIG) | ○ | ○ | ●* | ○ | ◐* | parțial: comparare GPO; baseline CIS/STIG importat ca fișier [DE VERIFICAT] | S24, S25, S27, S56 |
| B6 | Import GPO / aplicare pe stație | ○ | ○ | ○ | ○ | ○ | ◐ (Build Kits GPO, pentru membri) | ● (LGPO) | ○ | ○ | ○ | ○ | ○ | ○ | are (import / comparare / aplicare GPO) [DE VERIFICAT] | S24, S26 |
| B7 | Remediere ghidată per setare | ○* | ○* | ○ | ○* | ◐* | ● (pași de remediere în raport HTML) | ◐ (evidențiază conflicte) | ●* | ○ | ○ | ◐* | ○ | ◐* | parțial („următorul pas” pe Politici, commit 9dfc6b6c1) | S24, S25 |
| B8 | Scor de conformitate 0–100 | ○ | ○ | ○ | ○ | ●* | ● | ○ | ●* | ○ | ○ | ●* (SCA %) | ○ | ◐* | [DE VERIFICAT] | S24 |
| B9 | Conținut SCAP / XCCDF / OVAL | ○ | ○ | ○ | ○ | ● | ●* | ○ (nu suportă SCAP) | ○* | ○ | ○ | ◐* | ○ | ○ | lipsește | S27, S25b, S71 |
| B10 | Suport pentru Windows cu localizare non-engleză | ?| ?| ?| ?| ? | ? | ●* | ○ (nesuportat pe locale non-engleză; cere PowerShell CLM oprit) | ? | ? | ? | ? | ? | are (UI română) [DE VERIFICAT pe Windows în română] | S56 |
| B11 | Inventar USB / medii live + istoric pe stație și pe dispozitiv | ○ [NESIGUR] | ● (USB, HDD, CD, micro-SD, WPD) | ● (rapoarte) | ◐* | ○ | ◐ (setări) | ○ | ◐* | ● | ● | ◐ (command monitoring) | ○ | ◐* | are (pagina de medii, inventar complet, istoric, alerte) | S75, S81, S66, S76 |
| B12 | Blocare USB / doar-citire / listă albă | ○ | ○ | ●* | ○ | ○ | ○ | ◐ (prin GPO) | ◐* | ● (interzice scriere) | ● | ○ | ○ | ○ | lipsește prin design (read-only, fără driver kernel) | S66 |
| B13 | Acces temporar la dispozitiv (OTP / perioadă limitată) offline | ○ | ○ | ◐* | ○ | ○ | ○ | ○ | ○ | ● (perioadă limitată) | ● (30 min – 30 zile; OTP universal 1 oră) | ○ | ○ | ○ | lipsește (doar ca idee: „aprobat până la data X” în registrul de medii) | S31, S66 |
| B14 | File shadowing / file tracing | ○ | ○ | ● (copie pe share) | ○ | ○ | ○ | ○ | ○ | ◐* | ● | ○ | ○ | ○ | lipsește prin design (copierea informației clasificate e inacceptabilă); alternativă: auditul 4663 | S22 |
| B15 | Alerte live | ●* | ●* | ●* | ●* | ○ | ○ | ○ | ●* | ●* | ●* | ● | ● | ● | are (alerte USB live) | S53 |
| B16 | Registru administrativ de utilizatori și de medii | ○ | ○ | ◐* (listă dispozitive de încredere) | ○ | ○ | ○ | ○ | ○ | ◐* | ◐* (trusted devices) | ○ | ○ | ○ | are (umplut de pe stație, commit bc97f3535) | – |
| B17 | Profil de procedură (cerințe naționale, ex. HG 585) | ○ | ○ | ○ | ○ | ◐ (fișiere de audit custom) | ◐ (profiluri benchmark) | ○ | ○ | ○ | ○ | ◐ (politici SCA custom) | ○ | ○ | are (`HG585_ACCREDITATION_REQUIREMENTS.md`) | S27, S76 |
| B18 | Rapoarte PDF pentru auditori | ●* (abonamente la rapoarte) | ● (PDF nativ) [NESIGUR] | ●* | ●* | ●* | ◐ (HTML, CSV, XML, JSON, Excel) | ◐ (Excel) | ◐* | ●* | ●* | ◐* | ◐* | ●* | are | S20, S50, S24, S25 |
| B19 | Rapoarte predefinite pe standarde (PCI, HIPAA, GDPR…) | ●* | ● | ○* | ● | ● | ● (CIS Controls IG1) | ○ | ◐* | ○* | ●* | ● | ◐* | ●* | parțial: profilul de procedură național, fără standarde internaționale [DE VERIFICAT] | S49, S32, S27, S24 |
| B20 | Control aplicații (listă albă) | ○ | ○ | ○ | ○ | ○ | ○ | ◐ (prin GPO / AppLocker)* | ○ | ● | ○ | ○ | ○ | ○ | lipsește (doar citire / raport) | S67 |
| B21 | Vulnerabilități CVE cu feed offline | ○ | ◐* | ○ | ○ | ● (actualizare offline de plugin-uri) | ○ | ○ | ○ (cloud) | ◐* | ○ | ● (snapshot CTI offline) | ○ | ◐* | lipsește | S28, S30 |
| B22 | Reguli Sigma importate offline | ○ | ◐* | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ◐* | ◐ (doar cu licența Graylog Security; upload local) | ◐ (regulile incluse nu se mai actualizează; registru propriu sau export/import) | parțial (folderul `Rules/`; regulile peste subset sunt raportate ca neîncărcate) | S51, S52 |
| B23 | Integritatea fișierelor (FIM) | ●* | ●* | ○ | ●* | ◐* | ○ | ○ | ○ | ○ | ○ | ● | ○ | ●* | [DE VERIFICAT] | S76 |

### 1C. Transversal: offline, AI, licențiere, utilizare

| # | Funcție | AXM | BLK | FTK | CEL | ENC | XWF | AUT | VR | KAPE | CT | BIN | ORC | NWX | MEA/MED | CIS | MSPA | MDVM | EPP | WAZ | GRL/ELS | MSC | LogAnalyzer: are / parțial / lipsește — verifică | Surse |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| C1 | Funcționare complet offline / air-gapped | ● | ● | ●* | ●* | ●* | ● | ● | ● (colector offline) | ● | ●* | ● (colectare offline) | ● | ◐* | ◐ | ● | ● | ○ | ◐ | ◐ (feed offline) | ◐ | ○ | are (ediția AirGapped) | S8, S4, S36, S34, S12, S18, S19, S23, S26, S56, S30, S52, S44 |
| C2 | Actualizare de conținut offline documentată (reguli, feed, baseline) | ? | ? | ? | ? | ? | ? | ○ | ◐* | ◐ (sincronizare din GitHub) [NESIGUR] | ? | ? | ○ | ? | ? | ◐* | ● (descărcare manuală) | ○ | ○ | ● | ● (ELS) / ◐ (GRL) | ○ | lipsește ca procedură semnată [DE VERIFICAT] | S28, S30, S52, S51 |
| C3 | AI generativ integrat | ● (Magnet Copilot) | ● (BelkaGPT) | ● (Exterro Intelligence) | ● (clasificare media) | ? | ○ | ○ | ○ | ○ | ● (MCP, scriere înapoi) | ? | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ● | parțial: doar ediția neclasificată (`EDITIONS.md`) | S7, S4, S74, S41, S10, S44 |
| C4 | AI rulat local / offline | ● (din AXIOM 8.6, fără GPU) | ● (și doar pe CPU) | ? | ? | – | – | – | – | – | ◐ (LLM local posibil prin client MCP) | ? | – | – | – | – | – | – | – | – | – | ○ (cloud) | are (Ollama pe loopback numeric, fără proxy) | S8, S4, S10, S44 |
| C5 | Răspunsuri AI cu citări către artefacte | ● | ● (3 artefacte relevante) | ? | ? | – | – | – | – | – | ● (notele AI încep cu „[AI]”) | ? | – | – | – | – | – | – | – | – | – | ◐ (rezumat de verificat) | are (1–4 citări, validare deterministă, `AI_FORENSIC_REASONING.md`) | S7, S4, S10, S44 |
| C6 | Separare strictă AI vs constatări deterministe | ? | ? | ? | ? | – | – | – | – | – | ● (scorare deterministă; AI doar „AI-Suggested Finding”) | ? | – | – | – | – | – | – | – | – | – | ◐ | are (afirmație AI = UNPROVEN, nu modifică constatările) | S11, S10 |
| C7 | Interfață în limba română | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | are | – |
| C8 | Utilizabil de non-IT / non-specialiști | ◐ (cere instruire) | ◐ (copleșitor pentru începători) | ○* | ◐* | ○* | ○ (dens, cere bază forensică) | ● (intuitiv) | ◐ (VQL intimidează) | ○* | ● | ●* | ○ | ◐ (nu pentru utilizatori ocazionali) | ◐ (curbă de învățare) | ◐* | ○* | ◐ | ◐* | ○ (reglaj de săptămâni) | ○* | ◐ | are ca obiectiv (controlori non-IT); validare: `WP18_USABILITY_SCRIPT.md` [DE VERIFICAT] | S3, S59, S35, S33, S79, S57, S58, S20, S49, S55 |
| C9 | Model de licență | per licență, scump (~3.800 → ~8.000 USD după un utilizator) | preț la cerere; BelkaGPT inclus | ? | ? | ? | perpetuu ~3.859 USD sau anual; dongle | gratuit | gratuit (open source)* | gratuit pentru uz intern* | comercial* | comercial | gratuit (open source) | plătești pentru toți utilizatorii din AD/M365 | „puțin scump” | membru SecureSuite* | gratuit | add-on MDE P2 | comercial | gratuit | Sigma doar cu licență (GRL) | per SCU* | două ediții, fișier de licență local [DE VERIFICAT] | S46, S47, S4, S35, S36, S34, S19, S20, S49, S56, S51 |
| C10 | Cerințe hardware mari pe cazuri mari | ◐ | ● (RAM, CPU) | ● (spațiu mare) | ? | ? | ○ (rapid) | ● (lent pe seturi mari) | ○* | ○ | ? | ? | ○ | ? | ● (Log360: consum mare) | ○ | ○ | – | ? | ◐ | ◐* | – | [DE VERIFICAT] | S2, S59, S38, S48, S33, S34, S65 |
| C11 | Stabilitate raportată pe cazuri mari | ◐ (repornire client pe cazuri mari) | ? | ◐ (PostgreSQL, FTK 4.0, veche) | ? | ? | ? | ◐ | ? | ? | ? | ? | ? | ? | ◐ (java.exe 100% CPU) | ? | ? | ? | ? | ? | ? | ? | [DE VERIFICAT] | S2, S37, S33, S50 |
| C12 | Două ediții separate la nivel de cod (clasificat / neclasificat) | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | ○* | are (`EDITIONS.md`, `LogAnalyzer.Edition.Tests`) | – |

---

## 2. Ce laudă utilizatorii și ce reclamă recurent

### 2.1 Laude recurente

| Temă | Produs(e) | Ce spun utilizatorii | Sursă |
|---|---|---|---|
| Acoperire mare de artefacte, parsere comunitare | AXIOM Cyber | Varietatea parserelor implicite și posibilitatea de a adăuga parsere proprii sau comunitare | [S2](https://aws.amazon.com/marketplace/reviews/reviews-list/prodview-vjhzht6hnm5ki?page=7) |
| Caz portabil pentru cei care nu sunt examinatori | AXIOM | Un fișier de caz portabil, organizat astfel încât oricine citește raportul să-l poată urmări | [S1](https://www.capterra.com/p/228075/Magnet-AXIOM-Cyber/) |
| Filtrare recursivă pe volume mari | AXIOM | Filtrele recursive restrâng seturi mari la probele-cheie | [S1](https://www.capterra.com/p/228075/Magnet-AXIOM-Cyber/) |
| Cronologie clară, indexare rapidă, BelkaGPT | Belkasoft X | Rezumat G2: analiză pe artefacte, indexare rapidă, cronologie clară, BelkaGPT util la raport [NESIGUR: rezumat generat de AI pe G2] | [S59](https://www.g2.com/compare/belkasoft-vs-magnet-forensics-magnet-forensics) |
| Accesibil celor fără pregătire forensică | Cyber Triage | Investigații fără expertiză tehnică extinsă; instruire ușor de urmat | [S57](https://g2.com/products/cyber-triage/reviews), [S58](https://www.trustradius.com/products/cyber-triage) |
| Raportare de conformitate și abonamente la rapoarte | Netwrix Auditor | „Raportul și abonamentul” ajută conformitatea; informații despre schimbări în AD, servere de fișiere, SQL, GPO prezentate clar | [S20](https://www.gartner.com/reviews/product/netwrix-auditor) |
| Șabloane de conformitate, PDF nativ | ADAudit Plus | Șabloanele de raport economisesc timp; auditorii apreciază granularitatea și PDF-ul nativ [NESIGUR: citat G2 preluat de agregator] | [S49](https://www.capterra.co.uk/reviews/210774/adaudit-plus), [S50](https://www.saasworthy.com/product/manageengine-adaudit-plus/reviews) |
| Viteză și preț | X-Ways Forensics | „Extrem de rapid și puternic”, sub o treime din prețul concurenților, inclusiv reînnoirile | [S48](https://www.forensicfocus.com/forums/general/the-high-cost-of-computer-forensics-software/), [S46](https://reddit.sentinel-team.org/posts/1u0gp3m/snapshots/2026-06-10T22%3A59%3A20.27224Z) |
| Gratuit, intuitiv, căutare și raport | Autopsy | Interfață intuitivă, căutare după cuvinte-cheie, generare de raport, fără cost pe stație | [S33](https://www.g2.com/products/autopsy/reviews), [S34](https://us.fitgap.com/products/autopsy) |
| Colectare automată, fără operator expert | Velociraptor (colector offline), Magnet Response | Operatorul doar rulează colectorul; Magnet Response are doi pași ghidați, pentru personal netehnic | [S12](https://docs.velociraptor.app/docs/deployment/offline_collections/), [S42](https://www.magnetforensics.com/blog/magnet-response-new-free-tool-for-ir-investigations/) |
| Triaj rapid al unei infrastructuri după breșă | Binalyze AIR | Triaj rapid al infrastructurii unui client pentru compromitere | [S68](https://www.gartner.com/reviews/product/binalyze-air/alternatives) |
| Rulează în fundal fără probleme | Lepide | „Merge în fundal fără să facă probleme”; a recuperat permisiuni AD | [S32](https://www.gartner.com/reviews/product/lepide-data-security-platform) |

### 2.2 Reclamații recurente

| Temă | Produs(e) | Ce reclamă utilizatorii | Sursă | Lecție pentru LogAnalyzer |
|---|---|---|---|---|
| **Licențe scumpe, captivitate față de furnizor** | AXIOM | Licența a crescut de la ~3.800 la ~8.000 USD; upgrade forțat la un nivel mai scump, taxa de suport aproape dublată [NESIGUR: fir Reddit arhivat]; oferte de ~5k (AXIOM) și ~8k (AXIOM Cyber) | [S46](https://reddit.sentinel-team.org/posts/1u0gp3m/snapshots/2026-06-10T22%3A59%3A20.27224Z), [S47](https://www.forensicfocus.com/forums/general/cost-of-magnetic-axiom-licence/) | Licență previzibilă, fără funcții de bază ascunse în niveluri superioare |
| **Licență pe toți utilizatorii tenantului** | Netwrix Auditor | Nu se poate limita la utilizatori sau grupuri, se plătește pentru toți utilizatorii din AD/M365 | [S20](https://www.gartner.com/reviews/product/netwrix-auditor) | Licență pe stație sau pe instalare, nu pe numărul de conturi |
| **Instalare și actualizare dificile** | ADAudit Plus | Instalare MSI, actualizare prin fișier PPM și scripturi batch care intră în conflict cu antimalware-ul; erorile de actualizare nu apar clar în jurnale | [S50](https://www.saasworthy.com/product/manageengine-adaudit-plus/reviews) | Actualizare offline într-un singur pachet semnat, cu jurnal lizibil |
| **Curbă de învățare, nu pentru utilizatori ocazionali** | Netwrix, ADAudit Plus, Lepide, X-Ways, AXIOM | „Nu e pentru utilizatori ocazionali sau netehnici”; „curbă de învățare mai abruptă”; „cere multe cunoștințe practice”; „dens în funcții”; „cere instruire solidă” | [S20](https://www.gartner.com/reviews/product/netwrix-auditor), [S49](https://www.capterra.co.uk/reviews/210774/adaudit-plus), [S32](https://www.gartner.com/reviews/product/lepide-data-security-platform), [S35](https://us.fitgap.com/products/x-ways-forensics), [S3](https://www.softwareadvice.com/digital-forensics/magnet-axiom-profile) | Confirmă direcția: cuvinte simple, „ce înseamnă”, un singur pas următor |
| **Personalizare limitată a rapoartelor** | ADAudit Plus | Formatarea se face exportând datele și modificându-le în afara produsului | [S49](https://www.capterra.co.uk/reviews/210774/adaudit-plus) | Rapoarte pe audiențe direct din aplicație, fără export manual |
| **Zgomot și alarme false** | Wazuh, Log360 | Configurația implicită face zgomot și oboseală de alertă; detectorul de vulnerabilități inundă cu sute de alerte; reglajul durează 1–2 săptămâni; reglajul corelărilor cere efort | [S53](https://www.g2.com/products/wazuh-the-open-source-security-platform/reviews?page=3), [S54](https://groups.google.com/g/wazuh/c/BiOLiJttJzA), [S55](https://mustafa.net/2026/09/12/wazuh-self-hosted-review-6-months-running-it-in-production/), [S65](https://www.capterra.fr/reviews/175185/log360) | Fiecare regulă validată pe corpus real cu 0 alarme false pe fișiere benigne (practica din `DETECTION.md`); suprimare îngustă, nu dezactivare |
| **Scorare automată fără context** | Cyber Triage | Evaluarea automată „nu ia complet în considerare contextul” | [S57](https://g2.com/products/cyber-triage/reviews) | Cardul de constatare arată contextul benign posibil (ex. driverul GPU-Z din Temp, `DETECTION.md`) |
| **Resurse hardware și lentoare pe cazuri mari** | Belkasoft X, Autopsy, FTK, Log360 | RAM și CPU mari pe cazuri mari; Autopsy lent pe seturi mari și imagini de mai mulți TB; FTK „ocupă mult spațiu”; Log360 „consum mare de resurse” | [S59](https://www.g2.com/compare/belkasoft-vs-magnet-forensics-magnet-forensics), [S33](https://www.g2.com/products/autopsy/reviews), [S34](https://us.fitgap.com/products/autopsy), [S38](https://origin.peerspot.com/products/accessdata-ftk-eol-pros-and-cons), [S65](https://www.capterra.fr/reviews/175185/log360) | Stațiile clasificate sunt de obicei modeste: de măsurat consumul pe o stație de referință |
| **Instabilitate pe cazuri mari** | AXIOM, Lepide, ADAudit Plus | Bug-uri care forțează repornirea clientului; „se blochează frecvent” (recenzie veche, 2019); java.exe la 100% CPU | [S2](https://aws.amazon.com/marketplace/reviews/reviews-list/prodview-vjhzht6hnm5ki?page=7), [S32](https://www.gartner.com/reviews/product/lepide-data-security-platform), [S50](https://www.saasworthy.com/product/manageengine-adaudit-plus/reviews) | Test de anduranță pe jurnale mari înainte de release (`RELEASE_GATE.md`) |
| **Lacune de audit nedeclarate** | Netwrix Auditor | Logon-ul conturilor locale nu e raportat, logoff-ul de pe stații nu e raportat; jurnalul Security trebuie setat la 4 GB | [S21](https://docs.netwrix.com/docs/auditor/10_6/configuration/logonactivity/overview) | Pe stațiile izolate toate conturile sunt locale: e exact golul pe care LogAnalyzer îl acoperă; păstrăm raportul explicit de goluri (`AuditCoverage`) |
| **Limitări de localizare și mediu** | Microsoft Defender Vulnerability Management | Baselines assessment nu e suportat pe locale Windows non-engleză, cere PowerShell Constrained Language Mode oprit, doar GPO, nu Intune | [S56](https://learn.microsoft.com/en-us/defender-vulnerability-management/tvm-security-baselines) | Diferențiator direct pentru stații Windows în română |
| **Funcții de bază doar în licența plătită** | Graylog | Regulile Sigma țin de Graylog Security, licență separată | [S51](https://go2docs.graylog.org/current/what_more_can_graylog_do_for_me/sigma_rules.htm) | Detecția Sigma rămâne în ambele ediții |
| **Reguli care îmbătrânesc offline** | Elastic Security | Regulile incluse în Kibana pentru air-gapped nu mai primesc actualizări fără registru propriu sau export/import [NESIGUR: documentație preview] | [S52](https://docs-v3-preview.elastic.dev/elastic/docs-content/pull/6377/solutions/security/detect-and-alert/prebuilt-rules-airgapped.md) | Pachet de reguli versionat, cu dată și hash, afișat în raport |
| **Imagistică și date criptate** | AXIOM | Imagistică slabă față de Cellebrite (mobil) și FTK (computere); dificultăți cu date criptate | [S1](https://www.capterra.com/p/228075/Magnet-AXIOM-Cyber/), [S3](https://www.softwareadvice.com/digital-forensics/magnet-axiom-profile) | Nu concurăm la imagistică; integrăm ieșirile instrumentelor dedicate |
| **Dongle pierdut = licență pierdută** | X-Ways | Fără asigurare, un dongle pierdut nu se înlocuiește | [S36](https://www.x-ways.net/dongle.html) | Licența prin fișier local (fără hardware) e potrivită pentru stațiile izolate |

---

## 3. AI offline în DFIR: abordări și protecții împotriva halucinațiilor

### 3.1 Ce fac produsele (2024–2026)

| Produs | Unde rulează | Ce face | Protecții declarate | Sursă |
|---|---|---|---|---|
| Magnet Copilot (AXIOM) | Versiune offline din AXIOM 8.6 (oct. 2024): procesare locală, fără GPU; varianta cloud cere confirmare înainte de orice încărcare | Întrebări despre conversații și căutări web, evidențiază artefactele relevante; probabilitatea ca o imagine să fie generată (Stable Diffusion, Midjourney, DALL-E 3) | Răspunsuri cu citări către datele cazului, pentru verificare | [S7](https://www.magnetforensics.com/blog/accelerating-investigations-with-ai-using-magnet-copilot), [S8](https://www.magnetforensics.com/blog/magnet-axiom-8-6-magnet-copilot-offline-ai-capabilities-and-more/) |
| BelkaGPT (Belkasoft X) | Complet offline, merge și doar pe CPU; BelkaGPT Hub pentru infrastructură comună; inclus fără cost | Bază de cunoștințe a cazului din artefactele extrase; întrebări în limbaj natural; clasificare de imagini definită în cuvinte (v2.9), transcriere audio din video | Referințe la cele mai relevante 3 artefacte, pentru verificare | [S4](https://belkasoft.com/belkagpt), [S5](https://www.forensicfocus.com/news/belkasoft-advances-ai-assisted-dfir-with-major-update-to-belkasoft-x-and-belkagpt/), [S6](https://belkasoft.com/whats-new-in-belkax-2-9), [S83](https://www.forensicfocus.com/news/belkasoft-x-streamlines-investigations-with-ai-powered-picture-analysis/) |
| Cyber Triage 3.17 / 3.18 | Client AI extern prin MCP (ex. Claude Desktop); acceptă și LLM-uri locale | 3.17: doar citire. 3.18: AI poate scrie note de îmbogățire și scoruri | Scorarea rămâne deterministă („nu folosim GenAI pentru scorare”, 3.17); AI scrie doar două tipuri: note care încep cu „[AI]” și scoruri de tip „AI-Suggested Finding”, afișate separat | [S11](https://www.cybertriage.com/blog/cyber-triage-3-17-use-cyber-triage-with-ai/), [S10](https://www.cybertriage.com/blog/cyber-triage-3-18-new-ai-cloud-automation-capabilities/) |
| Exterro FTK 8.2 SP2 | ? [NESIGUR] | „Exterro Intelligence”: căutare în limbaj natural după obiecte, acțiuni, fraze în media și text | Nedocumentat în sursele găsite | [S74](https://www.exterro.com/resources/product-briefs/whats-new-in-ftk-8-2-sp2) |
| Microsoft Security Copilot | Cloud, licență separată | Rezumat de incident (până la 100 de alerte), răspunsuri ghidate, promptbook-uri | Microsoft cere revizuirea rezumatului; în promptbook-uri fiecare pas se construiește pe cel anterior, deci erorile se propagă | [S44](https://learn.microsoft.com/en-us/defender-xdr/security-copilot-m365d-incident-summary), [S45](https://learn.microsoft.com/security-copilot/using-promptbooks) |
| Timesketch + Sec-Gemini | Cloud (Google) | Agent care analizează autonom cronologii; panou AI în Timesketch | Evaluat pe 100 de sisteme compromise reale (raportat de autori) [NESIGUR: rezumat de prezentare] | [S72](https://elie.net/talk/autonomous-timeline-analysis-and-threat-hunting-an-ai-agent-for-timesketch) |

### 3.2 Ce spune cercetarea

- **ForensicLLM (DFRWS EU 2025)**: LLaMA-3.1-8B cuantizat pe 4 biți, ajustat pe articole DFIR și artefacte; depășește modelul de bază și o variantă RAG; atribuie sursa în 86,6% din cazuri. Motivația declarată: dependența de API-uri cloud și riscul de halucinație în medii cu resurse limitate. [S60](https://dfrws.org/presentation/forensicllm-a-local-large-language-model-for-digital-forensics/)
- **Metodologie standard pentru analiza de cronologie cu LLM** (Studiawan, Breitinger, Scanlon, 2025), după modelul NIST CFTT. Folosește BLEU/ROUGE, care măsoară suprapunerea de text, nu corectitudinea faptelor. [S61](https://arxiv.org/html/2505.03100v1)
- **DFIR-Metric (2025)**: 700 de întrebări, 150 de sarcini CTF, 500 de cazuri NIST; 14 LLM-uri evaluate pe acuratețe și **consistență între rulări**. [NESIGUR: rezumat secundar] [S62](https://chatpaper.com/paper/141095)
- **Survey LLM în forensică** (arXiv 2504.02963): halucinație, interpretabilitate, părtinire; cere transparență și standardizare. [S63b](https://www.emergentmind.com/papers/2504.02963)
- **ElcomSoft (oct. 2025)**: modelele pot halucina sau pot schimba răspunsul la aceeași întrebare; multe instrumente includ ML fără să-și declare modelele. AI e util pentru sortare și etichetare în volum, „folosit orbește e o răspundere”. [S63](https://blog.elcomsoft.com/2025/10/ai-in-digital-forensics-a-tool-not-an-oracle/)
- **Admisibilitate**: instanțele și practicienii sunt sceptici față de extragerea de probe prin AI de tip „cutie închisă”; explicabilitatea condiționează admisibilitatea. [S64](https://dfrws.org/presentation/digital-forensics-ai-on-explainability-and-interpretability-of-evidence-mining-techniques/)
- **Florida Tech**: proiect care caută scenariile în care AI generativ trage concluzii nesusținute din jurnale și analize de memorie. [S9](https://news.fit.edu/?p=84855)

### 3.3 Catalogul protecțiilor (ce folosește cine)

| Protecție | Magnet Copilot | BelkaGPT | Cyber Triage | Security Copilot | LogAnalyzer (`AI_FORENSIC_REASONING.md`) |
|---|---|---|---|---|---|
| Model local, fără ieșire în rețea | ● (offline) | ● | ◐ (depinde de client) | ○ | ● (doar loopback numeric; `localhost` refuzat; proxy și redirecționări oprite) |
| Modelul nu vede probele brute | ? | ○ (bază de cunoștințe din artefacte) | ○ (citește baza incidentului) | ○ | ● (doar un catalog numerotat de constatări, evenimente, goluri) |
| Citare obligatorie a sursei | ● | ● (3 artefacte) | ? | ◐ | ● (1–4 citări, cu EvidenceId, locator, SHA-256) |
| Validare deterministă a răspunsului | ? | ? | ? | ? | ● (respinge citări inexistente, entități care nu apar în probele citate, ipoteze formulate ca certitudini) |
| AI nu modifică scorarea | ? | ? | ● | ? | ● (afirmație acceptată = UNPROVEN) |
| Etichetarea vizibilă a textului AI | ? | ? | ● („[AI]”, „AI-Suggested Finding”) | ◐ | ◐ (secțiune separată; de verificat dacă eticheta apare și în raportul exportat) [DE VERIFICAT] |
| Rulare reproductibilă | ? | ? | ? | ○ | ● (`temperature: 0`, `seed: 0`, schemă JSON impusă) |
| Verificare de integritate înainte de analiză | ? | ? | ? | ? | ● (analiza pornește doar dacă probele corespund hash-ului) |
| Exclus din ediția clasificată | – | – | – | – | ● (`EDITIONS.md`) |

**Concluzie**: protecțiile LogAnalyzer sunt mai stricte decât ce declară public concurenții (validare deterministă, catalog în loc de probe brute, reproductibilitate). Concurenții sunt mai departe la **utilitate** (întrebări libere în limbaj natural, clasificare de imagini, transcriere). Cyber Triage are un model de etichetare care merită preluat: orice text AI care ajunge în baza cazului are prefix fix și o categorie separată.

---

## 4. Diferențiatori de păstrat

1. **Un singur instrument pentru controlorul non-IT și pentru CSIRT.** Niciun produs analizat nu combină controlul de conformitate al stației (conturi, politici, audit, medii, registre, profil de procedură) cu DFIR (lanțuri de incident, carduri, custodie). Concurenții sunt fie suite forensice pentru examinatori (AXM, BLK, ENC, XWF), fie platforme de audit centralizate care presupun AD și server (NWX, MEA, LEP). Surse: §1, [S20](https://www.gartner.com/reviews/product/netwrix-auditor), [S21](https://docs.netwrix.com/docs/auditor/10_6/configuration/logonactivity/overview).
2. **Stație standalone, conturi locale, fără server.** Netwrix nu raportează logon-ul conturilor locale [S21]; Device Control Plus face shadowing doar on-prem cu server [S22]; MDVM cere cloud [S56]. Instrumentele care rulează local (CIS-CAT, Policy Analyzer) nu fac DFIR și nu țin registre.
3. **Interfață și explicații în română, pe Windows în română.** MDVM nu suportă locale non-engleză [S56]; niciun concurent nu are UI în română (C7, [NESIGUR] pentru produsele neverificate).
4. **Read-only, fără driver kernel, fără copii ale informației.** File shadowing (MED [S22], EPP) e incompatibil cu informațiile clasificate. Inventarul fără blocare e o alegere de design, nu o lipsă.
5. **Registre administrative (utilizatori, medii) și profil de procedură național.** Nu apar la niciun concurent (B16, B17).
6. **Goluri de probă raportate explicit** (`AuditCoverage`). Concurenții tac despre ce nu au putut vedea; Netwrix își documentează lacunele doar în manual [S21].
7. **Detecții validate pe corpus, cu 0 alarme false pe fișiere benigne**, ca răspuns la reclamația dominantă despre zgomot (Wazuh, Log360) [S53](https://www.g2.com/products/wazuh-the-open-source-security-platform/reviews?page=3), [S65](https://www.capterra.fr/reviews/175185/log360).
8. **Distincția „prezență” vs „execuție”** (Amcache/ShimCache, `REALITY_AUDIT.md`) și contextul benign pe cardul de constatare, ca răspuns la critica „scorare fără context” [S57].
9. **AI local, cu validare deterministă și exclus din ediția clasificată** (§3.3). Răspunde direct scepticismului despre admisibilitate [S64] și reproductibilitate [S63].
10. **Două ediții separate la nivel de cod**, licență prin fișier local, fără dongle [S36] și fără taxare pe utilizatori [S20].

---

## 5. Funcții lipsă, prioritizate

Efort: **S** = sub o săptămână, **M** = 1–4 săptămâni, **L** = peste o lună. Prioritatea ține cont de regula producție-consumator: întâi cablăm ce există (FACADE), apoi construim nou.

### P1 (impact mare pe misiunea actuală)

| # | Funcție | De ce | Efort | Referință |
|---|---|---|---|---|
| P1.1 | Cablare în producție a parserelor LNK / Jumplists / Shellbags și $MFT (azi FACADE), cu teste pe corpus și fără `ExecutionProven` | Artefacte standard la toți concurenții (A11, A12); fără ele lanțurile de incident au goluri | M (fiecare), L împreună | S41, `UNWIRED_COMPONENTS.md`, `REALITY_AUDIT.md` |
| P1.2 | Sigma: corelare temporală (`timeframe`, `count`) și import în bloc al unui pachet SigmaHQ adus pe mediu aprobat, cu raport „încărcate / neîncărcate și de ce” | Concurenții IR (BIN, VR, Hayabusa) folosesc Sigma complet; Graylog o ține după licență | M | S16, S51, S70, `DETECTION.md` |
| P1.3 | Pachet de actualizare offline semnat (reguli, MITRE, baseline-uri, texte „ce înseamnă”), cu hash verificat, dată și versiune afișate în raport | Modelul Tenable și Wazuh; reclamația ADAudit despre actualizări; regulile Elastic care îmbătrânesc | M | S28, S30, S50, S52 |
| P1.4 | Baseline standard ca fișier (Microsoft SCT, CIS, STIG) comparat cu politica stației, cu scor și pas de remediere pe setare | CIS-CAT, Policy Analyzer, MDVM au asta; controlorul are nevoie de o cifră și de pasul următor | M | S24, S25, S56 |
| P1.5 | Lanț de custodie la fiecare transfer: formular, hash la fiecare export de raport sau colecție, jurnal de operații | Cerință ISO/IEC 27037 și NIST SP 800-86 (auditabilitate); Magnet nu o documentează în portable case | S | S73 [NESIGUR], S69 |
| P1.6 | Eticheta „[AI]” obligatorie în orice export (PDF, JSON) și categorie separată „sugestie AI” | Modelul Cyber Triage; evită confundarea textului AI cu constatări în rapoartele pe audiențe | S | S10 |

### P2 (util, nu blocant)

| # | Funcție | De ce | Efort | Referință |
|---|---|---|---|---|
| P2.1 | Colector de triaj portabil (un exe pentru stații fără LogAnalyzer instalat), ieșire hash-uită, criptată cu cheie publică | Modelul Velociraptor, Magnet Response, DFIR-ORC; potrivit CSIRT-ului care vine la o stație izolată | L | S12, S13, S42, S19 |
| P2.2 | Enumerare și citire Volume Shadow Copies | Cellebrite o prezintă ca avantaj; recuperează istoric șters | M | S41 |
| P2.3 | Export cronologie compatibil Timeline Explorer / Timesketch (CSV, JSONL) | Interoperabilitate cu fluxurile CSIRT existente | S | S70 |
| P2.4 | Import rezultate XCCDF / SCAP (ex. ieșirea DISA SCC) în profilul de conformitate | Nessus și SCC lucrează pe SCAP; Policy Analyzer nu | M | S27, S71 [NESIGUR] |
| P2.5 | Import de capturi RAM făcute cu instrumente dedicate (DumpIt, WinPmem) și analiză minimă (procese, conexiuni) | Toți concurenții IR au RAM; noi nu capturăm (read-only) | M–L | S42, S41 |
| P2.6 | „Aprobat până la data X” în registrul de medii, cu alertă la expirare | Echivalentul fără blocare al OTP (EPP, IVN) | S | S31, S66 |
| P2.7 | Test de consistență AI: aceeași întrebare rulată de N ori, divergențele afișate | Critica ElcomSoft și DFIR-Metric despre răspunsuri instabile | S | S63, S62 |
| P2.8 | Caz portabil doar-citire (HTML static, fără server) pentru șefi și juriști | Laudă recurentă la AXIOM portable case și Magnet REVIEW, fără a renunța la offline | M | S1, S69, S43 |

### P3 (de evaluat, posibil în afara scopului)

| # | Funcție | De ce nu acum | Efort | Referință |
|---|---|---|---|---|
| P3.1 | Dispozitive mobile | În afara scopului stației Windows izolate; domeniu acoperit de AXIOM, Belkasoft, Cellebrite | L | S1, S41 |
| P3.2 | Achiziție cloud (M365, Google Workspace) | Doar pentru ediția neclasificată; parserul M365 e FACADE | L | S3, S16 |
| P3.3 | Clasificare AI de imagini / transcriere audio, local | BelkaGPT și Magnet Copilot o au; cere modele mari, irelevant pentru controlorul de conformitate | L | S6, S7 |
| P3.4 | Revizuire colaborativă în browser | Contrar modelului offline pe stație izolată; P2.8 acoperă nevoia de bază | L | S43 |
| P3.5 | Feed CVE offline (snapshot de tip Wazuh CTI) | Util, dar alt produs (management de vulnerabilități); doar dacă procedura o cere | M | S30 |

---

## 6. Surse

Toate accesate pe 2026-10-11 prin motorul de căutare (rezumate de pagină). Marcajul [NESIGUR] indică sursă secundară, oglindă, preview sau rezumat generat de AI.

| # | Sursă |
|---|---|
| S1 | https://www.capterra.com/p/228075/Magnet-AXIOM-Cyber/ |
| S2 | https://aws.amazon.com/marketplace/reviews/reviews-list/prodview-vjhzht6hnm5ki?page=7 (recenzie G2 republicată) |
| S3 | https://www.softwareadvice.com/digital-forensics/magnet-axiom-profile |
| S4 | https://belkasoft.com/belkagpt |
| S5 | https://www.forensicfocus.com/news/belkasoft-advances-ai-assisted-dfir-with-major-update-to-belkasoft-x-and-belkagpt/ |
| S6 | https://belkasoft.com/whats-new-in-belkax-2-9 |
| S7 | https://www.magnetforensics.com/blog/accelerating-investigations-with-ai-using-magnet-copilot |
| S8 | https://www.magnetforensics.com/blog/magnet-axiom-8-6-magnet-copilot-offline-ai-capabilities-and-more/ |
| S9 | https://news.fit.edu/?p=84855 |
| S10 | https://www.cybertriage.com/blog/cyber-triage-3-18-new-ai-cloud-automation-capabilities/ |
| S11 | https://www.cybertriage.com/blog/cyber-triage-3-17-use-cyber-triage-with-ai/ |
| S12 | https://docs.velociraptor.app/docs/deployment/offline_collections/ |
| S13 | https://www.rapid7.com/blog/post/2022/12/02/velociraptor-version-0-6-7-better-offline-collection-encryption-and-an-improved-ntfs-parser-dig-deeper-than-ever (versiune 2022) |
| S14 | https://kroll.com/en/insights/publications/cyber/kroll-artifact-parser-extractor-kape |
| S15 | https://www.blackhillsinfosec.com/?p=39016 |
| S16 | https://kb.binalyze.com/air/features/drone/what-is-drone |
| S17 | https://kb.binalyze.com/air/features/air-investigation-hub |
| S18 | https://www.forensicfocus.com/articles/offline-collection-with-binalyze-air/ |
| S19 | https://cyber.gouv.fr/en/actualites/dfir-orc-open-source-forensics-tool-dedicated-artefact-collection |
| S20 | https://www.gartner.com/reviews/product/netwrix-auditor |
| S21 | https://docs.netwrix.com/docs/auditor/10_6/configuration/logonactivity/overview |
| S22 | https://www.manageengine.com/device-control/file-shadowing.html |
| S23 | https://ciscat-assessor.docs.cisecurity.org/en/latest/About/ |
| S24 | https://cisecurity.org/tools/cis-cat-pro |
| S25 | https://learn.microsoft.com/en-us/windows/security/operating-system-security/device-management/windows-security-configuration-framework/security-compliance-toolkit-10 |
| S25b | https://redmondmag.com/articles/2017/06/16/microsoft-ending-security-compliance-manager.aspx (2017: SCT nu suportă SCAP) [NESIGUR] |
| S26 | https://www.microsoft.com/en-us/download/details.aspx?id=55319 |
| S27 | https://docs.tenable.com/nessus/10_12/Content/UploadCustomAuditFile.htm |
| S28 | https://docs.tenable.com/security-center/6_9/Content/AirGappedEnvironments.htm |
| S29 | https://www.tenable.com/blog/whats-new-in-nessus-62 (audit offline pe fișiere de configurație, veche) |
| S30 | https://documentation.wazuh.com/current/user-manual/capabilities/vulnerability-detection/offline-update.html |
| S31 | https://docs.netwrix.com/docs/endpointprotector/admin/otpassword |
| S32 | https://www.gartner.com/reviews/product/lepide-data-security-platform |
| S33 | https://www.g2.com/products/autopsy/reviews |
| S34 | https://us.fitgap.com/products/autopsy [NESIGUR] |
| S35 | https://us.fitgap.com/products/x-ways-forensics [NESIGUR] |
| S36 | https://www.x-ways.net/dongle.html |
| S37 | https://www.forensicfocus.com/news/accessdata-ftk-4-0-initial-impressions-2/ (FTK 4.0, veche) |
| S37b | https://www.forensicfocus.com/news/a-letter-from-the-ceo-of-exterro-the-ftk-roadmap-and-our-exciting-path-forward/ (FTK Central, 2021) |
| S38 | https://origin.peerspot.com/products/accessdata-ftk-eol-pros-and-cons |
| S39 | https://static.carahsoft.com/concrete/files/8117/3807/7578/OpenText_EnCase_Forensic_Overview_PDF.pdf |
| S40 | https://www.g2.com/products/opentext-encase-forensic/reviews |
| S41 | https://cellebrite.com/en/inspector/ |
| S41b | https://cellebrite.com/en/collecting-and-analyzing-e-mail-from-a-macbook |
| S42 | https://www.magnetforensics.com/blog/magnet-response-new-free-tool-for-ir-investigations/ |
| S43 | https://www.magnetforensics.com/blog/magnet-review-5-3-review-all-your-evidence-together/ |
| S44 | https://learn.microsoft.com/en-us/defender-xdr/security-copilot-m365d-incident-summary |
| S45 | https://learn.microsoft.com/security-copilot/using-promptbooks |
| S46 | https://reddit.sentinel-team.org/posts/1u0gp3m/snapshots/2026-06-10T22%3A59%3A20.27224Z (arhivă r/computerforensics) [NESIGUR] |
| S47 | https://www.forensicfocus.com/forums/general/cost-of-magnetic-axiom-licence/ |
| S48 | https://www.forensicfocus.com/forums/general/the-high-cost-of-computer-forensics-software/ |
| S49 | https://www.capterra.co.uk/reviews/210774/adaudit-plus |
| S50 | https://www.saasworthy.com/product/manageengine-adaudit-plus/reviews (citate G2) [NESIGUR] |
| S51 | https://go2docs.graylog.org/current/what_more_can_graylog_do_for_me/sigma_rules.htm |
| S52 | https://docs-v3-preview.elastic.dev/elastic/docs-content/pull/6377/solutions/security/detect-and-alert/prebuilt-rules-airgapped.md [NESIGUR: preview] |
| S53 | https://www.g2.com/products/wazuh-the-open-source-security-platform/reviews?page=3 |
| S54 | https://groups.google.com/g/wazuh/c/BiOLiJttJzA |
| S55 | https://mustafa.net/2026/09/12/wazuh-self-hosted-review-6-months-running-it-in-production/ |
| S56 | https://learn.microsoft.com/en-us/defender-vulnerability-management/tvm-security-baselines |
| S57 | https://g2.com/products/cyber-triage/reviews |
| S58 | https://www.trustradius.com/products/cyber-triage |
| S59 | https://www.g2.com/compare/belkasoft-vs-magnet-forensics-magnet-forensics (rezumat AI G2) [NESIGUR] |
| S60 | https://dfrws.org/presentation/forensicllm-a-local-large-language-model-for-digital-forensics/ |
| S61 | https://arxiv.org/html/2505.03100v1 |
| S62 | https://chatpaper.com/paper/141095 (DFIR-Metric) [NESIGUR] |
| S63 | https://blog.elcomsoft.com/2025/10/ai-in-digital-forensics-a-tool-not-an-oracle/ |
| S63b | https://www.emergentmind.com/papers/2504.02963 |
| S64 | https://dfrws.org/presentation/digital-forensics-ai-on-explainability-and-interpretability-of-evidence-mining-techniques/ |
| S65 | https://www.capterra.fr/reviews/175185/log360 |
| S66 | https://help.ivanti.com/ht/help/en_US/IDAC/latest/device-control/device-control-overview.htm |
| S67 | https://help.ivanti.com/ht/help/en_US/IDAC/latest/app-control/application-control.htm |
| S68 | https://www.gartner.com/reviews/product/binalyze-air/alternatives |
| S69 | https://www.magnetforensics.com/docs/axiom/html/Content/en-us/sharing-evidence/portable-case/create-portable-case.htm |
| S70 | https://www.skills.sh/mukul975/anthropic-cybersecurity-skills/generating-forensic-timelines-with-hayabusa (Hayabusa, oglindă) [NESIGUR] |
| S71 | https://www.trackr.live/?p=1048 (DISA SCC) [NESIGUR] |
| S72 | https://elie.net/talk/autonomous-timeline-analysis-and-threat-hunting-an-ai-agent-for-timesketch |
| S73 | https://truescreen.io/?p=56188 (ISO/IEC 27037, NIST SP 800-86, SWGDE comparate) [NESIGUR] |
| S74 | https://www.exterro.com/resources/product-briefs/whats-new-in-ftk-8-2-sp2 |
| S75 | https://www.manageengine.com/products/active-directory-audit/sem/lp/windows-active-directory-workstations-auditing.html |
| S75b | https://www.helpnetsecurity.com/2021/10/14/exterro-digital-forensic-investigation/ (FTK: CAID, Project VIC, 2021) |
| S76 | https://documentation.wazuh.com/4.8/user-manual/agent/index.html (SCA, FIM, „Detect USB Storage”, doar navigație) [NESIGUR] |
| S77 | https://docs.sekoia.io/integration/categories/endpoint/harfanglab |
| S78 | https://harfanglab.io/connectors/ |
| S79 | https://docs.velociraptor.app/presentations/2022_velocon/ |
| S80 | https://blog.ciaops.com/2026/06/06/defender-vulnerability-management/ |
| S81 | https://www.manageengine.com/device-control/help/device-control/dc-reports.html |
| S82 | https://cellebrite.com/en/analyzing-program-execution-windows-artifacts |
| S83 | https://www.forensicfocus.com/news/belkasoft-x-streamlines-investigations-with-ai-powered-picture-analysis/ |
| S84 | https://www.binalyze.com/air/compromise-assessment/ |

### Necunoscute rămase

- **Sekoia / HarfangLab și DFIR-ORC**: nu am găsit nicio sursă care să lege DFIR-ORC de Sekoia sau HarfangLab. Sekoia primește evenimentele EDR HarfangLab prin syslog [S77]; HarfangLab are colectare forensică prin responders TheHive [S78]. Coloana ORC descrie doar DFIR-ORC (ANSSI) [S19]. **[NESIGUR]**
- **Netwrix Auditor și USB**: nu am găsit documentație Netwrix Auditor pentru medii amovibile (B11 = ○ [NESIGUR]). Endpoint Protector, acum la Netwrix, acoperă zona [S31].
- **G2 direct**: paginile G2 pentru AXIOM Cyber, Binalyze AIR și EnCase nu au întors textul recenziilor; cifrele vin din agregatori. Binalyze: 4,8/5 din 9 recenzii pe G2, 4,6/5 din 14 pe Gartner, fără reclamații vizibile [S68].
- **Reddit**: un singur fir găsit (arhivă) [S46]; restul opiniilor vin din forumurile Forensic Focus.
- **Coloana LogAnalyzer**: tot ce e marcat [DE VERIFICAT] trebuie confirmat prin rulare pe o stație de test (`FORENSIC_TEST_LAB.md`, `WP18_USABILITY_SCRIPT.md`) înainte de a fi folosit în materiale de prezentare.
