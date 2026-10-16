# LogAnalyzer — ghid de utilizare

Ghidul este pentru oamenii care fac controale pe stații și pentru cei care sprijină răspunsul la incidente, **fără pregătire IT**.
Textele în ghilimele „…” sunt exact cele de pe ecran. Codurile tehnice (evenimente Windows, chei de registru etc.) sunt explicate în
[Anexa: verificările și codurile](ANEXA_VERIFICARI_SI_CODURI.md), generată din aceleași texte pe care le arată aplicația.

> Stare: versiune de lucru, 2026-10-11. Capturile de ecran se adaugă după aprobarea designului de către proprietar.

---

## 1. Ce face aplicația, pe scurt

- **Citește** stația: conturi, setări de securitate, jurnalele Windows. Controlul **nu modifică nimic** pe stație.
- **Judecă** ce a găsit după reguli și după procedurile organizației și spune pentru fiecare verificare: CONFORM, NECONFORM, DE VERIFICAT sau NEDETERMINAT.
- **Explică** fiecare rezultat pe înțeles: ce s-a verificat, de ce contează, cum, ce s-a găsit și ce aveți de făcut.
- **Produce** raportul pentru procesul-verbal (PDF), cu amprenta lui (SHA-256), ca să nu poată fi schimbat fără să se observe.

Pe ecran apare doar **numele stației** (de exemplu `1234DOC5143SV` sau `NETWS01`); felul în care lucrează stația îl stabilește administratorul.

## 2. Înainte de a începe

1. **Porniți aplicația ca administrator**: clic dreapta pe LogAnalyzer → „Executare ca administrator”.
   *De ce:* fără drepturi de administrator, Windows nu lasă citirea jurnalului Security și a politicii de audit, iar multe verificări ies NEDETERMINAT.
2. **Prima pornire pe o stație.** Aplicația cere crearea administratorului principal al aplicației: nume și prenume, cont, parolă și „Confirmare parolă”. Țineți parola într-un loc sigur.
3. **Autentificarea** se face cu cardul în slotul tastaturii: apăsați „Autentificare cu card” și introduceți PIN-ul în fereastra Windows / SafeNet (aplicația nu vede și nu păstrează PIN-ul). Administratorul principal se poate autentifica și cu cont și parolă.

## 3. Ecranul „Acasă”

Sus vedeți numele stației și întrebarea **„Ce doriți să faceți?”**. Dedesubt e o listă scurtă de acțiuni; fiecare rând spune într-o propoziție ce face. Restul acțiunilor sunt sub **„Alte acțiuni”**.

Pe stația de control, principalele acțiuni sunt:

| Acțiune | Ce face |
|---|---|
| „Verifică această stație” | Rulează controlul pe perioada aleasă. Aplicația doar citește. |
| „Verifică un suport (USB, CD/DVD)” | Ce suporturi au fost conectate și dacă sunt în registrul de medii. |
| „Cine a lucrat și când” | Conturi, autentificări locale și de la distanță, sesiuni cu drepturi de administrator, cu dovada fiecăreia. |
| „Deschide un control anterior” | Un control salvat mai devreme; probele se reverifică la deschidere. |
| „Raport pentru proces-verbal” | PDF-ul controlului, salvat în caz cu amprentă SHA-256. |

Dacă o acțiune nu este disponibilă pe stația dumneavoastră, la clic aplicația spune **de ce** (de exemplu „necesită modul conectat; această stație este izolată”).

## 4. Controlul unei stații, pas cu pas

### 4.1 Cei trei pași

Apăsați **„Verifică această stație”**. Pe ecran apare o singură întrebare odată; mergeți cu **„Înainte”**, reveniți cu **„Înapoi”** sau renunțați cu **„Oprește”**.

1. **„Ce stație?”**: numele stației și ce înseamnă (de exemplu cod unitate, compartiment, număr de înregistrare). Bifați „Stația trebuie să fie izolată (fără rețea)” dacă este o stație izolată; atunci orice conectare la rețea iese NECONFORM.
2. **„Ce perioadă?”**: „De la ultimul control”, „Ultimele 30 de zile”, „Ultimele 90 de zile” sau „Interval ales de mine”. Fără un control anterior, aplicația verifică 90 de zile și vă spune asta.
3. **„Ce proceduri?”**: secțiunile profilului de proceduri după care se judecă stația. O secțiune nedefinită nu poate da CONFORM. Tot aici completați cine controlează, funcția, numărul de înregistrare și marcajul raportului.

