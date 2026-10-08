# Surse și citate — verificare prin sondaj (HG 585 / INFOSEC / NIS2)

Generat 2026-10-08 pentru `tasks/loganalyzer/HG585_ACCREDITATION_REQUIREMENTS.md`. Pentru fiecare citat: sursa, URL/fișier, rândurile din textul extras, contextul de dinainte și de după și (pentru HG 585) controlul încrucișat în celelalte două copii.

Texte extrase (pdftotext / conversie HTML) din scratchpad: `src/legistm.txt`, `src/asist.txt`, `src/snppc1.txt`, `acr/src/Lege_182.2002.txt`, `acr/src/i2.txt`, `acr/src/ghid.txt`, `acr/src/nis2.txt`, `acr/src/r2690.txt`, `src/oug_raw.txt`. «Rânduri» = numere de rând din aceste fișiere.

## Surse și amprente

| Sursă | Fișier local (scratchpad) | SHA-256 | URL |
|---|---|---|---|
| HG 585/2002 consolidat CTCE 28-11-2022 | `legistm.pdf` | `19d3cc962fe222ac…af6d8a` | (URL nerecuperat) |
| HG 585/2002 consolidat CTCE 24-03-2005 (control) | `asist.pdf` | `c3ed40986a968635…2112a4` | (URL nerecuperat) |
| HG 585/2002 «forma sintetică» 29-06-2022 (control) | `snppc1.pdf` | `92032ee5d56de3e7…4cd701` | (URL nerecuperat) |
| Legea 182/2002 consolidată 13-03-2024 | `Lege_182.2002.pdf` | `a4e97fdebb7a5677…e4bf44` | https://www.sri.ro/assets/files/legislatie/2024/Lege_182.2002.pdf |
| OUG 155/2024 (MO 1332/31.12.2024) | `oug.pdf` | `03e9d024450a7be5…bee72d` | https://upt.ro/img/files/legislatie/2024/OUG_155_2024.pdf |
| Dir. 2022/2555 RO | `nis2.xhtml` | `a0e17bc5a1a31f9b…cd6b55` | https://publications.europa.eu/resource/celex/32022L2555 (Accept: application/xhtml+xml, Accept-Language: ron → cellar 9b84d482-85bd-11ed-9887-01aa75ed71a1.0020.03) |
| Reg. 2024/2690 RO | `r2690.xhtml` | `98739edf7d9e3b82…2c307d` | https://publications.europa.eu/resource/celex/32024R2690 (aceleași antete → cellar 28f15de8-8ce9-11ef-a130-01aa75ed71a1.0021.03) |
| INFOSEC 2 (ord. ORNISS 16/2014), anexa | `i2.html` | `bae6e039005a4cf1…54fb96` | https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala |
| Ghid PrOpSec DS 2 (ord. ORNISS 18/2014), anexa | `e111d2` | `a71e94c908b0569c…306545` | https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid |
| SRI — precizări acreditare SIC secret de serviciu | `d56872` | `7a4e63bdb42b886e…78d7b1` | https://www.sri.ro/assets/files/informatii-clasificate/Precizari_referitoare_la_acreditarea_SIC_SSv.pdf |

## Jurnal de obținere (ce nu a mers)

* `https://legislatie.just.ro/Public/DetaliiDocument/38227` și `…/DetaliiDocumentAfis/38227`: `WebFetch` → HTTP 502; `curl` (HTTP/1.1 și HTTP/2) → conexiune închisă / PROTOCOL_ERROR. **Neobținut.**
* `https://www.sie.ro/pdf/legislatie/585.pdf`: răspuns `200 text/html` (aplicație JS, nu PDF). **Neutilizabil.**
* `https://geomil.ro/webroot/fileslib/upload/files/legi/HG%20585%20din%202002.pdf`: HTTP 410. **Neutilizabil.**
* `https://sg.mapn.ro/proiecte/ProiectHG_1546568756.doc`: HTTP 410 (proiect de modificare). **Neobținut.**
* `https://www.academia.edu/10492605`: HTTP 403. **Neobținut.**
* Cele trei copii HG 585 folosite (`legistm.pdf`, `asist.pdf`, `snppc1.pdf`) au fost descărcate într-o etapă anterioară a sesiunii; URL-ul nu s-a păstrat. Originea lor este stabilită doar prin antetele din document (nota CTCE Piatra-Neamț; metadate PDF: `legistm.pdf` creat 2022-11-28, Word 2019; `asist.pdf` creat 2006-07-16, Acrobat Distiller; `snppc1.pdf` wkhtmltopdf 2022-06-29). **Până la confirmarea pe legislatie.just.ro, citatele HG 585 sunt «verificate în surse secundare, neconfirmate oficial».**
* Control încrucișat HG 585: pentru fiecare articol citat din `legistm.pdf` s-au comparat, după eliminarea diacriticelor/punctuației, trei fragmente (început, mijloc, sfârșit) cu `asist.pdf` și `snppc1.pdf`; rezultatul e în câmpul «Control» de mai jos (toate cele 109 articole HG au fost găsite în **ambele** copii).
* `legeaz.net` este un mirror neoficial al Monitorului Oficial; textul INFOSEC 2 și al Ghidului PrOpSec nu a fost confruntat cu MO original (neverificat).

## Citate

### `[HG-258]` — HG 585/2002, anexa, art. 258
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2068–2077 · **Control:** asist, snppc1
* **Context dinainte:** «domeniu. ⏎ C.Agenția de protecţie criptografică»
* **Citat (început):** «Articolul 258 Agentia de protecţie criptografica se organizeaza la nivel naţional, este subordonata institutiei desemnate la nivel naţional pentru protectia informaţiilor clasificate şi are urmatoarele atribuţii principale: a)asigură managementul materialelor …»
* **Context după:** «Secţiunea a 3-a ⏎ Măsuri, cerinţe şi moduri de operare»

### `[HG-327]` — HG 585/2002, anexa, art. 327
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2465–2470 · **Control:** asist, snppc1
* **Context dinainte:** «de către echipe de expertizare cu pregătire tehnică adecvată şi autorizate corespunzător. Aceste echipe vor ⏎ fi compuse din experți selecționati de către agenția de acreditare de securitate.»
* **Citat (început):** «Articolul 327 (1)În procesele de evaluare şi certificare se va stabili în ce măsură un SPAD sau RTD - SIC îndeplinește condiţiile de securitate specificate prin CSS, avându-se în vedere ca, după încheierea procesului de evaluare şi certificare, anumite secţiun…»
* **Context după:** «K.Verificări de rutină pentru menţinerea acreditării ⏎ Articolul 328»

### `[HG-21]` — HG 585/2002, anexa, art. 21
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 289–292 · **Control:** asist, snppc1
* **Context dinainte:** «»
* **Citat (început):** «Articolul 21 Ori de câte ori este posibil, emitentul unui document clasificat trebuie să precizeze dacă acesta poate fi declasificat ori trecut la un nivel inferior de secretizare, la o anumită dată sau la producerea unui anumit eveniment.»
* **Context după:** «Articolul 22 ⏎ (1)La schimbarea clasei sau nivelului de secretizare atribuit inițial unei informaţii, emitentul este obligat să»

### `[HG-65]` — HG 585/2002, anexa, art. 65
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 550–552 · **Control:** asist, snppc1
* **Context dinainte:** «Articolul 64 ⏎ Atribuirea aceluiași număr de înregistrare unor documente cu conținut diferit este interzisă.»
* **Citat (început):** «Articolul 65 Registrele de evidență vor fi completate de persoana desemnată care deține autorizație sau certificat de securitate corespunzător.»
* **Context după:** «Articolul 66 ⏎ (1)Multiplicarea prin dactilografiere şi procesare la calculator a documentelor clasificate poate fi realizată numai»

### `[HG-246]` — HG 585/2002, anexa, art. 246
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1990–1993 · **Control:** asist, snppc1
* **Context dinainte:** « la nivelul fiecărei SPAD şi RTD - SIC şi reprezintă persoana sau compartimentul cu responsabilitatea delegată de către agenția de securitate pentru informatică şi comunicații de a implementa metodele, mijloacele şi măsurile de securitate şi de a exploata SPAD şi RTD - SIC în condiţii de securitate.»
* **Citat (început):** «Articolul 246 CSTIC este condusă de către funcționarul de securitate TIC şi are în compunere administratorii de securitate şi, după caz, şi alti specialiști din SPAD sau RTD - SIC. Toata structura CSTIC face parte din personalul unităţii care administrează SPA…»
* **Context după:** «Articolul 247 ⏎ Exercitarea atribuţiilor CSTIC trebuie să cuprindă întregul ciclu de viaţă al SPAD sau RTD - SIC, începând cu»

### `[HG-325]` — HG 585/2002, anexa, art. 325
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2453–2460 · **Control:** asist, snppc1
* **Context dinainte:** «Cerinţele de evaluare şi certificare se includ în planificarea sistemului SPAD şi RTD - SIC şi sunt stipulate ⏎ explicit în CSS, imediat după ce modul de operare de securitate a fost stabilit.»
* **Citat (început):** «Articolul 325 Următoarele situaţii impun evaluarea şi certificarea de securitate în modul de operare de securitate multinivel: a)pentru SPAD sau RTD - SIC care stochează, procesează sau transmite informaţii clasificate strict secret de importanţă deosebită; b)…»
* **Context după:** «Articolul 326 ⏎ Procesele de evaluare şi certificare trebuie să se desfăşoare, conform principiilor şi instrucțiunilor aprobate,»

### `[HG-323]` — HG 585/2002, anexa, art. 323
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2443–2449 · **Control:** asist, snppc1
* **Context dinainte:** «autoritatea naţională de securitate, cu consultarea ADS şi a agențiilor INFOSEC, potrivit competențelor. ⏎ J.Evaluarea şi certificarea»
* **Citat (început):** «Articolul 323 În situaţiile ce privesc modul de operare de securitate multi-nivel, înainte de acreditarea propriu-zisă a SPAD sau RTD - SIC, hardware-ul, firmware-ul şi software-ul vor fi evaluate şi certificate de către agenția de acreditare de securitate, în…»
* **Context după:** «Articolul 324 ⏎ Cerinţele de evaluare şi certificare se includ în planificarea sistemului SPAD şi RTD - SIC şi sunt stipulate»

### `[HG-295]` — HG 585/2002, anexa, art. 295
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2301–2310 · **Control:** asist, snppc1
* **Context dinainte:** «b)Pentru nivelul strict secret şi strict secret de importanţă deosebită, informaţiile detaliate asupra mediului de ⏎ stocare, incluzând conţinutul şi nivelul de clasificare, se ţin într-un registru adecvat.»
* **Citat (început):** «Articolul 295 Controlul punctual şi de ansamblu al mediilor de stocare, pentru a asigura compatibilitatea cu procedurile de identificare şi control în vigoare, trebuie să asigure îndeplinirea următoarelor cerinţe: a)pentru nivelul secret - controalele punctual…»
* **Context după:** «G.Declasificarea şi distrugerea mediilor de stocare a informaţiilor în format electronic ⏎ Articolul 296»

### `[HG-298]` — HG 585/2002, anexa, art. 298
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2320–2322 · **Control:** asist, snppc1
* **Context dinainte:** «trebuie distrus printr-o procedură aprobată. ⏎ (2)Sunt interzise declasificarea şi refolosirea mediilor de stocare care conţin informaţii strict secrete de importanţă deosebită, acestea putând fi numai distruse, în conformitate cu procedurile operaționale de securitate.»
* **Citat (început):** «Articolul 298 Informaţiile clasificate în format electronic stocate pe un mediu de unică folosinţă - cartele, benzi perforate trebuie distruse conform prevederilor procedurilor operaționale de securitate.»
* **Context după:** «Secţiunea a 5-a ⏎ Reguli generale de securitate TIC»

### `[HG-302]` — HG 585/2002, anexa, art. 302
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2343–2346 · **Control:** asist, snppc1
* **Context dinainte:** «tehnic calificat, care are acces la informaţii de cel mai înalt nivel de clasificare pe care respectivul SPAD sau ⏎ RTD - SIC le va stoca, procesa sau transmite.»
* **Citat (început):** «Articolul 302 Toate echipamentele SPAD şi RTD - SIC vor fi instalate în conformitate cu reglementările specifice în vigoare, emise de către instituția desemnată la nivel naţional pentru protecția informaţiilor clasificate, cu directivele şi standardele tehnice…»
* **Context după:** «Articolul 303 ⏎ Sistemele SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat vor fi protejate corespunzător faţă de vulnerabilitățile de securitate cauzate de radiațiile compromițătoare - TEMPEST.»

### `[HG-303]` — HG 585/2002, anexa, art. 303
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2347–2348 · **Control:** asist, snppc1
* **Context dinainte:** «emise de către instituția desemnată la nivel naţional pentru protecția informaţiilor clasificate, cu directivele şi ⏎ standardele tehnice corespunzătoare.»
* **Citat (început):** «Articolul 303 Sistemele SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat vor fi protejate corespunzător faţă de vulnerabilitățile de securitate cauzate de radiațiile compromițătoare - TEMPEST.»
* **Context după:** «C.Securitatea în timpul procesării informaţiilor clasificate ⏎ Articolul 304»

### `[HG-14]` — HG 585/2002, anexa, art. 14
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 242–248 · **Control:** asist, snppc1
* **Context dinainte:** «periodică a tuturor informaţiilor secrete de stat cărora le-au atribuit nivelurile de secretizare, prilej cu care, ⏎ dacă este necesar, vor fi reevaluate nivelurile şi termenele de clasificare.»
* **Citat (început):** «Articolul 14 (1)Documentul elaborat pe baza prelucrarii informaţiilor cu niveluri de secretizare diferite va fi clasificat conform noului conținut, care poate fi superior originalelor. (2)Documentul rezultat din cumularea neprelucrată a unor extrase provenite …»
* **Context după:** «Articolul 15 ⏎ Marcarea informaţiilor clasificate are drept scop atenţionarea persoanelor care le gestionează sau le accesează»

### `[HG-15]` — HG 585/2002, anexa, art. 15
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 249–252 · **Control:** asist, snppc1
* **Context dinainte:** «(3)Rezumatele, traducerile şi extrasele din documentele clasificate primesc clasa sau nivelul de secretizare ⏎ corespunzător conținutului.»
* **Citat (început):** «Articolul 15 Marcarea informaţiilor clasificate are drept scop atenţionarea persoanelor care le gestionează sau le accesează că sunt în posesia unor informaţii în legătură cu care trebuie aplicate măsuri specifice de acces şi protecţie, în conformitate cu lege…»
* **Context după:** «Articolul 16 ⏎ Cazurile considerate supraevaluări ori subevaluări ale clasei sau nivelului de secretizare vor fi supuse atenţiei»

### `[HG-22]` — HG 585/2002, anexa, art. 22
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 293–299 · **Control:** asist, snppc1
* **Context dinainte:** «declasificat ori trecut la un nivel inferior de secretizare, la o anumită dată sau la producerea unui anumit ⏎ eveniment.»
* **Citat (început):** «Articolul 22 (1)La schimbarea clasei sau nivelului de secretizare atribuit inițial unei informaţii, emitentul este obligat să încunoștințeze structura/funcționarul de securitate, care va face menţiunile necesare în registrele de evidență. (2)Data şi noua clasă…»
* **Context după:** «Articolul 23 ⏎ (1)Informaţiile clasificate despre care s-a stabilit cu certitudine că sunt compromise sau iremediabil pierdute»

### `[HG-23]` — HG 585/2002, anexa, art. 23
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 300–304 · **Control:** asist, snppc1
* **Context dinainte:** «(3)Emitentul informaţiilor declasificate ori trecute în alt nivel de clasificare se va asigura că gestionarii acestora ⏎ sunt anunțati la timp, în scris, despre acest lucru.»
* **Citat (început):** «Articolul 23 (1)Informaţiile clasificate despre care s-a stabilit cu certitudine că sunt compromise sau iremediabil pierdute vor fi declasificate. (2)Declasificarea se face numai în baza cercetării prin care s-a stabilit compromiterea sau pierderea informaţiil…»
* **Context după:** «Articolul 24 ⏎ Informaţiile secrete de serviciu se declasifică de conducătorii unităţilor care le-au emis, prin scoaterea de pe»

