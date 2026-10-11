# LogAnalyzer — Cerințe HG 585/2002 (INFOSEC) și pregătire pentru acreditare

> **Stare:** analiză de cercetare, **doar citire**; nu s-a modificat cod, nu s-a făcut commit/push. Data: 2026-10-08. Cod analizat: `origin/main` @ `126a2bd015d0de3a87ddfbaa200d488bbf093265`, `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`. Citatele de la sursă, cu URL și context, sunt în `hg585_quote_sources.md` (scratchpad al sesiunii).
>
> **Avertisment:** documentul **nu** afirmă că aplicația este sau va fi conformă HG 585/2002; stabilește cerințele, golurile și un plan. Acreditarea este decizia agenției de acreditare de securitate (AAS) din cadrul ORNISS. Nu este consultanță juridică.

## Rezumat

* **Decizia proprietarului nr. 13** (luată în timpul analizei; `CONTRACT_AUDIT_STAGE1.md` rândul 13): P1 = aplicație separată, P2/P3 = o aplicație cu două moduri prin politică semnată; §3.1 o urmează.
* **Scop (trei profiluri):** P1 clasificat (HG 585/2002 + Legea 182/2002), P2 neclasificat izolat și P3 conectat (NIS2 / OUG 155/2024 / ordinele DNSC; Reg. 2024/2690 doar ca referință tehnică).
* **Cerințe:** 159 fragmente verbatim, dintre care 109 din HG 585/2002 (anexa, cap. 8 «Protecția surselor generatoare de informații – INFOSEC», art. 236-337, plus art. 3, 14-15, 21-23, 45-49, 56, 65, 76-79, 88, 338), Legea 182/2002, INFOSEC 2 (public), Ghidul PrOpSec (public), Dir. 2022/2555 art. 21, OUG 155/2024 și Reg. 2024/2690 (anexă).
* **Golurile aplicației (55 de cerințe derivate):**

| Profil | MEETS | PARTIAL | MISSING | N/A | aplicabile |
|---|---|---|---|---|---|
| **P1** | 4 | 23 | 20 | 1 | 48 |
| P2 | 4 | 28 | 13 | 4 | 49 |
| P3 | 2 | 28 | 12 | 3 | 45 |

### Primele 10 blocaje pentru acreditare

1. **Nu există lanț de integritate al livrabilului** — executabil nesemnat, fără manifest de hash-uri, fără SBOM, fără build reproductibil, DLL-uri native auto-extrase (SW-01..SW-03; HG 309-311, 318).
2. **Nu există clasificare și marcaj** — `CaseInfo` nu are nivel de secretizare, iar PDF-urile și exporturile nu sunt marcate pe pagină (MRK-01..MRK-03; HG 46-49, 56).
3. **Nu există registru de acces unic și tamper-evident** — doar jurnalul de politici și custodia de intake sunt înlănțuite; jurnalul de caz, custodia CSV și `AuditLogService` sunt text simplu, fără retenție configurabilă (AUD-01..AUD-04; HG 291).
4. **Afirmații de conformitate nefondate și conținut simulat** — «CONFORM HG 585/NATO», scor 100 codificat, citări greșite (art. 21/65), etichetă statică «SECRET DE SERVICIU», detecție simulată «TEMPEST» (DOC-01).
5. **Modul de rețea nu poate fi eliminat la compilare și poate fi relaxat de operator** — `--mode=`/`LogAnalyzer.mode` suprascriu detecția; codul de rețea este compilat mereu (NET-02, NET-03; HG 282, 305; INFOSEC 2 art. 35).
6. **Acțiunile asupra gazdei nu pot fi dezactivate global** — firewall, `auditpol`, registru, suspendare proces; plus `powershell -ExecutionPolicy Bypass` cu fallback pe o cale de dezvoltator (HOST-02, HOST-03; HG 309, 310, 329).
7. **Fără roluri și fără regula celor doi în afara politicilor** — niciun administrator de securitate distinct, nicio limitare pe nevoia de a cunoaște (AC-01..AC-03; HG 244, 268-277).
8. **Fără documentația de acreditare a aplicației** — CSS, PrOpSec, analiză de risc, documentație tehnică de distribuire, fluxuri de date (ACR-01..ACR-04; HG 261-263, 307-308, 318).
9. **Igiena datelor** — copie EVTX reparată rămâne în `%TEMP%`, dosarele de caz necriptate, nu există «scoatere din uz caz», sanitizarea cu certificat citează articol greșit (DEL-01..DEL-03, CRY-03; HG 292, 296-298).
10. **Secret codificat în licență și criptografie care nu poate fi revendicată ca aprobată** — sare în cod, `license.lic` în repo, SQLCipher nativ «unofficial and unsupported», normele criptografice ORNISS nepublice (CRY-01, CRY-02; INFOSEC 2 art. 41).

### Marcat «neverificat» în acest document

Forma consolidată oficială a HG 585/2002 la zi (copiile folosite sunt consolidări secundare; `legislatie.just.ro` inaccesibil), orice modificare după 28.11.2022; textul INFOSEC 1, 3, 4, normele criptografice, formularul de incident și conținutul Catalogului național INFOSEC; ordinul DNSC de la art. 12 alin. (1) OUG 155/2024 și ordinele DNSC 1-2/2025 (doar semnalate); starea OUG 155/2024 după Legea 124/2025; aplicabilitatea Reg. 2024/2690 și a NIS2 organizațiilor clientului; dacă LogAnalyzer e «produs informatic de securitate» (HG 319); efectul `esentutl /vss` pe gazdă; tranzitivele NuGet; comportamentul rulat al aplicației (nu s-a executat).


## PARTEA 1 — Cerințe (citate verbatim, cu articol/alineat)

### 1.0 Cum se citește

* Blocurile `>` conțin **text verbatim** din sursa indicată (diacriticele și grafia sursei se păstrează; rândurile frânte de PDF sunt unite). Fiecare bloc are un identificator `[ID]`; în `hg585_quote_sources.md` există, pentru fiecare ID: URL/fișier, rândurile sursei, contextul de dinainte/de după și rezultatul controlului încrucișat.
* Rândurile care încep cu **Interpretare** sunt ale autorului acestui document, **nu** sunt citate și nu au valoare juridică; ele traduc textul în cerință pentru aplicație.
* **neverificat** = nu s-a putut obține textul (sau nu e public) / nu s-a putut confirma pe sursa oficială.
* Statutul surselor (detaliat în §1.2): textul HG 585/2002 provine din **trei copii secundare concordante** (două consolidări CTCE și o formă sintetică); **nu** a putut fi confirmat pe `legislatie.just.ro` (acces refuzat din mediul de lucru). Citatele HG 585 sunt deci „verificat în surse secundare, neconfirmat pe legislatie.just.ro”.

### 1.1 Cele trei profiluri de desfășurare (clarificarea proprietarului)

| Profil | Mediu | Regim primar | Regim de rezervă / adițional |
|---|---|---|---|
| **P1 — CLASIFICAT** | rețele izolate (air-gap) și PC-uri standalone care prelucrează informații clasificate | HG 585/2002 (cap. 8, INFOSEC) + Legea 182/2002 | directive ORNISS INFOSEC (parțial publice, vezi §1.16) |
| **P2 — NECLASIFICAT, IZOLAT** | rețele izolate și PC-uri standalone fără informații clasificate | NIS2 (Dir. 2022/2555) art. 21 alin. (2) + OUG 155/2024 + ordinele DNSC; Reg. 2024/2690 doar dacă entitatea este de tipul din art. 1 al regulamentului | — |
| **P3 — CONECTAT** | rețele / PC-uri conectate direct la internet | la fel ca P2 | + cerințe specifice sistemelor expuse: gestionarea vulnerabilităților, actualizări sigure, securitatea rețelei, comunicații securizate, lanț de aprovizionare |

**Decizia proprietarului nr. 13 (2026-10-08):** P1 este o **aplicație separată** (rețea, AI la distanță, acțiuni asupra gazdei și actualizări necompilate); P2/P3 este **o aplicație cu două moduri** alese prin politică semnată (vezi §3.1). **Interpretare (neverificat juridic):** cerințele P1 sunt cele mai stricte. OUG 155/2024 își delimitează scopul la „spațiul cibernetic național civil” (art. 2 alin. (1) lit. a), mai jos), iar art. 63 prevede doar informarea instituțiilor din domeniul informațiilor clasificate; rezultă că SIC acreditate pentru informații clasificate sunt guvernate de HG 585/2002, nu de OUG 155/2024. Dacă proprietarul sau clienții săi sunt „entități esențiale/importante” în sensul OUG 155/2024 este o chestiune **neverificată** și nu poate fi stabilită din repo.

### 1.2 Statutul surselor

| Sursă | Cum a fost obținută | Statut |
|---|---|---|
| HG 585/2002 (text consolidat) | `legistm.pdf` — «Formă consolidată valabilă la data 28-11-2022», nota CTCE Piatra-Neamț (modificări: HG 2.202/2004 și HG 185/2005); control încrucișat cu `asist.pdf` (consolidare CTCE până la 24.03.2005) și `snppc1.pdf` («forma sintetică» 29.06.2022). URL-ul de descărcare al acestor trei fișiere **nu a putut fi recuperat** (descărcate într-o etapă anterioară a sesiunii); `legislatie.just.ro` a returnat 502 / conexiune închisă la reîncercare. | **verificat în surse secundare; neconfirmat pe sursa oficială** |
| Legea 182/2002 | `https://www.sri.ro/assets/files/legislatie/2024/Lege_182.2002.pdf` (forma consolidată 13.03.2024, același SHA-256 ca fișierul folosit anterior) | verificat (site SRI) |
| ORNISS nr. 16/2014 — INFOSEC 2 | `legeaz.net` (mirror neoficial al MO nr. 262/10.04.2014) | verificat în mirror neoficial |
| OG ORNISS nr. 18/2014 — Ghid PrOpSec (DS 2) | `legeaz.net` (mirror neoficial al MO nr. 242/04.04.2014) | verificat în mirror neoficial |
| Directiva (UE) 2022/2555, Reg. (UE) 2024/2690 | `publications.europa.eu` (Cellar), versiunea RO, XHTML | verificat (sursa oficială UE) |
| OUG 155/2024 | `https://upt.ro/img/files/legislatie/2024/OUG_155_2024.pdf` (MO 1332/31.12.2024, text original) | verificat în copie instituțională; **aprobarea prin Legea 124/2025 și eventualele modificări: neverificat** |
| INFOSEC 1 (ord. ORNISS 86/2013), INFOSEC 3 (ord. ORNISS 484/2003), INFOSEC 4, norme de criptografie, formularul de incident | menționate prin titlu în textele publice | **nepublic / neverificat** |
| Ordinul directorului DNSC prevăzut la art. 12 alin. (1) OUG 155/2024 (măsurile tehnice, operaționale, organizatorice) | căutare web: nu a fost găsit | **neverificat** (existența și conținutul) |

### 1.3 Terminologie exactă folosită de text


> **Legea 182/2002, art. 4** · `[L182-4]`  
> ART. 4  
> Principalele obiective ale protecţiei informaţiilor clasificate sunt: a) protejarea informaţiilor clasificate împotriva acţiunilor de spionaj, compromitere sau acces neautorizat, alterării sau modificării conţinutului acestora, precum şi împotriva sabotajelor ori distrugerilor neautorizate; b) realizarea securităţii sistemelor informatice şi de transmitere a informaţiilor clasificate.  

> **Legea 182/2002, art. 15 lit. a)-e)** · `[L182-15]`  
> ART. 15  
> În sensul prezentei legi, următorii termeni se definesc astfel: a) informaţii - orice documente, date, obiecte sau activităţi, indiferent de suport, forma, mod de exprimare sau de punere în circulaţie; b) informaţii clasificate - informaţiile, datele, documentele de interes pentru securitatea naţională, care, datorită nivelurilor de importanta şi consecinţelor care s-ar produce ca urmare a dezvăluirii sau diseminării neautorizate, trebuie să fie protejate; c) clasele de secretizare sunt: secrete de stat şi secrete de serviciu; d) informaţii secrete de stat - informaţiile care privesc securitatea naţională, prin a căror divulgare se pot prejudicia siguranţa naţională şi apărarea tarii; e) informaţii secrete de serviciu - informaţiile a căror divulgare este de natura să determine prejudicii unei persoane juridice de drept public sau privat;  

> **Legea 182/2002, art. 18** · `[L182-18]`  
> ART. 18  
> (1) Informaţiile secrete de stat se clasifica pe niveluri de secretizare, în funcţie de importanta valorilor protejate. (2) Nivelurile de secretizare atribuite informaţiilor din clasa secrete de stat sunt: a) strict secret de importanta deosebita; b) strict secret; c) secret.  

> **HG 585/2002, art. 3, definiția «incident de securitate»** · `[HG-3-INCIDENT]`  
> – incident de securitate - orice acțiune sau inacțiune contrară reglementărilor de securitate a cărei consecinţă a determinat sau este de natură să determine compromiterea informaţiilor clasificate;  

> **HG 585/2002, art. 3, definiția «marcare»** · `[HG-3-MARCARE]`  
> – marcare - activitatea de inscripționare a nivelului de secretizare a informației şi de semnalare a cerințelor speciale de protecţie a acesteia;  

> **HG 585/2002, art. 237, definiția INFOSEC** · `[HG-237-INFOSEC]`  
> – INFOSEC - ansamblul măsurilor şi structurilor de protecţie a informaţiilor clasificate care sunt prelucrate, stocate sau transmise prin intermediul sistemelor informatice de comunicații şi al altor sisteme electronice, împotriva amenințărilor şi a oricăror acţiuni care pot aduce atingere confidențialității, integrității, disponibilităţii autenticităţii şi nerepudierii informaţiilor clasificate precum şi afectarea funcţionării sistemelor informatice, indiferent dacă acestea apar accidental sau intenționat. Măsurile INFOSEC acoperă securitatea calculatoarelor, a transmisiilor, a emisiilor, securitatea criptografică, precum şi depistarea şi prevenirea amenințărilor la care sunt expuse informaţiile şi sistemele;  

> **HG 585/2002, art. 237, definiția SIC** · `[HG-237-SIC]`  
> – sistemul informatic şi de comunicații - SIC - ansamblu informatic prin intermediul căruia se stochează, se procesează şi se transmit informaţii în format electronic, alcătuit din cel puţin un SPAD, izolat sau conectat la o RTD. Poate avea o configurație complexă, formată din mai multe SPAD-uri şi/sau RTD-uri interconectate;  

> **HG 585/2002, art. 237, «confidențialitatea», «integritatea», «disponibilitatea»** · `[HG-237-CIA]`  
> – confidențialitatea - asigurarea accesului la informaţii clasificate numai pe baza certificatului de securitate al persoanei, în acord cu nivelul de secretizare a informației accesate şi a permisiunii rezultate din aplicarea principiului nevoii de a cunoaște;  
> – integritatea - interdicția modificării - prin ștergere sau adăugare - ori a distrugerii în mod neautorizat a informaţiilor clasificate;  
> – disponibilitatea asigurarea condiţiilor necesare regăsirii şi folosirii cu ușurință, ori de câte ori este nevoie, cu respectarea strictă a condiţiilor de confidențialitate şi integritate a informaţiilor clasificate;  

> **HG 585/2002, art. 237, «autenticitatea» și «nerepudierea»** · `[HG-237-NEREP]`  
> – autenticitatea - asigurarea posibilităţii de verificare a identităţii pe care un utilizator de SPAD sau RTD pretinde că o are;  
> – nerepudierea - măsura prin care se asigură faptul că, după emiterea/recepționarea unei informaţii într-un sistem de comunicații securizat, expeditorul/destinatarul nu poate nega, în mod fals, că a expediat/primit informaţii;  

> **HG 585/2002, art. 238 (abrevieri CSTIC, CSS)** · `[HG-238]`  
> Articolul 238  
> Abrevierile utilizate în prezentul capitol semnifică:  
> a)CSTIC - componenta de securitate pentru tehnologia informației şi comunicațiilor instituită în unitățile deținătoare de informaţii clasificate;  
> b)TIC - tehnologia informației şi comunicațiilor;  
> c)CSS - cerinţele de securitate specifice.  

**Interpretare:** termenii HG 585 care trebuie folosiți în documentația de acreditare: *SPAD*, *RTD*, *SIC*, *CSTIC*, *CSS* (cerințe de securitate specifice), *procedurile operaționale de securitate*, *agenția de acreditare de securitate (AAS)*, *administrator de securitate al SPAD / al rețelei / al obiectivului SIC*, *registre de acces*, *mediu de stocare*, *regula celor doi*, *mod de operare dedicat / de nivel înalt / multi-nivel*. În Ghidul PrOpSec (DS 2) apar în plus *PrOpSec*, *DCS / CSSS*, *AOSIC* (autoritatea operațională a SIC), *SII* (structuri interne INFOSEC), *ADS*.

### 1.4 Acreditarea și documentația


> **HG 585/2002, anexa, art. 240** · `[HG-240]`  
> Articolul 240  
> (1)Sistemele SPAD şi RTD - SIC au dreptul să stocheze, să proceseze sau să transmită informaţii clasificate, numai dacă sunt autorizate potrivit prezentei hotărâri.  
> (2)În vederea autorizării SPAD şi RTD - SIC unitățile vor întocmi, cu aprobarea organelor lor de conducere, strategia proprie de securitate, în baza căreia vor implementa sisteme proprii de securitate, care vor include utilizarea de produse specifice tehnologiei informației şi comunicațiilor, personal instruit şi măsuri de protecţie a informației, incluzând controlul accesului la sistemele şi serviciile informatice şi de comunicatii, pe baza principiului necesităţii de a cunoaște şi al nivelului de secretizare atribuit.  
> (3)SPAD şi RTD - SIC vor fi supuse procesului de acreditare, urmat de evaluări periodice, în vederea menținerii acreditării.  

> **HG 585/2002, anexa, art. 241** · `[HG-241]`  
> Articolul 241  
> (1)Aplicarea reglementărilor în vigoare referitoare la protecția informaţiilor clasificate în format electronic funcţionează unitar la nivel naţional. Sistemul de emitere şi implementare a măsurilor de securitate adresate protecției informaţiilor clasificate care sunt stocate, procesate sau transmise de SPAD sau RTD - SIC, precum şi controlul modului de implementare a măsurilor de securitate se realizează de către o structură funcțională cu atribuţii de reglementare, control şi autorizare, care include:  
> a)o agenție pentru acordarea acreditării de funcționare în regim de securitate;  
> b)o agenție care elaborează şi implementează metode, mijloace şi măsuri de securitate;  
> c)o agenție responsabilă cu protecția criptografică.  
> (2)Agențiile menţionate la alin. (1) sunt subordonate instituției desemnate la nivel naţional, pentru protecția informaţiilor clasificate, ORNISS.  
> (3)Măsurile de protecţie a informaţiilor clasificate în format electronic trebuie reactualizate permanent, prin depistare, documentare şi gestionare a amenințărilor şi vulnerabilităților la adresa informaţiilor clasificate şi sistemelor care le prelucrează, stochează şi transmit.  

> **HG 585/2002, anexa, art. 253** · `[HG-253]`  
> Articolul 253  
> Agenția de acreditare de securitate este subordonată instituției desemnate la nivel naţional pentru protecția informaţiilor clasificate, are reprezentanți delegaţi din cadrul ADS implicate, în funcție de SPAD şi RTD - SIC care trebuie acreditate, şi îndeplinește următoarele atribuţii principale:  
> a)asigură, la nivel naţional, acreditarea de securitate şi reacreditarea SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii clasificate, în funcție de nivelul de clasificare a acestora;  
> b)asigură evaluarea şi certificarea sistemelor SPAD şi RTD - SIC sau a unor elemente componente ale acestora;  
> c)stabilește criteriile de acreditare de securitate pentru SPAD şi RTD - SIC.  

> **HG 585/2002, anexa, art. 258** · `[HG-258]`  
> Articolul 258  
> Agentia de protecţie criptografica se organizeaza la nivel naţional, este subordonata institutiei desemnate la nivel naţional pentru protectia informaţiilor clasificate şi are urmatoarele atribuţii principale:  
> a)asigură managementul materialelor şi echipamentelor criptografice;  
> b)realizează distribuirea materialelor şi echipamentelor criptografice;  
> c)raportează instituției desemnate la nivel naţional pentru protectia informaţiilor clasificate incidentele de securitate cu care s-a confruntat;  
> d)cooperează cu agenția de acreditare de securitate, cu agenția de concepere şi implementare a metodelor, mijloacelor şi măsurilor de securitate şi cu alte structuri cu atribuţii în domeniu.  

> **HG 585/2002, art. 237, definiția «acreditarea»** · `[HG-237-ACRED]`  
> – acreditarea - etapa de acordare a autorizării şi aprobării unui SPAD sau RTD - SIC de a prelucra informaţii clasificate, în spațiul/mediul operațional propriu. Etapa de acreditare trebuie să se desfăşoare după ce s-au implementat toate procedurile de securitate şi după ce s-a atins un nivel suficient de protecţie a resurselor de sistem. Acreditarea se face, în principal, pe baza CSS şi include următoarele:  
> a)nota justificativă despre obiectivul acreditării sistemului, nivelul/nivelurile de clasificare a informaţiilor care urmează să fie procesate şi vehiculate; modul/modurile de operare protejată propuse;  
> b)nota justificativă despre managementul riscurilor - modul de tratare, gestionare şi rezolvare a riscurilor în care se specifică pericolele şi punctele vulnerabile, precum şi măsurile adecvate de contracarare a acestora;  
> c)o descriere detaliată a facilităților de securitate şi a procedurilor propuse, destinate SPAD sau RTD - SIC. Această descriere va reprezenta elementul esențial pentru finalizarea procesului de acreditare;  
> d)planul de implementare şi întreţinere a caracteristicilor de securitate;  
> e)planul de desfăşurare a etapelor de testare, evaluare şi certificare a securității SPAD sau RTD - SIC;  
> f)certificatul şi, acolo unde este necesar, elemente de acreditare suplimentare;  

> **HG 585/2002, anexa, art. 322** · `[HG-322]`  
> Articolul 322  
> (1)Toate SPAD şi RTD - SIC, înainte de a fi utilizate pentru stocarea, procesarea sau transmiterea informaţiilor clasificate, trebuie acreditate de către agenția de acreditare de securitate, pe baza datelor furnizate de către CSS, procedurilor operaționale de securitate şi altor documentaţii relevante.  
> (2)Subsistemele SPAD şi RTD - SIC şi stațiile de lucru cu acces la distanță sau terminalele vor fi acreditate ca parte integrantă a sistemelor SPAD şi RTD - SIC la care sunt conectate, în cazul în care un sistem SPAD sau RTD - SIC deserveşte atât NATO, cât şi organizaţiile/structurile interne ale țării, acreditarea se va face de către autoritatea naţională de securitate, cu consultarea ADS şi a agențiilor INFOSEC, potrivit competențelor.  

> **HG 585/2002, anexa, art. 261** · `[HG-261]`  
> Articolul 261  
> (1)Cerinţele de securitate specifice - CSS se constituie într-un document încheiat între agenția de acreditare de securitate şi CSTIC, ce va cuprinde principii şi măsuri de securitate care trebuie să stea la baza procesului de certificare şi acreditare a SPAD sau RTD - SIC.  
> (2)CSS se elaborează pentru fiecare SPAD şi RTD - SIC care stochează, procesează sau transmite informaţii clasificate, sunt stabilite de către CSTIC şi aprobate de către agenția de acreditare de securitate.  

> **HG 585/2002, anexa, art. 262** · `[HG-262]`  
> Articolul 262  
> CSS vor fi formulate încă din faza de proiectare a SPAD sau RTD - SIC şi vor fi dezvoltate pe tot ciclul de viaţă al sistemului.  

> **HG 585/2002, anexa, art. 263** · `[HG-263]`  
> Articolul 263  
> CSS au la bază standardele naţionale de protecţie, parametrii esențiali ai mediului operațional, nivelul minim de autorizare a personalului, nivelul de clasificare a informaţiilor gestionate şi modul de operare a sistemului care urmează să fie acreditat.  

> **HG 585/2002, anexa, art. 324** · `[HG-324]`  
> Articolul 324  
> Cerinţele de evaluare şi certificare se includ în planificarea sistemului SPAD şi RTD - SIC şi sunt stipulate explicit în CSS, imediat după ce modul de operare de securitate a fost stabilit.  

> **HG 585/2002, anexa, art. 307** · `[HG-307]`  
> Articolul 307  
> Procedurile operaționale de securitate reprezintă descrierea implementării strategiei de securitate ce urmează să fie adoptată, a procedurilor operaționale de urmat şi a responsabilităților personalului.  

> **HG 585/2002, anexa, art. 308** · `[HG-308]`  
> Articolul 308  
> Procedurile operaționale de securitate sunt elaborate de către agenția de concepere şi implementare a metodelor, mijloacelor şi măsurilor de securitate, în colaborare cu CSTIC, precum şi cu agenția de acreditare de securitate, care are atribuţii de coordonare, şi alte autorităţi cu atribuţii în domeniu. Agenția de acreditare de securitate va aproba procedurile de operare înainte de a autoriza stocarea, procesarea sau transmiterea informaţiilor secrete de stat prin SPAD - RTD - SIC.  

> **HG 585/2002, anexa, art. 317** · `[HG-317]`  
> Articolul 317  
> Sistemele SPAD sau RTD - SIC, precum şi componentele lor hardware şi software sunt achiziţionate de la furnizori interni sau externi selectați dintre cei agreați de către agenția de acreditare de securitate.  

> **HG 585/2002, anexa, art. 318** · `[HG-318]`  
> Articolul 318  
> Componentele sistemelor de securitate implementate în SPAD sau RTD - SIC trebuie acreditate pe baza unei documentaţii tehnice amănunțite privind proiectarea, realizarea şi modul de distribuire al acestora.  

> **HG 585/2002, anexa, art. 319** · `[HG-319]`  
> Articolul 319  
> SPAD sau RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat sau componentele lor de bază - sisteme de operare de scop general, produse de limitare a funcţionării pentru realizarea securității şi produse pentru comunicare în rețea - se pot achiziționa numai dacă au fost evaluate şi certificate de către agenția de acreditare de securitate.  

