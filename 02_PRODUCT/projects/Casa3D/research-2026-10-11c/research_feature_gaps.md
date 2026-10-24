---
id: "6b355c98-02e8-4fa6-ba45-7028845ff836"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, features, forums, gaps]
created: "2026-10-11"
updated: "2026-10-11"
provenance:
  source_type: web
  source_ref: "Web and forum research on interior designer software needs (Reddit, Capterra, G2, vendor sites, Romanian sources), 2026-10-11"
  source_date: "2026-10-11"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
---
# Casa3D: ce mai lipsește și ce cer designerii (cercetare web, 2026-10-11)

## 0. Limite ale cercetării (citiți primele)

- **Reddit nu a putut fi citit.** Instrumentul de căutare nu a returnat niciun fir din r/InteriorDesign, r/interiordesigners, r/Homeowners, r/HomeImprovement sau r/kitchenremodel, iar WebFetch pe reddit.com este blocat. Orice afirmație „de pe Reddit” este deci UNVERIFIED și lipsește aici. Singura mențiune indirectă: blogul DesignFiles spune că utilizatorii Reddit văd Spoak „ca unealtă DIY, nu platformă de business” (UNVERIFIED, sursă concurentă): https://blog.designfiles.co/e-design-software/
- **Capterra a returnat 403** la citirea directă; citatele Capterra provin din rezumatele motorului de căutare, nu din textul paginii. Pagini: https://www.capterra.com/p/199689/Houzz-Pro/reviews/ , https://capterra.com/p/248518/Programa/reviews/ , https://www.capterra.com/p/155585/Mydoma-Studio/reviews/?page=3
- G2 și Trustpilot nu au fost accesate. Forumuri românești (softpedia, desigur.ro, arhitectura-1906.ro) și grupuri Facebook: **nu s-a găsit nicio discuție relevantă**; căutarea a dat doar articole generale. Toate afirmațiile despre „cum lucrează designerii români” sunt contextuale, nu citate din practicieni.
- Multe surse sunt bloguri de vendori (DesignFiles, Agiled, Programa, RaftLabs): marcate „sursă concurentă”.

## 1. Ce are deja Casa3D (nu se propune din nou)

Din `todo-claude-casa3d.md`, `app/README.md` și `app/core/`: plan 2D/3D multi-nivel, catalog IKEA/Dedeman/JYSK cu prețuri, BOQ cu pierderi și manoperă, buget cu TVA, opțiuni A/B/C pe cameră, iluminat (lux, straturi), randări Blender, link client cu feedback și alegeri, aprobări produse și change orders cu preț, comenzi pe furnizor cu livrare și date „decide-by”, vizite pe șantier cu poze și snag list, pachet de predare, desene (plan, plafon, pardoseală, elevații, DXF, PDF, cartuș), mood board, pontaj, sarcini, profil studio (CUI/IBAN), coș 30 zile, export ZIP, module bucătărie, listă de tăiere dressing, verificări NKBA, trasare gresie, releveu (`survey.ts`), GLB export. Fișiere relevante: `procurement.ts`, `change-orders.ts`, `client-progress.ts`, `timesheet.ts`, `studio-profile.ts`, `trade-checks.ts`, `survey.ts`, `underlay.ts`.

## 2. Ce au și ce li se reproșează concurenților

