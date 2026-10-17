---
id: "693ea7e5-480a-490a-9399-eca8fa9e2c03"
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
    target_id: "f09fc736-92ae-472e-9f6b-395293bfbed8"
---
# Casa3D: design-depth research (2026-10-11)

Status: web research, `DOCUMENT_VERIFIED` against vendor pages, reviews and guides only. Not checked against Casa3D code. Most competitor feature claims come from vendor marketing. Reddit could not be fetched (domain blocked for the crawler), so the pain points come from Capterra, G2, GetApp and the SketchUp/ChiefTalk forums.

## (a) Top 15 missing design features, ranked by value/effort

Value: H/M/L for a Romanian residential designer. Effort: S/M/L given what Casa3D already has (catalog, finishes, style packs, 3D, Blender).

| # | Feature | Who has it (evidence) | Value | Effort | Why it ranks here |
|---|---|---|---|---|---|
| 1 | **Design rules checker / auto-place** for rugs, art, curtains, pendants, TV and clearances (table b) | No competitor markets a full rule engine. Planner 5D has Smart Wizard auto-layout ([P5D](https://planner5d.com/blog/smart-wizard-for-android/)) | H | S–M | Casa3D already checks NKBA and bathrooms, so this extends an existing engine. It is also a differentiator. |
| 2 | **Mood/material board linked to the plan and catalog**: drag-drop, background removal, paint swatches, live-updating plan image | Houzz Pro ([pro.houzz.com](https://pro.houzz.com/for-pros/feature-mood-board), [press](https://www.houzz.com.au/press/793/Houzz-Pro-Visualisation-Tools-Empower-Home-Professionals-to-Bring-Client-Projects-to-Life)), DesignFiles ([blog](https://blog.designfiles.co/moodboard-apps/)), Programa Pinboards ([programa](https://programa.design/features/interior-design-mood-boards)) | H | M | Every pro-tier competitor has one, and it is the first deliverable clients see. |
| 3 | **Mood board → spec/approval pipeline** (an approved board item becomes a BOQ line) | Fohlio ([fohlio](https://www.fohlio.com/teams/design)), DesignFiles auto product lists ([Capterra](https://www.capterra.com/p/179202/DesignFiles/)) | H | S | Casa3D already has BOQ and approvals, so only the link is missing. |
| 4 | **Design options A/B (variants)** of a room: layout, finishes and products, with side-by-side cost delta and client pick | Chief Architect saved plan views / design options ([KB-01167](https://www.chiefarchitect.com/support/article/KB-01167/understanding-default-sets.html), [blog](https://www.chiefarchitect.com/blog/design-options/)); Foyr AI-Restyle variations ([fitoutawards](https://fitoutawards.ie/news/foyr-introduces-ai-hub-to-enhance-interior-design-and-fit-out-projects)) | H | M | Builds on the share link and approvals Casa3D already has. |
| 5 | **Colour scheme tool**: palette per room (60-30-10 roles), brand paint fan decks (RO: Savana, Dulux, Caparol), harmony check | Houzz (Benjamin Moore/Dulux) ([furnituretoday](https://www.furnituretoday.com/designers/houzz-pro-pairs-up-with-benjamin-moore-to-bring-new-color-options-to-online-tools)), RoomSketcher brand colours ([GetApp](https://www.getapp.co.uk/software/2049888/roomsketcher)) | H | S | |
| 6 | **Lighting design layers + lux estimate**: ambient/task/accent circuits, a lux-per-room target with a lumen-method check, light scenes in 3D | Homestyler Lighting Editor ([unite.ai](https://www.unite.ai/homestyler-review)); DIALux/IES for real calculation ([a23d](https://www.a23d.co/blog/ies-profiles-all-you-need-to-know)) | H | M | Fixtures already exist; the lumens/area check is cheap. |
| 7 | **Ceiling designer**: bulkheads/soffits, tray/coffered ceilings, LED coves, drop-ceiling plasterboard (rigips) shapes linked to the ceiling drawing | Coohom 3 modes incl. parametric and offset/chamfer ([Coohom](https://www.coohom.com/helpcenter/3FO4K4UKEIKM)); Chief tray/coffered ([KB-00201](https://www.chiefarchitect.com/support/article/KB-00201/creating-a-tray-ceiling.html), [KB-00738](https://www.chiefarchitect.com/support/article/KB-00738/creating-a-coffered-ceiling.html)) | H | M–L | Rigips ceilings with LED strip are standard in RO renovations. |
| 8 | **Parametric custom joinery** beyond the kitchen/wardrobe: TV wall units, built-in bookcases, bench seats, with handles and a material per face | Coohom kitchen & closet parametric ([WWN](https://www.woodworkingnetwork.com/product/plant-production/3d-kitchen-bath-design-tool)); RoomSketcher gap: "can't add cabinet handles without manually placing a block on each door" ([Capterra](https://capterra.com/p/164021/RoomSketcher/reviews/)) | H | M | Reuses the cut-list engine. |
| 9 | **Wall panelling & mouldings**: wainscot, slats/riflaj, picture-frame moulding, cornices, profile libraries | Chief moldings on ceilings/walls ([KB-00738](https://www.chiefarchitect.com/support/article/KB-00738/creating-a-coffered-ceiling.html)); not found in RoomSketcher | M–H | M | Riflaj (slat) walls are very popular in RO. |
| 10 | **Textiles & soft furnishing**: curtains/blinds sized to the window (auto width/height/fullness), rugs with sizes, cushions | Mostly generic catalog items elsewhere; no dedicated tool found | M | S–M | Pairs with the rules in #1. |
| 11 | **Decor styling / auto-accessorize**: fill shelves, tables and beds with style-pack decor | Homestyler AI Decoration ([TrustRadius](https://www.trustradius.com/products/homestyler)), Planner 5D virtual staging ([P5D AI](https://planner5d.com/use/ai-furniture-placement)) | M | M | Makes renders look finished. |
| 12 | **Auto-furnish a room** from room type, style pack and budget, then let the designer edit | Planner 5D Smart Wizard ([support](https://support.planner5d.com/en/articles/7439451-smart-wizard-ios)), Homestyler | M | M–L | Needs #1 rules to be credible. |
| 13 | **Alignment/symmetry guides**: centre-on-wall, equal spacing, mirror pair, snapping to the window axis | Generic in CAD; Homestyler users complain they "couldn't place objects at the end of a wall or at a room corner" ([Capterra CA](https://capterra.ca/software/1060582/homestyler)) | M | S | |
| 14 | **Door/window/hardware catalog** with real RO products (interior doors, PVC windows, handles) | Houzz resize to product ([visualization tools](https://www.houzz.com/for-pros/visualization-tools)) | M | S–M | |
| 15 | **AI restyle / image-to-3D** of a client's photo or product | Foyr AI Hub ([fitoutawards](https://fitoutawards.ie/news/foyr-introduces-ai-hub-to-enhance-interior-design-and-fit-out-projects)) | M | L | Trend item; lower priority than rules and boards. |

## (b) Numeric design rules (checkable / auto-applicable)

| Rule | Value | Source |
|---|---|---|
| Rug vs sofa | At least the front legs of all seating on the rug; otherwise size down so no legs touch | [designshop](https://www.designshop.com/collection/rug-size-guide), [ruglove](https://www.ruglove.co.uk/blogs/rugs/how-big-should-your-rug-be-compared-to-your-sofa-a-guide-to-perfect-proportions) |
| Rug border to wall | 30–45 cm (12–18 in) of exposed floor; 45–90 cm in large rooms | [homfurniture](https://www.homfurniture.com/blog/what-size-area-rug-do-you-really-need-for-your-living-room-28ab) |
| Rug under bed | 60–90 cm (2–3 ft) of rug each side of the bed | [ruggable](https://ruggable.com/en-CA/pages/rug-sizes) |
| Art centre height | 145 cm (57 in) from the floor; 150–155 cm (60–62 in) under high ceilings; treat a group as one piece | [Apartment Therapy](https://www.apartmenttherapy.com/how-to-hang-artwork-properly-57-inches-from-the-floor-6174), [livingetc](https://www.livingetc.com/advice/picture-hanging-hack) |
| Art above sofa | Gap 15–20 cm (6–8 in) above the backrest; width 60–75% (about 2/3) of the sofa | [tidbitsandtwine](https://www.tidbitsandtwine.com/how-high-to-hang-pictures), [edwardmartin](https://edwardmartin.com/blogs/information/how-to-choose-the-right-size-wall-art-for-your-living-room) |
| Curtain rod width | 10–15 cm (4–6 in) past the frame each side (C&B: 7.5–15 cm) | [TaskRabbit](https://www.taskrabbit.com/blog/how-high-to-hang-curtains), [Crate&Barrel](https://www.crateandbarrel.com/ideas-and-advice/hanging-curtain-panels-guide) |
| Curtain rod height | 1/2–2/3 of the way from the trim top to the ceiling; 10–15 cm above the frame is the minimum | [TaskRabbit](https://www.taskrabbit.com/blog/how-high-to-hang-curtains) |
| Pendant over table/island | 76–91 cm (30–36 in) above the surface; +7.5 cm per extra 30 cm of ceiling above 2.44 m | [This Old House](https://www.thisoldhouse.com/lighting/all-about-pendant-lights), [Hunker](https://www.hunker.com/13412812/how-high-should-you-hang-pendant-lights-above-a-kitchen-island) |
| Multiple pendants | 61–76 cm (24–30 in) on centre; about 45 cm (18 in) in from the island ends | [Artika](https://artika.com/blogs/inspiration/complete-pendant-height-spacing-guide), [Hunker](https://www.hunker.com/13412812/how-high-should-you-hang-pendant-lights-above-a-kitchen-island) |
| Fixture over a walkway | Bottom at least 2.13 m (84 in) above the floor | [Mod Lighting](https://help.mod-lighting.com/en-US/how-to-choose-the-right-pendant-size-8016576) |
| Dining fixture size | 1/2–2/3 of the table width | [Mod Lighting](https://help.mod-lighting.com/en-US/how-to-choose-the-right-pendant-size-8016576) |
| Sofa to coffee table | 35–45 cm (14–18 in); 30 cm minimum | [Wayfair](https://www.wayfair.com/ideas-and-advice/coffee-table-size-guide-S4674.html), [megafurniture](https://megafurniture.sg/blogs/articles/coffee-table-sofa-distance) |
| Coffee table length | 1/2–2/3 of the sofa length | [megafurniture](https://megafurniture.sg/blogs/articles/coffee-table-sofa-distance) |
| Side table height | Equal to the sofa arm or up to 5 cm lower | [tidbitsandtwine](https://www.tidbitsandtwine.com/my-secret-cheat-sheet-of-measurements/comment-page-1/) |
| TV size vs distance | SMPTE 30° field of view: diagonal ≈ 0.65 × distance (minimum); THX 40°: diagonal ≈ 0.835 × distance (immersive) | [Kaleidescape](https://www.kaleidescape.com/home-theater-guide/screen-size/), [RTINGS](https://rtings.com/tv/learn/size-to-distance-relationship) |
| Circulation path | 90 cm (36 in) standard; 120 cm at main entrances | [studiomatrx](https://www.studiomatrx.org/students/interior-design-foundations/activity-space-clearances-interiors), [dimensions.com](https://www.dimensions.com/element/dining-room-clearances) |
| Behind dining chair | 90 cm pull-back only; 105–120 cm when people walk behind seated diners | [arcedior](https://arcedior.com/blog/chair-clearance-behind-dining-tables), [dimensions.com](https://www.dimensions.com/element/dining-room-clearances) |
| Bedside clearance | 60–90 cm (24–36 in) on each used side | [arcedior](https://arcedior.com/blog/chair-clearance-behind-dining-tables), [studiomatrx](https://www.studiomatrx.org/students/interior-design-foundations/activity-space-clearances-interiors) |
| Lux by room (practice) | Living 100–300 lx; kitchen 300–750 lx (worktop task); bedroom 100–200 lx; bathroom 200–300 lx. EN 12464-1 covers workplaces only and applies just to the home office (500 lx desk). | [Trilux](https://trilux.com/en/lighting-practice/indoor-lighting/specific-lighting-requirements/lighting-of-further-indoor-spaces/residential-areas), [Ensto](https://ensto.com/support/tools/lighting-guide/the-indoor-lighting-standard-sfs-en-12464-12011) |
| Colour 60-30-10 | 60% dominant (walls, floor, large pieces), 30% secondary (curtains, chairs, feature wall), 10% accent (cushions, art, lamps); a guideline, not a hard rule | [Bob Vila](https://bobvila.com/articles/60-30-10-rule), [Ideal Home](https://idealhome.co.uk/all-rooms/all-rooms-decor/60-30-10-rule) |

Implementation note: encode these as warnings with ranges, not errors, because the sources disagree within each band. Store per-rule provenance with the source URL. Units in the app should be metric (cm).

## (c) Top pain points (design workflow)

1. **The mood board and the spec drift apart.** "A client approving an item by email while the spreadsheet still lists it as pending" ([specsources](https://www.specsources.com/blog/interior-design-software-that-makes-every-project-flow-naturally/)). Fohlio sells the fix: "approved mood board items can go straight to your project specs" ([fohlio](https://www.fohlio.com/teams/design)). This supports items #2 and #3.
2. **Too many tools: board in one app, 3D in another.** Programa "doesn't include built-in design tools like floor planning or 3D rendering" ([DesignFiles blog](https://blog.designfiles.co/programa-design-software/)). Designers fall back to "InDesign and Photoshop" for boards ([Houzz forum](https://www.houzz.com/discussions/2284430/mood-boards)).
3. **Custom pieces are hard to model.** A Homestyler architect/designer had trouble with "upholstered headboards and smooth curves" and "can't use all the furniture without a membership" ([Capterra AU](https://www.capterra.com.au/software/1060582/homestyler)). RoomSketcher: "Limited customization", with cabinet handles placed per door by hand ([Capterra](https://capterra.com/p/164021/RoomSketcher/reviews/)). This supports item #8.
4. **Simple design moves take many steps.** Chief Architect Interiors is "over-engineered"; a vaulted ceiling or rotating a floor pattern are multi-step tasks ([Capterra p2](https://www.capterra.com/p/80035/Chief-Architect/reviews/?page=2)). This supports items #7 and #13.
5. **Generic, dated catalogs; no real products.** Chief furniture is "dated and blocky" and outside manufacturer products cannot be imported (same source). Planner 5D has a "limited range of furniture and decor options" ([Capterra](https://capterra.com/p/164022/Planner-5D/reviews/)). This confirms Casa3D's real-product catalog as an advantage.
6. **Documentation is slow to produce from the model.** SketchUp LayOut users report spending "half your time... waiting for the viewports to update" and want "tables... connected to the SketchUp files for automating a list of components" ([SketchUp forum](https://forums.sketchup.com/t/layout-too-slow-to-use/119318?page=5), [forum](https://forums.sketchup.com/t/i-hate-layout-it-is-very-very-bad/340318)). Casa3D already auto-generates drawings and the BOQ, which is a strength to keep.
7. **Placement constraints and snapping.** Homestyler users "couldn't place objects at the end of a wall or at a room corner" ([Capterra CA](https://capterra.ca/software/1060582/homestyler)). This supports item #13.
8. **Speed, lag and learning curve.** Coohom's most common G2 complaint is slow performance, plus a "difficult learning curve" ([G2](https://www.g2.com/products/coohom)). Keep new design tools parametric and one-click.
9. **Premium gating of lighting and materials, and credits.** RoomSketcher reviewers say "advanced customization options (like lighting and materials) are limited to premium plans" and complain about credits ([Capterra](https://capterra.com/p/164021/RoomSketcher/reviews/)).

Gaps and unknowns: no first-hand Reddit evidence (blocked). Coohom's mood-board and lux features are UNKNOWN (not found in its docs). The RO paint-brand availability of fan-deck data is UNVERIFIED.
