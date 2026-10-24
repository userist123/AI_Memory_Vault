# Cercetare 2026-10-11: ce îi trebuie unui CSIRT de la LogAnalyzer (raportare, taxonomie, cazuri, IOC, playbook-uri)

Stare: cercetare web și o verificare a codului existent, doar pentru citire. Nu s-a modificat cod.
Convenții: fiecare afirmație are URL-ul sursei. `[NESIGUR]` marchează ce nu am putut verifica într-o sursă primară.
`CODE_VERIFIED` marchează ce am verificat direct în acest depozit (căile sunt relative la `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`).

---

## 0. Pe scurt

1. Termenele legale din România sunt cele din NIS2, plus un termen național în plus: **6 ore** de la momentul în care entitatea află orice informație care arată un impact transfrontalier (OUG 155/2024 art. 15 alin. (3)). Canalul legal de raportare este **PNRISC**, nu e-mailul.
2. Generatorul de notificare NIS2 din aplicație (`LogAnalyzer.Core/Services/Nis2NotificationService.cs`) are greșeli de fond: calculează termenul de la „detectare” în loc de momentul „luării la cunoștință”, calculează raportul final de la detectare în loc de la notificarea de 72 h, adresează documentul către `contact@dnsc.ro`, completează automat sectorul și scrie patru măsuri de atenuare ca deja aplicate, fără să știe dacă au fost aplicate (`CODE_VERIFIED`).
3. Exportul MISP etichetează orice IOC care nu e hash drept `ip-dst`, deci și domeniile, URL-urile și IPv6 (`LogAnalyzer.Core/Services/StixMispExportService.cs:141`, `CODE_VERIFIED`). Nu există import STIX/MISP/CSV, deci schimbul de IOC offline merge într-un singur sens.
4. Taxonomia recomandată este **RSIT** (ENISA/TF-CSIRT), cu etichete ATT&CK pe fiecare constatare și, opțional, VERIS A4 pentru statistică. În cod nu există RSIT și nici ID-uri ATT&CK (`T1xxx`) (`CODE_VERIFIED`, grep).
5. Analiștii se plâng cel mai des de alerte fals pozitive, de timpul pierdut cu rapoartele și de fusurile orare. Aplicația are deja baza pentru fusuri orare (`TimeZoneBasis`, `TimeUncertainty` în `docs/dfir/FINDING_CONTRACT.md`). Îi lipsesc ceasul legal al cazului, predarea între ture, indicatorii MTTD/MTTR și șablonul de lecții învățate.

---

## 1. Obligații de raportare și câmpurile cerute la fiecare etapă

### 1.1 Baza legală

| Sursă | Ce stabilește | URL |
|---|---|---|
| Directiva (UE) 2022/2555 (NIS2), art. 23 | Avertizare timpurie în 24 h, notificare în 72 h, raport intermediar la cerere, raport final în cel mult o lună de la notificare, raport de progres dacă incidentul e încă în curs | https://eur-lex.europa.eu/eli/dir/2022/2555/oj/eng |
| OUG 155/2024 (MO 1332/31.XII.2024), art. 15 | Transpunerea. Raportarea se face către echipa națională CSIRT, **prin PNRISC** (alin. 2). Termen de **6 h** pentru informațiile despre impact transfrontalier (alin. 3). Etapele a)–e) sunt la alin. (7). Prestatorii de servicii de încredere au 24 h în loc de 72 h (alin. 8). CSIRT-ul național răspunde în 24 h (alin. 9) | text MO: https://arhiva.upt.ro/img/files/legislatie/2024/OUG_155_2024.pdf |
| Legea 124/2025 | Aprobă OUG 155/2024 cu modificări. Numerotarea articolelor poate să se fi schimbat: prima amendă DNSC (50.000 lei, 2026) e raportată ca încălcare a „art. 18 alin. (2)”, termenul de raportare, deși în textul inițial art. 18 e registrul entităților **[NESIGUR]** | https://www.posta-romana.ro/cnpr-data/_editor/files/sintact-legea-124-2025-pentru-aprobarea-ordonantei%281%29.pdf ; https://informat.ro/en/current-affairs/dnsc-fines-central-public-institution-50000-lei-141254 |
| Ordinele directorului DNSC 1/2025 și 2/2025 (MO 776/20.08.2025) | Ordinul 1/2025: notificarea pentru înregistrare și metoda de transmitere. Ordinul 2/2025: criteriile și pragurile de perturbare a serviciului și metodologia de evaluare a riscului | https://agerpres.ro/comunicate/2025/08/21/comunicat-de-presa---directoratul-national-de-securitate-cibernetica--1477633 |
| PNRISC | Platforma națională de raportare. DNSC a actualizat-o pentru a simplifica raportarea, inclusiv pentru entitățile NIS2. **Lista oficială de câmpuri nu este publică în sursele găsite [NESIGUR]** | https://financialintelligence.ro/dnsc-simplifica-modalitatea-de-raportare-a-incidentelor-cibernetice-si-lanseaza-o-platforma-publica-de-tip-blacklist/ |
| Regulamentul de punere în aplicare (UE) 2024/2690, art. 3 | Pragurile pentru incident semnificativ la furnizorii digitali: pierdere directă peste min(500.000 EUR; 5% din cifra de afaceri), exfiltrare de secrete comerciale, deces sau vătămare a sănătății, acces neautorizat suspect rău-intenționat cu potențial de perturbare gravă. Sursa e o reproducere secundară; trebuie verificată în OJ | https://advisera.com/cir-2024-2690/significant-incidents/ |
| Șabloane UE comune | Grupul de Cooperare NIS le-a adoptat pe 26.05.2026. Vor deveni obligatorii printr-un act de punere în aplicare **încă neadoptat**, iar câmpurile nu sunt publicate | https://digital-strategy.ec.europa.eu/en/news/nis2-cooperation-group-adopts-common-templates-incident-reporting |
| GDPR art. 33 (în paralel, către ANSPDCP) | 72 h de la luarea la cunoștință dacă e afectată confidențialitatea datelor personale. Informațiile pot fi trimise pe etape. Orice breșă trebuie documentată | https://gdpr-info.eu/art-33-gdpr/ |