> **HG 585/2002, anexa, art. 323** · `[HG-323]`  
> Articolul 323  
> În situaţiile ce privesc modul de operare de securitate multi-nivel, înainte de acreditarea propriu-zisă a SPAD sau RTD - SIC, hardware-ul, firmware-ul şi software-ul vor fi evaluate şi certificate de către agenția de acreditare de securitate, în acest sens, instituția desemnată la nivel naţional pentru protecția informaţiilor clasificate va stabili criterii diferențiate pentru fiecare nivel de secretizare a informaţiilor vehiculate de SPAD sau RTD - SIC.  

> **HG 585/2002, anexa, art. 325** · `[HG-325]`  
> Articolul 325  
> Următoarele situaţii impun evaluarea şi certificarea de securitate în modul de operare de securitate multinivel:  
> a)pentru SPAD sau RTD - SIC care stochează, procesează sau transmite informaţii clasificate strict secret de importanţă deosebită;  
> b)pentru SPAD sau RTD - SIC care stochează, procesează sau transmite informaţii clasificate strict secret, în cazurile în care:  
> – SPAD sau RTD - SIC este interconectat cu un alt SPAD sau RTD - SIC - de exemplu, aparţinând altui CSTIC;  
> – SPAD sau RTD - SIC are un număr de utilizatori posibili care nu poate fi definit exact.  

> **HG 585/2002, anexa, art. 327** · `[HG-327]`  
> Articolul 327  
> (1)În procesele de evaluare şi certificare se va stabili în ce măsură un SPAD sau RTD - SIC îndeplinește condiţiile de securitate specificate prin CSS, avându-se în vedere ca, după încheierea procesului de evaluare şi certificare, anumite secţiuni - paragrafe sau capitole - din CSS trebuie să fie modificate sau actualizate.  
> (2)Procesele de evaluare şi certificare trebuie să înceapă din stadiul de definire a SPAD sau RTD - SIC şi continuă pe parcursul fazelor de dezvoltare.  

> **INFOSEC 2, art. 36** · `[I2-36]`  
> Art. 36  
> INFOSEC 3 stabilește cerințele de acreditare de securitate, iar directivele tehnice și de implementare stabilesc măsurile care trebuie implementate.  

> **Ghid PrOpSec (DS 2), art. 2** · `[PRP-2]`  
> Art. 2  
> Întocmirea PrOpSec este obligatorie pentru toate sistemele informatice și de comunicații (SIC) supuse procesului de acreditare de securitate, conform prevederilor Directivei privind managementul INFOSEC pentru sisteme informatice și de comunicații - INFOSEC 3, aprobată prin Ordinul directorului general al Oficiului Registrului Național al Informațiilor Secrete de Stat nr. 484/2003, denumită în continuare INFOSEC 3.  

> **Ghid PrOpSec (DS 2), art. 3** · `[PRP-3]`  
> Art. 3  
> (1) PrOpSec reprezintă descrierea precisă a implementării cerințelor de securitate definite anterior în documentațiile cu cerințele de securitate (DCS), a procedurilor operaționale care vor trebui urmate și a responsabilităților personalului, specifice SIC.  
> (2) PrOpSec se dezvoltă pe măsura elaborării și actualizării DCS și se finalizează după aprobarea DCS de către AAS.  
> (3) AAS aprobă forma finală a PrOpSec.  

> **Ghid PrOpSec (DS 2), art. 5** · `[PRP-5]`  
> Art. 5  
> (1) Potrivit prevederilor INFOSEC 3, SIC care urmează să stocheze, să proceseze sau să transmită informații naționale clasificate cu nivel de clasificare SECRET și superior sau echivalent trebuie supuse unui proces de acreditare de securitate.  
> (2) Acreditarea de securitate trebuie obținută și pentru SIC care stochează, procesează sau transmit informații cu nivel de clasificare maxim NATO RESTRICTED sau RESTREINT UE/EU RESTRICTED.  
> (3) Pentru sistemele prevăzute la alin. (1) și (2), stocarea, procesarea sau transmiterea informațiilor clasificate trebuie să fie realizate în conformitate cu prevederile PrOpSec.  
> (4) Suplimentar, AAS poate solicita ca PrOpSec să fie întocmite și pentru SIC care stochează, procesează sau transmit informații neclasificate, dar care poartă marcaje administrative sau de limitare a diseminării și care sunt interconectate cu alte SIC ori cu rețele publice.  
> (5) Documentul cu cerințele de securitate specifice sistemului (CSSS) elaborat pentru SIC constituie baza pentru elaborarea PrOpSec.  

**Interpretare:** (1) aplicația nu se „acreditează” singură; se acreditează **SIC-ul** în care rulează (HG 240 alin. (1), 322 alin. (1)). Aplicația trebuie însă să furnizeze CSTIC-ului intrările pentru **CSS** și pentru **procedurile operaționale de securitate** și documentația tehnică cerută de art. 318 (proiectare, realizare, mod de distribuire). (2) Art. 319 vizează „sisteme de operare de scop general, produse de limitare a funcționării pentru realizarea securității și produse pentru comunicare în rețea”; dacă AAS consideră LogAnalyzer „produs informatic de securitate” (HG 237) este **neverificat**. (3) Conținutul formularelor și al INFOSEC 3 este **nepublic**.

### 1.5 Moduri de operare, control acces, nevoia de a cunoaște, roluri


> **Ghid PrOpSec (DS 2), art. 19** · `[PRP-19]`  
> Art. 19  
> (1) Cap. 1 "Administrarea și organizarea securității“ din cuprinsul PrOpSec conține o introducere de tipul celei prezentate mai jos: "Acest capitol, precum și capitolele următoare ale acestui document constituie Procedurile operaționale de securitate (PrOpSec) pentru stocarea, procesarea și transmiterea informațiilor (naționale/NATO/UE) clasificate în (.......... numele SIC ..........). PrOpSec au fost întocmite de către AOSIC împreună cu administratorii de securitate ai SIC (.......... enumerarea funcțiilor..........) în conformitate cu cerințele conținute în reglementările naționale privind protecția informațiilor clasificate, asociate cu (enumerarea normelor specifice privind securitatea: instrucțiuni locale, politici ale rețelelor din care SIC face parte sau cu care se interconectează). PrOpSec au fost aprobate de către ORNISS. Nu este permisă nicio abatere de la conținutul PrOpSec sau modificarea conținutului acestui document până când nu este obținut acordul explicit al AAS. Înainte de implementarea oricărei modificări semnificative în PrOpSec, AOSIC trebuie să obțină aprobarea AAS. Efectuarea unor modificări minore trebuie raportată de către AOSIC la AAS, dar implementarea acestora nu depinde de obținerea unei aprobări prealabile.“  
> (2) Capitolul menționat la alin. (1) conține, de asemenea, detalii referitoare la următoarele aspecte:  
> a) descrierea SIC - o descriere sumară a sistemului, inclusiv a interconectărilor externe și o subliniere a capacităților funcționale;  
> b) responsabilitățile privind securitatea personalului cu atribuții în acest sens, potrivit Directivei privind structurile cu responsabilități în domeniul INFOSEC - INFOSEC 1, aprobată prin Ordinul directorului general al Oficiului Registrului Național al Informațiilor Secrete de Stat nr. 86/2013, denumită în continuare INFOSEC 1 (de exemplu, AOSIC, administratorii de securitate ai SIC, administratorul CRIPTO, administratorul COMSEC, inclusiv personalul având responsabilități în asigurarea securității fizice, a personalului, a informațiilor și, unde este cazul, a securității industriale);  
> c) detalii despre modul de operare de securitate al SIC și nivelul de clasificare a informațiilor vehiculate în SIC;  
> d) proceduri administrative pentru actualizarea sau efectuarea de modificări în Lista cu utilizatorii autorizați ai SIC și drepturile de acces ale acestora;  
> e) prevederi pentru raportarea imediată a oricărui incident major care implică încălcarea securității fizice, a personalului, a informațiilor sau a SIC către administratorii de securitate ai SIC, incident care apoi trebuie raportat de către AOSIC la AAS folosindu-se formularul din INFOSEC 3;  
> f) prevederi care să garanteze că întregul personal al SIC a luat la cunoștință, a înțeles și și-a însușit conținutul PrOpSec, în părțile care îl privesc. Într-un mediu de rețea, pentru creșterea gradului de conștientizare a personalului privind securitatea, PrOpSec sau extrase semnificative din acest document pot fi stocate pe un server central, în așa fel încât și utilizatorii să poată avea acces ușor la procedurile care îi interesează, cu precizarea ca PrOpSec să poată fi accesate doar de utilizatorii autorizați ai SIC, conform drepturilor de acces ale acestora.  
> (3) Capitolul prevăzut la alin. (1) conține, de asemenea, detalii, acolo unde este cazul, referitoare la următoarele aspecte:  
> a) procedurile privind asistența de securitate a utilizatorilor din locațiile distribuite sau aflate la distanță ale SIC;  
> b) extrase din cerințele de securitate a comunicațiilor, pentru a include, de exemplu, procedurile operaționale criptografice pentru produsele și mecanismele criptografice în uz;  
> c) proceduri pentru controlul personalului tehnic sau al altor categorii de personal suport care necesită accesul în zona SIC sau în zonele terminalelor/stațiilor de lucru aflate la distanță;  
> d) proceduri pentru controlul mediilor de stocare, a componentelor software și hardware autorizate, care sunt proprietate privată;  
> e) proceduri pentru controlul echipamentelor și componentelor software autorizate ale contractorilor.  

> **HG 585/2002, anexa, art. 259** · `[HG-259]`  
> Articolul 259  
> (1)Măsurile de protecţie a informaţiilor clasificate în format electronic se aplică sistemelor SPAD şi RTD - SIC care stochează, procesează sau transmit asemenea informaţii.  
> (2)Unitățile deținătoare de informaţii clasificate au obligaţia de a stabili şi implementa un ansamblu de măsuri de securitate a sistemelor SPAD şi RTD - SIC - fizice, de personal, administrative, de tip TEMPEST şi criptografic.  

> **HG 585/2002, anexa, art. 260** · `[HG-260]`  
> Articolul 260  
> Măsurile de securitate destinate protecției SPAD şi RTD - SIC trebuie să asigure controlul accesului pentru prevenirea sau detectarea divulgării neautorizate a informaţiilor. Procesul de certificare şi acreditare va stabili dacă aceste măsuri sunt corespunzătoare.  

> **HG 585/2002, anexa, art. 264** · `[HG-264]`  
> Articolul 264  
> SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii clasificate vor fi certificate şi acreditate să opereze, pe anumite perioade de timp, în unul din următoarele moduri de operare:  
> a)dedicat;  
> b)de nivel înalt;  
> c)multi-nivel.  

> **HG 585/2002, anexa, art. 265** · `[HG-265]`  
> Articolul 265  
> (1)În modul de operare dedicat, toate persoanele cu drept de acces la SPAD sau la RTD trebuie să aibă certificat de securitate pentru cel mai înalt nivel de clasificare a informaţiilor stocate, procesate sau transmise prin aceste sisteme. Necesitatea de a cunoaște pentru aceste persoane se stabilește cu privire la toate informaţiile stocate, procesate sau transmise în cadrul SPAD sau RTD - SIC.  
> (2)În acest mod de operare, principiul necesităţii de a cunoaște nu impune o separare a informaţiilor în cadrul SPAD sau RTD, ca mijloc de securitate a SIC. Celelalte măsuri de protecţie prevăzute vor asigura îndeplinirea cerințelor impuse de cel mai înalt nivel de clasificare a informaţiilor gestionate şi de toate categoriile de informaţii cu destinație specială stocate, procesate sau transmise în cadrul SPAD sau RTD.  

> **HG 585/2002, anexa, art. 266** · `[HG-266]`  
> Articolul 266  
> (1)În modul de operare de nivel înalt, toate persoanele cu drept de acces la SPAD sau la RTD - SIC trebuie să aibă certificat de securitate pentru cel mai înalt nivel de clasificare a informaţiilor stocate, procesate sau transmise în cadrul SPAD sau RTD - SIC, iar accesul la informaţii se va face diferențiat, conform principiului necesităţii de a cunoaște.  
> (2)Pentru a asigura accesul diferențiat la informaţii, conform principiului necesităţii de a cunoaște, se instituie facilităţi de securitate care să asigure un acces selectiv la informaţii în cadrul SPAD sau RTD - SIC.  
> (3)Celelalte măsuri de protecţie vor satisface cerinţele pentru cel mai înalt nivel de clasificare şi pentru toate categoriile de informaţii cu destinație specială stocate, procesate, transmise în cadrul SPAD sau RTD - SIC.  
> (4)Toate informaţiile stocate, procesate sau vehiculate în cadrul unui SPAD sau RTD - SIC în acest mod de operare vor fi protejate ca informaţii cu destinație specială, având cel mai înalt nivel de clasificare care a fost constatat în mulțimea informaţiilor stocate, procesate sau vehiculate prin sistem.  

> **HG 585/2002, anexa, art. 267** · `[HG-267]`  
> Articolul 267  
> (1)În modul de operare multi-nivel, accesul la informaţiile clasificate se face diferențiat, potrivit principiului necesităţii de a cunoaște, conform următoarelor reguli:  
> a)nu toate persoanele cu drept de acces la SPAD sau RTD - SIC au certificat de securitate pentru acces la informaţii de cel mai înalt nivel de clasificare care sunt stocate, procesate sau transmise prin aceste sisteme;  
> b)nu toate persoanele cu acces la SPAD sau RTD - SIC au acces la toate informaţiile stocate, procesate sau transmise prin aceste sisteme.  
> (2)Aplicarea regulilor prevăzute la alin. (1) impune instituirea, în compensatie, a unor facilităţi de securitate care să asigure un mod selectiv, individual, de acces la informaţiile clasificate din cadrul SPAD sau RTD - SIC.  

> **HG 585/2002, anexa, art. 244** · `[HG-244]`  
> Articolul 244  
> (1)În fiecare unitate care administrează SPAD şi RTD - SIC în care se stochează, se procesează sau se transmit informaţii clasificate, se va institui o componentă de securitate pentru tehnologia informației şi a comunicațiilor  
> - CSTIC, în subordinea structurii/funcționarului de securitate.  
> (2)În funcție de volumul de activitate şi dacă cerinţele de securitate permit, atribuţiile CSTIC pot fi îndeplinite numai de către funcționarul de securitate TIC sau pot fi preluate, în totalitate, de către structura/funcționarul de securitate din unitate.  
> (3)CSTIC îndeplinește atribuţii privind:  
> a)implementarea metodelor, mijloacelor şi măsurilor necesare protecției informaţiilor în format electronic;  
> b)exploatarea operațonală a SPAD şi RTD - SIC în condiţii de securitate;  
> c)coordonarea cooperării dintre unitatea deținătoare a SPAD sau RTD - SIC şi autoritatea care asigură acreditarea;  
> d)implementarea măsurilor de securitate şi protecția criptografică ale SPAD sau RTD - SIC.  
> (4)CSTIC reprezintă punctul de contact al agențiilor competente cu unitățile care deţin în administrare SPAD sau RTD - SIC şi, după caz, poate fi investită, temporar, de către aceste agenții, cu unele dintre atribuţiile lor.  
> (5)Propunerile pe linie de securitate avansate de către CSTIC devin operaționale numai după ce au fost aprobate de către conducerea unităţii care deține în administrare respectivul SPAD sau RTD - SIC.  

> **HG 585/2002, anexa, art. 246** · `[HG-246]`  
> Articolul 246  
> CSTIC este condusă de către funcționarul de securitate TIC şi are în compunere administratorii de securitate şi, după caz, şi alti specialiști din SPAD sau RTD - SIC. Toata structura CSTIC face parte din personalul unităţii care administrează SPAD sau RTD - SIC.  

> **HG 585/2002, anexa, art. 268** · `[HG-268]`  
> Articolul 268  
> (1)Securitatea SPAD a rețelei şi a obiectivului SIC se asigură prin funcțiile de administrator de securitate.  
> (2)Administratorii de securitate sunt:  
> a)administratorul de securitate al SPAD;  
> b)administratorul de securitate al rețelei;  
> c)administratorul de securitate al obiectivului SIC.  
> (3)Funcțiile de administratori de securitate trebuie să asigure îndeplinirea atribuţiilor CSTIC. Dacă este cazul, aceste funcții pot fi cumulate de către un singur specialist.  

> **HG 585/2002, anexa, art. 269** · `[HG-269]`  
> Articolul 269  
> (1)CSTIC desemnează un administrator de securitate al SPAD responsabil cu supervizarea dezvoltării, implementării şi administrării măsurilor de securitate dintr-un SPAD, inclusiv participarea la elaborarea procedurilor operaționale de securitate.  
> (2)La recomandarea autorităţii de acreditare de securitate, CSTIC poate desemna structuri de administrare ale SPAD care îndeplinesc aceleași atribuţii.  

> **HG 585/2002, anexa, art. 271** · `[HG-271]`  
> Articolul 271  
> (1)Administratorul de securitate al obiectivului SIC este desemnat de CSTIC sau de autoritatea de securitate competentă şi răspunde de asigurarea implementării şi menţinerea măsurilor de securitate aplicabile obiectivului SIC respectiv.  
> (2)Responsabilitățile unui administrator de securitate al obiectivului SIC pot fi îndeplinite de către structura/funcționarul de securitate al unităţii, ca parte a îndatoririlor sale profesionale.  
> (3)Obiectivul SIC reprezintă un amplasament specific sau un grup de amplasamente în care funcţionează un SPAD şi/sau RTD. Responsabilitatile şi măsurile de securitate pentru fiecare zonă de amplasare a unui terminal/stație de lucru care funcţionează la distanță trebuie explicit determinate.  

> **HG 585/2002, anexa, art. 272** · `[HG-272]`  
> Articolul 272  
> (1)Toţi utilizatorii de SPAD sau RTD - SIC poartă responsabilitatea în ce privește securitatea acestor sisteme raportate, în principal, la drepturile acordate şi sunt îndrumați de către administratorii de securitate.  
> (2)Utilizatorii vor fi autorizați pentru clasa şi nivelul de secretizare a informaţiilor clasificate stocate, procesate sau transmise în SPAD sau RTD - SIC. La acordarea accesului la informaţii, individual, se va urmări respectarea principiului necesităţii de a cunoaște.  
> (3)Informarea şi conștientizarea utilizatorilor asupra îndatoririlor lor de securitate trebuie să asigure o eficacitate sporită a sistemului de securitate.  

> **HG 585/2002, anexa, art. 274** · `[HG-274]`  
> Articolul 274  
> (1)Utilizatorii SPAD şi RTD - SIC sunt autorizați şi li se permite accesul la informaţii clasificate pe baza principiului necesităţii de a cunoaște şi în funcție de nivelul de clasificare a informaţiilor stocate, procesate sau transmise prin aceste sisteme.  
> (2)Unitățile deținătoare de informaţii clasificate în format electronic au obligaţia de a institui măsuri speciale pentru instruirea şi supravegherea personalului, inclusiv a personalului de proiectare de sistem care are acces la SPAD şi RTD, în vederea prevenirii şi înlăturării vulnerabilităților faţă de accesarea neautorizată.  

> **HG 585/2002, anexa, art. 275** · `[HG-275]`  
> Articolul 275  
> În proiectarea SPAD şi RTD - SIC trebuie să se aibă în vedere ca atribuirea sarcinilor şi răspunderilor personalului să se faca în așa fel încât să nu existe o persoană care să aibă cunoştinţă sau acces la toate programele şi cheile de securitate - parole, mijloace de identificare personală.  

> **HG 585/2002, anexa, art. 276** · `[HG-276]`  
> Articolul 276  
> Procedurile de lucru ale personalului din SPAD şi RTD - SIC trebuie să asigure separarea între operaţiunile de programare şi cele de exploatare a sistemului sau rețelei. Este interzis, cu excepţia unor situaţii speciale, ca personalul să facă atât programarea, cât şi operarea sistemelor sau rețelelor şi trebuie instituite proceduri speciale pentru depistarea acestor situaţii.  

> **HG 585/2002, anexa, art. 277** · `[HG-277]`  
> Articolul 277  
> Pentru orice fel de modificare aplicată unui sistem SPAD sau RTD - SIC este obligatorie colaborarea a cel puţin două persoane - regula celor doi. Procedurile de securitate vor menţiona explicit situaţiile în care regula celor doi trebuie aplicată.  

> **HG 585/2002, art. 237, «regula celor doi»** · `[HG-237-REGULA2]`  
> – regula celor doi - obligativitatea colaborării a două persoane pentru îndeplinirea unei activităţi specifice;  

> **HG 585/2002, anexa, art. 282** · `[HG-282]`  
> Articolul 282  
> Când un SPAD este exploatat în mod autonom, deconectat în mod permanent de alte SPAD, ţinând cont de condiţiile specifice, de alte măsuri de securitate, tehnice sau procedurale şi de rolul pe care îl are respectivul SPAD în funcționarea de ansamblu a sistemului, agenția de acreditare de securitate trebuie să stabilească măsuri specifice de protecţie, adaptate la structura acestui SPAD, conform nivelului de clasificare a informaţiilor gestionate.  

> **HG 585/2002, anexa, art. 283** · `[HG-283]`  
> Articolul 283  
> Toate informaţiile şi materialele care privesc accesul la un SPAD sau RTD - SIC sunt controlate şi protejate prin reglementări corespunzătoare nivelului de clasificare cel mai înalt şi specificului informaţiilor la care respectivul SPAD sau RTD - SIC permite accesul.  

**Interpretare:** aplicația trebuie să știe în ce mod de operare a fost acreditat SIC-ul; în modurile *nivel înalt* și *multi-nivel* trebuie să ofere „acces selectiv” (art. 266 alin. (2), 267 alin. (2)). Funcțiile care modifică sistemul (configurare, ștergere, export, schimbarea profilului) cer **regula celor doi** (art. 277) și separarea rolurilor *administrator de securitate* / *utilizator* / *administrator de sistem* (art. 244, 268-272, 276).

### 1.6 Identificare și autentificare


> **INFOSEC 2, art. 41** · `[I2-41]`  
> Art. 41  
> Implementarea securității criptografice în SIC care vehiculează informații clasificate se realizează în conformitate cu prevederile reglementărilor naționale, NATO, UE sau specifice SIC, după caz, specifice domeniului.  

> **INFOSEC 2, art. 46** · `[I2-46]`  
> Art. 46  
> (1) Controlul accesului reprezintă o primă linie de apărare, dat fiind că acesta permite identificarea, autentificarea, autorizarea și evidența oricărei entități (de exemplu: persoană, dispozitiv, serviciu) care solicită acces la SIC și la elementele acestuia.  
> (2) În SIC care vehiculează informații clasificate, controlul accesului se implementează pentru a preveni operațiuni neautorizate asupra SIC și a elementelor acestuia (de exemplu: date, dispozitive, servicii).  

> **INFOSEC 2, art. 49** · `[I2-49]`  
> Art. 49  
> (1) Cerințele minime privind identificarea și autentificarea pe SIC care vehiculează informați clasificate sunt stabilite prin reglementările în domeniu emise de către ORNISS și, după caz, se vor avea în vedere rezultatele procesului de management al riscului de securitate.  
> (2) Cerințele privind identificarea și autentificarea trebuie să definească proprietățile mecanismelor de securitate.  

> **Ghid PrOpSec (DS 2), art. 12** · `[PRP-12]`  
> Art. 12  
> Cap. 5 "Securitatea SIC“ din cuprinsul PrOpSec oferă detalii cu privire la metodele de utilizare și control al facilităților de protecție asigurate de componentele software, în special în ceea ce privește:  
> a) conceptul de identificare (user-id) - procedurile pentru stabilirea conturilor de utilizatori, grupurile de utilizatori, alocarea identificatorilor de utilizator, procedurile pentru ștergerea conturilor de utilizator la părăsirea funcției/postului sau atunci când este detectată o compromitere a acestor date;  
> b) conceptul de autentificare - modalități de autentificare (de exemplu: parole, token, mecanisme biometrice), proceduri de control și de schimbare, autoritatea emitentă, păstrarea evidenței pentru controlul acestor mijloace și persoana responsabilă, frecvența de schimbare și proceduri de utilizare a mecanismelor de autentificare;  
> c) mecanisme de control al accesului - proceduri pentru implementarea controlului accesului discreționar/obligatoriu la informații/servicii/dispozitive; procedurile pentru stabilirea drepturilor și permisiunilor utilizatorilor pentru utilizarea serviciilor și resurselor SIC; detalii cu privire la autoritățile responsabile și la păstrarea evidențelor de control.  

**Interpretare:** HG 585 definește *autenticitatea* (art. 237, mai sus) dar nu detaliază mecanismele; ele sunt în „reglementările emise de ORNISS” (INFOSEC 2 art. 49 alin. (1)) — **nepublice**. Din Ghidul PrOpSec rezultă că aplicația/SIC-ul trebuie să poată descrie: identificatori de utilizator, conturi, grupuri, ștergerea conturilor, mecanisme de autentificare, control de acces discreționar/obligatoriu și evidența controalelor.

### 1.7 Audit / jurnalizare / registre de acces


> **HG 585/2002, anexa, art. 291** · `[HG-291]`  
> Articolul 291  
> (1)Evidența automată a accesului la informaţiile clasificate în format electronic se ține în registrele de acces şi trebuie realizată necondiționat prin software.  
> (2)Registrele de acces se păstrează pe o perioadă stabilită de comun acord între agenția de acreditare de securitate şi CSTIC.  
> (3)Perioada minimă de păstrare a registrelor de acces la informaţiile strict secrete de importanţă deosebită este de 10 ani, iar a registrelor de acces la informaţiile strict secrete şi secrete, de cel puţin 3 ani.  

> **INFOSEC 2, art. 43** · `[I2-43]`  
> Art. 43  
> (1) SIC care vehiculează informații clasificate sunt protejate de măsuri de securitate pentru detecția activităților malițioase și a defecțiunilor, prin colectarea, analiza și stocarea informațiilor referitoare la evenimente relevante din punctul de vedere al securității.  
> (2) Măsurile prevăzute la alin. (1) sunt necesare pentru a asigura informații suficiente, inclusiv trasabilitatea evenimentelor, în vederea investigării unei compromiteri accidentale sau deliberate a obiectivelor securității informațiilor, precum și a unei tentative de compromitere a acestora, proporțional cu prejudiciul care poate fi produs.  
> (3) Cerințele de colectare a informațiilor referitoare la evenimente relevante din punctul de vedere al securității sunt definite având în vedere că logurile de securitate au un rol esențial pentru sprijinirea activității de audit al securității efectuate de AAS.  
> (4) Perioada de analiză și de păstrare a logurilor de securitate se aprobă de către AAS, pe baza analizei riscului și având în vedere următoarele aspecte:  
> a) obiectivele de securitate ale SIC;  
> b) mediul de amenințare;  
> c) tipul logurilor și al datelor colectate;  
> d) frecvența analizei logurilor;  
> e) utilizarea de instrumente automate pentru verificarea logurilor;  
> f) cerințele privind investigarea, auditul și alte cerințe legale.  