### `[HG-45]` — HG 585/2002, anexa, art. 45
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 448–450 · **Control:** asist, snppc1
* **Context dinainte:** «Când documentele care conţin informaţii clasificate se emit în comun de două sau mai multe unităţi, denumirile acestora se înscriu separat în antet, iar la sfârşit se semnează de către conducătorii unităţilor respective, ⏎ de la stanga la dreapta, aplicându-se ștampilele corespunzătoare.»
* **Citat (început):** «Articolul 45 Informaţiile clasificate vor fi marcate, inscriptionate şi gestionate numai de către persoane care au autorizație sau certificat de securitate corespunzător nivelului de clasificare a acestora.»
* **Context după:** «Articolul 46 ⏎ (1)Toate documentele, indiferent de formă, care conţin informaţii clasificate au înscrise, pe fiecare pagină,»

### `[HG-46]` — HG 585/2002, anexa, art. 46
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 451–458 · **Control:** asist, snppc1
* **Context dinainte:** «Informaţiile clasificate vor fi marcate, inscriptionate şi gestionate numai de către persoane care au autorizație ⏎ sau certificat de securitate corespunzător nivelului de clasificare a acestora.»
* **Citat (început):** «Articolul 46 (1)Toate documentele, indiferent de formă, care conţin informaţii clasificate au înscrise, pe fiecare pagină, nivelul de secretizare. (2)Nivelul de secretizare se marchează prin ștampilare, dactilografiere, tipărire sau olograf, astfel: a)în parte…»
* **Context după:** «Articolul 47»

### `[HG-47]` — HG 585/2002, anexa, art. 47
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 459–465 · **Control:** asist, snppc1
* **Context dinainte:** «c)sub legendă, titlu sau scara de reprezentare şi în exterior - pe verso - atunci când acestea sunt pliate, pe ⏎ toate schemele, diagramele, hărțile, desenele şi alte asemenea documente.»
* **Citat (început):** «Articolul 47 Porțiunile clar identificabile din documentele clasificate complexe, cum sunt secţiunile, anexele, paragrafele, titlurile, care au niveluri diferite de secretizare sau care nu sunt clasificate, trebuie marcate corespunzător nivelului de clasificar…»
* **Context după:** «Articolul 48 ⏎ Marcajul de clasificare va fi aplicat separat de celelalte marcaje, cu caractere şi/sau culori diferite.»

### `[HG-48]` — HG 585/2002, anexa, art. 48
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 466–467 · **Control:** asist, snppc1
* **Context dinainte:** «titlurile, care au niveluri diferite de secretizare sau care nu sunt clasificate, trebuie marcate corespunzător ⏎ nivelului de clasificare şi secretizare.»
* **Citat (început):** «Articolul 48 Marcajul de clasificare va fi aplicat separat de celelalte marcaje, cu caractere şi/sau culori diferite.»
* **Context după:** «Articolul 49 ⏎ (1)Toate documentele clasificate aflate în lucru sau în stadiu de proiect vor avea inscrise menţiunile "Document»

### `[HG-49]` — HG 585/2002, anexa, art. 49
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 468–472 · **Control:** asist, snppc1
* **Context dinainte:** «Articolul 48 ⏎ Marcajul de clasificare va fi aplicat separat de celelalte marcaje, cu caractere şi/sau culori diferite.»
* **Citat (început):** «Articolul 49 (1)Toate documentele clasificate aflate în lucru sau în stadiu de proiect vor avea inscrise menţiunile "Document în lucru" sau "Proiect" şi vor fi marcate potrivit clasei sau nivelului de secretizare a informaţiilor ce le conţin. (2)Gestionarea do…»
* **Context după:** «Articolul 50 ⏎ Documentele sau materialele care conţin informaţii clasificate şi sunt destinate unei persoane strict determinate vor fi inscripționate, sub destinatar, cu menţiunea "Personal".»

### `[HG-56]` — HG 585/2002, anexa, art. 56
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 506–509 · **Control:** asist, snppc1
* **Context dinainte:** «clasificate vor avea inscripţionat clasa sau nivelul de secretizare, numărul şi data înregistrării în evidențe şi li ⏎ se va atașa o listă cu denumirea acestora.»
* **Citat (început):** «Articolul 56 (1)Atunci când se utilizează documente clasificate ca surse pentru întocmirea unui alt document, marcajele documentelor sursă le vor determina pe cele ale documentului rezultat. (2)Pe documentul rezultat se vor preciza documentele sursă care au st…»
* **Context după:** «Articolul 57 ⏎ Numărul şi data inițială a înregistrării documentului clasificat trebuie păstrate, chiar dacă i se aduc amendamente, până când documentul respectiv va face obiectul reevaluării clasei sau a nivelului de secretizare.»

### `[HG-78]` — HG 585/2002, anexa, art. 78
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 636–644 · **Control:** asist, snppc1
* **Context dinainte:** «al informaţiilor conținute, arhivate şi protejate corespunzător clasei sau nivelului de secretizare a documentului ⏎ final.»
* **Citat (început):** «Articolul 78 (1)Informaţiile strict secrete de importanţă deosebită destinate distrugerii vor fi înapoiate unităţii emitente cu adresa de restituire. (2)Fiecare asemenea informație va fi trecută pe un proces-verbal de distrugere, care va fi aprobat de conducer…»
* **Context după:** «Articolul 79 ⏎ (1)Distrugerea informaţiilor strict secrete, secrete şi secrete de serviciu va fi evidențiată într-un proces-verbal»

### `[HG-79]` — HG 585/2002, anexa, art. 79
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 645–650 · **Control:** asist, snppc1
* **Context dinainte:** «(4)Procesele-verbale de distrugere şi documentele de evidență ale acestora vor fi arhivate şi păstrate cel puţin ⏎ 10 ani.»
* **Citat (început):** «Articolul 79 (1)Distrugerea informaţiilor strict secrete, secrete şi secrete de serviciu va fi evidențiată într-un proces-verbal semnat de două persoane asistente autorizate să aibă acces la informaţii de acest nivel, avizat de structura/funcționarul de securi…»
* **Context după:** «Articolul 80 ⏎ (1)Distrugerea ciornelor documentelor secrete de stat se realizează de către persoanele care le-au elaborat.»

### `[HG-88]` — HG 585/2002, anexa, art. 88
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 736–752 · **Control:** asist, snppc1
* **Context dinainte:** «Articolul 87 ⏎ Conducătorii unităţilor deținătoare de secrete de stat vor asigura condiţiile necesare pentru ca toate persoanele care gestionează astfel de informaţii să cunoască reglementările în vigoare referitoare la protecția informaţiilor clasificate.»
* **Citat (început):** «Articolul 88 (1)Conducătorii unităţilor deținătoare de informaţii secrete de stat au obligaţia de a înștiinta, în scris, instituţiile prevăzute la art. 25 din Legea nr. 182/2002, potrivit competențelor, prin cel mai operativ sistem de comunicare, despre compro…»
* **Context după:** «Articolul 89 ⏎ Pentru prejudiciile cauzate deţinătorului informației secrete de stat compromise, acesta are dreptul la despăgubiri civile, potrivit dreptului comun.»

### `[HG-236]` — HG 585/2002, anexa, art. 236
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1797–1799 · **Control:** asist, snppc1
* **Context dinainte:** «Dispoziții generale»
* **Citat (început):** «Articolul 236 Modalitățile şi măsurile de protecţie a informaţiilor clasificate care se prezintă în format electronic sunt similare celor pe suport de hârtie.»
* **Context după:** «Articolul 237 ⏎ Termenii specifici, folosiți în prezentul capitol, cu aplicabilitate în domeniul INFOSEC, se definesc după cum»

### `[HG-240]` — HG 585/2002, anexa, art. 240
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1941–1949 · **Control:** asist, snppc1
* **Context dinainte:** «(2)Încărcarea informaţiilor pe mediile prevăzute în alin. (1) lit. b, precum şi interpretarea lor pentru a deveni ⏎ inteligibile, se face cu ajutorul echipamentelor electronice specializate.»
* **Citat (început):** «Articolul 240 (1)Sistemele SPAD şi RTD - SIC au dreptul să stocheze, să proceseze sau să transmită informaţii clasificate, numai dacă sunt autorizate potrivit prezentei hotărâri. (2)În vederea autorizării SPAD şi RTD - SIC unitățile vor întocmi, cu aprobarea o…»
* **Context după:** «Articolul 241 ⏎ (1)Aplicarea reglementărilor în vigoare referitoare la protecția informaţiilor clasificate în format electronic»

### `[HG-241]` — HG 585/2002, anexa, art. 241
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1950–1963 · **Control:** asist, snppc1
* **Context dinainte:** «(3)SPAD şi RTD - SIC vor fi supuse procesului de acreditare, urmat de evaluări periodice, în vederea menținerii ⏎ acreditării.»
* **Citat (început):** «Articolul 241 (1)Aplicarea reglementărilor în vigoare referitoare la protecția informaţiilor clasificate în format electronic funcţionează unitar la nivel naţional. Sistemul de emitere şi implementare a măsurilor de securitate adresate protecției informaţiilor…»
* **Context după:** «Articolul 242 ⏎ Măsurile de securitate INFOSEC vor fi structurate după nivelul de clasificare al informaţiilor pe care le protejează şi în conformitate cu conţinutul acestora.»

### `[HG-243]` — HG 585/2002, anexa, art. 243
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1966–1968 · **Control:** asist, snppc1
* **Context dinainte:** «Articolul 242 ⏎ Măsurile de securitate INFOSEC vor fi structurate după nivelul de clasificare al informaţiilor pe care le protejează şi în conformitate cu conţinutul acestora.»
* **Citat (început):** «Articolul 243 Conducatorul unităţii deținătoare de informaţii clasificate răspunde de securitatea propriilor informaţii care sunt stocate, procesate sau transmise în SPAD sau RTD - SIC.»
* **Context după:** «Articolul 244 ⏎ (1)În fiecare unitate care administrează SPAD şi RTD - SIC în care se stochează, se procesează sau se transmit»

### `[HG-244]` — HG 585/2002, anexa, art. 244
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1969–1987 · **Control:** asist, snppc1
* **Context dinainte:** «Conducatorul unităţii deținătoare de informaţii clasificate răspunde de securitatea propriilor informaţii care ⏎ sunt stocate, procesate sau transmise în SPAD sau RTD - SIC.»
* **Citat (început):** «Articolul 244 (1)În fiecare unitate care administrează SPAD şi RTD - SIC în care se stochează, se procesează sau se transmit informaţii clasificate, se va institui o componentă de securitate pentru tehnologia informației şi a comunicațiilor - CSTIC, în subordi…»
* **Context după:** «Articolul 245 ⏎ CSTIC se instituie la nivelul fiecărei SPAD şi RTD - SIC şi reprezintă persoana sau compartimentul cu responsabilitatea delegată de către agenția de securitate pentru informatică şi comunicații de a implementa metodele, mijloacele şi măsurile de securitate şi de a exploata SPAD şi RTD »

### `[HG-247]` — HG 585/2002, anexa, art. 247
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1994–1997 · **Control:** asist, snppc1
* **Context dinainte:** «şi, după caz, şi alti specialiști din SPAD sau RTD - SIC. Toata structura CSTIC face parte din personalul unităţii ⏎ care administrează SPAD sau RTD - SIC.»
* **Citat (început):** «Articolul 247 Exercitarea atribuţiilor CSTIC trebuie să cuprindă întregul ciclu de viaţă al SPAD sau RTD - SIC, începând cu proiectarea, continuând cu elaborarea specificațiilor, testarea instalării, acreditarea, testarea periodică în vederea reacreditării, ex…»
* **Context după:** «Articolul 248 ⏎ CSTIC mijlocește cooperarea dintre conducerea unităţii căreia îi aparţine SPAD sau RTD - SIC şi agenția pentru»

### `[HG-248]` — HG 585/2002, anexa, art. 248
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1998–2009 · **Control:** asist, snppc1
* **Context dinainte:** «cu elaborarea specificațiilor, testarea instalării, acreditarea, testarea periodică în vederea reacreditării, exploatarea operațională, modificarea şi încheind cu scoaterea din uz. În anumite situaţii, ⏎ rolul CSTIC poate fi preluat de către alte componente ale unităţii, în decursul ciclului de viaţă.»
* **Citat (început):** «Articolul 248 CSTIC mijlocește cooperarea dintre conducerea unităţii căreia îi aparţine SPAD sau RTD - SIC şi agenția pentru acreditare de securitate, atunci când unitatea: a)planifică dezvoltarea sau achiziția de SPAD sau RTD; b)propune schimbări ale unei con…»
* **Context după:** «Articolul 249 ⏎ CSTIC, cu aprobarea autorităţii de acreditare de securitate, stabilesște standardele şi procedurile de securitate»

### `[HG-249]` — HG 585/2002, anexa, art. 249
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2010–2014 · **Control:** asist, snppc1
* **Context dinainte:** «g)planifică sau propune intreprinderea oricărei alte activităţi referitoare la îmbunătăţirea securității SPAD sau ⏎ RTD - SIC deja acreditate.»
* **Citat (început):** «Articolul 249 CSTIC, cu aprobarea autorităţii de acreditare de securitate, stabilesște standardele şi procedurile de securitate care trebuie respectate de către furnizorii de echipamente, pe parcursul dezvoltării, instalarii şi testării SPAD şi RTD - SIC şi ră…»
* **Context după:** «Articolul 250 ⏎ CSTIC stabilește, pentru structurile de securitate şi management ale SPAD şi RTD - SIC, încă de la înfiinţare,»

### `[HG-251]` — HG 585/2002, anexa, art. 251
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2018–2022 · **Control:** asist, snppc1
* **Context dinainte:** «CSTIC stabilește, pentru structurile de securitate şi management ale SPAD şi RTD - SIC, încă de la înfiinţare, ⏎ responsabilitățile pe care le vor exercita pe tot ciclul de viaţă al SPAD şi RTD - SIC respective.»
* **Citat (început):** «Articolul 251 Activitatea INFOSEC din SPAD şi RTD - SIC, desfăşurată de către CSTIC, trebuie condusă şi coordonată de persoane care deţin certificat de securitate corespunzător, cu pregătire de specialitate în domeniul sistemelor TIC precum şi al securității a…»
* **Context după:** «Articolul 252 ⏎ Protecția SPAD şi RTD - SIC din compunerea sistemelor de armament şi de detecție va fi definită în contextul»

### `[HG-253]` — HG 585/2002, anexa, art. 253
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2031–2041 · **Control:** asist, snppc1
* **Context dinainte:** «A.Agenția de acreditare de securitate»
* **Citat (început):** «Articolul 253 Agenția de acreditare de securitate este subordonată instituției desemnate la nivel naţional pentru protecția informaţiilor clasificate, are reprezentanți delegaţi din cadrul ADS implicate, în funcție de SPAD şi RTD - SIC care trebuie acreditate,…»
* **Context după:** «Articolul 254 ⏎ Agenția de acreditare de securitate îşi exercită atribuţiile în domeniul INFOSEC în numele instituției desemnate»

### `[HG-256]` — HG 585/2002, anexa, art. 256
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2051–2062 · **Control:** asist, snppc1
* **Context dinainte:** «nivel naţional pentru protecția informaţiilor electronice clasificate, având reprezentanți delegaţi din cadrul ⏎ ADS implicate care acţionează la nivel naţional.»
* **Citat (început):** «Articolul 256 Agentia este responsabilă de conceperea şi implementarea mijloacelor, metodelor şi masurilor de protecţie a informaţiilor clasificate care sunt stocate, procesate sau transmise prin intermediul SPAD şi RTD - SIC şi are, în principal, următoarele …»
* **Context după:** «Articolul 257 ⏎ Pentru îndeplinirea atribuţiilor sale, agenția de securitate pentru informatică şi comunicații cooperează cu»

