# Corpus $LogFile (volum de test scris de Windows 11, 2026-11-14)

Volum VHDX de 64 MB, NTFS, creat de `make_logcalib.ps1` pe stația de dezvoltare: jurnal USN pornit, apoi 9 operații cunoscute
(`ops.json`: fel, cale, oră UTC, ID de fișier — toate luate din Windows), imediat după formatare, ca jurnalul circular să nu se fi rotit.
Nu conține date ale stației (fișiere „TEST”); scanat după identificatorii privați (ASCII, UTF-16, SID binar al contului): 0.

| Fișier | Ce este |
|---|---|
| `logfile.bin.gz` | `$LogFile:$DATA` (2.097.152 octeți necomprimați): 2 pagini RSTR, 46 de pagini RCRD folosite, restul goale. `fsutil file queryextents` refuză `$LogFile` (Access denied), deci a fost copiat de `extract_logfile.ps1`, un decodor PowerShell separat de codul C# (boot → înregistrarea MFT 2 → run-uri), cu volumul montat doar-citire |
| `mft.bin`, `boot.bin` | `$MFT` și sectorul de boot ale aceluiași volum, după operații |
| `usn.csv` | `fsutil usn readjournal` (ore locale, fus `GTB Standard Time`) |
| `ops.json` | operațiile: mkdir `\work`; create `created.txt` (rezident); create `stomp.txt`; timestomp doar al orei de creare → 2019-03-14T09:26:53Z; create `before.txt`, redenumit `after.txt`; create `deleted.txt`, șters; create `big.bin` (200.000 octeți) — care reutilizează înregistrarea MFT a lui `deleted.txt` (secvența 2) |
| `g0_usnscan.py` | scanare independentă a imaginilor USN_RECORD_V2 din paginile jurnalului (pe acest volum: 33, toate în primele 2 părți ale fișierului) |

Măsurat pe LAB-WS01 (Windows 11, `$LogFile` de 64 MiB citit brut din copia de umbră, 2026-11-14; nu e în repo): 16.382 de pagini RCRD,
14.267 de imagini USN_RECORD_V2 cu ore între 05:36:44 și 10:25:05 UTC = fereastră de 4,8 ore pe un VM de laborator mai mult inactiv.