> **Ghid PrOpSec (DS 2), art. 27** · `[PRP-27]`  
> Art. 27  
> (1) Secțiunea "Managementul și auditul automat al securității“ conține un sumar al tuturor măsurilor și procedurilor automate de management al securității, al procedurilor de audit, atât cele manuale, cât și cele asigurate de sistem, alocarea responsabilităților relevante pentru SIC.  
> (2) Secțiunea menționată la alin. (1) include următoarele:  
> a) procedurile pentru rularea instrumentelor/programelor automate de management al securității și detalii despre facilitățile de audit;  
> b) detalii despre evenimentele relevante pentru securitate care trebuie luate în evidență (logged) (de exemplu, tipul evenimentului și informația asociată fiecărui tip de eveniment);  
> c) detalii despre modul cum sunt folosite jurnalele (log-urile) de securitate, atât pentru investigarea erorilor, cât și pentru anumite fișiere sau categorii de personal, bazate pe urmărirea evenimentelor sau activităților, a tendințelor anormale, incluzând detalii despre evenimentele care trebuie supravegheate;  
> d) desfășurarea inspecțiilor/analizelor periodice ale înregistrărilor de audit, în scopul descoperirii prompte a accesului neautorizat sau a încercărilor de acces și pentru luarea măsurilor corespunzătoare de remediere;  
> e) responsabilitățile persoanelor care trebuie să ruleze și să valideze integritatea instrumentelor/programelor de management automat al securității și să desfășoare investigări și analize în cazul descoperirii de anomalii;  
> f) proceduri de reacție la evenimente specifice, de exemplu, activarea alarmelor în timp real;  
> g) detalii privind perioada de păstrare a fișierelor de audit;  
> h) proceduri care trebuie urmate în cazul apariției de anomalii ale auditului.  

**Interpretare:** (1) „registre de acces” ținute *necondiționat prin software* (HG 291 alin. (1)) — acces la **informațiile clasificate în format electronic**, nu doar evenimente interne ale aplicației; (2) retenția se stabilește cu AAS, minimum 3 ani (secret, strict secret) / 10 ani (strict secret de importanță deosebită) — alin. (3); (3) INFOSEC 2 art. 43 cere trasabilitate și retenție aprobată de AAS; „trasabilitatea evenimentelor” pentru investigarea unei compromiteri presupune protecția jurnalelor împotriva alterării (inferență, nu text).

### 1.8 Integritatea software-ului și managementul configurației


> **HG 585/2002, anexa, art. 309** · `[HG-309]`  
> Articolul 309  
> CSTIC are obligaţia să efectueze controale periodice, prin care să stabilească dacă toate produsele software originale - sisteme de operare generale, subsisteme şi pachete soft - aflate în folosinţă, sunt protejate în condiţii conforme cu nivelul de clasificare al informaţiilor pe care acestea trebuie să le proceseze. Protecția programelor - software de aplicație se stabilește pe baza evaluării nivelului de secretizare a acestora, ţinând cont de nivelul de clasificare a informaţiilor pe care urmeaza să le proceseze.  

> **HG 585/2002, anexa, art. 310** · `[HG-310]`  
> Articolul 310  
> (1)Este interzisă utilizarea de software neautorizat de către agenția de acreditare de securitate.  
> (2)Conservarea exemplarelor originale, a copiilor - backup sau off-site, precum şi salvările periodice ale datelor obținute din procesare vor fi executate în conformitate cu prevederile procedurilor operaționale de securitate.  

> **HG 585/2002, anexa, art. 311** · `[HG-311]`  
> Articolul 311  
> (1)Versiunile software care sunt în uz trebuie să fie verificate la intervale regulate, pentru a garanta integritatea şi funcționarea lor corectă.  
> (2)Versiunile noi sau modificate ale software-ului nu vor fi folosite pentru procesarea informaţiilor secrete de stat, până când procedurile de securitate ale acestora nu sunt testate şi aprobate conform CSS.  
> (3)Un software care îmbunătățește posibilitățile sistemului şi care nu are nici o procedură de securitate nu poate fi folosit înainte de a fi verificat de către CSTIC.  

> **HG 585/2002, anexa, art. 316** · `[HG-316]`  
> Articolul 316  
> Cerinţele menţionate la art. 314 trebuie stipulate în CSS, iar procedurile de desfăşurare a activităţii respective trebuie stabilite în procedurile operaționale de securitate. Nu se acceptă tipurile de întreţinere care constau în aplicarea unor proceduri de diagnosticare ce implică accesul de la distanță la sistem, decât dacă activitatea respectivă se desfăşoară sub control strict şi numai cu aprobarea agenției de acreditare de securitate.  

> **HG 585/2002, anexa, art. 320** · `[HG-320]`  
> Articolul 320  
> Pentru SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de serviciu, sistemele şi componentele lor de bază vor respecta, pe cât posibil, criteriile prevăzute de prezentele standarde.  

> **HG 585/2002, anexa, art. 321** · `[HG-321]`  
> Articolul 321  
> La închirierea unor componente hardware sau software, în special a unor medii de stocare, se va ține cont ca astfel de echipamente, odată utilizate în SPAD sau RTD - SIC ce procesează, stochează sau transmit informaţii clasificate, vor fi supuse măsurilor de protecţie reglementate prin prezentele standarde. O dată clasificate, componentele respective nu vor putea fi scoase din zonele SPAD sau RTD - SIC decât după declasificare.  

> **HG 585/2002, anexa, art. 328** · `[HG-328]`  
> Articolul 328  
> Pentru toate SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat, CSTIC stabilește proceduri de control prin care să se poată stabili dacă schimbările intervenite în SIC sunt de natură a le compromite securitatea.  

> **HG 585/2002, anexa, art. 329** · `[HG-329]`  
> Articolul 329  
> (1)Modificările care implică reacreditarea sau pentru care se solicită aprobarea anterioară a agenției de acreditare de securitate trebuie să fie identificate cu claritate şi expuse în CSS.  
> (2)După orice modificare, reparare sau eroare care ar fi putut afecta dispozitivele de securitate ale SPAD sau RTD - SIC, CSTIC trebuie să efectueze o verificare privind funcționarea corectă a dispozitivelor de securitate.  
> (3)Menţinerea acreditării SPAD sau RTD - SIC trebuie să depindă de satisfacerea criteriilor de verificare.  

> **HG 585/2002, anexa, art. 330** · `[HG-330]`  
> Articolul 330  
> (1)Toate SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat sunt inspectate şi reexaminate periodic de către agenția de acreditare de securitate.  
> (2)Pentru SPAD sau RTD - SIC care stochează, procesează sau transmit informaţii strict secrete de importanţă deosebită, inspecţia se va face cel puţin o dată pe an.  

> **INFOSEC 2, art. 40** · `[I2-40]`  
> Art. 40  
> (1) Aspectele de securitate trebuie înglobate în ciclul de viață (proiectare, dezvoltare, implementare și întreținere) al componentelor software special dezvoltate pentru vehicularea de informații clasificate, avându-se în vedere obiectivele de securitate definite pentru SIC.  
> (2) Aplicațiile sunt supuse testărilor de securitate, managementului securității (de exemplu: versiuni de bază) și controlului modificărilor (de exemplu: patch-uri).  

> **INFOSEC 2, art. 44** · `[I2-44]`  
> Art. 44  
> (1) Pentru SIC care vehiculează informații clasificate și componentele hardware și software critice sunt definite configurații de securitate de bază, care trebuie aplicate și păstrate la zi, prin procesele de management al configurației și de cel de control al modificărilor pe întregul ciclu de viață al SIC.  
> (2) Configurațiile de bază ale securității includ setările de securitate necesare pentru consolidarea configurației componentelor critice, înainte de instalare, și cerințele pentru actualizările de securitate ale componentelor aflate în operare.  

> **Ghid PrOpSec (DS 2), art. 25** · `[PRP-25]`  
> Art. 25  
> (1) Securitatea software se referă la caracteristicile de securitate asigurate de următoarele componente:  
> a) firmware - instrucțiuni software, de obicei scrise de furnizorii de hardware, care simulează hardware-ul și pot fi înlocuite prin implementarea hardware efectivă;  
> b) sistemul de operare;  
> c) programe utilitare - asigură facilități comune și frecvent utilizate, cum ar fi funcții automatizate de birou, sisteme de gestiune a bazelor de date, compilatoare de programe, sortare și concatenare de fișiere, programe de verificare, scanare, control, audit etc.;  
> d) programe de aplicație - care satisfac cerințele utilizatorilor.  
> (2) Secțiunea "Securitatea software“ furnizează detalii, acolo unde este cazul, despre metoda de utilizare și control al caracteristicilor de protecție furnizate prin software, specificând în particular următoarele:  
> a) metoda de identificare (identificatorul utilizatorului) - proceduri de stabilire a conturilor utilizatorilor, a grupurilor de utilizatori și de alocare a identificatorilor utilizatorilor, proceduri de ștergere a conturilor utilizatorilor în cazul plecării personalului de la post sau atunci când a fost detectată o compromitere a contului respectiv;  
> b) metoda de autentificare - include protecția informațiilor de autentificare (de exemplu, parole, token sau metode biometrice), procedurile de control și schimbare, autoritatea emitentă, păstrarea înregistrărilor de control și de către cine, frecvența schimbării și procedurile utilizate pentru mecanismul de autentificare;  
> c) mecanismele de control al accesului - proceduri de implementare a controlului accesului discreționar și/sau obligatoriu la informații/servicii/dispozitive, proceduri pentru stabilirea drepturilor și permisiuni utilizatorilor de accesare și utilizare a informațiilor, serviciilor și resurselor SIC, detalii despre autoritățile responsabile și păstrarea evidențelor privind controlul;  
> d) evidența versiunii sistemului de operare, programelor utilitare și pachetelor software, inclusiv cele care vor fi folosite în situații deosebite;  
> e) controlul asupra facilităților de copiere sau de modificare a sistemului de operare, cu detalii despre autoritatea și documentația necesară;  
> f) detalii despre măsurile de precauție ce trebuie luate înainte și după procesare sau în timpul pregătirii diferitelor tipuri de activități clasificate, incluzând rutine de ștergere a memoriei principale, reguli de declasificare sau de suprascriere a versiunilor anterioare și proceduri care să asigure că bufferele sunt curățate și că toate datele din fișierele jurnalelor de audit au fost listate și suprascrise.  
> (3) Secțiunea software furnizează, de asemenea, unde este cazul, detalii privind software-ul de sistem și de aplicații, după cum urmează:  
> a) responsabilități pentru generare și utilizare;  
> b) procedurile de primire și introducere în sistem, autorizări și formularele necesare;  
> c) clasificarea;  
> d) controlul copierii;  
> e) utilizarea limbajelor de programare/compilatoarelor/macro-urilor;  
> f) proceduri de audit și validare a componentelor software - ce, de către cine, cu ce frecvență și ce înregistrări se păstrează;  
> g) copiile de siguranță ale sistemului - ce conțin și unde se păstrează, în ce formă, ce verificări se fac, cu ce frecvență și cine este autorizat să activeze/să folosească aceste copii;  
> h) proceduri care trebuie urmate în caz de erori și ce înregistrări trebuie păstrate;  
> i) controlul copiilor în format hârtie.  

> **Ghid PrOpSec (DS 2), art. 32** · `[PRP-32]`  
> Art. 32  
> (1) Managementul configurației SIC constă în identificarea, controlul, păstrarea evidenței, diseminarea și auditul tuturor modificărilor efectuate în timpul etapelor de proiectare, dezvoltare, exploatare, întreținere și îmbunătățire a ciclului de viață al SIC.  
> (2) Cap. 7 "Managementul configurației“ din cuprinsul PrOpSec pentru AOSIC furnizează detalii despre următoarele caracteristici ale planului de management al configurației, acolo unde acestea sunt legate de aspecte ale securității hardware, firmware și software:  
> a) responsabilitățile personalului pentru controlul și organizarea actualizării configurației;  
> b) documentația care descrie configurația de bază autorizată pentru SIC;  
> c) măsurile de securitate aplicate pentru a garanta faptul că arhitectura/configurația de bază autorizată pentru SIC nu poate face obiectul unor modificări neautorizate, de exemplu, prin introducerea unui software neautorizat;  
> d) controale care se efectuează la modificarea documentației de proiectare și de implementare;  
> e) controale care se efectuează la generarea unei noi versiuni a sistemului, incluzând pachetele utilitare și cele software;  
> f) măsuri aplicabile în cazul actualizării sistemului de operare (service packs, hoț fixes, security patches), incluzând pachetele utilitare și de software;  
> g) măsurile (tehnice, fizice și procedurale) care se efectuează pentru protecția față de modificarea sau distrugerea neautorizată a copiei principale sau a copiilor tuturor celorlalte materiale utilizate pentru generarea sistemului, incluzând pachetele software și utilitarele;  
> h) controale care se efectuează pentru configurarea dispozitivelor de comunicații (de exemplu, routere) și a dispozitivelor de protecție a limitelor sistemului (de exemplu, firewall);  
> i) procedurile pentru solicitarea modificării configurației hardware, firmware și software a SIC;  
> j) procedurile pentru solicitarea de modificări specifice ale configurației hardware sau a mediului operațional al sistemului, acolo unde există nevoia punerii de acord cu standardul TEMPEST și pentru auditul implementării modificărilor specifice;  
> k) procedurile postimplementare necesare pentru actualizarea documentației privind modificarea configurației.  
> (3) Capitolul menționat la alin. (1) include, de asemenea, detalii cu privire la procedurile de implementare a actualizărilor aplicațiilor antivirus, a sistemului de operare prin service packs/hotfixes/security patches.  

### 1.9 Protecția împotriva codului malițios


> **HG 585/2002, anexa, art. 312** · `[HG-312]`  
> Articolul 312  
> Verificarea prezenței virusilor şi software-ului nociv se face în conformitate cu cerinţele impuse de către agenția de acreditare de securitate.  

> **HG 585/2002, anexa, art. 313** · `[HG-313]`  
> Articolul 313  
> (1)Versiunile de software noi sau modificate - sisteme de operare, subsisteme, pachete de software şi software de aplicație - stocate pe diferite medii care se introduc într-o unitate, trebuie verificate obligatoriu pe sisteme de calcul izolate, în vederea depistării software-ului nociv sau a virușilor de calculator, înainte de a fi folosite în SPAD sau RTD - SIC. Periodic se va proceda la verificarea software-ului instalat.  
> (2)Verificările trebuie făcute mai frecvent dacă SPAD sau RTD - SIC sunt conectate la alt SPAD sau RTD -SIC sau la o rețea publică de comunicații.  

> **INFOSEC 2, art. 45** · `[I2-45]`  
> Art. 45  
> (1) Evoluția complexității software-ului malițios și capacitatea sa de a executa atacuri direcționale impun acordarea unei atenții sporite.  
> (2) În SIC care vehiculează informații clasificate sunt utilizate soluții de detecție care să blocheze instalarea, să prevină executarea, să trimită în carantină software-ul malițios și să alerteze personalul responsabil cu activitățile asociate răspunsului la incidente.  

> **Ghid PrOpSec (DS 2), art. 26** · `[PRP-26]`  
> Art. 26  
> (1) Secțiunea "Protecția antivirus a calculatoarelor“ conține un sumar al tuturor procedurilor și mecanismelor de protecție împotriva software-ului malițios, atât manuale, cât și automate, și responsabilitățile individuale relevante pentru SIC.  
> (2) Secțiunea menționată la alin. (1) include următoarele:  
> a) proceduri de verificare a sistemelor de operare instalate, a pachetelor software și a programelor utilitare, privind prezența virușilor sau a altui software malițios, incluzând proceduri pentru ștergerea acestora în cazul detectării lor;  
> b) proceduri pentru verificarea mediilor de stocare (conținând informații și software) primite din surse externe, incluzând proceduri pentru dezinfectarea lor;  
> c) proceduri pentru verificarea mesajelor electronice și a atașamentelor primite din surse externe pentru a identifica eventuala prezență a software-ului malițios;  
> d) proceduri care trebuie urmate de către utilizatori în cazul detectării unor evenimente cauzate de software malițios;  
> e) proceduri pentru raportarea incidentelor cauzate de viruși atât către expeditorul mediului de stocare infectat, cât și la AAS, folosindu-se formularul din Directiva privind managementul INFOSEC pentru sisteme informatice și de comunicații - INFOSEC 3, aprobată prin Ordinul directorului general al Oficiului Registrului Național al Informațiilor Secrete de Stat nr. 484/2003.  

### 1.10 Marcarea informațiilor produse (rapoarte, exporturi)


> **HG 585/2002, anexa, art. 15** · `[HG-15]`  
> Articolul 15  
> Marcarea informaţiilor clasificate are drept scop atenţionarea persoanelor care le gestionează sau le accesează că sunt în posesia unor informaţii în legătură cu care trebuie aplicate măsuri specifice de acces şi protecţie, în conformitate cu legea.  

> **HG 585/2002, anexa, art. 21** · `[HG-21]`  
> Articolul 21  
> Ori de câte ori este posibil, emitentul unui document clasificat trebuie să precizeze dacă acesta poate fi declasificat ori trecut la un nivel inferior de secretizare, la o anumită dată sau la producerea unui anumit eveniment.  

> **HG 585/2002, anexa, art. 22** · `[HG-22]`  
> Articolul 22  
> (1)La schimbarea clasei sau nivelului de secretizare atribuit inițial unei informaţii, emitentul este obligat să încunoștințeze structura/funcționarul de securitate, care va face menţiunile necesare în registrele de evidență.  
> (2)Data şi noua clasă sau nivel de secretizare vor fi marcate pe document deasupra sau sub vechea inscripție, care va fi anulată prin trasarea unei linii oblice.  
> (3)Emitentul informaţiilor declasificate ori trecute în alt nivel de clasificare se va asigura că gestionarii acestora sunt anunțati la timp, în scris, despre acest lucru.  

> **HG 585/2002, anexa, art. 14** · `[HG-14]`  
> Articolul 14  
> (1)Documentul elaborat pe baza prelucrarii informaţiilor cu niveluri de secretizare diferite va fi clasificat conform noului conținut, care poate fi superior originalelor.  
> (2)Documentul rezultat din cumularea neprelucrată a unor extrase provenite din informaţii clasificate va primi clasa sau nivelul de secretizare corespunzător conținutului extrasului cu cel mai înalt nivel de secretizare.  
> (3)Rezumatele, traducerile şi extrasele din documentele clasificate primesc clasa sau nivelul de secretizare corespunzător conținutului.  

> **HG 585/2002, anexa, art. 45** · `[HG-45]`  
> Articolul 45  
> Informaţiile clasificate vor fi marcate, inscriptionate şi gestionate numai de către persoane care au autorizație sau certificat de securitate corespunzător nivelului de clasificare a acestora.  

> **HG 585/2002, anexa, art. 46** · `[HG-46]`  
> Articolul 46  
> (1)Toate documentele, indiferent de formă, care conţin informaţii clasificate au înscrise, pe fiecare pagină, nivelul de secretizare.  
> (2)Nivelul de secretizare se marchează prin ștampilare, dactilografiere, tipărire sau olograf, astfel:  
> a)în partea dreaptă sus şi jos, pe exteriorul copertelor, pe pagina cu titlul şi pe prima pagină a documentului;  
> b)în partea de jos şi de sus, la mijlocul paginii, pe toate celelalte pagini ale documentului;  
> c)sub legendă, titlu sau scara de reprezentare şi în exterior - pe verso - atunci când acestea sunt pliate, pe toate schemele, diagramele, hărțile, desenele şi alte asemenea documente.  

> **HG 585/2002, anexa, art. 47** · `[HG-47]`  
> Articolul 47  
> Porțiunile clar identificabile din documentele clasificate complexe, cum sunt secţiunile, anexele, paragrafele, titlurile, care au niveluri diferite de secretizare sau care nu sunt clasificate, trebuie marcate corespunzător nivelului de clasificare şi secretizare.  

> **HG 585/2002, anexa, art. 48** · `[HG-48]`  
> Articolul 48  
> Marcajul de clasificare va fi aplicat separat de celelalte marcaje, cu caractere şi/sau culori diferite.  

> **HG 585/2002, anexa, art. 49** · `[HG-49]`  
> Articolul 49  
> (1)Toate documentele clasificate aflate în lucru sau în stadiu de proiect vor avea inscrise menţiunile "Document în lucru" sau "Proiect" şi vor fi marcate potrivit clasei sau nivelului de secretizare a informaţiilor ce le conţin.  
> (2)Gestionarea documentelor clasificate aflate în lucru sau în stadiu de proiect se face în aceleași condiţii ca şi a celor în forma definitivă.  

> **HG 585/2002, anexa, art. 56** · `[HG-56]`  
> Articolul 56  
> (1)Atunci când se utilizează documente clasificate ca surse pentru întocmirea unui alt document, marcajele documentelor sursă le vor determina pe cele ale documentului rezultat.  
> (2)Pe documentul rezultat se vor preciza documentele sursă care au stat la baza întocmirii lui.  

> **HG 585/2002, anexa, art. 285** · `[HG-285]`  
> Articolul 285  
> Informaţiile clasificate în format electronic trebuie să fie controlate conform regulilor INFOSEC, înainte de a fi transmise din zonele SPAD şi RTD - SIC sau din cele cu terminale la distanță.  

> **HG 585/2002, anexa, art. 286** · `[HG-286]`  
> Articolul 286  
> Modul în care este prezentată informația în clar, chiar dacă se utilizează codul prescurtat de transmisie sau reprezentarea binară ori alte forme de transmitere la distanță, nu trebuie să influențeze nivelul de clasificare acordat informaţiilor respective.  

> **HG 585/2002, anexa, art. 287** · `[HG-287]`  
> Articolul 287  
> Când informaţiile sunt transferate între diverse SPAD sau RTD - SIC, ele trebuie să fie protejate atât în timpul transferului, cât şi la nivelul sistemelor informatice ale beneficiarului, corespunzător cu nivelul de clasificare al informaţiilor transmise.  

> **HG 585/2002, anexa, art. 337** · `[HG-337]`  
> Articolul 337  
> Marcarea informaţiilor cu destinație specială se aplică, în mod obișnuit, informaţiilor clasificate care necesită o distribuţie limitată şi manipulare specială, suplimentar faţă de caracterul atribuit prin clasificarea de securitate.  

**Interpretare:** art. 46 impune nivelul de secretizare pe **fiecare pagină** a „documentelor, indiferent de formă”, sus și jos; art. 56 face ca marcajul unui document derivat să fie determinat de sursele sale; art. 49 cere mențiunea „Document în lucru”/„Proiect”. Fără un câmp de clasificare la nivel de caz/probă, aplicația nu poate produce rapoarte conforme. Formatele electronice nepaginate (CSV, JSON, ZIP) nu sunt tratate expres de text; **neverificat** cum acceptă AAS marcarea lor (propunere: antet în fișier + câmp în manifest).

### 1.11 Medii de stocare, copiere, echipamente


> **Ghid PrOpSec (DS 2), art. 16** · `[PRP-16]`  
> Art. 16  
> (1) Dispozitivele portabile de calcul și comunicații includ laptopuri, agende electronice și palmtop cu capacitate de stocare, procesare și/sau transmitere (de exemplu: PDA, BlackBerry, tablete) și telefoane celulare/telefoane mobile GSM cu funcționalitate de PDA.  
> (2) PrOpSec trebuie să conțină instrucțiuni pe care utilizatorii trebuie să le aplice când utilizează dispozitive portabile de calcul și comunicații cum sunt cele menționate la alin. (1) în misiuni oficiale în afara organizației.  
> (3) PrOpSec trebuie să includă prevederi de tipul: "Dispozitivele portabile de calcul și comunicații pot fi scoase în afara (.......... denumirea organizației..........) pentru a fi utilizate în cadrul unei misiuni oficiale numai cu aprobarea AAS. Echipamentele, precum și mediile de stocare și documentația asociate vor fi protejate pe întreaga perioadă în conformitate cu standardele de securitate aplicabile celui mai înalt nivel de clasificare a informațiilor stocate sau procesate. Dispozitivul portabil de calcul și comunicații trebuie gestionat ca document cu nivel de clasificare similar celui pentru care a fost acreditat dispozitivul. Informațiile clasificate trebuie să fie stocate pe medii de stocare detașabile, etichetate corespunzător (de exemplu, dispozitive de memorie USB), care trebuie stocate în locații adecvate. Hard diskul dispozitivului portabil de calcul este criptat utilizând un mecanism de criptare adecvat, a cărui utilizare a fost aprobată de AAS. În această situație dispozitivul portabil de calcul poate fi lăsat fără supraveghere (de exemplu: într-o cameră de hotel), dar trebuie luate măsurile aplicabile obiectelor de valoare. Dispozitivul portabil de calcul trebuie purtat într-o servietă care se poate încuia, ale cărei dimensiuni permit păstrarea acesteia în permanență asupra posesorului. Atunci când transportul se realizează cu linii aeriene comerciale, personalul de securitate al aeroportului poate inspecta echipamentul, cu condiția ca această operațiune să nu conducă la deteriorarea componentelor electronice sau la accesul la informațiile clasificate. Trebuie luate măsuri pentru a se evita furtul dispozitivului. La sediul la care se desfășoară misiunea trebuie respectate regulile de securitate locale, care pot include inspecția tehnică de securitate a dispozitivului portabil de calcul, operațiune realizată de personal specializat. Regulile de securitate locale trebuie respectate și în ceea ce privește schimbul de informații. Toate mediile de stocare introduse în dispozitivul portabil de calcul trebuie verificate pentru a se identifica eventualul software malițios. Pierderea dispozitivelor portabile de calcul și comunicații, precum și a mediilor de stocare asociate acestora trebuie raportată imediat..........(se precizează autoritatea responsabilă din cadrul organizației).......... Echipamente proprietate privată Este interzisă utilizarea dispozitivelor portabile de calcul pentru stocarea, procesarea sau transmiterea informațiilor clasificate. Luarea la cunoștință a responsabilităților La plecarea în misiune, personalul trebuie să ia o copie a PrOpSec. Personalul trebuie să semneze o declarație potrivit căreia este pe deplin conștient de responsabilitățile ce îi revin în ceea ce privește protecția echipamentelor și a informațiilor asociate. Puncte de contact Îndrumări suplimentare pot fi obținute de la administratorul de sistem și cel de securitate..........(se precizează datele de contact)..........“  
> (4) În situația în care un dispozitiv portabil de calcul sau comunicații conținând mecanisme de securitate (de exemplu, mecanisme criptografice) este utilizat pentru o misiune oficială, condițiile privind transportul, protecția și utilizarea trebuie stabilite în PrOpSec.  