Consecință pentru produs: un incident cu exfiltrare de date personale pornește **două ceasuri** (DNSC și ANSPDCP), uneori trei dacă apare indiciul transfrontalier de 6 h. Ceasurile pornesc din momentul luării la cunoștință, nu din momentul producerii incidentului (https://nisd2.eu/ro/wiki/implementare/obligatia-de-raportare-nis2, sursă secundară).

### 1.2 Câmpurile pe etape

Legenda coloanei „Bază”: **L** = cerut de text (art. 23 NIS2 / art. 15 OUG). **P** = practica șablonului neoficial nisd2.eu cu 30 de câmpuri (https://nisd2.eu/en/nis2-incident-notification-schema; publicat de o platformă comercială, nu e standard oficial). **G** = GDPR art. 33.

| Câmp | 24 h avertizare | 6 h transfrontalier (RO) | 72 h notificare | Intermediar (la cerere) | Final (≤1 lună de la 72 h) | Progres (incident în curs) | Bază | Sursa în LogAnalyzer |
|---|---|---|---|---|---|---|---|---|
| Identificarea entității (denumire, CUI, esențială/importantă, sector din anexe) | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | P | Setările cazului (azi sectorul e scris fix în cod) |
| Persoana de contact (nume, e-mail, telefon) și referința internă a cazului | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | P | Cazul / operatorul |
| Momentul luării la cunoștință (UTC + fusul local) | ✔ | ✔ | ✔ | | ✔ | | L | **Lipsește** (azi se folosește „detectare”) |
| Momentul producerii estimate și momentul detectării | ○ | | ✔ | | ✔ | | P | Cronologia: primul eveniment al lanțului |
| Rezumat scurt | ✔ | | ✔ | ✔ | | ✔ | P | Rezumatul lanțului de incident |
| Suspiciune de acțiune ilicită sau rău-intenționată (da / nu / necunoscut) | ✔ | | ✔ (actualizare) | | | | L | Decizia analistului (nu automat) |
| Posibil impact transfrontalier (da / nu / necunoscut) și statele afectate | ✔ | ✔ | ✔ | | ✔ | | L | Decizia analistului |
| Evaluarea inițială a gravității și impactului | | | ✔ | ✔ | | | L | Scorul de risc + impactul declarat |
| IOC, dacă sunt disponibili | | | ✔ | ✔ | ✔ | | L | `CurrentIocs` (doar IP, hash, domeniu, URL) |
| Descriere detaliată, cu gravitate și impact | | | | | ✔ | | L | Raportul de investigație |
| Tipul amenințării sau cauza principală probabilă | | | | | ✔ | | L | Clasificarea RSIT (vezi §2) + ATT&CK |
| Măsuri de atenuare **aplicate** și **în curs** (separat) | | | ○ | ✔ | ✔ | ✔ | L | Jurnalul de izolare și remediere (azi text fix în cod) |
| Utilizatori afectați (estimare), durata și descrierea perturbării serviciului | | | ✔ | ✔ | ✔ | | P / Ordin 2/2025 [NESIGUR] | Lipsește |
| Pierdere financiară estimată | | | ○ | | ✔ | | P / CIR 2024/2690 | Lipsește |
| Proprietățile CIA afectate | | | ○ | | ✔ | | P | Se poate deriva din RSIT |
| Notificarea destinatarilor serviciului (da/nu și textul mesajului) | ○ | | ✔ | | ✔ | | L (art. 15 alin. 1, 5) | Lipsește |
| Date personale afectate: categorii, număr aproximativ, consecințe, DPO | | | ✔ dacă e cazul | | ✔ | | G | Lipsește |
| Marcaj TLP al documentului | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | practică FIRST | Azi fix `TLP:AMBER+STRICT` |
| Sesizare la organele de urmărire penală (da/nu) | | | ○ | | ✔ | | OUG art. 15 alin. (10) | Lipsește |

✔ = cerut, ○ = recomandat.
Câmpurile exacte din PNRISC rămân **[NESIGUR]**. Recomand ca aplicația să genereze un **pachet de câmpuri** (JSON + text pentru copiere) care urmează tabelul de mai sus, nu un „formular DNSC” care pretinde că are forma oficială.

### 1.3 Ce trebuie corectat în generatorul existent (`CODE_VERIFIED`, `Nis2NotificationService.cs`)

| Linie | Problemă | Ce cere legea |
|---|---|---|
| 32 | „Către: DNSC — contact@dnsc.ro” | Raportarea se face prin PNRISC (OUG art. 15 alin. 2) |
| 38–40 | „24/72 ore de la detectare” | De la **luarea la cunoștință** (art. 15 alin. 7 a, b) |
| 40, 90 | „1 lună”, apoi „30 de zile” | O lună **de la transmiterea notificării de 72 h** (alin. 7 d) |
| 56 | Sectorul e scris fix: „Infrastructură Critică / Servicii Esențiale” | Sectorul real din anexele OUG |
| 77–80 | Patru măsuri scrise ca deja aplicate (izolare, imagini, revocare Kerberos, blocare IOC) | Doar măsurile documentate în caz, separate în „aplicate” și „în curs”. Altfel aplicația produce o declarație falsă către o autoritate |
| enum `Nis2Stage` | Lipsesc etapele de 6 h transfrontalier, intermediar și progres | alin. (3), (7) c), e) |

