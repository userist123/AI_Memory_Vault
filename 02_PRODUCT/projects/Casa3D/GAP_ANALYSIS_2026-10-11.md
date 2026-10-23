---
id: "3e458b8c-e0f1-4b06-b0d7-9a3ffae2ae1f"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, gap-analysis, roadmap, mobile]
created: "2026-10-11"
updated: "2026-10-11"
provenance:
  source_type: web
  source_ref: "Vendor feature and help pages of Houzz Pro, Programa, Mydoma Studio, DesignFiles, Studio Designer, Gather, Material Bank, Foyr Neo, Coohom, Planner 5D, RoomSketcher, SketchUp LayOut, Chief Architect, pCon; practitioner reviews (Spaces by Dee, Capterra/GetApp); RO e-Factura articles (read 2026-10-11); Casa3D code inventory and phone audit at 390 px (2026-10-11)"
  source_date: "2026-10-11"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
relations:
  - type: related_to
    target_id: "9d6dcc90-0076-4aad-8458-0f91c20e91d2"
---
# Casa3D: ce lipsește față de software-ul profesional de design interior (2026-10-11)

Cerința proprietarului (2026-10-11): „fă un research pentru ce mai lipsește și după pune în aplicare”; pe telefon „nu funcționează așa bine”.
Sursele sunt în cea mai mare parte paginile producătorilor (promoționale); nu s-au găsit discuții pe Reddit și nici un sondaj ASID/IIDA despre nevoile designerilor. Afirmațiile neconfirmate sunt marcate UNVERIFIED, deducțiile pentru piața din România sunt marcate (deducție).

## Unde e Casa3D deja peste piață
- **Planșe tehnice în aceeași aplicație:** releveu cotat, plan de tavan și iluminat, pardoseli, elevații, DXF, dosar PDF. Houzz Pro, Programa, Mydoma, DesignFiles și Studio Designer nu produc planșe pentru meseriași; designerii le combină cu SketchUp sau CAD.
- **Deviz:** cu pierderi pe model, manoperă (min/probabil/max) și rezervă.
- **Finisaje pe cameră:** bucătărie, baie și corpuri de iluminat plasate.
- **3D și randări:** 3D în browser și randări Blender.
- **Link pentru client:** fără cont, cu păreri pe camere.

## Ce lipsește (după fazele unui proiect)
| Faza | Lipsă | Cine are | Prioritate |
|---|---|---|---|
| Client | Fișa clientului (contact, adresă), starea proiectului, termene, onorariu | toți | M |
| Client | Chestionar de început tipăribil; contract din șablon + acceptare online | DesignFiles, Mydoma, Houzz | M / S |
| FF&E | Produs adăugat dintr-un link de magazin (web clipper: nume, preț, imagine, cod) | Programa, Mydoma, Houzz, DesignFiles | M |
| Aprobări | Aprobarea clientului pe fiecare produs/finisaj, cu comentarii | Houzz, Programa | M |
| Achiziții | Stare pe linie (de comandat / comandat / livrat / montat), furnizor, nr. comandă, date, întârzieri; listă de comandă pe furnizor | Programa, Houzz, Mydoma | M |
| Șantier | Vizite cu poze de pe telefon pe cameră, note; listă de remedieri (snag list) cu poze, stare, raport PDF | Houzz (jurnal), aplicații dedicate de punch list; lipsește din uneltele de design | M |
| Organizare | Sarcini cu termen, lista „azi” pe toate proiectele | Houzz, DesignFiles, Mydoma | M |
| Bani | Plan de plăți (avans, tranșe), proforme; facturare e-Factura (obligatorie B2C din 2025) prin SmartBill/Oblio | toți (fără e-Factura) | M (integrare ulterioară, necesită cont) |
| Tâmplărie | Desene de mobilier la comandă + listă de debitare PAL (deducție: important în RO) | Coohom, pCon | S |
| Predare | Dosar de predare: produse, garanții, întreținere | nimeni (gol în piață) | S |

## Telefon (audit la 390 px, 2026-10-11)
- **Antetul editorului:** ocupă aproape jumătate din ecran (două rânduri de butoane).
- **Etapele 2–4:** pornesc pe „Plan + 3D”; pe telefon jumătatea 3D apare ca o zonă albă mare.
- **Prezentarea pentru client:** e în format 16:9 de laptop, cu text mic; bara „← → pentru planșe” acoperă conținutul.
- **Ținte de atingere:** multe sub 32 px (37 în etapa Concept).
- **Lățime:** etapa Mobilier e cu 16 px mai lată decât ecranul.
- **Ce fac designerii pe telefon (Houzz Pro, Studio Designer):**
  - poze și video de pe șantier, cu note vocale;
  - jurnal zilnic și sarcini;
  - aprobarea ofertelor;
  - scanare LiDAR.
- **Tipare de interfață pe telefon:** bară de navigare jos, buton central „+”, captură direct cu camera, lucru fără internet.

## Ordinea de implementare
0. **Telefon:** antet compact, fără ecran împărțit, prezentare pe verticală pentru client, ținte de 44 px, fără depășire de lățime.
1. **Fișa clientului și starea proiectului:** contact, adresă, etapă, termen, onorariu; panoul de proiecte filtrat pe stare.
2. **Comenzi:** urmărire pe linie din fișa de finisaje și mobilier, listă de comandă pe furnizor, întârzieri.
3. **Șantier:** vizite cu poze de pe telefon, listă de remedieri cu poze și stare, raport PDF.
4. **Aprobări pe produs în linkul pentru client.**
5. **Produs dintr-un link de magazin:** cu protecție SSRF și listă de magazine permise.
6. **Sarcini și lista „azi”.**
7. **Mai târziu:**
   - plan de plăți și proforme; e-Factura prin SmartBill/Oblio (cere contul proprietarului);
   - contract cu acceptare online;
   - mobilier la comandă cu listă de debitare;
   - dosar de predare.

## Surse principale
- **Houzz Pro:** https://pro.houzz.com/for-pros/software-interior-designer · https://pro.houzz.com/pro-help/r/how-do-i-use-the-houzz-pro-app
- **Programa:** https://programa.design/features/interior-design-procurement-software · https://programa.design/blog/purchase-orders
- **Mydoma și DesignFiles:** https://www.mydomastudio.com/features · https://s.designfiles.co/plans
- **Gather:** https://gatherit.co/blog/best-ffe-specification-software-comparison-2025
- **Liste de remedieri (punch list):** https://www.deltek.com/resources/articles/snag-list-app/
- **Recenzie de practician:** https://www.spacesbydee.com/the-best-and-worst-interior-design-software/
- **e-Factura B2C:** https://www.fiscalitatea.ro/e-factura-b2c-23791/
