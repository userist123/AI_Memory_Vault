---
id: "d8a68dd6-7976-4cee-a92b-2207b8e04603"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, design]
created: "2026-10-11"
updated: "2026-10-11"
provenance:
  source_type: web
  source_ref: "Web research by a Claude subagent on 2026-10-11; every source URL is cited inline in the body"
  source_date: "2026-10-11"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
relations:
  - type: related_to
    target_id: "3e458b8c-e0f1-4b06-b0d7-9a3ffae2ae1f"
---
# Casa3D: what designers and homeowners say design, space-planning and visualisation tools lack (research, 2026-10-11)

Scope: drawing, survey, kitchens and bathrooms, technical plans, joinery, visualisation, colour, import/export, mobile. This report builds on `02_PRODUCT/projects/Casa3D/GAP_ANALYSIS_2026-10-11.md`, which covered business and studio workflow. It goes further by checking Casa3D's code for each need (`02_PRODUCT/projects/workspaces/casa3d/app/core/*`).

## How the evidence was gathered (read this first)

- **Reddit was not reachable.** WebFetch refuses reddit.com, and curl gets Reddit's bot-block page. Search engines also returned no r/InteriorDesign, r/Kitchens or r/HomeImprovement threads. **This report contains no Reddit quotes.**
- **Capterra, GetApp and Softpedia (RO) returned HTTP 403 to direct fetch.** Quotes from those sites, marked **[search extract]**, come from the search engine's extract of the page, not from a page I opened. Treat them as unverified wording.
- **[fetched]** means I opened the page and the quote comes from it.
- **Romanian sources are thin.** I found no accessible RO forum or Facebook-group discussion of design tools. RO findings are therefore limited to:
  - profiles of RO designers listing their tools;
  - a dated Softpedia thread on PRO100 (search extract);
  - RO supplier pages for PAL;
  - RO survey-service ("releveu") pages.

  Anything about RO practice beyond those is marked **(inference)**.
- **Casa3D status** uses the feature list from the brief and a grep of the code. Code-confirmed items cite the module. "Not seen in code" means my grep found nothing; it does not prove the feature is absent.

---

## 1. Needs and pain points, ranked

Ranking is by how often a need appears across independent sources and how much it costs users (rework, wrong orders). Strength: **High** = 4 or more independent sources or tools; **Med** = 2–3; **Low** = 1, or vendor claims only.

### 1. Dimensioning and drawing for trades is slow and fiddly. Users want precise walls and fast, clean dimensions. (High)
- **What users say:**
  - Professional drawing sets take too long to polish.
  - Dimensioning slows to a crawl.
  - Wall edits break joins.
  - Drafting tools inside visualisation-first tools are weak.
- **Evidence:**
  - SketchUp forum, LayOut: an architecture firm says the workflow "is dramatically slowed down when entering the dimensioning and labeling phase" and that "LayOut seems very sluggish". Another user: "if you have a large sketchup file you can expect to spend at least half your time in layout waiting for the viewports to update". https://forums.sketchup.com/t/layout-too-slow-to-use/119318?page=5 · https://forums.sketchup.com/t/layout-taking-up-to-15-minutes-to-process-simple-moves/138709 [search extract]
  - "All functions that speed design are gone!" (LayOut '24/'25 regressions). https://forums.sketchup.com/t/i-hate-layout-it-is-very-very-bad/340318?page=4 [fetched]
  - 2020 Design Live (Capterra): dimensioning "needs constant adjustment"; drawings to professional standard take "a lot of timely finessing". https://capterra.com/p/218525/2020-Design-Live/reviews/ [search extract]
  - Coohom (Capterra): "Drafting in coohom SUCKS." https://capterra.com/p/192882/Coohom/reviews/ [search extract]
  - Live Home 3D (Capterra/GetApp): in 2D "adjusting walls can sometimes cause the joins to get a bit funky and thus a lot of rework". https://www.capterra.com/p/166944/Live-Home-3D [search extract]
- **Who does it well:**
  - Chief Architect: a SketchUp user says people doing working drawings in it were "much more efficiently" served. Forum, [search extract].
  - AutoCAD and Vectorworks for 2D.