---

## 2. Taxonomie: recomandare și corespondența cu tipurile noastre de constatări

### 2.1 Ce există și ce recomand

| Taxonomie | Scop | Recomandare | URL |
|---|---|---|---|
| **RSIT** (Reference Security Incident Taxonomy, v1003) | Limbaj comun între CSIRT-urile europene. Are 11 categorii și etichete citibile de mașini, de forma `categorie/tip` | **Principală**, pe caz și pe fiecare constatare | https://github.com/enisaeu/Reference-Security-Incident-Taxonomy-Task-Force/blob/master/working_copy/humanv1.md |
| Taxonomii MISP (`rsit`, `nis2`, `veris`, `tlp`, `PAP`, `incident-disposition`, `action-taken`, `admiralty-scale`, `estimative-language`) | Etichete pentru schimbul de date | Exportul folosește `rsit:`, `tlp:`, `PAP:`. Pe caz se adaugă `incident-disposition` și `action-taken` | https://github.com/MISP/misp-taxonomies |
| Taxonomia NIS (din Recomandarea 2017/1584, `nis` / `nis2` în MISP) | Natură și impact, pentru incidente la scară mare | Secundară, doar pentru câmpul „cauză principală” din raportul final | https://github.com/MISP/misp-taxonomies |
| **MITRE ATT&CK** | Tehnicile atacatorului | Pe fiecare constatare (ID `Txxxx`) | https://attack.mitre.org/ |
| VERIS (A4: Actor, Action, Asset, Attribute) | Statistică și analiză post-incident | Opțional, în raportul de audit și lecții învățate | https://www.verizon.com/business/resources/reports/dbir/ ; https://infosecinstitute.com/resources/threat-hunting/veris-incident-framework/ |

