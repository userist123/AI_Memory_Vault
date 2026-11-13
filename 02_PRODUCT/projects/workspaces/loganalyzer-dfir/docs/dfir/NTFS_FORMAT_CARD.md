# NTFS — fișa de format pentru parserul $MFT / $I30 (2026-11-13)

Cercetare pe surse primare, verificată de sesiunea principală pe `LogAnalyzer.Dfir.Tests/Corpus/Ntfs` (vezi „Confirmat
pe corpus”). Decalaje în octeți, little-endian. Surse: [LF] libfsntfs „New Technologies File System (NTFS).asciidoc”
(github.com/libyal/libfsntfs, documentation/); [MS] Microsoft Learn devnotes: file-record-segment-header,
multi-sector-header, attribute-record-header, mft-segment-reference; [MS-4K] blogul NTDebugging „NTFS and 4K disks”;
[FC] flatcap.github.io/linux-ntfs (data_runs, index_record); [HS] harelsegev.github.io „I30 parsers output false
entries”; [VR] docs.velociraptor.app forensic/filesystem/ntfs; [IC] inversecos.com timestomping (2022); [SANS] Hull
2010 „detecting time stamp manipulation”; [HC] windowsir.blogspot.com 2014-07; [KB] Microsoft KB172190 (tunneling).

## Confirmat pe corpus (volum Windows 11, 512 B/sector, 8 sectoare/cluster)

OEM „NTFS    ”, 0x55AA la 510; 0x40 = 0xF6 (int8 −10) → înregistrare de 1.024 octeți; MFT LCN 0x154A; USA offset
0x30, count 3; ultimul u16 din fiecare sector de 512 = USN (USA[0]); înregistrările 0, 5, 50 au numărul propriu la
+44; 17 înregistrări FILE libere pentru 20 de fișiere șterse (3 reutilizate de fișiere create după ștergere).

## 1. Boot sector
0x0B u16 bytes/sector (256–4096, putere a lui 2); 0x0D u8 sectors/cluster (0–128 literal, 244–255 = 2^(256−n));
0x28 u64 total sectors; 0x30 u64 MFT LCN; 0x38 u64 MFTMirr LCN; 0x40 u8 clusters per MFT record (0–127 clustere;
citit ca int8 negativ = 2^|n| octeți); 0x44 u8 clusters per index record (aceeași codificare); 0x48 u64 serial.
Validări: record size putere a lui 2 în 256..65536 și ≥ bytes/sector; |n| ≤ 31; MFT LCN în interiorul volumului.
Mărimea înregistrării se citește, nu se presupune (1.024 pe 512n/512e, 4.096 pe 4Kn [MS-4K]).

## 2. Antet FILE
0 „FILE” („BAAD” = marcat stricat de chkdsk); 4 u16 USA offset; 6 u16 USA count în USHORT-uri cu USN inclus
(= recordSize/512 + 1; [MS] spune greșit „bytes”); 8 u64 LSN; 16 u16 sequence; 18 u16 link count; 20 u16 primul
atribut; 22 u16 flags (0x1 în uz, 0x2 director, 0x4 $Extend, 0x8 view index); 24 u32 used; 28 u32 allocated; 32 u64
base reference (0 = înregistrare de bază); 40 u16 next attribute id; 44 u32 numărul înregistrării (doar 3.1).
Referință = 48 de biți număr + 16 biți sequence; validă doar dacă sequence = sequence-ul înregistrării țintă.
Fixup: pas fix de 512 octeți indiferent de mărimea sectorului; întâi se verifică TOATE pozițiile i·512−2 = USA[0],
apoi se restaurează din USA[i]. Nepotrivire = scriere ruptă: înregistrarea se păstrează marcată, nu e sursă de
încredere. Validează offset + 2·count ≤ 510 și count = size/512 + 1.

## 3. Antet de atribut
+0 u32 type (0xFFFFFFFF = sfârșit); +4 u32 lungime (multiplu de 8, > 0); +8 u8 form (0 rezident, 1 nerezident);
+9 u8 name length (caractere); +10 u16 name offset; +12 u16 flags (0x00FF comprimat, 0x4000 criptat, 0x8000 sparse);
+14 u16 instance. Rezident: +16 u32 value length, +20 u16 value offset. Nerezident: +16 u64 LowestVCN, +24 u64
HighestVCN, +32 u16 mapping pairs offset, +34 u16 compression unit, +40 i64 allocated, +48 i64 file size, +56 i64
valid data length, +64 i64 total allocated (doar comprimat). Mărimile sunt valide doar când LowestVCN = 0.
Bucla de atribute se oprește la 0xFFFFFFFF, la used size sau la allocated size, care vine primul.

## 4. Data runs
Octet antet = v + 16·l (v = octeți lungime, low nibble; l = octeți offset, high nibble). Lungime v octeți (> 0);
offset l octeți cu semn, relativ la LCN-ul run-ului anterior (primul relativ la 0). l = 0 → run sparse, LCN-ul
curent NU se actualizează. Terminator 0x00; octeții după el nu sunt neapărat zero. Validări: v ∈ 1..8, l ∈ 0..8,
LCN ≥ 0 și < total clusters, suma lungimilor = HighestVCN − LowestVCN + 1, număr de run-uri plafonat.

