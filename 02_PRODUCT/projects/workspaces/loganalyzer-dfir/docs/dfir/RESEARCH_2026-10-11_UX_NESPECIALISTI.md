# Research 2026-10-11 — rezultate de securitate pe înțelesul nespecialiștilor

Cerut de proprietar: cum fac produsele de succes și sistemele de design din sectorul public ca rezultatele de securitate și conformitate să fie înțelese și folosite de oameni fără pregătire IT, ce reclamă utilizatorii unor astfel de unelte și ce înseamnă asta concret pentru ecranele LogAnalyzer.

**Pentru cine.** Utilizatorul principal este controlorul fără pregătire IT, care verifică stații clasificate, izolate de rețea, și întocmește un proces-verbal oficial. Utilizatorul secundar este analistul CSIRT.

**Ce există deja (nu se repetă aici):** tema Fluent Windows 11, controlul ghidat cu o întrebare pe ecran, ecrane listă-și-detaliu cu explicații simple pentru fiecare verificare și fiecare cod, „pasul următor” pe ecranul Politici, glosar, nivelurile de limbaj Simplu și Expert, ghidul utilizatorului în română. Vezi și `RESEARCH_2026-10-11_UI_SI_POLITICI.md` (de ce o interfață pare generată de AI, decizii aprobate).

**Metodă și limite.** Am căutat pe web și am citit paginile oficiale: GOV.UK Design System, NHS Service Manual, Scottish Design System, Nielsen Norman Group, Microsoft Learn, W3C, Google Research. Pentru plângeri am folosit recenzii G2/Capterra preluate de agregatori și articole de practicieni. **Firele Reddit nu au putut fi citite direct**: ce apare despre ele vine din rezumate, deci e marcat [NESIGUR]. Ghidul Microsoft de localizare în română l-am citit dintr-o extragere de text incompletă (fișierul este PDF), așa că regulile luate de acolo care nu sunt citate exact sunt marcate [NESIGUR]. Pagina de scriere Fluent 2 nu a putut fi găsită: am folosit în locul ei ghidul Microsoft de scriere pentru aplicații Windows.

Depozitul este public: documentul nu conține identificatori ai organizației.

---

## 1. Tipare care funcționează

Fiecare tipar este descris prin: **ce este**, **când se folosește** și **sursa**.

### 1.1 Structura parcursului

**T1. Un singur lucru pe pagină (question pages).**
- *Ce:* fiecare ecran are o singură întrebare, informație sau decizie. Ecranul are obligatoriu o legătură Înapoi, un titlu care este chiar întrebarea și un buton Continuați. Indicatorul de progres este opțional și se pune doar dacă cercetarea arată că ajută. Răspunsul „Nu știu” se acceptă acolo unde este un răspuns legitim. Fiecare informație se cere o singură dată pe tot parcursul.
- *Când:* procese nefamiliare și utilizatori cu încredere scăzută. Tiparul ajută și la refacerea după o eroare, la ramificații și la salvarea progresului.
- *Surse:* https://design-system.service.gov.uk/patterns/question-pages , https://www.gov.uk/service-manual/design/form-structure , https://designnotes.blog.gov.uk/2015/07/03/one-thing-per-page/ . Sistemul scoțian permite gruparea întrebărilor strâns legate, cu o afirmație ca titlu: https://designsystem.gov.scot/patterns/question-page

**T2. Asistent pas cu pas (wizard) pentru novici, nu pentru experți.**
- *Ce:* fiecare pas are un nume descriptiv. Se vede o listă cu toți pașii și pasul curent. Există butoane explicite Înapoi și Înainte. Lucrul se poate întrerupe, salva și relua.
- *Când:* utilizatori novici, sarcini rare, pași care depind de răspunsurile anterioare. **Nu** se folosește pentru sarcini repetate des, pentru comparații între pași sau pentru experți care vor să controleze ordinea.
- *Sursă:* https://www.nngroup.com/articles/wizards/

**T3. Listă de sarcini (task list).**
- *Ce:* o pagină cu sarcinile care compun un proces lung, fiecare cu o stare. Starea de bază este Finalizat sau Incomplet. Mai pot exista „Nu se poate începe încă” (text gri, fără legătură) și „Există o problemă”. Numele sarcinilor încep cu un verb. Sarcinile se pot face în orice ordine, când e posibil. Utilizatorul poate marca singur o secțiune ca finalizată.
- *Când:* procese lungi, făcute uneori în mai multe sesiuni. Recomandarea este ca procesul să fie mai întâi simplificat. Se pornește cu cât mai puține stări.
- *Sursă:* https://design-system.service.gov.uk/patterns/complete-multiple-tasks/

**T4. Navigare pas cu pas (step-by-step).**
- *Ce:* o listă numerotată cu etapele unui parcurs de la început la sfârșit. „și” înseamnă etape făcute împreună, „sau” înseamnă alternative. Fiecare etapă are doar contextul esențial.
- *Când:* parcursuri cu ordine utilă, care strâng mai multe acțiuni. **Nu** se folosește în interiorul unui serviciu tranzacțional, unde se folosește lista de sarcini.
- *Sursă:* https://design-system.service.gov.uk/patterns/step-by-step-navigation/