| Produs | Funcții-cheie | Laude / plângeri (cu sursă) |
|---|---|---|
| Programa | Achiziții (comandă, expediere, livrare, instalare), oferte, facturi legate de etape, tablou de bord client cu aprobări. Sursă: https://programa.design/blog/interior-design-project-management-software (vendor) | Reviewers Capterra: facturarea merge doar cu Stripe, „purchase status can't be shared with clients”, probleme multi-valută la clienți europeni, schimbări care nu se salvează, pe iPad/mobil „buttons hidden”, bibliotecă de produse „finicky”, vor sincronizare calendar Google și CRM mai dezvoltat. Sursă: rezumat căutare Capterra https://capterra.com/p/248518/Programa/reviews/ . Preț $79-129/lună, 2-3 săptămâni de onboarding (sursă vendor, https://programa.design/blog/interior-design-project-management-software) |
| Houzz Pro | CRM, propuneri, portal client, planificator 3D, urmărire plăți, lead-uri. https://programa.design/best-interior-design-software-guide | Interfață „frustrating, sluggish”, anulare dificilă, auto-reînnoire pe un an, lead-uri care nu răspund (rezumat Capterra: https://www.capterra.com/p/199689/Houzz-Pro/reviews/ ). „It feels like Houzz Pro is more interested in selling items directly to designers' clients” (https://www.spacesbydee.com/the-best-and-worst-interior-design-software/ ) |
| Mydoma Studio (fuzionat cu Studio Designer în 2024, vendor Programa) | Comenzi de achiziție, bază de prețuri trade, contabilitate, șabloane de prezentare. | Scor 4,2/5 din 80 recenzii; ușurință 3,9/5. „a little clunky”, greu de găsit lucruri; „difficult tracking actual profit when it comes to furniture discount and markup” (https://www.capterra.ca/reviews/155585/mydoma-studio ). „glitchy and difficult to use” (https://www.spacesbydee.com/the-best-and-worst-interior-design-software/ ) |
| Studio Designer | Contabilitate și raportare pentru firme mari. | Capterra ~3,0 (sursă concurentă); „interface doesn't work well for non-accounting designers”: https://blog.designfiles.co/houzz-pro-vs-studio-designer/ |
| DesignFiles | Mood board, randări 3D, facturare. | „The 3D rendering can lag at times”; managementul de proiect slab (timp, programare): https://www.spacesbydee.com/the-best-and-worst-interior-design-software/ , https://blog.designfiles.co/e-design-software/ |
| IvyMark (fost Ivy) | Sourcing, propuneri, mood board, timp și cheltuieli, QuickBooks. https://www.cbinsights.com/company/matterbase/alternatives-competitors (informație slabă) | „designers aren't happy about Houzz Pro taking away Ivy” (spacesbydee, link mai sus). Recenzii: nu s-au găsit. |
| Material Bank, Gather | Mostre multi-brand într-o cutie peste noapte; Gather = specificații FF&E. https://help.materialbank.com/en/articles/14489450-how-material-bank-works | Fără recenzii găsite. Relevanță pentru România: mică (UNVERIFIED că operează acolo). |

**Tipare repetate:** (a) interfață grea și curbă de învățare lungă; (b) profitul real (adaos + discount trade) greu de văzut; (c) facturarea legată de un singur procesator; (d) mobil slab; (e) tool sprawl: „Houzz Pro plus QuickBooks plus DesignFiles plus Dropbox plus DocuSign plus Calendly” (sursă vendor: https://agiled.app/blog/best-all-in-one-software-for-interior-designers ).

## 3. Fluxul de lucru: unde doare

**Achiziții, adaos, avansuri.**
- Designerii cumpără la preț trade și facturează cu adaos; „trade pricing 30-40% below MSRP”, 50-200 articole de la zeci de furnizori; adaos tipic 30-35% (medie citată 35%), unii 50-60%: https://blog.designfiles.co/how-should-i-markup-products/ , https://www.raftlabs.com/blog/how-to-build-interior-design-studio-management-software
- Costuri ascunse (transport, depozitare, restituiri, comenzi amânate) scad câștigul; recomandare de a dezvălui adaosul în contract (aceeași sursă DesignFiles, jurisdicție SUA; pentru România UNVERIFIED).
- Avans înainte de comandă, articole la comandă nereturnabile, cine suportă transportul deteriorat: https://4dbiz.com/interior-design-purchasing/ (vendor).
- Urmărirea comenzilor „ordered / shipped / back-ordered” e un gol des citat (RaftLabs, link mai sus).

**Aprobări și change orders.**
- „The FF&E approval workflow is where most design projects lose weeks” (sursă vendor, promoțional): https://www.schedulingkit.com/customer-portal/interior-designers
- Business of Home (9 designeri): declanșatorul tipic al unui change order e „a back-ordered fabric discovered after a design was already confirmed”: https://businessofhome.com/articles/how-does-your-firm-deal-with-change-orders
- Sfat: change order scris cu impact pe cost și termen, confirmat în aceeași zi: https://blog.designfiles.co/interior-design-approvals/ . **Casa3D acoperă deja** change orders cu preț legat de versiunea văzută.
- Timpii de livrare, nu designul, deraiază proiectele (șablon Notion, opinie): https://grizzled-cinnamon-c27.notion.site/Interior-Designer-Studio-Hub-3a8f7ab2ba05818a888ae3f5c0fdf676

**Portal client.** Reviewerii Programa laudă aprobările ușoare pentru materiale; Mydoma „getting approval from clients to order is very easy”, dar „difficult to have the client see what they posted”: https://www.capterra.com/p/155585/Mydoma-Studio/reviews/?page=3 . Materio leagă selecțiile de planul de etaj (mai puțin scroll): https://capterra.com/p/238666/Materio/reviews/ . Idee pentru Casa3D: aprobări ancorate pe plan/3D.

**Programare cu meșteri.** Programa: lipsă sincronizare calendar (Capterra, de mai sus); DesignFiles: lipsă programare. Nu s-au găsit surse despre Gantt/dependențe între meserii la designeri de interior (UNVERIFIED).

**Măsurare cu telefonul / LiDAR.**
- Eroare tipică LiDAR ±2%, problemă dacă >5%: https://help.albiware.com/floor-plans-troubleshooting-frequently-asked-questions
- Un utilizator SketchUp: serviciul scan-to-CAD Canvas „introduced too many errors”, a folosit scanările doar ca referință: https://forums.sketchup.com/t/iphone-lidar-scanning-app/196087
- Aplicațiile exportă DXF (Lagarsoft) sau cer abonament pentru export (RoomScan): https://apps.apple.com/us/app/lagarsoft-lidar-scanner/id6779339424 , https://apps.apple.com/us/app/-/id673673795 (liste de magazin, vendor).
- Concluzie: import DXF/plan din aplicații de scanare + câmp „toleranță măsurătoare” are sens; nu un scanner propriu.

**AR/VR.** Studiu SAGE 2024: vizualizarea și prezentările către client sunt principalele utilizări; bariere: adoptarea și costul echipamentului/instruirii: https://journals.sagepub.com/doi/xml/10.1177/14780771241254632 . Studiu mic (8 clienți): VR mai util la comercial decât la rezidențial: https://mjaf.journals.ekb.eg/article_245540.html?lang=en . Nu există sondaj de adoptare 2025-26 (UNVERIFIED orice procent). Casa3D are deja GLB export și turul 3D; un buton „Vezi în camera ta” (WebXR/Quick Look) ar fi un strat subțire.

**Sustenabilitate.** Nu s-a găsit nicio dovadă (nicio sursă) că designerii români sau mici cer date de carbon/EPD în unelte. Prioritate mică, UNVERIFIED.

**Accesibilitate.** Doar liste de verificare (aging in place): chestionar pe cameră, 75% din lucrările unui remodelator au componentă de accesibilitate (https://www.jlconline.com/remodeling/problem-solver-comprehensive-universal-design-checklist , anecdotă SUA). Nicio unealtă care verifică automat gabarite/cercul de rotire (aceeași căutare). Casa3D are `trade-checks.ts` (NKBA); extindere cu reguli de gabarit 150 cm/prag/lățimi ușă = cerere de nișă, valoare mică-medie.

**Idei cu AI (marcate AI; Casa3D nu apelează modele reale în produs).** [AI] rezumat automat al feedback-ului clientului; [AI] propuneri de produse; [AI] descrieri pentru oferte. Pot fi înlocuite cu reguli deterministe sau șabloane; nicio sursă găsită care să arate că designerii le cer ca prioritate. UNVERIFIED.

## 4. Context românesc

**e-Factura.** RO e-Factura este obligatorie B2B (din 1 iulie 2024), B2C (din 1 ianuarie 2025; amenzi B2C din 1 iulie 2025 conform aceluiași articol, date contradictorii în sursă) și B2G; termen de transmitere 5 zile lucrătoare de la emitere; persoanele fizice identificate prin CNP nu sunt obligate să descarce, factura B2C către cumpărător fără cod fiscal poartă cod din 13 zerouri; amenzi: 15% din valoare pentru netransmitere B2B, 1.000-2.500 lei pentru alte persoane juridice la transmitere tardivă. Legea 88/2026 (MO 459/29.05.2026, în vigoare 1.06.2026) modifică OUG 120/2021: https://www.capital.ro/anaf-actualizeaza-regulile-pentru-e-factura-firmele-trebuie-sa-verifice-noile-obligatii-si-sanctiunile-aplicabile.html . Alte surse: https://www.fiscalitatea.ro/e-factura-obligatorie-din-1-ianuarie-2025-pentru-relatia-b2c-ce-trebuie-sa-stie-firmele-23870/ . Verificați textul oficial ANAF înainte de implementare (UNVERIFIED juridic).
**Integrări.** SmartBill și FGO sunt descrise ca integrate cu SPV/e-Factura (FGO: transmitere automată, 15 lei + TVA/lună); CRM-uri precum Zarina se conectează prin cheie API și trimit proforma, cu un singur partener de facturare activ: https://taxdome.com/ro-ro/blog/top-programe-de-facturare-2026-comparatii-e-factura-si-integrarea-cu-contabilitatea , https://www.zarinacrm.ro/crm-integrat-cu-smartbill/ (comparatoare/vendori; documentația API oficială nu a fost citită, UNVERIFIED). Concluzie: Casa3D nu trebuie să devină program de facturare; trebuie să exporte/împingă proforma și datele (client, linii, TVA, avans) spre SmartBill/FGO/Oblio.
**GDPR.** Fotografiile cu persoane identificabile sunt date personale; datele clientului (nume, adresă, facturare) cer informare la colectare; amenzi exemplu: >5.000 EUR pentru publicarea datelor pe Facebook, 2.000 EUR pentru trimiterea datelor fiscale unui terț: https://www.revistabiz.ro/gdpr-ul-si-fotografiile/ , https://startupcafe.ro/gdpr-firma-din-romania-amendata-pentru-un-document-fiscal-cu-date-personale-80014 . Nu există ghid ANSPDCP specific pentru design interior (căutare fără rezultat); recomandare practică (nu juridică): clauză de prelucrare + consimțământ separat pentru portofoliu, export/ștergere date client, jurnal acces pentru linkul client. Casa3D are coș și export ZIP (parțial).
**Tarife.** Estimări foarte variabile: 50-250 lei/mp pentru proiect complet (și un alt interval 160-470 lei/mp pe aceeași pagină, contradictoriu), consultanță 200-500 lei/oră, apartament 75 mp = 5.000-18.000 lei: https://brig.ro/decoratiuni-interior/bucuresti ; un membru de grup Facebook: 20-100 EUR/mp, altul de la 15 EUR/mp cu randări 3D și schițe cotate: https://playtech.ro/2026/cat-costa-un-designer-de-interior-pentru-mobilarea-unui-apartament-de-3-camere-in-bucuresti-preturi-actualizate-pentru-2026/ ; design interior 4.500-15.000 lei: https://brig.ro/cat-costa/proiectare ; onorariu de 5-15% din valoarea amenajării: https://www.zf.ro/companii/cine-apeleaza-la-arhitecti-pentru-decorarea-casei-costurile-se-ridica-la-5-15-din-pretul-amenajarii-interioare-6452022 (titlu din rezultate, conținut necitit, UNVERIFIED). Buget mediu de amenajare 30-50.000 EUR (Asociația Română de Design Interior, Pro TV): https://stirileprotv.ro/divers/tot-mai-multi-romani-vor-locuinte-ca-in-reviste-cat-sunt-dispusi-sa-plateasca-pentru-designeri-si-arhitecti.html . Implicație: un calculator de onorariu cu 4 modele (lei/mp, % din buget, forfetar pe pachet, pe oră) și etape cu tranșe.
**Șantier.** Dirigintele răspunde de execuția tehnică; designerul urmărește conformitatea estetică și funcțională; diriginte obligatoriu la proiecte mari: https://www.forbes.ro/?p=125510 . Termenele de livrare lungi sunt o problemă a constructorilor (73,6% pun întârzierile pe prețuri materiale, studiu 2022): https://www.economica.net/?p=612573 . Interiorul rămâne în rolul de „supervizare de autor”, deci raport de vizită cu poze (există) și semnătura executantului (lipsește).

## 5. Top 20 funcții lipsă pentru Casa3D (ordonat)

Mărime: S = sub 1 zi, M = 1-3 zile, L = peste 3 zile. „Parțial” = ce există deja.

| # | Funcție | Dovezi (surse) | Cine cere | Valoare vânzare | Mărime | Parțial deja? |
|---|---|---|---|---|---|---|
| 1 | Proformă/factură din deviz spre SmartBill/FGO/Oblio (CSV sau API), cu avans, TVA, cod 13 zerouri pentru PF | capital.ro (e-Factura); taxdome.com; zarinacrm.ro | Studiouri cu PFA/SRL; contabili | Mare: obligație legală | M-L | Parțial: buget cu TVA, profil studio CUI/IBAN |
| 2 | Adaos și discount trade pe furnizor, cu raport de profit pe proiect (cost, preț client, marjă) | DesignFiles markup; Mydoma Capterra (profit tracking); RaftLabs | Designeri care revând | Mare | M | Parțial: BOQ/buget, comenzi pe furnizor; marjă nu |
| 3 | Plan de plăți: avans, tranșe pe etape, stare încasat/restant | Programa Capterra; 4dbiz; DesignFiles | Toți cei cu contract | Mare | M | Nu |
| 4 | Contract și ofertă generate (pachete, etape, calculator onorariu lei/mp, % din buget, oră) cu clauze GDPR | brig.ro; playtech.ro; zf.ro | Studiouri mici | Mare | M | Parțial: profil studio |
| 5 | Produse proprii/import din orice furnizor (URL, CSV, preț cost + preț catalog + discount) | Programa „library finicky”; Houzz Pro cumpărare | Toți: catalogul fix IKEA/Dedeman/JYSK nu acoperă mobilă la comandă | Mare | M-L | Parțial: catalog fix cu proveniență |
| 6 | Conturi multiple și roluri (echipă, client cu drepturi limitate, meșter) | todo-claude-casa3d (lipsă SaaS multi-cont); Studio Designer „role-based permissions” (destinationeichler.com) | Studiouri cu 2+ persoane | Mare (blocant de vânzare) | L | Nu (model single-designer) |
| 7 | Aprobări cu semnătură/jurnal (cine, când, versiune) și amintiri înainte de „decide-by” | blog.designfiles.co/interior-design-approvals; businessofhome.com | Toți | Mare | S-M | Parțial: aprobări, change orders, date decide-by |
| 8 | Calendar de execuție pe meserii (Gantt simplu, dependențe, export ICS/Google) | Programa Capterra (calendar sync); DesignFiles | Designeri cu supervizare | Medie-mare | M | Parțial: sarcini, termene comenzi |
| 9 | Urmărire comenzi: status „întârziat/lipsă stoc”, alerte de termen, avertizare înainte de aprobare | RaftLabs; Notion template; BoH | Cine face FF&E | Medie-mare | S-M | Parțial: livrare și decide-by |
| 10 | Portal client: aprobări ancorate pe plan/3D, comentarii pe piesă, rol client separat | Materio Capterra; Mydoma Capterra | Clienți și designeri | Medie-mare | M | Parțial: link cu feedback și alegeri |
| 11 | Mobil: PWA offline pentru vizite pe șantier (poze, snag list), semnătură executant | Programa Capterra (mobil/iPad); forbes.ro | Designeri pe teren | Medie-mare | M | Parțial: vizite, poze, snag list |
| 12 | Import plan de la aplicații de scanare (DXF/PDF/PNG la scară) + toleranță | albiware; sketchup forum; apps.apple.com | Cine măsoară cu telefonul | Medie | M | Parțial: releveu, underlay |
| 13 | GDPR: registru prelucrări, consimțământ foto, export și ștergere date client, retenție | revistabiz.ro; startupcafe.ro | Toți (obligație) | Medie | M | Parțial: coș, export ZIP |
| 14 | Fișă de produs/specificație PDF per piesă (dimensiuni, material, furnizor, termen) pentru client și meșter | Gather/FF&E (cbinsights); DesignFiles | Studiouri cu dosare | Medie | S-M | Parțial: handover, finish-schedule |
| 15 | Notificări (email/WhatsApp link) către client la modificări și aprobări | Programa/Mydoma (portal); schedulingkit | Clienți | Medie | S-M | Nu |
| 16 | Versiuni de ofertă comparabile (v1/v2) cu diferențe de preț | Programa/Houzz Pro oferte | Toți | Medie | M | Parțial: opțiuni A/B/C, revizii |
| 17 | Mostre și materiale: registru de mostre cerute/primite/returnate | Material Bank (help.materialbank.com) | Studiouri cu mostre | Mică-medie | S-M | Parțial: finisaje, mood board |
| 18 | „Vezi în camera ta” (AR prin GLB, WebXR/Quick Look) | SAGE 2024; GLB deja exportat | Prezentare client | Medie (efect de demo) | S-M | Parțial: GLB export, tur 3D |
| 19 | Verificări de accesibilitate (gabarit 150 cm, lățimi, praguri) ca avertismente | jlconline.com checklist | Nișă (seniori, familii) | Mică-medie | S-M | Parțial: trade-checks NKBA |
| 20 | Raport de rentabilitate pe proiect: ore (pontaj) × tarif + marjă produse vs onorariu | Mydoma Capterra; DesignFiles (pontaj, management) | Studiouri în creștere | Medie | S-M | Parțial: pontaj, buget |

**Notă de încredere:** clasamentul este judecata autorului pe dovezi indirecte (vendori, rezumate Capterra); nu există volum de cereri Reddit sau sondaj românesc. Se confirmă prin 5-10 interviuri cu designeri din România.

## 6. Lista „nu construi” (tentant, dovezi slabe)

1. **Contabilitate completă / program de facturare propriu.** Studio Designer eșuează la designeri non-contabili (3,0 pe Capterra, sursă concurentă); SmartBill/FGO/Oblio există deja. Exportați, nu înlocuiți.
2. **Generare de lead-uri / marketplace de clienți.** Houzz Pro este criticat pentru lead-uri care nu răspund și pentru presiunea de vânzare directă (surse la punctul 2).
3. **VR cu cască.** Bariere de cost și adoptare, utilitate mai mare la comercial (SAGE 2024; mjaf.journals.ekb.eg). AR simplu (#18) ajunge.
4. **Scanner LiDAR propriu.** Precizie ±2-5% și erori de scan-to-CAD (albiware, forum SketchUp); aplicații dedicate există. Doar import.
5. **Date de sustenabilitate / EPD / carbon.** Nicio sursă care să arate cerere (căutare fără rezultat). UNVERIFIED, amânat.
6. **Funcții AI în produs** (generare de design, chat). Interzis de regula proiectului; nicio dovadă că sunt prioritate. Folosiți reguli deterministe.
7. **Casă de marcat integrată / plăți online proprii.** Programa e criticat pentru dependența de Stripe și multi-valută; plata bancară cu IBAN pe proformă ajunge în România.
8. **Mostre fizice multi-brand ca Material Bank.** Model logistic, fără echivalent local verificat.
9. **Biblioteci uriașe de modele 3D generice.** Deja investit în produse reale verificate (233 variante); reclamația concurenților e despre bibliotecă greu de gestionat, nu despre lipsă de volum.
