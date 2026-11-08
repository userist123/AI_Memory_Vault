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

## Rularea controlată a Volatility din aplicație (plan 6.6)

Cod: `LogAnalyzer.Dfir.Windows/Memory/VolatilityRunner.cs`, profilul și manifestul simbolurilor în `LogAnalyzer.Dfir.Core/Memory/VolatilityToolProfile.cs`. Interfață: fila „Memorie (Volatility)” a cazului. Teste: `VolatilityRunnerTests`.

**Aprobarea (administratorul stației, o singură dată, din consolă pornită ca administrator):**

```
LogAnalyzer.KeyGen volatility-profile "C:\Program Files\Volatility3\vol.exe" "C:\Program Files\Volatility3-symbols" [--timeout 30]
```

Comanda calculează amprentele SHA-256 ale executabilului, ale fiecărui fișier din folderul uneltei și ale fiecărui fișier de simboluri. Folderul uneltei trebuie să fie al ei propriu: o versiune Volatility autonomă, nu un executabil care folosește un Python instalat în altă parte. Comanda scrie manifestele `tool_manifest.json` și `symbols_manifest.json` și profilul `tools.json` în `%PROGRAMDATA%\LogAnalyzer\tools`. Folderul primește proprietar Administrators și drepturi de scriere doar pentru Administrators și SYSTEM. Un folder de profil existent care poate fi modificat de alte conturi (de exemplu, pregătit dinainte de un neadministrator) este mutat deoparte, iar cel nou primește drepturile chiar la creare. Comanda refuză un folder al uneltei sau al simbolurilor pe care îl pot modifica și alte conturi. De exemplu, un folder creat direct sub C:\ moștenește dreptul de modificare pentru Authenticated Users. Aplicația doar citește profilul.

**Rularea (analistul):** alege imaginea de memorie din caz și apasă „Rulează Volatility”. Se rulează pluginurile `windows.pslist`, `psscan`, `pstree`, `netscan`, `cmdline` și `malfind`. Cazul este apoi reanalizat, iar ieșirile trec prin parserul și regulile de mai sus.

**Ce se verifică, pe fiecare amenințare:**

| Amenințare | Verificare |
|---|---|
| Profil scris de un neadministrator | Folderul profilului, folderul uneltei și folderul simbolurilor (fiecare fișier și subfolder) trebuie să aibă proprietar Administrators, SYSTEM sau TrustedInstaller și să nu poată fi scrise de alt cont. Altfel rularea este refuzată. |
| Executabil înlocuit sau fișier schimbat lângă el (DLL, pachet Python, plugin) | Tot folderul uneltei trebuie să corespundă manifestului aprobat; amprenta executabilului este fixată separat. Executabilul rămâne deschis cu scrierea blocată până la ultimul plugin și se reverifică sub acest blocaj. |
| Simboluri schimbate | Folderul trebuie să corespundă exact manifestului aprobat: niciun fișier în plus, lipsă sau modificat, nicio legătură sau joncțiune. |
| Rețea și configurare moștenită | Unealta rulează cu `--offline`. Variabilele `PYTHON*` și `VOLATILITY*` sunt eliminate. TEMP, TMP, directorul de lucru și cache-ul sunt într-un folder privat, șters după rulare. |
| Argumente injectate | Pluginurile vin doar din lista permisă. Imaginea este copia din caz, dată cu cale completă. Unealta pornește fără shell, cu un argument pe element. |
| Imagine modificată | Amprenta imaginii se compară cu indexul probelor înainte și după rulare. Între cele două verificări, imaginea este deschisă cu scrierea blocată. O diferență oprește rularea sau elimină toate ieșirile. |
| Blocare sau inundare | Fiecare plugin are o limită de timp. Unealta este oprită în momentul în care o ieșire trece de 512 MB, nu după ce a umplut discul. |
| Ieșire falsă | Ieșirea trebuie să fie o listă JSON. Rândurile ei rămân candidați citiți de parser, nu verdicte. |

**Ce rămâne în caz:**
- Fiecare pornire apare în auditul sigilat (`collector.exec`). Unealta aprobată este trecută printre programele pornite chiar de aplicație. În auditul cazului apar `volatility.start`, `volatility.done`, `volatility.refused`, `volatility.plugin_failed` și `volatility.aborted`.
- Fiecare ieșire păstrată devine o probă nouă, de tip `volatility_json`, marcată „derived” din imagine (`ParentEvidenceId`). Notele de custodie conțin amprenta uneltei, amprenta manifestului de simboluri, argumentele, codul de ieșire și durata.
- O ieșire goală, eșuată sau care nu este JSON nu devine probă. Problema ei este afișată și auditată.

**Verificat în laborator (2026-11-08, Insula, Arhipelag, Continent):** cu o unealtă TEST care ține locul Volatility. Laboratorul nu are Volatility, iar unealta nu a fost descărcată. Unealta TEST verifică argumentele primite (`--offline`, `-r json`, `-s`, `-f`, mediul fără `PYTHON*`) și tipărește ieșirile sintetice ale pluginurilor. Rezultate:
- aprobarea făcută cu `volatility-profile`, profilul instalat cu drepturi doar pentru administratori și unealta sub `C:\Program Files` trec verificarea reală a drepturilor;
- cele 6 pluginuri au dat probele derivate EV-000002…EV-000007 din imaginea TEST;
- reanalizarea a dat 8 evenimente și 3 constatări de memorie (MEM-HIDDEN, MEM-INJECT etc.);
- un fișier plantat în folderul uneltei (`python311.dll`) a fost refuzat cu numele lui, înainte de orice pornire.

**Limite:**
- Aplicația nu include Volatility și nu descarcă simboluri. Administratorul aduce unealta și simbolurile pe un mediu aprobat.
- Verificarea acoperă folderul uneltei și folderul simbolurilor. Ce extrage la pornire un executabil împachetat (PyInstaller) provine din fișierul verificat și ajunge într-un folder temporar privat.
- Volatility rămâne o unealtă terță: ieșirea ei este citită, nu verificată independent.
- Reziduuri acceptate (revizuire de securitate, 2 runde, 2026-11-08):
  - O rulare Volatility mai lungă de 10 minute poate apărea într-un control ulterior al stației ca program străin. Fereastra de acțiune a auditului sigilat este de 10 minute. Avertismentul este fals pozitiv, nu o lipsă.
  - La ieșirea normală, un jurnal de erori cu o linie foarte lungă se scrie linie cu linie.