**T5. Verificați răspunsurile înainte de trimitere (check answers).**
- *Ce:* o listă-rezumat de tip întrebare–răspuns, cu o legătură „Modificați” pe fiecare rând. După modificare, utilizatorul revine pe pagina de verificare, fără să refacă tot parcursul. Întrebările sărite apar ca „Nefurnizat”. Titlul spune ce are de făcut utilizatorul și că procesul nu e încheiat până la confirmare. Butonul final numește acțiunea, de exemplu „Trimiteți cererea”.
- *Când:* chiar înaintea confirmării, în tranzacții mici și medii. Tiparul reduce erorile.
- *Sursă:* https://design-system.service.gov.uk/patterns/check-answers/

**T6. Pagina de confirmare.**
- *Ce:* afișează numărul de referință, spune ce urmează și când, oferă o cale de a păstra o copie (de exemplu PDF) și dă contactul. Utilizatorii fac capturi de ecran ale acestei pagini și revin la ea.
- *Când:* la finalul oricărei tranzacții care produce un document.
- *Surse:* https://design-system.service.gov.uk/patterns/confirmation-pages , https://service-manual.nhs.uk/design-system/patterns/confirmation-page

### 1.2 Cum se arată starea și riscul

**T7. Trei stări, fiecare cu simbol și text, cu acțiune pe fiecare zonă (Windows Security, „Securitatea dintr-o privire”).**
- *Ce:* fiecare zonă are o pictogramă în trei culori. Verde cu bifă înseamnă protejat, fără acțiuni recomandate. Galben cu semnul exclamării înseamnă că există o recomandare. Roșu cu X înseamnă că ceva cere atenție imediată. Utilizatorul apasă zona marcată și ajunge direct la acțiune.
- *Când:* un rezumat de stare pentru nespecialiști. Utilizatorii Windows cunosc deja convenția, iar ea se potrivește cu tema Fluent folosită de LogAnalyzer.
- *Sursă:* https://support.microsoft.com/windows/security/windows-security/windows-security-app-overview

**T8. Recomandări personalizate, nu o listă pasivă identică pentru toți (Google Security Checkup).**
- *Ce:* starea generală, numărul de probleme de rezolvat, grupare în câteva categorii, fiecare problemă cu acțiunea ei directă. Varianta veche oferea aceeași listă pasivă tuturor.
- *Când:* când multe verificări nu se aplică tuturor. Cele care nu se aplică nu se arată.
- *Surse:* https://techcrunch.com/2017/10/16/google-revamps-its-security-checkup-feature-with-personalized-suggestions-for-your-account/ . Sursa este din 2017. Interfața actuală poate diferi [NESIGUR].

**T9. Recomandări ordonate după impact, separate pe tip (Microsoft Defender Vulnerability Management).**
- *Ce:* recomandările sunt ordonate după cât reduc expunerea. Configurările greșite sunt separate de vulnerabilități. Lista are filtre după tipul remedierii. Excepțiile se acceptă cu justificare și durată.
- *Când:* ecranul analistului (Expert), unde există multe constatări.
- *Surse:* https://learn.microsoft.com/security-exposure-management/security-recommendations , https://learn.microsoft.com/en-us/training/modules/implement-endpoint-protection-use-microsoft-defender-endpoint/7-reduce-threat-exposure

**T10. Starea „Verificare manuală”, separată de scor (CIS-CAT).**
- *Ce:* ce nu poate verifica unealta apare explicit ca „Manual”, cu procedura de verificare și remediere. Aceste elemente nu intră în procentul de conformitate.
- *Când:* orice verificare pe care aplicația nu o poate decide singură, de exemplu un sigiliu fizic sau un registru pe hârtie.
- *Sursă:* https://www.cisecurity.org/cybersecurity-tools/cis-cat-pro/cis-cat-faq

**T11. Design „cu opinie” pentru acțiunea sigură (Google, avertismentele SSL din Chrome).**
- *Ce:* textul principal fără termeni tehnici, acțiunea sigură ca buton proeminent, cea riscantă ca legătură discretă, în spatele unui „Avansat”. Comprehensiunea a crescut puțin, dar aderența a crescut mult (de la 37% la 61%, conform presei).
- *Când:* orice decizie cu o variantă sigură clară, de exemplu „Continuați fără a salva dovezile?”.
- *Surse:* https://research.google/pubs/improving-ssl-warnings-comprehension-and-adherence/ , https://www.securityweek.com/new-chrome-ssl-warning-improved-adherence-rates-google

**T12. Numere în loc de procente, termenul comun înaintea celui tehnic (NHS).**
- *Ce:* peste 6 din 10 adulți au dificultăți când conținutul include numere și statistici. Riscul se exprimă ca „1 din 100”, nu ca procent. Se scrie întâi cuvântul uzual, apoi termenul de specialitate. Ținta este o vârstă de citire de 9–11 ani. Frazele au cel mult ~20 de cuvinte. Paragrafele au ~3 fraze.
- *Când:* orice text pentru controlor.
- *Surse:* https://service-manual.nhs.uk/content/health-literacy , https://service-manual.nhs.uk/content/how-we-write . Recomandarea „1 din 100” vine dintr-un document regional NHS [NESIGUR ca normă națională].

### 1.3 Informație dezvăluită treptat, stări goale, așteptare

**T13. Dezvăluire progresivă (progressive disclosure).**
- *Ce:* primul ecran conține ce e frecvent și important. Restul se deschide la cerere, printr-o cale vizibilă și bine etichetată. **Cel mult două niveluri.** Împărțirea se face pe date despre frecvența de folosire.
- *Când:* explicații de cod, dovezi brute, căi de registry.
- *Sursă:* https://www.nngroup.com/articles/progressive-disclosure/

