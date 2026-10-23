# Pornire la rece ca începător — LogAnalyzer Standalone (2026-10-22)

Aplicația reală instalată (`C:\Program Files\LogAnalyzer`, build dbc39de92), pornită de la zero, cu contul de test al
administratorului principal. Am parcurs fiecare ecran ca un utilizator care deschide aplicația prima oară și nu știe
nimic despre DFIR. Capturile de ecran au rămas doar în scratchpad-ul sesiunii (nu se publică).

Gravitate: **B** = blochează sau induce în eroare un începător, **C** = îl încurcă, **D** = finisaj.

## 1. Ce vede și unde se blochează

| # | Ecran | Ce vede un începător | Gravitate |
|---|---|---|---|
| 1 | Autentificare | Fereastra s-a deschis în spatele altor ferestre (pornire dintr-un proces fără prim-plan; de reverificat la dublu-click manual). | D |
| 2 | Acasă | Trei butoane care par să facă același lucru: „Colectează tot” (antet), „Colectează acum” (banner), „Fă controlul stației”. Plus cardul „Verifică această stație”, care duce tot la control. Nu scrie nicăieri care e diferența. | **B** |
| 3 | Acasă | Primul câmp de pe ecran e „Codul de ancorare de pe procesul-verbal anterior”: termen necunoscut, cerut înainte de orice altceva. | **B** |
| 4 | Acasă / Control | „jurnalul sigilat”, „postura ediției”, „Numele stației nu urmează convenția”: termeni fără explicație; „jurnal sigilat”, „air-gap”, „caz LIVE”, „ediția Standalone” lipsesc din glosarul „Ce înseamnă?”. | C |
| 5 | Acasă vs Control | Două ore diferite pentru „ultimul control”: Acasă 22.10.2026 21:44 (ultimul control terminat, din jurnalul sigilat), Control „cel mai recent: 2026-10-22 20:54” (ultimul raport salvat). Același cuvânt, două sensuri; și formate de dată diferite. | C |
| 6 | Control stație | Bannerul „Nu există încă date” stă lângă „14 controale anterioare”. | C |
| 7 | Toate | Meniul și titlul paginii au nume diferite: „Control stație” → „Verifică această stație”; „Monitor dispozitive” → „Medii de stocare și dispozitive”; „Politici” → „Politici — import, aprobare, aplicare verificată”. | C |
| 8 | Toate | Bara de stare spune pe fiecare ecran „Izolare automată: nu este disponibilă în ediția Standalone” (mesaj negativ, fără acțiune posibilă) și „Nicio analiză încă.” deși există cazuri recente. | C |
| 9 | Toate | Butoanele din stânga jos „Încarcă un folder de probe / Încarcă jurnale Windows / Exportă tabelul (CSV)” apar și pe Acasă, unde nu există tabel. | C |
| 10 | Acasă, Control | Conținutul ocupă ~30% din lățime la 3440 px; restul ecranului e gol. | D |
| 11 | Monitor | **O cască Bluetooth apare ca ~12 rânduri** (WH-CH720N, Avrcp Transport ×2, Hands-Free AG, GSOUND_*, Airoha_*, BTFASTPAIR, SPP Serial Port, Generic Attribute Profile…), fiecare roșu „clasă interzisă”. Regula owner-ului „un rând = un dispozitiv fizic” nu se aplică profilurilor/serviciilor Bluetooth. Un începător vede 25 de alarme roșii pentru ~3 dispozitive. | **B** |
| 12 | Monitor | Coloana „Registru: Neînregistrat” și „Nr. de înregistrare” apar și la mouse/tastatură/cască, deși numărul se cere doar pentru medii de stocare. | C |
| 13 | Monitor | Paragraful explicativ de sus are 5 rânduri dense; filele „Monitor în timp real” și „Acum” nu se deosebesc pentru un începător. | C |
| 14 | Registrul de transferuri | SSD-ul extern apare „Stick / disc USB”; datele din grilă și din lista roșie sunt ISO („2026-10-16 18:08”), nu dd.MM.yyyy; lista „Conectări fără transfer înregistrat” e o cutie mică cu ultimul rând tăiat. | C |
| 15 | Registrul de transferuri | „Martor (regula celor patru ochi)”, „Sursă (structura)” fără explicație. Date de test vizibile (două transferuri de probă) — intră în curățenia dinaintea predării. | C (curățenie) |
| 16 | Registru utilizatori | Subtitlu cu literă mică („editare permisă…”); textul de ajutor păstrează numele englezești în paranteză (Person, Accounts, Sids, Clearance, NeedToKnow, ZonesAllowed); „registru nedefinit” de trei ori; primul câmp e o cale `C:\ProgramData\…`. | C |
| 17 | Profil de proceduri | 12 file goale, toate „nedefinit”; ajutorul spune „Ziua (Day): Luni..Duminică (sau Mon..Sun); Începutul și sfârșitul (Start/End)”. Nu scrie cu ce să înceapă începătorul. | C |
| 18 | Politici | Subtitlul înșiră stările cu engleza în paranteză și lipsește spațiul după punct („…(VERIFIED).Aprobarea”); depozitul e în `%LOCALAPPDATA%`, celelalte registre în `C:\ProgramData`. | D |
| 19 | Autentificare și conturi | „Cont nou”: două căsuțe fără etichetă vizibilă (au doar nume accesibil). Numele persoanei contului principal pare o valoare de test (de verificat la curățenie). Textul CRL: „ediția Standalone refuză, cea neclasificată avertizează”. Un buton e tăiat la marginea de jos. | C |
| 20 | Meniu „Avansat” | Deschide 22 de pagini care dublează cele 8 principale (Control stație, Monitor, Politici… apar de două ori); lista intră sub butoanele din stânga jos; „Tehnici de atac (MITRE ATT&CK)” e tăiat la „ATT&C” fără „…”. | C |
| 21 | Investigație | Deja notat (J/K): două formulare paralele (asistent „Pasul 1 din 3” + formularul complet) și panouri gri goale înainte de rulare. | **B** |