- **Casa3D: PARTIAL.**
  - Has: the 2D editor with a dimension style, a dimensioned survey plan and DXF output (`core/dimensions.ts`, `core/dxf.ts`).
  - Not verified: typed-length wall entry, angle entry, and how quickly a full annotated set can be produced.

### 2. On-site survey accuracy. Phone scans are not precise enough, a laser measure fixes it, and plans must stay editable. (High)
- **What users say:**
  - LiDAR and AR scans are off by centimetres to decimetres, especially in cluttered rooms.
  - Professionals pair the app with a Bluetooth laser measure.
  - Users need to edit walls after a scan.
- **Evidence:**
  - A UK surveying firm's test: MagicPlan LiDAR-only errors averaged "within 150mm for the large test room"; with a laser disto, accuracy was "spot-on". https://mavricresearch.com/2021/02/02/more-lidar-contenders-how-do-they-measure-up%f0%9f%93%8f/ [fetched]
  - magicplan reviews:
    - "Routinely off by 4-6"" (warned against using it for cabinet orders).
    - "The LIDAR can not tell the difference between a corner of a room vs the corner of a cabinet".
    - "you can't edit the placement of the walls or edges".
    - "The DXF file export does not open at full scale 1:1 on CAD".

    https://justuseapp.com/en/app/427424432/magicplan/reviews [fetched, aggregator of App Store reviews]
  - App Store: "if I try to scan more than a few rooms in a single session, it would lose it position". https://apps.apple.com/us/app/magicplan/id427424432?see-all=reviews [fetched]
  - RoomPlan: "It seems to often work to 15cm" (Apple developer forum, anecdotal). https://developer.apple.com/forums/thread/712500 [fetched]
