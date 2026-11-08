# Memoria: ieșirile Volatility 3 (master plan 6.6)

**Unde se află:**
- **Cod:** `LogAnalyzer.Dfir.Core/Memory/VolatilityJsonParser.cs`, înregistrat în `WindowsParsers.Registry`.
- **Teste:** `VolatilityJsonParserTests`, care trec prin registru și prin pipeline-ul real.

**Cum se lucrează:**
1. Analistul rulează Volatility 3 pe imaginea de memorie, în afara aplicației: `vol -r json -f mem.raw windows.pslist > pslist.json` și la fel pentru `psscan`, `netscan`, `cmdline`, `malfind`.
2. Importă fișierele JSON în caz ca probe.
3. Aplicația nu rulează Volatility și nu citește imaginea de memorie. Citește doar ce a scris unealta și o spune în limitele parserului.

**Ce se citește:** pluginul se recunoaște după coloane, nu după nume.

| Plugin | Ce intră în cronologie |
|---|---|
| `pslist` / `psscan` / `pstree` (cu `__children`) | proces, PID, părinte, ora pornirii, ora încheierii |
| `netscan` | conexiune, proces proprietar, adrese, stare, ora creării |
| `cmdline` | linia de comandă la momentul capturii |
| `malfind` | regiunea de memorie, protecția, dacă începe cu antet MZ |

Un JSON care nu are coloanele unui plugin cunoscut nu este citit ca și cum ar fi; parserul îl numără ca înregistrare malformată.

**Proces doar în psscan:** `VolatilityJsonParser.PsscanOnly` dă procesele găsite de `psscan` și negăsite de `pslist`, nemarcate ca încheiate. Sunt candidați pentru un proces ascuns, nu un verdict: psscan găsește și structuri rămase de la procese încheiate, iar procesele pornesc și se opresc între rulările pluginurilor.

**Verificat în laborator (Arhipelag, LAB-WS01, 2026-11-08):** ieșiri SINTETICE în formatul Volatility 3 (laboratorul nu are o imagine de memorie) au fost importate într-un caz nou. Rezultatul: 8 evenimente în cronologie (pslist, psscan, netscan, malfind).

**Încă nu există:**
- regulile de constatare pe aceste evenimente (proces ascuns, regiune injectată), pentru că cer un contract de regulă;
- corelarea automată memorie–disc. Deocamdată evenimentele stau în cronologie alături de Prefetch, Amcache și 4688.

## Reguli de constatare și corelarea memorie–disc (plan 6.6)

Regulile rulează pe rândurile `Memory:*` din cronologie (`MemoryFindings.Run`, un singur punct de apel în pipeline). Toate constatările sunt **candidați citiți din ieșirea unei unelte**, cu clasificare „Nedovedit” sau „Candidat”, nu verdicte; fiecare citează rândurile folosite (EvidenceId și locator). Listele (locații inscriptibile, procese de bază Windows, surse de pe disc) sunt în `Analysis/Data/memory_rules.json`, încorporat comprimat.

| Regulă | Când apare | Severitate |
|---|---|---|
| MEM-HIDDEN | proces găsit de psscan, negăsit de pslist, neîncheiat (psscan găsește și rămășițe) | medie |
| MEM-INJECT | regiune malfind PAGE_EXECUTE_READWRITE care începe cu MZ (T1055); fără MZ doar dacă procesul are alt semnal (ascuns sau cale inscriptibilă), altfel nimic | mare / mică |
| MEM-NETPROC | conexiune netscan cu adresă la distanță a unui proces MEM-HIDDEN sau MEM-INJECT mare; citează ambele semnale | mare |
| MEM-PATH | imaginea din linia de comandă e în Users\Public, AppData\Local\Temp, Windows\Temp, ProgramData, $Recycle.Bin, Descărcări sau Desktop | medie |
| MEM-NODISK | proces din pslist/psscan, neîncheiat, al cărui nume de executabil nu apare în nicio sursă de pe disc a cazului | mică, informativ |

**Corelarea MEM-NODISK:** sursele de pe disc citite sunt `Prefetch`, `Amcache`, `ShimCache`, `AppCompat`, `Service` și evenimentele de creare de proces `EventLog:Security` 4688 și `EventLog:Microsoft-Windows-Sysmon/Operational` 1. Numele se caută în câmpurile proces, cale și serviciu și în rezumatul rândului. Numele din ieșirea Volatility sunt tăiate la 14 caractere, deci un nume de 14 caractere sau mai lung se potrivește ca prefix. Se sare peste procesele de bază Windows din listă. Formularea este „fără urmă pe disc în probele cazului”, **nu** „fără fișier”: Prefetch poate fi dezactivat, jurnalele rotite, iar probele pot veni de la alt moment. Dacă cazul nu are nicio sursă de pe disc, regula nu emite nimic și apare un decalaj (EvidenceGap) care spune că MEM-NODISK nu poate fi evaluată.

Fiecare regulă dă cel mult 200 de constatări, deduplicate pe (regulă, PID, proces). Rezultatul depinde doar de cronologie: două rulări dau aceleași constatări.

**Simboluri lipsante:** un JSON Volatility cu listă goală (`[]`) sau ale cărui rânduri nu pot fi citite produce un decalaj „Parțial”: pluginul nu a produs rânduri (simboluri lipsă sau plugin eșuat), iar aceasta nu înseamnă că imaginea nu conține astfel de date. Un JSON care nu are coloanele unui plugin cunoscut rămâne înregistrare malformată, ca până acum.

**Încă nu există:** citirea imaginii de memorie de către aplicație, verificarea semnăturilor în memorie și legarea regulilor de un contract de regulă separat.