## 2. Cum ar trebui să arate ca să fie înțeleasă de un începător

Principii consacrate (euristicile Nielsen: „potrivire cu limbajul utilizatorului”, „consecvență”, „recunoaștere în loc de
amintire”; dezvăluire progresivă; stări goale care spun ce faci mai departe), aplicate la aplicația aceasta:

1. **Un singur drum principal pe Acasă.** Un buton mare „Verifică această stație” (control) și, dedesubt, „Investighează un
   incident”. „Colectează tot/acum” dispar din antet și banner în nivelul Simplu (rămân în Expert). Fiecare buton are o
   propoziție „ce se întâmplă când apăs”.
2. **Codul de ancorare devine pas opțional, nu primul câmp.** În asistentul de control, pasul 2: „Aveți procesul-verbal al
   controlului anterior? Introduceți codul de pe el (opțional) — de ce contează”. Pe Acasă nu mai apare.
3. **Un nume pentru fiecare lucru.** Meniul = titlul paginii = textul din carduri. Un „ultim control” definit o singură dată
   (propunere: ultimul control terminat), afișat la fel pe Acasă și pe Control, mereu dd.MM.yyyy HH:mm.
4. **Stări goale care învață.** Pe Registru utilizatori, Profil de proceduri, Politici: în loc de tabel gol + „nedefinit”,
   un panou „Ce este pagina asta / de ce contează pentru control / primul pas: [buton]”, cu ce verificări rămân NEDETERMINAT
   cât timp pagina e goală. Căile de fișier trec în „Avansat”.
5. **Prima pornire ghidată** (o dată, se poate sări): 3 ecrane — ce face aplicația (doar citește), ce faceți întâi
   (control → raport → proces-verbal), unde găsiți „Ce înseamnă?”.
6. **Monitor: un dispozitiv fizic = un rând și pentru Bluetooth**; serviciile/profilurile pliate sub dispozitivul lor; o
   singură alarmă per dispozitiv; coloanele de înregistrare doar pentru medii de stocare.
7. **Bara de stare doar cu ce se poate face ceva**; mesajul despre izolarea automată mutat în pagina „Ediție/despre”.
8. **Meniul Avansat fără dubluri** (numai paginile care nu sunt deja în „Pagini principale”), cu text întreg sau „…” + tooltip.
9. **Glosar completat** cu: jurnal sigilat, cod de ancorare (dacă lipsește la căutare), air-gap, caz LIVE, ediție/Standalone,
   martor (patru ochi), structură, postura ediției; termenii din pagină legați direct de glosar (clic → explicație).
10. **Fără engleză în nivelul Simplu** (inclusiv parantezele cu numele de câmp); engleza rămâne în Expert.

## 3. Decizii pentru owner

- Ordinea de lucru: propun întâi B (2, 3, 11, 21), apoi C, apoi D.
- Punctul 1 al propunerilor (ascunderea „Colectează tot/acum” în Simplu) și punctul 5 (prima pornire ghidată) schimbă felul
  în care arată aplicația: cer acordul owner-ului.
- J/K (Investigație): păstrăm asistentul și mutăm formularul complet în Expert, sau invers?
- Ce înseamnă „ultimul control” (punctul 5 din tabel): ultimul control terminat sau ultimul raport salvat?
