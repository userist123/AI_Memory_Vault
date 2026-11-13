# Corpus NTFS (volum de test scris de Windows)

Proveniență: volum VHDX fix de 64 MB, GPT, formatat NTFS de Windows 11 pe stația de dezvoltare, 2026-11-13, cu
`make_fixture.ps1` (din acest folder). Numele scurte 8.3 au fost pornite doar pe acest volum. Octeții au fost copiați
din volum cu `extract.ps1`, după extinderile raportate de Windows (`fsutil file queryextents`), deci copierea nu
depinde de codul LogAnalyzer care decodează data runs.

Nu conține date ale stației: fișierele sunt create de script, cu text „TEST”. Binarele au fost scanate după
identificatorii privați ai stației (ASCII, UTF-16 și SID-ul binar al contului): 0 potriviri. `$Secure` nu este inclus.

| Fișier | Ce este |
|---|---|
| `mft.bin` | `$MFT:$DATA` întreg, 262.144 octeți = 256 înregistrări de 1.024 octeți, o singură extindere (LCN 0x154A) |
| `many_i30.bin` | `\many:$I30:$INDEX_ALLOCATION`, 3 clustere de 4.096 octeți (LCN 0x5D4), înregistrări INDX |
| `boot.bin` | primul sector al volumului (512 octeți) |
| `truth.json` | adevărul de referință, luat din Windows (fsutil, Get-Item), nu din LogAnalyzer |
| `layout_*.txt`, `ntfsinfo.txt` | ieșirea `fsutil file layout` / `fsutil fsinfo ntfsinfo` (ore $SI, ID-uri de fișier, mărimi) |

Scenarii (toate cu ID de fișier = referință MFT în `truth.json`):

1. Fișiere vii: `\docs\small.txt` (date rezidente), `\docs\big.bin` (300.000 octeți, nerezident), un nume cu
   diacritice, un nume lung care primește și un nume DOS 8.3.
2. `\many` cu 60 de fișiere `file_001.txt` … `file_060.txt`; cele divizibile cu 3 (20) au fost șterse
   (`truth.deleted`): înregistrări MFT libere și intrări rămase în slack-ul INDX.
3. Redenumire `\docs\before_rename.txt` → `\docs\after_rename.txt`, același ID de fișier.
4. Timestomp prin API-ul Win32 (`SetFileTime` din PowerShell) pe `\docs\stomped.txt`: cele trei ore $SI puse la
   2019-03-14T09:26:53Z; orele dinainte sunt în `truth.stomped.before` și sunt și orele $FILE_NAME așteptate
   ($FILE_NAME nu este schimbat de `SetFileTime`). „Change Time” ($SI, intrare MFT modificată) rămâne ora reală.
5. Mutare pe același volum `\docs\moved.txt` → `\many\moved.txt` (aceeași înregistrare, alt părinte).

Orele din `truth.json` sunt UTC, cu precizia pe care o raportează .NET (100 ns). `fsutil file layout` afișează
orele $SI rotunjite la secundă.

`volume.raw.gz`: imaginea brută întreagă a aceluiași volum (66.973.696 octeți necomprimați, gzip), citită din `\\.\F:` la
sfârșitul lui `extract.ps1`, după aceleași operații. `mft.bin` și `many_i30.bin` sunt copiate din ea după extinderile date de
Windows (`fsutil file queryextents`), deci sunt oracolul independent pentru un cititor brut (boot sector → înregistrarea 0 →
data runs). Scanată după identificatorii privați (ASCII, UTF-16, SID binar al contului): 0.