### `[HG-259]` — HG 585/2002, anexa, art. 259
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2082–2086 · **Control:** asist, snppc1
* **Context dinainte:** «A.Măsuri şi cerinţe specifice INFOSEC»
* **Citat (început):** «Articolul 259 (1)Măsurile de protecţie a informaţiilor clasificate în format electronic se aplică sistemelor SPAD şi RTD - SIC care stochează, procesează sau transmit asemenea informaţii. (2)Unitățile deținătoare de informaţii clasificate au obligaţia de a sta…»
* **Context după:** «Articolul 260 ⏎ Măsurile de securitate destinate protecției SPAD şi RTD - SIC trebuie să asigure controlul accesului pentru»

### `[HG-260]` — HG 585/2002, anexa, art. 260
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2087–2090 · **Control:** asist, snppc1
* **Context dinainte:** «(2)Unitățile deținătoare de informaţii clasificate au obligaţia de a stabili şi implementa un ansamblu de măsuri ⏎ de securitate a sistemelor SPAD şi RTD - SIC - fizice, de personal, administrative, de tip TEMPEST şi criptografic.»
* **Citat (început):** «Articolul 260 Măsurile de securitate destinate protecției SPAD şi RTD - SIC trebuie să asigure controlul accesului pentru prevenirea sau detectarea divulgării neautorizate a informaţiilor. Procesul de certificare şi acreditare va stabili dacă aceste măsuri sun…»
* **Context după:** «B.Cerinţe de securitate specifice SPAD şi RTD - SIC ⏎ Articolul 261»

### `[HG-261]` — HG 585/2002, anexa, art. 261
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2092–2100 · **Control:** asist, snppc1
* **Context dinainte:** «dacă aceste măsuri sunt corespunzătoare. ⏎ B.Cerinţe de securitate specifice SPAD şi RTD - SIC»
* **Citat (început):** «Articolul 261 (1)Cerinţele de securitate specifice - CSS se constituie într-un document încheiat între agenția de acreditare de securitate şi CSTIC, ce va cuprinde principii şi măsuri de securitate care trebuie să stea la baza procesului de certificare şi acre…»
* **Context după:** «Articolul 262 ⏎ CSS vor fi formulate încă din faza de proiectare a SPAD sau RTD - SIC şi vor fi dezvoltate pe tot ciclul de viaţă»

### `[HG-262]` — HG 585/2002, anexa, art. 262
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2101–2103 · **Control:** asist, snppc1
* **Context dinainte:** «»
* **Citat (început):** «Articolul 262 CSS vor fi formulate încă din faza de proiectare a SPAD sau RTD - SIC şi vor fi dezvoltate pe tot ciclul de viaţă al sistemului.»
* **Context după:** «Articolul 263 ⏎ CSS au la bază standardele naţionale de protecţie, parametrii esențiali ai mediului operațional, nivelul minim»

### `[HG-263]` — HG 585/2002, anexa, art. 263
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2104–2107 · **Control:** asist, snppc1
* **Context dinainte:** «CSS vor fi formulate încă din faza de proiectare a SPAD sau RTD - SIC şi vor fi dezvoltate pe tot ciclul de viaţă ⏎ al sistemului.»
* **Citat (început):** «Articolul 263 CSS au la bază standardele naţionale de protecţie, parametrii esențiali ai mediului operațional, nivelul minim de autorizare a personalului, nivelul de clasificare a informaţiilor gestionate şi modul de operare a sistemului care urmează să fie ac…»
* **Context după:** «C.Moduri de operare ⏎ Articolul 264»

### `[HG-264]` — HG 585/2002, anexa, art. 264
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2109–2114 · **Control:** asist, snppc1
* **Context dinainte:** «care urmează să fie acreditat. ⏎ C.Moduri de operare»
* **Citat (început):** «Articolul 264 SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii clasificate vor fi certificate şi acreditate să opereze, pe anumite perioade de timp, în unul din următoarele moduri de operare: a)dedicat; b)de nivel înalt; c)multi-nivel.»
* **Context după:** «Articolul 265 ⏎ (1)În modul de operare dedicat, toate persoanele cu drept de acces la SPAD sau la RTD trebuie să aibă certificat»

### `[HG-265]` — HG 585/2002, anexa, art. 265
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2115–2123 · **Control:** asist, snppc1
* **Context dinainte:** «b)de nivel înalt; ⏎ c)multi-nivel.»
* **Citat (început):** «Articolul 265 (1)În modul de operare dedicat, toate persoanele cu drept de acces la SPAD sau la RTD trebuie să aibă certificat de securitate pentru cel mai înalt nivel de clasificare a informaţiilor stocate, procesate sau transmise prin aceste sisteme. Necesit…»
* **Context după:** «Articolul 266 ⏎ (1)În modul de operare de nivel înalt, toate persoanele cu drept de acces la SPAD sau la RTD - SIC trebuie să»

### `[HG-266]` — HG 585/2002, anexa, art. 266
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2124–2134 · **Control:** asist, snppc1
* **Context dinainte:** «cerințelor impuse de cel mai înalt nivel de clasificare a informaţiilor gestionate şi de toate categoriile de informaţii ⏎ cu destinație specială stocate, procesate sau transmise în cadrul SPAD sau RTD.»
* **Citat (început):** «Articolul 266 (1)În modul de operare de nivel înalt, toate persoanele cu drept de acces la SPAD sau la RTD - SIC trebuie să aibă certificat de securitate pentru cel mai înalt nivel de clasificare a informaţiilor stocate, procesate sau transmise în cadrul SPAD …»
* **Context după:** «Articolul 267 ⏎ (1)În modul de operare multi-nivel, accesul la informaţiile clasificate se face diferențiat, potrivit principiului»

### `[HG-267]` — HG 585/2002, anexa, art. 267
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2135–2143 · **Control:** asist, snppc1
* **Context dinainte:** «operare vor fi protejate ca informaţii cu destinație specială, având cel mai înalt nivel de clasificare care a fost ⏎ constatat în mulțimea informaţiilor stocate, procesate sau vehiculate prin sistem.»
* **Citat (început):** «Articolul 267 (1)În modul de operare multi-nivel, accesul la informaţiile clasificate se face diferențiat, potrivit principiului necesităţii de a cunoaște, conform următoarelor reguli: a)nu toate persoanele cu drept de acces la SPAD sau RTD - SIC au certificat…»
* **Context după:** «D.Administratorii de securitate ⏎ Articolul 268»

### `[HG-268]` — HG 585/2002, anexa, art. 268
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2145–2152 · **Control:** asist, snppc1
* **Context dinainte:** «care să asigure un mod selectiv, individual, de acces la informaţiile clasificate din cadrul SPAD sau RTD - SIC. ⏎ D.Administratorii de securitate»
* **Citat (început):** «Articolul 268 (1)Securitatea SPAD a rețelei şi a obiectivului SIC se asigură prin funcțiile de administrator de securitate. (2)Administratorii de securitate sunt: a)administratorul de securitate al SPAD; b)administratorul de securitate al rețelei; c)administra…»
* **Context după:** «Articolul 269 ⏎ (1)CSTIC desemnează un administrator de securitate al SPAD responsabil cu supervizarea dezvoltării, implementării şi administrării măsurilor de securitate dintr-un SPAD, inclusiv participarea la elaborarea procedurilor»

### `[HG-269]` — HG 585/2002, anexa, art. 269
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2153–2157 · **Control:** asist, snppc1
* **Context dinainte:** «(3)Funcțiile de administratori de securitate trebuie să asigure îndeplinirea atribuţiilor CSTIC. Dacă este cazul, ⏎ aceste funcții pot fi cumulate de către un singur specialist.»
* **Citat (început):** «Articolul 269 (1)CSTIC desemnează un administrator de securitate al SPAD responsabil cu supervizarea dezvoltării, implementării şi administrării măsurilor de securitate dintr-un SPAD, inclusiv participarea la elaborarea procedurilor operaționale de securitate.…»
* **Context după:** «Articolul 270»

### `[HG-271]` — HG 585/2002, anexa, art. 271
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2164–2170 · **Control:** asist, snppc1
* **Context dinainte:** «Administratorul de securitate al rețelei este desemnat de CSTIC pentru un SIC de mari dimensiuni sau în ⏎ cazul interconectării mai multor SPAD şi îndeplinește atribuţii privind managementul securității comunicațiilor.»
* **Citat (început):** «Articolul 271 (1)Administratorul de securitate al obiectivului SIC este desemnat de CSTIC sau de autoritatea de securitate competentă şi răspunde de asigurarea implementării şi menţinerea măsurilor de securitate aplicabile obiectivului SIC respectiv. (2)Respon…»
* **Context după:** «E.Utilizatorii şi vizitatorii ⏎ Articolul 272»

### `[HG-272]` — HG 585/2002, anexa, art. 272
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2172–2177 · **Control:** asist, snppc1
* **Context dinainte:** «SPAD şi/sau RTD. Responsabilitatile şi măsurile de securitate pentru fiecare zonă de amplasare a unui terminal/stație de lucru care funcţionează la distanță trebuie explicit determinate. ⏎ E.Utilizatorii şi vizitatorii»
* **Citat (început):** «Articolul 272 (1)Toţi utilizatorii de SPAD sau RTD - SIC poartă responsabilitatea în ce privește securitatea acestor sisteme raportate, în principal, la drepturile acordate şi sunt îndrumați de către administratorii de securitate. (2)Utilizatorii vor fi autori…»
* **Context după:** «Articolul 273 ⏎ Vizitatorii trebuie să aibă autorizare de securitate de nivel corespunzător şi sa îndeplinească principiul necesităţii de a cunoaște, în situaţia în care accesul unui vizitator fără autorizare de securitate este considerat»

### `[HG-274]` — HG 585/2002, anexa, art. 274
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2187–2191 · **Control:** asist, snppc1
* **Context dinainte:** «A.Securitatea personalului»
* **Citat (început):** «Articolul 274 (1)Utilizatorii SPAD şi RTD - SIC sunt autorizați şi li se permite accesul la informaţii clasificate pe baza principiului necesităţii de a cunoaște şi în funcție de nivelul de clasificare a informaţiilor stocate, procesate sau transmise prin aces…»
* **Context după:** «Articolul 275 ⏎ În proiectarea SPAD şi RTD - SIC trebuie să se aibă în vedere ca atribuirea sarcinilor şi răspunderilor personalului să se faca în așa fel încât să nu existe o persoană care să aibă cunoştinţă sau acces la toate programele»

### `[HG-275]` — HG 585/2002, anexa, art. 275
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2192–2194 · **Control:** asist, snppc1
* **Context dinainte:** «pentru instruirea şi supravegherea personalului, inclusiv a personalului de proiectare de sistem care are acces ⏎ la SPAD şi RTD, în vederea prevenirii şi înlăturării vulnerabilităților faţă de accesarea neautorizată.»
* **Citat (început):** «Articolul 275 În proiectarea SPAD şi RTD - SIC trebuie să se aibă în vedere ca atribuirea sarcinilor şi răspunderilor personalului să se faca în așa fel încât să nu existe o persoană care să aibă cunoştinţă sau acces la toate programele şi cheile de securitate…»
* **Context după:** «Articolul 276 ⏎ Procedurile de lucru ale personalului din SPAD şi RTD - SIC trebuie să asigure separarea între operaţiunile de»

### `[HG-276]` — HG 585/2002, anexa, art. 276
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2195–2199 · **Control:** asist, snppc1
* **Context dinainte:** «În proiectarea SPAD şi RTD - SIC trebuie să se aibă în vedere ca atribuirea sarcinilor şi răspunderilor personalului să se faca în așa fel încât să nu existe o persoană care să aibă cunoştinţă sau acces la toate programele ⏎ şi cheile de securitate - parole, mijloace de identificare personală.»
* **Citat (început):** «Articolul 276 Procedurile de lucru ale personalului din SPAD şi RTD - SIC trebuie să asigure separarea între operaţiunile de programare şi cele de exploatare a sistemului sau rețelei. Este interzis, cu excepţia unor situaţii speciale, ca personalul să facă atâ…»
* **Context după:** «Articolul 277 ⏎ Pentru orice fel de modificare aplicată unui sistem SPAD sau RTD - SIC este obligatorie colaborarea a cel puţin»

### `[HG-277]` — HG 585/2002, anexa, art. 277
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2200–2203 · **Control:** asist, snppc1
* **Context dinainte:** «personalul să facă atât programarea, cât şi operarea sistemelor sau rețelelor şi trebuie instituite proceduri ⏎ speciale pentru depistarea acestor situaţii.»
* **Citat (început):** «Articolul 277 Pentru orice fel de modificare aplicată unui sistem SPAD sau RTD - SIC este obligatorie colaborarea a cel puţin două persoane - regula celor doi. Procedurile de securitate vor menţiona explicit situaţiile în care regula celor doi trebuie aplicată…»
* **Context după:** «Articolul 278 ⏎ Pentru a asigura implementarea corectă a măsurilor de securitate, personalul SPAD şi RTD - SIC şi personalul»

### `[HG-282]` — HG 585/2002, anexa, art. 282
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2231–2235 · **Control:** asist, snppc1
* **Context dinainte:** «se impune cerinţa de aplicare a regulii de lucru cu două persoane şi în alte zone, ce vor fi stabilite în stadiul ⏎ inițial al proiectului şi prezentate în cadrul CSS.»
* **Citat (început):** «Articolul 282 Când un SPAD este exploatat în mod autonom, deconectat în mod permanent de alte SPAD, ţinând cont de condiţiile specifice, de alte măsuri de securitate, tehnice sau procedurale şi de rolul pe care îl are respectivul SPAD în funcționarea de ansamb…»
* **Context după:** «C.Controlul accesului la SPAD şi/sau la RTD - SIC ⏎ Articolul 283»

### `[HG-283]` — HG 585/2002, anexa, art. 283
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2237–2239 · **Control:** asist, snppc1
* **Context dinainte:** «măsuri specifice de protecţie, adaptate la structura acestui SPAD, conform nivelului de clasificare a informaţiilor gestionate. ⏎ C.Controlul accesului la SPAD şi/sau la RTD - SIC»
* **Citat (început):** «Articolul 283 Toate informaţiile şi materialele care privesc accesul la un SPAD sau RTD - SIC sunt controlate şi protejate prin reglementări corespunzătoare nivelului de clasificare cel mai înalt şi specificului informaţiilor la care respectivul SPAD sau RTD -…»
* **Context după:** «Articolul 284 ⏎ Când nu mai sunt utilizate, informaţiile şi materialele de control specificate la articolul precedent trebuie să»

### `[HG-284]` — HG 585/2002, anexa, art. 284
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2240–2242 · **Control:** asist, snppc1
* **Context dinainte:** «Toate informaţiile şi materialele care privesc accesul la un SPAD sau RTD - SIC sunt controlate şi protejate ⏎ prin reglementări corespunzătoare nivelului de clasificare cel mai înalt şi specificului informaţiilor la care respectivul SPAD sau RTD - SIC permite accesul.»
* **Citat (început):** «Articolul 284 Când nu mai sunt utilizate, informaţiile şi materialele de control specificate la articolul precedent trebuie să fie distruse conform prevederilor prezentelor standarde.»
* **Context după:** «D.Securitatea informaţiilor clasificate în format electronic ⏎ Articolul 285»

### `[HG-285]` — HG 585/2002, anexa, art. 285
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2244–2246 · **Control:** asist, snppc1
* **Context dinainte:** «fie distruse conform prevederilor prezentelor standarde. ⏎ D.Securitatea informaţiilor clasificate în format electronic»
* **Citat (început):** «Articolul 285 Informaţiile clasificate în format electronic trebuie să fie controlate conform regulilor INFOSEC, înainte de a fi transmise din zonele SPAD şi RTD - SIC sau din cele cu terminale la distanță.»
* **Context după:** «Articolul 286 ⏎ Modul în care este prezentată informația în clar, chiar dacă se utilizează codul prescurtat de transmisie sau»

