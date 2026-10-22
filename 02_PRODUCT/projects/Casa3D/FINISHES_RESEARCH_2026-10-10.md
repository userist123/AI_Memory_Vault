---
id: "bedaf7f9-acd9-47d5-9c3f-00635941b05d"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, finishes, interior-design, research, boq]
created: "2026-10-10"
updated: "2026-10-10"
provenance:
  source_type: web
  source_ref: "Research agent run 2026-10-10: competitor feature pages, Dedeman/IKEA product pages, brig.ro search snippets, standards summaries (160 URLs, listed in the report)"
  source_date: "2026-10-10"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
relations:
  - type: related_to
    target_id: "9d6dcc90-0076-4aad-8458-0f91c20e91d2"
---
> Status implementare (2026-10-10, sesiunea Claude Code): P0 punctele 1–6 sunt făcute în aplicație (commit-urile 0e75f3d1, 4daf123f,
> 0b7491ba, 80b62a0d pe `codex/casa3d-memory`): model de finisaj cu produs/furnizor/specificații, mod de așezare cu rost, placări pe
> pereți, tavan fals/scafă/cornișă/spoturi, BOQ cu pierderi pe model, avertismente tehnice, fișa de finisaje (CSV + tipărit).
> Prețurile manoperei din brig.ro provin din rezultatele căutării (pagina a răspuns 403) și sunt marcate LOW în catalog.

# Casa3D — Cercetare „finisaj de designer”: ce poate fi schimbat/adăugat într-o locuință

Data cercetării: 2026-10-10. Autor: agent de cercetare (fără modificări în depozit). Toate afirmațiile factuale au URL; ce nu a putut fi citit direct este marcat **[neverificat]** sau **[not found]** și apare și în lista de la final.

Fișiere însoțitoare: `taxonomy.json` (taxonomie mașină), `ro_products.json` (doar produse ale căror pagini au fost citite efectiv), `sources.json` (toate URL-urile consultate).

---

## 0. Ce are Casa3D azi și unde este golul (rezumat)

Casa3D permite azi: culoare perete/pardoseală/tavan per cameră, accent per față de perete, culoare toc per ușă/fereastră, culoare+material per mobilier; finisaje cu preț per cameră (2 parchete laminate, 2 gresii 60x60, 3 lavabile albe, 3 faianțe 30x60, 2 plinte MDF, 3 corpuri IKEA, adeziv) și tarife de manoperă (parchet, plintă, gresie, faianță, zugrăvit, hidroizolație); 3D procedural (parchet/gresie), uși, ferestre, scări, ~20 grupe de mobilier, export Blender Cycles.

Golul față de un instrument profesional se concentrează în 6 zone: (1) **suprafețe de perete** dincolo de vopsea (tapet, riflaj/lambriu, tencuială decorativă, piatră/cărămidă, panouri 3D); (2) **tavan** (tavan fals rigips, scafă LED, cornișe, înălțimi pe zone); (3) **model de montaj** (herringbone/chevron, dimensiune placă, rost, offset, rotație); (4) **iluminat** (straturi, tip corp, CCT, CRI, IP, lux); (5) **bucătărie/baie ca sistem** (fronturi, blat, splashback, baterii, cabină duș, oglindă, nișe); (6) **livrabile** (fișă de finisaje, plan iluminat/electric, deviz cu pierderi și manoperă).

---

## 1. Audit de funcționalități al competitorilor

### 1.1 Constatări per aplicație (cu surse)