La ultimul pas butonul devine **„Pornește controlul”**. Controlul durează de la câteva secunde la câteva minute.

### 4.2 Rezultatul

Sus apare fișa rezultatului, cu patru răspunsuri: „Există o problemă?”, „Cât de grav?”, „Sunt probele de încredere?”, „Ce s-a găsit?”. Dedesubt, **„Ce urmează”** listează pașii recomandați. Tot aici sunt butoanele:

- **„Raport pentru proces-verbal (PDF)”**;
- **„Compară cu controlul anterior”**: ce s-a schimbat de la ultimul control;
- **„Deschide folderul rapoartelor”**;
- **„Control nou”**.

### 4.3 Verificările: listă în stânga, explicație în dreapta

În fila **„Verificări”** fiecare rând este o verificare (codul ei, de exemplu `P04`, titlul și ce s-a găsit). Prima verificare este deja aleasă; **dați clic pe oricare rând** și în dreapta apare:

- rezultatul și ce înseamnă (de exemplu „NECONFORM: ce s-a găsit încalcă regula…”);
- **Ce verificăm**, **De ce contează**, **Cum a verificat aplicația**;
- **Ce s-a găsit** și **Cum se citește rezultatul**;
- **Ce faceți**: pasul concret;
- **Codurile care apar, pe înțeles**: fiecare cod din probe (de exemplu „Security 1102”, „S-1-5-32-544”, „Services\USBSTOR\Start”) cu ce este, de ce contează și ce înseamnă valoarea;
- **Probele**: înregistrările exacte pe care se bazează rezultatul, cu jurnalul, numărul înregistrării și ora.

Butonul **„Copiază explicația (pentru procesul-verbal)”** copiază totul ca text, gata de lipit în document.

Cu lista **„Filtru după rezultat”** vedeți doar NECONFORM, doar DE VERIFICAT etc.

### 4.4 Utilizatori și cronologie

- **„Utilizatori”**: pentru fiecare persoană, autentificările la tastatură, cele de la distanță (RDP), eșecurile, sesiunile cu drepturi de administrator, prima și ultima activitate.
- **„Cronologia acțiunilor”**: cine a făcut ce și când (conturi create, jurnale șterse, suporturi conectate, programe instalate). La clic pe un rând, dedesubt apare ce înseamnă codul din coloana „Probă”.

### 4.5 Raportul pentru procesul-verbal

Apăsați **„Raport pentru proces-verbal (PDF)”**. **Prima dată** pe o stație se deschide fereastra **„Scopul cazului stației”**: scop, aprobator, sisteme în scop, perioada, temei, categoria sistemului, clasificare, observații. Completați-o o singură dată; fără ea raportul nu se salvează, iar aplicația vă spune asta.

Raportul se salvează în cazul stației, cu amprenta lui; vi se oferă și salvarea unei copii unde doriți (originalul rămâne în caz). Raportul conține verificările, pe ce se bazează fiecare și golurile de probă (sursele care nu au putut fi citite și ce înseamnă asta pentru control).

## 5. Cum citiți rezultatele

| Rezultat | Ce înseamnă | Ce faceți |
|---|---|---|
| **CONFORM** | Ce s-a verificat corespunde regulii. | Nimic. |
| **NECONFORM** | Ce s-a găsit încalcă regula. | Se trece în procesul-verbal și se cere remedierea (vezi „Ce faceți” în explicație). |
| **DE VERIFICAT** | Aplicația arată faptele, dar doar organizația poate spune dacă erau aprobate. | Comparați cu documentele: registre, aprobări, liste de software. |
| **NEDETERMINAT** | Sursa necesară nu a putut fi citită. Nu înseamnă nici bine, nici rău. | Reporniți aplicația ca administrator și rulați din nou; dacă rămâne așa, notați că sursa lipsește. |

**Important:** lipsa urmelor nu dovedește că un lucru nu s-a întâmplat. De aceea, de exemplu, „niciun stick USB găsit” apare DE VERIFICAT, nu CONFORM: jurnalele au o istorie limitată.

## 6. Mediile de stocare și dispozitivele