### `[HG-286]` — HG 585/2002, anexa, art. 286
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2247–2250 · **Control:** asist, snppc1
* **Context dinainte:** «Informaţiile clasificate în format electronic trebuie să fie controlate conform regulilor INFOSEC, înainte de a fi ⏎ transmise din zonele SPAD şi RTD - SIC sau din cele cu terminale la distanță.»
* **Citat (început):** «Articolul 286 Modul în care este prezentată informația în clar, chiar dacă se utilizează codul prescurtat de transmisie sau reprezentarea binară ori alte forme de transmitere la distanță, nu trebuie să influențeze nivelul de clasificare acordat informaţiilor r…»
* **Context după:** «Articolul 287 ⏎ Când informaţiile sunt transferate între diverse SPAD sau RTD - SIC, ele trebuie să fie protejate atât în timpul»

### `[HG-287]` — HG 585/2002, anexa, art. 287
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2251–2254 · **Control:** asist, snppc1
* **Context dinainte:** «reprezentarea binară ori alte forme de transmitere la distanță, nu trebuie să influențeze nivelul de clasificare ⏎ acordat informaţiilor respective.»
* **Citat (început):** «Articolul 287 Când informaţiile sunt transferate între diverse SPAD sau RTD - SIC, ele trebuie să fie protejate atât în timpul transferului, cât şi la nivelul sistemelor informatice ale beneficiarului, corespunzător cu nivelul de clasificare al informaţiilor t…»
* **Context după:** «Articolul 288 ⏎ Toate mediile de stocare a informaţiilor se păstrează într-o modalitate care să corespundă celui mai înalt nivel»

### `[HG-288]` — HG 585/2002, anexa, art. 288
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2255–2257 · **Control:** asist, snppc1
* **Context dinainte:** «transferului, cât şi la nivelul sistemelor informatice ale beneficiarului, corespunzător cu nivelul de clasificare ⏎ al informaţiilor transmise.»
* **Citat (început):** «Articolul 288 Toate mediile de stocare a informaţiilor se păstrează într-o modalitate care să corespundă celui mai înalt nivel de clasificare a informaţiilor stocate sau suporților, fiind protejate permanent.»
* **Context după:** «Articolul 289 ⏎ Copierea informaţiilor clasificate situate pe medii de stocare specifice TIC se execută în conformitate cu prevederile din procedurile operaționale de securitate.»

### `[HG-289]` — HG 585/2002, anexa, art. 289
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2258–2259 · **Control:** asist, snppc1
* **Context dinainte:** «Toate mediile de stocare a informaţiilor se păstrează într-o modalitate care să corespundă celui mai înalt nivel ⏎ de clasificare a informaţiilor stocate sau suporților, fiind protejate permanent.»
* **Citat (început):** «Articolul 289 Copierea informaţiilor clasificate situate pe medii de stocare specifice TIC se execută în conformitate cu prevederile din procedurile operaționale de securitate.»
* **Context după:** «Articolul 290 ⏎ Mediile refolosibile de stocare a informaţiilor utilizate pentru înregistrarea informaţiilor clasificate îşi mențin»

### `[HG-290]` — HG 585/2002, anexa, art. 290
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2260–2263 · **Control:** asist, snppc1
* **Context dinainte:** «Articolul 289 ⏎ Copierea informaţiilor clasificate situate pe medii de stocare specifice TIC se execută în conformitate cu prevederile din procedurile operaționale de securitate.»
* **Citat (început):** «Articolul 290 Mediile refolosibile de stocare a informaţiilor utilizate pentru înregistrarea informaţiilor clasificate îşi mențin cea mai înaltă clasificare pentru care au fost utilizate anterior, până când respectivelor informaţii li se reduce nivelul de clas…»
* **Context după:** «E.Controlul şi evidența informaţiilor în format electronic ⏎ Articolul 291»

### `[HG-291]` — HG 585/2002, anexa, art. 291
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2265–2271 · **Control:** asist, snppc1
* **Context dinainte:** «nivelul de clasificare sau sunt declasificate, moment în care mediile susmenționate se reclasifică în mod corespunzător sau sunt distruse în conformitate cu prevederile procedurilor operaționale de securitate. ⏎ E.Controlul şi evidența informaţiilor în format electronic»
* **Citat (început):** «Articolul 291 (1)Evidența automată a accesului la informaţiile clasificate în format electronic se ține în registrele de acces şi trebuie realizată necondiționat prin software. (2)Registrele de acces se păstrează pe o perioadă stabilită de comun acord între ag…»
* **Context după:** «Articolul 292»

### `[HG-292]` — HG 585/2002, anexa, art. 292
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2272–2281 · **Control:** asist, snppc1
* **Context dinainte:** «(3)Perioada minimă de păstrare a registrelor de acces la informaţiile strict secrete de importanţă deosebită este ⏎ de 10 ani, iar a registrelor de acces la informaţiile strict secrete şi secrete, de cel puţin 3 ani.»
* **Citat (început):** «Articolul 292 (1)Mediile de stocare care conţin informaţii clasificate utilizate în interiorul unei zone SPAD pot fi manipulate ca unic material clasificat, cu condiţia ca materialul să fie identificat, marcat cu nivelul său de clasificare şi controlat în inte…»
* **Context după:** «Articolul 293 ⏎ În cazul în care un mediu de stocare este generat într-un SPAD sau RTD - SIC, iar apoi este transmis într-o»

### `[HG-294]` — HG 585/2002, anexa, art. 294
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2288–2300 · **Control:** asist, snppc1
* **Context dinainte:** «informaţiilor în format electronic. ⏎ F.Manipularea şi controlul mediilor de stocare a informaţiilor clasificate în format electronic»
* **Citat (început):** «Articolul 294 (1)Toate mediile de stocare secrete de stat se identifică şi se controlează în mod corespunzător nivelului de secretizare. (2)Pentru informaţiile neclasificate sau secrete de serviciu se aplică regulamente de securitate interne. (3)Identificarea …»
* **Context după:** «Articolul 295 ⏎ Controlul punctual şi de ansamblu al mediilor de stocare, pentru a asigura compatibilitatea cu procedurile de»

### `[HG-296]` — HG 585/2002, anexa, art. 296
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2312–2314 · **Control:** asist, snppc1
* **Context dinainte:** «controlează punctual, în legătură cu prezenta fizica şi conţinutul lor. ⏎ G.Declasificarea şi distrugerea mediilor de stocare a informaţiilor în format electronic»
* **Citat (început):** «Articolul 296 Informaţiile clasificate înregistrate pe medii de stocare refolosibile se șterg doar în conformitate cu procedurile operaționale de securitate.»
* **Context după:** «Articolul 297 ⏎ (1)Când un mediu de stocare urmează sa iasă din uz, trebuie să fie declasificat suprimându-se orice marcaje»

### `[HG-297]` — HG 585/2002, anexa, art. 297
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2315–2319 · **Control:** asist, snppc1
* **Context dinainte:** «Informaţiile clasificate înregistrate pe medii de stocare refolosibile se șterg doar în conformitate cu procedurile ⏎ operaționale de securitate.»
* **Citat (început):** «Articolul 297 (1)Când un mediu de stocare urmează sa iasă din uz, trebuie să fie declasificat suprimându-se orice marcaje de clasificare, ulterior putând fi utilizat ca mediu de stocare nesecret. Dacă acesta nu poate fi declasificat, trebuie distrus printr-o p…»
* **Context după:** «Articolul 298 ⏎ Informaţiile clasificate în format electronic stocate pe un mediu de unică folosinţă - cartele, benzi perforate trebuie distruse conform prevederilor procedurilor operaționale de securitate.»

### `[HG-300]` — HG 585/2002, anexa, art. 300
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2330–2336 · **Control:** asist, snppc1
* **Context dinainte:** «Toate mijloacele folosite pentru transmiterea electromagnetică a informaţiilor clasificate se supun instrucțiunilor de securitate a comunicațiilor emise de către instituția desemnată la nivel naţional pentru protecția ⏎ informaţiilor clasificate.»
* **Citat (început):** «Articolul 300 Într-un SPAD - SIC trebuie să se dispună mijloace de interzicere a accesului la informaţiile clasificate de la toate terminalele/stațiile de lucru la distanță, atunci când se solicită acest lucru, prin deconectare fizică sau prin proceduri softwa…»
* **Context după:** «B.Securitatea la instalare şi faţă de emisiile electromagnetice ⏎ Articolul 301»

### `[HG-304]` — HG 585/2002, anexa, art. 304
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2350–2352 · **Control:** asist, snppc1
* **Context dinainte:** «Sistemele SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat vor fi protejate corespunzător faţă de vulnerabilitățile de securitate cauzate de radiațiile compromițătoare - TEMPEST. ⏎ C.Securitatea în timpul procesării informaţiilor clasificate»
* **Citat (început):** «Articolul 304 Procesarea informaţiilor se realizează în conformitate cu procedurile operaționale de securitate, prevăzute în prezentele standarde.»
* **Context după:** «Articolul 305 ⏎ Transmiterea informaţiilor secrete de stat către instalaţii automate - a căror funcționare nu necesită prezența»

### `[HG-305]` — HG 585/2002, anexa, art. 305
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2353–2357 · **Control:** asist, snppc1
* **Context dinainte:** «Procesarea informaţiilor se realizează în conformitate cu procedurile operaționale de securitate, prevăzute în ⏎ prezentele standarde.»
* **Citat (început):** «Articolul 305 Transmiterea informaţiilor secrete de stat către instalaţii automate - a căror funcționare nu necesită prezența unui operator uman - este interzisă, cu excepţia cazului când se aplică reglementări speciale aprobate de către autoritatea de acredit…»
* **Context după:** «Articolul 306 ⏎ În SPAD sau RTD-SIC care au utilizatori - existenți sau potențiali - fără certificate de securitate emise conform»

### `[HG-307]` — HG 585/2002, anexa, art. 307
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2362–2364 · **Control:** asist, snppc1
* **Context dinainte:** «prezentelor standarde nu se pot stoca, procesa sau transmite informaţii strict secrete de importanţă deosebită. ⏎ D.Procedurile operaționale de securitate»
* **Citat (început):** «Articolul 307 Procedurile operaționale de securitate reprezintă descrierea implementării strategiei de securitate ce urmează să fie adoptată, a procedurilor operaționale de urmat şi a responsabilităților personalului.»
* **Context după:** «Articolul 308 ⏎ Procedurile operaționale de securitate sunt elaborate de către agenția de concepere şi implementare a metodelor, mijloacelor şi măsurilor de securitate, în colaborare cu CSTIC, precum şi cu agenția de acreditare de»

### `[HG-308]` — HG 585/2002, anexa, art. 308
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2365–2368 · **Control:** asist, snppc1
* **Context dinainte:** «Procedurile operaționale de securitate reprezintă descrierea implementării strategiei de securitate ce urmează ⏎ să fie adoptată, a procedurilor operaționale de urmat şi a responsabilităților personalului.»
* **Citat (început):** «Articolul 308 Procedurile operaționale de securitate sunt elaborate de către agenția de concepere şi implementare a metodelor, mijloacelor şi măsurilor de securitate, în colaborare cu CSTIC, precum şi cu agenția de acreditare de securitate, care are atribuţii …»
* **Context după:** «E.Protecția produselor software şi managementul configurației ⏎ Articolul 309»

### `[HG-309]` — HG 585/2002, anexa, art. 309
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2370–2375 · **Control:** asist, snppc1
* **Context dinainte:** «securitate va aproba procedurile de operare înainte de a autoriza stocarea, procesarea sau transmiterea informaţiilor secrete de stat prin SPAD - RTD - SIC. ⏎ E.Protecția produselor software şi managementul configurației»
* **Citat (început):** «Articolul 309 CSTIC are obligaţia să efectueze controale periodice, prin care să stabilească dacă toate produsele software originale - sisteme de operare generale, subsisteme şi pachete soft - aflate în folosinţă, sunt protejate în condiţii conforme cu nivelul…»
* **Context după:** «Articolul 310 ⏎ (1)Este interzisă utilizarea de software neautorizat de către agenția de acreditare de securitate.»

### `[HG-310]` — HG 585/2002, anexa, art. 310
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2376–2379 · **Control:** asist, snppc1
* **Context dinainte:** «programelor - software de aplicație se stabilește pe baza evaluării nivelului de secretizare a acestora, ţinând ⏎ cont de nivelul de clasificare a informaţiilor pe care urmeaza să le proceseze.»
* **Citat (început):** «Articolul 310 (1)Este interzisă utilizarea de software neautorizat de către agenția de acreditare de securitate. (2)Conservarea exemplarelor originale, a copiilor - backup sau off-site, precum şi salvările periodice ale datelor obținute din procesare vor fi ex…»
* **Context după:** «Articolul 311 ⏎ (1)Versiunile software care sunt în uz trebuie să fie verificate la intervale regulate, pentru a garanta integritatea»

### `[HG-311]` — HG 585/2002, anexa, art. 311
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2380–2386 · **Control:** asist, snppc1
* **Context dinainte:** «(2)Conservarea exemplarelor originale, a copiilor - backup sau off-site, precum şi salvările periodice ale datelor ⏎ obținute din procesare vor fi executate în conformitate cu prevederile procedurilor operaționale de securitate.»
* **Citat (început):** «Articolul 311 (1)Versiunile software care sunt în uz trebuie să fie verificate la intervale regulate, pentru a garanta integritatea şi funcționarea lor corectă. (2)Versiunile noi sau modificate ale software-ului nu vor fi folosite pentru procesarea informaţiil…»
* **Context după:** «F.Verificări pentru depistarea virusilor de calculator şi a software-ului nociv ⏎ Articolul 312»

### `[HG-312]` — HG 585/2002, anexa, art. 312
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2388–2392 · **Control:** asist, snppc1
* **Context dinainte:** «poate fi folosit înainte de a fi verificat de către CSTIC. ⏎ F.Verificări pentru depistarea virusilor de calculator şi a software-ului nociv»
* **Citat (început):** «Articolul 312 Verificarea prezenței virusilor şi software-ului nociv se face în conformitate cu cerinţele impuse de către agenția de acreditare de securitate.»
* **Context după:** «Articolul 313 ⏎ (1)Versiunile de software noi sau modificate - sisteme de operare, subsisteme, pachete de software şi software»

### `[HG-313]` — HG 585/2002, anexa, art. 313
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2393–2399 · **Control:** asist, snppc1
* **Context dinainte:** «Verificarea prezenței virusilor şi software-ului nociv se face în conformitate cu cerinţele impuse de către agenția de acreditare de securitate.»
* **Citat (început):** «Articolul 313 (1)Versiunile de software noi sau modificate - sisteme de operare, subsisteme, pachete de software şi software de aplicație - stocate pe diferite medii care se introduc într-o unitate, trebuie verificate obligatoriu pe sisteme de calcul izolate, …»
* **Context după:** «G.Întreţinerea tehnică a SPAD sau RTD - SIC ⏎ Articolul 314»

### `[HG-316]` — HG 585/2002, anexa, art. 316
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2408–2412 · **Control:** asist, snppc1
* **Context dinainte:** «Articolul 315 ⏎ Scoaterea echipamentelor sau a componentelor hardware din zona SPAD sau RTD - SIC se execută în conformitate cu prevederile procedurilor operaționale de securitate.»
* **Citat (început):** «Articolul 316 Cerinţele menţionate la art. 314 trebuie stipulate în CSS, iar procedurile de desfăşurare a activităţii respective trebuie stabilite în procedurile operaționale de securitate. Nu se acceptă tipurile de întreţinere care constau în aplicarea unor p…»
* **Context după:** «H.Achiziții ⏎ Articolul 317»