**Coohom** — tool de pavaj/„paving” pe bucăți: rost implicit 1 mm, maxim 10 mm, culoare rost personalizabilă; 4 tipuri de pavaj (grid, mixing, brickwork/cărămidă, checkerboard); sloturi verticale/orizontale/diagonale; punct de start cu linii-ghid; șanfren (chamfer) pe muchie; *fără* rotație, offset sau herringbone ca preset în articol ([helpcenter 3FO4K4WKW5NM](https://www.coohom.com/helpcenter/3FO4K4WKW5NM?hl=en)). Tavan: model din bibliotecă, tavan parametric, sau editor de tavan desenat liber; iluminat încastrat în tavan ([helpcenter 3FO4K4UKEIKM](https://www.coohom.com/helpcenter/3FO4K4UKEIKM)). Cornișe/„crown molding” și „light rail molding” generate parametric pe mobilier (stil, material, înălțime, extensie, wrap) ([how-to-generate-crown-molding](https://www.coohom.com/helpcenter/how-to-generate-crown-molding), [light rail](https://www.coohom.com/th/helpcenter/how-to-generate-light-rail-molding)). Cotație pentru corpuri de mobilier custom: reguli de preț pe arie desfășurată sau proiectată a panourilor, listă de cotație descărcabilă (enterprise) ([3FO4K4UKTSK7](https://www.coohom.com/helpcenter/3FO4K4UKTSK7), [3FO4K4UKSQRV](https://www.coohom.com/helpcenter/3FO4K4UKSQRV)). Desene de execuție: pagina de construction drawings poate lipsi elevații/instalații electrice; se completează manual; import CAD cu recunoaștere pereți/uși/ferestre ([3FO4K4UPYOP0](https://www.coohom.com/helpcenter/3FO4K4UPYOP0), [3FO4K4WL8BUY](https://www.coohom.com/helpcenter/3FO4K4WL8BUY)). Recenzie terță: „one-click” pachet desene de construcție, dar insuficient pentru documentație tehnică detaliată ([softwareadvice](https://www.softwareadvice.com/3d-cad/coohom-profile/)). Pagina `/features` a returnat 404 **[neverificat: lista oficială completă]**. IES/Kelvin: **[not found]** în help center.

**Homestyler** — editor de tavan „Customize Ceiling” cu șabloane, mulaje (moldings), benzi de lumină, „Molding Brush”, extrudare cu grosime introdusă numeric; tavan parametric cu „light troughs, light band and decorative moldings” și tavane înclinate ([forum 1486281687275294721](https://www.homestyler.com/forum/view/1486281687275294721), [blog/1301](https://www.homestyler.com/blog/1301?spm)). Material Editor (culoare/material pe model), „Interior Finishes” (pereți/pardoseli/tavane/plăci), Lighting Editor (intensitate, culoare, direcție), export BOM/plan/DWG; AI Texturer cu dimensiune reală a unității de textură ([trustradius](https://www.trustradius.com/products/homestyler), [forum 2094270295577980930](https://www.homestyler.com/forum/view/2094270295577980930)). Pagina oficială `/features` listează Floor Planner, Kitchen & Closet, Bathroom Design, Real Time Render, Image to Texture, 10M+ modele ([homestyler.com/features](https://www.homestyler.com/features)). Grout/herringbone în tool de pavaj: **[not found]**. IES: **[not found]**.

**Planner 5D** — catalog 8.000+ obiecte, randări 4K, AI floorplan recognition, „shopping list” cu estimare de cost și comutare buget/lux ([planner5d.com](https://planner5d.com/)). Texturi custom: 512–1024 px, seamless, scară și rotație, 5 sloturi gratuite ([support 5876729](https://support.planner5d.com/en/articles/5876729-custom-textures-web), [15886070](https://support.planner5d.com/en/articles/15886070-how-to-upload-a-custom-texture)). Fără control de rost/herringbone documentat **[not found]**.

**Foyr Neo** — bibliotecă 60.000+ produse, randări 4K/„12K”, editare material (culoare, luciu, transparență), lumină de dimineață/după-amiază ([capterra reviews](https://www.capterra.com/p/204757/Foyr-Neo/reviews/), [techjockey](https://techjockey.com/detail/foyr)). BOQ: **[not found]**.

**Cedreo** — 8.500+ mobilier/decor/finisaje; finisaje diferite pe aceeași suprafață („backsplash or trim accent”); lumini on/off cu **temperatură de culoare și intensitate**; randări, 3D floor plans; „Presentation Documents” în meniu ([cedreo 3d-furnishing](https://cedreo.com/3d-visualization-software/3d-furnishing/)). Tile layout, mulaje, deviz: nu apar pe pagină **[not found]**.

**RoomSketcher** — „Replace Materials” (Pro/Team), 5.000+ obiecte, 3D Photos, 360, Live 3D, 2D/3D floor plans, site plans, measurements, print la scară ([online-product-sheet](https://www.roomsketcher.com/online-product-sheet/)); adăugiri recente de plăci, cuarț, marmură ([newsroom](https://www.roomsketcher.com/?p=21345)). Control rost/model: **[not found]**.

**Live Home 3D (Pro)** — 2.100 materiale, Material Editor PBR (roughness/metalness), Light Source Editor (glow, atenuare, direcție), geolocalizare + oră, export glTF/USDZ/OBJ/FBX, randare Cycles/ProRender, elevații 2D ([livehome3d.com/mac](https://www.livehome3d.com/mac)). Mulaje/costuri: nu apar **[not found]**.

**Chief Architect / Home Designer** — Materials List: raportează trim (ex. „76 feet of base molding”), gips-carton pe foi, straturi perete, finisaj tavan/pardoseală, „Components” editabile; listă = snapshot, trebuie regenerată ([KB-00453](https://homedesignersoftware.com/support/article/KB-00453/estimating-material-costs.html)). Mulaje pe mobilier din bibliotecă (ex. profil CM14) ([KB-00701](https://www.chiefarchitect.com/support/article/KB-00701/adding-moldings-to-cabinets.pdf)); tipuri corpuri framed/frameless/inset ([videos/7-cabinets](https://www.chiefarchitect.com/videos/playlists/87/7-cabinets.html)); hărți roughness/metal/normal/emissive din X10 ([x10-new-features](https://cloud.chiefarchitect.com/1/pdf/marketing/x10-new-features.pdf)); „live material lists” din X15 ([chieftalk X15](https://chieftalk.chiefarchitect.com/topic/37945-new-x15-features/)); din 2026 un singur produs „Home Designer” ([homedesignersoftware.com](https://homedesignersoftware.com/products/home-designer-pro)).

**SketchUp + Enscape/V-Ray** — Enscape: tipuri de lumină sphere/spot/rect/disk/linear, „Load IES profile” din Enscape Objects, intensitate luminoasă editabilă; recomandă IES de producător, hărți roughness pe imagine, FOV ~50° pentru stills, surse CC0 de texturi ([learn.enscape3d.com lighting](https://learn.enscape3d.com/?p=31862), [blog.chaos.com interior best practices](https://blog.chaos.com/interior-rendering-best-practices)). V-Ray: bibliotecă de materiale și lumini IES ([archdaily tutorials](https://www.archdaily.com/928379/6-easy-tutorials-for-better-sketchup-renders)). Fără BOQ nativ (sunt motoare de randare).

**2020 Design Live** — CAD bucătărie/baie cu cataloage de producător (corpuri, electrocasnice, chiuvete/baterii), EZ Render, lumini cloud cu update în timp real; ediție Foundation cu cataloage generice ([2020spaces about](https://www.2020spaces.com/?p=281205), [brochure](https://www.2020spaces.com/wp-content/uploads/2021/10/Brochure_DesignLive_1920x1080_Draft008.pdf)). Preț la cerere.

**Floorplanner** — 150.000+ obiecte, „roomstyles” (seturi de obiecte+materiale+culori) aplicabile cu Magic Layout, branding, export HD ([floorplanner.com/pro](https://floorplanner.com/pro)). Control pattern pardoseală: **[not found]**.

**IKEA Kreativ** — scanare cameră (Scene Scanner), ștergere mobilier existent, plasare produse IKEA, coș de cumpărături; nu schimbă finisaje de pereți/pardoseli în sursele găsite ([IKEA newsroom](https://ikea.com/us/en/newsroom/corporate-news/ikea-launches-new-ai-powered-digital-experience-empowering-customers-to-create-lifelike-room-designs-pub58c94890), [techcrunch](https://techcrunch.com/?p=2340297)). Pagina de customer-service a dat 404.

**HomeByMe** — catalog cu produse de brand incl. acoperiri de perete/pardoseală, randări 4K, 2D/3D, oferte pentru designeri/brand-uri ([home.by.me](https://home.by.me/)); „shopping list” în notele aplicației ([apkmirror 1.13](https://www.apkmirror.com/?p=6042755)) **[parțial verificat]**.

### 1.2 Matrice de funcționalități

Legendă: ✔ documentat; ◐ parțial/limitat; ✘ absent sau nedocumentat (**[not found]**); ? neverificat.

| Funcție | Coohom | Homestyler | Planner 5D | Foyr Neo | Cedreo | RoomSketcher | Live Home 3D | Chief Arch. | SketchUp+Enscape/V-Ray | 2020 Design | Floorplanner | IKEA Kreativ | HomeByMe | **Casa3D azi** |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Pardoseală: material din catalog | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✘ | ✔ | ◐ (4 produse) |
| Pardoseală: dimensiune placă / scară textură | ✔ | ✔ (AI Texturer) | ✔ (scară) | ? | ? | ? | ✔ | ✔ | ✔ | ✔ | ? | ✘ | ? | ✘ |
| Rost: lățime + culoare | ✔ (1–10 mm) | ? | ✘ | ✘ | ✘ | ✘ | ✘ | ◐ | ◐ (prin textură) | ? | ✘ | ✘ | ✘ | ✘ |
| Pattern: cărămidă/offset/checkerboard | ✔ | ? | ✘ | ✘ | ✘ | ✘ | ✘ | ◐ | ◐ | ? | ✘ | ✘ | ✘ | ✘ |
| Pattern: herringbone/chevron | ◐ (manual) | ◐ (blog) | ✘ | ✘ | ✘ | ✘ | ✘ | ◐ | ◐ (textură) | ? | ✘ | ✘ | ✘ | ✘ |
| Pereți: vopsea cu luciu | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ (PBR) | ✔ | ✔ | ✔ | ✔ | ✘ | ✔ | ◐ (culoare, 3 lavabile) |
| Pereți: tapet / lambriu / piatră | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ◐ | ✔ | ✘ | ✔ | ✘ |
| Mulaje: cornișe, plinte, chair rail | ✔ (pe mobilier) | ✔ (tavan) | ? | ? | ◐ (parametric) | ? | ✘ | ✔ | ◐ (modelat) | ✔ | ? | ✘ | ? | ◐ (plintă doar în BOQ) |
| Tavan fals / scafă / bandă LED | ✔ | ✔ | ? | ? | ? | ? | ✘ | ✔ (tray/coffered) | ◐ (modelat) | ? | ✘ | ✘ | ? | ✘ |
| Uși/ferestre: stiluri, mânere | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✘ | ✔ | ◐ (culoare toc) |
| Iluminat: tipuri corpuri | ✔ | ✔ (area/spot/point) | ◐ | ✔ | ✔ | ◐ | ✔ | ✔ | ✔ (5 tipuri) | ✔ | ◐ | ✘ | ◐ | ◐ (3 plafoniere) |
| Iluminat: CCT/Kelvin | ? | ◐ (culoare) | ✘ | ? | ✔ | ✘ | ◐ (culoare) | ✔ | ◐ (culoare) | ? | ✘ | ✘ | ✘ | ✘ |
| Iluminat: IES | ? | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ | ? | ✔ | ? | ✘ | ✘ | ✘ | ✘ |
| Textile: perdele/covoare | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✘ | ✔ | ◐ | ✔ | ✘ |
| Decor: artă, plante, accesorii | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✘ | ✔ | ✔ | ✔ | ✘ |
| Bucătărie: fronturi/blat/splashback | ✔ | ✔ | ◐ | ◐ | ◐ | ◐ | ◐ | ✔ | ◐ | ✔ | ◐ | ✔ | ◐ | ◐ (culoare/material) |
| Baie: obiecte sanitare, finisaj baterii | ✔ | ✔ | ◐ | ◐ | ◐ | ◐ | ◐ | ✔ | ◐ | ✔ | ◐ | ◐ | ◐ | ◐ |
| Materiale PBR editabile | ✔ | ✔ (reflectivitate etc.) | ✘ | ◐ | ✘ | ✘ | ✔ | ✔ | ✔ | ? | ✘ | ✘ | ✘ | ◐ (procedural) |
| Cataloage de brand | ✔ | ✔ | ◐ | ✔ | ✘ | ✘ | ✘ | ✔ | ◐ | ✔ | ✘ | ✔ (IKEA) | ✔ | ◐ (Dedeman/IKEA manual) |
| Randare foto | ✔ cloud | ✔ | ✔ 4K | ✔ 4K | ✔ | ✔ | ✔ local | ✔ ray trace | ✔ | ✔ EZ Render | ✔ | ✔ AI | ✔ 4K | ✔ Blender Cycles |
| BOQ / listă costuri | ◐ (cotație corpuri, enterprise) | ✔ BOM | ✔ shopping list + cost | ✘ | ✘ | ✘ | ✘ | ✔ Materials List | ✘ | ✔ quote | ✘ | ✔ coș | ◐ | ✔ RoomFinishes |
| Desene tehnice | ◐ | ✔ DWG | ◐ CAD export | ✘ | ✔ 3D plans | ✔ 2D/3D, site | ✔ elevații | ✔ layout sheets | ✔ (SketchUp Layout) | ✔ | ◐ | ✘ | ◐ | ✘ |
| Moodboard / prezentare client | ◐ (Image to Moodboard, Homestyler) | ✔ | ◐ | ◐ | ✔ Presentation Docs | ◐ | ✘ | ◐ | ✘ | ◐ | ✔ branding | ✘ | ◐ | ✘ |

Surse matrice: cele din 1.1.

---

## 2. Taxonomie completă a elementelor schimbabile (grad designer)

Pentru fiecare element: opțiuni, parametri pe care îi setează designerul, unitate BOQ, pierderi tipice, manoperă. Versiunea mașină este în `taxonomy.json`. Sursele pentru pierderi și reguli sunt în §4; retailerii în §3.

### 2.1 Pardoseli
- **Parchet laminat** (click, 7–12 mm, clasa 31/32/33, AC3–AC5) — parametri: lungime×lățime lamelă, grosime, direcție (paralel cu sursa principală de lumină — [Quick-Step](https://www.quick-step.co.uk/en-GB/frequently-asked-questions/laminate/where-in-the-room-should-i-begin-to-install-the-floor)), offset (1/2, 1/3, random), rost de dilatare ≥10 mm ([Sisu](https://a.storyblok.com/f/162000/x/a57db4a720/sisu-laminate-installation-guide-v1-2-web.pdf)), compatibil UFH ≤27 °C suprafață ([BerryAlloc](https://dxm.mediacenter.bintg.com/api/wedia/dam/variation/9kcwb57gfjr6bj7g5ucq85bdmpwz8j15ijnez5o/original)); BOQ mp + folie + plintă ml; pierderi 5–10 % drept, 15–20 % spic ([flooringclarity](https://www.flooringclarity.com/how-much-overage-tile-how-much-extra-tile/)); manoperă da.
- **Parchet laminat/SPC herringbone & chevron** — lamele scurte (ex. Classen 8 mm herringbone, Hornbach [10725328](https://www.hornbach.ro/p/parchet-laminat-classen-8-mm-campo-oak-herringbone/10725328/) — pagina nu s-a încărcat, preț **[not found]**); unghi 90° (herringbone) / 45–60° (chevron); manoperă +20–30 % ([brig.ro ghid](https://brig.ro/montare-parchet/deva)).
- **Parchet stratificat / masiv** (lipit sau flotant, uleiat/lăcuit) — manoperă 50–80 RON/mp, spic 80–150 ([brig.ro pardoseală](https://brig.ro/cat-costa/pardoseala) **[citit din snippet; pagina a răspuns 403 la acces direct]**).
- **Gresie porțelanată** — format (30x60, 60x60, 60x120, 120x120+), rectificată/nerectificată, finisaj (mat/lucios/lappato/antiderapant), R-rating (R9–R11), PEI; parametri: rost 2–3 mm rectificat (standard ANSI ≥1/8" ≈3,2 mm pentru laturi >15") / ≥3/16" nerectificat ([ceramictilefoundation](https://www.ceramictilefoundation.org/blog/want-credit-card-grout-joints-check-tile-industry-standards)), offset ≤33 % la >45 cm ([IMI lippage](https://info.imiweb.org/blog/large-format-tile-dealing-with-lippage)), rotație 0/45/90°, culoare chit, punct de start; BOQ mp (cutii de 1,44 mp la 60x120 Dedeman) + adeziv kg + chit kg; pierderi 10 % drept, 15 % diagonal; manoperă 63–150 RON/mp după format ([brig.ro gresie](https://brig.ro/cat-costa/montaj-gresie) **[snippet]**).
- **Covor/mochetă, LVT/SPC, microciment, beton lustruit, piatră naturală, terrazzo, lemn exterior/WPC pentru terasă** — parametri similari; pierderi 10 %; manoperă da.
- **Plintă** — PVC/MDF/duropolimer/lemn, înălțime 6–12 cm (regula ~6–7 % din înălțimea peretelui — [brainbound](https://www.brainbound.blog/molding-height-guide), [Laurel Bern](https://laurelberninteriors.com/best-proportions-for-interior-trim-why-youre-confused/)); BOQ ml; manoperă 10–35 RON/ml ([brig.ro plintă](https://brig.ro/montaj-plinta/galati) **[snippet]**); MDF nerecomandat în băi (brig).
- **Prag/profil de trecere, pardoseală încălzită (electric/apă)** — ml / mp; manoperă da.

### 2.2 Pereți
- **Vopsea lavabilă** — culoare (RAL/NCS), luciu (mat tavan; eggshell living/dormitor; satin bucătărie; satin/semi-lucios baie — [houselogic](https://houselogic.com/remodel/paint-sheen-guide/), [360painting](https://www.360painting.com/blog/types-of-paint/paint-finishes/the-complete-paint-sheen-guide-choosing-the-righ/)), straturi (2), randament 12–16 mp/l (AplaLux Dedeman); BOQ mp net (sc. goluri) → litri; pierderi 5–10 %; manoperă 11–25 RON/mp + glet 24–50 ([brig.ro vopsitor](https://brig.ro/cat-costa/vopsitor) **[snippet]**).
- **Tapet** — vlies/vinil/textil/fototapet; rolă 0,53×10,05 m (≈5,3 mp) sau 1,06 m; raport model (repeat) & tip potrivire (free/straight/drop); BOQ role (calcul pe „drop-uri” = H + repeat), pierderi 10 % (repeat ≤26 cm), 15–20 % peste ([James Dunlop](https://showroom.jamesdunloptextiles.com/journal/tips-how-to/how-to-calculate-wallpaper-requirements), [wallsneedlove](https://wallsneedlove.com/blogs/news/pattern-repeat-wallpaper)); manoperă 22–55 RON/mp, tavan 80–150 ([brig.ro tapet](https://brig.ro/cat-costa/montaj-tapet) **[snippet]**).
- **Riflaj / lambriu / panou acustic** (MDF, PVC, lemn) — panou 2400–2700 × 400–600 mm, orientare, culoare; BOQ buc/mp; pierderi 5–10 %; manoperă da.
- **Tencuială decorativă** (stucco venețian, marmorino, microciment, beton aparent, structurată) — consum 2–2,5 kg/mp (Kober), culoare, textură; BOQ kg/mp; manoperă 15–60 RON/mp standard, stucco venețian ~125 RON/mp ([brig.ro Barsa](https://brig.ro/tencuiala-decorativa/barsa) **[snippet]**).
- **Piatră/cărămidă decorativă** (gips, beton, naturală, polistiren) — mp (pachete 0,62 mp), colțare ml; pierderi 10 %; manoperă da.
- **Panouri 3D, placaj perete (faianță/gresie pe perete)** — ca la gresie; faianță 80–120 RON/mp (brig).
- **Accent pe o singură față** (Casa3D are deja „accent per face”) — de extins la orice material, nu doar culoare.

### 2.3 Tavane
- **Tavan fals rigips** (plan, în trepte, cu scafă) — grosime 12,5 mm (Rigips RB 1200×2600, 3,12 mp/placă), structură CD/UD, înălțime coborâre; BOQ mp + ml profile + glet + vopsea; manoperă 18–50 RON/mp (doar manoperă) ([brig.ro tavan fals](https://brig.ro/cat-costa/tavan-fals) **[snippet]**).
- **Scafă LED** — simplă 28–35 RON/ml, la cheie 50–90, luminoasă 55–100 + LED 30–80 RON/ml ([brig.ro scafă](https://brig.ro/cat-costa/scafa-tavan) **[snippet]**); bandă LED 3000 K 2500 lm/5 m (Dedeman 98,07 lei) + profil aluminiu.
- **Cornișă / baghetă** — polistiren/poliuretan/MDF; înălțime proporțională cu tavanul (2,5–6" la 8 ft; 7–12" la ≥10 ft — [Angi](https://angi.com/articles/crown-molding-sizes.htm)); BOQ ml; manoperă 10–30 RON/ml, rozete 50–100/buc ([brig.ro profile](https://brig.ro/montaj-profile-decorative/bata) **[snippet]**).
- **Înălțime tavan pe zonă, grinzi aparente, tavan lambrisat, tavan întins (stretch)** — mp.

### 2.4 Mulaje & trim
Plintă, chair rail (~81–91 cm — [block renovation](https://www.blockrenovation.com/guides/wall-moulding-design-ideas-and-different-types?hsLang=en)), panouri de perete (wainscot/boiserie), pervazuri uși/ferestre, profile LED. BOQ ml; manoperă ml.

### 2.5 Uși
Tip (celulară, plină, sticlă, glisantă, pivot, pocket), foaie 66–96 cm, înălțime 202/210/240, finisaj (folie, CPL, furnir, vopsit RAL), toc (standard/ascuns), pervaz, balamale (aparente/ascunse), mâner (finisaj crom/negru/alamă), prag; exemplu Dedeman R80 stejar gri 86×202 cu toc 789 lei, mânere separat ([6022716](https://www.dedeman.ro/ro/usa-interior-celulara-cu-geam-r80-dreapta-gol-d3-stejar-gri-86-x-202-cm/p/6022716)). BOQ buc; manoperă buc.

### 2.6 Ferestre & tratamente
Profil (PVC/aluminiu/lemn), culoare interior/exterior, glaf interior (PVC/piatră/lemn, ml). **Perdele/draperii**: fullness 1,5× (tailored) / 2× (standard) / 2,5× (pliuri) × lățime șină; lungime până la ~1 cm deasupra pardoselii ([direct-fabrics](https://www.direct-fabrics.co.uk/understanding-curtain-fullness), [twopages](https://twopagescurtains.com/blogs/news/how-to-measure-for-curtains)); BOQ ml stofă / buc + șină ml. **Jaluzele**: rulou, zebra, venețiene, romane, verticale, plisate — buc pe deschidere. Manoperă montaj.

### 2.7 Scări
Treaptă (lemn, gresie, piatră), contratreaptă, balustradă (sticlă/metal/lemn), mână curentă, iluminat trepte, profil anti-alunecare. BOQ buc trepte / ml balustradă.

### 2.8 Iluminat
- **Straturi**: ambiental, de lucru (task), de accent, decorativ.
- **Tipuri corpuri**: plafonieră, spot încastrat (IP20 interior / IP44 baie), șină magnetică/track, pendul, aplică, lampadar, veioză, bandă LED, profil LED, iluminat de ghidare.
- **Parametri**: W, lm, CCT (2700 K dormitor, 2700–3000 K living, 3000–4000 K bucătărie/baie — [psmlighting](https://psmlighting.be/en/news/blogs/which-color-temperature-suits-which-room), [Feit](https://www.feit.com/inspire/how-to-choose-the-right-color-light-temperature-for-your-bulb-or-fixture/)), CRI (≥90 bucătărie/baie — [shophorne](https://shophorne.com/blogs/journal/color-rendering-index-what-is-cri-and-why-does-it-matter)), unghi fascicul, dimabil (TRIAC/0-10V/DALI/Zigbee), IP, diametru decupaj (ex. 75 mm), înălțime pendul 76–91 cm peste masă/insulă ([Studio McGee](https://studio-mcgee.com/light-fixture-hanging-heights-guide/), [artika](https://artika.com/blogs/inspiration/complete-pendant-height-spacing-guide)), nivel lux țintă (living 100–300, bucătărie 300–750 / blat 400, dormitor 100–200, baie 200–300 — [ledyilighting](https://www.ledyilighting.com/residential-recommended-lighting-levels/), [V-TAC](https://www.vtacexports.com/poland/blog/post/recommended-lighting-levels-for-residential-and-office-spaces)); EN 12464-1 acoperă locuri de muncă, nu locuințe ([sonel](https://sonel.pl/en/knowledge-centre/press-articles/illuminance-measurements/en-12464-12021-key-lighting-requirements-for-indoor-work-places)).
- BOQ buc + ml bandă + driver buc; manoperă electrician (buc/punct).

### 2.9 Finisaje electrice
Întrerupătoare/prize: gamă (ex. Schneider Asfora 32,13 lei/buc Dedeman), culoare (alb/crem/antracit/negru), ramă (1–4 posturi), tip (simplu, cap-scară, cruce, jaluzele, dimmer, USB), IP44 în baie; înălțimi montaj; BOQ buc; manoperă punct.

### 2.10 HVAC vizibil
Calorifer oțel panou vs decorativ vertical (ex. Radox Nova 420×1800, 937 W, 1846 lei; Radox Serpentine baie 275 W, 1382 lei — Dedeman), culoare RAL, robineți (termostatici, design), unitate AC interioară (split/consolă/ductat cu grilă), grile ventilație, încălzire în pardoseală (compatibilitate finisaj, ≤27 °C). BOQ buc/W; manoperă instalator.

### 2.11 Bucătărie
Fronturi (PAL melaminat, MDF vopsit mat/lucios, furnir, lemn), stil (plat, framed, shaker, cu riflaj), mânere (bară, scoică, gola/push), carcasă (PAL 16/18 mm alb/gri), blat (PAL 38 mm — ex. Hornbach 4100×600×38 ~530–539 lei **[snippet, pagina nu s-a încărcat]**; compact HPL 12 mm; cuarț/granit/ceramică 12–20 mm; lemn masiv), grosime, muchie, splashback (gresie, sticlă, HPL, cuarț; înălțime 54–60 cm între blat și corpuri suspendate — [Valcucine](https://www.valcucine.com/en/?p=17517)), înălțime blat 85–95 cm (uzual ~90–91) ([Egger](https://www.egger.com/en/blog/kitchen-worktop-height-guide?country=GB), [Valcucine ergonomics](https://www.valcucine.com/en/planning/ergonomics/height-depth)), chiuvetă (inox/compozit/ceramică; sub/peste blat), baterie (crom/negru/inox/alamă; extractibilă), electrocasnice (încorporabile vs libere), iluminat sub corpuri. BOQ: ml corpuri (730–1950 RON/ml la comandă — [brig.ro mobilă](https://brig.ro/cat-costa/mobila-la-comanda) **[snippet]**), mp blat, mp splashback, buc; manoperă montaj.

### 2.12 Baie
Gresie/faianță (R10 min, R11 duș; clasă B barefoot — [tilemountain](https://tilemountain.co.uk/blog/slip-ratings-explained), [wdtegels](https://wdtegels.com/en/blogs/kennisbank/antislipwaarde-tegels-r-waarde)), hidroizolație mp, obiecte sanitare (WC suspendat/stativ, rezervor încastrat, lavoar pe blat/suspendat/mobilier, cadă/duș), cabină duș (walk-in sticlă 8 mm, profil negru/crom; ex. Seldus EUR5 cu cădiță 1440 lei Dedeman), rigolă, baterii (finisaj crom/negru mat/alamă/inox periat; ex. Kludi Bozz negru mat 1319 lei), accesorii (portprosop, radiator portprosop), oglindă (LED, dezaburire, IP44; centru 145–165 cm — [bathroommountain](https://www.bathroommountain.co.uk/inspiration-and-advice/bathroom-mirror-height); aplice la 152–168 cm — [edwardmartin](https://www.edwardmartin.com/blogs/information/how-high-should-bathroom-vanity-lights-be-hung-above-the-floor)), nișe în duș, mobilier lavoar (înălțime 75–85 cm — [worktophub](https://www.worktophub.co.uk/help-and-ideas/the-standard-worktop-height-a-practical-guide)), zone IEC 60364-7-701 (0: IPX7/SELV; 1: ≥IPX4 (unele surse IPX5); 2: ≥IPX4 — [electrical-installation.org](https://www.electrical-installation.org/enwiki/Bathroom_electrical_installation), [vanmoofer](https://vanmoofer.com/wiresketch/learn/iec-60364-wet-room-zones/), [IEC 60364-7-701:2019](https://webstore.iec.ch/publication/28906)). BOQ mp/buc; manoperă faianțar + instalator.

### 2.13 Tâmplărie la comandă (built-in)
Dressing, bibliotecă, nișă TV, bancă fereastră, uși de dressing (glisante/batante), fronturi, interior (PAL/MDF), iluminat integrat. BOQ ml/mp front; manoperă.

### 2.14 Mobilier liber
Canapea (țesătură/catifea/piele; dimensiune), pat, masă/scaune, comodă, birou etc. — Casa3D acoperă deja; de adăugat: cod produs, furnizor, preț, lead time.

### 2.15 Textile
Perdele/draperii/sheers (fullness, lungime, căptușeală, șină/galerie), jaluzele, covoare (living: picioarele din față pe covor; dormitor: 60–90 cm în jurul patului; dining: ≥60–75 cm dincolo de masă — [rugs.com](https://rugs.com/blog/what-size-rug-do-i-need-the-complete-rug-size-guide-for-every-room/), [chairish](https://www.chairish.com/blog/sc-standard-rug-sizes-guide-room-by-room-measurements/)), lenjerie, perne, pleduri. BOQ buc/ml.

### 2.16 Decor
Artă (dimensiune, ramă), oglinzi, plante (ghiveci), accesorii, cărți, lumânări. BOQ buc.

### 2.17 Exterior adiacent
Pardoseală balcon/terasă (gresie R11 antigel, WPC, lemn), balustradă, parapet, iluminat IP65. BOQ mp/ml.

---

## 3. Piața din România (2025-2026)

### 3.1 Retaileri pe categorie (cine vinde ce)
| Categorie | Retaileri (RO) |
|---|---|
| Parchet laminat/SPC/herringbone | Dedeman, Leroy Merlin, Hornbach, Brico Depot, Altex (marketplace), eMAG |
| Gresie/faianță | Dedeman, Leroy Merlin, Hornbach, Ambient, Arabesque, Brico Depot, showroom-uri |
| Vopsea, tencuială decorativă | Dedeman, Hornbach, Leroy Merlin, Brico Depot |
| Tapet | Dedeman, Hornbach, Leroy Merlin, eMAG |
| Riflaj/lambriu/panouri | Dedeman, Hornbach, Altex, eMAG |
| Cornișe/baghete/plinte | Dedeman, Hornbach, Leroy Merlin |
| Gips-carton, profile | Dedeman, Hornbach, Leroy Merlin, MaxBau |
| LED/profile/spoturi | Dedeman, Hornbach, eMAG, Leroy Merlin |
| Întrerupătoare/prize | Dedeman, Hornbach, eMAG |
| Calorifere decorative | Dedeman, Hornbach, Altex |
| Baterii, obiecte sanitare, cabine duș | Dedeman, Hornbach, Leroy Merlin, Altex |
| Oglinzi LED | Dedeman, Hornbach |
| Uși interior | Dedeman, Hornbach, Leroy Merlin, Porta Doors |
| Blat/corpuri bucătărie | Dedeman (Domino), Hornbach, IKEA, Mobexpert, Leroy Merlin |
| Covoare, perdele, textile | IKEA, JYSK, Hornbach, Altex, Mobexpert, eMAG |

### 3.2 Produse verificate (pagina citită efectiv pe 2026-10-10)
Lista completă cu dovezi este în `ro_products.json`. Rezumat:

| Categorie | Produs | Magazin | Preț | URL |
|---|---|---|---|---|
| Tapet | Grandeco Marmor A74801, 10,05×0,53 m (~5,3 mp) | Dedeman | 101,99 lei/rolă | [8123684](https://www.dedeman.ro/ro/tapet-vinil-model-marmura-grandeco-marmor-a74801-10-x-0-53-m/p/8123684) |
| Riflaj acustic MDF | Unic Spot alb 2400×600×21 | Dedeman | 299,00 lei/buc | [4026478](https://www.dedeman.ro/ro/riflaj-decorativ-tip-panou-acustic-mdf-unic-spot-alb-2400-x-600-x-21-mm/p/4026478) |
| Riflaj (categorie) | Set 376 stejar 2600×400×20 / Wood Class RS900 polimer | Dedeman | 219,00 / 62,90 lei | [c/4078](https://www.dedeman.ro/ro/riflaje-decorative-lemn/-polistiren/c/4078?page=1) |
| Cornișă polistiren | NMC NC109 200×13,5×10 cm | Dedeman | 168,00 lei/buc | [6037113](https://www.dedeman.ro/ro/cornisa-decorativa-polistiren-expandat-nc109-200-x-13-5-x-10-cm/p/6037113) |
| Gresie 60×120 rectificată | 2055 E-Marble Grey, mată, R10, PEI4, 1,44 mp/cutie | Dedeman | 54,90 lei/mp | [4030050](https://www.dedeman.ro/ro/gresie-exterior/-interior-portelanata-2055-e-marble-grey-60-x-120-cm-alb-mata-rectificata-aspect-marmura/p/4030050) |
| Gresie 60×120 lucioasă | Aden Light Grey, PEI2 (promo până 31.10.2026) | Dedeman | 94,89 → 84,90 lei/mp | [4028513](https://www.dedeman.ro/ro/gresie-exterior/-interior-portelanata-aden-light-grey-60-x-120-cm-gri-lucioasa-rectificata-aspect-marmura/p/4028513) |
| Tencuială decorativă | Kober Profesional siliconată 1,5 mm, 25 kg, 2–2,5 kg/mp | Dedeman | 165,00 lei/găleată (6,60 lei/kg) | [5019293](https://www.dedeman.ro/ro/tencuiala-decorativa-siliconata-kober-profesional-1-5-mm-structurata-aspect-bob-de-orez-gri-piatra-interior/-exterior-25-kg/p/5019293) |
| Gips-carton | Rigips RB 12,5×1200×2600 (3,12 mp) | Dedeman | 14,81 lei/mp; 46,20 lei/buc | [5006976](https://www.dedeman.ro/ro/placa-gips-carton-tip-a-rigips-rb-12-5-x-1200-x-2600-mm/p/5006976) |
| Vopsea superlavabilă | AplaLux 15 L + amorsă 4 L, mat, 12–16 mp/l | Dedeman | 447,89 lei | [5014504](https://www.dedeman.ro/ro/vopsea-superlavabila-interior-aplalux-alb-15-l-amorsa-4-l/p/5014504) |
| Bandă LED | Hoff SMD2835 12 V 24 W 2500 lm 3000 K 5 m, cu alimentator | Dedeman | 98,07 lei/pachet | [1070874-1048524](https://www.dedeman.ro/ro/banda-led-dublu-adeziva-pentru-interior-hoff-smd-2835-12-v-24-w-2500-lm-lumina-calda-cu-alimentator-arelux-5-m/p/1070874-1048524) |
| Spot LED încastrat | MT 143 9 W 905 lm 3000 K IP20 Ø75 | Dedeman | 85,32 lei/buc | [1081737](https://www.dedeman.ro/ro/spot-led-incastrat-mt-143-70385-9-w-lumina-calda-alb-mat/p/1081737) |
| Întrerupător | Schneider Asfora jaluzele EPH1300121 | Dedeman | 32,13 lei/buc | [1041324](https://www.dedeman.ro/ro/intrerupator-pentru-jaluzele-schneider-electric-asfora-eph1300121-incastrat-rama-inclusa-alb/p/1041324) |
| Calorifer decorativ | Radox Nova 420×1800, 937 W | Dedeman | 1846,00 lei | [2035188](https://www.dedeman.ro/ro/calorifer-vertical-decorativ-living-radox-nova-drept-turcoaz-420-x-1800-mm-accesorii-incluse/p/2035188) |
| Calorifer baie | Radox Serpentine 500×730, 275 W | Dedeman | 1382,00 lei | [2035202](https://www.dedeman.ro/ro/calorifer-vertical-decorativ-baie-radox-serpentine-portprosop-drept-turcoaz-500-x-730-mm-accesorii-incluse/p/2035202) |
| Baterie lavoar negru mat | Kludi Bozz 382863976 (indisponibil momentan) | Dedeman | 1319,00 lei | [3039712](https://www.dedeman.ro/ro/baterie-baie-pentru-lavoar-kludi-bozz-382863976-inalta-montaj-stativ-monocomanda-finisaj-negru-mat/p/3039712) |
| Cabină duș | Seldus EUR5 100×70×205, sticlă 5 mm, profil negru | Dedeman | 1440,02 lei | [3052040](https://www.dedeman.ro/ro/cabina-dus-dreptunghiulara-cadita-seldus-eur5-100-x-70-x-205-cm-sticla-securizata-transparenta-profil-negru/p/3052040) |
| Oglindă LED | Savini Due SPR101 60×80 / Class Mirrors D26 Ø50 ramă neagră | Dedeman | 561→436,46 / 707→590 lei | [c/66](https://www.dedeman.ro/ro/oglinzi/c/66) |
| Ușă interior | R80 Gol D3 stejar gri 86×202 cu toc (mânere separat) | Dedeman | 789,00 lei | [6022716](https://www.dedeman.ro/ro/usa-interior-celulara-cu-geam-r80-dreapta-gol-d3-stejar-gri-86-x-202-cm/p/6022716) |
| Corp bucătărie | Domino inferior 60×53×82, PAL 16 mm, blat neinclus | Dedeman | preț afișat doar după alegerea localității **[not found]** | [8101879](https://www.dedeman.ro/ro/corp-inferior-bucatarie-domino-gri-inchis-alb-60-x-53-x-82-cm-1c/p/8101879) |
| Covor | STOENSE fir scurt 200×300 / MORUM țesătură plată 160×230 / ÄRENDE fir lung 200×300 | IKEA RO | 699 / 299 / 399 lei | [cat/covoare](https://www.ikea.com/ro/ro/cat/covoare-10653/) |

Produse văzute doar în rezultate de căutare (pagina nu s-a putut citi — Hornbach returnează shell JS, JYSK/Leroy 404/absent): profil LED încastrabil 2 m Hornbach 82,90 lei; cornișă Decoflair WT6 53,79 lei; parchet Brera Clay 36,50 lei/mp; blat PAL 4100×600×38 530–539 lei; spot Eglo Pineda IP44 76,90–81,60 lei; piatră Klimex 114–147 lei/mp; draperii Hornbach 110–139 lei; JYSK/Leroy Merlin/Mobexpert/Ambient/Arabesque **[not found]** — vezi lista finală.

### 3.3 Manoperă (RON, 2025-2026)
Sursa principală accesibilă prin căutare a fost brig.ro (ghiduri „Cât costă … în 2026”); paginile au răspuns 403 la acces direct, deci cifrele sunt **citite din snippet-urile de căutare** și marcate ca atare. Daibau/Construct/Meseriasi/Neoma: **[not found]** în rezultate. Playtech 2025 (403 la acces direct): montaj gresie/faianță ~100 lei/mp, decopertare 35, parchet laminat 60–120 lei/mp ([playtech](https://playtech.ro/2025/renovarea-locuintei-in-2025-cat-costa-investitia-la-ce-preturi-sa-te-astepti-pentru-materiale-de-constructii-si-manopera/)). Hornbach servicii: laminat 37–40 lei/mp, SPC 43–45, +175 lei măsurători ([hornbach Sibiu](https://www.hornbach.ro/servicii/montaj-pardoseli-sibiu/)).

| Lucrare | Interval manoperă | Sursă |
|---|---|---|
| Parchet laminat click | 30–45 RON/mp (brig) / 37–40 (Hornbach) / 60–120 (Playtech) | [brig](https://brig.ro/cat-costa/pardoseala), [hornbach](https://www.hornbach.ro/servicii/montaj-pardoseli-sibiu/), [playtech](https://playtech.ro/2025/renovarea-locuintei-in-2025-cat-costa-investitia-la-ce-preturi-sa-te-astepti-pentru-materiale-de-constructii-si-manopera/) |
| Parchet masiv/stratificat lipit | 50–80; spic 80–150 RON/mp | [brig](https://brig.ro/cat-costa/pardoseala) |
| Gresie standard / diagonală / 60×120 | 63–85 / 75–130 / 100–150 RON/mp; faianță 80–120 | [brig](https://brig.ro/cat-costa/montaj-gresie) |
| Tapet vlies / vinil / textil / foto | 22–35 / 25–40 / 30–45 / 35–55 RON/mp; tavan 80–150 | [brig](https://brig.ro/cat-costa/montaj-tapet) |
| Tencuială decorativă / stucco venețian | 15–60 / ~125 RON/mp | [brig Barsa](https://brig.ro/tencuiala-decorativa/barsa), [hornbach](https://www.hornbach.ro/proiecte/cat-costa-aplicarea-tencuielii-decorative/) |
| Tavan rigips (manoperă) | 18–50 RON/mp | [brig](https://brig.ro/cat-costa/tavan-fals) |
| Scafă simplă / la cheie / luminoasă | 28–35 / 50–90 / 55–100 RON/ml (+LED 30–80) | [brig](https://brig.ro/cat-costa/scafa-tavan) |
| Baghete / cornișe / rozete | 10–20 / 15–30 RON/ml / 50–100 RON/buc | [brig](https://brig.ro/montaj-profile-decorative/bata) |
| Zugrăvit lavabilă 2 straturi / glet | 11–25 / 24–50 RON/mp | [brig](https://brig.ro/cat-costa/vopsitor) |
| Plintă PVC / MDF / duropolimer / lemn | 10–20 / 10–22 / 22–35 / 18–35 RON/ml | [brig](https://brig.ro/montaj-plinta/galati) |
| Mobilă bucătărie la comandă (cu montaj) | 730–1950 RON/ml; montaj simplu de la ~1000 RON | [brig](https://brig.ro/cat-costa/mobila-la-comanda) |
| Regional | București/Cluj +10–40 % | brig, playtech |

---

## 4. Reguli tehnice pe care aplicația ar trebui să le aplice/semnalizeze

| Regulă | Valoare | Sursă |
|---|---|---|
| Rost gresie rectificată | ≥1/8" (~3 mm) medie la laturi >15"; +deformarea muchiei; nerectificată ≥3/16" (~4,8 mm) | [CTF/ANSI A108.02](https://www.ceramictilefoundation.org/blog/want-credit-card-grout-joints-check-tile-industry-standards), [diytileguy](https://www.diytileguy.com/?p=7390) |
| Offset plăci mari | ≤33 % la laturi >18" (lippage) | [IMI](https://info.imiweb.org/blog/large-format-tile-dealing-with-lippage) |
| Antiderapare baie | R10 min, R11 în duș (DIN 51130); clasa B (DIN 51097) | [tilemountain](https://tilemountain.co.uk/blog/slip-ratings-explained), [delforno](https://www.delforno.ie/blogs/blog/tile-slip-ratings-explained-for-safer-floors) |
| Zone baie IEC 60364-7-701 | Zona 0 IPX7 + SELV; zona 1 ≥IPX4 (unele surse IPX5), 2,25 m; zona 2 bandă 0,6 m ≥IPX4 | [electrical-installation.org](https://www.electrical-installation.org/enwiki/Bathroom_electrical_installation), [vanmoofer](https://vanmoofer.com/wiresketch/learn/iec-60364-wet-room-zones/), [IEC](https://webstore.iec.ch/publication/28906) |
| Lux rezidențial | Living 100–300; bucătărie 300–750 (blat 400); dormitor 100–200; baie 200–300 (EN 12464-1 nu e rezidențial) | [ledyilighting](https://www.ledyilighting.com/residential-recommended-lighting-levels/), [vtac](https://www.vtacexports.com/poland/blog/post/recommended-lighting-levels-for-residential-and-office-spaces), [sonel](https://sonel.pl/en/knowledge-centre/press-articles/illuminance-measurements/en-12464-12021-key-lighting-requirements-for-indoor-work-places) |
| CCT / CRI | 2700 K dormitor; 2700–3000 K living; 3000–4000 K bucătărie/baie; CRI ≥90 bucătărie/baie | [psmlighting](https://psmlighting.be/en/news/blogs/which-color-temperature-suits-which-room), [studiomatrx](https://www.studiomatrx.org/students/interior-design-foundations/colour-temperature-cri-lighting) |
| Pendul peste masă/insulă | 76–91 cm deasupra suprafeței (8 ft tavan); +7,5 cm per 30 cm tavan în plus | [Studio McGee](https://studio-mcgee.com/light-fixture-hanging-heights-guide/), [hunker](https://www.hunker.com/13412812/how-high-should-you-hang-pendant-lights-above-a-kitchen-island) |
| Perdele | Fullness 1,5× / 2× / 2,5×; tiv ~1 cm deasupra pardoselii | [direct-fabrics](https://www.direct-fabrics.co.uk/understanding-curtain-fullness), [twopages](https://twopagescurtains.com/blogs/news/how-to-measure-for-curtains) |
| Covoare | Living: picioare față pe covor; dormitor 60–90 cm laterale; dining ≥60–75 cm dincolo de masă | [rugs.com](https://rugs.com/blog/what-size-rug-do-i-need-the-complete-rug-size-guide-for-every-room/), [hugrug](https://hugrug.co.uk/pages/rug-size-guide) |
| Blat bucătărie | 85–95 cm (uzual ~90–91); splashback/corpuri suspendate la 54–60 cm | [Egger](https://www.egger.com/en/blog/kitchen-worktop-height-guide?country=GB), [Valcucine](https://www.valcucine.com/en/?p=17517) |
| Mobilier lavoar / oglindă / aplice | 75–85 cm; centru oglindă 145–165 cm; aplice 152–168 cm | [worktophub](https://www.worktophub.co.uk/help-and-ideas/the-standard-worktop-height-a-practical-guide), [bathroommountain](https://www.bathroommountain.co.uk/inspiration-and-advice/bathroom-mirror-height), [edwardmartin](https://www.edwardmartin.com/blogs/information/how-high-should-bathroom-vanity-lights-be-hung-above-the-floor) |
| Mulaje vs tavan | Cornișă 2,5–6" la 8 ft; 7–12" la ≥10 ft; plintă ~6–7 % din H perete; chair rail 81–91 cm | [Angi](https://angi.com/articles/crown-molding-sizes.htm), [brainbound](https://www.brainbound.blog/molding-height-guide), [blockrenovation](https://www.blockrenovation.com/guides/wall-moulding-design-ideas-and-different-types?hsLang=en) |
| Luciu vopsea | Mat tavan; eggshell living/dormitor; satin bucătărie; satin/semi-lucios baie; semi-lucios tâmplărie | [houselogic](https://houselogic.com/remodel/paint-sheen-guide/), [360painting](https://www.360painting.com/blog/types-of-paint/paint-finishes/the-complete-paint-sheen-guide-choosing-the-righ/) |
| Tapet | Rolă 0,53×10,05 m (~5,3 mp; confirmat pe Dedeman); drop = H + repeat; pierderi 10 % (≤26 cm) / 15–20 % (>26 cm) | [James Dunlop](https://showroom.jamesdunloptextiles.com/journal/tips-how-to/how-to-calculate-wallpaper-requirements), [Dedeman 8123684](https://www.dedeman.ro/ro/tapet-vinil-model-marmura-grandeco-marmor-a74801-10-x-0-53-m/p/8123684) |
| Laminat | Rost dilatare ≥10 mm (unii 15 mm); direcție paralelă cu lumina principală; UFH: suprafață ≤27 °C (AGT ≤25 °C), R termic ≤0,15 m²K/W, folie ≥0,2 mm | [Sisu](https://a.storyblok.com/f/162000/x/a57db4a720/sisu-laminate-installation-guide-v1-2-web.pdf), [Nu-Heat](https://www.nu-heat.co.uk/underfloor-heating/floor-coverings/laminate-flooring-with-underfloor-heating/), [Quick-Step](https://www.quick-step.co.uk/en-GB/frequently-asked-questions/laminate/where-in-the-room-should-i-begin-to-install-the-floor), [BerryAlloc](https://dxm.mediacenter.bintg.com/api/wedia/dam/variation/9kcwb57gfjr6bj7g5ucq85bdmpwz8j15ijnez5o/original) |
| Pierderi plăci/parchet | 10 % drept; 12–18 % diagonal; 15–20 % herringbone/chevron | [flooringclarity](https://www.flooringclarity.com/how-much-extra-tile-herringbone-pattern/), [wdtegels](https://wdtegels.com/en/blogs/kennisbank/hoeveel-tegels-nodig-berekenen) |
| Plintă MDF în baie | Nerecomandată (se umflă) → duropolimer/PVC | [brig](https://brig.ro/montaj-plinta/galati) |

Normativ I7/2011 (RO) pentru zone baie: **[not found]** în căutare; a se verifica textul oficial.

---

## 5. Livrabile profesionale

Ce predă un designer (RO/UE) — sinteză din surse: etape documentare → concept (moodboard, schițe) → desen tehnic → implementare ([ULIM curs](https://www.ulim.md/wp-content/uploads/2019/12/Atelier_design_interior_3.pdf)); după aprobarea moodboard-ului clientul primește estimare de cost ([internityhome](https://internityhome.pl/home/)); set de execuție: plan mobilier, plan materiale, plan cote, plan pardoseli, plan tavan (reflected ceiling), plan iluminat, plan prize/întrerupătoare, elevații pereți, coduri de vopsea, nume locale de produse, DWG+PDF ([rayon.design](https://rayon.design/blog/technical-plans-5-examples-for-interior-design-and-architecture), [freelancehunt](https://freelancehunt.com/en/showcase/work/working-project-apartment/2038526.html)); FF&E schedule cu ID articol, cameră, referință desen, brand/model/finisaj/culoare/dimensiuni, cost unitar, furnizor, buget ([programa.design](https://programa.design/blog/ffe-schedule-template-guide), [mastt](https://www.mastt.com/resources/ffe-schedule)). Termenul „fișă de finisaje” nu are un standard oficial RO găsit **[not found]**.

Set recomandat pentru Casa3D:
1. Concept + moodboard (randări + paletă + materiale).
2. **Fișă de finisaje** (per cameră).
3. Plan pardoseli cu direcție/pattern/punct start.
4. Plan tavan (reflected ceiling) cu cote de înălțime, scafe, corpuri.
5. Plan iluminat + plan electric (prize/întrerupătoare, înălțimi, circuite).
6. Desene tâmplărie la comandă (elevații, secțiuni).
7. Plan placări baie/bucătărie (tile layout, nișe, start).
8. Deviz (materiale cu pierderi + manoperă + TVA).
9. Set randări.
10. Listă de cumpărături cu linkuri și coduri.

**Șablon fișă de finisaje (per cameră):**

| Cameră | Element | Produs | Furnizor | Cod | Culoare/finisaj | Dimensiune | Cantitate | Unitate | Preț unitar | Total | Pierderi % | Manoperă | Note |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Living | Pardoseală | Gresie 2055 E-Marble Grey | Dedeman | 4030050 | alb mat, rectificat | 60×120 | 24,2 | mp | 54,90 | 1328,58 | 10 | 100–150 RON/mp | rost 3 mm, offset 1/3 |

---

## 6. Recomandare: roadmap prioritizat pentru Casa3D

Constrângeri respectate: fără apeluri AI plătite; prețuri doar cu URL+dată; texturi doar procedurale sau CC0 (Poly Haven CC0 — [polyhaven.com/license](https://polyhaven.com/license); ambientCG CC0 — [ambientcg.com](https://ambientcg.com/)).

### P0 (fundație profesională)
| # | Ce | Tip schimbare | Efort | De ce |
|---|---|---|---|---|
| 1 | **Model de date „Finish” generic**: {element, produs, furnizor, cod, url, preț, unitate, dată verificare, pierderi %, manoperă ref} pentru orice suprafață/obiect, nu doar RoomFinishes | Data model | M | Baza pentru fișa de finisaje, BOQ, shopping list; competitorii cu BOQ (Chief Architect, Planner 5D) au exact acest lucru |
| 2 | **Pattern engine pardoseală/placări**: dimensiune placă, rost (mm+culoare), offset (0/⅓/½/random), rotație (0/45/90), herringbone/chevron, punct de start, direcție | 3D (procedural, shader three.js + Blender node group) + UI | L | Cel mai vizibil gol față de Coohom; pierderile depind de pattern (10/15/20 %) |
| 3 | **Pereți: tapet, riflaj, tencuială decorativă, piatră/cărămidă** ca straturi pe față de perete (extinde „accent per face”) | Data model + 3D + BOQ | M | Cele mai cerute finisaje de designer în RO (vezi oferta Dedeman/Hornbach) |
| 4 | **Tavan fals + scafă LED + cornișă + plintă** (geometrie procedurală din perimetru) | 3D + BOQ (mp/ml) | M | Tarife ml disponibile (brig); Homestyler/Coohom au editor de tavan |
| 5 | **BOQ cu pierderi și manoperă pe pattern** + export fișă de finisaje (CSV/PDF) | BOQ + UI | S | „Deviz” este livrabilul de bază |
| 6 | **Validatoare**: R-rating în baie, IP în zonele 0/1/2, rost min. rectificat, MDF în baie, UFH ≤27 °C | Logică + UI avertizări | S | Diferențiator „profesional” fără cost |

### P1 (iluminat, bucătărie, baie)
| # | Ce | Tip | Efort | De ce |
|---|---|---|---|---|
| 7 | **Lighting model**: corp {tip, W, lm, CCT, CRI, IP, dimabil, unghi}, straturi, calcul lux aproximativ pe cameră (lumen method), presetări CCT per cameră | Data + 3D (emissive + point/spot three.js; Blender light nodes, IES opțional) + UI | L | Cedreo/Enscape expun CCT/IES; lux/CCT sunt reguli clare |
| 8 | **Bucătărie ca sistem**: fronturi (material/stil/culoare), mânere, blat (material/grosime/muchie), splashback, chiuvetă/baterie finisaj, electrocasnice | Data + 3D + BOQ (ml/mp) | L | 2020 Design/Coohom; RO: Dedeman Domino, IKEA |
| 9 | **Baie ca sistem**: cabină duș (walk-in 8 mm), finisaj baterii, oglindă LED IP44, nișe, hidroizolație mp, calorifer portprosop | Data + 3D + BOQ | M | Produse verificate Dedeman |
| 10 | **Uși/ferestre**: stil foaie, toc, pervaz, mâner finisaj; glaf | Data + 3D | S/M | Lipsesc complet azi |
| 11 | **Textile & covoare** cu reguli de dimensionare (fullness, lungime, covor vs mobilier) | Data + 3D (plane cu textură CC0) + validator | M | Reguli clare; IKEA RO prețuri verificabile |
| 12 | **Finisaje electrice & HVAC vizibil** (prize/întrerupătoare gamă+culoare, calorifere) în plan electric | Data + UI plan 2D | M | Plan electric = livrabil standard |

### P2 (livrabile & bibliotecă)
| # | Ce | Tip | Efort | De ce |
|---|---|---|---|---|
| 13 | Plan tavan (RCP), plan pardoseli cu direcție, plan placări (tile layout) exportabile PDF/SVG | UI/export | M | Set de execuție |
| 14 | Moodboard automat (randări + swatch-uri + listă) și shopping list cu linkuri | UI | S | Prezentare client |
| 15 | Bibliotecă materiale CC0 PBR (vezi mai jos) cu metadata (scară reală, roughness) și editor material | 3D | M | Realism Cycles/three.js |
| 16 | Tâmplărie la comandă parametrică (dressing, nișă TV) | 3D + BOQ | L | Diferențiator avansat |
| 17 | Decor (artă, plante, accesorii) din modele CC0 (Poly Haven models) | 3D | S | Prezentare |

### Texturi CC0 concrete (toate CC0; verificate pe pagină/API)
| Finisaj | Set | URL |
|---|---|---|
| Parchet herringbone | Poly Haven `herringbone_parquet`, `diagonal_parquet`, `rectangular_parquet` | [api wood](https://api.polyhaven.com/assets?t=textures&c=wood) |
| Parchet/planks | Poly Haven `wood_floor_deck` (1–8K, diffuse/normal/rough/disp/AO) ([pagina](https://polyhaven.com/a/wood_floor_deck)); `plank_flooring_04`, `oak_wood_planks`; ambientCG `WoodFloor051` (parquet, polished, 1–8K) ([pagina](https://ambientcg.com/view?id=WoodFloor051)), `WoodFloor040` ([pagina](https://ambientcg.com/view?id=WoodFloor040)) | idem |
| Laminat | Poly Haven `laminate_floor`, `laminate_floor_02`, `laminate_floor_03` | api wood |
| Furnir fronturi | Poly Haven `oak_veneer_01..05`, `walnut_veneer`, `black_oak_veneer`, `ash_veneer` | api wood |
| Gresie/marmură | ambientCG `Tiles074` (marble tiles) ([pagina](https://ambientcg.com/view?id=Tiles074)), `Marble012` (white veined, kitchen/bath) ([pagina](https://ambientcg.com/view?id=Marble012)); Poly Haven `floor_tiles_02/04/06`, `interior_tiles`, `terrazzo_tiles`, `terracotta_floor_tiles`, `grey_cartago_01` | [api floor](https://api.polyhaven.com/assets?t=textures&c=floor), [api tiles](https://api.polyhaven.com/assets?t=textures&c=tiles) |
| Tencuială/stucco/beton aparent | ambientCG `Plaster001` (white stucco, matte) ([pagina](https://ambientcg.com/view?id=Plaster001)), `Concrete034` (smooth light grey) ([pagina](https://ambientcg.com/view?id=Concrete034)); Poly Haven `white_stucco`, `beige_wall_001`, `concrete_wall_001` | [api plaster](https://api.polyhaven.com/assets?t=textures&c=plaster) |
| Cărămidă aparentă | ambientCG `Bricks052` (photogrammetry, red) ([pagina](https://ambientcg.com/view?id=Bricks052)); Poly Haven `brick_wall_001`, `painted_brick`, `brick_wall_10` | [api wall](https://api.polyhaven.com/assets?t=textures&c=wall) |
| Textile (canapea, perdele) | Poly Haven `velour_velvet`, `rough_linen`, `wool_boucle`, `crepe_georgette` (sheer), `ribbed_corduroy`; ambientCG `Fabric030` ([pagina](https://ambientcg.com/view?id=Fabric030)) | [api fabric](https://api.polyhaven.com/assets?t=textures&c=fabric) |
| Piele | Poly Haven `brown_leather`, `leather_white`; ambientCG `Leather026` (black) ([pagina](https://ambientcg.com/view?id=Leather026)) | idem |
| Covor | ambientCG `Carpet013` ([pagina](https://ambientcg.com/view?id=Carpet013)); Poly Haven `dirty_carpet` (uzat) | idem |
| Metal (baterii, mânere) | Poly Haven nu are brushed steel/brass curat în categorie (doar vopsit/ruginit) → PBR procedural (metalness 1, roughness 0,2–0,4; alamă RGB ≈ (0,8, 0,6, 0,3)) | [api metal](https://api.polyhaven.com/assets?t=textures&c=metal) |
| Tapet | ambientCG `Wallpaper001` **nu există** (404) → procedural (repeat model + normal map plaster) | — |

---

## Unverified / not found
- Coohom: pagina oficială `/features` 404; IES/Kelvin în help center not found; herringbone preset not found.
- Homestyler: tool de pavaj cu rost/herringbone not found; IES not found; BOM doar din recenzie TrustRadius.
- Planner 5D: control rost/pattern not found; mapare scară textură → cm not found.
- Foyr Neo: BOQ not found. Cedreo: tile layout, mulaje, deviz not found. RoomSketcher: control pattern not found. Live Home 3D: mulaje/costuri not found. Floorplanner: pattern pardoseală not found. HomeByMe: shopping list parțial (note APK). IKEA Kreativ: pagina customer-service 404; schimbare finisaje pereți not found.
- Hornbach: paginile de produs nu se încarcă fără JS („A required part of this site couldn't load”) — toate prețurile Hornbach sunt din snippet-uri de căutare, neverificate pe pagină.
- brig.ro și playtech.ro: 403 la acces direct; cifrele de manoperă sunt din snippet-uri de căutare. Daibau, Construct.ro, Meseriasi, Neoma: not found în rezultate.
- Leroy Merlin RO, JYSK RO, Mobexpert, Ambient, Arabesque, Brico Depot, eMAG: nicio pagină de produs citită (404/absent în căutare).
- Dedeman: corp bucătărie Domino — preț afișat doar după selectarea localității; blat bucătărie Dedeman not found; spot IP44 Dedeman not found (doar IP20); bandă LED 2700 K not found (3000 K găsită); plintă MDF Dedeman not found; piatră decorativă Dedeman not found.
- Normativ I7/2011 (zone baie RO): not found; folosit IEC 60364-7-701 prin surse secundare; valoarea IP pentru zona 1 diferă între surse (IPX4 vs IPX5).
- EN 12464-1: acoperă locuri de muncă; valorile lux rezidențiale provin din ghiduri de producător, nu din standard.
- Înălțime oglindă cu marginea de jos la 100–110 cm: not found (doar centru 145–165 cm).
- Egger recomandare direcție laminat: not found (folosit Quick-Step).
- ambientCG `Wallpaper001`: 404 (nu există).