Pagina **„Medii de stocare și dispozitive”** (din meniu, „Registru medii”) urmărește tot ce poate stoca sau transfera date: discuri interne, stick-uri și discuri externe, carduri, CD/DVD, telefoane conectate prin cablu, dispozitive Bluetooth, unități de rețea și discuri virtuale (ISO/VHD).

- **„Acum”**: ce este conectat în acest moment și, dedesubt, tot ce s-a conectat sau s-a scos de la pornirea aplicației, cu ora. Dacă se conectează un suport care **nu este în registru**, pe orice pagină apare sus un mesaj roșu, cu un sunet: „Vezi dispozitivul” vă duce la el, „Am văzut” închide mesajul.
- **„Toate dispozitivele”**: fiecare dispozitiv cunoscut de stație, inclusiv cele conectate în trecut. La clic, în dreapta apar toate datele lui, fiecare cu explicația: ce este, dacă e în registru, ce faceți, producător, model, serie, VID:PID, capacitate, literă, etichetă (descifrată după convenția `xxxx-yyyyL`), criptare, prima și ultima conectare, ultima deconectare, istoricul conectărilor din jurnal, sursele. „Copiază datele” le pune gata de lipit în procesul-verbal.
- **„Protecțiile Windows”**: dacă Windows însuși blochează stick-urile, CD/DVD-urile sau telefoanele, dacă cere stick-uri criptate și dacă **înregistrează copierea fișierelor** pe suporturi. Dacă nu o înregistrează, controlul nu poate spune ce fișiere au ieșit, iar pagina spune asta.
- **„Registrul de medii”**: registrul propriu-zis (vezi mai jos).
- **Registrul de transferuri** nu mai este o filă a acestei pagini: are pagina lui, „Registrul de transferuri”, lângă „Registru medii” în meniu. Citește din registrul de medii și din mediile conectate acum, fără să deschideți întâi pagina de medii. Din „Medii de stocare” îl deschideți cu butonul „Deschide registrul de transferuri”.

Aplicația **nu blochează** dispozitive; pentru blocare se folosesc politicile Windows (GPO) sau un produs dedicat. Windows păstrează în fișa unui dispozitiv doar **ultima** conectare; istoricul complet vine din jurnal și are o durată limitată.

### 6.1 Istoricul mediilor și confruntarea între stații

Ofițerul de securitate urmărește un mediu de transfer pe toate stațiile:

1. **Pe fiecare stație**, după control, apăsați **„Exportă istoricul mediilor pentru ofițer”** (pagina de control, lângă „Copiază jurnalul pentru CD-R”). Ofițerul, prezent, introduce cardul și semnează; exportul (mediile văzute, conectările, fișierele scrise sau citite de pe ele, rândurile din registrul de transferuri, versiunea registrului semnat, codul de ancorare) se scrie pe mediul ofițerului. Exportul se trece întâi în jurnalul aplicației.
2. **Pe stația ofițerului**, „Medii de stocare” → **„Confruntare între stații”**: alegeți perioada, apăsați „Importă exporturile stațiilor…” și selectați toate fișierele. Un export modificat, nesemnat sau semnat de altcineva decât un ofițer de pe listă este **respins**.
3. Tabelul arată, în ordinea gravității: transferurile confirmate (mediul la sursă, la destinație, fișierele pe destinație), stațiile în care mediul a fost introdus fără un transfer aprobat (cu fișierele văzute acolo), mediile care nu sunt în registrul semnat, posibilele clonări ale seriei, stațiile cu registru învechit și stațiile care nu au predat exportul.
4. **„Exportă raportul semnat”** scrie raportul PDF semnat cu cardul și tabelul CSV.

Raportul spune mereu ce nu poate arăta: stațiile fără export, exporturile mai vechi decât perioada, mediile fără serie, stațiile fără auditarea fișierelor.

## 7. Registrele de utilizatori și de medii

Registrele **nu se completează de mână**. Apăsați **„Preia de pe stație”**: aplicația adaugă conturile găsite pe stație, respectiv suporturile conectate vreodată (cu producător, model, serie). Dumneavoastră completați doar ce stația nu poate ști (autorizații, persoana responsabilă).

