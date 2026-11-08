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