### `[HG-317]` — HG 585/2002, anexa, art. 317
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2414–2416 · **Control:** asist, snppc1
* **Context dinainte:** «respectivă se desfăşoară sub control strict şi numai cu aprobarea agenției de acreditare de securitate. ⏎ H.Achiziții»
* **Citat (început):** «Articolul 317 Sistemele SPAD sau RTD - SIC, precum şi componentele lor hardware şi software sunt achiziţionate de la furnizori interni sau externi selectați dintre cei agreați de către agenția de acreditare de securitate.»
* **Context după:** «Articolul 318 ⏎ Componentele sistemelor de securitate implementate în SPAD sau RTD - SIC trebuie acreditate pe baza unei»

### `[HG-318]` — HG 585/2002, anexa, art. 318
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2417–2419 · **Control:** asist, snppc1
* **Context dinainte:** «Sistemele SPAD sau RTD - SIC, precum şi componentele lor hardware şi software sunt achiziţionate de la ⏎ furnizori interni sau externi selectați dintre cei agreați de către agenția de acreditare de securitate.»
* **Citat (început):** «Articolul 318 Componentele sistemelor de securitate implementate în SPAD sau RTD - SIC trebuie acreditate pe baza unei documentaţii tehnice amănunțite privind proiectarea, realizarea şi modul de distribuire al acestora.»
* **Context după:** «Articolul 319 ⏎ SPAD sau RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat sau componentele lor»

### `[HG-319]` — HG 585/2002, anexa, art. 319
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2420–2424 · **Control:** asist, snppc1
* **Context dinainte:** «Componentele sistemelor de securitate implementate în SPAD sau RTD - SIC trebuie acreditate pe baza unei ⏎ documentaţii tehnice amănunțite privind proiectarea, realizarea şi modul de distribuire al acestora.»
* **Citat (început):** «Articolul 319 SPAD sau RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat sau componentele lor de bază - sisteme de operare de scop general, produse de limitare a funcţionării pentru realizarea securității şi produse pentru comunicare…»
* **Context după:** «Articolul 320 ⏎ Pentru SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de serviciu, sistemele»

### `[HG-320]` — HG 585/2002, anexa, art. 320
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2425–2427 · **Control:** asist, snppc1
* **Context dinainte:** «şi produse pentru comunicare în rețea - se pot achiziționa numai dacă au fost evaluate şi certificate de către ⏎ agenția de acreditare de securitate.»
* **Citat (început):** «Articolul 320 Pentru SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de serviciu, sistemele şi componentele lor de bază vor respecta, pe cât posibil, criteriile prevăzute de prezentele standarde.»
* **Context după:** «Articolul 321 ⏎ La închirierea unor componente hardware sau software, în special a unor medii de stocare, se va ține cont ca»

### `[HG-321]` — HG 585/2002, anexa, art. 321
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2428–2432 · **Control:** asist, snppc1
* **Context dinainte:** «Pentru SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de serviciu, sistemele ⏎ şi componentele lor de bază vor respecta, pe cât posibil, criteriile prevăzute de prezentele standarde.»
* **Citat (început):** «Articolul 321 La închirierea unor componente hardware sau software, în special a unor medii de stocare, se va ține cont ca astfel de echipamente, odată utilizate în SPAD sau RTD - SIC ce procesează, stochează sau transmit informaţii clasificate, vor fi supuse …»
* **Context după:** «I. Acreditarea SPAD şi RTD - SIC ⏎ Articolul 322»

### `[HG-322]` — HG 585/2002, anexa, art. 322
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2434–2441 · **Control:** asist, snppc1
* **Context dinainte:** «componentele respective nu vor putea fi scoase din zonele SPAD sau RTD - SIC decât după declasificare. ⏎ I. Acreditarea SPAD şi RTD - SIC»
* **Citat (început):** «Articolul 322 (1)Toate SPAD şi RTD - SIC, înainte de a fi utilizate pentru stocarea, procesarea sau transmiterea informaţiilor clasificate, trebuie acreditate de către agenția de acreditare de securitate, pe baza datelor furnizate de către CSS, procedurilor op…»
* **Context după:** «J.Evaluarea şi certificarea ⏎ Articolul 323»

### `[HG-324]` — HG 585/2002, anexa, art. 324
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2450–2452 · **Control:** asist, snppc1
* **Context dinainte:** «»
* **Citat (început):** «Articolul 324 Cerinţele de evaluare şi certificare se includ în planificarea sistemului SPAD şi RTD - SIC şi sunt stipulate explicit în CSS, imediat după ce modul de operare de securitate a fost stabilit.»
* **Context după:** «Articolul 325 ⏎ Următoarele situaţii impun evaluarea şi certificarea de securitate în modul de operare de securitate multinivel:»

### `[HG-328]` — HG 585/2002, anexa, art. 328
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2472–2475 · **Control:** asist, snppc1
* **Context dinainte:** «pe parcursul fazelor de dezvoltare. ⏎ K.Verificări de rutină pentru menţinerea acreditării»
* **Citat (început):** «Articolul 328 Pentru toate SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat, CSTIC stabilește proceduri de control prin care să se poată stabili dacă schimbările intervenite în SIC sunt de natură a le compromite securitatea.»
* **Context după:** «Articolul 329 ⏎ (1)Modificările care implică reacreditarea sau pentru care se solicită aprobarea anterioară a agenției de acreditare de securitate trebuie să fie identificate cu claritate şi expuse în CSS.»

### `[HG-329]` — HG 585/2002, anexa, art. 329
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2476–2480 · **Control:** asist, snppc1
* **Context dinainte:** «stabilește proceduri de control prin care să se poată stabili dacă schimbările intervenite în SIC sunt de natură ⏎ a le compromite securitatea.»
* **Citat (început):** «Articolul 329 (1)Modificările care implică reacreditarea sau pentru care se solicită aprobarea anterioară a agenției de acreditare de securitate trebuie să fie identificate cu claritate şi expuse în CSS. (2)După orice modificare, reparare sau eroare care ar fi…»
* **Context după:** «Articolul 330 ⏎ (1)Toate SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat sunt inspectate»

### `[HG-330]` — HG 585/2002, anexa, art. 330
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2481–2485 · **Control:** asist, snppc1
* **Context dinainte:** «RTD - SIC, CSTIC trebuie să efectueze o verificare privind funcționarea corectă a dispozitivelor de securitate. ⏎ (3)Menţinerea acreditării SPAD sau RTD - SIC trebuie să depindă de satisfacerea criteriilor de verificare.»
* **Citat (început):** «Articolul 330 (1)Toate SPAD şi RTD - SIC care stochează, procesează sau transmit informaţii secrete de stat sunt inspectate şi reexaminate periodic de către agenția de acreditare de securitate. (2)Pentru SPAD sau RTD - SIC care stochează, procesează sau transm…»
* **Context după:** «L.Securitatea microcalculatoarelor sau a calculatoarelor personale ⏎ Articolul 331»

### `[HG-331]` — HG 585/2002, anexa, art. 331
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2487–2492 · **Control:** asist, snppc1
* **Context dinainte:** «deosebită, inspecţia se va face cel puţin o dată pe an. ⏎ L.Securitatea microcalculatoarelor sau a calculatoarelor personale»
* **Citat (început):** «Articolul 331 (1)Microcalculatoarele sau calculatoarele personale care au discuri fixe sau alte medii nevolatile de stocare a informației, ce operează autonom sau ca parte a unei rețele, precum şi calculatoarele portabile cu discuri fixe sunt considerate medii…»
* **Context după:** «Articolul 332 ⏎ Echipamentelor prevăzute la art. 331 trebuie să li se acorde nivelul de protecţie pentru acces, manipulare,»

### `[HG-332]` — HG 585/2002, anexa, art. 332
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2493–2497 · **Control:** asist, snppc1
* **Context dinainte:** «informaţiilor. ⏎ (2)În măsura în care acestea stochează informaţii clasificate trebuie supuse prezentelor standarde.»
* **Citat (început):** «Articolul 332 Echipamentelor prevăzute la art. 331 trebuie să li se acorde nivelul de protecţie pentru acces, manipulare, stocare şi transport, corespunzător cu cel mai înalt nivel de clasificare a informaţiilor care au fost vreodată stocate sau procesate pe e…»
* **Context după:** «M.Utilizarea echipamentelor de calcul proprietate privată ⏎ Articolul 333»

### `[HG-333]` — HG 585/2002, anexa, art. 333
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2499–2502 · **Control:** asist, snppc1
* **Context dinainte:** «cu procedurile legale. ⏎ M.Utilizarea echipamentelor de calcul proprietate privată»
* **Citat (început):** «Articolul 333 (1)Este interzisă utilizarea mediilor de stocare amovibile, a software-ului şi a hardware-ului, aflate în proprietate privată, pentru stocarea, procesarea şi transmiterea informaţiilor secrete de stat. (2)Pentru informaţiile secrete de serviciu s…»
* **Context după:** «Articolul 334 ⏎ Este interzisă introducerea mediilor de stocare amovibile, a software-ului şi hardware-ului, aflate în proprietate privată, în zonele în care se stochează, se procesează sau se transmit informaţii clasificate, fără aprobarea conducătorului unităţii.»

### `[HG-334]` — HG 585/2002, anexa, art. 334
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2503–2507 · **Control:** asist, snppc1
* **Context dinainte:** «privată, pentru stocarea, procesarea şi transmiterea informaţiilor secrete de stat. ⏎ (2)Pentru informaţiile secrete de serviciu sau neclasificate, se aplică reglementările interne ale unităţii.»
* **Citat (început):** «Articolul 334 Este interzisă introducerea mediilor de stocare amovibile, a software-ului şi hardware-ului, aflate în proprietate privată, în zonele în care se stochează, se procesează sau se transmit informaţii clasificate, fără aprobarea conducătorului unităţ…»
* **Context după:** «N.Utilizarea echipamentelor contractorilor sau a celor puse la dispoziţie de alte instituții ⏎ Articolul 335»

### `[HG-335]` — HG 585/2002, anexa, art. 335
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2509–2511 · **Control:** asist, snppc1
* **Context dinainte:** «N.Utilizarea echipamentelor contractorilor sau a celor puse la dispoziţie de alte instituții»
* **Citat (început):** «Articolul 335 Utilizarea într-un obiectiv a echipamentelor şi a software-ului contractanților, pentru stocarea, procesarea sau transmiterea informaţiilor clasificate este permisă numai cu avizul CSTIC şi aprobarea șefului unităţii.»
* **Context după:** «Articolul 336 ⏎ Utilizarea într-un obiectiv a echipamentelor şi software-ului puse la dispoziţie de către alte institutii poate fi»

### `[HG-336]` — HG 585/2002, anexa, art. 336
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2512–2515 · **Control:** asist, snppc1
* **Context dinainte:** «Utilizarea într-un obiectiv a echipamentelor şi a software-ului contractanților, pentru stocarea, procesarea sau ⏎ transmiterea informaţiilor clasificate este permisă numai cu avizul CSTIC şi aprobarea șefului unităţii.»
* **Citat (început):** «Articolul 336 Utilizarea într-un obiectiv a echipamentelor şi software-ului puse la dispoziţie de către alte institutii poate fi permisă, în acest caz echipamentele sunt evidențiate în inventarul unităţii, în ambele situaţii, trebuie obţinut avizul CSTIC.»
* **Context după:** «O.Marcarea informaţiilor cu destinație specială ⏎ Articolul 337»

### `[HG-337]` — HG 585/2002, anexa, art. 337
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2517–2520 · **Control:** asist, snppc1
* **Context dinainte:** «avizul CSTIC. ⏎ O.Marcarea informaţiilor cu destinație specială»
* **Citat (început):** «Articolul 337 Marcarea informaţiilor cu destinație specială se aplică, în mod obișnuit, informaţiilor clasificate care necesită o distribuţie limitată şi manipulare specială, suplimentar faţă de caracterul atribuit prin clasificarea de securitate.»
* **Context după:** «Capitolul 9 ⏎ CONTRAVENȚII ŞI SANCȚIUNI LA NORMELE PRIVIND PROTECȚIA INFORMAȚIILOR CLASIFICATE»

### `[HG-76]` — HG 585/2002, anexa, art. 76
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 620–627 · **Control:** asist, snppc1
* **Context dinainte:** «În cazul în care un document secret de stat este studiat de o persoană abilitată, pentru care s-a stabilit necesitatea de a accesa astfel de documente în vederea îndeplinirii sarcinilor de serviciu, această activitate trebuie ⏎ consemnată în fișa de consultare, conform modelului din anexa nr. 1.»
* **Citat (început):** «Articolul 76 (1)Informaţiile clasificate iesite din termenul de clasificare se arhivează sau se distrug. (2)Arhivarea sau distrugerea unui document clasificat se menţionează în registrul de evidență principal, prin consemnarea cotei arhivistice de regaăsire sa…»
* **Context după:** «Articolul 77»

### `[HG-77]` — HG 585/2002, anexa, art. 77
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 628–635 · **Control:** asist, snppc1
* **Context dinainte:** «(4)Distrugerea documentelor clasificate sau a ciornelor care conţin informaţii cu acest caracter se face astfel ⏎ încât să nu mai poată fi reconstituite.»
* **Citat (început):** «Articolul 77 (1)Documentele de lucru, ciornele sau materialele acumulate sau create în procesul de elaborare a unui document, care conţin informaţii clasificate, de regulă, se distrug. (2)În cazul în care se păstrează, acestea vor fi datate, marcate cu clasa s…»
* **Context după:** «Articolul 78 ⏎ (1)Informaţiile strict secrete de importanţă deosebită destinate distrugerii vor fi înapoiate unităţii emitente cu»

### `[HG-237-INFOSEC]` — HG 585/2002, art. 237, definiția INFOSEC
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1803–1807 · **Control:** asist, snppc1
* **Context dinainte:** «Termenii specifici, folosiți în prezentul capitol, cu aplicabilitate în domeniul INFOSEC, se definesc după cum ⏎ urmează:»
* **Citat (început):** «– INFOSEC - ansamblul măsurilor şi structurilor de protecţie a informaţiilor clasificate care sunt prelucrate, stocate sau transmise prin intermediul sistemelor informatice de comunicații şi al altor sisteme electronice, împotriva amenințărilor şi a oricăror a…»
* **Context după:** «»

### `[HG-237-ACRED]` — HG 585/2002, art. 237, definiția «acreditarea»
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1899–1911 · **Control:** asist, snppc1
* **Context dinainte:** «menţionează măsurile în care SPAD şi RTD - SIC satisfac cerinţele de securitate, precum şi măsura în care ⏎ produsele informatice de securitate răspund exigențelor referitoare la protecția informaţiilor clasificate în format electronic;»
* **Citat (început):** «– acreditarea - etapa de acordare a autorizării şi aprobării unui SPAD sau RTD - SIC de a prelucra informaţii clasificate, în spațiul/mediul operațional propriu. Etapa de acreditare trebuie să se desfăşoare după ce s-au implementat toate procedurile de securit…»
* **Context după:** «– zona SPAD - reprezintă o zonă de lucru în care se gasesc şi operează unul sau mai multe calculatoare, ⏎ unităţi periferice locale şi de stocare, mijloace de control şi echipament specific de rețea şi de comunicații.»

### `[HG-237-EVAL]` — HG 585/2002, art. 237, definiția «evaluarea»
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1886–1894 · **Control:** asist, snppc1
* **Context dinainte:** «– TEMPEST - ansamblul măsurilor de testare şi de realizare a securității împotriva scurgerii de informaţii, prin ⏎ intermediul emisiilor electromagnetice parazite;»
* **Citat (început):** «– evaluarea - examinarea detaliată, din punct de vedere tehnic şi funcțional, a aspectelor de securitate ale SPAD şi RTD - SIC sau a produselor de securitate, de către o autoritate abilitată în acest sens. Prin procesul de evaluare se verifică: a)prezența faci…»
* **Context după:** «– certificarea - emiterea unui document de constatare, la care se atașează unul de analiză, în care sunt ⏎ prezentate modul în care a decurs evaluarea şi rezultatele acesteia, în documentul de constatare se»