## 5. $STANDARD_INFORMATION (0x10)
+0 creation, +8 modification, +16 MFT entry modified, +24 last access (FILETIME); +32 u32 attributes; 3.0+: +48 owner
id, +52 security id, +56 quota, +64 USN. Mărime 48 sau 72.

## 6. $FILE_NAME (0x30)
+0 u64 parent reference; +8 creation, +16 modification, +24 MFT modified, +32 access; +40 allocated, +48 real size
(pot fi inexacte; mărimea reală vine din $DATA); +56 u32 flags; +60 u32 reparse/EA; +64 u8 name length (caractere);
+65 u8 namespace (0 POSIX, 1 Win32, 2 DOS, 3 Win32&DOS; > 3 invalid); +66 nume UTF-16LE (surogate nepereche legale:
decodare fără excepție). Mai multe $FN pe fișier (hard links, perechea DOS/Win32).

## 7. $ATTRIBUTE_LIST (0x20)
Intrare: +0 type, +4 u16 lungime, +6 u8 name length, +7 u8 name offset, +8 u64 starting VCN, +16 u64 referința
înregistrării care conține atributul, +24 u16 attribute id, apoi numele, aliniere 8. Poate fi rezidentă sau nu,
chiar în înregistrarea 0. Segmentele unui atribut se concatenează în ordinea VCN.

## 8. $INDEX_ROOT (0x90), $INDEX_ALLOCATION (0xA0), $I30
INDEX_ROOT: +0 tipul indexat (0x30), +4 collation, +8 index record size, +12 clusters per index record, apoi node
header. Node header: +0 offset intrări (relativ la node header), +4 used, +8 allocated, +12 flags (0x1 nod cu copii).
INDX: +0 „INDX”, +4 USA offset, +6 USA count, +8 LSN, +16 VCN, +24 node header, fixup ca la FILE (pas 512).
Intrare: +0 u64 file reference, +8 u16 lungime intrare, +10 u16 lungime cheie, +12 flags (0x1 are copil = VCN în
ultimii 8 octeți ai intrării, 0x2 ultima intrare fără cheie), +16 cheia = structura $FILE_NAME; aliniere 8.
Slack (între used și allocated) poate conține intrări vechi, fără semnătură, parțial suprascrise. O intrare din slack
NU dovedește ștergerea: ștergerile și reechilibrarea arborelui lasă copii ale intrărilor vii [HS]. Se clasează față
de $MFT pe referință + sequence: „copie a unei intrări vii” / „nume istoric, înregistrare reutilizată” / „nume
istoric, înregistrare liberă”.

## 9. Timestomp (euristică gradată, nu standard publicat)
Legitim: mutare între volume și instalatoare moștenesc ore [SANS]; copiere pe același volum: $SI M păstrează ora
originalului, restul primesc ora copierii → $SI M < $FN B normal [HC, Win7]; mutare pe același volum: doar $SI C și
părintele $FN se schimbă [HC]; redenumirea copiază orele $SI în $FN [VR] (de verificat pe Win11); arhivatoarele
setează ore vechi, $SI < $FN nesigur [VR]; tunneling 15 s moștenește creation time [KB].
Unelte: Metasploit timestomp, Cobalt Strike, Timestomp.exe, SetFileTime / NtSetInformationFile → doar $SI [IC];
SetMACE (driver) poate scrie și $FN; „stomp apoi mutare/redenumire” propagă falsul în $FN [IC][VR].
Semnale: slab — fracțiune .0000000; $SI M < $FN B singur; $SI B < $FN B pe fișiere din arhive. Mediu — $SI B < $FN B
la executabil în cale de utilizator, cu fracțiune zero. Puternic — coroborare: USN FILE_CREATE / RENAME după $SI B,
$SI B înainte de instalarea sistemului, $SI diferit de orele din intrarea $I30 a părintelui. Concluzia doar din
coroborare.

**Calibrat pe Windows 11 (2026-11-13, `LogAnalyzer.Dfir.Tests/Corpus/NtfsCalib`, observat direct):** redenumirea și
mutarea pe același volum COPIAZĂ orele $SI în $FN (un timestomp urmat de redenumire nu mai lasă nepotrivire în MFT;
doar USN-ul îl arată); robocopy /COPY:DAT și Copy-Item copiază și ora „schimbare MFT” a sursei ($SI MftModified <
$FN Created), pe care SetFileTime nu o poate seta; robocopy reproduce exact tiparul $SI creare < $FN creare; tar
trunchiază la secundă (diferență < 1 s); Move-Item între volume NU păstrează ora de creare. Timestomp-ul apare în USN
ca „Basic info change” într-o operație separată, după „File create … Close”. Detalii: README-ul corpusului.

## 10. Copii de umbră
Calea robustă: citire brută a dispozitivului \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN → boot sector →
înregistrarea 0 → runs ale $MFT (+ $ATTRIBUTE_LIST) [VR]. Deschiderea „…ShadowCopyN\$MFT” ca fișier: neverificat.

## Capcane pentru input ostil
Fiecare offset (atribut, nume, valoare, mapping pairs, intrări de index) în limite și cu avans strict (lungime 0 =
buclă infinită); cicluri în liste de atribute, sub-noduri de index și lanțul de părinți (set de vizitate + adâncime);
sequence nepotrivit = referință învechită, marcată; name length × 2 în buffer; FILETIME 0 / negativ / după 9999 cu
conversie sigură; fixup nepotrivit și „BAAD” raportate, nu excepție; nicio alocare dimensionată din input fără plafon.