> **HG 585/2002, anexa, art. 288** · `[HG-288]`  
> Articolul 288  
> Toate mediile de stocare a informaţiilor se păstrează într-o modalitate care să corespundă celui mai înalt nivel de clasificare a informaţiilor stocate sau suporților, fiind protejate permanent.  

> **HG 585/2002, anexa, art. 289** · `[HG-289]`  
> Articolul 289  
> Copierea informaţiilor clasificate situate pe medii de stocare specifice TIC se execută în conformitate cu prevederile din procedurile operaționale de securitate.  

> **HG 585/2002, anexa, art. 290** · `[HG-290]`  
> Articolul 290  
> Mediile refolosibile de stocare a informaţiilor utilizate pentru înregistrarea informaţiilor clasificate îşi mențin cea mai înaltă clasificare pentru care au fost utilizate anterior, până când respectivelor informaţii li se reduce nivelul de clasificare sau sunt declasificate, moment în care mediile susmenționate se reclasifică în mod corespunzător sau sunt distruse în conformitate cu prevederile procedurilor operaționale de securitate.  

> **HG 585/2002, anexa, art. 292** · `[HG-292]`  
> Articolul 292  
> (1)Mediile de stocare care conţin informaţii clasificate utilizate în interiorul unei zone SPAD pot fi manipulate ca unic material clasificat, cu condiţia ca materialul să fie identificat, marcat cu nivelul său de clasificare şi controlat în interiorul zonei SPAD, până în momentul în care este distrus, redus la o copie de arhivă sau pus într-un dosar permanent.  
> (2)Evidențele acestora vor fi menținute în cadrul zonei SPAD până când sunt supuse controlului sau distruse, conform prezentelor standarde.  

> **HG 585/2002, anexa, art. 294** · `[HG-294]`  
> Articolul 294  
> (1)Toate mediile de stocare secrete de stat se identifică şi se controlează în mod corespunzător nivelului de secretizare.  
> (2)Pentru informaţiile neclasificate sau secrete de serviciu se aplică regulamente de securitate interne.  
> (3)Identificarea şi controalele trebuie să asigure următoarele cerinţe:  
> a)Pentru nivelul secret:  
> – un mijloc de identificare - număr de serie şi marcajul nivelului de clasificare - pentru fiecare astfel de mediu, în mod separat;  
> – proceduri bine definite pentru emiterea, primirea, retragerea, distrugerea sau păstrarea mediilor de stocare;  
> – evidențele manuale sau tipărite la imprimantă, indicând conţinutul şi nivelul de secretizare a informaţiilor înregistrate pe mediile de stocare.  
> b)Pentru nivelul strict secret şi strict secret de importanţă deosebită, informaţiile detaliate asupra mediului de stocare, incluzând conţinutul şi nivelul de clasificare, se ţin într-un registru adecvat.  

> **HG 585/2002, anexa, art. 295** · `[HG-295]`  
> Articolul 295  
> Controlul punctual şi de ansamblu al mediilor de stocare, pentru a asigura compatibilitatea cu procedurile de identificare şi control în vigoare, trebuie să asigure îndeplinirea următoarelor cerinţe:  
> a)pentru nivelul secret - controalele punctuale ale prezentei fizice şi continutului mediilor de stocare se efectueaza periodic, verificandu-se dacă acele medii de stocare nu conţin informaţii cu un nivel de clasificare superior;  
> b)pentru nivelul strict secret - toate mediile de stocare se inventariaza periodic, controland punctual prezenta lor fizica şi conţinutul, pentru a verifica dacă pe acele medii nu sunt stocate informaţii cu un nivel de clasificare superior;  
> c)pentru nivelul strict secret de importanţa deosebită, toate mediile se verifica periodic, cel puţin anual şi se controlează punctual, în legătură cu prezenta fizica şi conţinutul lor.  

> **HG 585/2002, anexa, art. 331** · `[HG-331]`  
> Articolul 331  
> (1)Microcalculatoarele sau calculatoarele personale care au discuri fixe sau alte medii nevolatile de stocare a informației, ce operează autonom sau ca parte a unei rețele, precum şi calculatoarele portabile cu discuri fixe sunt considerate medii de stocare a informaţiilor, în același sens ca şi celelalte medii amovibile de stocare a informaţiilor.  
> (2)În măsura în care acestea stochează informaţii clasificate trebuie supuse prezentelor standarde.  

> **HG 585/2002, anexa, art. 332** · `[HG-332]`  
> Articolul 332  
> Echipamentelor prevăzute la art. 331 trebuie să li se acorde nivelul de protecţie pentru acces, manipulare, stocare şi transport, corespunzător cu cel mai înalt nivel de clasificare a informaţiilor care au fost vreodată stocate sau procesate pe ele, până la trecerea la un alt nivel de clasificare sau declasificarea lor, în conformitate cu procedurile legale.  

> **HG 585/2002, anexa, art. 333** · `[HG-333]`  
> Articolul 333  
> (1)Este interzisă utilizarea mediilor de stocare amovibile, a software-ului şi a hardware-ului, aflate în proprietate privată, pentru stocarea, procesarea şi transmiterea informaţiilor secrete de stat.  
> (2)Pentru informaţiile secrete de serviciu sau neclasificate, se aplică reglementările interne ale unităţii.  

> **HG 585/2002, anexa, art. 334** · `[HG-334]`  
> Articolul 334  
> Este interzisă introducerea mediilor de stocare amovibile, a software-ului şi hardware-ului, aflate în proprietate privată, în zonele în care se stochează, se procesează sau se transmit informaţii clasificate, fără aprobarea conducătorului unităţii.  

> **HG 585/2002, anexa, art. 335** · `[HG-335]`  
> Articolul 335  
> Utilizarea într-un obiectiv a echipamentelor şi a software-ului contractanților, pentru stocarea, procesarea sau transmiterea informaţiilor clasificate este permisă numai cu avizul CSTIC şi aprobarea șefului unităţii.  

> **HG 585/2002, anexa, art. 336** · `[HG-336]`  
> Articolul 336  
> Utilizarea într-un obiectiv a echipamentelor şi software-ului puse la dispoziţie de către alte institutii poate fi permisă, în acest caz echipamentele sunt evidențiate în inventarul unităţii, în ambele situaţii, trebuie obţinut avizul CSTIC.  

### 1.12 Ștergere, declasificare, distrugere


> **HG 585/2002, anexa, art. 23** · `[HG-23]`  
> Articolul 23  
> (1)Informaţiile clasificate despre care s-a stabilit cu certitudine că sunt compromise sau iremediabil pierdute vor fi declasificate.  
> (2)Declasificarea se face numai în baza cercetării prin care s-a stabilit compromiterea sau pierderea informaţiilor respective ori a suportului material al acestora, cu acordul scris al emitentului.  

> **HG 585/2002, anexa, art. 76** · `[HG-76]`  
> Articolul 76  
> (1)Informaţiile clasificate iesite din termenul de clasificare se arhivează sau se distrug.  
> (2)Arhivarea sau distrugerea unui document clasificat se menţionează în registrul de evidență principal, prin consemnarea cotei arhivistice de regaăsire sau, după caz, a numărului de înregistrare a procesului-verbal de distrugere.  
> (3)Distrugerea informaţiilor clasificate înlocuite sau perimate se face numai cu avizul emitentului.  
> (4)Distrugerea documentelor clasificate sau a ciornelor care conţin informaţii cu acest caracter se face astfel încât să nu mai poată fi reconstituite.  

> **HG 585/2002, anexa, art. 77** · `[HG-77]`  
> Articolul 77  
> (1)Documentele de lucru, ciornele sau materialele acumulate sau create în procesul de elaborare a unui document, care conţin informaţii clasificate, de regulă, se distrug.  
> (2)În cazul în care se păstrează, acestea vor fi datate, marcate cu clasa sau nivelul de secretizare cel mai înalt al informaţiilor conținute, arhivate şi protejate corespunzător clasei sau nivelului de secretizare a documentului final.  

> **HG 585/2002, anexa, art. 78** · `[HG-78]`  
> Articolul 78  
> (1)Informaţiile strict secrete de importanţă deosebită destinate distrugerii vor fi înapoiate unităţii emitente cu adresa de restituire.  
> (2)Fiecare asemenea informație va fi trecută pe un proces-verbal de distrugere, care va fi aprobat de conducerea unităţii şi semnat de șeful structurii/funcționarul de securitate şi de persoana care asistă la distrugere, autorizată să aibă acces la informaţii strict secrete de importanţă deosebită.  
> (3)În situaţii de urgență, protecția, inclusiv prin distrugere, a materialelor şi documentelor strict secrete de importanţă deosebită va avea întotdeauna prioritate faţă de alte documente sau materiale.  
> (4)Procesele-verbale de distrugere şi documentele de evidență ale acestora vor fi arhivate şi păstrate cel puţin 10 ani.  

> **HG 585/2002, anexa, art. 79** · `[HG-79]`  
> Articolul 79  
> (1)Distrugerea informaţiilor strict secrete, secrete şi secrete de serviciu va fi evidențiată într-un proces-verbal semnat de două persoane asistente autorizate să aibă acces la informaţii de acest nivel, avizat de structura/funcționarul de securitate şi aprobat de conducătorul unităţii.  
> (2)Procesele-verbale de distrugere şi documentele de evidență a informaţiilor strict secrete, secrete şi secrete de serviciu vor fi păstrate de compartimentul care a executat distrugerea, o perioadă de cel puţin trei ani, după care vor fi arhivate şi păstrate cel puţin 10 ani.  

> **HG 585/2002, anexa, art. 284** · `[HG-284]`  
> Articolul 284  
> Când nu mai sunt utilizate, informaţiile şi materialele de control specificate la articolul precedent trebuie să fie distruse conform prevederilor prezentelor standarde.  

> **HG 585/2002, anexa, art. 296** · `[HG-296]`  
> Articolul 296  
> Informaţiile clasificate înregistrate pe medii de stocare refolosibile se șterg doar în conformitate cu procedurile operaționale de securitate.  

> **HG 585/2002, anexa, art. 297** · `[HG-297]`  
> Articolul 297  
> (1)Când un mediu de stocare urmează sa iasă din uz, trebuie să fie declasificat suprimându-se orice marcaje de clasificare, ulterior putând fi utilizat ca mediu de stocare nesecret. Dacă acesta nu poate fi declasificat, trebuie distrus printr-o procedură aprobată.  
> (2)Sunt interzise declasificarea şi refolosirea mediilor de stocare care conţin informaţii strict secrete de importanţă deosebită, acestea putând fi numai distruse, în conformitate cu procedurile operaționale de securitate.  

> **HG 585/2002, anexa, art. 298** · `[HG-298]`  
> Articolul 298  
> Informaţiile clasificate în format electronic stocate pe un mediu de unică folosinţă - cartele, benzi perforate trebuie distruse conform prevederilor procedurilor operaționale de securitate.  

**Interpretare:** ștergerea suporturilor reutilizabile se face „doar în conformitate cu procedurile operaționale de securitate” (art. 296); distrugerea se consemnează în proces-verbal semnat de **două** persoane autorizate (art. 79 alin. (1)). O funcție software de suprascriere nu înlocuiește procedura aprobată; ea poate fi doar un instrument al procedurii, dacă AAS o aprobă (**neverificat**).

### 1.13 Interconectare, izolare, comunicații, TEMPEST


> **HG 585/2002, anexa, art. 300** · `[HG-300]`  
> Articolul 300  
> Într-un SPAD - SIC trebuie să se dispună mijloace de interzicere a accesului la informaţiile clasificate de la toate terminalele/stațiile de lucru la distanță, atunci când se solicită acest lucru, prin deconectare fizică sau prin proceduri software speciale, aprobate de către autoritatea de acreditare de securitate.  

> **HG 585/2002, anexa, art. 304** · `[HG-304]`  
> Articolul 304  
> Procesarea informaţiilor se realizează în conformitate cu procedurile operaționale de securitate, prevăzute în prezentele standarde.  

> **HG 585/2002, anexa, art. 305** · `[HG-305]`  
> Articolul 305  
> Transmiterea informaţiilor secrete de stat către instalaţii automate - a căror funcționare nu necesită prezența unui operator uman - este interzisă, cu excepţia cazului când se aplică reglementări speciale aprobate de către autoritatea de acreditare de securitate, iar acestea au fost specificate în procedurile operaționale de securitate.  

> **INFOSEC 2, art. 35** · `[I2-35]`  
> Art. 35  
> (1) În vederea atingerii obiectivelor asumate, organizațiile au nevoie să își interconecteze propriile SIC cu SIC ale altor organizații, cu diferite comunități de interes, diferite nivele de clasificare și diferite standarde de securitate.  
> (2) În scopul respectării principiului SIC autoprotejat, sunt necesare analizarea riscului potențial reprezentat de interconectare, fie aceasta în mod direct sau în cascadă, și implementarea de măsuri de securitate specifice pentru protejarea interconectării.  
> (3) Pentru toate interconectările SIC care vehiculează informații clasificate este necesar ca AAS să aprobe:  
> a) metoda de interconectare și serviciile oferite;  
> b) metodologia de management al riscului și rezultatele analizei riscului;  
> c) arhitectura de securitate și măsurile de securitate pentru asigurarea respectării obiectivelor securității;  
> d) documentația de securitate, inclusiv planul de testare a securității și rezultatele aplicării acestui plan.  

> **INFOSEC 2, art. 37** · `[I2-37]`  
> Art. 37  
> (1) Cerințele privind măsurile de protecție ce trebuie implementate în SIC care vehiculează informații clasificate și sunt conectate la internet sau la rețele similare din domeniul public trebuie să țină seama de riscurile de securitate excepționale pe care aceste tipuri de rețele publice le ridică, din cauza accesibilității necontrolate, pe scară largă, a susceptibilității create de protocoalele orientate pe lipsa conectării și a vulnerabilității SIC finale față de exploatare.  
> (2) Interconectarea directă sau în cascadă la internet sau la alte rețele similare din domeniul public a SIC care vehiculează informații clasificate de nivel maxim STRICT SECRET sau echivalent trebuie să fie:  
> a) strict controlată;  
> b) în conformitate cu cerințele stabilite de AAS;  
> c) evaluată și certificată din punctul de vedere al mecanismelor de securitate;  
> d) supusă unei analize periodice oficiale a vulnerabilităților.  
> (3) Conectarea directă sau tip cascadă a SIC care vehiculează informații clasificate STRICT SECRET DE IMPORTANȚĂ DEOSEBITĂ sau echivalent ori informații din Categoria specială la internet sau la rețele similare din domeniul public este interzisă.  

> **HG 585/2002, anexa, art. 302** · `[HG-302]`  
> Articolul 302  
> Toate echipamentele SPAD şi RTD - SIC vor fi instalate în conformitate cu reglementările specifice în vigoare, emise de către instituția desemnată la nivel naţional pentru protecția informaţiilor clasificate, cu directivele şi standardele tehnice corespunzătoare.  

> **HG 585/2002, anexa, art. 303** · `[HG-303]`  
> Articolul 303  
> Sistemele SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat vor fi protejate corespunzător faţă de vulnerabilitățile de securitate cauzate de radiațiile compromițătoare - TEMPEST.  

**Interpretare:** (a) un SPAD „exploatat în mod autonom, deconectat în mod permanent” primește măsuri stabilite de AAS (art. 282) — standalone-ul nu e scutit, doar tratat distinct; (b) orice conexiune a unui SIC care vehiculează informații clasificate la alt sistem trebuie aprobată de AAS (INFOSEC 2 art. 35 alin. (3)); (c) TEMPEST (art. 302-303) este o cerință de instalare/echipament — **NOT_APPLICABLE software**; singura implicație: aplicația nu trebuie să pretindă că măsoară sau garantează TEMPEST.

### 1.14 Incidente de securitate


> **HG 585/2002, anexa, art. 88** · `[HG-88]`  
> Articolul 88  
> (1)Conducătorii unităţilor deținătoare de informaţii secrete de stat au obligaţia de a înștiinta, în scris, instituţiile prevăzute la art. 25 din Legea nr. 182/2002, potrivit competențelor, prin cel mai operativ sistem de comunicare, despre compromiterea unor astfel de informaţii.  
> (2)Înștiințarea prevăzută la alin. (1) se face în scopul obținerii sprijinului necesar pentru recuperarea informaţiilor, evaluarea prejudiciilor, diminuarea şi înlăturarea consecințelor.  
> (3)Înștiințarea trebuie să conţină:  
> a)prezentarea informaţiilor compromise, respectiv clasificarea, marcarea, conţinutul, data emiterii, numărul de înregistrare şi de exemplare, emitentul şi persoana sau compartimentul care le-a gestionat;  
> b)o scurtă prezentare a împrejurărilor în care a avut loc compromiterea, inclusiv data constatării, perioada în care informaţiile au fost expuse compromiterii şi persoanele neautorizate care au avut sau ar fi putut avea acces la acestea, dacă sunt cunoscute;  
> c)precizări cu privire la eventuala informare a emitentului.  
> (4)La solicitarea instituţiilor competente, înstiințările preliminare vor fi completate pe măsura derulării cercetărilor.  
> (5)Documentele privind evaluarea prejudiciilor şi activităţile ce urmează a fi intreprinse ca urmare a compromiterii vor fi prezentate instituţiilor competente.  

> **INFOSEC 2, art. 50** · `[I2-50]`  
> Art. 50  
> (1) Un incident de securitate în SIC reprezintă orice anomalie detectată care a compromis sau are potențialul de a compromite sistemele de comunicații, sistemele informatice ori alte sisteme electronice sau informațiile stocate, procesate ori transmise prin intermediul acestor sisteme.  
> (2) Pentru gestionarea incidentelor de securitate se desemnează personal specializat din punct de vedere tehnic.  

> **INFOSEC 2, art. 51** · `[I2-51]`  
> Art. 51  
> Incidentele care vizează securitatea SIC se raportează la ORNISS.  

Atribuția agenției de securitate pentru informatică și comunicații privind incidentele (HG 585, art. 256 lit. c)-d)) este în `[HG-256]`:


> **HG 585/2002, anexa, art. 256** · `[HG-256]`  
> Articolul 256  
> Agentia este responsabilă de conceperea şi implementarea mijloacelor, metodelor şi masurilor de protecţie a informaţiilor clasificate care sunt stocate, procesate sau transmise prin intermediul SPAD şi RTD - SIC şi are, în principal, următoarele atribuţii:  
> a)coordonează activităţile de protecţie a informaţiilor clasificate care sunt stocate, procesate sau transmise prin intermediul SPAD şi RTD - SIC;  
> b)elaborează şi promovează reglementări şi standarde specifice;  
> c)analizeaza cauzele incidentelor de securitate şi gestionează baza de date privind amenințările şi vulnerabilitatile din sistemele de comunicație şi informatice, necesare pentru elaborarea managementul de risc;  
> d)semnalează agenției de acreditare de securitate incidentele de securitate în domeniu;  
> e)integrează măsurile privind protecția fizică, de personal, a documentelor administrative, COMPUSEC, COMSEC, TEMPEST şi criptografică;  
> f)execută inspecţii periodice asupra SPAD şi RTD - SIC în vederea reacreditării;  
> g)supune certificării şi autorizării sistemele de securitate specifice SPAD şi RTD - SIC.  

**Interpretare:** formularul de raportare a incidentelor INFOSEC este în INFOSEC 3 (nepublic — **neverificat**). Aplicația trebuie să poată furniza datele brute (cine, ce, când, ce probă) dintr-un registru protejat.

### 1.15 Sancțiuni (de ce contează)


> **HG 585/2002, art. 338 alin. (1) lit. e)** · `[HG-338-1e]`  
> e)neîndeplinirea sau îndeplinirea defectuoasa a obligaţiilor prevăzute în art. 240 alin. (2) şi (3), art. 243 şi art. 248, precum şi nerespectarea regulilor prevăzute în art. 274-336.  

### 1.16 Modificări ulterioare ale HG 585/2002 și norme ORNISS

* Modificări **verificate** în consolidările folosite: HG 2.202/2004 și HG 185/2005 (nota CTCE din antetul `legistm.pdf`). Cele trei copii secundare nu mai arată alte modificări până la 28.11.2022.
* Un proiect MApN de modificare a standardelor (aerofotografiere etc., `sg.mapn.ro/proiecte/ProiectHG_1546568756.doc`) a fost semnalat de căutarea web; **neverificat** dacă a fost adoptat; nu pare să atingă capitolul INFOSEC.
* **Modificări după 28.11.2022: neverificat** — nu s-a putut consulta `legislatie.just.ro` / Monitorul Oficial; acțiune pentru proprietar: confirmarea formei consolidate la zi.
* Norme ORNISS INFOSEC referite prin titlu în texte publice:

| Normă | Titlu (cum apare în text) | Public? | Conținut folosit aici |
|---|---|---|---|
| INFOSEC 1 | «Directiva privind structurile cu responsabilități în domeniul INFOSEC», ord. dir. gen. ORNISS nr. 86/2013 | titlu public (în PRP-19); text **nepublic / neverificat** | niciunul |
| INFOSEC 2 | «Directiva principală privind domeniul INFOSEC», ord. ORNISS nr. 16/2014 (MO 262/10.04.2014); abrogă ord. 483/2003 | **public** (mirror legeaz.net) | citate `[I2-*]` |
| INFOSEC 3 | «Directiva privind managementul INFOSEC pentru sisteme informatice și de comunicații», ord. ORNISS nr. 484/2003 | titlu public; text și formularul de incident **nepublice / neverificate** | niciunul |
| DS 2 / PrOpSec | «Ghid privind structura și conținutul PrOpSec pentru SIC – DS 2», ord. ORNISS nr. 18/2014 (MO 242/04.04.2014) | **public** (mirror legeaz.net) | citate `[PRP-*]` |
| Catalogul național cu pachete, produse și profile de protecție INFOSEC | ord. ORNISS 34/2014 (MO 348/12.05.2014), actualizat prin 45/2014, 10/2016, 52/2016; mod. 12/2013 | titlurile publice (legeaz.net); **conținutul catalogului neverificat** | niciunul |
| Documentația-model de acreditare SIC «secret de serviciu» («Documentația de acreditare de securitate a SIC» + 23 de anexe) | publicată de SRI (`Precizari_referitoare_la_acreditarea_SIC_SSv.pdf`) | pagina de precizări publică; arhiva cu modelul **neobținută — neverificat** | doar denumirea |
| Norme criptografice, TEMPEST, formulare ORNISS | — | **nepublic / neverificat** | niciunul |


### 1.17 P2 și P3 — NIS2, OUG 155/2024, Reg. (UE) 2024/2690

**Avertismente de aplicabilitate.** (1) Dir. 2022/2555 și OUG 155/2024 obligă **entitățile**, nu produsele software; cerințele de mai jos devin cerințe pentru aplicație doar în măsura în care entitatea o folosește ca parte a sistemelor proprii (art. 21 alin. (1) NIS2). (2) Reg. (UE) 2024/2690 se aplică numai entităților enumerate în art. 1; dacă proprietarul/clienții sunt într-o astfel de categorie este **neverificat**. Anexa regulamentului este folosită aici ca **standard tehnic de referință** pentru P2/P3, nu ca obligație stabilită.


> **OUG 155/2024, art. 2 alin. (1) lit. a)** · `[OUG-2]`  
> Art. 2. — (1) Scopul prezentei ordonanțe de urgență îl constituie: a) stabilirea măsurilor de gestionare a riscurilor de securitate cibernetică pentru spațiul cibernetic național civil și a obligațiilor de raportare a incidentelor pentru entitățile esențiale și importante;  

> **OUG 155/2024, art. 63** · `[OUG-63]`  
> Art. 63. — DNSC informează instituțiile cu atribuții de coordonare a activității și control în domeniul protecției informațiilor clasificate astfel cum sunt acestea stabilite prin Legea nr. 182/2002 privind protecția informațiilor clasificate, cu modificările și completările ulterioare, și actele normative subsecvente dacă se constată că incidentele de securitate cibernetică pot avea impact în planul protecției datelor și informațiilor secrete de stat sau secrete de serviciu.  

