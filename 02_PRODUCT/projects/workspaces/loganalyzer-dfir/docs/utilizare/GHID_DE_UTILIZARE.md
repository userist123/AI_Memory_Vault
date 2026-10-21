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

### 4.6 Colectarea pentru investigație: ce aduce profilul „Complet”

În ecranul de investigație, profilul de colectare „Complet (+ hive-uri utilizatori, Coș $I)” aduce, pe lângă ce aduce „Standard”, urmele de activitate ale **fiecărui profil de utilizator** de pe stație. Fișierele utilizatorilor doar se citesc; singura urmă lăsată pe stație este copia de umbră temporară, pe care o creează și o elimină `esentutl` (aplicația verifică după colectare că nu a rămas niciuna și raportează dacă a rămas). Este nevoie de drepturi de administrator: fără ele aplicația scrie că nu a colectat nimic și de ce.

- **NTUSER.DAT** și **UsrClass.dat** (`AppData\Local\Microsoft\Windows`) ale fiecărui profil din lista de profiluri a Windows (cheia `ProfileList`): se copiază dintr-o copie de umbră (VSS) cu `esentutl /vss`, la fel ca Amcache și SRUM. Profilurile de serviciu (S-1-5-18, 19, 20) se iau doar dacă au hive. Jurnalele de tranzacții ale hive-urilor (`.LOG1`, `.LOG2`) nu se copiază.
- **Coșul de reciclare**: fișierele `$I` din `$Recycle.Bin\<SID>` de pe fiecare volum fix (ora ștergerii, calea inițială, dimensiunea). Pentru fiecare `$I` se notează doar **dacă fișierul `$R` (conținutul) exista și ce dimensiune avea** în momentul colectării; **conținutul `$R` nu se copiază niciodată**.
- Fiecare copie primește amprenta SHA-256 în momentul colectării, iar în notele probei apar SID-ul și profilul.
- Ce nu s-a putut lua apare ca lacună, cu motivul: lipsa drepturilor, copia de umbră indisponibilă sau eșuată, folderul profilului lipsă sau necitibil, hive lipsă, Coș necitibil, limite atinse (cel mult 200 de profiluri, 10 000 de fișiere `$I` pentru un SID și 20 000 în total, un `$I` de cel mult 128 KiB). Legăturile (joncțiuni, legături simbolice) din calea profilului sau din Coș nu se urmează, iar folderele al căror nume nu este un SID se omit cu o notă.

### 4.7 „Activitatea utilizatorului”: ce arată fila și ce înseamnă orele

În pagina **Investigație**, fila **„Activitatea utilizatorului”** listează urmele pe care Windows le păstrează în profilul fiecărui utilizator și în Coșul de reciclare: **ShellBags** (foldere afișate în Explorer), **RecentDocs** (documente recente), **Office MRU** (fișiere deschise recent în Word, Excel etc.) și **Coșul de reciclare ($I)** (elemente șterse). Ele apar doar dacă investigația a folosit profilul de colectare „Complet” (vezi 4.6) sau dacă ați importat hive-urile și fișierele `$I`; fără ele fila spune că nu există nicio urmă, ceea ce **nu dovedește** că nu s-a întâmplat nimic.

Coloanele sunt: **Relevanță** (folder clasificat din profilul de proceduri, volum amovibil sau „—”), **Sursă**, **Cale**, **Ora**, **Ce înseamnă ora**, **Utilizator / SID** (profilul, sau SID-ul proprietarului pentru Coș), **Detalii** și **Probă**. Rândurile din foldere clasificate sau de pe volume amovibile sunt primele. Deasupra tabelului sunt numărătorile și explicația orelor.

**Ce înseamnă ora, pe sursă (nu este niciodată „ora accesului”):**

- **ShellBags**: ultima actualizare a intrării folderului de către Explorer (ora cheii BagMRU). Nu este prima deschidere și nu dovedește că un fișier a fost citit, doar că Explorer a afișat folderul. Doar cea mai nouă intrare din cheie are ora cheii; celelalte apar „fără oră”. Orele din interiorul elementului (data DOS, rezoluție 2 secunde, ora locală fără fus) sunt ale folderului însuși și apar doar la Detalii.
- **RecentDocs**: ora cheii, valabilă doar pentru cea mai nouă intrare. RecentDocs păstrează numai numele documentului, nu calea întreagă, deci nu poate fi legat de un folder.
- **Office MRU**: ultima deschidere a fișierului de către aplicația Office (UTC).
- **Coș de reciclare**: ora ștergerii (UTC), calea inițială și dimensiunea. Dacă fișierul `$R` lipsește, textul spune „conținut golit sau eliminat”: asta nu înseamnă că elementul nu a existat. Ștergerea definitivă (Shift+Del) nu lasă urme în Coș.