**T14. Starea goală care spune ce se întâmplă, învață și oferă o acțiune.**
- *Ce:* starea goală deosebește clar trei situații: se încarcă, a apărut o eroare, nu există date. Nu se afișează „Nicio înregistrare” cât timp încă se încarcă. Starea goală explică la ce servește zona și conține un buton pentru sarcina principală.
- *Când:* orice listă care poate fi goală: medii de stocare, evenimente, constatări.
- *Sursă:* https://www.nngroup.com/articles/empty-state-interface-design/

**T15. Indicator de progres cu procent pentru operații de peste ~10 secunde.**
- *Ce:* procentul arată cât s-a făcut și cât mai e. Un indicator care doar se rotește e riscant când durata variază. Bara nu stă pe loc, nu minte și nu revine la zero. La operații în serie, bara arată procesul întreg.
- *Când:* scanarea jurnalelor, colectarea de pe stație.
- *Surse:* https://www.nngroup.com/articles/progress-indicators/ , https://www.nngroup.com/articles/visibility-system-status/

### 1.4 Erori, ajutor, tabele

**T16. Mesaje de eroare specifice, lângă câmp, cu remediere.**
- *Ce:* mesajul spune ce s-a întâmplat și cum se repară. Fiecare problemă are mesajul ei; nu se folosesc „A apărut o eroare” sau „Câmp obligatoriu”. Pentru câmpuri goale se dă o instrucțiune, pentru valori greșite o descriere. Același text apare lângă câmp și în rezumatul erorilor. Răspunsurile introduse se păstrează. Nu se folosesc „vă rugăm”, „ne pare rău”, „valid/invalid” sau cuvinte dure. Codurile tehnice se ascund, cu excepția cazului în care sunt necesare diagnosticului. Gravitatea afișării corespunde impactului: dialogul modal se păstrează pentru cazurile grave.
- *Surse:* https://design-system.service.gov.uk/components/error-message/ , https://www.nngroup.com/articles/error-message-guidelines/ . Microsoft: aplicația își asumă problema și nu dă vina pe utilizator, https://learn.microsoft.com/windows/uwp/design/style/writing-style

**T17. Sfaturi la survolare (tooltip) doar ca plasă de siguranță.**
- *Ce:* conțin indicii scurte, care adaugă ceva nou. **Nu** conțin informație necesară pentru a termina sarcina, pentru că dispare. Trebuie să apară și la tastatură, nu doar la survolarea cu mouse-ul.
- *Sursă:* https://www.nngroup.com/articles/tooltip-guidelines/

**T18. Tabele de date construite pe patru sarcini.**
- *Ce:* cele patru sarcini sunt găsirea înregistrărilor, compararea, vizualizarea unui rând și acțiunea asupra lui. Tabelul permite filtrare, sortare, căutare și parcurgere vizuală. Prima coloană este un identificator lizibil, nu un cod generat. Fișele (cards) nu sunt potrivite pentru comparații.
- *Când:* inventarul de dispozitive, istoricul mediilor, evenimentele.
- *Sursă:* https://www.nngroup.com/articles/data-tables/

**T19. Ajutor contextual în locul turului inițial.**
- *Ce:* într-un studiu NN/g cu 70 de utilizatori, cei care au parcurs tutorialul din cartonașe au reușit sarcinile la fel de bine ca cei care l-au sărit (91% față de 94%), dar au perceput sarcinile ca mai grele.
- *Surse:* https://www.nngroup.com/articles/mobile-tutorials/ . Cifrele vin dintr-un rezumat secundar [NESIGUR]: https://elearningindustry.com/offer-tutorials-for-using-elearning

### 1.5 Accesibilitate

**T20. WCAG 2.2 și teme de contrast Windows.**
- *Ce aduce WCAG 2.2:*
  - 2.4.11: focusul nu este acoperit de alte elemente;
  - 2.5.8: țintele au cel puțin 24×24 px sau spațiu suficient în jur;
  - 3.2.6: ajutorul stă în același loc pe toate ecranele;
  - 3.3.7: ce s-a introdus deja nu se mai cere încă o dată;
  - 2.5.7: orice tragere cu mouse-ul are o alternativă printr-un clic.
- *Teme de contrast în WPF:* culorile vin din `SystemColors`, legate prin `DynamicResource` (cheile de resursă). Nu se folosesc culori fixe. Testarea se face cu Alt stânga + Shift stânga + Print Screen.
- *Surse:* https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/ , https://learn.microsoft.com/windows/apps/design/accessibility/high-contrast-themes , https://learn.microsoft.com/en-us/archive/blogs/wpf/systemcolors-reference

---

## 2. Anti-tipare și plângeri

