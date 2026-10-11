---
id: "740998bd-d812-45fb-a936-e5c5ad03fa75"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, rendering]
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
# Casa3D: product-accurate 3D models (research, 2026-10-11)

Status: web research, `DOCUMENT_VERIFIED` only where a URL is cited. This is not legal advice. Every licence row must be re-checked on the live page before anything ships.

## 0. Bottom line

You cannot legally bundle real IKEA geometry, and the only free official IKEA dataset is non-commercial. The manufacturer path is open only for sanitaryware (Roca, Geberit, Grohe BIM). So the realistic way to get "exactly like the ordered product" is:

**parametric families per product line, driven by captured visual attributes, plus textures from the product's own colour/finish**,

with manufacturer BIM meshes for wall-hung WCs and basins where the licence allows rendering. Image-to-3D is a fallback for odd items, never for hero shots.

## 1. Recommended strategy (ranked)

| # | Approach | Fidelity | Effort | Legal risk | Use for |
|---|---|---|---|---|---|
| 1 | **Per-product-line parametric families** (KIVIK, LACK, BESTÅ, BILLY, MALM, HEMNES, ENHET as named generators, not one generic "sofa"), plus an attribute schema (section 4) filled per SKU | High in silhouette, medium in detail | Medium: about 1–3 days per family, after which it scales to every size and colour | **Low**: your own geometry; brand names used only as product references | All IKEA, Dedeman and eMAG furniture |
| 2 | **Manufacturer BIM/CAD meshes** (Roca, Geberit, Grohe), decimated and re-materialised for Cycles and glTF | Very high (true shape) | Low–medium (cleanup, scale, UVs) | **Low–medium**: free licences are usually "use in your design" and non-transferable. Rendering is fine; redistributing the raw file to the browser needs checking | WC, basin, taps, flush plates |
| 3 | **Texture/colour from the product page** (sampled swatch, fabric normal map from a CC0 library) applied to #1 | Large perceived gain | Low | Low if you sample colour only. Medium if you reuse retailer photos as textures (copyright belongs to the retailer) | Fabric sofas, wood/white/oak-veneer finishes |
| 4 | **Commissioned or partner models** (ask Goodmob, Savini or Seldus for GLB/OBJ; the Planner 5D and Coohom model of supplier-delivered files) | Very high | Business effort | Low with written permission | Vanity units and shower cabins from small RO brands |
| 5 | **Image-to-3D** (TripoSR, Meshy, Hunyuan3D) from product photos | Low–medium: blobby edges, wrong proportions, baked lighting | Low per item | Medium: tool licence, plus the copyright in the photo and the product design | Background decor only, never the ordered hero item |
| 6 | **CC0/CC-BY generic assets** (Poly Haven, Sketchfab CC0/CC-BY) | Generic, not the product | Low | Low (CC0) to low with attribution (CC-BY) | Plants, decor, lamps, filler props |
| ✗ | Scraping IKEA's web/AR GLBs, or 3D Warehouse/3dsky "IKEA" models | High | Low | **High**: no licence grant, and uploaders often do not own the rights | Do not ship. At most, internal proportion reference |

## 2. Licence table per source