DFIR-IRIS folosește direct clasificări de forma `malicious-code:ransomware` în șabloanele de caz (https://docs.dfir-iris.org/latest/operations/case_templates/). Folosirea RSIT face exportul compatibil cu IRIS și MISP fără conversie.

### 2.2 Corespondența cu `RuleId`-urile LogAnalyzer (`CODE_VERIFIED`, grep în `LogAnalyzer.Dfir.*`, `LogAnalyzer.Core`)

ID-urile ATT&CK de mai jos sunt propunerea mea și trebuie revizuite de un analist.

| RuleId LogAnalyzer | RSIT (`categorie/tip`) | ATT&CK propus |
|---|---|---|
| `CRED-BRUTEFORCE` | `intrusion-attempts/login-attempts` | T1110 |
| `ACCOUNT-CREATED`, `ACCOUNT-CREATED-THEN-USED` | `intrusions/unprivileged-account-compromise` (sau `privileged-…` dacă e admin) | T1136, T1078 |
| `ACCOUNT-ADDED-PRIVILEGED-GROUP` | `intrusions/privileged-account-compromise` | T1098 |
| `EXEC-USERPATH`, `EXEC-USERPATH-CORRELATED`, `DOWNLOAD-THEN-EXEC` | `malicious-code/infected-system` | T1204, T1105 |
| `EXEC-SCRIPT-VIA-LOLBIN`, `PS-SUSPICIOUS` | `malicious-code/infected-system` / `intrusions/system-compromise` | T1059.001, T1218 |
| `PERSIST-RUNKEY-USERPATH`, `LIVE-AUTORUN-USERPATH` | `intrusions/system-compromise` | T1547.001 |
| `PERSIST-TASK`, `PERSIST-TASK-CONFIG`, `LIVE-TASK-USERPATH` | `intrusions/system-compromise` | T1053.005 |
| `PERSIST-SERVICE-*`, `LIVE-SERVICE-USERPATH` | `intrusions/system-compromise` | T1543.003 |
| `PERSIST-IFEO-DEBUGGER` | `intrusions/system-compromise` | T1546.012 |
| `PERSIST-WINLOGON` | `intrusions/system-compromise` | T1547.004 |
| `REMOTE-RDP-INTERNAL`, `REMOTE-PSEXEC`, `REMOTE-WINRM`, `REMOTE-SSH`, `SMB-ADMIN-SHARE` | `intrusions/system-compromise` (mișcare laterală) | T1021.001/.002/.006/.004, T1569.002 |
| `REMOTE-RDP-PUBLIC` | `vulnerable/potentially-unwanted-accessible-services` | T1133 |
| `DEF-TAMPER`, `SECURITY-AGENT-STOPPED`, `FIREWALL-RULE-USERPATH` | `intrusions/system-compromise` | T1562.001, T1562.004 |
| `DEF-DETECTION` | `malicious-code/infected-system` | după detecție |
| `LOG-TAMPER`, `LOG-GAP` | `intrusions/system-compromise` / `other/undetermined` (golul poate fi și o defecțiune) | T1070.001 |
| `VSS-SNAPSHOT-DELETED` | `availability/sabotage` (indiciu de ransomware) | T1490 |
| `DNS-SERVER-CHANGED` | `intrusions/system-compromise` | T1565 / T1584 [NESIGUR] |
| `DNS-RARE-DOMAIN` | `malicious-code/c2-server` (doar ca suspiciune) | T1071.004 |
| `SMB-SHARE-PERMS-CHANGED` | `information-content-security/unauthorised-modification-of-information` | T1222 |
| `MEDIA-FILE-ACTIVITY`, `MEDIA-OPTICAL-ACTIVITY` | `information-content-security/leak-of-confidential-information` (doar cu dovadă de copiere) altfel `other/undetermined` | T1052.001 |
| `POLICY-CONTROL-GAP` | `vulnerable/vulnerable-system` | — |
| `INCIDENT-CHAIN` | clasa dominantă a lanțului | toate tehnicile din lanț |
| Investigație mail: regulă de inbox care redirecționează | `fraud/phishing` + `intrusions/unprivileged-account-compromise` | T1114.003, T1566 |

Reguli de corespondență:
- Constatarea primește eticheta RSIT. Cazul primește clasa dominantă, aleasă de analist, nu de aplicație.
- Tipul `SemanticType` (OBSERVATION…ATTRIBUTION) rămâne separat de RSIT. O constatare de tip OBSERVATION nu ar trebui să ducă singură la o clasă `intrusions/*` fără confirmare.

---

## 3. Gestiunea cazurilor și schimbul de IOC: comparație

### 3.1 Ce oferă instrumentele folosite de CSIRT-uri

| Funcție | TheHive 5 + Cortex | DFIR-IRIS | MISP | Timesketch | Velociraptor | LogAnalyzer azi |
|---|---|---|---|---|---|---|
| Licență | Comercială. Ediția Community e gratuită, dar trebuie înregistrată, altfel intră în read-only după 14 zile (https://docs.strangebee.com/thehive/installation/licenses/about-licenses/ ; https://blog.strangebee.com/thehive-turns-5-and-adopts-a-model-shaped-for-the-future) | Open source, self-hosted (https://docs.dfir-iris.org/) | Open source (https://www.misp-project.org/) | Open source (https://timesketch.org/) | Open source (https://docs.velociraptor.app/) | Produs propriu, offline și conectat |
| Șabloane de caz cu sarcini | Da (https://github.com/CERT-BDF/TheHive) | Da: JSON cu `tasks`, `note_directories`, `classification`, `tags` (https://docs.dfir-iris.org/latest/operations/case_templates/) | Nu (evenimente) | Nu | Nu (hunt-uri) | Parțial (management de caz; fără șabloane pe tip de incident) |
| Observabile/IOC cu TLP/PAP, semnalate dacă au mai apărut | Da (https://github.com/CERT-BDF/TheHive) | Da (IOC pe caz) | Da (atribute + corelare) | Nu | Nu | Da, dar doar 5 tipuri și fără TLP pe IOC (`Models/IocItem.cs`, `CODE_VERIFIED`) |
| Analiză automată a observabilelor | Cortex, peste 100 de analizori și responderi | Module | Expansiuni | Analizori | Artefacte VQL | YARA/Sigma local |
| Cronologie colaborativă | Limitată | Da | Nu | **Da**: Plaso/CSV/JSONL, etichete, căutări salvate (https://timesketch.org/) | Nu | Da (cronologia cazului, UTC + bază de fus orar) |
| Colectare offline | Nu | Nu | Nu | Nu | **Da**: colector offline, ZIP criptat X.509, importat transparent în server (https://docs.velociraptor.app/docs/deployment/offline_collections/) | Da (dovezi aduse de la stații) |
| Lanț de custodie / hash | Parțial | Evidențe cu hash | Nu | Nu | Hash la colectare | **Da**, SHA-256 + reverificare |
| Export STIX 2.1 / MISP | Da (integrare MISP) | Da [NESIGUR] | Nativ | Nu | Nu | Doar export, cu bug-ul `ip-dst` |
| Import STIX / MISP / CSV offline | Da | Da [NESIGUR] | Da | — | — | **Lipsește** |

Ce se laudă și ce se reclamă:
- TheHive: după trecerea la model comercial, versiunile 3/4 nu mai sunt întreținute, iar Community cere licență, altfel intră în read-only (sursele de mai sus). Pentru un CSIRT izolat de internet, înregistrarea licenței e un obstacol practic [NESIGUR, nu am găsit plângeri publice documentate].
- DFIR-IRIS: e apreciat pentru că pune notele și IOC-urile la un loc și se instalează ușor în Docker. Sursele se contrazic despre etichetarea ATT&CK (https://isc.sans.edu/diary/29880).
- Plaso/Timesketch: volumul (milioane de evenimente) cere filtre de timp și reguli de etichetare. Recomandarea e normalizarea în UTC, cu fusul local luat din stupul SYSTEM (https://ayinedjimi-consultants.fr/glossaire/log2timeline, sursă secundară).

### 3.2 Formate de schimb IOC

| Format | Stare | Ce să suporte LogAnalyzer | URL |
|---|---|---|---|
| STIX 2.1 (+TAXII 2.1) | Standard OASIS din 10.06.2021 | Export și **import** de fișier: `indicator`, `observed-data`, `sighting`, `relationship`, `attack-pattern`, plus `marking-definition` pentru TLP | https://www.oasis-open.org/?p=7287 |
| MISP core format (JSON) | Internet-Draft IETF; draft-20 din 11.2025 a expirat la 30.05.2026 | Export și import de fișier eveniment cu `Attribute[].type` corect (`ip-dst`, `domain`, `url`, `sha256`, `filename`, `regkey`, `email-src`…) și `Tag` (rsit, tlp) | https://datatracker.ietf.org/doc/draft-dulaunoy-misp-core-format |
| OpenIOC 1.1 | Format Mandiant, folosit tot mai rar [NESIGUR] | Doar import, prioritate mică | https://github.com/fireeye/OpenIOC_1.1 |
| CSV | De facto | Import și export simplu: `type,value,tlp,first_seen,last_seen,source,comment` | — |

Pentru ediția fără rețea: importul trebuie să valideze schema, să calculeze SHA-256 pe fișierul importat și să-l înregistreze în lanțul de custodie ca sursă externă neverificată. Este același principiu ca la colectorul offline Velociraptor, unde fișierul ZIP e criptat cu certificatul serverului.

---

## 4. Playbook-uri pe tip de incident (Windows)

Cadru: NIST SP 800-61r3 (aprilie 2025) leagă răspunsul la incident de cele 6 funcții CSF 2.0 și nu mai impune un ciclu fix (https://www.nist.gov/news-events/news/2025/04/nist-revises-sp-800-61-incident-response-recommendations-and-considerations ; https://csrc.nist.gov/pubs/sp/800/61/r3/final). SANS PICERL (Preparation, Identification, Containment, Eradication, Recovery, Lessons learned) rămâne structura folosită în practică (https://www.sans.org/white-papers/33901/). Microsoft dă pentru fiecare playbook: cerințe prealabile, flux, listă de verificare și pași de investigație (https://learn.microsoft.com/en-us/security/operations/incident-response-playbooks). CISA are liste de verificare pentru răspuns și pregătire (https://cisa.gov/sites/default/files/2024-08/Federal_Government_Cybersecurity_Incident_and_Vulnerability_Response_Playbooks_508C.pdf).
Colectarea dovezilor urmează ordinea volatilității din RFC 3227: registre și memorie, apoi tabele de proces și rețea, apoi disc, jurnale remote, arhive. Nu se folosesc unelte de pe sistemul compromis. Sistemul nu se oprește înainte de colectare (https://www.rfc-editor.org/rfc/rfc3227). ISO/IEC 27037 cere auditabilitate, repetabilitate, reproductibilitate și justificabilitate și definește rolurile DEFR și DES (https://www.iso27001security.com/html/27037). 27042 acoperă analiza și interpretarea (https://www.iso27001security.com/html/27042), iar 27043 procesul de investigație (https://www.iso27001security.com/html/27043).

### 4.1 Ransomware

| Fază | Pași | Dovezi de colectat | Ce i se spune conducerii |
|---|---|---|---|
| Identificare | Delimitezi stațiile afectate și găsești pacientul zero | EVTX Security/System/PowerShell/TaskScheduler, Prefetch, Amcache, SRUM (volum de rețea pe proces), nota de răscumpărare, extensiile fișierelor | Ce servicii sunt oprite, câte stații, dacă există indicii de exfiltrare. Ceasul de 24 h a pornit |
| Izolare | Izolezi din rețea, **fără să oprești** stația, pentru că memoria RAM se pierde. Păstrezi nota și comunicările cu atacatorii. Colectezi jurnalele de firewall (DNSC: https://startupcafe.ro/ai-aflat-ca-datele-firmei-au-fost-compromise-ce-trebuie-sa-faci-in-primele-24-de-ore-potrivit-dnsc-103355) | Memoria (dacă e posibil), `VSS-SNAPSHOT-DELETED`, `DEF-TAMPER`, `LOG-TAMPER` | Decizia de oprire a serviciilor și de comunicare externă îi aparține conducerii (DNSC, aceeași sursă) |
| Eradicare | Elimini persistența (servicii, sarcini, Run) și resetezi conturile privilegiate și krbtgt [NESIGUR pentru pașii exacți]. Scanezi YARA toată infrastructura, nu doar stațiile afectate (DNSC, aceeași sursă) | Constatările `PERSIST-*`, `REMOTE-*` | Data estimată de revenire |
| Recuperare | Restaurezi din copii offline verificate. Restaurarea nu rezolvă confidențialitatea dacă datele au fost exfiltrate (DNSC, aceeași sursă). Ghidul #StopRansomware are lista de verificare completă (https://cisa.gov/news-events/news/cisa-fbi-nsa-ms-isac-publish-updated-stopransomware-guide) | Hash-ul copiilor folosite | Nu se plătește răscumpărarea: DNSC o descurajează, iar plata nu elimină obligația de notificare |
| Raportare | 24 h / 72 h / final către DNSC prin PNRISC. GDPR art. 33 dacă sunt afectate date personale. Asigurătorul, dacă există poliță | | Cine semnează fiecare raport și până când |

### 4.2 Phishing / BEC (compromiterea căsuței de e-mail)

| Fază | Pași | Dovezi | Conducerii |
|---|---|---|---|
| Identificare | Găsești destinatarii și cine a dat clic. Verifici că auditul căsuțelor e activ (Microsoft: https://learn.microsoft.com/en-ca/security/zero-trust/security-operations-playbook-phishing) | Antetele mesajului, URL-urile, autentificările utilizatorului (4624/4625, Entra), **regulile de inbox** (redirecționare, ștergere) | Dacă au plecat bani sau date |
| Izolare | Dezactivezi contul, revoci tokenurile, **ștergi regulile de inbox**, pentru că supraviețuiesc resetării parolei (https://www.aguidetocloud.com/mind-maps/phishing-response-playbook/, sursă secundară [NESIGUR]) | Exportul regulilor înainte de ștergere (hash) | Plățile suspecte trebuie oprite imediat la bancă |
| Eradicare | Verifici aplicațiile OAuth cu consimțământ, conform playbook-urilor Microsoft pentru app consent și password spray | | |
| Raportare | NIS2 dacă impactul e semnificativ. GDPR dacă au fost expuse date personale | | Mesajul pentru parteneri și clienți |

### 4.3 Compromiterea unui cont (inclusiv password spray)

Pași: găsești sursa (IP-uri, stații), stabilești intervalul de compromitere, enumeri ce a accesat contul, resetezi parola, revoci sesiunile și reînregistrezi MFA. Dovezi: 4624 (tip logon), 4625, 4648, 4768/4769/4776, 4720/4728/4732 (`ACCOUNT-*`), inventarul AD. Password spray înseamnă multe conturi încercate cu aceleași parole comune (https://github.com/Cloud-Architekt/AzureAD-Attack-Defense). Conducerii: ce date și sisteme a putut atinge contul.

### 4.4 Mișcare laterală

Pași: construiești graful sursă → destinație (RDP, PsExec, WinRM, SMB admin share), apoi stabilești contul folosit și momentul. Dovezi: 4624 tip 3/10, 4648, 7045 (serviciu PsExec), 5140/5145, TerminalServices-*, Sysmon dacă există. JPCERT documentează urmele lăsate de 49 de unelte și arată că, în configurarea implicită a Windows, multe nu se înregistrează. De aici nevoia de politică de audit și de Sysmon (https://blogs.jpcert.or.jp/en/2017/12/research-report-released-detecting-lateral-movement-through-tracking-event-logs-version-2.html). Conducerii: câte sisteme au fost atinse și dacă au fost atinse conturile de domeniu.

### 4.5 Exfiltrare de date de către un angajat (USB)

Pași: identifici dispozitivul (serie, VID/PID, eticheta volumului), legi dispozitivul de utilizator (MountPoints2 din NTUSER), apoi legi fișierele de dispozitiv (LNK, JumpLists, ShellBags, `MEDIA-FILE-ACTIVITY`). Dovezi: Partition/Diagnostic 1006 (Windows 10+, activ implicit, conține serii de volum) (https://blog.ncst.com/usb-forensics-recover-more-volume-serial-numbers-with-the-windows-10-partition-diagnostic-event-log1678811064). Security 6416 apare doar cu politica avansată de audit și e un indiciu, nu o dovadă de transfer (https://detection.fyi/sigmahq/sigma/windows/builtin/security/win_security_external_device/). Contează și SetupAPI, USBSTOR, Windows Portable Devices (https://hackmd.io/@M4shl3/USB-Forensics). Atenție juridică: RFC 3227 §2.3 cere să nu colectezi din zone personale fără indicii suficiente de incident. Investigațiile asupra angajaților implică HR și juristul. Conducerii: ce date au plecat, dovada legăturii persoană–dispozitiv–fișier și ce rămâne nedovedit.

### 4.6 Liste transversale

- **Izolare**: izolare în rețea, nu oprire; blocare IOC; dezactivare conturi; păstrarea dovezilor înainte de orice schimbare. Pentru „izolarea proceselor cu confirmare” din produs, fiecare acțiune trebuie înregistrată în jurnalul cazului ca „măsură aplicată”. Aceeași înregistrare alimentează câmpul din raportul NIS2.
- **Eradicare**: cauza principală, eliminarea persistenței, resetarea credențialelor, verificarea absenței recurenței.
- **Recuperare**: restaurare verificată, monitorizare sporită, criteriile de închidere.
- **Lecții învățate**: cauza principală, cronologia, ce a mers și ce nu, acțiuni corective cu responsabil și termen, indicatori. Sursele: SANS ISC (https://isc.sans.edu/diary/372) și NIST SP 800-61r3. Lista de întrebări exactă e de verificat în textul NIST **[NESIGUR]**.
- **RACI / comunicare**: conducerea aprobă oprirea serviciilor, comunicarea externă și sesizarea autorităților. Responsabilul de securitate coordonează partea tehnică. DPO-ul se ocupă de datele personale. Partenerii se informează fără întârzieri nejustificate. Planul se exersează cel puțin anual (DNSC, prin https://startupcafe.ro/ai-aflat-ca-datele-firmei-au-fost-compromise-ce-trebuie-sa-faci-in-primele-24-de-ore-potrivit-dnsc-103355).
- **Indicatori**: MTTD (de la producere la detectare), MTTR sau MTTC (de la detectare la izolare sau rezolvare), timpul până la avertizarea timpurie, procentul de fals pozitive pe regulă. Definițiile variază între organizații **[NESIGUR, fără sursă normativă]**. Aplicația le poate calcula din cele trei momente pe care le are deja sau le va avea (producere, detectare, luare la cunoștință) plus momentele izolării.

### 4.7 Ce îi chinuie pe analiști

- Fals pozitivele sunt principala problemă de detecție pentru 73% dintre echipe, potrivit SANS 2025 Detection & Response Survey (citat printr-un rezumat al unui vendor [NESIGUR]: https://www.stamus-networks.com/blog/what-the-2025-sans-detection-response-survey-reveals-false-positives-alert-fatigue-are-worsening).
- SOC-urile sunt mici (cel mai des 2–10 oameni), iar 62% spun că organizația nu face destul pentru a-și păstra oamenii (SANS 2025 SOC Survey: https://www.elastic.co/resources/security/report/2025-state-of-the-soc-survey).
- 52% se declară suprasolicitați (Cisco, nu SANS: https://investor.cisco.com/files/doc_news/Global-State-of-Security-Report-Reveals-Critical-Need-for-Connected-Security-Operations-2025.pdf).
- Fusurile orare și volumul cronologiilor: vezi §3.1.
- Predarea între ture și timpul de scriere a rapoartelor apar des în practică, dar nu am găsit o sursă cantitativă primară **[NESIGUR]**.

---

## 5. Goluri pentru LogAnalyzer, în ordinea priorității

Efort: S ≤ 2 zile, M ≤ 2 săptămâni, L > 2 săptămâni (estimări, nemăsurate).

| # | Gol | Prioritate | Efort | Motiv / sursă |
|---|---|---|---|---|
| 1 | Corectarea generatorului NIS2: momentul luării la cunoștință, final = notificare + 1 lună, canal PNRISC, sector din setări, măsuri luate doar din jurnalul cazului | P1 | S | §1.3; OUG art. 15. Risc de declarație falsă |
| 2 | **Ceasul legal al cazului**: momentul luării la cunoștință, termenele de 24 h, 6 h (transfrontalier), 72 h, final și GDPR 72 h, cu alertă vizibilă | P1 | M | OUG art. 15 alin. (3), (7); GDPR art. 33 |
| 3 | Bug export MISP: tipul atributului e `ip-dst` pentru domeniu, URL și IPv6 | P1 | S | `StixMispExportService.cs:141` |
| 4 | Pachet de raportare pe etape (24 h / 72 h / intermediar / final / progres) cu câmpurile din §1.2, câmpuri lipsă semnalate, export JSON + text pentru PNRISC | P1 | M | §1.2 |
| 5 | Clasificare RSIT pe caz și pe constatare + ATT&CK pe fiecare `RuleId` (tabelul din §2.2) | P1 | M | RSIT; MISP; IRIS |
| 6 | Import offline STIX 2.1 / MISP JSON / CSV de IOC, cu hash și înregistrare în lanțul de custodie, apoi căutare în dovezile cazului (sightings) | P1 | M | §3.2 |
| 7 | Jurnal de acțiuni de răspuns (cine, ce, când, aprobat de cine) care alimentează „măsuri aplicate / în curs” și cronologia | P1 | M | Art. 23(4)(d); ISO 27037 (auditabilitate) |
| 8 | Șabloane de caz pe tip de incident (cele 5 din §4), cu sarcini, dovezi de colectat și text pentru conducere, compatibile cu formatul JSON IRIS | P2 | M | DFIR-IRIS; Microsoft; CISA |
| 9 | Extinderea `IocType`: e-mail, nume de fișier, cheie de registru, cale, user-agent, JA3, plus TLP/PAP și sursă pe fiecare IOC | P2 | S | MISP core format |
| 10 | Deduplicare și suprimare a constatărilor repetate (aceeași regulă, gazdă și fereastră de timp) + marcare fals pozitiv pe regulă, ca feedback | P2 | M | SANS 2025 (fals pozitive) |
| 11 | Predare între ture: rezumat generat din caz (ce s-a schimbat de la ultima predare, sarcini deschise, termene legale care se apropie) | P2 | S | §4.7 [NESIGUR ca nevoie cuantificată] |
| 12 | Indicatori MTTD/MTTC/MTTR, timpul până la avertizarea timpurie și rata de fals pozitive, pe caz și agregat | P2 | S | §4.6 |
| 13 | Șablon de lecții învățate (cauză principală, ce a mers, acțiuni corective cu responsabil și termen) legat de propunerea către memory vault | P2 | S | NIST 800-61r3; SANS |
| 14 | Formular de lanț de custodie tipăribil (RFC 3227 §4.1: cine, unde, când a descoperit/colectat/manipulat, transferuri, depozitare) + roluri DEFR/DES | P2 | S | RFC 3227; ISO 27037 |
| 15 | RACI și șabloane de comunicare (conducere, parteneri, clienți, autorități, asigurător) generate din caz | P3 | S | DNSC (comunicat de presă) |
| 16 | Import de colecții Velociraptor offline (ZIP) și export Timesketch (JSONL/CSV, UTC) | P3 | M | Velociraptor; Timesketch |
| 17 | Câmpuri VERIS A4 opționale în raportul de audit, pentru statistică | P3 | S | VERIS |
| 18 | Actualizarea la șabloanele UE comune după adoptarea actului de punere în aplicare | P3 | M | Comisia, 26.05.2026 (încă neadoptat) |

---

## 6. Riscuri și necunoscute

- **Câmpurile PNRISC și conținutul Ordinului 2/2025 nu au fost verificate**. Tabelul din §1.2 urmează textul legii, nu formularul platformei.
- **Numerotarea după Legea 124/2025**: este posibil ca art. 15 să fi devenit altul. Amenda DNSC citează „art. 18 alin. (2)” pentru termenul de raportare [NESIGUR].
- Șabloanele UE comune vor schimba câmpurile când va fi adoptat actul de punere în aplicare. Exportul trebuie gândit ca date (JSON) cu o mapare pe versiuni.
- Cifrele SANS sunt preluate prin rezumate ale unor vendori. Rapoartele originale nu au fost citite.
- Corespondența ATT&CK din §2.2 este o propunere și trebuie revizuită de un analist.
- Riscul juridic al textului fix din generatorul NIS2 (§1.3) este real: aplicația poate atesta în fața autorității măsuri neaplicate.
