# Corpus de calibrare pentru timestomp (Windows 11, 2026-11-13)

Volum VHDX de 64 MB, NTFS, creat de `make_calib.ps1` pe stația de dezvoltare (Windows 11). Jurnalul USN a fost pornit
(`fsutil usn createjournal`) ÎNAINTE de orice operație, deci fiecare operație are rândurile ei. Nu conține date ale
stației (fișiere „TEST” create de script); scanat după identificatorii privați (ASCII, UTF-16, UTF-8, SID binar): 0.

| Fișier | Ce este |
|---|---|
| `mft.bin` | `$MFT:$DATA`, 256 × 1.024 octeți, copiat după extinderile raportate de Windows |
| `boot.bin` | primul sector al volumului |
| `usn.csv` | `fsutil usn readjournal G: csv`; orele sunt LOCALE, fusul `GTB Standard Time` (în `truth.json` → `tz`), precizie de o secundă |
| `truth.json` | fiecare scenariu: cale, ID de fișier (referința MFT), orele $SI înainte/după din .NET, ce se așteaptă |
| `calib_view.py` | vizualizare independentă de C# ($SI / $FN din octeți + rândurile USN ale fiecărui fișier) |

## Ce a arătat calibrarea (observat, nu presupus)

| Scenariu | Operația | $SI creare față de $FN creare | USN | Așteptat |
|---|---|---|---|---|
| A | timestomp cu SetFileTime (fracțiune zero), operație separată | 2019 < 2026 | „Basic info change” într-o operație separată, după „File create … Close” | suspect |
| B | la fel, fracțiune nenulă | 2019 < 2026 | la fel | suspect |
| C | timestomp, apoi redenumire | **egale**: Windows copiază orele $SI în $FN la redenumire | ca A + Rename | suspect (doar prin USN) |
| D | timestomp, apoi mutare pe același volum | **egale** (aceeași copiere) | ca A + Rename | suspect (doar prin USN) |
| E | doar ora de creare schimbată | 2019 < 2026 | ca A | suspect |
| F | Copy-Item pe același volum | $SI creare = ora copierii | o singură operație cu „Basic info change” | legitim |
| G | Copy-Item de pe alt volum | $SI creare = ora copierii | o singură operație | legitim |
| H | robocopy /COPY:DAT de pe alt volum | 2019 < 2026, **ca la A** | operația de creare + o operație separată „Basic info change” | legitim |
| I | Move-Item de pe alt volum | $SI creare = ora mutării (nu se păstrează) | o singură operație | legitim |
| J | Expand-Archive | $SI creare = ora extragerii; doar $SI modificare = 2018 | operație separată „Basic info change” | legitim |
| K | tar -xf | $SI creare 18:29:08**.0000000** < $FN 18:29:08.4944813 (0,49 s) | o operație | legitim |

Ce le deosebește:

- H (robocopy) și G (Copy-Item) copiază și ora „schimbare MFT” a sursei: **$SI schimbare MFT < $FN creare**, ceea ce
  la un fișier creat pe loc nu se poate. `SetFileTime` nu poate seta această oră, deci A–E au $SI schimbare MFT ≥ $FN
  creare. (Unelte care folosesc NtSetInformationFile pot seta și ora de schimbare; rămân nedistinse.)
- K: diferența este sub o secundă (tar trunchiază la secundă) → o toleranță de 60 s elimină cazul.
- C și D: MFT-ul singur nu mai arată nicio nepotrivire după redenumire sau mutare; doar USN-ul (FILE_CREATE în 2026
  pentru un fișier cu $SI creare 2019) îl arată.
- Rămâne nedistins: un arhivator care restaurează și ora de creare (7-Zip cu `-mtc`, WinRAR cu ore de creare salvate)
  produce tiparul lui A. Netestat aici.
