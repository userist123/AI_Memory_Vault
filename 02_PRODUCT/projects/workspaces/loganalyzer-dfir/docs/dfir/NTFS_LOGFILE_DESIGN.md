# NTFS $LogFile — proiectarea fazei (2026-11-14)

## De ce și ce nu

`$LogFile` (jurnalul tranzacțional NTFS) se rotește de regulă MAI REPEDE decât jurnalul USN, deci nu „vede mai departe în timp”.
Valoarea lui este alta: (A) imaginile înregistrărilor USN scrise în `$UsnJrnl:$J` rămân în jurnal și după `fsutil usn deletejournal`;
(B) actualizările lui `$STANDARD_INFORMATION` au valoarea veche (undo) și cea nouă (redo) — USN-ul nu le are (neconfirmat încă pe un
eșantion propriu: de verificat pe corpus înainte de felia B).

Înregistrările `$LogFile` NU au oră proprie: ordinea este după LSN, ora vine doar din conținut (USN, $SI). Niciun High doar din `$LogFile`;
„nu apare în `$LogFile`” nu înseamnă „nu s-a întâmplat” — fereastra acoperită (cea mai veche și cea mai nouă oră datată) se afișează mereu.

## Poarta G0 (măsurată, 2026-11-14)

Prag fixat înainte de măsurare: fereastra mediană ≥ 2 h pe stația de laborator, altfel felia semantică se amână în favoarea SRUM.
Prima estimare (din LSN-ul din antetul FILE și ora $SI) a fost INVALIDĂ: 45% inversiuni de oră între vecinii după LSN. Măsurare directă:
`$LogFile` de 64 MiB al LAB-WS01, citit brut din copia de umbră → 14.267 de imagini USN_RECORD_V2, 05:36:44–10:25:05 UTC = 4,8 h. Trecut.

## Sarcini (consilier Fable + decizia sesiunii principale)

| # | Ce | Semnal | Review Opus |
|---|---|---|---|
| L1 | AF07: jurnal USN absent pe volumul de sistem (1179) = semnal distinct; crearea jurnalului ancorată de crearea volumului / instalare, nu doar de EVTX; regulă pe linia de comandă `fsutil usn deletejournal` / `createjournal` (4688, Sysmon 1, 4104) | pe un VM: deletejournal + createjournal → Detected cu ora corectă; stație neatinsă → NotDetected; instalare proaspătă → nu Detected | obligatoriu (falsuri pozitive) |
| L2 | Conservare: `$LogFile` (înregistrarea 2, `$DATA` fără nume) copiat din ACEEAȘI copie de umbră în NtfsMetadataCollector, CÂT MAI DEVREME în colectare, SHA-256, manifest, plafon | mărime = $DATA, RSTR în paginile 0 și 1, copii 0 → 0 | obligatoriu (mâner brut) |
| L3 | Parser structural pur (Dfir.Core): restart area (ambele copii, versiunea 1.1/2.0, seq_number_bits = 67 − biții mărimii), pagini RCRD cu fixup, pagini tail (v2.0: blocurile 2..33), LSN → offset, reasamblare peste pagini și peste wrap, înregistrări „stale” respinse și numărate; emite doar un rezumat (interval LSN, numărători pe cod de operație, pagini nesigure, înregistrări incomplete) | teste sintetice + trunchiere la fiecare decalaj; pe corpus: fiecare LSN din antetele FILE ale `$MFT` din același volum, aflat în intervalul jurnalului, este găsit, cu ținta = numărul înregistrării | obligatoriu |
| L4 | Felia A: imaginile USN din redo-ul scrierilor în `$J` → evenimente USN marcate „din $LogFile” | identice câmp cu câmp cu USN-ul citit de Windows pe fereastra comună; după `deletejournal` pe VHDX cele dinainte se regăsesc | obligatoriu |
| L5 | Felia B: undo/redo $SI → câmp de coroborare pentru NTFS-TIMESTOMP | `stomp.txt` din corpus: undo = ora dinainte, redo = 2019; copierea / mutarea / arhivele nu potrivesc | obligatoriu (gradarea regulii) |

## Ce nu se face în această fază

Redo/undo complet, reconstrucția stării, tabelele de tranzacții / pagini murdare; versiunea 1.1 fără eșantion (se raportează „versiune
neacoperită”); `$LogFile` în pachetul la distanță; carving pe volum; ore inventate; uneltele terțe (LogFileParser, NTFS Log Tracker) ca
poartă de acceptare (sunt alte implementări ale aceluiași format nedocumentat: doar coroborare).

Surse: `ntfs-3g` include/ntfs-3g/logfile.h, libntfs-3g/logfile.c, ntfsprogs/ntfsrecover.c (structuri, LSN → bloc, pagini tail, reasamblare);
Flatcap linux-ntfs `$LogFile` (semantica câmpurilor și a operațiilor); libfsntfs (doar antetul paginii de restart).