### `[HG-237-CERT]` — HG 585/2002, art. 237, definiția «certificarea»
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1895–1898 · **Control:** asist, snppc1
* **Context dinainte:** «e)stabilirea nivelului de încredere al SPAD sau RTD - SIC ori al produselor informatice de securitate implementate; ⏎ f)existența performanțelor de securitate ale produselor informatice de securitate instalate în SPAD sau RTDSIC;»
* **Citat (început):** «– certificarea - emiterea unui document de constatare, la care se atașează unul de analiză, în care sunt prezentate modul în care a decurs evaluarea şi rezultatele acesteia, în documentul de constatare se menţionează măsurile în care SPAD şi RTD - SIC satisfac…»
* **Context după:** «– acreditarea - etapa de acordare a autorizării şi aprobării unui SPAD sau RTD - SIC de a prelucra informaţii ⏎ clasificate, în spațiul/mediul operațional propriu.»

### `[HG-237-NEREP]` — HG 585/2002, art. 237, «autenticitatea» și «nerepudierea»
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1854–1858 · **Control:** asist, snppc1
* **Context dinainte:** «– disponibilitatea asigurarea condiţiilor necesare regăsirii şi folosirii cu ușurință, ori de câte ori este nevoie, ⏎ cu respectarea strictă a condiţiilor de confidențialitate şi integritate a informaţiilor clasificate;»
* **Citat (început):** «– autenticitatea - asigurarea posibilităţii de verificare a identităţii pe care un utilizator de SPAD sau RTD pretinde că o are; – nerepudierea - măsura prin care se asigură faptul că, după emiterea/recepționarea unei informaţii într-un sistem de comunicații s…»
* **Context după:** «– risc de securitate - probabilitatea ca o amenințare sau o vulnerabilitate ale SPAD sau RTD - SIC să se ⏎ materializeze în mod efectiv;»

### `[HG-237-CIA]` — HG 585/2002, art. 237, «confidențialitatea», «integritatea», «disponibilitatea»
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1847–1853 · **Control:** asist, snppc1
* **Context dinainte:** «sau împiedica extragerea sau modificarea informaţiilor clasificate stocate, procesate, transmise prin intermediul acestora - prin interceptare, alterare, distrugere, accesare neautorizată cu mijloace electronice, precum ⏎ şi invalidarea de servicii sau funcții, prin mijloace specifice;»
* **Citat (început):** «– confidențialitatea - asigurarea accesului la informaţii clasificate numai pe baza certificatului de securitate al persoanei, în acord cu nivelul de secretizare a informației accesate şi a permisiunii rezultate din aplicarea principiului nevoii de a cunoaște;…»
* **Context după:** «– autenticitatea - asigurarea posibilităţii de verificare a identităţii pe care un utilizator de SPAD sau RTD ⏎ pretinde că o are;»

### `[HG-237-SIC]` — HG 585/2002, art. 237, definiția SIC
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1841–1843 · **Control:** asist, snppc1
* **Context dinainte:** «– RTD locală - rețea de transmisii de date care interconectează mai multe computere sau echipamente de ⏎ rețea, situate în același perimetru;»
* **Citat (început):** «– sistemul informatic şi de comunicații - SIC - ansamblu informatic prin intermediul căruia se stochează, se procesează şi se transmit informaţii în format electronic, alcătuit din cel puţin un SPAD, izolat sau conectat la o RTD. Poate avea o configurație comp…»
* **Context după:** «– securitatea SPAD, RTD şi SIC - aplicarea măsurilor de securitate la SPAD şi RTD - SIC cu scopul de a preveni ⏎ sau împiedica extragerea sau modificarea informaţiilor clasificate stocate, procesate, transmise prin intermediul acestora - prin interceptare, alterare, distrugere, accesare neautorizată c»

### `[HG-237-REGULA2]` — HG 585/2002, art. 237, «regula celor doi»
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1870–1870 · **Control:** asist, snppc1
* **Context dinainte:** «de cost corelat cu consecințele care ar decurge din divulgarea, modificarea sau ștergerea informaţiilor care ⏎ trebuie protejate;»
* **Citat (început):** «– regula celor doi - obligativitatea colaborării a două persoane pentru îndeplinirea unei activităţi specifice;»
* **Context după:** «– produs informatic de securitate - componenta de securitate care se incorporeaza într-un SPAD sau RTD SIC şi care servește la sporirea sau asigurarea confidențialității, integrității, disponibilităţii, autenticităţii şi ⏎ nerepudierii informaţiilor stocate, procesate sau transmise;»

### `[HG-237-COMPUSEC]` — HG 585/2002, art. 237, COMPUSEC
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1873–1874 · **Control:** asist, snppc1
* **Context dinainte:** «– produs informatic de securitate - componenta de securitate care se incorporeaza într-un SPAD sau RTD SIC şi care servește la sporirea sau asigurarea confidențialității, integrității, disponibilităţii, autenticităţii şi ⏎ nerepudierii informaţiilor stocate, procesate sau transmise;»
* **Citat (început):** «– securitatea calculatoarelor - COMPUSEC - aplicarea la nivelul fiecărui calculator a facilităților de securitate hardware, software şi firmware, pentru a preveni divulgarea, manevrarea, modificarea sau ștergerea neautorizată a informaţiilor clasificate ori in…»
* **Context după:** «– securitatea comunicațiilor - COMSEC - aplicarea măsurilor de securitate în telecomunicații, cu scopul de a ⏎ proteja mesajele dintr-un sistem de telecomunicații, care ar putea fi interceptate, studiate, analizate şi, prin»

### `[HG-3-INCIDENT]` — HG 585/2002, art. 3, definiția «incident de securitate»
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 138–139 · **Control:** asist, snppc1
* **Context dinainte:** «multiplicare, manipulare, transport, transmitere, inventariere, păstrare, arhivare sau distrugere a informaţiilor ⏎ clasificate;»
* **Citat (început):** «– incident de securitate - orice acțiune sau inacțiune contrară reglementărilor de securitate a cărei consecinţă a determinat sau este de natură să determine compromiterea informaţiilor clasificate;»
* **Context după:** «– indicator de interdicție text sau simbol care semnalează interzicerea accesului sau derulării unor activităţi ⏎ în zone, obiective, sectoare sau locuri care prezintă importanţă deosebită pentru protecția informaţiilor clasificate;»

### `[HG-3-MARCARE]` — HG 585/2002, art. 3, definiția «marcare»
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 148–149 · **Control:** asist, snppc1
* **Context dinainte:** «Serviciul Român de Informaţii, Serviciul de Informaţii Externe, Serviciul de Protecţie şi Pază, Serviciul de ⏎ Telecomunicații Speciale, potrivit competențelor stabilite prin lege;»
* **Citat (început):** «– marcare - activitatea de inscripționare a nivelului de secretizare a informației şi de semnalare a cerințelor speciale de protecţie a acesteia;»
* **Context după:** «– material clasificat - document sau produs prelucrat ori în curs de prelucrare, care necesită a fi protejat ⏎ împotriva cunoașterii neautorizate;»

### `[HG-238]` — HG 585/2002, art. 238 (abrevieri CSTIC, CSS)
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 1929–1934 · **Control:** asist, snppc1
* **Context dinainte:** «– vulnerabilitatea - slăbiciune sau lipsă de control care ar putea permite sau facilita o manevră tehnică, ⏎ procedurală sau operațională, prin care se amenință o valoare sau ţintă specifică.»
* **Citat (început):** «Articolul 238 Abrevierile utilizate în prezentul capitol semnifică: a)CSTIC - componenta de securitate pentru tehnologia informației şi comunicațiilor instituită în unitățile deținătoare de informaţii clasificate; b)TIC - tehnologia informației şi comunicațiil…»
* **Context după:** «Articolul 239 ⏎ (1)Informaţiile care se prezintă în format electronic pot fi:»

### `[HG-338-1e]` — HG 585/2002, art. 338 alin. (1) lit. e)
* **Sursă:** HG 585/2002 (forma consolidată CTCE, valabilă 28-11-2022)
* **URL:** (URL nerecuperat) — fișier local `legistm.pdf` (extras text: `scratchpad/src/legistm.txt`); control: `asist.txt`, `snppc1.txt` din același director
* **Rânduri în textul extras:** 2531–2532 · **Control:** asist, snppc1
* **Context dinainte:** «c)neîndeplinirea obligaţiilor prevăzute la art. 31, 41-43, 213, 214; ⏎ d)nerespectarea normelor prevăzute în art. 140-142, 145, 159, 160, 162, 163, 179-181, 183 alin. (1) şi 185190;»
* **Citat (început):** «e)neîndeplinirea sau îndeplinirea defectuoasa a obligaţiilor prevăzute în art. 240 alin. (2) şi (3), art. 243 şi art. 248, precum şi nerespectarea regulilor prevăzute în art. 274-336.»
* **Context după:** «(2)Contravențiile prevăzute la alin. (1) se sancţionează astfel: ⏎ a)contravențiile prevăzute la alin. (1) lit. a) se sancţionează cu amendă de la 500.000 lei la 50.000.000 lei în»

### `[L182-4]` — Legea 182/2002, art. 4
* **Sursă:** Legea 182/2002, forma consolidată 13.03.2024 (SRI)
* **URL:** https://www.sri.ro/assets/files/legislatie/2024/Lege_182.2002.pdf
* **Rânduri în textul extras:** 51–59 · _rândurile PDF au fost unite; notele de modificare «(la ...)» omise_
* **Context dinainte:** «pactelor şi a celorlalte tratate la care România este parte, referitoare ⏎ la dreptul de a primi şi răspândi informaţii.»
* **Citat (început):** «ART. 4 Principalele obiective ale protecţiei informaţiilor clasificate sunt: a) protejarea informaţiilor clasificate împotriva acţiunilor de spionaj, compromitere sau acces neautorizat, alterării sau modificării conţinutului acestora, precum şi împotriva sabot…»
* **Context după:** «    ART. 5 ⏎     Măsurile ce decurg din aplicarea legii sunt destinate:»

### `[L182-11]` — Legea 182/2002, art. 11
* **Sursă:** Legea 182/2002, forma consolidată 13.03.2024 (SRI)
* **URL:** https://www.sri.ro/assets/files/legislatie/2024/Lege_182.2002.pdf
* **Rânduri în textul extras:** 146–150 · _rândurile PDF au fost unite; notele de modificare «(la ...)» omise_
* **Context dinainte:** «prioritate ori de câte ori apar indicii că menţinerea acesteia nu mai ⏎ este compatibilă cu interesele de securitate.»
* **Citat (început):** «ART. 11 Accesul în clădirile şi infrastructurile informatice în care se desfăşoară activităţi cu informaţii clasificate ori sunt păstrate sau stocate informaţii cu acest caracter este permis numai în cazuri autorizate.»
* **Context după:** «    ART. 12 ⏎     Standardele de protecţie a informaţiilor clasificate, încredinţate»

### `[L182-15]` — Legea 182/2002, art. 15 lit. a)-e)
* **Sursă:** Legea 182/2002, forma consolidată 13.03.2024 (SRI)
* **URL:** https://www.sri.ro/assets/files/legislatie/2024/Lege_182.2002.pdf
* **Rânduri în textul extras:** 165–229 · _rândurile PDF au fost unite; notele de modificare «(la ...)» omise_
* **Context dinainte:** «    SECŢIUNEA a 2-a ⏎     Definiţii»
* **Citat (început):** «ART. 15 În sensul prezentei legi, următorii termeni se definesc astfel: a) informaţii - orice documente, date, obiecte sau activităţi, indiferent de suport, forma, mod de exprimare sau de punere în circulaţie; b) informaţii clasificate - informaţiile, datele, …»
* **Context după:** «    CAP. II ⏎     Informaţii secrete de stat»

### `[L182-18]` — Legea 182/2002, art. 18
* **Sursă:** Legea 182/2002, forma consolidată 13.03.2024 (SRI)
* **URL:** https://www.sri.ro/assets/files/legislatie/2024/Lege_182.2002.pdf
* **Rânduri în textul extras:** 301–308 · _rândurile PDF au fost unite; notele de modificare «(la ...)» omise_
* **Context dinainte:** «ori înţelegeri internaţionale, statul român şi-a asumat obligaţia de ⏎ protecţie.»
* **Citat (început):** «ART. 18 (1) Informaţiile secrete de stat se clasifica pe niveluri de secretizare, în funcţie de importanta valorilor protejate. (2) Nivelurile de secretizare atribuite informaţiilor din clasa secrete de stat sunt: a) strict secret de importanta deosebita; b) s…»
* **Context după:** «    ART. 19 ⏎     Sunt împuterniciţi să atribuie unul dintre nivelurile de secretizare»

### `[L182-42]` — Legea 182/2002, art. 42
* **Sursă:** Legea 182/2002, forma consolidată 13.03.2024 (SRI)
* **URL:** https://www.sri.ro/assets/files/legislatie/2024/Lege_182.2002.pdf
* **Rânduri în textul extras:** 609–630 · _rândurile PDF au fost unite; notele de modificare «(la ...)» omise_
* **Context dinainte:** «conducătorului autorităţii sau instituţiei publice ori al agentului ⏎ economic.»
* **Citat (început):** «ART. 42 În termen de 60 de zile de la data publicării prezentei legi în Monitorul Oficial al României, Partea I, Guvernul va stabili prin hotărâre: a) clasificările informaţiilor secrete de stat şi normele privind măsurile minime de protecţie în cadrul fiecăre…»
* **Context după:** «    ART. 43 ⏎     Prezenta lege va intra în vigoare la 60 de zile de la data»

### `[I2-35]` — INFOSEC 2, art. 35
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 187–194
* **Context dinainte:** «SECȚIUNEA 2.10 ⏎ Interconectarea SIC»
* **Citat (început):** «Art. 35 (1) În vederea atingerii obiectivelor asumate, organizațiile au nevoie să își interconecteze propriile SIC cu SIC ale altor organizații, cu diferite comunități de interes, diferite nivele de clasificare și diferite standarde de securitate. (2) În scopu…»
* **Context după:** «Art. 36 ⏎ INFOSEC 3 stabilește cerințele de acreditare de securitate, iar directivele tehnice și de implementare stabilesc măsurile care trebuie implementate.»

### `[I2-37]` — INFOSEC 2, art. 37
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 197–204
* **Context dinainte:** «Art. 36 ⏎ INFOSEC 3 stabilește cerințele de acreditare de securitate, iar directivele tehnice și de implementare stabilesc măsurile care trebuie implementate.»
* **Citat (început):** «Art. 37 (1) Cerințele privind măsurile de protecție ce trebuie implementate în SIC care vehiculează informații clasificate și sunt conectate la internet sau la rețele similare din domeniul public trebuie să țină seama de riscurile de securitate excepționale pe…»
* **Context după:** «SECȚIUNEA 2.11 ⏎ Conectarea SIC care vehiculează informații clasificate la internet sau la alte rețele din domeniul public»

### `[I2-40]` — INFOSEC 2, art. 40
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 224–226
* **Context dinainte:** «SECȚIUNEA 2.12 ⏎ Securitatea aplicațiilor»
* **Citat (început):** «Art. 40 (1) Aspectele de securitate trebuie înglobate în ciclul de viață (proiectare, dezvoltare, implementare și întreținere) al componentelor software special dezvoltate pentru vehicularea de informații clasificate, avându-se în vedere obiectivele de securit…»
* **Context după:** «SECȚIUNEA 2.13 ⏎ Securitatea criptografică»

### `[I2-41]` — INFOSEC 2, art. 41
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 229–230
* **Context dinainte:** «SECȚIUNEA 2.13 ⏎ Securitatea criptografică»
* **Citat (început):** «Art. 41 Implementarea securității criptografice în SIC care vehiculează informații clasificate se realizează în conformitate cu prevederile reglementărilor naționale, NATO, UE sau specifice SIC, după caz, specifice domeniului.»
* **Context după:** «SECȚIUNEA 2.14 ⏎ Securitatea emisiilor»