> **OUG 155/2024, art. 11** · `[OUG-11]`  
> Art. 11. — (1) Entitățile esențiale și entitățile importante iau măsuri tehnice, operaționale și organizatorice proporționale și adecvate pentru a identifica, evalua și gestiona riscurile aferente securității rețelelor și a sistemelor informatice pe care acestea le utilizează în desfășurarea activităților lor sau furnizarea serviciilor lor, precum și pentru a elimina sau, după caz, a reduce efectele incidentelor asupra destinatarilor serviciilor lor și asupra altor servicii.  
> (2) Măsurile prevăzute la alin. (1) trebuie să asigure un nivel de securitate cibernetică adecvat nivelului de risc al entității, ținând seama de stadiul actual al tehnologiei și, după caz, de cele mai relevante standarde și bune practici naționale, europene și internaționale, cât și de costurile de punere în aplicare a acestor măsuri.  
> (3) Nivelul de risc al entității se evaluează conform metodologiei de evaluare a nivelului de risc al entităților cuprinse în ordinul directorului DNSC prevăzut la art. 10 alin. (2).  
> (4) Măsurile prevăzute la alin. (1) trebuie să cuprindă o abordare cuprinzătoare a amenințărilor cibernetice în vederea asigurării protecției rețelelor și a sistemelor informatice atât la nivel logic, cât și fizic împotriva incidentelor, inclusiv prin jurnalizarea și asigurarea trasabilității tuturor activităților în cadrul rețelelor și sistemelor informatice.  
> (5) Entitățile esențiale și entitățile importante sunt obligate să se supună efectuării unui audit de securitate cibernetică în condițiile și cu periodicitatea stabilite prin ordinul directorului DNSC prevăzut la art. 12 alin. (1), în funcție de nivelul de risc prevăzut la alin. (3).  
> (6) Atunci când există autoritatea cu competențe sectoriale, condițiile și periodicitatea auditului de securitate prevăzute la alin. (5) vor fi stabilite prin ordin comun în condițiile art. 37 alin. (8) lit. b), în funcție de nivelul de risc prevăzut la alin. (3).  
> (7) Entitățile esențiale și importante pun la dispoziția DNSC, la cerere, lista activelor relevante și lista riscurilor identificate în urma analizei riscurilor, prevăzută la alin. (1).  
> (8) Cu privire la securitatea lanțului de aprovizionare, măsurile prevăzute la alin. (1) trebuie să țină seama de: a) vulnerabilitățile specifice ale fiecărui furnizor direct și ale fiecărui furnizor de servicii, de calitatea generală a produselor și de calitatea practicilor de securitate cibernetică ale furnizorilor direcți și ale furnizorilor de servicii, inclusiv de securitatea proceselor de dezvoltare ale acestora; b) rezultatele evaluărilor coordonate ale riscurilor efectuate care au în vedere rezultatele evaluărilor coordonate ale riscurilor de securitate a lanțurilor critice de aprovizionare elaborate la nivelul Uniunii Europene în cadrul Grupului de cooperare.  
> (9) În vederea asigurării securității lanțului de aprovizionare, entitățile esențiale și entitățile importante au obligația să transmită către DNSC, la cerere, date cu privire la prestatorii de servicii de încredere, prestatorii de servicii de încredere calificați, furnizorii de servicii DNS, registrele de nume TLD sau entitățile care oferă servicii de înregistrare nume de domenii, furnizorii de servicii de cloud computing, furnizorii de servicii de centre de date, furnizorii de servicii gestionate și furnizorii de servicii de securitate gestionate și care le asigură aceste tipuri de servicii, în termenul prevăzut în cererea DNSC.  
> (10) Atunci când este evaluată proporționalitatea măsurilor de gestionare a riscurilor în conformitate cu dispozițiile alin. (1), se ține seama în mod corespunzător de amploarea expunerii la riscuri a entității și a serviciilor pe care le furnizează, de dimensiunea entității, de probabilitatea producerii unor incidente și de gravitatea acestora, inclusiv de impactul lor social și economic.  

> **OUG 155/2024, art. 13** · `[OUG-13]`  
> Art. 13. — Măsurile prevăzute la art. 11 alin. (1) cuprind cel puțin următoarele: a) politicile și procedurile referitoare la analiza riscurilor și la securitatea sistemelor informatice și revizuirea periodică a acestora; b) politicile și procedurile de evaluare a eficacității măsurilor de gestionare a riscurilor de securitate cibernetică; c) politicile și procedurile referitoare la utilizarea criptografiei și, după caz, a criptării; d) securitatea lanțului de aprovizionare, inclusiv aspectele legate de securitatea relației dintre entitate și prestatorii și furnizorii săi direcți; e) securitatea achiziției, dezvoltării, întreținerii și casării rețelelor și sistemelor informatice, inclusiv gestionarea și divulgarea vulnerabilităților; f) securitatea resurselor umane, politicile de control al accesului și gestionarea activelor; g) gestionarea incidentelor; h) continuitatea activității, inclusiv gestionarea copiilor de rezervă, redresarea în caz de dezastru și managementul crizelor; i) practicile de bază în materie de igienă cibernetică și formarea în domeniul securității cibernetice; j) utilizarea soluțiilor de autentificare multifactor sau de autentificare continuă a comunicațiilor vocale, video și text, a sistemelor de comunicații de urgență securizate și securizate în interiorul entității, după caz.  

> **Directiva 2022/2555, art. 21 alin. (1)-(2)** · `[NIS2-21]`  
> Articolul 21  
> Măsuri de gestionare a riscurilor în materie de securitate cibernetică  
> (1) Statele membre se asigură că entitățile esențiale și entitățile importante iau măsuri tehnice, operaționale și organizatorice adecvate și proporționale pentru a gestiona riscurile la adresa securității rețelelor și a sistemelor informatice pe care entitățile respective le utilizează pentru operațiunile lor sau pentru a furniza servicii și pentru a preveni sau reduce la minimum impactul incidentelor asupra beneficiarilor serviciilor lor și asupra altor servicii. Ținând seama de cele mai avansate standarde în domeniu și, atunci când este cazul, de standardele europene și internaționale relevante, precum și de costul punerii în aplicare, măsurile menționate la primul paragraf asigură un nivel de securitate a rețelelor și a sistemelor informatice corespunzător riscurilor prezentate. Atunci când se evaluează proporționalitatea acestor măsuri, se ține seama în mod corespunzător de gradul de expunere a entității la riscuri, de dimensiunea entității și de probabilitatea producerii incidentelor, precum și de gravitatea acestora, inclusiv de impactul lor societal și economic.  
> (2) Măsurile menționate la alineatul (1) se bazează pe o abordare multirisc care vizează protejarea rețelelor și a sistemelor informatice, precum și a mediului fizic al acestor sisteme împotriva incidentelor, și includ cel puțin următoarele:  
> (a) politici referitoare la analiza riscurilor și securitatea sistemelor informatice;  
> (b) gestionarea incidentelor;  
> (c) continuitatea activității, de exemplu gestionarea copiilor de rezervă și recuperarea în caz de dezastru, precum și gestionarea crizelor;  
> (d) securitatea lanțului de aprovizionare, inclusiv aspectele legate de securitate referitoare la relațiile dintre fiecare entitate și prestatorii sau furnizorii săi direcți de servicii;  
> (e) securitatea în achiziționarea, dezvoltarea și întreținerea rețelelor și a sistemelor informatice, inclusiv gestionarea vulnerabilităților și divulgarea acestora;  
> (f) politici și proceduri pentru a evalua eficacitatea măsurilor de gestionare a riscurilor în materie de securitate cibernetică;  
> (g) practici de bază în materie de igienă cibernetică și formare în domeniul securității cibernetice;  
> (h) politici și proceduri privind utilizarea criptografiei și, după caz, a criptării;  
> (i) securitatea resurselor umane, politicile de control al accesului și gestionarea activelor;  
> (j) utilizarea de soluții de autentificare multifactor sau de autentificare continuă, de comunicații securizate voce, video și text și de sisteme securizate de comunicații de urgență în cadrul entității, după caz.  

> **Directiva 2022/2555, art. 21 alin. (3)** · `[NIS2-21-3]`  
> (3) Statele membre se asigură că, atunci când analizează care măsuri menționate la alineatul (2) litera (d) de la prezentul articol sunt adecvate, entitățile iau în considerare vulnerabilitățile specifice fiecărui prestator și furnizor direct de servicii, precum și calitatea generală a produselor și a practicilor în materie de securitate cibernetică ale prestatorilor și furnizorilor lor de servicii, inclusiv procedurile lor securizate de dezvoltare. Statele membre se asigură, de asemenea, că, atunci când analizează care măsuri menționate la litera respectivă sunt adecvate, entitățile au obligația de a ține seama de rezultatele evaluărilor coordonate ale riscurilor de securitate la nivelul lanțurilor de aprovizionare critice efectuate în conformitate cu articolul 22 alineatul (1).  

> **Reg. 2024/2690, art. 1** · `[R2690-A1]`  
> Articolul 1  
> Obiect În ceea ce privește furnizorii de servicii DNS, registrele de nume TLD, furnizorii de servicii de cloud computing, furnizorii de servicii de centre de date, furnizorii de rețele de furnizare de conținut, furnizorii de servicii gestionate, furnizorii de servicii de securitate gestionate, furnizorii de piețe online, de motoare de căutare online și de platforme de servicii de socializare în rețea, precum și prestatorii de servicii de încredere (entitățile relevante), prezentul regulament stabilește cerințele tehnice și metodologice ale măsurilor menționate la articolul 21 alineatul (2) din Directiva (UE) 2022/2555 și precizează mai în detaliu cazurile în care un incident trebuie considerat semnificativ, astfel cum se menționează la articolul 23 alineatul (3) din Directiva (UE) 2022/2555.  

**Ordinul DNSC art. 12 alin. (1) OUG 155/2024** (măsurile concrete): **neverificat** — nu a fost găsit. Căutarea web a semnalat ordinele DNSC nr. 1/2025 (notificare pentru înregistrare) și nr. 2/2025 (criterii/praguri), publicate în MO nr. 776/20.08.2025 — **neverificat**, nefiind consultate; ele nu stabilesc măsurile tehnice.

#### Puncte din anexa Reg. 2024/2690 relevante pentru software (referință tehnică P2/P3)


> **Reg. 2024/2690, anexa, pct. 3.2** · `[R2690-3.2]`  
> 3.2. Monitorizare și jurnalizare  
> 3.2.1. Entitățile relevante stabilesc proceduri și utilizează instrumente pentru monitorizarea și jurnalizarea activităților în rețelele lor și în sistemele lor informatice pentru a detecta evenimentele care ar putea fi considerate incidente și pentru a răspunde în consecință pentru a atenua impactul.  
> 3.2.2. În măsura în care este fezabil, monitorizarea trebuie să fie automată și să se efectueze fie în mod continuu, fie la intervale periodice, în funcție de capacitățile operaționale. Entitățile relevante își pun în aplicare activitățile de monitorizare într-un mod care să reducă la minimum rezultatele fals pozitive și fals negative.  
> 3.2.3. Pe baza procedurilor menționate la punctul 3.2.1, entitățile relevante păstrează, documentează și revizuiesc jurnalele. Entitățile relevante întocmesc o listă a activelor care urmează să facă obiectul jurnalizării pe baza rezultatelor evaluării riscurilor efectuate în temeiul punctului 2.1. După caz, jurnalele includ:  
> (a) traficul de rețea relevant la ieșire și la intrare;  
> (b) crearea, modificarea sau eliminarea utilizatorilor rețelelor și sistemelor informatice ale entităților relevante și extinderea permisiunilor;  
> (c) accesul la sisteme și aplicații;  
> (d) evenimente legate de autentificare;  
> (e) toate accesurile privilegiate la sisteme și aplicații, precum și toate activitățile desfășurate de conturile administrative;  
> (f) accesul la configurația critică și la fișierele backup sau modificările aduse acestora;  
> (g) jurnale de evenimente și jurnale de la instrumente de securitate, cum ar fi antivirus, sisteme de detectare a intruziunilor sau firewall;  
> (h) utilizarea resurselor sistemului, precum și performanța acestora;  
> (i) accesul fizic la instalații;  
> (j) accesul la echipamentele și dispozitivele lor de rețea și utilizarea acestora;  
> (k) activarea, oprirea și întreruperea diferitelor jurnale;  
> (l) evenimente de mediu.  
> 3.2.4. Jurnalele sunt reexaminate periodic pentru a identifica orice tendință neobișnuită sau nedorită. După caz, entitățile relevante stabilesc valori adecvate pentru pragurile de alarmă. În cazul în care valorile stabilite pentru pragul de alarmă sunt depășite, se declanșează automat, dacă este adecvat, o alarmă. Entitățile relevante se asigură că, în cazul unei alarme, se inițiază în timp util un răspuns calificat și adecvat.  
> 3.2.5. Entitățile relevante păstrează jurnalele și realizează copii de rezervă ale acestora pentru o perioadă predefinită și le protejează împotriva accesului neautorizat sau a modificărilor neautorizate.  
> 3.2.6. În măsura în care este fezabil, entitățile relevante se asigură că toate sistemele dispun de surse ale orei sincronizate pentru a putea corela jurnalele între sisteme pentru evaluarea evenimentelor. Entitățile relevante întocmesc și păstrează o listă a tuturor activelor jurnalizate și se asigură că sistemele de monitorizare și de jurnalizare sunt redundante. Disponibilitatea sistemelor de monitorizare și de jurnalizare este monitorizată independent de sistemele pe care le monitorizează.  
> 3.2.7. Procedurile, precum și lista activelor jurnalizate sunt revizuite și, după caz, actualizate la intervale regulate și după incidente semnificative.  

> **Reg. 2024/2690, anexa, pct. 6.1** · `[R2690-6.1]`  
> 6.1. Securitatea în achiziționarea de servicii TIC sau de produse TIC  
> 6.1.1. În sensul articolului 21 alineatul (2) litera (e) din Directiva (UE) 2022/2555, entitățile relevante stabilesc și pun în aplicare, pe baza evaluării riscurilor efectuată în temeiul punctului 2.1, procese de gestionare a riscurilor care decurg din achiziționarea de servicii TIC sau de produse TIC de la furnizori sau prestatori de servicii pentru componente care sunt critice pentru securitatea rețelelor și a sistemelor informatice ale entităților relevante pe parcursul întregului lor ciclu de viață.  
> 6.1.2. În sensul punctului 6.1.1, procesele menționate la punctul 6.1.1 includ:  
> (a) cerințe de securitate aplicabile serviciilor TIC sau produselor TIC care urmează să fie achiziționate;  
> (b) cerințe privind actualizările de securitate pe întreaga durată de viață a serviciilor TIC sau a produselor TIC sau înlocuirea după încheierea perioadei de asistență;  
> (c) informații care descriu componentele hardware și software utilizate în serviciile TIC sau în produsele TIC;  
> (d) informații care descriu funcțiile de securitate cibernetică puse în aplicare ale serviciilor TIC sau ale produselor TIC și configurația necesară pentru funcționarea lor în condiții de siguranță;  
> (e) asigurarea faptului că serviciile TIC sau produsele TIC respectă cerințele de securitate în conformitate cu litera (a);  
> (f) metode de validare a conformității serviciilor TIC sau a produselor TIC livrate cu cerințele de securitate declarate, precum și documentarea rezultatelor validării.  
> 6.1.3. Entitățile relevante revizuiesc și, dacă este adecvat, actualizează procesele la intervale planificate și atunci când apar incidente semnificative.  

> **Reg. 2024/2690, anexa, pct. 6.2** · `[R2690-6.2]`  
> 6.2. Ciclul de viață al dezvoltării securizate  
> 6.2.1. Înainte de a dezvolta o rețea și un sistem informatic, inclusiv un software, entitățile relevante stabilesc norme pentru dezvoltarea securizată de rețele și sisteme informatice și le aplică atunci când dezvoltă rețele și sisteme informatice la nivel intern sau atunci când externalizează dezvoltarea rețelelor și a sistemelor informatice. Normele acoperă toate etapele dezvoltării, inclusiv specificațiile, proiectarea, dezvoltarea, punerea în aplicare și testarea.  
> 6.2.2. În sensul punctului 6.2.1, entitățile relevante:  
> (a) efectuează o analiză a cerințelor de securitate în etapele referitoare la specificațiile și proiectarea oricărui proiect de dezvoltare sau de achiziție întreprins de entitățile relevante sau în numele entităților respective;  
> (b) aplică principii de creare de sisteme securizate și principii de programare securizată oricărei activități de dezvoltare de sisteme informatice, cum ar fi promovarea securității cibernetice din faza de proiectare, a arhitecturilor de tip „încredere zero”;  
> (c) stabilesc cerințe de securitate privind mediile de dezvoltare;  
> (d) instituie și pun în aplicare procese de testare a securității în ciclul de viață al dezvoltării;  
> (e) selectează, protejează și gestionează în mod corespunzător datele privind testele de securitate;  
> (f) sanitizează și anonimizează datele privind testele în conformitate cu evaluarea riscurilor efectuată în temeiul punctului 2.1.  
> 6.2.3. În cazul în care dezvoltarea rețelelor și a sistemelor informatice este externalizată, entitățile relevante aplică și politicile și procedurile menționate la punctele 5 și 6.1.  
> 6.2.4. Entitățile relevante își revizuiesc și, dacă este necesar, își actualizează normele de dezvoltare securizată la intervale planificate.  

> **Reg. 2024/2690, anexa, pct. 6.3** · `[R2690-6.3]`  
> 6.3. Gestionarea configurației  
> 6.3.1. Entitățile relevante iau măsurile adecvate pentru a stabili, a documenta, a pune în aplicare și a monitoriza configurațiile, inclusiv configurațiile de securitate ale hardware-ului, software-ului, serviciilor și rețelelor.  
> 6.3.2. În sensul punctului 6.3.1, entitățile relevante:  
> (a) stabilesc și asigură securitatea configurațiilor pentru hardware, software, servicii și rețele;  
> (b) stabilesc și pun în aplicare procese și instrumente pentru a asigura respectarea configurațiilor securizate stabilite pentru hardware, software, servicii și rețele, pentru sistemele nou instalate, precum și pentru sistemele aflate în funcțiune pe durata lor de viață.  
> 6.3.3. Entitățile relevante revizuiesc și, dacă este adecvat, actualizează configurațiile la intervale planificate și atunci când apar incidente semnificative sau modificări semnificative ale operațiunilor sau ale riscurilor.  

> **Reg. 2024/2690, anexa, pct. 6.4** · `[R2690-6.4]`  
> 6.4. Gestionarea modificărilor, reparații și întreținere  
> 6.4.1. Entitățile relevante aplică proceduri de gestionare a modificărilor pentru a controla modificările aduse rețelelor și sistemelor informatice. Dacă acest lucru este aplicabil, procedurile trebuie să fie în concordanță cu politicile generale privind gestionarea modificărilor ale entităților relevante.  
> 6.4.2. Procedurile menționate la punctul 6.4.1 se aplică noilor versiuni, modificărilor și modificărilor de urgență ale oricărui software și hardware în exploatare și modificărilor configurației. Procedurile asigură faptul că modificările respective sunt documentate și, pe baza evaluării riscurilor efectuată în temeiul punctului 2.1, sunt testate și evaluate având în vedere impactul potențial înainte de a fi puse în aplicare.  
> 6.4.3. În cazul în care procedurile obișnuite de gestionare a modificărilor nu au putut fi urmate din cauza unei urgențe, entitățile relevante documentează rezultatul modificării și explicația motivului pentru care procedurile nu au putut fi urmate.  
> 6.4.4. Entitățile relevante revizuiesc și, dacă este adecvat, actualizează procedurile la intervale planificate și atunci când apar incidente semnificative sau modificări semnificative ale operațiunilor sau ale riscurilor.  

> **Reg. 2024/2690, anexa, pct. 6.6** · `[R2690-6.6]`  
> 6.6. Gestionarea corecțiilor de securitate  
> 6.6.1. Entitățile relevante specifică și aplică proceduri coerente cu procedurile de gestionare a modificărilor menționate la punctul 6.4.1, precum și cu gestionarea vulnerabilităților, gestionarea riscurilor și cu alte proceduri de gestionare relevante, pentru a se asigura că:  
> (a) corecțiile de securitate se aplică într-un termen rezonabil de la data la care devin disponibile;  
> (b) corecțiile de securitate se testează înainte de a fi aplicate în sistemele de producție;  
> (c) corecțiile de securitate provin din surse de încredere și sunt verificate din punctul de vedere al integrității;  
> (d) în cazurile în care o corecție nu este disponibilă sau nu se aplică în temeiul punctului 6.6.2, se pun în aplicare măsuri suplimentare și se acceptă riscuri reziduale.  
> 6.6.2. Prin derogare de la punctul 6.6.1 litera (a), entitățile relevante pot alege să nu aplice corecții de securitate atunci când dezavantajele aplicării corecțiilor de securitate sunt mai mari decât beneficiile în materie de securitate cibernetică. Entitățile relevante documentează și justifică în mod corespunzător motivele unei astfel de decizii.  

> **Reg. 2024/2690, anexa, pct. 6.7** · `[R2690-6.7]`  
> 6.7. Securitatea rețelelor  
> 6.7.1. Entitățile relevante iau măsurile adecvate pentru a-și proteja rețelele și sistemele informatice împotriva amenințărilor cibernetice.  
> 6.7.2. În sensul punctului 6.7.1, entitățile relevante:  
> (a) documentează arhitectura rețelei într-un mod inteligibil și actualizat;  
> (b) stabilesc și aplică controale pentru a proteja domeniile rețelei interne a entităților relevante împotriva accesului neautorizat;  
> (c) configurează controale pentru a preveni accesul și comunicarea în rețea care nu sunt necesare pentru funcționarea entităților relevante;  
> (d) stabilesc și aplică controale pentru accesul de la distanță la rețele și la sistemele informatice, inclusiv accesul prestatorilor de servicii;  
> (e) nu utilizează în alte scopuri sistemele folosite pentru administrarea punerii în aplicare a politicii de securitate;  
> (f) interzic sau dezactivează în mod explicit conexiunile și serviciile care nu sunt necesare;  
> (g) dacă este adecvat, permit exclusiv accesul la rețelele și sistemele informatice ale entităților relevante prin intermediul unor dispozitive autorizate de entitățile respective;  
> (h) permit conectarea prestatorilor de servicii numai după o cerere de autorizare și pentru o perioadă de timp stabilită, cum ar fi durata unei operațiuni de întreținere;  
> (i) stabilesc comunicarea între sisteme distincte numai prin canale de încredere care sunt izolate prin separare logică, criptografică sau fizică de alte canale de comunicare și garantează identificarea punctelor lor finale și protecția datelor canalului împotriva modificării sau divulgării;  
> (j) adoptă un plan de punere în aplicare pentru tranziția completă către protocoale de comunicare la nivel de rețea de ultimă generație într-un mod sigur, adecvat și treptat și stabilesc măsuri de accelerare a acestei tranziții;  
> (k) adoptă un plan de punere în aplicare pentru implementarea unor standarde moderne de comunicații prin e-mail convenite la nivel internațional și interoperabile pentru a securiza comunicațiile prin e-mail în vederea atenuării vulnerabilităților legate de amenințările privind e-mailuri și stabilesc măsuri pentru accelerarea unei astfel de implementări;  
> (l) aplică cele mai bune practici pentru securitatea DNS și pentru securitatea rutării pe internet și pentru igiena rutării traficului care provine din rețea și care este destinat acesteia.  
> 6.7.3. Entitățile relevante revizuiesc și, dacă este adecvat, actualizează aceste măsuri la intervale planificate și atunci când apar incidente semnificative sau modificări semnificative ale operațiunilor sau ale riscurilor.  

> **Reg. 2024/2690, anexa, pct. 6.9** · `[R2690-6.9]`  
> 6.9. Protecția împotriva software-ului rău-intenționat și neautorizat  
> 6.9.1. Entitățile relevante își protejează rețelele și sistemele informatice împotriva software-ului rău-intenționat și neautorizat.  
> 6.9.2. În acest scop, entitățile relevante pun în aplicare, în special, măsuri de detectare sau de prevenire a utilizării programelor informatice rău-intenționate sau neautorizate. Entitățile relevante se asigură, după caz, că rețelele și sistemele lor informatice sunt echipate cu software de detectare și răspuns, care este actualizat periodic în conformitate cu evaluarea riscurilor efectuată în temeiul punctului 2.1 și cu acordurile contractuale cu furnizorii.  

> **Reg. 2024/2690, anexa, pct. 6.10** · `[R2690-6.10]`  
> 6.10. Gestionarea și divulgarea vulnerabilităților  
> 6.10.1. Entitățile relevante obțin informații cu privire la vulnerabilitățile tehnice din rețelele și sistemele lor informatice, evaluează expunerea lor la astfel de vulnerabilități și iau măsurile adecvate pentru gestionarea vulnerabilităților.  
> 6.10.2. În sensul punctului 6.10.1, entitățile relevante:  
> (a) monitorizează informațiile privind vulnerabilitățile prin canale adecvate, cum ar fi anunțurile CSIRT, ale autorităților competente sau informațiile furnizate de furnizori sau de prestatorii de servicii;  
> (b) efectuează, dacă este adecvat, scanări ale vulnerabilității și consemnează rezultatele scanărilor, la intervale planificate;  
> (c) abordează, fără întârzieri nejustificate, vulnerabilitățile identificate de entitățile relevante ca fiind critice pentru operațiunile lor;  
> (d) se asigură că gestionarea vulnerabilităților este compatibilă cu procedurile lor de gestionare a modificărilor, de gestionare a corecțiilor de securitate, de gestionare a riscurilor și de gestionare a incidentelor;  
> (e) stabilesc o procedură pentru divulgarea vulnerabilităților în conformitate cu politica națională coordonată aplicabilă în materie de divulgare a vulnerabilităților.  
> 6.10.3. Atunci când acest lucru este justificat de impactul potențial al vulnerabilității, entitățile relevante creează și pun în aplicare un plan de atenuare a vulnerabilității. În alte cazuri, entitățile relevante documentează și justifică motivul pentru care vulnerabilitatea nu necesită remediere.  
> 6.10.4. Entitățile relevante revizuiesc și, după caz, actualizează la intervale planificate canalele pe care le utilizează pentru monitorizarea informațiilor privind vulnerabilitatea.  

> **Reg. 2024/2690, anexa, pct. 9** · `[R2690-9]`  
> 9. Criptografie [articolul 21 alineatul (2) litera (h) din Directiva (UE) 2022/2555]  
> 9.1. În sensul articolului 21 alineatul (2) litera (h) din Directiva (UE) 2022/2555, entitățile relevante stabilesc, implementează și aplică o politică și proceduri legate de criptografie, cu scopul de a asigura utilizarea adecvată și eficace a criptografiei pentru a proteja confidențialitatea, autenticitatea și integritatea datelor în conformitate cu clasificarea activelor entităților relevante și cu rezultatele evaluării riscurilor efectuată în temeiul punctului 2.1.  
> 9.2. Politica și procedurile menționate la punctul 9.1 stabilesc:  
> (a) în conformitate cu clasificarea activelor entităților relevante, tipul, robustețea și calitatea măsurilor criptografice necesare pentru a proteja activele entităților relevante, inclusiv datele în repaus și datele în tranzit;  
> (b) pe baza literei (a), protocoalele sau familiile de protocoale de adoptat, precum și algoritmii criptografici, puterea de criptare, soluțiile criptografice și practicile de utilizare care urmează să fie aprobate și care sunt necesare pentru a fi utilizate în cadrul entităților relevante, urmând, dacă este adecvat, o abordare bazată pe agilitate criptografică;  
> (c) abordarea entităților relevante în ceea ce privește gestionarea cheilor, inclusiv, dacă este adecvat, metodele pentru:  
> (i) generarea de chei diferite pentru sistemele și aplicațiile criptografice; (ii) emiterea și obținerea de certificate de cheie publică; (iii) distribuirea cheilor către entitățile vizate, inclusiv modul de activare a cheilor atunci când sunt primite; (iv) stocarea cheilor, inclusiv modul în care utilizatorii autorizați obțin acces la chei;  
> (v) modificarea sau actualizarea cheilor, inclusiv norme privind momentul și modul de modificare a cheilor; (vi) gestionarea cheilor compromise; (vii) revocarea cheilor, inclusiv modul de retragere sau dezactivare a cheilor; (viii) recuperarea cheilor pierdute sau corupte; (ix) crearea de copii de rezervă sau arhivarea cheilor;  
> (x) distrugerea cheilor; (xi) jurnalizarea și auditarea activităților legate de gestionarea cheilor; (xii) stabilirea datelor de activare și dezactivare a cheilor, astfel încât cheile să poată fi utilizate numai pentru perioada de timp specificată, în conformitate cu normele organizației privind gestionarea cheilor.  
> 9.3. Entitățile relevante revizuiesc și, dacă este adecvat, își actualizează politicile și procedurile la intervale planificate, ținând seama de stadiul actual al criptografiei.  