**A1. Prea multe constatări, fals pozitive, oboseală la alerte.** Este cea mai frecventă plângere la scanerele de vulnerabilități.
- *Nessus:* recenzenții G2 reclamă „multe fals pozitive”, mai ales când unealta citește doar bannerul și nu vede patch-urile aplicate (https://beaglesecurity.com/blog/article/nessus-review.html , https://www.capterra.in/software/130577/nessus).
- *Analiștii SOC:* într-un sondaj IDC/FireEye, analiștii estimează că 45% din alerte sunt fals pozitive; 35% spun că ignoră alerte când coada e plină (https://www.cybersecuritydive.com/news/security-alert-analyst-SOC-idc-fireeye/595111/). Sondajul este finanțat de un furnizor și răspunsurile sunt declarative.
- *Practicieni:* când oamenii văd sute de constatări nerezolvate, le filtrează mental înainte de orice politică (https://linuxsecurity.com/news/security-projects/when-vulnerability-management-meets-compliance).

**A2. Obișnuința cu avertismentele repetate.** Atenția la un avertisment identic, văzut des, scade măsurabil (fMRI și urmărirea privirii). Un avertisment care își schimbă aspectul rezistă mai bine. Lecția pentru LogAnalyzer: dialogul de avertizare se rezervă pentru rare situații reale. Avertismentul nu se repetă la fiecare pornire. Surse: https://misq.org/tuning-out-security-warnings-a-longitudinal-examination-of-habituation-through-fmri-eye-tracking-and-field-experiments.html , https://scholarsarchive.byu.edu/facpub/1955

**A3. Interfață complexă, curbă de învățare abruptă.** Qualys: utilizatorii noi se pierd printre multe module și opțiuni, iar înțelegerea rapoartelor cere cunoștințe tehnice (https://www.capterra.in/reviews/82971/qualysguard-enterprise , https://beaglesecurity.com/blog/article/qualys-review.html). Același mesaj apare la Nessus: „complex și consumator de timp” (https://www.saasworthy.com/product/nessus/reviews).

**A4. Un scor care măsoară existența controlului, nu eficiența lui (Microsoft Secure Score).**
- Punctajul este binar: politica există, deci punctele se acordă.
- Reguli lăsate în modul „doar raportare” au ridicat scorul fără să protejeze nimic. Un studiu de caz german arată un scor de 76% cu reguli de acces condiționat care nu se aplicau la nicio autentificare.
- Recomandările sunt generice. Urmărirea scorului împinge spre acțiuni contraproductive. Mai multe surse percep scorul ca instrument de vânzare.
- Surse: https://blog.pa.com.au/it-and-security/your-secure-score-isn-t-the-full-story , https://www.hagel-it.de/it-sicherheit/secure-score-grenzen , https://blog.ise.io/blog/microsoft-secure-score-helpful-or-sales-pitch , https://blog.admindroid.com/cybersecurity-awareness-month-secure-score-in-microsoft-365/ . AdminDroid spune că se bazează pe Reddit, dar fără legături [NESIGUR].
- *Lecție:* un procent unic pe ecranul controlorului ar induce în eroare. Afirmația din procesul-verbal trebuie să spună ce s-a verificat efectiv și cum.

**A5. Scor schimbat fără ca situația reală să se schimbe.** Microsoft avertizează că noul scor de expunere nu se poate compara cu cel vechi și că o variație nu înseamnă neapărat o schimbare a posturii de securitate. Actualizarea poate întârzia până la 24 de ore. Sursa: https://www.helpnetsecurity.com/2026/06/01/microsoft-defender-exposure-score-update/ (articol de presă, neverificat în jurnalul de modificări Microsoft [NESIGUR]).

**A6. Rapoarte pentru conducere care listează volumul în loc de decizii.** Practicienii recomandă un rezumat de o pagină: riscurile principale, cine răspunde, termene. Fiecare constatare primește un răspuns în raportul următor: fals pozitiv, atenuare planificată sau risc acceptat. Surse: https://isc.sans.edu/diary/21191 , discuție Reddit citată prin rezumat [NESIGUR].

**A7. Situații „Manual” care par erori ale uneltei.** La CIS-CAT, unele recomandări marcate „Automated” în catalog apar „Manual” în raport, iar utilizatorii le iau drept defecte (https://www.cisecurity.org/cybersecurity-tools/cis-cat-pro/cis-cat-faq). *Lecție:* explicați de ce un element nu a putut fi verificat automat.

**A8. Alarmism.** Google Security Checkup a fost criticat la lansare pentru rezultate „uneori prea alarmante”, de exemplu suplimente legitime marcate ca riscante (https://techcrunch.com/2017/10/16/google-revamps-its-security-checkup-feature-with-personalized-suggestions-for-your-account/).

**A9. Tur inițial obligatoriu.** Sugerează că produsul e greu și nu îmbunătățește reușita (T19).

**A10. Informație esențială ascunsă în tooltip, culoarea ca singur purtător de sens, mesaje generice.**
- Tooltip-ul dispare, deci nu poate purta informație necesară (https://www.nngroup.com/articles/tooltip-guidelines/).
- Culoarea nu poate fi singurul indicator; e nevoie și de text sau simbol (https://www.nngroup.com/articles/error-message-guidelines/).
- Mesajele generice nu ajută (https://design-system.service.gov.uk/components/error-message/).

**A11. Opțiuni care nu fac nimic, butoane dezactivate fără explicație.** Este una dintre plângerile proprietarului. Tiparul GOV.UK pentru sarcini indisponibile este text gri, fără legătură, cu motivul afișat. Un buton care pare activ și nu face nimic nu e acceptat (https://design-system.service.gov.uk/patterns/complete-multiple-tasks/).

---

## 3. Recomandări pentru ecranele LogAnalyzer

Legendă:
- **Prioritate:** P1 = de făcut înaintea următoarei demonstrații către proprietar; P2 = următoarea iterație; P3 = îmbunătățire.
- **Efort:** S = sub o zi; M = 1–3 zile; L = peste 3 zile.

Estimările de efort sunt aproximări fără inspecția codului [NESIGUR].

### 3.1 Acasă
- **R1 (P1, S).** O singură acțiune dominantă, „Începeți controlul stației”, și sub ea starea ultimului control într-o propoziție, de exemplu: „Ultimul control: 08.10.2026, 2 probleme nerezolvate.” Fără grilă de butoane egale. *Surse:* T7; `RESEARCH_2026-10-11_UI_SI_POLITICI.md` §1.
- **R2 (P2, M).** Zona „Situația pe scurt”, după modelul Windows Security: câteva zone (Conturi, Medii de stocare, Jurnale, Politici). Fiecare zonă are pictogramă și cuvânt de stare (În regulă / De verificat / Problemă) și duce direct la lista filtrată. Culoarea nu este niciodată singurul indicator. *Surse:* T7, A10.
- **R3 (P2, S).** Acțiunile pentru analist (Expert) stau într-o zonă secundară, etichetată clar. Nu se amestecă cu acțiunea controlorului. *Sursă:* T13.

### 3.2 Controlul ghidat
- **R4 (P1, S).** Fiecare ecran are titlul „Pasul 3 din 7: Medii de stocare”, legătura Înapoi și butonul Continuați. Lista pașilor e vizibilă, cu pasul curent evidențiat. *Surse:* T1, T2.
- **R5 (P1, S).** Răspunsul „Nu pot verifica” este acceptat explicit și ajunge în procesul-verbal ca „Neverificat — motiv: …”. Nu se forțează un Da/Nu fals. *Surse:* T1, T10.
- **R6 (P1, M).** Se salvează progresul după fiecare pas, iar la repornire controlul se reia de unde a rămas. *Sursă:* T2.
- **R7 (P2, S).** Datele cunoscute nu se mai cer: numele stației citit de pe stație, numele controlorului de la ultimul control (propus, nu impus). *Sursă:* T20 (WCAG 3.3.7).
- **R8 (P2, M).** Analistul are acces direct la orice secțiune, fără asistent: listă de sarcini cu stări în loc de pași obligatorii. *Surse:* T2, T3.

### 3.3 Rezultate: lista și detaliul
- **R9 (P1, S).** Deasupra listei stă o propoziție de verdict cu numere absolute, nu procente: „3 probleme de rezolvat, 1 de verificat manual, 41 în regulă.” **Nu** se afișează scor procentual pentru controlor. *Surse:* T12, A4, A5.
- **R10 (P1, S).** Lista este ordonată implicit după gravitate. Elementele „În regulă” sunt grupate și strânse, deschise la cerere. Doar problemele reale sunt roșii. *Surse:* T9, A1, A8.
- **R11 (P1, M).** Panoul de detaliu are mereu aceeași ordine:
  1. **Ce am verificat**
  2. **Ce am găsit**
  3. **De ce contează**
  4. **Ce faceți acum** (o acțiune)
  5. **Dovada**, restrânsă: cod eveniment, cale de registry, valoare brută

  Codul tehnic apare doar ca paranteză după explicație. *Surse:* T13, T16, cererea „ce, de ce, cum, când”.
- **R12 (P1, S).** Starea „De verificat manual” are explicația „Aplicația nu poate verifica asta singură pentru că …” și procedura în pași. Nu intră în niciun total „în regulă”. *Surse:* T10, A7.
- **R13 (P2, M).** Analistul poate marca o constatare: fals pozitiv, risc acceptat (cu justificare), de remediat. Marcajul apare în raport și la controlul următor. *Surse:* T9, A6.
- **R14 (P3, S).** Avertismentele sunt diferențiate după gravitate. Dialogul modal se păstrează doar pentru ce blochează procesul-verbal. *Surse:* A2, T16.

### 3.4 Pagina dispozitivului / mediilor de stocare
- **R15 (P1, S).** Prima coloană este identificatorul lizibil: eticheta mediului sau numele producătorului și modelul. Seria și GUID-ul stau în detaliu. *Sursă:* T18.
- **R16 (P2, M).** Tabelul are filtru (de exemplu „doar neînregistrate în registru”), sortare și căutare. Verdictul față de registru apare ca text și simbol într-o coloană proprie. *Surse:* T18, A10.
- **R17 (P2, S).** Pe fiecare rând marcat se vede o propoziție cu ce trebuie făcut: „Mediul nu apare în registru. Verificați registrul pe hârtie sau consemnați în procesul-verbal.” *Surse:* T8, T16.

### 3.5 Generarea procesului-verbal
- **R18 (P1, M).** Înainte de generare apare pagina „Verificați datele procesului-verbal”: o listă-rezumat cu „Modificați” pe fiecare secțiune. Secțiunile sărite apar ca „Neverificat”. Butonul final este „Generați procesul-verbal”. *Sursă:* T5.
- **R19 (P1, S).** Pagina de confirmare arată unde s-a salvat fișierul, amprenta lui (hash) ca număr de referință, ce urmează (semnare, arhivare) și butonul „Deschideți dosarul”. *Sursă:* T6.
- **R20 (P2, S).** Procesul-verbal spune explicit ce **nu** s-a verificat și de ce. Este cea mai bună protecție împotriva unei afirmații de conformitate supraevaluate. *Surse:* A4, T10.

### 3.6 Erori și stări goale
- **R21 (P1, S).** Toate erorile urmează forma „ce s-a întâmplat + ce faceți”. Exemplu: „Jurnalul de securitate nu a putut fi citit, pentru că aplicația nu rulează ca administrator. Închideți aplicația și porniți-o cu clic dreapta → Executare ca administrator.” Detaliul tehnic (excepție, cod) stă într-o zonă „Detalii pentru specialist”. *Sursă:* T16.
- **R22 (P1, S).** Fiecare listă goală deosebește: „Se citesc datele…”, „Nu s-a găsit niciun mediu conectat din 01.09.2026” și „Citirea a eșuat: …”. Fiecare variantă are o acțiune. *Sursă:* T14.
- **R23 (P2, S).** Operațiile de peste 10 secunde arată procentul și pasul curent: „Se citesc jurnalele: 3 din 6”. Bara nu revine la zero. Există un buton de anulare. *Sursă:* T15.
- **R24 (P1, S).** Nu există butoane care nu fac nimic. Ce nu e disponibil e afișat inactiv, cu motivul scris lângă: „Disponibil după pasul Medii de stocare”. *Sursă:* A11.

### 3.7 Prima pornire
- **R25 (P2, S).** Fără tur în cartonașe. Prima pornire întreabă doar rolul (Controlor / Analist) și propune nivelul de limbaj. Ambele se pot schimba ulterior. *Surse:* T19, T1.
- **R26 (P3, M).** Un caz demonstrativ, cu date fictive, pentru instruire: controlorul exersează fără o stație reală. *Sursă:* T14 (exemplul „încărcați date demonstrative”).

### 3.8 Ajutor
- **R27 (P2, S).** Ajutorul stă în același loc pe toate ecranele. F1 deschide ajutorul pentru ecranul curent. *Sursă:* T20 (WCAG 3.2.6).
- **R28 (P2, S).** Termenii din glosar sunt legături în text. Tooltip-ul conține doar definiția scurtă și apare și la focus de tastatură. Informația necesară sarcinii stă pe ecran, nu în tooltip. *Sursă:* T17.
- **R29 (P2, M).** Verificare de accesibilitate:
  - test cu tema de contrast Windows și cu navigare doar din tastatură;
  - ținte de cel puțin 24 px;
  - focusul nu e acoperit de alte elemente;
  - culorile vin din `SystemColors`, legate prin `DynamicResource`.

  Riscul cunoscut Fluent + contrast ridicat (dotnet/wpf#10043) este descris în documentul-frate. *Surse:* T20; `RESEARCH_2026-10-11_UI_SI_POLITICI.md` §2.

---

## 4. Ghid de stil pentru textele interfeței în română

Fiecare regulă are un exemplu bun și unul rău.

**1. Formă de politețe, implicită prin verb.** Verbul se pune la persoana a II-a plural și pronumele se omite când nu e necesar. În ghidul Microsoft pentru română apare forma „pentru dvs.” (https://aka.ms/romanian-styleguide). Regula exactă dintre „dvs.” și „dumneavoastră” nu a putut fi citită [NESIGUR]. Propunere: „dumneavoastră” complet în procesul-verbal, „dvs.” acceptat doar în spații înguste.
- Bine: „Conectați mediul de stocare.”
- Rău: „Dumneavoastră trebuie să conectați mediul.” / „Conectează mediul.”

**2. Diacritice obligatorii, cu virgulă dedesubt (ș, ț: U+0219, U+021B), nu cu sedilă (ş, ţ).** Academia a adoptat forma cu virgulă, iar instituțiile publice o folosesc. Textele cu sedilă strică și căutarea, și sortarea. Surse: https://www.unicode.org/L2/L2013/13155r-cedilla-comma.pdf , https://brandient.com/kit-on-romanian-diacritics
- Bine: „Verificați setările stației.”
- Rău: „Verificati setarile statiei.” / „Verificaţi setările staţiei.”

**3. Majusculă doar la începutul titlului.** Titlurile nu au majusculă la fiecare cuvânt, ca în engleză. Ghidul Microsoft spune explicit că aceasta este o greșeală în română. Sursa: https://aka.ms/romanian-styleguide
- Bine: „Medii de stocare conectate”
- Rău: „Medii De Stocare Conectate”

**4. Fără spațiu înaintea semnelor de punctuație.** Sursa: https://aka.ms/romanian-styleguide
- Bine: „Atenție: mediul nu este înregistrat.”
- Rău: „Atenție : mediul nu este înregistrat .”

**5. Ghilimele românești „…”, iar « » doar pentru citat în citat.** Ghilimelele drepte se folosesc doar pentru cod. Sursa: https://aka.ms/romanian-styleguide . Forma exactă a ghilimelelor nu s-a putut citi din extragere [NESIGUR].
- Bine: Apăsați „Generați procesul-verbal”.
- Rău: Apăsați "Generati procesul verbal".

**6. Butoanele spun acțiunea: verb + obiect.** Sursa: https://design-system.service.gov.uk/patterns/check-answers/
- Bine: „Generați procesul-verbal”, „Salvați și continuați”
- Rău: „OK”, „Trimite”, „Procesare”

**7. Fraze scurte: în medie ~20 de cuvinte, cel mult.** Surse: https://service-manual.nhs.uk/content/how-we-write , ghidul UE „How to write clearly” (https://commission.europa.eu/resources/europa-web-guide/design-content-and-development/content/web-writing-guidelines/other-style-guides_en)
- Bine: „Contul Administrator este activ. Pe stațiile clasificate trebuie dezactivat.”
- Rău: „S-a constatat faptul că, în urma analizei efectuate asupra conturilor, contul Administrator figurează ca fiind activ, ceea ce contravine…”

**8. Cuvântul uzual întâi, termenul tehnic după.** Sursa: https://service-manual.nhs.uk/content/how-we-write
- Bine: „jurnalul de securitate al Windows (Security log)”
- Rău: „Security.evtx”

**9. Un cod tehnic nu stă niciodată singur.** Explicația vine întâi, codul în paranteză.
- Bine: „Un cont a fost blocat după prea multe parole greșite (eveniment 4740).”
- Rău: „EventID 4740 detectat.”

**10. Mesajele de eroare spun ce s-a întâmplat și ce faceți.** Sursa: https://design-system.service.gov.uk/components/error-message/
- Bine: „Fișierul nu a putut fi salvat pe unitatea E:, pentru că este protejată la scriere. Alegeți alt dosar.”
- Rău: „Eroare la salvare.”

**11. Fără cuvinte dure, fără „vă rugăm”, „ne pare rău”, „valid/invalid”.** Sursa: https://design-system.service.gov.uk/components/error-message/
- Bine: „Introduceți data controlului în forma zz.ll.aaaa.”
- Rău: „Vă rugăm introduceți o dată validă!” / „Operație ilegală.”

**12. Nu dați vina pe utilizator; aplicația își asumă problema.** Sursa: https://learn.microsoft.com/windows/uwp/design/style/writing-style
- Bine: „Aplicația nu a putut citi lista de dispozitive.”
- Rău: „Ați ales un dosar greșit.”

**13. Diateza activă, cu cine face acțiunea.** Sursa: ghidul UE „How to write clearly”, capitolele „Active, not passive” și „Use verbs, not nouns”.
- Bine: „Aplicația a găsit 3 medii neînregistrate.”
- Rău: „Au fost identificate un număr de 3 medii neînregistrate.”

**14. Verbe în loc de substantive abstracte.** Sursa: ghidul UE „How to write clearly”.
- Bine: „Verificați registrul.”
- Rău: „Efectuați verificarea registrului.”

**15. Numere absolute în loc de procente.** Sursa: https://service-manual.nhs.uk/content/health-literacy
- Bine: „3 din 45 de verificări au găsit probleme.”
- Rău: „Conformitate 93,3%.”

**16. Un singur termen pentru un lucru, peste tot.** Se aleg o dată „mediu de stocare”, „stație”, „proces-verbal” și nu se alternează cu „stick”, „PC”, „raport”. Sursa: regula de consecvență din https://design-system.service.gov.uk/components/error-message/
- Bine: „mediu de stocare” peste tot
- Rău: „stick USB” pe un ecran, „dispozitiv amovibil” pe altul, „mediu” pe al treilea

**17. Limbaj neutru ca gen.** Se evită „stimate” și terminațiile de tip „(ă)”, iar fraza se reformulează. Sursa: https://aka.ms/romanian-styleguide
- Bine: „Bun venit” / „Controlul a fost semnat de: …”
- Rău: „Stimate utilizator” / „Ați fost autentificat(ă)”

**18. Starea se scrie cu un cuvânt și o propoziție.** Etichetele de stare sunt scurte și constante, cu explicația alături. Sursa: T7.
- Bine: „De verificat — sigiliul carcasei nu poate fi verificat automat.”
- Rău: „WARN”, „Status: 2”, „Neconform?”

**19. Fără MAJUSCULE pentru accent și fără semne de exclamare.** Sursa: `RESEARCH_2026-10-11_UI_SI_POLITICI.md` §1 (semne de interfață generată), https://www.nngroup.com/articles/error-message-guidelines/ (ton calm, fără judecată).
- Bine: „Important: salvați dovezile înainte de a închide.”
- Rău: „ATENȚIE!!! NU ÎNCHIDEȚI APLICAȚIA!”

**20. Formate românești.** Data se scrie zz.ll.aaaa, ora 24h, zecimalele cu virgulă, ca în setările regionale Windows ro-RO. Formatele exacte din ghidul Microsoft nu au fost citite [NESIGUR]; de verificat cu `CultureInfo("ro-RO")`.
- Bine: „11.10.2026, 14:05”
- Rău: „10/11/2026 2:05 PM”

---

## 5. Riscuri și întrebări deschise

1. **Simplificarea poate ascunde dovezi.** Restrângerea codurilor și a dovezilor (R11) nu are voie să le scoată din procesul-verbal sau din exportul pentru analist. Nivelul Simplu schimbă prezentarea, nu conținutul probatoriu.
2. **Afirmația de conformitate supraevaluată (A4).** Orice total „în regulă” trebuie să excludă ce nu s-a verificat efectiv (R9, R12, R20). Altfel procesul-verbal poate afirma mai mult decât s-a constatat.
3. **Prea puține alerte roșii poate fi și el un risc.** Restrângerea elementelor „În regulă” (R10) nu trebuie să ascundă o problemă clasificată greșit ca minoră. Pragurile de gravitate trebuie revizuite de un analist.
4. **Sursele de plângeri sunt slabe.** Recenziile vin din agregatori, sondajele sunt finanțate de furnizori, iar Reddit nu a fost citit direct. Plângerile proprietarului, raportate cu capturi, sunt o dovadă mai bună decât acestea. Pentru validare: testul de utilizabilitate din `WP18_USABILITY_SCRIPT.md`, cu 3–5 controlori.
5. **Asistentul pas cu pas obosește la folosire repetată.** Controlorii fac aceleași verificări des. Dacă testul arată că se plictisesc, R8 (lista de sarcini) devine P1.
6. **Ghidul Microsoft pentru română** a fost citit doar parțial. Regulile 1, 5 și 20 trebuie confirmate pe PDF înainte de a deveni normă.
7. **Estimările de efort** nu au la bază inspecția codului WPF.

---

## Surse

GOV.UK și sectorul public
- https://design-system.service.gov.uk/patterns/question-pages
- https://www.gov.uk/service-manual/design/form-structure
- https://designnotes.blog.gov.uk/2015/07/03/one-thing-per-page/
- https://designsystem.gov.scot/patterns/question-page
- https://design-system.service.gov.uk/patterns/complete-multiple-tasks/
- https://design-system.service.gov.uk/patterns/step-by-step-navigation/
- https://design-system.service.gov.uk/patterns/check-answers/
- https://design-system.service.gov.uk/patterns/confirmation-pages
- https://design-system.service.gov.uk/components/error-message/
- https://service-manual.nhs.uk/content/how-we-write
- https://service-manual.nhs.uk/content/health-literacy
- https://service-manual.nhs.uk/design-system/patterns/confirmation-page
- https://commission.europa.eu/resources/europa-web-guide/design-content-and-development/content/web-writing-guidelines/other-style-guides_en
- Federal Plain Language Guidelines, copie arhivată: https://webarchive.library.unt.edu/web/20121006190813mp_/http:/www.plainlanguage.gov/howto/guidelines/FederalPLGuidelines/TOC.cfm

Nielsen Norman Group
- https://www.nngroup.com/articles/progressive-disclosure/
- https://www.nngroup.com/articles/error-message-guidelines/
- https://www.nngroup.com/articles/empty-state-interface-design/
- https://www.nngroup.com/articles/wizards/
- https://www.nngroup.com/articles/tooltip-guidelines/
- https://www.nngroup.com/articles/data-tables/
- https://www.nngroup.com/articles/mobile-tutorials/
- https://www.nngroup.com/articles/progress-indicators/
- https://www.nngroup.com/articles/visibility-system-status/

Microsoft, Google, Apple
- https://support.microsoft.com/windows/security/windows-security/windows-security-app-overview
- https://learn.microsoft.com/security-exposure-management/security-recommendations
- https://learn.microsoft.com/en-us/training/modules/implement-endpoint-protection-use-microsoft-defender-endpoint/7-reduce-threat-exposure
- https://www.helpnetsecurity.com/2026/06/01/microsoft-defender-exposure-score-update/
- https://learn.microsoft.com/windows/uwp/design/style/writing-style
- https://learn.microsoft.com/windows/apps/design/accessibility/high-contrast-themes
- https://learn.microsoft.com/en-us/archive/blogs/wpf/systemcolors-reference
- https://learn.microsoft.com/en-us/globalization/reference/microsoft-style-guides
- https://aka.ms/romanian-styleguide
- https://research.google/pubs/improving-ssl-warnings-comprehension-and-adherence/
- https://www.securityweek.com/new-chrome-ssl-warning-improved-adherence-rates-google
- https://techcrunch.com/2017/10/16/google-revamps-its-security-checkup-feature-with-personalized-suggestions-for-your-account/
- https://support.apple.com/guide/personal-safety/ips2aad835e1 (Safety Check: resetare de urgență vs. gestionare detaliată, progres salvat pe parcurs; model pentru „rapid” vs. „detaliat”)

Plângeri, scoruri, oboseală la alerte
- https://beaglesecurity.com/blog/article/nessus-review.html
- https://www.capterra.in/software/130577/nessus
- https://www.saasworthy.com/product/nessus/reviews
- https://www.capterra.in/reviews/82971/qualysguard-enterprise
- https://beaglesecurity.com/blog/article/qualys-review.html
- https://www.cisecurity.org/cybersecurity-tools/cis-cat-pro/cis-cat-faq
- https://blog.pa.com.au/it-and-security/your-secure-score-isn-t-the-full-story
- https://www.hagel-it.de/it-sicherheit/secure-score-grenzen
- https://blog.ise.io/blog/microsoft-secure-score-helpful-or-sales-pitch
- https://blog.admindroid.com/cybersecurity-awareness-month-secure-score-in-microsoft-365/
- https://www.cybersecuritydive.com/news/security-alert-analyst-SOC-idc-fireeye/595111/
- https://linuxsecurity.com/news/security-projects/when-vulnerability-management-meets-compliance
- https://isc.sans.edu/diary/21191
- https://misq.org/tuning-out-security-warnings-a-longitudinal-examination-of-habituation-through-fmri-eye-tracking-and-field-experiments.html
- https://scholarsarchive.byu.edu/facpub/1955

Accesibilitate și limba română
- https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/
- https://www.unicode.org/L2/L2013/13155r-cedilla-comma.pdf
- https://brandient.com/kit-on-romanian-diacritics