### `[I2-43]` — INFOSEC 2, art. 43
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 237–247
* **Context dinainte:** «SECȚIUNEA 2.15 ⏎ Logurile de securitate»
* **Citat (început):** «Art. 43 (1) SIC care vehiculează informații clasificate sunt protejate de măsuri de securitate pentru detecția activităților malițioase și a defecțiunilor, prin colectarea, analiza și stocarea informațiilor referitoare la evenimente relevante din punctul de ve…»
* **Context după:** «SECȚIUNEA 2.16 ⏎ Configurația de securitate de bază»

### `[I2-44]` — INFOSEC 2, art. 44
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 250–252
* **Context dinainte:** «SECȚIUNEA 2.16 ⏎ Configurația de securitate de bază»
* **Citat (început):** «Art. 44 (1) Pentru SIC care vehiculează informații clasificate și componentele hardware și software critice sunt definite configurații de securitate de bază, care trebuie aplicate și păstrate la zi, prin procesele de management al configurației și de cel de co…»
* **Context după:** «SECȚIUNEA 2.17 ⏎ Apărarea împotriva software-ului malițios»

### `[I2-45]` — INFOSEC 2, art. 45
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 255–257
* **Context dinainte:** «SECȚIUNEA 2.17 ⏎ Apărarea împotriva software-ului malițios»
* **Citat (început):** «Art. 45 (1) Evoluția complexității software-ului malițios și capacitatea sa de a executa atacuri direcționale impun acordarea unei atenții sporite. (2) În SIC care vehiculează informații clasificate sunt utilizate soluții de detecție care să blocheze instalare…»
* **Context după:** «SECȚIUNEA 2.18 ⏎ Controlul accesului»

### `[I2-46]` — INFOSEC 2, art. 46
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 260–262
* **Context dinainte:** «SECȚIUNEA 2.18 ⏎ Controlul accesului»
* **Citat (început):** «Art. 46 (1) Controlul accesului reprezintă o primă linie de apărare, dat fiind că acesta permite identificarea, autentificarea, autorizarea și evidența oricărei entități (de exemplu: persoană, dispozitiv, serviciu) care solicită acces la SIC și la elementele a…»
* **Context după:** «Art. 47 ⏎ (1) În selectarea unui model de control al accesului și a măsurilor de securitate asociate acestuia, AOSIC trebuie să țină cont de următorii factori:»

### `[I2-49]` — INFOSEC 2, art. 49
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 279–281
* **Context dinainte:** «d) furnizarea de autorizări granulare, pe baza politicilor de acces; ⏎ e) auditul utilizatorilor și activităților din sistem.»
* **Citat (început):** «Art. 49 (1) Cerințele minime privind identificarea și autentificarea pe SIC care vehiculează informați clasificate sunt stabilite prin reglementările în domeniu emise de către ORNISS și, după caz, se vor avea în vedere rezultatele procesului de management al r…»
* **Context după:** «SECȚIUNEA 2.19 ⏎ Răspunsul la incidente»

### `[I2-50]` — INFOSEC 2, art. 50
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 284–286
* **Context dinainte:** «SECȚIUNEA 2.19 ⏎ Răspunsul la incidente»
* **Citat (început):** «Art. 50 (1) Un incident de securitate în SIC reprezintă orice anomalie detectată care a compromis sau are potențialul de a compromite sistemele de comunicații, sistemele informatice ori alte sisteme electronice sau informațiile stocate, procesate ori transmise…»
* **Context după:** «Art. 51 ⏎ Incidentele care vizează securitatea SIC se raportează la ORNISS.»

### `[I2-51]` — INFOSEC 2, art. 51
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 287–288
* **Context dinainte:** «otențialul de a compromite sistemele de comunicații, sistemele informatice ori alte sisteme electronice sau informațiile stocate, procesate ori transmise prin intermediul acestor sisteme. ⏎ (2) Pentru gestionarea incidentelor de securitate se desemnează personal specializat din punct de vedere tehnic.»
* **Citat (început):** «Art. 51 Incidentele care vizează securitatea SIC se raportează la ORNISS.»
* **Context după:** «Art. 52 ⏎ (1) În cazul în care survin incidente de securitate în SIC NATO care vehiculează informații clasificate, ORNISS raportează incidentele către Oficiul de Securitate al NATO (NOS) și către NATO Computer Incident Response Capability (NCIRC).»

### `[I2-36]` — INFOSEC 2, art. 36
* **Sursă:** ORNISS nr. 16/2014, INFOSEC 2 (MO 262/10.04.2014), anexa — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
* **Rânduri în textul extras:** 195–196
* **Context dinainte:** «c) arhitectura de securitate și măsurile de securitate pentru asigurarea respectării obiectivelor securității; ⏎ d) documentația de securitate, inclusiv planul de testare a securității și rezultatele aplicării acestui plan.»
* **Citat (început):** «Art. 36 INFOSEC 3 stabilește cerințele de acreditare de securitate, iar directivele tehnice și de implementare stabilesc măsurile care trebuie implementate.»
* **Context după:** «Art. 37 ⏎ (1) Cerințele privind măsurile de protecție ce trebuie implementate în SIC care vehiculează informații clasificate și sunt conectate la internet sau la rețele similare din domeniul public trebuie să țină seama de riscurile de securitate excepționale pe care aceste tipuri de rețele publice le»

### `[PRP-2]` — Ghid PrOpSec (DS 2), art. 2
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 38–39
* **Context dinainte:** «ui Național al Informațiilor Secrete de Stat (ORNISS), structurilor interne INFOSEC (SII) acreditate în cadrul autorităților desemnate de securitate (ADS) și autorităților operaționale ale sistemului informatic și de comunicații (AOSIC) care stochează, procesează sau transmit informații clasificate.»
* **Citat (început):** «Art. 2 Întocmirea PrOpSec este obligatorie pentru toate sistemele informatice și de comunicații (SIC) supuse procesului de acreditare de securitate, conform prevederilor Directivei privind managementul INFOSEC pentru sisteme informatice și de comunicații - INF…»
* **Context după:** «Art. 3 ⏎ (1) PrOpSec reprezintă descrierea precisă a implementării cerințelor de securitate definite anterior în documentațiile cu cerințele de securitate (DCS), a procedurilor operaționale care vor trebui urmate și a responsabilităților personalului, specifice SIC.»

### `[PRP-3]` — Ghid PrOpSec (DS 2), art. 3
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 40–43
* **Context dinainte:** «reditare de securitate, conform prevederilor Directivei privind managementul INFOSEC pentru sisteme informatice și de comunicații - INFOSEC 3, aprobată prin Ordinul directorului general al Oficiului Registrului Național al Informațiilor Secrete de Stat nr. 484/2003, denumită în continuare INFOSEC 3.»
* **Citat (început):** «Art. 3 (1) PrOpSec reprezintă descrierea precisă a implementării cerințelor de securitate definite anterior în documentațiile cu cerințele de securitate (DCS), a procedurilor operaționale care vor trebui urmate și a responsabilităților personalului, specifice …»
* **Context după:** «Art. 4 ⏎ Prezentul ghid stabilește structura și conținutul PrOpSec pentru următoarele categorii de personal și mod de utilizare:»

### `[PRP-5]` — Ghid PrOpSec (DS 2), art. 5
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 52–57
* **Context dinainte:** «CAPITOLUL II ⏎ Domeniu de aplicare»
* **Citat (început):** «Art. 5 (1) Potrivit prevederilor INFOSEC 3, SIC care urmează să stocheze, să proceseze sau să transmită informații naționale clasificate cu nivel de clasificare SECRET și superior sau echivalent trebuie supuse unui proces de acreditare de securitate. (2) Acred…»
* **Context după:** «CAPITOLUL III ⏎ Structura PrOpSec»

### `[PRP-16]` — Ghid PrOpSec (DS 2), art. 16
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 145–165
* **Context dinainte:** «CAPITOLUL V ⏎ Conținutul PrOpSec pentru utilizatorii dispozitivelor portabile de calcul și de comunicații în cadrul misiunilor oficiale»
* **Citat (început):** «Art. 16 (1) Dispozitivele portabile de calcul și comunicații includ laptopuri, agende electronice și palmtop cu capacitate de stocare, procesare și/sau transmitere (de exemplu: PDA, BlackBerry, tablete) și telefoane celulare/telefoane mobile GSM cu funcționali…»
* **Context după:** «CAPITOLUL VI ⏎ Conținutul PrOpSec pentru utilizarea dispozitivelor portabile de calcul și de comunicații de către vizitatori»

### `[PRP-19]` — Ghid PrOpSec (DS 2), art. 19
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 188–206
* **Context dinainte:** «Prezentul capitol descrie conținutul PrOpSec, incluzând, unde este cazul, informații mai detaliate. ⏎ Administrarea și organizarea securității»
* **Citat (început):** «Art. 19 (1) Cap. 1 "Administrarea și organizarea securității“ din cuprinsul PrOpSec conține o introducere de tipul celei prezentate mai jos: "Acest capitol, precum și capitolele următoare ale acestui document constituie Procedurile operaționale de securitate (…»
* **Context după:** «Securitatea fizică ⏎ Art. 20»

### `[PRP-12]` — Ghid PrOpSec (DS 2), art. 12
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 124–128
* **Context dinainte:** «g) responsabilitățile și procedurile privind reclasificarea/declasificarea/distrugerea și scoaterea din uz a documentelor. ⏎ Securitatea SIC»
* **Citat (început):** «Art. 12 Cap. 5 "Securitatea SIC“ din cuprinsul PrOpSec oferă detalii cu privire la metodele de utilizare și control al facilităților de protecție asigurate de componentele software, în special în ceea ce privește: a) conceptul de identificare (user-id) - proce…»
* **Context după:** «Securitatea calculatoarelor ⏎ Protecția împotriva software-ului malițios»

### `[PRP-13]` — Ghid PrOpSec (DS 2), art. 13
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 131–137
* **Context dinainte:** «Securitatea calculatoarelor ⏎ Protecția împotriva software-ului malițios»
* **Citat (început):** «Art. 13 (1) Secțiunea "Protecția împotriva software-ului malițios“ din cap. 5 "Securitatea SIC“ conține un sumar al tuturor mecanismelor și procedurilor de protecție împotriva software-ului malițios, relevante pentru SIC. (2) Sumarul menționat la alin. (1) inc…»
* **Context după:** «Planificarea măsurilor pentru situații de urgență și pentru continuarea activității ⏎ Art. 14»

### `[PRP-25]` — Ghid PrOpSec (DS 2), art. 25
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 286–308
* **Context dinainte:** «i) în cazul în care se asigură și protecția TEMPEST pentru SIC, acest lucru trebuie precizat în această secțiune și corelat cu prevederile din secțiunea "Securitatea emisiei“. ⏎ Securitatea software»
* **Citat (început):** «Art. 25 (1) Securitatea software se referă la caracteristicile de securitate asigurate de următoarele componente: a) firmware - instrucțiuni software, de obicei scrise de furnizorii de hardware, care simulează hardware-ul și pot fi înlocuite prin implementarea…»
* **Context după:** «Protecția antivirus a calculatoarelor ⏎ Art. 26»

### `[PRP-26]` — Ghid PrOpSec (DS 2), art. 26
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 310–317
* **Context dinainte:** «i) controlul copiilor în format hârtie. ⏎ Protecția antivirus a calculatoarelor»
* **Citat (început):** «Art. 26 (1) Secțiunea "Protecția antivirus a calculatoarelor“ conține un sumar al tuturor procedurilor și mecanismelor de protecție împotriva software-ului malițios, atât manuale, cât și automate, și responsabilitățile individuale relevante pentru SIC. (2) Sec…»
* **Context după:** «Managementul și auditul automat al securității ⏎ Art. 27»

### `[PRP-27]` — Ghid PrOpSec (DS 2), art. 27
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 319–329
* **Context dinainte:** «AAS, folosindu-se formularul din Directiva privind managementul INFOSEC pentru sisteme informatice și de comunicații - INFOSEC 3, aprobată prin Ordinul directorului general al Oficiului Registrului Național al Informațiilor Secrete de Stat nr. 484/2003. ⏎ Managementul și auditul automat al securității»
* **Citat (început):** «Art. 27 (1) Secțiunea "Managementul și auditul automat al securității“ conține un sumar al tuturor măsurilor și procedurilor automate de management al securității, al procedurilor de audit, atât cele manuale, cât și cele asigurate de sistem, alocarea responsab…»
* **Context după:** «Securitatea criptografică ⏎ Art. 28»

### `[PRP-32]` — Ghid PrOpSec (DS 2), art. 32
* **Sursă:** OG ORNISS nr. 18/2014 (MO 242/04.04.2014), Ghid PrOpSec – DS 2 — mirror neoficial legeaz.net
* **URL:** https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid
* **Rânduri în textul extras:** 371–385
* **Context dinainte:** «(4) Capitolul prevăzut la alin. (1) furnizează, de asemenea, un sumar al modului de exersare a procedurilor de urgență și frecvența cu care se fac aceste exerciții sau face referiri la documente interne care conțin aceste prevederi. ⏎ Managementul configurației»
* **Citat (început):** «Art. 32 (1) Managementul configurației SIC constă în identificarea, controlul, păstrarea evidenței, diseminarea și auditul tuturor modificărilor efectuate în timpul etapelor de proiectare, dezvoltare, exploatare, întreținere și îmbunătățire a ciclului de viață…»
* **Context după:** «CAPITOLUL VIII ⏎ Proceduri operaționale asociate»

### `[NIS2-21]` — Directiva 2022/2555, art. 21 alin. (1)-(2)
* **Sursă:** Directiva (UE) 2022/2555, art. 21 alin. (1)-(2), versiunea în limba română (JO L 333, 27.12.2022)
* **URL:** https://publications.europa.eu/resource/celex/32022L2555 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 849–873
* **Context dinainte:** «ifica riscurile și a evalua practicile de gestionare a riscurilor în materie de securitate cibernetică și impactul acestora asupra serviciilor pe care le furnizează entitatea, și încurajează entitățile esențiale și entitățile importante să ofere o formare similară tuturor angajaților în mod regulat.»
* **Citat (început):** «Articolul 21 Măsuri de gestionare a riscurilor în materie de securitate cibernetică (1) Statele membre se asigură că entitățile esențiale și entitățile importante iau măsuri tehnice, operaționale și organizatorice adecvate și proporționale pentru a gestiona ri…»
* **Context după:** « (3) Statele membre se asigură că, atunci când analizează care măsuri menționate la alineatul (2) litera (d) de la prezentul articol sunt adecvate, entitățile iau în considerare vulnerabilitățile specifice fiecărui prestator și furnizor direct de servicii, precum și calitatea generală a produselor ș»

### `[NIS2-21-3]` — Directiva 2022/2555, art. 21 alin. (3)
* **Sursă:** Directiva (UE) 2022/2555 (RO)
* **URL:** https://publications.europa.eu/resource/celex/32022L2555
* **Rânduri în textul extras:** 874–874
* **Context dinainte:** « (j) ⏎  utilizarea de soluții de autentificare multifactor sau de autentificare continuă, de comunicații securizate voce, video și text și de sisteme securizate de comunicații de urgență în cadrul entității, după caz.»
* **Citat (început):** «(3) Statele membre se asigură că, atunci când analizează care măsuri menționate la alineatul (2) litera (d) de la prezentul articol sunt adecvate, entitățile iau în considerare vulnerabilitățile specifice fiecărui prestator și furnizor direct de servicii, prec…»
* **Context după:** « (4) Statele membre se asigură că o entitate care constată că nu respectă măsurile prevăzute la alineatul (2) ia, fără întârzieri nejustificate, toate măsurile corective necesare, adecvate și proporționale. ⏎  (5) Până la 17 octombrie 2024, Comisia adoptă acte de punere în aplicare de stabilire a ceri»

### `[R2690-3.2]` — Reg. 2024/2690, anexa, pct. 3.2
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 372–410
* **Context dinainte:** « 3.1.3. ⏎  Rolurile, responsabilitățile și procedurile stabilite în cadrul politicii sunt testate și revizuite și, dacă este adecvat, actualizate la intervale planificate și în urma unor incidente semnificative sau modificări semnificative ale operațiunilor sau ale riscurilor.»
* **Citat (început):** «3.2. Monitorizare și jurnalizare 3.2.1. Entitățile relevante stabilesc proceduri și utilizează instrumente pentru monitorizarea și jurnalizarea activităților în rețelele lor și în sistemele lor informatice pentru a detecta evenimentele care ar putea fi conside…»
* **Context după:** « 3.3. Raportarea evenimentelor ⏎  3.3.1.»