> **Reg. 2024/2690, anexa, pct. 11.2** · `[R2690-11.2]`  
> 11.2. Gestionarea drepturilor de acces  
> 11.2.1. Entitățile relevante acordă, modifică, elimină și documentează drepturile de acces la rețele și la sistemele informatice în conformitate cu politica de control al accesului menționată la punctul 11.1.  
> 11.2.2. Entitățile relevante:  
> (a) atribuie și revocă drepturi de acces pe baza principiilor necesității de a cunoaște, privilegiului minim și separării sarcinilor;  
> (b) se asigură că drepturile de acces sunt modificate în mod corespunzător la încetarea sau modificarea raportului de muncă;  
> (c) se asigură că accesul la rețele și la sistemele informatice este autorizat de către persoanele relevante;  
> (d) se asigură că drepturile de acces abordează în mod corespunzător accesul terților, cum ar fi vizitatorii, furnizorii și prestatorii de servicii, în special prin limitarea drepturilor de acces în ceea ce privește sfera și durata acestora;  
> (e) țin un registru al drepturilor de acces acordate;  
> (f) aplică jurnalizarea gestionării drepturilor de acces.  
> 11.2.3. Entitățile relevante revizuiesc drepturile de acces la intervale planificate și le modifică pe baza schimbărilor organizaționale. Entitățile relevante documentează rezultatele revizuirii, inclusiv modificările necesare ale drepturilor de acces.  

> **Reg. 2024/2690, anexa, pct. 11.3** · `[R2690-11.3]`  
> 11.3. Conturile privilegiate și conturile de administrare a sistemului  
> 11.3.1. Entitățile relevante mențin politici de gestionare a conturilor privilegiate și a conturilor de administrare a sistemului ca parte a politicii de control al accesului menționate la punctul 11.1.  
> 11.3.2. Politicile menționate la punctul 11.3.1:  
> (a) instituie proceduri riguroase de identificare, autentificare, cum ar fi autentificarea multifactorială, și de autorizare pentru conturile privilegiate și conturile de administrare a sistemului;  
> (b) creează conturi specifice care să fie utilizate exclusiv pentru operațiunile de administrare a sistemului, cum ar fi instalarea, configurarea, gestionarea sau întreținerea;  
> (c) individualizează și restricționează privilegiile de administrare a sistemului în cea mai mare măsură posibilă;  
> (d) prevăd faptul că sunt utilizate conturile de administrare a sistemului numai pentru conectarea la sistemele de administrare a sistemului.  
> 11.3.3. Entitățile relevante revizuiesc drepturile de acces aferente conturilor privilegiate și conturilor de administrare a sistemului la intervale planificate și le modifică pe baza schimbărilor organizaționale și documentează rezultatele revizuirii, inclusiv modificările necesare ale drepturilor de acces.  

> **Reg. 2024/2690, anexa, pct. 11.6** · `[R2690-11.6]`  
> 11.6. Autentificare  
> 11.6.1. Entitățile relevante pun în aplicare proceduri și tehnologii de autentificare securizată bazate pe restricții de acces și pe politica privind controlul accesului.  
> 11.6.2. În acest scop, entitățile relevante:  
> (a) se asigură că puterea autentificării este adecvată pentru clasificarea activului care urmează să fie accesat;  
> (b) controlează alocarea către utilizatori și gestionarea informațiilor secrete de autentificare printr-un proces care să asigure confidențialitatea informațiilor, inclusiv consilierea personalului cu privire la gestionarea adecvată a informațiilor de autentificare;  
> (c) solicită modificarea acreditărilor de autentificare inițial, la intervale predefinite și în cazul în care se suspectează că respectivele acreditări au fost compromise;  
> (d) solicită resetarea acreditărilor de autentificare și blocarea utilizatorilor după un număr predefinit de încercări nereușite de conectare;  
> (e) pun capăt sesiunilor inactive după o perioadă prestabilită de inactivitate; și  
> (f) solicită acreditări separate pentru a obține acces privilegiat sau a avea acces la conturi administrative.  
> 11.6.3. Entitățile relevante utilizează, în măsura posibilului, metode de autentificare de ultimă generație, în conformitate cu riscul evaluat asociat și cu clasificarea activului care urmează să fie accesat, precum și informații de autentificare unice.  
> 11.6.4. Entitățile relevante revizuiesc procedurile și tehnologiile de autentificare la intervale planificate.  

> **Reg. 2024/2690, anexa, pct. 11.7** · `[R2690-11.7]`  
> 11.7. Autentificarea multifactor  
> 11.7.1. Entitățile relevante se asigură că utilizatorii sunt autentificați prin factori de autentificare multipli sau prin mecanisme de autentificare continuă pentru accesarea rețelelor și a sistemelor informatice ale entităților relevante, dacă este adecvat, în conformitate cu clasificarea activului care urmează să fie accesat.  
> 11.7.2. Entitățile relevante se asigură că forța autentificării este adecvată pentru clasificarea activului care urmează să fie accesat.  

> **Reg. 2024/2690, anexa, pct. 12.3** · `[R2690-12.3]`  
> 12.3. Politica privind suporturile amovibile  
> 12.3.1. Entitățile relevante stabilesc, implementează și aplică o politică de gestionare a suporturilor de stocare amovibile și o comunică angajaților lor și părților terțe care gestionează suporturi de stocare amovibile în incintele entităților relevante sau în alte locuri în care suporturile amovibile sunt conectate la rețelele și sistemele informatice ale entităților relevante.  
> 12.3.2. Politica:  
> (a) prevede o interdicție tehnică de conectare a suporturilor amovibile, cu excepția cazului în care există un motiv organizațional pentru utilizarea acestora;  
> (b) prevede dezactivarea autoexecutării din aceste suporturi și scanarea suporturilor pentru coduri rău-intenționate înainte de a fi utilizate în sistemele entităților;  
> (c) prevede măsuri de control și protecție a dispozitivelor portabile de stocare care conțin date în timpul tranzitului și al stocării;  
> (d) dacă este adecvat, prevede măsuri pentru utilizarea tehnicilor criptografice pentru a proteja datele de pe suporturile de stocare amovibile.  
> 12.3.3. Entitățile relevante revizuiesc și, dacă este adecvat, actualizează politica la intervale planificate și atunci când apar incidente semnificative sau modificări semnificative ale operațiunilor sau ale riscurilor.  

**Aplicabilitate pe profil (interpretare):** *P2 (izolat)* — pct. 3.2, 6.2, 6.3, 6.4, 6.9, 9, 11.2, 11.3, 11.6, 12.3 sunt direct relevante; 6.6 (corecții) și 6.10 (vulnerabilități) se aplică ca proces al producătorului/entității, nu ca funcție de rețea; 6.7 (securitatea rețelelor) este în mare parte **neaplicabilă** unei stații fără rețea, cu excepția oricărui port deschis de aplicație. *P3 (conectat)* — toate punctele, plus 6.6, 6.7, 6.10 și 11.7 sunt direct aplicabile oricărei funcții de rețea a aplicației (listener, trimitere SIEM/TI, canal de actualizare).


## PARTEA 2 — Analiza golurilor aplicației (origin/main)

### 2.0 Metodă și limite

