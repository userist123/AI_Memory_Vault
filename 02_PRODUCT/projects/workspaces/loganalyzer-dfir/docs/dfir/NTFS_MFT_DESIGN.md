# NTFS: $MFT și $I30 — proiectarea fazei (2026-11-13)

Stare la început: `AntiForensics.cs` emite AF08 (timestomp) mereu „Nedeterminat” pentru că $MFT nu este parsat;
`LogAnalyzer.Infrastructure/Parsers/MftParser.cs` este UNWIRED și calculează doar SHA-256. Nu există niciun cititor
de înregistrări NTFS în Dfir.Core / Dfir.Windows. Faza construiește unul nou, gestionat, fără `unsafe`, în
`LogAnalyzer.Dfir.Core/FileSystem/Ntfs/`, și nu construiește peste stub-ul din Infrastructure.

## Sarcini (o implementare odată; Sonnet execută, Opus verifică)

| # | Ce | Semnal de reușită | Review Opus |
|---|---|---|---|
| S1 | Cititor NTFS pur: boot sector, fixup (USA), antet FILE, iterare de atribute, $SI, $FN, $ATTRIBUTE_LIST, data runs | teste sintetice + trunchiere la fiecare decalaj + `Corpus/Ntfs/mft.bin` egal cu `truth.json` | obligatoriu (input ostil) |
| S2 | `MftParser` (Dfir.Core) pe fișier $MFT exportat, înregistrat în registru, `ParserCapabilities` | pe corpus: căi, mărimi, ore $SI și $FN egale cu `truth.json`; cele 20 de fișiere șterse găsite | obligatoriu |
| S3 | Colector: $MFT citit brut din volumul unei singure copii de umbră (boot sector → înregistrarea 0 → runs), SHA-256 | pe VM: copii de umbră 0 → 0, mărimea = $DATA din înregistrarea 0, zero erori de fixup pe înregistrările în uz | obligatoriu (mâner brut) |
| S4 | Reguli: AF08 cu verdict real + regulă gradată de timestomp | `stomped.txt` detectat; mutare / copiere / arhivă fără High | obligatoriu (falsuri pozitive) |
| S5 | $I30: INDX + intrări din slack, clasate față de MFT | pe corpus: nume șterse recuperate, niciun fișier viu clasat șters | obligatoriu |

## Politica de emitere (decizie de proiectare)

Un $MFT real are sute de mii până la milioane de înregistrări; nu toate devin evenimente.

1. Căile se construiesc dintr-un index al **directoarelor** (referință + secvență → nume, părinte), pas separat,
   cu verificarea secvenței părintelui (o referință cu altă secvență = părinte reutilizat → calea începe cu „…\”
   și se marchează) și cu oprire la cicluri și la adâncimea 64.
2. Se emit:
   - câte un eveniment pentru fiecare înregistrare **ștearsă** (nefolosită) care încă are un $FILE_NAME citibil,
     cel mult 20.000 (cele mai noi după ora $FN de modificare a intrării); peste plafon, un rezumat cu numărul;
   - câte un eveniment pentru fiecare înregistrare **în uz** cu nepotrivire $SI/$FN (semnalele din S4), cel mult
     5.000, cu toate cele 8 ore în câmpuri;
   - câte un eveniment pentru fiecare fișier în uz creat sau modificat ($SI) în cele 30 de zile dinaintea
     `EvidenceItem.AcquiredAtUtc` (fără dată de achiziție: nimic din această categorie, cu o notă), cel mult 100.000;
   - un eveniment-rezumat: înregistrări totale, în uz, șterse, directoare, erori de fixup, mărimea înregistrării.
3. Erorile de fixup și înregistrările stricate se numără (`MalformedRecords`), nu opresc parsarea; un fișier
   trunchiat dă PARTIAL. Niciun câmp citit din input nu dimensionează o alocare fără plafon.
4. Orele sunt FILETIME UTC; o oră imposibilă (în afara 1980–2100) se păstrează brut și nu se interpretează.

## Ce nu face această fază

$LogFile; ADS, $EA, $Bitmap, $Secure; recuperarea conținutului șters; carving pe tot volumul; ReFS / FAT; imagini
E01 / VHDX; un ecran nou în aplicație; reguli care tratează o singură nepotrivire $SI/$FN ca dovadă (High doar cu
semnale independente, vezi S4).