**La redeschiderea unui caz:** folderele clasificate ale profilului folosit, literele suporturilor amovibile confirmate de stație și câmpurile specifice parserelor se salvează în caz (`Analysis/user_activity.json`). Un caz mai vechi, fără acest fișier, arată „neevaluabil la redeschidere: profilul și literele confirmate nu au fost salvate cu acest caz”; rulați din nou investigația pentru a-l evalua.

**Limită cunoscută la potrivirea cu folderele clasificate:** ShellBags păstrează căile ca Explorer le-a arătat („Documente\Clasificat”, „Desktop\…”, „Descărcări\…”, „Rețea\server\share\…”). Aplicația le rezolvă la calea absolută (de ex. `C:\Users\x\Documents\Clasificat`, `\\server\share`) numai dacă folderul profilului utilizatorului este cunoscut (nota probei „Profil <SID> (<cale>)” sau calea originală a hive-ului). Un folder cunoscut redirecționat (OneDrive, alt disc) este rezolvat la locul implicit din profil, deci poate să nu se potrivească; unde calea nu poate fi rezolvată, un folder clasificat poate lipsi din rezultat (fals negativ). RecentDocs păstrează doar numele și nu se potrivește niciodată cu un folder. Numele scurte 8.3 și legăturile simbolice nu sunt rezolvate.

**Regulile care citesc aceste urme** (apar la Constatări și în fila „Integritate air-gap”): `USER-ACTIVITY-CLASSIFIED` (un folder clasificat apare în ShellBags sau Office MRU), `USER-ACTIVITY-REMOVABLE` (căi pe litera unui suport amovibil din istoricul dispozitivelor din caz, sau doar „posibil amovibilă” pentru alte litere decât C:, cu incertitudinea scrisă) și `USER-ACTIVITY-DELETED` (elemente din Coș șterse dintr-un folder clasificat sau de pe un volum amovibil). Sunt urme slabe și indirecte: rezultatul este **DE VERIFICAT**, niciodată NECONFORM din ele singure, iar severitatea este provizorie, o stabilește proprietarul procedurii. Legătura cu un suport se face după **litera** unității, nu după seria volumului; o literă poate fi reutilizată.

**În raport:** raportul investigației are, după constatări, un paragraf „Activitatea utilizatorului” (numărători, cele mai relevante urme, cu ora și ce înseamnă ea). În raportul de control, secțiunea 3b arată același paragraf doar când controlul primește o investigație a aceleiași stații; altfel spune că activitatea utilizatorului nu a fost evaluată de acest raport. **Nu sunt încă făcute:** Zone.Identifier (Mark-of-the-Web) și copierea jurnalelor de tranzacții ale hive-urilor (`.LOG1`, `.LOG2`).

## 5. Cum citiți rezultatele

| Rezultat | Ce înseamnă | Ce faceți |
|---|---|---|
| **CONFORM** | Ce s-a verificat corespunde regulii. | Nimic. |
| **NECONFORM** | Ce s-a găsit încalcă regula. | Se trece în procesul-verbal și se cere remedierea (vezi „Ce faceți” în explicație). |
| **DE VERIFICAT** | Aplicația arată faptele, dar doar organizația poate spune dacă erau aprobate. | Comparați cu documentele: registre, aprobări, liste de software. |
| **NEDETERMINAT** | Sursa necesară nu a putut fi citită. Nu înseamnă nici bine, nici rău. | Reporniți aplicația ca administrator și rulați din nou; dacă rămâne așa, notați că sursa lipsește. |

**Important:** lipsa urmelor nu dovedește că un lucru nu s-a întâmplat. De aceea, de exemplu, „niciun stick USB găsit” apare DE VERIFICAT, nu CONFORM: jurnalele au o istorie limitată.

## 6. Mediile de stocare și dispozitivele

Pagina **„Medii de stocare și dispozitive”** (din meniu, „Monitor dispozitive”) urmărește mediile de stocare și dispozitivele conectate extern (stick-uri și discuri externe, carduri, CD/DVD, telefoane, camere, scanere, imprimante, adaptoare de rețea sau Wi-Fi USB, dispozitive Bluetooth, tastaturi și mouse-uri externe, cititoare de carduri); filele „Acum” și „Toate dispozitivele” arată și restul mediilor cunoscute de stație (discuri interne, unități de rețea, discuri virtuale ISO/VHD).