* Cod analizat: `origin/main` @ `126a2bd015d0de3a87ddfbaa200d488bbf093265` (git worktree detașat în scratchpad, eliminat la final). Aplicația: `02_PRODUCT/projects/workspaces/loganalyzer-dfir/` (.NET 10 / WPF, `LogAnalyzer.slnx` cu 9 proiecte; aplicația livrată = `LogAnalyzer.App`).
* Auditul stage-1 (`02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/CONTRACT_AUDIT_STAGE1.md`, pe ramura `claude/wonderful-bohr-iifqfn`) a fost făcut pe `0689f5d48`. `git diff --stat 0689f5d48 origin/main -- 02_PRODUCT/projects/workspaces/loganalyzer-dfir` este **gol**: niciun fișier al aplicației nu s-a schimbat între timp, deci rândurile stage-1 (`R…`/`U…`) rămân valabile și sunt doar **referite**, nu refăcute.
* Verificare: cod citit și `grep`; **nu** s-a rulat aplicația (mediu Linux, aplicația e WPF/Windows) și nu s-au rulat teste noi. Orice afirmație despre comportament la rulare este derivată din cod și marcată «neverificat» dacă nu e demonstrabilă din cod.
* Căi relative la rădăcina aplicației. Abrevieri: **DC** = `LogAnalyzer.Dfir.Core/`, **DW** = `LogAnalyzer.Dfir.Windows/`, **DT** = `LogAnalyzer.Dfir.Tests/`, **App** = `LogAnalyzer.App/`, **Core**/**Infra** = `LogAnalyzer.Core/`, `LogAnalyzer.Infrastructure/`, **PIPE** = `DW/Investigation/InvestigationPipeline.cs`.
* Verdicte: **MEETS** (cerința e îndeplinită, cu dovadă), **PARTIAL**, **MISSING**, **N/A** (NOT_APPLICABLE software), **—** (nu se aplică profilului respectiv). Verdictul pe profil diferă acolo unde cerința diferă (de ex. NET-01).
* Cele 55 de rânduri de mai jos sunt cerințe derivate din Partea 1 (nu rânduri-contract ca în stage-1, care are 194).

### 2.1 Tabel de verdicte

| ID | Cerință derivată | Temei (citate Partea 1) | P1 | P2 | P3 | Dovezi (fișier:rând) | Stage-1 | Notă |
|---|---|---|---|---|---|---|---|---|
| **ACR-01** | Aplicația livrată ca componentă acreditabilă a unui SIC (intrări pentru acreditare) | HG-240, HG-322, HG-237-ACRED | MISSING | MISSING | MISSING | Nu există CSS, PrOpSec, analiză de risc sau descriere de securitate a aplicației în `Documentation/` (4 fișiere) sau `docs/dfir/` (13 fișiere); `SECURITY.md` §«Limitări cunoscute» neagă certificarea ORNISS/CC. | R22, R23 | P2/P3: echivalentul este politica de securitate + analiza de riscuri (NIS2 21(2)(a)). |
| **ACR-02** | Cerințe de securitate specifice (CSS) pentru componenta aplicație | HG-261, HG-262, HG-263, HG-324 | MISSING | — | — | idem ACR-01; nicio mențiune a modului de operare (dedicat / nivel înalt / multi-nivel) în cod sau docs. | — | Documentul CSS este între AAS și CSTIC; repo-ul poate genera schița. |
| **ACR-03** | Proceduri operaționale de securitate (PrOpSec) pentru operarea aplicației | HG-307, HG-308, PRP-3, PRP-5 | MISSING | MISSING | MISSING | Fără ghid de operare orientat pe securitate; `README.md` și `Documentation/RELEASE-CHECKLIST.md` nu acoperă roluri, jurnale, ștergere, export. | — | Structura PrOpSec (8 capitole) este publică: `[PRP-*]`, §1.4. |
| **ACR-04** | Documentație tehnică de proiectare / realizare / distribuire, schemă de fluxuri de date | HG-318, HG-317 | PARTIAL | PARTIAL | PARTIAL | `docs/dfir/EVIDENCE_MODEL.md`, `PARSER_CONTRACT.md`, `POLICY_ENGINE.md`, `MEMORY_VAULT_INTEGRATION.md` descriu module; fără schemă de încredere/fluxuri; `DFIR_CURRENT_ARCHITECTURE_AUDIT.md` este **învechit** (54 de commit-uri de cod după el, stage-1). | stage-1 §1 tabel prospețime | Inventarul dependențelor și schema fluxurilor sunt generabile din repo (Partea 3). |
| **ACR-05** | Control al modificărilor și reacreditare (clasificarea modificărilor care cer re-aprobare) | HG-248, HG-311, HG-329 | PARTIAL | PARTIAL | PARTIAL | Versiune `ApplicationVersion = "0.1.0-dfir"` (`DC/Model/CaseInfo.cs:26`), `Documentation/RELEASE-CHECKLIST.md`; nu există clasificare «modificare cu impact de securitate» / note de versiune cu impact asupra acreditării. | R22 | — |
| **ACR-06** | Evaluare / certificare a produsului (ORNISS / Criterii Comune) acolo unde AAS o cere | HG-319, HG-323, HG-325, I2-36 | MISSING | N/A | N/A | `SECURITY.md` §«Limitări»: «Nu reprezintă certificare ORNISS, ISO/IEC 27037, Common Criteria». | — | Dacă LogAnalyzer intră sub art. 319 este neverificat; P2/P3 nu cer certificare. |
| **ID-01** | Identificarea unică a utilizatorului în toate înregistrările | HG-237-NEREP, PRP-12, I2-46 | PARTIAL | PARTIAL | PARTIAL | Identitatea = cont Windows (șir): `DC/Case/CaseWorkspace.cs:123,133,159` (`Environment.UserName`, fără domeniu); `DW/Policy/PolicyWorkbench.cs:29` (`WindowsIdentity.Name`); `DW/Investigation/InvestigationPipeline.cs:59-60` (domeniu\utilizator). | R13.2 | Acceptabil doar dacă CSS declară autentificarea delegată SO; domeniul lipsește din jurnalul de caz. |
| **ID-02** | Autentificare (delegată SO) documentată; aplicația nu ocolește SO | HG-272, PRP-12, I2-49 | PARTIAL | PARTIAL | PARTIAL | Aplicația nu are autentificare proprie (grep `Login\|Authenticate\|PasswordBox` = 0 în App/Core/Dfir*); oricine poate porni exe-ul; singura verificare de drepturi: Administrator pentru containment (`App/ViewModels/ContainmentViewModel.cs:261-262`). | — | P3: lipsa MFA/autentificare continuă este neaplicabilă aplicației locale (NIS2 21(2)(j)) dar trebuie declarată. |
| **ID-03** | Mesaj de avertizare la începutul sesiunii de lucru | PRP-12, PRP-5 | MISSING | — | — | Nu există banner de avertizare la pornire (`App/App.xaml.cs` → `SplashWindow` → `MainWindow`). | U22 | Cerință de PrOpSec, ieftin de implementat. |
| **AC-01** | Acces selectiv pe nevoia de a cunoaște (niveluri înalt / multi-nivel) | HG-260, HG-266, HG-267, HG-265 | PARTIAL | PARTIAL | PARTIAL | Fără ACL/compartimentare în aplicație; cazurile stau în profilul utilizatorului `%LOCALAPPDATA%\LogAnalyzer\Cases` (`App/ViewModels/InvestigationViewModel.cs:131`); protecția = ACL-ul SO. | — | Suficient doar pentru mod **dedicat** (HG 265 alin. (2)); insuficient pentru nivel înalt/multi-nivel. |
| **AC-02** | Separarea rolurilor (administrator de securitate / administrator de sistem / utilizator) | HG-244, HG-246, HG-268, HG-269, HG-271, HG-276 | MISSING | PARTIAL | PARTIAL | Nu există roluri în aplicație; singurele verificări de rol sunt `IsInRole(Administrator)` (`ContainmentViewModel.cs:261`, `DW/Policy/SettingProviders.cs:17-18`, `InvestigationPipeline.cs:109-110`); funcțiile de containment/firewall/auditpol/sanitizare sunt disponibile oricărui operator. | R13.2 | P2/P3: R2690 11.2-11.3 cer drepturi minime și conturi privilegiate controlate. |
| **AC-03** | Regula celor doi pentru modificări ale sistemului | HG-277, HG-237-REGULA2 | PARTIAL | PARTIAL | PARTIAL | Doar politicile: aprobare de un cont diferit de autor/înregistrator (`DC/Policy/PolicyStore.cs:80-86`) + legare de SHA-256 (`:101-119`). Containment, `netsh`, `auditpol`, sanitizare: un singur operator + `MessageBox` (`ContainmentViewModel.cs:117,131`; `MainViewModel.cs:2795`). | R13.2 | — |
| **AUD-01** | Registru automat de acces la informațiile clasificate în format electronic (deschidere caz, vizualizare, căutare, export, ștergere) | HG-291, HG-283 | PARTIAL | PARTIAL | PARTIAL | Evenimente existente: `case.opened` (`DC/Case/CaseWorkspace.cs:50`), `evidence.registered`, `report.pdf`, `ai.reasoning`, `auditpol.enable`, `finding.rejected` ș.a. (17 acțiuni literale în apelurile `Audit("…")`); lipsesc: vizualizare probă/constatare, căutare, export CSV/STIX/ZIP/Vault, ștergere/sanitizare caz, schimbare mod. | R16.1-R16.4, R16.8 | Acoperirea acțiunilor utilizatorului asupra datelor este parțială. |
| **AUD-02** | Retenția registrelor (perioadă stabilită cu AAS; minimum 3 / 10 ani) | HG-291 | PARTIAL | PARTIAL | PARTIAL | Aplicația nu are cod de ștergere/rotație a jurnalelor (grep), deci nu le pierde; dar nu există parametru de retenție, blocare la ștergere, arhivare; `AuditLogService` scrie în `<dir exe>\AuditLogs` (`Core/Services/AuditLogService.cs:13`), o locație în dir. de instalare. | R16.10 | — |
| **AUD-03** | Protecția și tamper-evidence a jurnalelor | I2-43, R2690-3.2 | PARTIAL | PARTIAL | PARTIAL | Înlănțuite SHA-256: jurnalul de politici (`DC/Policy/PolicyExecution.cs:279-309`), `App/Services/ChainOfCustodyService.cs:11-45` (doar intake: `EvidenceIntakeService.cs:25`), `Core/Services/ProvenanceLedgerService.cs:38-50`. **Simple**: jurnalul de caz (`CaseWorkspace.cs:156-160`), custodia CSV (`:136-143`), `AuditLogService.cs:19-24`. Fără ancoră externă / semnătură: trunchierea fișierului nu e detectabilă. | R16.10 (MISSING), R3.7, R16.7 | Cel mai important gol pentru «audit integrity». |
| **AUD-04** | Conținutul înregistrărilor: cine/ce/când/rezultat, UTC, acțiuni de administrare, pornire/oprire jurnal, modificări de configurare | R2690-3.2, PRP-27, I2-43 | PARTIAL | PARTIAL | PARTIAL | Caz: timestamp UTC + `UserName` + acțiune (`CaseWorkspace.cs:159`); `AuditLogService` UTC în linie dar data locală în nume (`:16`, linia UTC la `:23`); nu am găsit jurnalizare a rezolvării modului (`App/App.xaml.cs:45-48`), a citirii `LogAnalyzer.mode` sau a schimbărilor de configurare; fără câmp «rezultat». | R16.* | — |
| **AUD-05** | Nerepudiere (semnarea rezultatelor) | HG-237-NEREP | MISSING | N/A | N/A | Rapoartele PDF, `findings.json`, exporturile nu sunt semnate (doar SHA-256 în custodie); fără cheie de semnare în aplicație. | R14, R16.9 | Definiția INFOSEC include nerepudierea (P1). |
| **SW-01** | Software autorizat; verificarea integrității versiunilor în uz; distribuire autentică | HG-309, HG-310, HG-311, HG-318, R2690-6.2, R2690-6.3 | MISSING | MISSING | MISSING | `SECURITY.md`: «Executabilul nu este semnat Authenticode»; CI publică un singur fișier fără manifest de hash-uri (`.github/workflows/loganalyzer-dfir-build.yml:116-133`); fără auto-verificare la pornire; `win-x64-singlefile.pubxml`: `IncludeNativeLibrariesForSelfExtract=true` (extrage DLL-uri native în profilul utilizatorului la rulare). | R22.* | Blocant P1/P3 (AAS aprobă «software autorizat»; NIS2 21(2)(e)). Semnarea Authenticode / hash manifest sunt generabile din CI. |
| **SW-02** | Build reproductibil și lanț de aprovizionare fixat (blocarea dependențelor) | R2690-6.2, NIS2-21 | PARTIAL | PARTIAL | PARTIAL | Versiuni NuGet exacte în `.csproj`; **fără** `packages.lock.json`, `RestoreLockedMode`, `Deterministic`/`ContinuousIntegrationBuild` (`Directory.Build.props` conține doar `LangVersion`); proiectele de test folosesc versiuni flotante (`17.*`, `2.*`); acțiunile CI sunt fixate pe SHA (`.github/workflows/loganalyzer-dfir-build.yml:37,42,91,107,112,130`) cu `permissions: contents: read` (`:19-20`). | R21.* | — |
| **SW-03** | Inventar al componentelor terțe (SBOM) cu versiuni și licențe | HG-318, R2690-6.2, NIS2-21 | MISSING | MISSING | MISSING | Nu există inventar/SBOM în repo. Inventarul manual din Partea 3 (Anexa A) a fost produs din `.csproj` + `.nuspec` NuGet; tranzitivele nu au fost rezolvate (**neverificat**). | — | Generabil: `dotnet list package --include-transitive` / CycloneDX în CI. |
| **SW-04** | Fără actualizări automate, telemetrie sau descărcări la rulare | HG-311, HG-310 | MEETS | MEETS | — | Grep pe `HttpClient\|WebClient\|WebRequest\|Dns.\|GetHostEntry\|Ping\|Smtp\|HttpListener` în proiectele compilate: unica utilizare activă = `DC/AI/EvidenceReasoner.cs:173-185` (loopback). Fără cod de verificare a versiunii; `dotnet publish` self-contained (restaurarea NuGet doar la build). | R11.3 | Pentru P3 cerința se inversează: este nevoie de canal de actualizare semnat (vezi P3-02). |
| **SW-05** | Configurație de bază și protecția fișierelor de configurare | I2-44, PRP-32, R2690-6.3 | PARTIAL | PARTIAL | PARTIAL | Politici: SHA-256 + stări de ciclu de viață (`PolicyStore.cs:80-120`). Dar: fișierul `LogAnalyzer.mode` lângă exe și `--mode=` (`Core/Services/Connectivity/OperatingMode.cs:52-76`, `App/App.xaml.cs:45-48`) sunt editabile de operator și pot comuta pe **Network**; setările AI (`InvestigationViewModel.cs:72`) sunt text liber; fără configurație semnată. | R13.* | Vezi NET-03. |
| **SW-06** | Rezistență la date de intrare ostile (cod/fișiere malițioase analizate) | HG-313, I2-45, PRP-26 | PARTIAL | PARTIAL | PARTIAL | Teste adversariale pentru registry/regex/binare (`DT/RawRegistryMalformedTests`, `MalformedBinaryInputTests`, `RuleRegexTimeoutTests`); aplicația nu execută artefacte analizate; Defender doar `-DisableRemediation` (`DW/Containment/ProcessScanner.cs:276-282`); lipsește fuzzing și teste dedicate EVTX/Prefetch/ESE/LNK ostile; CodeQL nu acoperă C# (stage-1). | R21.7 | — |
| **MRK-01** | Câmp de clasificare la nivel de caz/probă | HG-15, HG-45, HG-56, HG-22 | MISSING | MISSING | MISSING | `DC/Model/CaseInfo.cs:4-12` nu are câmp de clasificare/marcaj; modelul `Finding` are «Classification» dar în sens epistemic (Direct/Inferred, `DC/Detection/IocMatcher.cs:21`), nu de secretizare. | U19 | P2/P3: marcaj administrativ/TLP opțional. |
| **MRK-02** | Marcajul de clasificare pe fiecare pagină/ieșire (PDF, CSV, JSON, STIX/MISP, ZIP, vault_proposals) | HG-46, HG-47, HG-48, HG-49, HG-337 | MISSING | MISSING | MISSING | `DW/Investigation/InvestigationReportPdf.cs:33-43` (antet) și `:123` (subsol) fără marcaj; exporturi: `DC/Memory/VaultExport.cs:110`, `Core/Services/StixMispExportService.cs:13`, `Core/Services/DfirCasePackagingService.cs:101`, `App/ViewModels/MainViewModel.cs:1464` — niciunul nu marchează. | R14.* | Fără MRK-01 aplicația nu poate produce documente conforme art. 46. |
| **MRK-03** | Document derivat/extras primește nivelul maxim al surselor; mențiune «Document în lucru» | HG-56, HG-14, HG-49 | MISSING | — | — | Consecință a MRK-01; nu există propagare de nivel în pipeline (`DW/Investigation/InvestigationPipeline.cs`). | — | — |
| **EXP-01** | Controlul informațiilor înainte de ieșirea din zona SPAD / transfer protejat | HG-285, HG-287, HG-286 | MISSING | PARTIAL | PARTIAL | Export fără poartă: `SaveFileDialog` către orice cale (`MainViewModel.cs:1464`, `InvestigationViewModel.cs:186`), ZIP (`DfirCasePackagingService.cs:100-101` suprascrie ținta), STIX/MISP, CaseUCO, `vault_proposals.jsonl` (`PIPE:260-265`), HTML/CSV/TXT/.ps1 (`MainViewModel.cs:842,879,934,1480,1606,1626,2667`); fără aprobare, jurnal dedicat de export, verificare de marcaj. | R15.1 (Vault: doar propuneri — IMPLEMENTED) | Pentru Vault există regula «doar propuneri» (MEETS), nu și poartă de export. |
| **MED-01** | Identificarea / evidența mediilor de stocare pentru exporturi | HG-288, HG-289, HG-294, HG-295 | MISSING | — | — | Nu există câmp «mediu de destinație»/număr de serie în export; copierea nu este controlată (HG 289 → PrOpSec). | — | Parțial procedural; aplicația poate cere ID-ul mediului la export. |
| **DEL-01** | Ștergerea / distrugerea doar prin proceduri aprobate; proces-verbal cu două persoane | HG-296, HG-297, HG-78, HG-79, HG-284 | PARTIAL | PARTIAL | PARTIAL | `Core/Services/MediaSanitizationEngine.cs` (zero/random/DoD 3 treceri/crypto-erase) apelat din `MainViewModel.cs:2785`; verificare prin recitire; certificat PDF care declară «al doilea operator… NEDECLARAT»; **dar** certificatul afirmă «CONFORM … HG 585/2002 ART. 65» (`SanitizationPdfCertificateGenerator.cs:33`) iar art. 65 tratează registrele de evidență, nu sanitizarea (`[HG-65]`). | — | Instrument util, atribuire de conformitate eronată (vezi DOC-01). |
| **DEL-02** | Fără copii temporare necontrolate ale probelor | HG-292, HG-288 | PARTIAL | PARTIAL | PARTIAL | Curățate în `finally`: `DW/Parsers/FirefoxHistoryParser.cs:125-128`, `SrumNetworkParser.cs:72-74`, `Infra/Parsers/BrowserForensicsParser.cs:98`, `MainViewModel.cs:2761-2766`. **Scurgere**: EVTX reparat rămâne în `%TEMP%\LogAnalyzer\evtx_repair` (`DW/Parsers/EvtxParser.cs:49`; fără `Delete` în fișier; analog `Infra/Parsers/EvtxParser.cs:31`). SQLCipher: `temp_store=MEMORY` (`Infra/Services/DatabaseService.cs:679-684`). | — | Copie de probă în director temporar necriptat și neșters = mediu necontrolat. |
| **DEL-03** | Aplicația nu șterge nimic de pe sistemul analizat | — | PARTIAL | PARTIAL | PARTIAL | Colectarea copiază (`DW/Acquisition/Collectors.cs:42,99,230,246`: `wevtutil epl`, `esentutl /y /vss`, `reg save`); probele proprii primesc atribut ReadOnly (`DC/Case/CaseWorkspace.cs:91`). Excepții deliberate: sanitizare la alegerea operatorului (DEL-01), rollback de politici care șterge valori de registru (`DC/Policy/PolicyExecution.cs:228` → `DW/Policy/SettingProviders.cs:78`), suprascriere ZIP destinație (`DfirCasePackagingService.cs:100`). | R3.2 | Nicio ștergere a probelor analizate găsită; modificări ale gazdei: vezi HOST-*. `esentutl /vss` poate crea instantaneu VSS (efect pe gazdă: neverificat). |
| **NET-01** | Fără egress de rețea în mod izolat; interconectare doar aprobată | HG-282, HG-300, HG-305, I2-35, I2-37 | PARTIAL | PARTIAL | — | Inventar în §2.2: toate căile de rețea cunoscute sunt blocate de `NetworkPolicy.EnsureAllowed` (`Core/Services/Connectivity/OperatingMode.cs:144-148`) în AirGapped, cu excepția AI (loopback impus în constructor) și a citirii remote `EventLogSession` (implicit `localhost`). | stage-1 R11.3 | Modul e ales la pornire; vezi NET-02/NET-03 pentru eliminare și nealterabilitate. |
| **NET-02** | Cod de rețea eliminabil la compilare pentru un build air-gapped | HG-282, I2-35 | MISSING | MISSING | — | `App.csproj:13-14`: «AirGapped or Network is chosen at startup … not at compile time». Simbolul `AIR_GAPPED_EDITION` există doar în `LogAnalyzer.AirGapped.csproj:34` (în afara `LogAnalyzer.slnx`) și condiționează blocuri din `ViewModels/MainViewModel.cs:80,546,565` (arborele legacy din rădăcină). Cod de rețea livrat în asamblări: `Core/Services/Network/{M365LiveConnector,SiemForwarder,LiveThreatIntel}Service.cs` (neinstanțiate de App), `Infra/Services/AuditCollectionService.cs:102` (UDP), `Infra/Watchers/LiveEventLogWatcherService.cs:35` (remote). | — | Blocant pentru «poate fi complet eliminat/dezactivat». |
| **NET-03** | Profilul de rețea nu poate fi coborât (relaxat) de operator | HG-282, HG-241, I2-37 | MISSING | MISSING | — | `--mode=Network` și `LogAnalyzer.mode` suprascriu detectarea (`OperatingMode.cs:52-76`; `App/App.xaml.cs:45-48`) fără autentificare/semnătură; detecția automată trece în Network dacă SO raportează «Internet» (`OperatingMode.cs:85`); în caz contrar e fail-closed AirGapped (`:87-91`). | — | Cerința proprietarului: profil selectat prin politică semnată, ireversibil de operator. |
| **NET-04** | AI/LLM doar local (loopback) | HG-305, I2-35 | MEETS | MEETS | MEETS | **Verificat:** `LocalModelClient` acceptă doar `http` + IP numeric loopback, `UseProxy=false`, `AllowAutoRedirect=false` (`DC/AI/EvidenceReasoner.cs:180,185`); singurul cod AI cu HttpClient; pornit doar la acțiunea utilizatorului (`InvestigationViewModel.cs:199`). | R11.3, R11.10 | Egress-ul aplicației este doar loopback. Daemonul Ollama este un produs separat (AI-02). |
| **AI-02** | Provenința modelului/daemonului AI (produs terț) și dezactivarea implicită pentru P1 | HG-310, HG-318, R2690-6.1 | PARTIAL | PARTIAL | PARTIAL | Se înregistrează digest-ul modelului și hash-urile prompt/răspuns (`EvidenceReasoner.cs:189-195,219-220`), dar nu există listă permisă de digest-uri, nu e semnat, iar Ollama nu apare în niciun inventar; endpoint implicit `http://127.0.0.1:11434` (`InvestigationViewModel.cs:72`). | R11.* | Pentru P1: AI = componentă separată care cere propria aprobare; implicit OFF. |
| **NET-05** | Fără DNS/ping/sockets în afara celor gate-uite | HG-305 | MEETS | MEETS | — | Singurele sockets: `UdpClient(port)` (`AuditCollectionService.cs:102`, după gate `:85`); fără `Dns.*`, `Ping`, `TcpClient` în proiectele compilate; sonda de conectivitate e pasivă (`Core/Services/Connectivity/WindowsConnectivityProbe.cs:66-75`, nu trimite trafic). | — | Verificat prin grep. |
| **NET-06** | Receptor syslog UDP: autentificare/filtrare sursă (expunere de rețea) | R2690-6.7, I2-35 | — | — | MISSING | `Infra/Services/AuditCollectionService.cs:102` — `new UdpClient(port)` leagă toate interfețele, fără listă de surse, fără autentificare/criptare (UDP clar). | — | Relevant doar P3 (și P1 dacă AAS aprobă interconectarea). |
| **HOST-01** | Acțiuni care modifică gazda: opt-in, confirmare, verificare, rollback | HG-329, HG-248 | PARTIAL | PARTIAL | PARTIAL | Firewall `netsh` (`Infra/Services/SystemDefenseExecutionService.cs:37,67,85`) și COM (`DW/Containment/FirewallController.cs:26-57`); `auditpol /set` (`DW/Containment/Detection.cs:117-119`); registru prin politici (`DW/Policy/SettingProviders.cs:67-78`); suspendare proces (`ProcessContainmentService.cs:111-113`). Toate pornesc din clic de operator cu confirmare (`ContainmentViewModel.cs:117,131`, `MainViewModel.cs:1793`) sau ciclu de politici cu aprobare; verificare prin recitire în politici, dar `Success` după apelul COM în containment. | R13.1-R13.8 | Opt-in, nu implicit. |
| **HOST-02** | Acțiunile care modifică gazda pot fi dezactivate global prin profil/politică semnată | HG-329, HG-309, HG-241 | MISSING | MISSING | PARTIAL | Nu există întrerupător global; modul AirGapped nu le dezactivează (doar parametrul `IsAirGappedMode` către `ExecuteContainmentScript`, `MainViewModel.cs:1004-1007`). | — | Pe un SIC acreditat, modificarea firewall/audit/registru de către o aplicație schimbă configurația acreditată. |
| **HOST-03** | Fără execuție de scripturi cu ExecutionPolicy Bypass din căi neinventariate | HG-310, HG-309, R2690-6.9 | MISSING | MISSING | MISSING | `Infra/Services/AuditCollectionService.cs:28-49`: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File …\Scripts\AuditCollector.ps1`; scriptul **nu există în repo**; dacă lipsește, fallback la `C:\Users\Marius\Desktop\LogAnalyzer.MVP\Scripts` (`:33`) — cale de dezvoltator codificată. | R9.* | Vector de execuție de cod neinventariat; a se elimina/izola sub profil. |
| **HOST-04** | Privilegii minime | R2690-11.3, HG-244 | PARTIAL | PARTIAL | PARTIAL | `App/app.manifest:19`: `requestedExecutionLevel level="highestAvailable"` → rulează elevat dacă utilizatorul e administrator; `AuditLogs` și `startup_debug.log` în dir. exe (`App.xaml.cs:36`, `AuditLogService.cs:13`). | — | — |
| **CRY-01** | Criptografie aprobată pentru protecția informațiilor clasificate | I2-41, HG-258 | PARTIAL | PARTIAL | PARTIAL | SQLCipher via `SQLitePCLRaw.bundle_e_sqlcipher 2.1.10` (nuspec: build-uri native «unofficial and unsupported»), DPAPI `CurrentUser` (`Core/Services/DpapiEncryptionService.cs:18,34`), SHA-256 (peste tot), RSA-PSS neînregistrat în DI (`App/Services/LicenseService.cs:26`). | R12.* | Nu se poate revendica protecție criptografică «de nivel clasificat»: normele criptografice ORNISS sunt nepublice (neverificat). P2/P3: lipsește politica de criptografie (NIS2 21(2)(h)). |
| **CRY-02** | Fără secrete codificate; schemă de licențiere robustă | R2690-9, HG-275 | MISSING | MISSING | MISSING | Sare codificată `INFOSEC_ROMANIA_SOC_2026_SECURE_KEY` în `Core/Services/LicenseService.cs:12` (în binar) și `Generate-LicenseKey.ps1:20`; cheia = SHA-256(HWID+dată+sare) (`LicenseService.cs:31`); `license.lic` cu cheie în repo; `SECURITY.md` recunoaște că «oprește doar copierea ocazională». Varianta RSA există, dar `App.xaml.cs:71,108` înregistrează clasa din Core. | — | Pe stații izolate, expirarea licenței este și un risc de disponibilitate (HG 237). |
| **CRY-03** | Gestionarea cheilor și protecția datelor la repaus | HG-275, HG-288 | PARTIAL | PARTIAL | PARTIAL | Cheie DB 256 biți aleatoare (`Infra/Services/DatabaseService.cs:707`), învelită DPAPI (`:711`) în același dosar cu baza (`:23-35`), păstrată în memorie nezeroizată (`:691`, comentariu «We do not zero memory»); **doar baza SQLite e criptată** — dosarele de caz (probe, jurnale, rapoarte) sunt fișiere simple în `%LOCALAPPDATA%`. | R12.* | P1 se bazează pe criptarea discului/mediului aprobată de AAS (PRP-16). |
| **INC-01** | Date pentru raportarea incidentelor de securitate (registru protejat; incidente ale aplicației) | HG-88, I2-50, I2-51 | PARTIAL | PARTIAL | PARTIAL | Mutația probelor e detectată și raportată (`DW/Investigation/ReportIntegrity.cs:13-23`, audit `evidence.mutated`); nu există export de «incident de securitate» cu câmpurile din HG 88 alin. (3) și nu se jurnalizează eșecul auto-verificării (inexistentă). | R3.4, R3.5 | Formularul INFOSEC 3 e nepublic. |
| **DOC-01** | Fără declarații de conformitate neîntemeiate (HG 585 / NATO / TEMPEST) și fără simulări prezentate ca reale | HG-318, HG-322 | MISSING | MISSING | MISSING | `App/ViewModels/MainViewModel.cs:84-86` («PROTOCOL AIR-GAPPED CONFORM HG 585 / NATO», text static), `Core/Services/PdfReportService.cs:64` («STATUS CONFORMITATE» = **100** codificat, «HG 585 & ISO 27037»), `Core/Services/ComplianceAuditEngine.cs:19-30` («HG 585/2002 – Art. 21 … CONFORM» când nu sunt modificări — art. 21 tratează declasificarea, `[HG-21]`), `SanitizationPdfCertificateGenerator.cs:33` + `SanitizationCertificateGenerator.cs:78` + `MediaSanitizationEngine.cs:14,85` («HG 585/2002 ART. 65»), `App/Views/DataCollectionView.xaml:42-43` («CLASSIFICATION CEILING: SECRET DE SERVICIU / HG 585 / NATO AC/35 Baseline»), `MainViewModel.cs:2578-2606` (detecție simulată «Acoustic Air-Gap / TEMPEST Zone 0»), `Core/Services/AdAuditPdfReportService.cs:192,252`, `AiCopilotInvestigationEngine.cs:56,101`, `StandaloneSamAuditEngine.cs:78`, `App/Views/KerberosAndLateralMovementView.xaml:275`. | R12.3, U3, top-10 #4/#5 | Orice afirmație de conformitate necontrolată în documentele de acreditare/rapoarte este un risc direct pentru acreditare. |
| **NIS-01** | Politici de analiză a riscurilor și de securitate a sistemului (NIS2 21(2)(a)); revizuire periodică | NIS2-21, OUG-13 | — | MISSING | MISSING | Fără registru de riscuri / model de amenințări pentru aplicație în repo. | — | Generabil ca schiță. |
| **NIS-02** | Securitate în dezvoltare și întreținere, inclusiv gestionarea și divulgarea vulnerabilităților (21(2)(e)) | NIS2-21, R2690-6.2, R2690-6.10 | — | PARTIAL | PARTIAL | `SECURITY.md`: canal de raportare (contact direct), fără termene/avize/CVE; verificare de dependențe prin `dependency-audit.yml` (acoperă proiectele .NET prin `dotnet restore`); CodeQL nu acoperă C# (stage-1). | R21.* | — |
| **NIS-03** | Gestionarea corecțiilor (R2690 6.6) și canal de actualizare sigur | R2690-6.6 | — | PARTIAL | MISSING | Fără mecanism de actualizare; runtime .NET inclus (self-contained) → orice corecție cere reconstruire și redistribuire; fără semnătură de pachet. | — | P2: proces manual documentat suficient; P3: canal semnat necesar. |
| **NIS-04** | Comunicații securizate / autentificarea serviciilor (21(2)(j); R2690 6.7, 11.7) pentru funcțiile de rețea | NIS2-21, R2690-6.7, R2690-11.7 | — | N/A | PARTIAL | Servicii cu `HttpClient` implicit către `https://` (graph.microsoft.com, api.abuseipdb.com, virustotal.com, SIEM configurabil) există dar nu sunt instanțiate de aplicație; neauditate criptografic; AI pe `http` loopback; syslog UDP în clar (NET-06). | — | P3: cerințe de TLS/validare/ fixare de certificate neimplementate/neverificate. |
| **NIS-05** | Controlul accesului și gestionarea activelor (21(2)(i); R2690 11.2-11.3, 12.3) | NIS2-21, R2690-11.2, R2690-12.3 | — | PARTIAL | PARTIAL | Vezi AC-01..AC-03; fără politică pentru medii amovibile/exporturi (MED-01). | — | — |
| **EMI-01** | Emisii compromițătoare (TEMPEST) și instalare echipamente | HG-302, HG-303 | N/A | — | — | Cerință de instalare/echipament, nu de software; aplicația nu trebuie să pretindă măsurători/garanții TEMPEST (a se vedea DOC-01: `MainViewModel.cs:2578-2606`). | — | NOT_APPLICABLE software. |
| **OUT-01** | Ieșiri către memoria Vault doar ca propuneri (fișier), fără scriere directă | HG-285, HG-287 | MEETS | MEETS | MEETS | `DC/Memory/VaultExport.cs:11-14,105-111` — scrie doar `vault_proposals.jsonl`; nicio cale de scriere în vault (stage-1). | R15.1 (IMPLEMENTED) | Fișierul exportat rămâne fără marcaj (MRK-02) și fără poartă (EXP-01). |
| **TIME-01** | Surse de timp sincronizate pentru corelarea jurnalelor | R2690-3.2 | — | N/A | N/A | Responsabilitatea SO/rețelei; aplicația folosește `DateTimeOffset.UtcNow` (`CaseWorkspace.cs:159`). | — | Se documentează ca ipoteză în CSS/PrOpSec. |

### 2.1b Numărare

| Profil | MEETS | PARTIAL | MISSING | N/A | rânduri aplicabile |
|---|---|---|---|---|---|
| **P1 (clasificat)** | 4 | 23 | 20 | 1 | 48 |
| P2 (izolat, neclasificat) | 4 | 28 | 13 | 4 | 49 |
| P3 (conectat) | 2 | 28 | 12 | 3 | 45 |
| *cel mai strict dintre profilele aplicabile* | 4 | 26 | 23 | 2 | 55 |

(«MEETS» pe P1 = 4 rânduri: SW-04 fără actualizări/telemetrie, NET-04 AI doar loopback, NET-05 fără sockets neprotejate, OUT-01 Vault doar propuneri. Cele mai multe cerințe sunt PARTIAL sau MISSING.)

### 2.2 Inventarul tuturor căilor de rețea (egress/ingress)

Metodă: grep pe `HttpClient|WebClient|WebRequest|TcpClient|UdpClient|Socket|Dns\.|GetHostEntry|Ping|SmtpClient|HttpListener|ClientWebSocket|DownloadString|DownloadFile|EventLogSession|DirectoryEntry|DirectorySearcher|ManagementScope|NetworkInformation` în toate proiectele compilate (excluzând `obj/`, `_recovered/`, teste).

| # | Mecanism | Fișier:rând | Țintă | Declanșare | Poartă `NetworkPolicy` | Cablat în App? | Eliminabil la build? |
|---|---|---|---|---|---|---|---|
| 1 | Client model local (Ollama) | `DC/AI/EvidenceReasoner.cs:173-185,191,205` | `http://127.0.0.1:11434` (numeric loopback, impus în constructor `:180`) | buton AI (`App/ViewModels/InvestigationViewModel.cs:199`) | nu (loopback impus în schimb) | da (`DW/Investigation/AiCaseAnalysis.cs:22`) | nu (în `Dfir.Core`) |
| 2 | Receptor syslog UDP | `Infra/Services/AuditCollectionService.cs:85,102` | ascultă pe toate interfețele, port ales de utilizator | buton «Start Syslog» (`App/ViewModels/MainViewModel.cs:1142-1150`) | da (`:85`) | da (`App/App.xaml.cs:85`) | nu |
| 3 | Colectare prin `powershell.exe` (script extern) | `Infra/Services/AuditCollectionService.cs:28-49` | gazda indicată (comportamentul scriptului: neverificat, scriptul nu e în repo) | buton colectare (`MainViewModel.cs:1116-1124`) | nu | da | nu |
| 4 | Abonare live la jurnale, inclusiv gazdă remote | `Infra/Watchers/LiveEventLogWatcherService.cs:35` | `EventLogSession(remoteHost,…)`; implicit `localhost` (`MainViewModel.cs:319`), niciun câmp editabil găsit în XAML | pornire automată în modul Network (`MainViewModel.cs:568,593,1659,1753`) | nu (doar `IsNetworkMode`) | da | nu |
| 5 | Citire jurnale de pe controlere de domeniu / LDAP | `DW/Domain/UserInvestigation.cs:53`, `DW/Domain/DirectoryCollector.cs:70` | DC-uri / Active Directory | ecranul de investigație domeniu (`App/ViewModels/DomainInvestigationViewModel.cs:42,75`) | da | da | nu |
| 6 | Conector Microsoft 365 | `Core/Services/Network/M365LiveConnectorService.cs:22-26,34,42-46,83` | `login.microsoftonline.com`, `graph.microsoft.com` | — | da (`:34,77`) | **nu** (niciun consumator în App/Infra/Dfir*) | nu |
| 7 | Trimitere SIEM | `Core/Services/Network/SiemForwarderService.cs:24-39` | URL configurabil | — | da (`:39`) | **nu** | nu |
| 8 | Threat intel online | `Core/Services/Network/LiveThreatIntelService.cs:36,57,92,110` | `api.abuseipdb.com`, `www.virustotal.com` | — | da | **nu** | nu |
| 9 | Sondă de conectivitate | `Core/Services/Connectivity/WindowsConnectivityProbe.cs:66-75`; `MainViewModel.cs:1642` | interfețe locale (pasivă, nu trimite trafic) | la pornire / schimbare adresă | n/a | da | nu |
| 10 | `Process.Start(explorer.exe / fișier exportat)` | `InvestigationViewModel.cs:188,247`, `ContainmentViewModel.cs:230,238` ș.a. | local (deschide fișiere create de aplicație) | clic | n/a | da | nu |

**Verdict (verificat, cu limita grep):** nu există telemetrie, verificare de actualizări, DNS, ping, descărcare la rulare sau NuGet la rulare. Singura cale către rețea activă în mod AirGapped este modelul AI pe loopback (tipul de adresă e impus în constructor, fără proxy, fără redirecționări). Concordă cu stage-1 (R11.3). Ce **nu** satisface cerința de „complet eliminabil”: tot codul de rețea este compilat în ambele moduri (NET-02) și modul poate fi comutat de operator (NET-03).

### 2.3 Autentificare, autorizare, audit al acțiunilor utilizatorului

* **Utilizatori/roluri:** niciun cont aplicativ; identitatea este contul Windows (ID-01, ID-02). Nu există separare operator / administrator de securitate (AC-02). Singura aprobare cu două persoane: politicile (`DC/Policy/PolicyStore.cs:80-86`).
* **Ce se jurnalizează:** (a) `CaseWorkspace.Audit` (`DC/Case/CaseWorkspace.cs:156-160`) — fișier text TSV `Logs/LogAnalyzer_Audit.log` în caz, 17 acțiuni literale (deschidere caz, înregistrare probă, raport PDF, AI, `auditpol.enable`, containment, încredere program…); (b) custodie CSV+JSONL în caz (`:136-143`); (c) `AuditLogService` (5 acțiuni: izolare gazdă, restaurare rețea, blocare IOC, erori import, vizualizare alertă — `MainViewModel.cs:1799,1811,1850,2225`, `AlertDetailWindow.xaml.cs:34`) în `<dir exe>\AuditLogs\SOC_Audit_yyyyMMdd.log`; (d) `ChainOfCustodyService` înlănțuit SHA-256 în `%LOCALAPPDATA%\LogAnalyzer\chain-of-custody.ndjson` (doar intake; `App/App.xaml.cs:73-78`, `EvidenceIntakeService.cs:25`); (e) jurnal de politici înlănțuit (`DC/Policy/PolicyExecution.cs:279-309`); (f) `ProvenanceLedgerService` (legacy, sanitizare, `MainViewModel.cs`).
* **Tamper-evident?** Doar (d), (e), (f). (a), (b), (c) sunt text simplu fără lanț; niciun jurnal nu are ancoră externă sau semnătură → AUD-03.
* **Cine ce a deschis / exportat / șters:** deschiderea cazului și generarea raportului sunt jurnalizate; vizualizarea probelor/constatărilor, căutarea, exporturile (CSV, STIX/MISP, CaseUCO, ZIP, Vault) și ștergerile nu au intrări dedicate (AUD-01).

### 2.4 Integritate: semnare, reproductibilitate, manifest, configurare

* **Semnare binare:** nu (`SECURITY.md` §«Limitări cunoscute»); nu există pas de semnare în `loganalyzer-dfir-build.yml`.
* **Manifest de hash-uri pentru release:** nu; CI încarcă directorul `publish/` ca artefact (`:130-133`) și are verificarea «Verify publish output» (`:119-122`), fără SHA256SUMS.
* **Reproductibilitate:** nu s-a demonstrat; fără `packages.lock.json`, `Deterministic`/`ContinuousIntegrationBuild`; `Directory.Build.props` setează doar `LangVersion`; versiuni de test flotante. Acțiunile CI sunt fixate pe SHA.
* **Pachet livrat:** single-file self-contained win-x64 cu `IncludeNativeLibrariesForSelfExtract=true` (bibliotecile native SQLCipher se extrag la rulare în profilul utilizatorului → conflict cu liste de aplicații permise).
* **Fișiere de configurare:** `LogAnalyzer.mode` (lângă exe), setări AI în UI, `license.lic`, politici (cu SHA-256). Nimic nu este semnat sau protejat de ACL de aplicație.

### 2.5 Criptografie

* **SQLCipher** (baza locală): cheie 256 biți din `RandomNumberGenerator`, învelită cu **DPAPI `CurrentUser`**, fișier `LogAnalyzer.key` lângă `LogAnalyzer.db` în `%LOCALAPPDATA%\LogAnalyzer` (`Infra/Services/DatabaseService.cs:23-35,679,700-715`). Biblioteca nativă vine din `SQLitePCLRaw.lib.e_sqlcipher 2.1.10`, descrisă în nuspec ca build-uri «unofficial and unsupported» (Zetetic) — aspect relevant pentru acreditare. Algoritmul efectiv al SQLCipher nu e setat în cod (valorile implicite ale bibliotecii: neverificat în acest mediu).
* **Hash:** SHA-256 peste tot în căile de producție; **SHA-1** apare o dată, pentru UUID derivat (`DC/Compliance/ComplianceModel.cs:231`), nu în scop de securitate; MD5: nu s-a găsit.
* **Semnături:** RSA-PSS/SHA-256 în `App/Services/LicenseService.cs:10-26` (neînregistrat în DI); licența **efectiv folosită** este SHA-256(HWID+dată+sare) (`Core/Services/LicenseService.cs:12,31`).
* **Secrete în cod/repo:** sarea de licență (`LicenseService.cs:12`, `Generate-LicenseKey.ps1:20`), `license.lic` cu cheie în repo, cale de dezvoltator (`AuditCollectionService.cs:33`). Un grep orientativ (`Password=|Key=|PRAGMA key`) nu a returnat alte secrete în clar în codul de producție (nu e o scanare de secrete completă).
* **Date la repaus:** doar baza SQLite e criptată; dosarele de caz nu.

### 2.6 Marcarea ieșirilor și clasificarea cazurilor

* Un caz **nu poate** avea nivel de clasificare: `CaseInfo` (`DC/Model/CaseInfo.cs:4-12`) are `CaseId, Name, Host, User, CreatedAtUtc, Investigator…`, fără câmp de secretizare/marcaj.
* Raportul PDF (`DW/Investigation/InvestigationReportPdf.cs:33-43,123`) are antet cu nume caz/gazdă/investigator și subsol fără marcaj. Exporturile CSV/JSON/STIX/ZIP/Vault nu poartă marcaj.
* Singura mențiune de clasificare în UI este o etichetă statică, nelegată de date (`App/Views/DataCollectionView.xaml:42-43`) → DOC-01.

### 2.7 Tratarea datelor: temporare, cache, ștergere, locație

* Cazuri: `%LOCALAPPDATA%\LogAnalyzer\Cases` (`InvestigationViewModel.cs:131`); baza și cheia: `%LOCALAPPDATA%\LogAnalyzer`; `AuditLogs` și `startup_debug.log` în directorul executabilului (`AuditLogService.cs:13`, `App.xaml.cs:36`).
* Temporare: vezi DEL-02 (scurgere EVTX reparat în `%TEMP%`).
* **Ce șterge aplicația pe sistemul analizat:** colectarea doar copiază; probele din caz devin ReadOnly (`CaseWorkspace.cs:91`); ștergeri: fișiere temporare proprii, ZIP destinație (la suprascriere), valori de registru la rollback de politică (acțiune a operatorului), și funcția de sanitizare pe fișierul ales de operator (cu confirmare cu «Nu» implicit, `MainViewModel.cs:2795-2799`). Nicio ștergere automată a probelor analizate.

### 2.8 Procese externe lansate și acțiuni asupra gazdei

| Proces / API | Fișier:rând | Ce modifică gazda? | Opt-in? | Cum ar fi tratat pe un SIC acreditat |
|---|---|---|---|---|
| `netsh.exe advfirewall` add/delete rule (izolare stație, blocare IOC) | `Infra/Services/SystemDefenseExecutionService.cs:37,67,85,131` | **da** — reguli firewall | clic + confirmare; verificare prin interogare | dezactivat prin profil (P1/P2); când e permis: aprobare în 2 persoane, aprobat în CSS |
| COM `HNetCfg.FWRule` (izolare program) | `DW/Containment/FirewallController.cs:26-57` | **da** | clic + `MessageBox` (`ContainmentViewModel.cs:117,131`) | idem |
| `auditpol.exe /set … /failure:enable` | `DW/Containment/Detection.cs:117-119` | **da** — politica de audit a SO | clic (`ContainmentViewModel.cs:247`); jurnalizat `auditpol.enable` | idem; schimbă configurația acreditată (HG 329) |
| Scriere/ștergere registru prin politici | `DW/Policy/SettingProviders.cs:67-78`; `DC/Policy/PolicyExecution.cs:228` | **da** | ciclu de politici cu aprobare 4 ochi, SHA-256, rollback | idem |
| Suspendare/reluare proces | `DW/Containment/ProcessContainmentService.cs:111-113,162-164` | **da** | opțiune la containment | idem |
| `powershell.exe -ExecutionPolicy Bypass -File AuditCollector.ps1` | `Infra/Services/AuditCollectionService.cs:28-49` | depinde de script (neverificat; scriptul nu e în repo; fallback pe cale de dezvoltator `:33`) | clic | a se elimina din buildul acreditat |
| `wevtutil.exe epl`, `esentutl.exe /y /vss`, `reg.exe save` | `DW/Acquisition/Collectors.cs:42,99,230,246`; `ToolRunner.cs:19-26` | citesc/copiază; `/vss` poate crea instantaneu VSS (neverificat) | acțiunea de colectare | permis în P1 după aprobarea AAS; căi absolute în System32 (`ToolRunner.System32`) |
| `MpCmdRun.exe -Scan … -DisableRemediation` | `DW/Containment/ProcessScanner.cs:274-282` | nu remediază; scanare AV | la scanare | permis; dependent de politica AV a SIC |
| `explorer.exe` / `UseShellExecute` pe fișiere exportate | vezi §2.2 #10 | nu | clic | de verificat: deschiderea unui PDF/fișier creat de aplicație |
| Scripturi generate (playbook-uri PowerShell / `netsh` / `Stop-Process`) | `Core/Services/IncidentResponsePlaybookService.cs:28-142`, `Network/CyberAttackCountermeasureEngine.cs:79-336` | doar **text** scris în fișiere alese de operator (`MainViewModel.cs:1606,1626`); aplicația nu le execută; **atenție**: scriptul «kill process tree» folosește `EventId` ca PID și implicit `5124` (`MainViewModel.cs:1624`) | — | marcate ca recomandări; nu se execută automat |

Manifestul cere `highestAvailable` (`App/app.manifest:19`), deci toate cele de mai sus rulează elevat pe un cont de administrator.

### 2.9 Dependențe terțe

Inventarul (versiuni, licențe din `.nuspec` oficiale) este în **Anexa A** (Partea 3). Observații: **QuestPDF 2026.7.2** are licență *duală* (fișier `LICENSE.md` în pachet: Community / Professional / Enterprise) — încadrarea o stabilește proprietarul (decizie juridică; neverificat); `SQLitePCLRaw.bundle_e_sqlcipher` aduce o bibliotecă nativă «unofficial and unsupported»; dependențele tranzitive nu au fost rezolvate.

## PARTEA 3 — Pachet de lucru propus: WP-ACR (pregătire pentru acreditare)

### 3.0 Principii (regulile proprietarului)

1. **Nicio funcționalitate existentă nu se elimină sau refactorizează doar pentru acreditare.** Se adaugă un strat: *profil de desfășurare* + *porți de funcționalități* + compilare condiționată pentru build-ul acreditat. Funcțiile rămân disponibile în profilurile care le permit.
2. **Nicio fațadă prezentată ca gata de producție.** Singura «eliminare» propusă este a *afirmațiilor* nefondate (DOC-01) și a conținutului simulat; ele nu sunt funcționalitate. Eliminarea lor din rapoarte/UI este o decizie a proprietarului (vezi §3.6).
3. Orice element WP-ACR are **criteriu de acceptare testabil** și leagă cerința de textul din Partea 1.
4. Limita onestă a protecției: un administrator local rău-intenționat poate înlocui executabilul sau cheia de verificare; profilul protejează împotriva modificării de către operator și a erorilor de configurare, nu împotriva unui administrator care controlează sistemul. Pentru P1 aceasta se acoperă prin CSS/PrOpSec (HG 240, 244, 277).

### 3.1 Design: două aplicații (decizia proprietarului nr. 13, 2026-10-08) și profilul semnat pentru P2/P3

> Decizia nr. 13 (rândul 13 din `02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/CONTRACT_AUDIT_STAGE1.md`, commit `900e570c0`) a fost luată **în timpul** acestei analize și înlocuiește varianta inițială «un singur binar, trei profiluri la runtime». Proiectul de mai jos o urmează; ce nu e acoperit de decizie este marcat «de decis».

| Element | Proiect |
|---|---|
| **Ediția P1 (clasificat)** | **executabil separat**; rețeaua, AI-ul la distanță, acțiunile care modifică gazda și actualizările **nu sunt compilate** («absente, nu dezactivate»). Adaugă marcaj de clasificare, roluri INFOSEC și audit complet al acțiunilor utilizatorului. Aceasta este exact cerința HG 282/305, I2-35/37 în forma cea mai solidă (absența codului). |
| **Ediția P2/P3 (neclasificat)** | **un executabil**, două moduri — P2 izolat (rețea oprită), P3 conectat (rețea pornită și securizată: TLS, actualizări semnate, politică pentru endpoint-ul AI) — alese printr-o **politică semnată la instalare**, **nedegradabilă** de operator; păstrează toată funcționalitatea existentă. |
| Bibliotecile comune | parsere, probe, cronologie, verificare, custodie — folosite de ambele; nimic duplicat. |
| Politica semnată (P2/P3) | fișier `LogAnalyzer.policy` (JSON + semnătură) într-un director cu ACL doar pentru administratori (de ex. `%ProgramData%\LogAnalyzer\`), verificat la pornire cu o cheie publică încorporată (RSA-PSS/ECDSA; mecanismul există în `App/Services/LicenseService.cs`, neînregistrat); conține `mode` ∈ {P2, P3}, `featureAllow[]`, `retention`, `markingPolicy`, `exportPolicy`, `roles` (grupuri Windows), `version` monoton, `notBefore`. **Absentă / invalidă / expirată ⇒ P2** (fail-closed: fără rețea). |
| Nedegradabil | `--mode=` și `LogAnalyzer.mode` (`OperatingMode.cs:52-76`) sunt ignorate când există politică; trecerea P2→P3 cere o politică nouă cu `version` mai mare, semnată de deținătorul cheii; hash-ul politicii se jurnalizează la fiecare pornire. În ediția P1 nu există mod de relaxat. |
| Poartă comună | `FeatureGate.Require(Feature.X)` (extinde `NetworkPolicy.EnsureAllowed`, apelat deja în 8 locuri) la fiecare punct cu efect de rețea sau asupra gazdei în ediția P2/P3. |
| Garanție de build | analizor Roslyn «banned API» (`BannedSymbols.txt`: `HttpClient`, `UdpClient`, `Socket`, `Process.Start`, `EventLogSession`, `DirectoryEntry`, `Registry.SetValue`…) cu listă de excepții = apelurile din spatele porții; **test post-build pe ediția P1** care scanează referințele și simbolurile asamblărilor și eșuează dacă apar tipurile excluse (cerința din decizia 13: «P1 build contains none of the excluded code»). |
| Limită onestă | un administrator local rău-intenționat poate înlocui executabilul sau cheia de verificare; protecția este împotriva operatorului și a erorilor de configurare. Pentru P1 riscul rezidual se acoperă prin CSS/PrOpSec (HG 240, 244, 277). |

**Matrice de funcții** (propunere; de aprobat de proprietar):

| Funcție | P1 (ediție separată) | P2 | P3 |
|---|---|---|---|
| Parsare / corelare / grafic / rapoarte local | da | da | da |
| AI local (loopback) | **de decis** (decizia 13 exclude doar AI «remote»); propunere: inclus, implicit OFF, singurul cod `HttpClient`, doar loopback | opțional | opțional |
| Receptor syslog, jurnale DC/LDAP, SIEM, TI, M365, colectare remote | **nu e compilat** | OFF | opțional, cu TLS/auth |
| Acțiuni asupra gazdei (firewall, `auditpol`, registru, suspendare proces, PowerShell) | **nu e compilat** | OFF implicit | opțional, cu aprobare în 2 persoane |
| Actualizări | **nu e compilat** | proces manual documentat | canal semnat |
| Sanitizare de fișiere | de decis (modifică date; propunere: doar cu proces-verbal și aprobare) | opțional | opțional |
| Export (CSV/STIX/ZIP/Vault) | doar cu marcaj + registru + aprobare după nivel | cu marcaj administrativ | liber, jurnalizat |
| Licență hash+sare | nu (se înlocuiește cu verificare semnată) | opțional | opțional |

### 3.2 Lista ordonată WP-ACR

| Nr. | Pachet | Cerințe (Partea 2) | Temei (Partea 1) | Aditiv? | Criteriu de acceptare |
|---|---|---|---|---|---|
| **WP-ACR-00** | **Adevăr în afirmații:** scoaterea/înlocuirea textelor «conform HG 585/NATO», a scorului 100 codificat, a citărilor greșite (art. 21/65), a etichetei «CLASSIFICATION CEILING»; marcarea conținutului simulat ca DEMO sau excluderea lui din build-ul de producție; test CI care interzice reintroducerea șirurilor fără sursă de evidență | DOC-01, DEL-01 | `[HG-21]`, `[HG-65]`, HG 318/322 | nu elimină funcții; elimină doar afirmații | `grep` din CI fără șirurile interzise; rapoartele afișează «Neevaluat» sau stare calculată din dovezi |
| **WP-ACR-01** | Politică semnată pentru P2/P3 (mod P2/P3, fail-closed pe P2), hash-ul politicii în jurnal; ignorarea `--mode=`/`LogAnalyzer.mode` când există politică | NET-03, SW-05, HOST-02 | HG 241, 282, 329; I2-37 | da | test: politică absentă ⇒ P2; un octet modificat ⇒ P2; `--mode=Network` ignorat; trecere P2→P3 fără semnătură refuzată |
| **WP-ACR-02** | `FeatureGate` + analizor «banned API» + matrice de funcții (ediția P2/P3) | NET-01, NET-05, HOST-01, HOST-02 | HG 282, 300, 305, 329 | da | build eșuează la apel negărduit; test pe fiecare funcție din matrice × mod |
| **WP-ACR-03** | **Ediția P1 separată** = WP-ED din decizia 13 (după WP0/WP1/WP12, înainte de WP3): inventarul asamblărilor cu cod de rețea / de modificare a gazdei, mutarea lor în spatele granițelor de ediție (fără eliminare), două ieșiri de build în CI | NET-02, HOST-02, HOST-03 | HG 282, 305, 310, 329; I2-35 | da (granițe noi, nu ștergeri) | test post-build: ediția P1 nu conține tipurile excluse; test manual cu firewall «block all outbound» (de executat de proprietar) |
| **WP-ACR-04** | **Registru unic de acces/audit**: schemă fixă, înlănțuire SHA-256 + ancoră (fișier de cap exportabil / semnătură), evenimente complete (deschidere/vizualizare/export/ștergere/configurare/profil/pornire-oprire/refuzuri de poartă), UTC, `DOMENIU\utilizator`, rezultat, retenție configurabilă și blocare la ștergere; adaptoare peste `CaseWorkspace.Audit`/`Custody`/`AuditLogService` (fără a le elimina) + comandă `verify` | AUD-01..AUD-04, AUD-05 (opțional semnare) | HG 291; `[I2-43]`; `[PRP-27]`; R2690 3.2 | da | teste de alterare (modificare, ștergere de rând, trunchiere detectate); test de acoperire a acțiunilor; specificația înregistrărilor generată |
| **WP-ACR-05** | **Clasificare și marcaj:** câmp în `CaseInfo` (nivel, clasă, avertismente, dată declasificare; versiune de schemă crescută, implicit «nesetat»), propagare maximă la derivate, marcaj sus/jos pe fiecare pagină PDF, antet + câmp de manifest în CSV/JSON/ZIP/Vault, «Document în lucru» până la finalizare | MRK-01..MRK-03 | HG 15, 45-49, 56, 22, 337 | da | test: extragere text PDF — marcaj pe 100 % din pagini; export fără marcaj refuzat în P1 |
| **WP-ACR-06** | Poartă de export: aprobare, ID mediu destinație, verificare de marcaj, registru de exporturi | EXP-01, MED-01 | HG 285, 287, 289, 294, 295 | da | test: export P1 fără aprobare ⇒ refuzat + înregistrat |
| **WP-ACR-07** | Roluri (Operator / Administrator de securitate / Administrator de sistem) mapate pe grupuri Windows din politica semnată (P2/P3) sau din configurația ediției P1; generalizarea fluxului de aprobare din `PolicyStore` pentru: schimbare profil/retenție, sanitizare, acțiuni asupra gazdei, export P1 | AC-01..AC-03, ID-02 | HG 244, 268-277 | da | matrice de permisiuni testată; autoaprobare refuzată |
| **WP-ACR-08** | **Integritate și lanț de aprovizionare:** semnare Authenticode (certificatul proprietarului), `SHA256SUMS` + SBOM CycloneDX + atestare de build în CI, `packages.lock.json` + `RestoreLockedMode`, `Deterministic`/`ContinuousIntegrationBuild`, auto-verificare la pornire față de manifestul semnat, livrare cu bibliotecile native alături (fără auto-extragere în profil), instalare offline | SW-01..SW-03 | HG 309-311, 318; R2690 6.2-6.3; NIS2 21(2)(d)(e) | da | verificare de semnătură pe artefact; build repetat ⇒ hash identic (sau diferențe explicate); pornire cu modul alterat ⇒ refuz + jurnal |
| **WP-ACR-09** | **Igiena datelor:** ștergerea copiei EVTX reparate (`finally`), director temporar în interiorul cazului (aceleași ACL), funcție «scoatere din uz caz» (verificare + jurnal + șablon proces-verbal) | DEL-01..DEL-03, CRY-03 | HG 292, 296-298, 76-79 | da | test: după analiză, `%TEMP%\LogAnalyzer` conține 0 fișiere de probă |
| **WP-ACR-10** | **Cripto și licență:** înlocuirea schemei hash+sare cu verificarea RSA-PSS deja codificată (sau înlocuirea licenței cu profilul semnat), eliminarea sării și a `license.lic` din repo, zeroizarea buffer-elor de cheie, inventar de algoritmi, punct de integrare pentru criptare aprobată (fără a revendica nivel clasificat) | CRY-01..CRY-03 | `[I2-41]`; R2690 9; HG 275 | da | scan de secrete curat; test de licență cu cheie publică; document de inventar cripto |
| **WP-ACR-11** | **Acțiuni asupra gazdei:** absente din ediția P1 (WP-ACR-03), implicit oprite în P2; eliminarea căii codificate `C:\Users\Marius\…`; înlocuirea `-ExecutionPolicy Bypass` (script încorporat și verificat prin hash sau cod nativ); manifest `asInvoker` + ridicare de privilegii doar la acțiune | HOST-01..HOST-04 | HG 309, 310, 329; R2690 6.9, 11.3 | da | test: fără apel `Process.Start` negărduit; manifest verificat |
| **WP-ACR-12** | Bannere: mesaj de avertizare la pornire cu confirmare jurnalizată; indicator permanent de profil/clasificare | ID-03 | `[PRP-12]`, `[PRP-5]` | da | test UI/automat de prezență |
| **WP-ACR-13** | **Documentație generată** (vezi §3.3) — în paralel cu toate | ACR-01..ACR-06, NIS-01 | HG 237 «acreditarea»; 261; 307; 318 | da | documente regenerabile din CI, marcate «PROIECT» |
| **WP-ACR-14** | **P3:** canal de actualizare semnat, politică de divulgare cu termene, autentificare/ACL/TLS pentru receptorul syslog, TLS minim 1.2 + validare de certificat pentru SIEM/TI/M365, secrete API sub DPAPI, acoperire CodeQL pentru C# | NIS-02..NIS-04, NET-06, SW-01 | NIS2 21(2)(e)(j); R2690 6.6, 6.7, 6.10, 11.7 | da | test de integrare TLS; scanare dependențe în CI |
| **WP-ACR-15** | **Verificare continuă:** teste de acceptare pe fiecare ID din Partea 2, rulate pe Windows în CI (stage-1: 65 de teste cad pe Linux), raport de test atașat dosarului de acreditare | toate | HG 324, 327, 329 | da | raport verde pe runner Windows; rezultatele atașate release-ului |

**Ordine și dependențe:** 00 → 03 (WP-ED, ediția P1) în paralel cu 01 → 02 (politica și porțile ediției P2/P3); 04 și 05 pot începe după 01; 06 și 07 după 04+05; 08 în paralel; 09-12 după 02; 13 și 15 permanent; 14 doar pentru P3.

### 3.3 Documentația de acreditare (denumirile folosite de HG 585 / ORNISS) și ce poate genera repo-ul

| Document (denumire exactă din text) | Temei | Cine îl semnează / aprobă | Poate fi generat din repo? |
|---|---|---|---|
| **Strategia proprie de securitate** a unității | HG 240 alin. (2) | conducerea unității | nu (organizațional) |
| **Cerințele de securitate specifice (CSS)** — cu: nota justificativă despre obiectivul acreditării, nivelurile și modul/modurile de operare; nota justificativă despre managementul riscurilor; descrierea detaliată a facilităților de securitate și a procedurilor; planul de implementare și întreținere a caracteristicilor de securitate; planul de testare, evaluare și certificare; certificatul | HG 237 «acreditarea» lit. a)-f); 261-263 (în Ghid: «documentația cu cerințele de securitate (DCS)» / «CSSS») | stabilite de CSTIC, aprobate de AAS | **parțial**: schițe pentru lit. c) (din matricea de funcții + profil), d) și e) (din suita de teste); a), b) și f) sunt ale unității |
| **Procedurile operaționale de securitate (PrOpSec)** — Ghid DS 2: cap. 1 Administrarea și organizarea securității; 2 Securitatea fizică; 3 Securitatea personalului; 4 Securitatea informațiilor; 5 Securitatea SIC (calculatoare, criptografică, emisii, transmisii); 6 Planificarea măsurilor pentru situații de urgență și continuarea activității; 7 Managementul configurației; 8 Proceduri operaționale asociate | HG 307-308; `[PRP-*]` | AOSIC + administratorii de securitate; aprobat de AAS | **parțial**: scheletul + faptele specifice aplicației (jurnale, ștergere, export, configurație, cod malițios) |
| **Analiza / managementul riscurilor** (registru de riscuri) | HG 237 «managementul de risc» | CSTIC | schiță (modelul de amenințări al aplicației) |
| **Documentația tehnică privind proiectarea, realizarea și modul de distribuire** a componentelor de securitate | HG 318 | producător | **da**: arhitectură, schema fluxurilor de date, inventar dependențe/SBOM, procedura de build/release, matricea de funcții pe profil |
| **Specificația înregistrărilor de audit / registrelor de acces** (evenimente, câmpuri, retenție, verificare) | HG 291; PRP-27 | CSTIC + AAS (retenția) | **da** (din schema WP-ACR-04) |
| **Managementul configurației**: configurația de bază autorizată, lista fișierelor de configurare cu hash, procedura de modificare | I2-44; PRP-32; HG 311, 329 | CSTIC | **da** (liste + hash-uri la fiecare release) |
| **Rapoarte de testare/evaluare** | HG 237 «evaluarea», 324, 327 | echipa de evaluare AAS | **da** (rezultatele CI, WP-ACR-15) |
| Ghid de hardening (nu e denumit în HG 585) | bune practici, R2690 6.3 | producător/CSTIC | **da** |
| Lista utilizatorilor autorizați și drepturile lor | PRP-19 alin. (2) lit. d) | AOSIC | nu |
| Formularul de raportare a incidentelor | INFOSEC 3 | — | **nepublic / neverificat** |
| Dosarul de acreditare depus la AAS (agenția din cadrul ORNISS) | HG 253, 322 | unitatea | nu |

### 3.4 Ce poate genera repo-ul (livrabile automate propuse)

1. **Inventar de dependențe + SBOM** (CycloneDX) din `*.csproj`, `packages.lock.json` și `.nuspec` — Anexa A este prima ediție manuală.
2. **Descrierea fluxurilor de date** (surse de intrare → parsere → caz → rapoarte/exporturi → Vault) generată din registrul de parsere și din lista de scrieri pe disc; include harta egress din §2.2.
3. **Specificația înregistrărilor de audit** din schema WP-ACR-04.
4. **Ghidul de hardening** (ACL, profil, manifest, ștergere, backup), cu valorile implicite verificate de teste.
5. **Matricea cerință → dovadă → test** (Partea 2 convertită în CSV regenerabil).

### 3.5 Riscuri și decizii de luat de proprietar

* Confirmarea formei consolidate la zi a HG 585/2002 și a eventualelor modificări după 28.11.2022 (neverificat).
* Încadrarea juridică NIS2/OUG 155/2024 a organizațiilor clientului și aplicabilitatea Reg. 2024/2690 (neverificat).
* Dacă AAS consideră LogAnalyzer «produs informatic de securitate» (art. 319) — ar cere evaluare/certificare.
* Licența QuestPDF (duală) și originea SQLCipher nativ «unofficial».
* Soarta conținutului simulat (MainViewModel `InitializeProcessTree`, detecția «Acoustic Air-Gap») și a stratului legacy din `Core/Services` care produce afirmații de conformitate (referit în stage-1 R12.3).
* Obținerea (prin canalele ORNISS) a INFOSEC 1/3/4, a normelor criptografice și a formularului de incident — fără ele PrOpSec și CSS nu pot fi finalizate.

### 3.6 Ce NU propune acest document

Nu propune eliminarea funcțiilor DFIR, a modulelor legacy sau refactorizări; nu revendică conformitate cu HG 585/2002 a aplicației; nu înlocuiește evaluarea AAS; nu conține conținut al normelor ORNISS nepublice.

## ANEXA A — Inventarul dependențelor directe (NuGet)

Sursă: `.csproj` din `origin/main` + `.nuspec` oficiale de la `api.nuget.org` (obținute în această rulare). Dependențele **tranzitive** și cele ale proiectelor de test **nu** au fost rezolvate complet (neverificat). Runtime-ul .NET 10 este inclus în publicarea self-contained (`win-x64-singlefile.pubxml`).

| Pachet | Versiune | Licență (nuspec) | Proiecte | Observații |
|---|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MIT | App, Core | |
| Microsoft.Extensions.DependencyInjection | 10.0.10 | MIT | App | |
| QuestPDF | 2026.7.2 | **licență duală** (`LICENSE.md` din pachet: Community / Professional / Enterprise) | App, Core, Dfir.Windows | decizie juridică a proprietarului; dependență `System.Numerics.Vectors 4.6.1` |
| DiscUtils.Registry | 0.16.13 | MIT | App, Infrastructure | parsare registry |
| Microsoft.Data.Sqlite.Core | 9.0.0 | MIT | App, Infrastructure, Dfir.Windows | |
| SQLitePCLRaw.bundle_e_sqlcipher | 2.1.10 | Apache-2.0 | App, Infrastructure, Dfir.Windows | aduce `SQLitePCLRaw.lib.e_sqlcipher` (Apache-2.0, **cod nativ**; nuspec: «unofficial and unsupported» builds of SQLCipher) |
| System.Diagnostics.EventLog | 10.0.10 | MIT | App, Infrastructure, Dfir.Windows | |
| System.Management | 10.0.10 | MIT | App, Core, Dfir.Windows, KeyGen | WMI |
| System.Security.Cryptography.ProtectedData | 10.0.10 | MIT | App, Core | DPAPI |
| System.IO.Hashing | 9.0.0 | MIT | Dfir.Core | |
| YamlDotNet | 18.1.0 | MIT | Dfir.Core | reguli Sigma-lite/politici |
| System.DirectoryServices | 10.0.9 | MIT | Dfir.Windows | LDAP (gate `NetworkPolicy`) |
| *(teste)* Microsoft.NET.Test.Sdk `17.*`, xunit `2.*`, xunit.runner.visualstudio `2.*` | flotante | — | Dfir.Tests, UI.Tests | nepinnate; licențe neverificate |

Tool-uri proprii de emitere a licențelor (`LogAnalyzer.KeyGen`, `LogAnalyzer.LicenseManager`, `Generate-LicenseKey.ps1`) nu trebuie livrate pe sisteme acreditate (conform `SECURITY.md`).