### `[R2690-6.1]` — Reg. 2024/2690, anexa, pct. 6.1
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 590–608
* **Context dinainte:** «or TIC, a serviciilor TIC și a proceselor TIC furnizate entităților relevante de furnizorul sau de prestatorul de servicii direct. ⏎  6. Securitatea în achiziționarea, dezvoltarea și întreținerea rețelelor și a sistemelor informatice [articolul 21 alineatul (2) litera (e) din Directiva (UE) 2022/2555]»
* **Citat (început):** «6.1. Securitatea în achiziționarea de servicii TIC sau de produse TIC 6.1.1. În sensul articolului 21 alineatul (2) litera (e) din Directiva (UE) 2022/2555, entitățile relevante stabilesc și pun în aplicare, pe baza evaluării riscurilor efectuată în temeiul pu…»
* **Context după:** « 6.2. Ciclul de viață al dezvoltării securizate ⏎  6.2.1.»

### `[R2690-6.2]` — Reg. 2024/2690, anexa, pct. 6.2
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 609–629
* **Context dinainte:** « 6.1.3. ⏎  Entitățile relevante revizuiesc și, dacă este adecvat, actualizează procesele la intervale planificate și atunci când apar incidente semnificative.»
* **Citat (început):** «6.2. Ciclul de viață al dezvoltării securizate 6.2.1. Înainte de a dezvolta o rețea și un sistem informatic, inclusiv un software, entitățile relevante stabilesc norme pentru dezvoltarea securizată de rețele și sisteme informatice și le aplică atunci când dezv…»
* **Context după:** « 6.3. Gestionarea configurației ⏎  6.3.1.»

### `[R2690-6.3]` — Reg. 2024/2690, anexa, pct. 6.3
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 630–640
* **Context dinainte:** « 6.2.4. ⏎  Entitățile relevante își revizuiesc și, dacă este necesar, își actualizează normele de dezvoltare securizată la intervale planificate.»
* **Citat (început):** «6.3. Gestionarea configurației 6.3.1. Entitățile relevante iau măsurile adecvate pentru a stabili, a documenta, a pune în aplicare și a monitoriza configurațiile, inclusiv configurațiile de securitate ale hardware-ului, software-ului, serviciilor și rețelelor.…»
* **Context după:** « 6.4. Gestionarea modificărilor, reparații și întreținere ⏎  6.4.1.»

### `[R2690-6.4]` — Reg. 2024/2690, anexa, pct. 6.4
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 641–649
* **Context dinainte:** « 6.3.3. ⏎  Entitățile relevante revizuiesc și, dacă este adecvat, actualizează configurațiile la intervale planificate și atunci când apar incidente semnificative sau modificări semnificative ale operațiunilor sau ale riscurilor.»
* **Citat (început):** «6.4. Gestionarea modificărilor, reparații și întreținere 6.4.1. Entitățile relevante aplică proceduri de gestionare a modificărilor pentru a controla modificările aduse rețelelor și sistemelor informatice. Dacă acest lucru este aplicabil, procedurile trebuie s…»
* **Context după:** « 6.5. Teste de securitate ⏎  6.5.1.»

### `[R2690-6.6]` — Reg. 2024/2690, anexa, pct. 6.6
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 665–677
* **Context dinainte:** « 6.5.3. ⏎  Entitățile relevante își revizuiesc și, dacă este adecvat, își actualizează politicile privind testele de securitate la intervale planificate.»
* **Citat (început):** «6.6. Gestionarea corecțiilor de securitate 6.6.1. Entitățile relevante specifică și aplică proceduri coerente cu procedurile de gestionare a modificărilor menționate la punctul 6.4.1, precum și cu gestionarea vulnerabilităților, gestionarea riscurilor și cu al…»
* **Context după:** « 6.7. Securitatea rețelelor ⏎  6.7.1.»

### `[R2690-6.7]` — Reg. 2024/2690, anexa, pct. 6.7
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 678–708
* **Context dinainte:** « entitățile relevante pot alege să nu aplice corecții de securitate atunci când dezavantajele aplicării corecțiilor de securitate sunt mai mari decât beneficiile în materie de securitate cibernetică. Entitățile relevante documentează și justifică în mod corespunzător motivele unei astfel de decizii.»
* **Citat (început):** «6.7. Securitatea rețelelor 6.7.1. Entitățile relevante iau măsurile adecvate pentru a-și proteja rețelele și sistemele informatice împotriva amenințărilor cibernetice. 6.7.2. În sensul punctului 6.7.1, entitățile relevante: (a) documentează arhitectura rețelei…»
* **Context după:** « 6.8. Segmentarea rețelei ⏎  6.8.1.»

### `[R2690-6.9]` — Reg. 2024/2690, anexa, pct. 6.9
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 732–736
* **Context dinainte:** « 6.8.3. ⏎  Entitățile relevante revizuiesc și, dacă este adecvat, actualizează segmentarea rețelei la intervale planificate și atunci când apar incidente semnificative sau modificări semnificative ale operațiunilor sau ale riscurilor.»
* **Citat (început):** «6.9. Protecția împotriva software-ului rău-intenționat și neautorizat 6.9.1. Entitățile relevante își protejează rețelele și sistemele informatice împotriva software-ului rău-intenționat și neautorizat. 6.9.2. În acest scop, entitățile relevante pun în aplicar…»
* **Context după:** « 6.10. Gestionarea și divulgarea vulnerabilităților ⏎  6.10.1.»

### `[R2690-6.10]` — Reg. 2024/2690, anexa, pct. 6.10
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 737–755
* **Context dinainte:** «u neautorizate. Entitățile relevante se asigură, după caz, că rețelele și sistemele lor informatice sunt echipate cu software de detectare și răspuns, care este actualizat periodic în conformitate cu evaluarea riscurilor efectuată în temeiul punctului 2.1 și cu acordurile contractuale cu furnizorii.»
* **Citat (început):** «6.10. Gestionarea și divulgarea vulnerabilităților 6.10.1. Entitățile relevante obțin informații cu privire la vulnerabilitățile tehnice din rețelele și sistemele lor informatice, evaluează expunerea lor la astfel de vulnerabilități și iau măsurile adecvate pe…»
* **Context după:** « 7. Politici și proceduri pentru a evalua eficacitatea măsurilor de gestionare a riscurilor în materie de securitate cibernetică [articolul 21 alineatul (2) litera (f) din Directiva (UE) 2022/2555] ⏎  7.1.»

### `[R2690-11.2]` — Reg. 2024/2690, anexa, pct. 11.2
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 894–912
* **Context dinainte:** « 11.1.3. ⏎  Entitățile relevante revizuiesc și, dacă este adecvat, actualizează politicile la intervale planificate și atunci când apar incidente semnificative sau modificări semnificative ale operațiunilor sau ale riscurilor.»
* **Citat (început):** «11.2. Gestionarea drepturilor de acces 11.2.1. Entitățile relevante acordă, modifică, elimină și documentează drepturile de acces la rețele și la sistemele informatice în conformitate cu politica de control al accesului menționată la punctul 11.1. 11.2.2. Enti…»
* **Context după:** « 11.3. Conturile privilegiate și conturile de administrare a sistemului ⏎  11.3.1.»

### `[R2690-11.3]` — Reg. 2024/2690, anexa, pct. 11.3
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 913–927
* **Context dinainte:** « 11.2.3. ⏎  Entitățile relevante revizuiesc drepturile de acces la intervale planificate și le modifică pe baza schimbărilor organizaționale. Entitățile relevante documentează rezultatele revizuirii, inclusiv modificările necesare ale drepturilor de acces.»
* **Citat (început):** «11.3. Conturile privilegiate și conturile de administrare a sistemului 11.3.1. Entitățile relevante mențin politici de gestionare a conturilor privilegiate și a conturilor de administrare a sistemului ca parte a politicii de control al accesului menționate la …»
* **Context după:** « 11.4. Sisteme de administrare ⏎  11.4.1.»

### `[R2690-11.6]` — Reg. 2024/2690, anexa, pct. 11.6
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 956–976
* **Context dinainte:** « 11.5.4. ⏎  Entitățile relevante revizuiesc periodic identitățile pentru rețele și sisteme informatice și ale utilizatorilor acestora și, dacă nu mai sunt necesare, le dezactivează fără întârziere.»
* **Citat (început):** «11.6. Autentificare 11.6.1. Entitățile relevante pun în aplicare proceduri și tehnologii de autentificare securizată bazate pe restricții de acces și pe politica privind controlul accesului. 11.6.2. În acest scop, entitățile relevante: (a) se asigură că putere…»
* **Context după:** « 11.7. Autentificarea multifactor ⏎  11.7.1.»

### `[R2690-11.7]` — Reg. 2024/2690, anexa, pct. 11.7
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 977–981
* **Context dinainte:** « 11.6.4. ⏎  Entitățile relevante revizuiesc procedurile și tehnologiile de autentificare la intervale planificate.»
* **Citat (început):** «11.7. Autentificarea multifactor 11.7.1. Entitățile relevante se asigură că utilizatorii sunt autentificați prin factori de autentificare multipli sau prin mecanisme de autentificare continuă pentru accesarea rețelelor și a sistemelor informatice ale entitățil…»
* **Context după:** « 12. Gestionarea activelor [articolul 21 alineatul (2) litera (i) din Directiva (UE) 2022/2555] ⏎  12.1. Clasificarea activelor»

### `[R2690-12.3]` — Reg. 2024/2690, anexa, pct. 12.3
* **Sursă:** Regulamentul de punere în aplicare (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690 (Accept: application/xhtml+xml, Accept-Language: ron)
* **Rânduri în textul extras:** 1009–1023
* **Context dinainte:** « 12.2.3. ⏎  Entitățile relevante revizuiesc și, dacă este adecvat, actualizează politica la intervale planificate și atunci când apar incidente semnificative sau modificări semnificative ale operațiunilor sau ale riscurilor.»
* **Citat (început):** «12.3. Politica privind suporturile amovibile 12.3.1. Entitățile relevante stabilesc, implementează și aplică o politică de gestionare a suporturilor de stocare amovibile și o comunică angajaților lor și părților terțe care gestionează suporturi de stocare amov…»
* **Context după:** « 12.4. Inventarul activelor ⏎  12.4.1.»

### `[R2690-9]` — Reg. 2024/2690, anexa, pct. 9
* **Sursă:** Regulamentul (UE) 2024/2690, anexa (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690
* **Rânduri în textul extras:** 806–842
* **Context dinainte:** « 8.2.5. ⏎  Programul se actualizează și se desfășoară periodic, ținând seama de politicile și normele aplicabile, de rolurile și responsabilitățile atribuite, precum și de amenințările cibernetice cunoscute și de evoluțiile tehnologice.»
* **Citat (început):** «9. Criptografie [articolul 21 alineatul (2) litera (h) din Directiva (UE) 2022/2555] 9.1. În sensul articolului 21 alineatul (2) litera (h) din Directiva (UE) 2022/2555, entitățile relevante stabilesc, implementează și aplică o politică și proceduri legate de …»
* **Context după:** « 10. Securitatea resurselor umane [articolul 21 alineatul (2) litera (i) din Directiva (UE) 2022/2555] ⏎  10.1. Securitatea resurselor umane»

### `[R2690-A1]` — Reg. 2024/2690, art. 1
* **Sursă:** Regulamentul (UE) 2024/2690 (RO)
* **URL:** https://publications.europa.eu/resource/celex/32024R2690
* **Rânduri în textul extras:** 103–105
* **Context dinainte:** « Măsurile prevăzute în prezentul regulament sunt conforme cu avizul comitetului instituit în conformitate cu articolul 39 din Directiva (UE) 2022/2555, ⏎  ADOPTĂ PREZENTUL REGULAMENT:»
* **Citat (început):** «Articolul 1 Obiect În ceea ce privește furnizorii de servicii DNS, registrele de nume TLD, furnizorii de servicii de cloud computing, furnizorii de servicii de centre de date, furnizorii de rețele de furnizare de conținut, furnizorii de servicii gestionate, fu…»
* **Context după:** « Articolul 2 ⏎  Cerințele tehnice și metodologice»

### `[OUG-2]` — OUG 155/2024, art. 2 alin. (1) lit. a)
* **Sursă:** OUG 155/2024 (MO 1332/31.12.2024), text original; aprobată prin Legea 124/2025 (neverificat dacă s-a modificat textul)
* **URL:** https://upt.ro/img/files/legislatie/2024/OUG_155_2024.pdf
* **Rânduri în textul extras:** 82–143 · _liniile PDF unite; structura pe alineate refăcută_
* **Context dinainte:** «»
* **Citat (început):** «Art. 2. — (1) Scopul prezentei ordonanțe de urgență îl constituie: a) stabilirea măsurilor de gestionare a riscurilor de securitate cibernetică pentru spațiul cibernetic național civil și a obligațiilor de raportare a incidentelor pentru entitățile esențiale ș…»
* **Context după:** «SECȚIUNEA a 2-a ⏎ Principii și definiții»

### `[OUG-11]` — OUG 155/2024, art. 11
* **Sursă:** OUG 155/2024 (MO 1332/31.12.2024), text original; aprobată prin Legea 124/2025 (neverificat dacă s-a modificat textul)
* **URL:** https://upt.ro/img/files/legislatie/2024/OUG_155_2024.pdf
* **Rânduri în textul extras:** 592–655 · _liniile PDF unite; structura pe alineate refăcută_
* **Context dinainte:** «»
* **Citat (început):** «Art. 11. — (1) Entitățile esențiale și entitățile importante iau măsuri tehnice, operaționale și organizatorice proporționale și adecvate pentru a identifica, evalua și gestiona riscurile aferente securității rețelelor și a sistemelor informatice pe care acest…»
* **Context după:** «Art. 12. — (1) Directorul DNSC emite un ordin privind ⏎ măsurile de gestionare a riscurilor prevăzute la art. 11 alin. (1) în»

### `[OUG-13]` — OUG 155/2024, art. 13
* **Sursă:** OUG 155/2024 (MO 1332/31.12.2024), text original; aprobată prin Legea 124/2025 (neverificat dacă s-a modificat textul)
* **URL:** https://upt.ro/img/files/legislatie/2024/OUG_155_2024.pdf
* **Rânduri în textul extras:** 685–711 · _liniile PDF unite; structura pe alineate refăcută_
* **Context dinainte:** «»
* **Citat (început):** «Art. 13. — Măsurile prevăzute la art. 11 alin. (1) cuprind cel puțin următoarele: a) politicile și procedurile referitoare la analiza riscurilor și la securitatea sistemelor informatice și revizuirea periodică a acestora; b) politicile și procedurile de evalua…»
* **Context după:** «Art. 14. — (1) Organele de conducere ale entităților esențiale ⏎ și ale entităților importante aprobă măsurile de gestionare a»

### `[OUG-63]` — OUG 155/2024, art. 63
* **Sursă:** OUG 155/2024 (MO 1332/31.12.2024), text original; aprobată prin Legea 124/2025 (neverificat dacă s-a modificat textul)
* **URL:** https://upt.ro/img/files/legislatie/2024/OUG_155_2024.pdf
* **Rânduri în textul extras:** 3057–3064 · _liniile PDF unite; structura pe alineate refăcută_
* **Context dinainte:** «»
* **Citat (început):** «Art. 63. — DNSC informează instituțiile cu atribuții de coordonare a activității și control în domeniul protecției informațiilor clasificate astfel cum sunt acestea stabilite prin Legea nr. 182/2002 privind protecția informațiilor clasificate, cu modificările …»
* **Context după:** «CAPITOLUL X ⏎ Dispoziții tranzitorii și finale»