- **„Monitor în timp real”** (prima filă): pornește singur cu aplicația, fără niciun buton. Arată **doar mediile de stocare și dispozitivele conectate extern**: stick USB, card de memorie, disc extern, unitate optică, telefon sau tabletă (MTP, PTP, ADB), cameră foto/video, scaner, imprimantă, adaptor de rețea, Wi-Fi sau modem USB, dispozitiv Bluetooth, tastatură și mouse externe, cititor de carduri (cardul introdus are rândul lui) și cititor de carduri smart. Nu apar hub-urile și controlerele USB, discurile interne, camera, tastatura și touchpad-ul integrate, radioul Bluetooth, adaptoarele virtuale (Hyper-V, VPN) și celelalte componente interne; un telefon care apare în Windows ca mai multe dispozitive este un singur rând. Orice dispozitiv conectat sau scos apare imediat în listă. Cercul din rând este **verde** cât timp dispozitivul este conectat și **roșu** după ce a fost scos; rândul rămâne, și după repornirea aplicației (jurnalul monitorului se păstrează în `%ProgramData%\LogAnalyzer\device_monitor`, comun tuturor conturilor Windows ale stației). Coloanele sunt, în ordine: **Stare** (cerc verde sau roșu și starea scrisă alături: **Conectat**, **Deconectat**, **Nu funcționează (fără driver)**, **Dezactivat**; nu există coloane de driver), **Nume**, **Tip**, **Producător**, **Model**, **VID**, **PID**, **Serie** (sau „fără serie unică” când Windows a inventat-o), **Capacitate** (întâi cum o arată Windows Explorer, apoi cum scrie pe etichetă, de exemplu „14,9 GB (16 GB pe etichetă)”), **Literă** și **Etichetă** (ultimele trei doar pentru mediile de stocare), **Utilizator** (contul conectat la consolă în momentul conectării, „—” când nu era nimeni), **Prima apariție**, **Ultima conectare**, **Ultima deconectare** (ora locală a stației, zz.LL.aaaa oo:mm:ss), **Nr. de înregistrare**, **Registru** (**Înregistrat** sau **Neînregistrat**) și **Alertă**. Coloanele se sortează cu un clic pe antet; ordinea inițială este conectatele întâi, apoi cea mai recentă conectare. Butonul **Istoric** de pe fiecare rând deschide o fereastră cu evenimentele dispozitivului **din jurnalul sigilat** (Data și ora, Eveniment, Utilizator, Detalii): prima apariție, conectări, deconectări, schimbări ale numărului de înregistrare (vechi → nou, cu operatorul), alerte și schimbări de stare; ce nu a putut fi sigilat încă este marcat „în așteptare (nesigilat)”. Butonul **Export CSV** salvează lista afișată, în ordinea sortării curente, într-un fișier CSV (UTF-8, separator „;”, se deschide direct în Excel); în jurnalul sigilat se trece nota „device.export_csv” cu operatorul, numărul de rânduri și amprenta SHA-256 a fișierului (nu și calea). Un card dintr-un cititor este recunoscut după seria volumului și capacitate: același card introdus de mai multe ori este același rând, își păstrează numărul și, odată înregistrat, nu mai dă alertă. O aceeași serie cu altă capacitate sau alt producător apare ca rând separat marcat „serie duplicată – posibilă clonă” și dă alertă critică. În coloana **„Nr. de înregistrare”** scrieți numărul fiecărui mediu de stocare (se poate și pentru orice alt dispozitiv); fiecare schimbare se trece în jurnalul de audit al aplicației, cu valoarea veche, cea nouă, operatorul și ora. La fiecare conectare apare sus, pe orice pagină, un mesaj: albastru pentru informare, **roșu cu sunet** pentru un mediu de stocare fără număr de înregistrare, o serie dublă sau, pe o stație izolată, un telefon, un dispozitiv Bluetooth, un adaptor Wi-Fi, un modem sau un adaptor de rețea USB. Același dispozitiv și același fel de alertă dau cel mult un mesaj la 10 minute, iar la peste 20 de mesaje pe minut restul se rețin; pagina arată „N alerte suprimate”, dar fiecare alertă (cele critice mereu) rămâne în jurnalul monitorului și în jurnalul de audit sigilat. LogAnalyzer cere mereu drepturi de administrator la pornire (pe un cont standard, Windows cere parola unui administrator). Dacă totuși rulează fără ele, jurnalul sigilat nu poate fi scris: pagina arată sus, cât timp durează, un banner roșu „Monitorul nu poate sigila evenimentele: porniți LogAnalyzer ca administrator. Până atunci D05 rămâne DE VERIFICAT.”, iar raportul listează dovada „monitorul a rulat fără drept de sigilare”. Dacă radioul Bluetooth este oprit sau absent sau adaptorul Wi-Fi este dezactivat, pagina și raportul spun „Bluetooth oprit – dispozitivele Bluetooth nu pot fi observate” (la fel pentru Wi-Fi), nu „niciun dispozitiv”. Monitorul își trece în jurnalul de audit pornirea, un puls la 15 minute, oprirea și intervalele pierdute (eroare la evenimente, stația în repaus); după un interval pierdut citește din nou tot și marchează ce a apărut sau a dispărut „observat la reluare”. Raportul de control are secțiunea „3c” și verificarea D05 cu aceleași dispozitive, acoperirea monitorului (calculată numai din jurnalul de audit sigilat) și alertele date.
- **„Acum”**: ce este conectat în acest moment și, dedesubt, tot ce s-a conectat sau s-a scos de la pornirea aplicației, cu ora. Dacă se conectează un suport care **nu este în registru**, pe orice pagină apare sus un mesaj roșu, cu un sunet: „Vezi dispozitivul” vă duce la el, „Am văzut” închide mesajul.
- **„Toate dispozitivele”**: fiecare dispozitiv cunoscut de stație, inclusiv cele conectate în trecut. La clic, în dreapta apar toate datele lui, fiecare cu explicația: ce este, dacă e în registru, ce faceți, producător, model, serie, VID:PID, capacitate, literă, etichetă (descifrată după convenția `xxxx-yyyyL`), criptare, prima și ultima conectare, ultima deconectare, istoricul conectărilor din jurnal, sursele. „Copiază datele” le pune gata de lipit în procesul-verbal.
- **„Protecțiile Windows”**: dacă Windows însuși blochează stick-urile, CD/DVD-urile sau telefoanele, dacă cere stick-uri criptate și dacă **înregistrează copierea fișierelor** pe suporturi. Dacă nu o înregistrează, controlul nu poate spune ce fișiere au ieșit, iar pagina spune asta.
- **„Registrul de medii”** și **„Registrul semnat”** nu mai apar pe această stație (decizia din 2026-10-16): registrul semnat al mediilor nu este folosit, iar verificarea D04 apare „Nu se aplică”. Numărul de înregistrare al fiecărui mediu se scrie în monitor.
- **Registrul de transferuri** nu mai este o filă a acestei pagini: are pagina lui, „Registrul de transferuri”, lângă „Monitor dispozitive” în meniu. Citește din registrul de medii și din mediile conectate acum, fără să deschideți întâi pagina de medii. Din „Medii de stocare” îl deschideți cu butonul „Deschide registrul de transferuri”.