- **Eticheta unui suport** are forma `xxxx-yyyyL`: `xxxx` = cifre (codul), `yyyy` = numărul de înregistrare, `L`/`LL`/`LLL` = nivelul de clasificare (litere).
- **Numele stației izolate**, de exemplu `1234DOC5143SV`: `1234` = cod, `DOC` = compartimentul sau rețeaua, `5143` = numărul de înregistrare, `SV` = clasificarea.
- **Numele stației din rețea**, de exemplu `NETWS01`: `NET` = rețeaua, `WS` = stație de lucru, `01` = numărul.

Doar administratorul aplicației poate salva registrele; ceilalți le pot citi, iar ecranul spune asta.

## 8. Politicile (import GPO, verificare, aplicare)

Ecranul **„Politici”** spune mereu, sus, **pasul următor** și de ce, cu un singur buton care îl face:

1. „Deschide politica…”: fișierul primit (de exemplu un backup GPO arhivat) sau folderul lui;
2. „Înregistrează politica”: se păstrează amprenta ei, ca nimeni să nu o schimbe pe ascuns;
3. „Validează”: fiecare regulă este completă;
4. „Rulare de probă”: se citește fiecare setare de pe stație, fără nicio modificare;
5. **aprobarea**: o face **altă persoană, din alt cont Windows**, cu o justificare scrisă (aplicația spune cine și nu arată butonul celui care a înregistrat politica);
6. „Calculează planul”: exact ce s-ar schimba;
7. „Aplică planul…”: cu confirmare, iar fiecare setare se recitește după scriere (doar în ediția neclasificată);
8. „Verifică conformitatea”.

### 8.1 Ce face fiecare setare: listă în stânga, explicație în dreapta

Ținând mouse-ul pe un rând din plan apare, pe un rând, ce face setarea. La clic pe rând, în dreapta apare explicația completă, pe înțeles:

- **Ce face** setarea, fără termeni tehnici;
- **De ce contează pe o stație izolată** care tratează informații clasificate;
- **Valoarea recomandată**. Unde valoarea o hotărăște organizația (de exemplu durata blocării contului), scrie „valoarea cerută de procedura dumneavoastră (profilul de proceduri)”, iar dedesubt apare valoarea pe care o cere politica deschisă; unde există un reper Microsoft, acesta este menționat ca reper, nu ca obligație;
- **Ce înseamnă valoarea de acum** a stației (de exemplu „0: contul nu se blochează niciodată”);
- **Unde se setează**: calea din editorul de politici de grup (în engleză, între ghilimele, cum apare în Windows) și, pentru IT, locul tehnic (cheia de registru, comanda auditpol sau „net accounts”).

Butonul „Copiază explicația (pentru procesul-verbal)” pune tot textul în clipboard. Catalogul cuprinde politica de parole și de blocare a conturilor, toate categoriile auditului avansat, serviciile cunoscute și valorile de registru folosite de obicei pe o stație izolată (UAC, ecran, suporturi amovibile, instalarea dispozitivelor, AutoPlay, BitLocker, firewall, Defender, PowerShell, Recall, Copilot, cameră, microfon, ceas). O setare din afara catalogului este spusă cinstit: aplicația arată ce fel de setare este și unde se află, dar nu îi ghicește semnificația; titlul controlului din politică o descrie.

Elementele pe care aplicația nu le poate citi din politică sunt listate în fila „Neacceptate la import”.

## 9. Când ceva nu merge

| Ce vedeți | Ce înseamnă | Ce faceți |
|---|---|---|
| Multe verificări NEDETERMINAT | Aplicația nu are drepturi de administrator. | Închideți și porniți cu „Executare ca administrator”. |
| „Nu s-a salvat nimic: … trebuie completat o dată scopul …” | Ați închis fereastra scopului fără să o completați. | Apăsați din nou butonul și completați fereastra. |
| Un buton spune „Ca să … , întâi …” | Acțiunea are nevoie de un pas anterior. | Faceți pasul spus în mesaj. |
| O acțiune de pe „Acasă” spune că nu e disponibilă | Ediția sau modul stației nu o permit. | Motivul este scris în mesaj; nu este o eroare. |
| Un cod din probe nu are explicație | Codul nu este încă în catalogul aplicației; nu i se inventează un sens. | Arătați înregistrarea unui specialist IT. |

---

*Ghidul se actualizează odată cu aplicația. Anexa se regenerează automat; acest text se revizuiește la fiecare schimbare de ecran (testul `UserGuideAppendixTests` și scriptul de utilizabilitate `docs/dfir/WP18_USABILITY_SCRIPT.md`).*