| Source | What exists | Formats | Commercial renders for clients? | Bundle/serve in the web app? | Evidence |
|---|---|---|---|---|---|
| IKEA website/app AR models | GLB previews on product pages, fetched by unofficial scripts and extensions | GLB/glTF (USDZ on iOS) | **No grant.** IKEA's terms still apply, and the press advises assuming no commercial use | **No** | [digitalproduction.com](https://digitalproduction.com/2026/02/09/download-ikea-furniture-as-3d-models-minus-the-trolley/), [Chrome extension (unofficial)](https://chromewebstore.google.com/detail/kihdhjecfjagoplfnnpdbbnhkjhnlfeg) |
| IKEA official datasets (e.g. IKEA3DAssemblyDataset) | Research meshes | GLB/OBJ | **No**: CC BY-NC-SA 4.0, and the licence excludes rights in the IKEA products/designs | No | [github.com/IKEA/IKEA3DAssemblyDataset](https://github.com/IKEA/IKEA3DAssemblyDataset) |
| Roca | BIM objects for sanitaryware, furniture, showers | RFA, ArchiCAD, 10+ formats via bimobject | Rendering in a design: generally yes. Read the per-object EULA | Unclear; ask Roca | [roca.com/bim](https://www.roca.com/bim), [uk.roca.com BIM](https://www.uk.roca.com/professional-area/bim-objects) |
| Geberit | Revit BIM plug-in (free) | RFA, inside Revit | Rendering in a design: yes (free tool) | Unclear; ask Geberit | [Geberit BIM download](https://cdn-geberit-country-uk.prod.web.geberit.com/sanitary-piping-systems/digital-tools-software/geberit-bim-plug-in/bim-downloadpage/) |
| Grohe | BIM/Revit data on its own site (400+ products in 2016); 3D/CAD also on CADENAS-type portals | RFA, others | Likely yes, within a design. Current terms not verified | Unclear | [specificationonline 2016](https://www.specificationonline.co.uk/articles/2016-08-03/grohe-now-offers-bim-data-on-more-than-400-products) |
| BIMobject (host platform) | Manufacturer objects | RFA, SKP, 3DS, FBX, OBJ, IFC | Set per manufacturer. Typical: free, revocable, non-transferable, "do not pass the file to third parties outside a design" | Usually **no** for raw files | [BIMobject ToS](https://business.bimobject.com/terms-of-service-eula), [Wavin example terms](https://eu-assets.contentstack.com/v3/assets/blt573fdbfcb1106c2a/blt9492b29f74d3c24c/646eeee897949e2d758b4423/180924_Standard_Licence_Terms.pdf) |
| CADENAS PARTcommunity | Free certified supplier CAD | STEP, STL, OBJ, many | Free to download; usage terms not verified | Unverified | [cadenas.de](https://cadenas.de/fr/news/the-partcommunity-download-record-triple-is-perfect) |
| Sketchfab | Per-model licence | GLB/glTF, FBX, OBJ, USDZ | CC0, CC-BY (credit) and Standard: yes. **CC-BY-NC: no.** Also check trademark/"editorial" on branded items | CC0/CC-BY: yes, with attribution | [Sketchfab licences](https://help.sketchfab.com/en/articles/16152217) |
| 3D Warehouse | User uploads under the General Model License | SKP (export to others) | Allowed when part of a substantially new work. **But uploaders often lack the rights to "IKEA X" models** | No resale of the raw model | [3DW ToS](https://embed-3dwarehouse-classic.sketchup.com/tos/), [forum thread](https://forums.sketchup.com/t/confusion-surrounding-license-terms-for-models/31767) |
| 3dsky | Paid/pro marketplace (many "IKEA" lookalikes) | MAX, FBX, OBJ | Own licence page not found. Assume renders are allowed and redistribution is not; brand-copy IP risk | No | Not verified (see [TurboSquid licence for typical terms](https://www.turbosquid.com/ko/licensing)) |
| Poly Haven | Models, textures, HDRIs | blend, glTF, FBX | **Yes, CC0**, no attribution required. No warranty | Yes | [polyhaven.com/about-contact](https://polyhaven.com/about-contact), [licenseorg summary](https://licenseorg.com/guide/3d-assets/poly-haven) |
| Meshy (AI) | Image/text to 3D | GLB, FBX, OBJ, USDZ | Free plan: CC BY 4.0 (credit Meshy). Pro: private, full commercial use. Meshy may train on non-Enterprise uploads | Yes | [Meshy commercial FAQ](https://intercom.help/meshy/en/articles/16102098-can-i-use-meshy-assets-commercially), [pricing](https://docs.meshy.ai/en/webapp/pricing) |
| TripoSR (self-host) | Single-image to mesh, under 0.5 s | OBJ/GLB | **MIT**: yes. Successor is Stability SF3D, under a different licence | Yes | [GitHub VAST-AI-Research/TripoSR](https://github.com/VAST-AI-Research/TripoSR), [overview](https://www.analyticsvidhya.com/blog/2024/03/stability-ais-triposr-from-image-to-3d-model-in-seconds/) |
| Hunyuan3D 2.x (Tencent) | Image to textured mesh | GLB | **Not licensed in the EU, UK or South Korea, so not usable for a Romanian business.** Elsewhere: over 1M MAU needs a separate licence | No (EU) | [HF LICENSE](https://huggingface.co/tencent/Hunyuan3D-2.1/blob/main/LICENSE) |

A licence on the file is not a licence on the design. Furniture designs and trademarks (IKEA, Grohe) stay with their owners. Showing the ordered product in a render for that customer is low risk. Redistributing a library of brand geometry is not.

## 3. How competitors get brand-accurate models

- **Coohom**: a branded library supplied by manufacturers (Natuzzi, Ethan Allen and Rolf Benz contributed 500+ models in 2020). Brands deliver standard 3DS Max/SketchUp files at no cost. [Furniture Today](https://www.furnituretoday.com/technology/tech-company-coohom-adds-branded-furniture-library)
- **Homestyler**: about 400k-item library. It runs a manufacturer service that models brand products and publishes them with brand info. [Homestyler forum](https://es.homestyler.com/forum/view/1546792366591627266), [profile](https://www.elioplus.com/profiles/vendors/186410/homestyler)
- **Planner 5D**: a B2B "3D product catalogue". Brands upload blend/obj/fbx/glb/stp, or Planner 5D models the products from product data or generates them with AI. [Planner 5D B2B catalogue](https://tool.planner5d.com/b2b-3d-product-catalogue)
- **Roomle**: geometry modelled in Blender or CAD by the brand or Roomle, then imported into Rubens. Variants run on levels: L2 swaps material/colour, L3 uses RoomleScript parametric components (door count, modules). This is the closest analogue to Casa3D's parametric approach. [Roomle content creation](https://docs.roomle.com/rubens/content-creation)
- **IKEA Kreativ**: IKEA's own product models, accurate dimensions. Room capture comes from Geomagical (LiDAR/photos, cloud AI). How the product models are made is not public. [VentureBeat](https://venturebeat.com/business/ikea-launches-first-ai-powered-design-experience-no-swedish-meatballs-included), [ComputerBase](https://www.computerbase.de/2023-04/ikea-3d-mixed-reality-fuer-den-virtuellen-raumplaner/)
- **Houzz**: commissioned 3D models for its 300k most popular SKUs (2017), growing to 1M+ by 2018, with materials and textures. [Houzz blog](https://blog.houzz.com/houzz-brings-augmented-reality-to-home-design-and/), [TechCrunch](https://techcrunch.com/2017/05/03/houzz-adds-a-basic-ar-mode-to-its-ios-apps-to-help-you-shop-for-furniture)
- **Colour/material variants**: everyone uses **one mesh plus a material slot per finish** (Roomle L2, Houzz textures). Nobody models a separate mesh per colour. Casa3D should do the same: `family + params + finish_id`.

Pattern: accurate models come from **the manufacturer supplying files, or a paid modelling team**. AI is used to scale the long tail, not for flagship SKUs.

## 4. Visual attributes to capture per product type

Store these per SKU (from the product page, specs and photo) and drive the generator from them. Dimensions alone are not enough.

- **Sofa** (KIVIK etc.): seats (2/3/corner/chaise side L/R), arm style (wide-square / slim / rolled / none), arm height, back type (loose cushions / fixed / pillow-back), seat cushion count and split, cushion edge (welt/piping, boxed, waterfall), leg type (block / tapered / metal / hidden plinth), leg height and colour, skirt yes/no, cover fabric colour and texture class (bouclé, chenille, velvet, leather), seat depth/height.
- **Coffee/side table** (LACK): top thickness (LACK has a 5 cm slab), leg section (square 5 cm), shelf below yes/no, finish (white, black-brown, oak effect), corner radius.
- **TV unit / storage** (BESTÅ, HEMNES): module grid (columns × rows), front type per cell (door / drawer / glass / open), door style (flat, frame-and-panel, glass), handle/knob type and position or push-open, base (legs / plinth / wall-hung), legs type, top overhang, finish per carcass and front.
- **Bookcase** (BILLY): width (40/80), height (106/202 + extension), shelf count, doors (none / glass / panel, half/full), back panel colour, finish, cornice/crown (HEMNES).
- **Bed** (MALM, HEMNES): headboard height and style (plain / panel / spindles), footboard yes/no and height, side rails height, storage drawers (count, side), legs vs. plinth, mattress size, finish, bedding.
- **Kitchen** (ENHET): layout and module widths, wall vs. base units, front style (flat / frame), handle type (bar, profile, knob) and orientation, worktop material and thickness, open shelves / frame units, plinth height, sink/tap model, colour per front/carcass.
- **Wall-hung WC** (Grohe/Roca/Geberit): bowl shape (oval/rimless-square/D), projection (48/52/54 cm), seat (soft-close, slim/thick), flush plate model and finish (white, chrome, matte black), frame/box-in height.
- **Vanity unit** (Goodmob/Savini): width, drawer vs. door count, handle (bar/integrated grip/none), mounting (wall-hung/legs), basin shape (rectangular/rounded/vessel/integrated), basin overhang, tap holes, tap model and finish, mirror/cabinet, front finish (gloss/matte/wood).
- **Shower cabin** (Seldus): footprint and shape (square / quadrant / pentagonal / walk-in), door type (sliding / pivot / folding), glass (clear/frosted/fluted, thickness), profile colour (chrome/black/gold), tray height, panel/column fittings, handle style.

Universal: dominant colour (hex), secondary colour, material class (wood/veneer, foil, lacquer, fabric, metal, ceramic, glass), gloss level, product photo URL for reference.

## 5. Interior render camera conventions

- **Height**: chest height, about 1.0–1.5 m (4–5 ft is the common real-estate rule). Eye level (around 1.6 m) tends to look high. Lower only for effect. [HDRsoft](https://www.hdrsoft.com/learn/best-height-for-shooting-real-estate-interiors.html), [VDCI](https://vdci.edu/learn/interior-design/interior-camera-settings-eye-level)
- **Verticals**: keep the camera level, or use lens shift (Cycles: shift Y) so walls stay vertical. Tilting causes convergence. [Photofocus](https://photofocus.com/2018/08/01/five-tips-to-photograph-architectural-interiors/), [DPS](https://digital-photography-school.com/tips-better-interior-photography/)
- **Focal**: 16–24 mm full-frame equivalent for rooms, with 24 mm as the usual "widest that still looks natural". Editorial work uses about 35 mm for detail shots. [DPS](https://digital-photography-school.com/tips-better-interior-photography/), [diyphotography](https://www.diyphotography.net/?p=322252)
- **Perspective**: two-point (into a corner) for living rooms and bedrooms shows two walls and depth. One-point (sensor parallel to a wall) for bathrooms, kitchens and symmetrical features. [MIOPS](https://miops.com/blogs/news/10-ways-to-create-breathtaking-interior-photography)
- **Bathrooms/small rooms**: one studio guide recommends 28–35 mm to avoid distorting fixtures. This is a studio preference, not a norm. In practice, render 2–3 views (door view one-point, corner two-point, a vanity/WC detail) rather than one ultra-wide. [Maverick Frame room guide](https://maverickframe.com/lead-magnets/room-by-room-frames.pdf)

## 6. Next steps for Casa3D (proposed)

1. Define an `attributes` JSON schema per product type (section 4) and fill it for the current catalogue.
2. Split generic generators into named families (KIVIK, LACK, BILLY and so on) and check each against the product photo side by side.
3. Request permission and files from Roca, Geberit, Grohe, Goodmob, Savini and Seldus (an email template stating the use is "renders for customers who bought your product").
4. Ban unlicensed sources in the asset pipeline: a provenance field on every asset (source URL, licence, SHA-256).
5. Add a camera preset per room type (section 5).