Aplicația **nu blochează** dispozitive; pentru blocare se folosesc politicile Windows (GPO) sau un produs dedicat. Windows păstrează în fișa unui dispozitiv doar **ultima** conectare; istoricul complet vine din jurnal și are o durată limitată.

### 6.1 Istoricul mediilor și confruntarea între stații

Ofițerul de securitate urmărește un mediu de transfer pe toate stațiile:

1. **Pe fiecare stație**, după control, apăsați **„Exportă istoricul mediilor pentru ofițer”** (pagina de control, lângă „Copiază jurnalul pentru CD-R”). Ofițerul, prezent, introduce cardul și semnează; exportul (mediile văzute, conectările, fișierele scrise sau citite de pe ele, rândurile din registrul de transferuri, versiunea registrului semnat, codul de ancorare) se scrie pe mediul ofițerului. Exportul se trece întâi în jurnalul aplicației.
2. **Pe stația ofițerului**, „Medii de stocare” → **„Confruntare între stații”**: alegeți perioada, apăsați „Importă exporturile stațiilor…” și selectați toate fișierele. Un export modificat, nesemnat sau semnat de altcineva decât un ofițer de pe listă este **respins**. Opțional, scrieți codurile de ancorare de pe procesele-verbale ale stațiilor, câte unul pe rând (`STAȚIE = COD`): un cod diferit de cel din export arată că jurnalul aplicației stației a fost rescris. Fiecare export aduce și indicatorii de integritate ai stației (A05, A11, A12, A13, A16, A17, S05; un export mai vechi, fără A17, apare cu A17 „lipsă”, adică nu este socotit curat); dacă unul nu este CONFORM, tabelul spune că istoricul acelei stații poate fi incomplet. Atenție: indicatorii de integritate sunt calculați de stația verificată însăși, la export; un administrator care oprește serviciul de jurnale și șterge înregistrări individuale poate lăsa indicatorii curați; codul de ancorare tastat de pe procesul-verbal dovedește doar că jurnalul aplicației nu a fost rescris, nu că istoricul dispozitivelor din Windows este complet. Un cod scris pentru o stație al cărei nume nu se regăsește în exporturi apare ca „cod de pe procesul-verbal pentru o stație necunoscută” — verificați numele.
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