- **Who does it well:** magicplan or RoomScan combined with a Bluetooth disto.
- **RO context:** RO survey providers deliver the measured plan ("releveu") as DWG/PDF from on-site measurements (https://brig.ro/releveu/deva, [search extract]). Bosch RO pitches its MeasureOn app because copying laser readings by hand risks transcription errors (https://www.bosch-professional.com/ro/ro/stiri-blog/measureon/, [search extract]).
- **Casa3D: PARTIAL.**
  - Has: plan-from-image import and underlay scale calibration (`core/underlay.ts`).
  - Missing: an on-site **survey mode** for entering measurements and checking how well the walls close.
  - Missing: laser-meter input.
  - Missing: import of RoomPlan or magicplan output.

### 3. Kitchen specifics: cabinet runs, worktops, appliance clearances, priced cabinet catalogs. (High)
- **What users say:**
  - Kitchen customisation is limited.
  - Users cannot price cabinets from the catalog.
  - The worktop logic is poor.
  - The plan and the order drift apart.
- **Evidence:**
  - Coohom (Capterra): "The kitchen customisation is very limited. Unable to add and upload my own materials in the kitchen designs". [search extract] https://capterra.com/p/192882/Coohom/reviews/
  - Chief Architect (GetApp): "the ability to load a catalog 'with pricing' would be great. But I use a spread sheet for that anyway". [search extract] https://www.getapp.co.uk/reviews/2059753/chief-architect
  - Chief Architect (Capterra): a kitchen and bath designer cites a high price, support "up to $525" a year, and "ridiculously expensive" training. [search extract] https://www.capterra.com/p/80035/Chief-Architect/reviews/?page=2
  - RoomSketcher (Capterra): "customers love seeing handles on their kitchen cabinets" but each handle has to be placed manually. [search extract] https://capterra.com/p/164021/RoomSketcher/reviews/
  - IKEA planner (forums and reviews):
    - The order didn't match the planner.
    - The planner doesn't support custom-cut worktops (PERSONLIG).
    - "go get a fitter to check measurements".

    https://www.hausbau-forum.de/en/threads/ikea-personal-worktop-in-the-online-planner.20964 · https://www.mumsnet.com/Talk/property/4470850-IKEA-kitchen-lessons-learnt [search extract]
  - Houzz homeowner thread on clearance rules: the dishwasher "should not be directly next to the sink" with a 45° sink; leave "6-12 in of space". https://www.houzz.com/discussions/3303417/please-help-need-help-advice-and-ideas-regarding-kitchen [fetched]
  - Clearance rules widely quoted (NKBA via secondary sources):
    - Work aisle 42"/48" for one or two cooks.
    - Walkway 36".
    - Work-triangle legs 4–9 ft, total 26 ft or less.
    - 15" refrigerator landing area.

    https://www.thewcsupply.com/pages/kitchen-design-guidelines-standard-clearances · https://kb.nkba.org/uploads/2023/05/2023-DC-Guidelines-1.pdf [search extract; NKBA PDF not opened]
- **Who does it well:**
  - 2020 Design: manufacturer catalogs, "relatively accurate specifications".
  - Chief Architect.
  - PRO100 (RO furniture makers).
- **Casa3D: PARTIAL.**
  - `core/kitchen.ts` treats the kitchen as a system: fronts, handles, uppers, worktop and its thickness, backsplash, sink, LED.
  - It has `kitchenRun()`, which gives run length and number of 60 cm modules.
  - `kitchenIssues()` checks only finish consistency (no tile, LED without uppers, shallow worktop, gloss without handles).
  - **Missing:**
    - per-module layout and elevation;
    - appliance positions;
    - aisle, landing-zone and dishwasher–sink checks;
    - worktop pieces and joints.

### 4. Construction drawing sets: title blocks, sheet consistency, PDF that trades can use. (Med-High)
- **Evidence:**
  - LayOut slowdowns traced to "the page borders and titleblock frames". https://forums.sketchup.com/t/layout-is-slow-for-some-but-not-all-my-files/276647 [search extract]
  - Cedreo (Capterra): an interior designer wants "a way to create a design proposal in one document based off of the current renderings and blue print", plus "CAD format for Blueprints" (GetApp). https://www.capterra.com/p/177284/Cedreo/reviews · https://www.getapp.ca/software/114007/cedreo [search extract]
  - magicplan: a reviewer says feet-and-inches dimensions show on screen but are missing from the printed PDF. [fetched, justuseapp]
  - Insight Design Studio: "Exporting to PDF can be tricky". https://www.spacesbydee.com/the-best-and-worst-interior-design-software/ [fetched]
  - Homeowner who works with designers: "We got our drawings from our designer ... but they were quite a mess", and revisions were hard to track. https://www.cs.yale.edu/homes/shah/interiors/electrical.html [fetched]
- **Casa3D: PARTIAL.**
  - Has: technical sheets, DXF, the A4 print/PDF page (`components/PrintView.tsx`), revision diff.
  - Not seen in code: a **title block** (studio, client, address, sheet no., scale, date, revision letter), a scale bar or north arrow, or one numbered sheet set.

### 5. Electrical and plumbing points with heights, frozen before first fix. (Med)
- **Evidence:**
  - Yale homeowner: "Your layout design should be frozen before you start the electrical work"; electricians "had to drill into the walls multiple times"; you must decide switch "height from the floor"; work across trades "can fall between the cracks". [fetched] https://www.cs.yale.edu/homes/shah/interiors/electrical.html
  - Jung (manufacturer): sockets in living rooms "30 cm above the floor"; switches near doors "105 cm"; kitchen "115 cm above the floor" or "20 cm above the worktop". [fetched] https://www.jung-group.com/en-UK/Switches-sockets-and-more/46835
  - Rayon: a socket and switch plan is a standard interior deliverable, and outlet heights should be dimensioned. https://rayon.design/blog/technical-plans-5-examples-for-interior-design-and-architecture [search extract]
- **Casa3D: HAS (mostly).** `core/technical.ts` covers:
  - socket, double socket, switch, light, data, cooker, cold and hot water, and drain points;
  - default heights (sockets 0.3 m, switches 1.1 m);
  - deterministic suggestions from furniture and room type;
  - counts per room;
  - DXF symbols.

  **Not verified:** that points are dimensioned from a wall corner on the printed sheet, and a heights legend. This could be a quick win.

### 6. Tile setting-out: where to start, avoiding slivers, cut sizes, wall elevations. (Med)
- **Evidence:**
  - Tilers' rules:
    - avoid cuts "less than a third of the tile size";
    - find the centre and decide whether a full tile or a split tile falls on the line;
    - "ping lines in both x and y axis and calculate all cuts".

    https://www.tilersforums.com/threads/setting-out-large-floor-help.4085 · https://tilersforums.com/threads/advice-for-setting-out.66746 [search extract]
  - Fine Homebuilding: a good layout places cut tiles so factory edges show in prominent spots and there are "no out-of-place-looking narrow cut tiles". https://www.finehomebuilding.com/project-guides/tiling/a-few-words-on-planning-and-layout [search extract]
  - magicplan App Store: "good luck adding tile or wainscot to the walls"; the user asks for editable tile size. [fetched]
  - Cedreo (Capterra): wants "more backsplash, wainscoting, and wall finish options". [search extract]
  - The only setting-out tools found are standalone planners: Layometry, TileFit (0 ratings), utils tile-layout. https://tile-layout.utils.com/ [search extract]
- **Casa3D: PARTIAL.**
  - `core/finishes.ts` has floor patterns (straight, brick, third, checker, diagonal, herringbone, chevron), grout mm and colour, and waste % by pattern and format.
  - `core/bath.ts` has the bathroom wall-tile height.
  - The floor drawing shows pattern, piece size and grout.
  - **Missing:** start point, cut-size calculation, sliver warnings, and a tile elevation with the actual joint grid.

### 7. Custom joinery and wardrobes with a cut list. Strong in RO, where PAL is the norm. (Med)
- **Evidence:**
  - SketchUp forum, asking for kitchen, wardrobe and vanity cutlists plus 2D drawings: "there is not one plugin to do all of what you need"; automatic 2D is unlikely, "Too much variation". https://forums.sketchup.com/t/best-plugin-for-cabinetry-wood-processing-cutlist-2d-drawings/198641 [fetched]
  - OpenCutList and CutList don't run in SketchUp web. https://forums.sketchup.com/t/is-open-cut-list-available-for-sketchup-free-and-go-or-is-there-another-program-to-export-to/247758 [search extract]
  - RO: a Softpedia thread (dated, 2008) describes PRO100 giving a cutting list of panels per carcass, with an adjustable edge tolerance. Blum RO lists PRO100 with CUT100. https://www.blum.com/md/ro/services/industrial-production/cad-cam-interface/software-partners/pro100/ [search extract]
  - Hornbach RO describes PAL as the standard fit-out material, sold in 2800×2070×18 mm sheets, with cutting configurators. https://www.hornbach.ro/c/constructii-lemn-ferestre-usi/constructie-mobilier/placi-pal/S14781 [search extract]
  - Coohom: modelling is weaker than SketchUp, which makes custom pieces hard. [search extract]
- **Casa3D: MISSING.** Wardrobes exist only as catalog items placed by layout rules (`twin-core/src/designer.ts`). There is no configurator and no cut list. The earlier gap note already listed this as S priority.

### 8. Catalog and library breadth, custom items, own materials. (High frequency, but already covered)
- **Evidence:**
  - Planner 5D: "the library's furniture and decor selection restricted" designs; users want to add custom furniture. [search extract] https://capterra.com/p/164022/Planner-5D/reviews/
  - Coohom: "Need more 3d model in library especially for brands". [search extract]
  - RoomSketcher: "The limited library. Lacks advance lighting features". [search extract]
  - Cedreo: "additional library items". [search extract]
- **Casa3D: HAS.** It has a verified RO shop catalog and custom items (`lib/custom-items.ts`). Uploading your own texture is not verified.

### 9. Material realism and render consistency, without paywalls or credits. (Med)
- **Evidence:**
  - 2020 Design: rendering "doesn't always deliver the level of realism clients expect". [search extract]
  - RoomSketcher: "advanced lighting and materials options are reserved for paid tiers"; "Credit System is designed to drive the cost up". [search extract]
  - DesignFiles: "The 3D rendering can lag at times". [fetched, spacesbydee]
  - Homestyler: high-res renders cost extra. [search extract]
- **Casa3D: HAS.** PBR materials, day/night and Blender Cycles export. There are no credits, and that is worth keeping as a selling point.

### 10. Clients understanding the space: walkthroughs and 360 panoramas. (Med; mostly vendor-sourced)
- **Evidence:**
  - ArchDaily (sponsored): 2D plans, renders and videos "lack one important thing: immersion". https://www.archdaily.com/1014132/the-complete-guide-to-360-panoramas-for-immersive-client-presentations [search extract, vendor]
  - pCon tutorial: clients move through panoramas "to get a feel for proportions". [search extract, vendor]
  - Live Home 3D: the walkthrough is hard to steer; "Getting the angle right ... takes more effort than you'd think". [search extract]
- **Casa3D: HAS.** It has a 3D view with phone joystick and pinch, plus `components/PanoramaView.tsx` and `panorama-engine.js` (a 360 viewer with download). Whether panoramas are embedded in the client link was not verified.

### 11. Furniture clearances and ergonomics checks. (Med; standalone calculators exist, rarely built into planners)
- **Evidence:**
  - Livingetc (spatial designer): "keep walkways at least 90cm wide"; "around 45cm of space around coffee tables". https://livingetc.com/advice/how-much-space-between-furniture-for-walking [fetched]
  - Standalone clearance and walkway calculators exist, which suggests the need isn't met inside planners (inference). https://www.diysmarthomehub.com/?p=18828 [search extract]
- **Casa3D: HAS.** `core/advisor.ts` and `core/rules.ts` check:
  - front clearances (wardrobe 0.9 m, bed side 0.6 m, sofa to table 35–60 cm);
  - dining clearances;
  - an accessible mode at 1.2 m;
  - TV distance;
  - tall items in front of windows.

  Kitchen aisle checks are the gap; see item 3.

### 12. Lighting design and lux, without DIALux's learning curve. (Med)
- **Evidence:**
  - DIALux "has a very steep learning curve if you aren't already pretty familiar with 3D Cad". https://forums.mikeholt.com/threads/lighting-softwares-working.2558495 [search extract]
  - RoomSketcher: "Lacks advance lighting features". [search extract]
- **Casa3D: HAS.** `core/light-design.ts` estimates lux per room by the lumen method, with target bands (living 100–300 lx, kitchen 300–750) and CCT. The ceiling and lighting sheet shows lux and flags rooms below target.

### 13. Colour: real paint codes (RAL/NCS/brand), and screen colour ≠ real colour. (Med-Low)
- **Evidence:**
  - Teknos (paint maker, lists NCS equivalents): on-screen colours are renderings, not a replacement for physical samples; finish, gloss and lighting change perception. https://www.teknos.com/decorative-paints/colours/interior-colours/t1505/ [search extract]
  - Paint consultancy: visualisers are best for ruling colours out; order physical samples. https://paintcolorconsultants.com/paint-color-visualizer [search extract]
  - Canva has "no paint color library", so designers work around it with hex codes (via mood-board roundup). https://www.woodworkingnetwork.com/design/7-tools-interior-designers-use-create-mood-boards [search extract]
- **Casa3D: PARTIAL.** It uses hex colours and a WCAG-based colour advisor (`core/color.ts`). No RAL/NCS code appeared in the code.

### 14. Mood boards connected to the real spec, budget and plan. (Med)
- **Evidence:**
  - The client approves the board, "and then it sits in a separate app from the FF&E schedule, the sourcing list, the budget, and the floor plan". https://storyflow.so/blog/best-mood-board-tools-interior-designers-2026 [fetched]
- **Casa3D: HAS.** The moodboard sits inside the client presentation (`core/presentation.ts`) next to finish swatches, products and budget. This is a strength to keep.

### 15. Import and export interoperability: DWG/DXF at true scale, SKP, OBJ, GLB. (Med)
- **Evidence:**
  - magicplan DXF not at 1:1. [fetched]
  - "sketchup won't import a dwg file complete with text and dimensions". https://community.sketchucation.com/post/1099151 [search extract]
  - Coohom: "I can't download 3D file from coohom"; cannot upload more than one DWG; SKP over 30,000 mm is rejected. https://www.coohom.com/helpcenter/3FO4K4WKW90N.md [search extract]
  - Live Home 3D: export to 3ds Max crashes; OBJ "take forever to load in Max". [search extract]
- **RO context:**
  - RO designers and visualisers mostly list 3ds Max + Corona/V-Ray, AutoCAD/ArchiCAD and SketchUp. https://cgarchitect.com/members/stancubogdan-a · https://www.upwork.com/hire/interior-designers/ro/ [search extract]
  - So handing a model off to 3ds Max or SketchUp, and receiving DWG from architects, are likely real needs (inference).
- **Casa3D: PARTIAL.**
  - Has: DXF export, plan-from-image import.
  - Not seen in code: **DXF import** and **GLB/OBJ export**. A GLB only appears in the Blender render path.

### 16. Performance on large projects. (Med)
- **Evidence:**
  - LayOut: a dimension took up to 15 minutes to process. [search extract]
  - Homestyler "can lag with large projects". https://www.unite.ai/homestyler-review [search extract]
  - Home Designer "processor hungry". https://www.techradar.com/reviews/chief-architect-home-designer-suite-2022 [search extract]
  - RoomSketcher "rendering large projects can take a bit longer". [search extract]
- **Casa3D: UNKNOWN.** Not measured in this research. Multi-level houses with a large catalog should be profiled (open time, frame time on a mid-range phone).

### 17. Mobile and tablet: site work, sync, no data loss. (Med)
- **Evidence:**
  - Planner 5D: lost all progress after logging in on several devices. [search extract]
  - Morpholio Trace markup "on top of ... drawing sets & PDFs, construction site photos", with RoomPlan inside. [search extract, vendor] https://apps.apple.com/app/morpholio-trace-sketch-cad/id547274918
  - Homestyler has no offline desktop app. [search extract]
- **Casa3D: PARTIAL.** It has phone pinch and joystick and a site page (`core/site.ts`, a snag list with photos). The gap note's phone audit lists the remaining layout problems.

### 18. Markup and annotation for contractor collaboration (redlines). (Low-Med)
- **Evidence:**
  - A redline records how what was built differs from the approved drawing and matters "before walls close". https://www.designpresentation.com/red-line-markup-redline-changes/ [search extract]
  - The Yale homeowner on messy designer drawings and revision tracking. [fetched]
- **Casa3D: PARTIAL.**
  - Has: revision diff, a site snag list, per-product client approvals.
  - Not seen in code: free annotations or markups on the plan or sheets (text notes, leaders, revision clouds).

### 19. Pricing pain: paywalls, credits, subscriptions. (High frequency; a business insight, not a feature)
- **Evidence:**
  - magicplan: "forcing you to subscribe on a monthly basis"; "hide every feature you'd want to use behind obscure paywalls". [fetched]
  - RoomSketcher credits. [search extract]
  - Planner 5D: the cost is too high in weaker-currency countries (April 2026). [search extract]
  - Coohom: cannot pay in local currency. [search extract]
- **Relevance:** RO is a weaker-currency market (inference). Casa3D should keep core drawing and export free of per-render or per-export gates.

### 20. Before/after comparison. (Low evidence)
- I found no user complaint asking for it in design tools. It is mostly a marketing format.
- **Casa3D: not seen in code.** It is cheap to build (see section 3), but not justified by this evidence.

---

## 2. Top 10 MISSING/PARTIAL items for a solo or small RO studio (deterministic, no AI calls)

Ordering weighs how strong the evidence is, how much rework or wrong orders the gap causes, how much existing Casa3D code can be reused, and RO fit.

**1. Tile setting-out (floor and wall).**
- **Input:** each tiled surface (room floor, each bathroom wall up to the tile height in `bath.ts`), tile size, grout mm, pattern (already in `finishes.ts`), and an anchor: centre, a chosen wall or corner, or a focal object such as a vanity, niche or window.
- **Calculation:** the offset that maximises the smallest cut on both axes, with a rule warning when any cut is under 1/3 of a tile (the tilers' rule). Report the number of full tiles, the cut tiles grouped by size, and the waste.
- **Output:** a wall-elevation and floor sheet with the actual joint grid, the start line dimensioned from a wall, and the cut sizes in mm. The BOQ uses the real count instead of the flat waste %.
- **Smallest version:** rectangular surfaces and straight or brick patterns only. Openings are subtracted as rectangles.

**2. Kitchen module layout and clearance checks.**
- Turn the single kitchen piece into an ordered list of modules along walls: width from 15/20/30/40/45/50/60/80/90 cm, type (base, drawer, sink, dishwasher, oven housing, fridge, corner, filler) and uppers.
- **Checks, as WARN not ERROR with the source cited:**
  - Work aisle at least 1.07 m for one cook and 1.22 m for two (NKBA 42"/48", converted).
  - Dishwasher adjacent to the sink module (within one module) but not in the corner return.
  - Landing area of at least 38 cm (15") beside the fridge handle side and the hob.
  - The hob not under a window and not at the end of a run without landing space.
  - Corner conflicts (door or drawer clash with a perpendicular run).
  - Run length equals the sum of modules plus fillers.
- **Outputs:**
  - a front elevation of the run (base, upper, backsplash height `BACKSPLASH_M`);
  - a priced module list from the catalog, which answers the "catalog with pricing" ask;
  - worktop pieces (max length 3.0/4.1 m, joints placed away from the sink and hob cut-outs, and the cut-outs listed).

**3. Survey mode (on-site measurement entry).**
- A phone-first form, room by room: type each wall length in order, ceiling height, opening widths, heights and sill heights, and two diagonals.
- **Deterministic checks:**
  - polygon closure error (mm);
  - diagonal versus rectangle squareness (deviation in mm, e.g. "the corners are not square; one diagonal is 23 mm longer than the other");
  - a flag for any wall that differs from the underlay or the scan by more than a tolerance.
- **Outputs:** the room polygon is built from the entries, and a dimensioned survey plan ("releveu") comes from the existing dimensions code.
- **Phase 2:** read a Bluetooth laser measure into the focused field via Web Bluetooth. This is UNVERIFIED for each device's GATT protocol, and Web Bluetooth is not available in iOS Safari, so it would cover Android and desktop Chrome only.
- **Not included:** in-browser LiDAR.

**4. Title block and numbered sheet set.**
- **Studio profile:** name, logo, address, licence or registration number if any.
- **Project fields:** client, address, project code.
- **Per sheet:** sheet number and title (A-01 survey, A-02 furniture plan, E-01 electrical, L-01 ceiling and lighting, T-01 tiles, K-01 kitchen elevations), scale, date, revision letter (from the revision diff), drawn by, and a scale bar and north arrow.
- **Outputs:**
  - one "Dosar tehnic" PDF with a contents page;
  - a revision table on each sheet listing what changed (reusing `core/diff.ts`);
  - the same block written into DXF paper space or as a block, if cheap.

**5. Electrical and plumbing setting-out dimensions and legend.**
- Casa3D already places points with heights. Add:
  - on the E-sheet, each point dimensioned from the nearest room corner along the wall, plus its mounting height (e.g. "P2 · 1.15 m");
  - a legend with counts per room (already computed);
  - a "freeze" stamp (date and revision) so the electrician works from a dated version;
  - kitchen worktop-height outlets defaulting to worktop + 20 cm (Jung) when there is a worktop.
- **Also check:** plumbing points (cold, hot, drain) need heights per fixture type: basin, WC, shower, sink, washing machine, dishwasher.
- **Before building:** verify what the current sheet already prints. This may be half-done.

**6. Wardrobe and joinery configurator with a PAL cut list.**
- **Parametric carcass:** width, height and depth; 18 mm PAL and 3 mm or 8 mm back; divisions, shelves, rails and drawers; hinged or sliding doors. Built from catalog PAL decors (Egger and Kronospan exist in RO shops).
- **Output:**
  - a front elevation with dimensions;
  - a cut list (part, L×W, quantity, decor, grain direction, which edges get ABS edge banding 0.4/2 mm);
  - a CSV and PDF in the format RO cut services accept, plus a sheet count on 2800×2070 boards (simple area-based estimate with a margin).
- **Not included:** CNC export or nesting optimisation (see section 3).

**7. GLB/OBJ export of the 3D scene, and DXF import as an underlay.**
- **Export:** three.js `GLTFExporter` on the current level (walls, floors, furniture, with materials as PBR factors and textures) for a 3ds Max, SketchUp or Blender visualiser. Units in metres, Y-up, named by room and product.
- **Import:**
  - parse DXF LINE/LWPOLYLINE/ARC on chosen layers as a vector underlay;
  - detect units from `$INSUNITS`, or ask using a known dimension (reuse the underlay calibration);
  - the designer traces walls over it.
  - DWG must be converted to DXF by the user. State that plainly in the UI.

**8. Paint colour by code (RAL Classic, plus brand fan decks where licensable).**
- A wall-finish field holding a code and a name, with an approximate sRGB value. The schedule, BOQ and presentation print the code, not the hex.
- A fixed note on the screen and on the sheet: "screen approximation; confirm with a physical sample".
- **Licensing:** NCS notation and its colour data are licensed; RAL hex approximations are widely published but not official. Ship RAL Classic first and leave NCS as a free-text code. UNVERIFIED legal position; check before shipping NCS data.
- Feed LRV-like luminance into the existing advisor (dark walls in a small room).

**9. Plan and sheet annotations (markup layer).**
- Text notes, leader arrows, revision clouds and "verify on site" markers, placed on the 2D plan and on any sheet.
- Each annotation belongs to a revision and is optionally linked to a room or snag.
- They print on PDF and export as a DXF layer `NOTES`.
- In the client link, read-only notes can be shown to the client.
- This also gives contractors a "what changed" view that combines notes with the revision diff.

**10. Large-project performance budget (measure first).**
- Add a reproducible benchmark: a three-level house with about 300 placements.
- **Budgets:** open time, editor frame time and 3D frame time on a mid-range Android phone, and PDF generation time.
- **Fixes to try where over budget:** instancing, merging static geometry per level, lazy texture loading, and rendering only the active level.
- **Why:** several competitors are criticised for lag, and Casa3D is used on phones. It is a deterministic engineering item, but its value depends on the measurement, so it ranks last.

**Optional, cheap:** a before/after slider in the client link (site photo next to a render or panorama at the same camera). The evidence for it is weak.

---

## 3. Not worth building now, and why

- **In-browser LiDAR or RoomPlan scanning.**
  - RoomPlan is native iOS only, and Safari has no depth API (inference from platform docs; not tested).
  - Measured scan accuracy (about 15 cm to "within 150mm") is not good enough for cabinets or tiles anyway.
  - A typed survey with closure checks (item 3) is more accurate and cheaper.
  - At most, later: import an exported RoomPlan or magicplan plan as a reference underlay.
- **A photometric engine (DIALux-grade).** The lumen-method estimate already covers residential needs. Photometric results need IES/LDT data per luminaire and introduce liability. When a client needs certified lux, the designer can outsource to DIALux.
- **A BIM/IFC workflow and native DWG writing.**
  - IFC: residential interior jobs in RO are small. No source found a designer asking for IFC from an interior tool.
  - DWG: needs a licensed library (ODA). DXF covers the CAD handoff.
- **VR headsets and real-time path-traced walkthroughs.** These are vendor-pushed. Panoramas, phone 3D and Blender renders already cover client immersion at low cost.
- **CNC export or nesting optimisation for joinery.** This belongs to the cabinet maker's software (PRO100/CUT100, Mosaik, ABF). Casa3D should hand over a clean cut list (item 6), not compete.
- **A generic mood-board canvas (Canva or Milanote clone).** The evidence says the pain is disconnection from the spec, which Casa3D already solves by tying the board to the catalog, finishes and budget. A free-form canvas adds scope with little gain.
- **Code-compliance checking (building regulations).** Even Home Designer's competitors skip it, and reviewers say "you'll need to hire the architect ... anyway". RO norms (e.g. I7 for electrical) need expert curation and carry liability. Keep the advisory "rules of thumb, not norms" stance that `technical.ts` already declares.
- **Roof and exterior tools.** These come up in RoomSketcher and Home Designer complaints, but they are out of scope for an interior studio.
- **AI features** (auto-layout from a prompt, AI render enhance). Excluded by the product rule. Competitors such as DesignFiles' AI enhancer also show no evidence that this fixes the core complaints above.
- **Per-render or credit pricing.** This is the most resented pattern in reviews. Don't introduce it.

## Gaps in this research
- No Reddit or Facebook-group evidence, because both are inaccessible. RO practitioner voice is mostly inferred.
- Capterra, GetApp and G2 quotes are search-engine extracts, not opened pages.
- The full NKBA guideline PDF and RAL/NCS licensing terms were not read.
- Casa3D status comes from code grep. It was not run in a browser here.
