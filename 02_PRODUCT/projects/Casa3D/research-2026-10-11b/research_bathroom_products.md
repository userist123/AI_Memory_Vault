---
id: "3aa89918-80b2-43ae-9b6e-7256b3718e04"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, bathroom]
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
# Casa3D — Bathroom product variants on the Romanian market (research 2026-10-11)

Scope: every bathroom element, the shape/type variants sold in RO, typical dimensions (cm, W x D x H) and the attributes that change the 3D mesh, with real Dedeman examples.

**Evidence status.** All URLs are real Dedeman product pages returned by search on 2026-10-11. One price was fetched live (marked **[V]**). Every other price comes from a search-index snapshot (marked **[S]**) and is UNVERIFIED: it may be stale or a promo, and Dedeman prices depend on the locality you select. Typical dimensions come from the listings plus common catalogue practice. The IP-zone rules are standard IEC 60364-7-701 / I7 practice, not taken from a retailer.

Legend for the Req column: **R** = required/standard in a typical RO apartment bathroom, **O** = optional add-on, **R\*** = one of the alternatives is required.

---

## 1. WC (vas WC) — R

| Type | Typical dims | 3D-shape attributes | Example (price RON, URL) |
|---|---|---|---|
| Floor, bare pan, exposed cistern (cheap "compact") | 36 x 47–52 x 39–40 | pan only + separate cistern on top; outlet horizontal/vertical (changes rear foot) | Cersanit Roma R10, 36.7x47.5x39.3, 189 [S] — https://dedeman.ro/ro/vas-wc-cersanit-roma-r10-k07-015-alb-cu-evacuare-orizontala/p/3001767 |
| Floor monobloc / close-coupled (pan + ceramic cistern) | 36 x 60–68 x 75–80 (with cistern) | cistern width/height, cistern lid shape (square/rounded), flush button on top | Hansgrohe GladeLake S set 60312450, 969 [S] — https://www.dedeman.ro/ro/vas-wc-cu-rezervor-mecanism-capac-set-hansgrohe-gladelake-s-60312450-alb-evacuare-dubla-montaj-pe-pardoseala/p/3055424 ; Cersanit Crea K114-023, 36x63.5x42.5, oval, 1231 [S] — https://www.dedeman.ro/ro/vas-wc-cersanit-crea-k114-023-alb-evacuare-dubla-montaj-pe-pardoseala-36-x-63-5-x-42-5-cm-capac-wc-inclus/p/3045175 |
| Back-to-wall, floor (closed back, concealed or separate cistern) | 34–37 x 54–60 x 40–44 | solid shroud to the wall, no visible trap | Roca Meridian 37x60x40, 1279 [S] — https://dedeman.ro/ro/vas-wc-roca-meridian-alb-cu-dubla-evacuare/p/3019927 ; Roca Gap A342472000 34x60x44 — https://www.dedeman.ro/ro/vas-wc-roca-gap-a342472000-alb-evacuare-dubla-montaj-pe-pardoseala-34-x-60-x-44-cm/p/3033154 |
| Wall-hung (suspendat) + concealed frame | pan 35–37 x 48–58 x 33–43; frame 50 x 112 (or 41 x 82 low, under a window) | no floor contact; bowl projection 48 (compact) / 52–54 (std) / 56–58 (comfort); pan height off floor ~40–42 | Cersanit Zen S701-428, 35.5x49.5x42.9, rimless, slim soft-close seat, **1159 [V]** — https://www.dedeman.ro/ro/vas-wc-suspendat-cersanit-zen-s701-428-alb-evacuare-orizontala-35-5-x-49-5-x-42-9-cm-capac-wc-inclus/p/3038654 |
| Concealed frame (cadru/rezervor încastrat) | 50 x 112 x 12–15 | invisible; only the flush plate shows; can raise a "false wall" (placaj) 15–25 cm deep | Geberit Duofix Sigma 111.300.00.6, 1249 [S] — https://www.dedeman.ro/ro/rezervor-wc-incastrat-geberit-duofix-sigma-111-300-00-6-50-x-112-cm-3-6-l/p/3054999 ; Duofix Delta 458.133.21.3 with Delta01 plate, 989 [S] — https://www.dedeman.ro/ro/rezervor-wc-incastrat-geberit-duofix-delta-458-133-21-3-50-x-112-cm-3-6-l-clapeta-actionare-delta01-inclusa/p/3055010 |
| WC with bidet function (cu funcție de bideu: built-in nozzle; or shower-toilet) | 35–41 x 51–61 x 34–43 | extra tap hole/lever on the side; shower-toilets have a thicker seat unit with a side panel | Roca Nexo A34664T000, 763 [S] — https://www.dedeman.ro/ro/vas-wc-suspendat-roca-nexo-a34664t000-cu-functie-de-bideu-alb-evacuare-orizontala/p/3037450 ; Dura Petra 6033MB matte black, 1149 [S] — https://www.dedeman.ro/ro/vas-wc-cu-functie-de-bideu-dura-petra-6033mb-negru-mat-evacuare-orizontala-montaj-suspendat-35-5-x-54-x-40-cm/p/3047516 ; Ideal Standard i.Life E K875501 (shower-toilet) — https://www.dedeman.ro/ro/vas-wc-cu-functie-de-bideu-ideal-standard-i-life-e-k875501-evacuare-orizontala-montaj-suspendat-41-2-x-61-x-42-5-cm/p/3057794 |

**Shape parameters for the 3D model:** bowl plan shape `oval | rounded-square | square` · projection 48–58 · rimless yes/no (no visible inner rim) · seat `slim (≤3 cm, duroplast) | standard`, with soft-close · colour `white gloss | matte white | matte black | colour` (Bocchi, Dura offer black/colours).

**Flush plate (clapetă), concealed systems only:** ~24.6 x 16.4 x 1.2–1.5 cm (Geberit Sigma), 22 x 15 (TECE Now). Variants: two rectangular buttons, two round buttons, a split square, or touchless. Finishes: white, gloss chrome, matte chrome, gloss/matte black, brushed steel, glass. Examples: Geberit Sigma 01 115.770.JQ.5, matte chrome, 342 [S] — https://www.dedeman.ro/ro/placa-actionare-apa-wc-geberit-sigma-01-115-770-jq-5-finisaj-cromat-mat/p/3040467 ; Sigma 20 gloss black — https://www.dedeman.ro/ro/clapeta-actionare-rezervor-wc-incastrat-geberit-sigma-20-115-882-dw-1-negru-lucios-24-6-x-16-4-x-1-2-cm/p/3037782 ; TECE Now matte black — https://www.dedeman.ro/ro/clapeta-actionare-rezervor-wc-incastrat-tece-now-9240407-negru-mat-22-x-15-x-0-5-cm/p/3045537

## 2. Bidet — O

| Type | Dims | 3D attrs | Example |
|---|---|---|---|
| Floor bidet (pairs with a floor WC series) | 35–37 x 52–56 x 40 | same family silhouette as the WC; 1 tap hole | Ideal Standard Tesi T456901 (sold as a set with the WC) — https://www.dedeman.ro/ro/vas-wc-cu-rezervor-mecanism-capac-set-ideal-standard-tesi-t033601-evacuare-orizontala-bideu-ideal-standard-tesi-t456901-alb-montaj-pe-pardoseala/p/3047431-3045069 |
| Wall-hung bidet (on its own 112 cm frame) | 35–36 x 48–54 x 25–40 | matches the wall-hung WC; is placed beside it at 20–30 cm spacing | Roca Gap 35x54x31.5 — https://www.dedeman.ro/ro/bideu-suspendat-roca-gap-357475000-alb-35-x-54-x-31-5-cm/p/3024051 ; Geberit Selnova 35x53x40, ~505 [S] — https://www.dedeman.ro/ro/bideu-suspendat-geberit-selnova-500-271-01-1-alb-35-x-53-x-40-cm/p/3037776 ; Creavit Free, black — https://www.dedeman.ro/ro/bideu-suspendat-creavit-free-fe510-00sm-negru-36-x-50-6-x-29-cm/p/3040360 |

## 3. Washbasin (lavoar) — R

| Type | Typical dims | 3D attrs | Example |
|---|---|---|---|
| Wall-hung (suspendat) | W 40/45/50/55/60/65/70/80, D 35–48 | rectangular or rounded front; tap hole yes/no; brackets | Roca Debba 60 — https://www.dedeman.ro/ro/lavoar-suspendat-baie-roca-debba-a325994000-alb-dreptunghiular-60-cm/p/3023453 ; Kolo Style 60 — https://www.dedeman.ro/ro/lavoar-suspendat-baie-kolo-style-l21960-alb-dreptunghiular-60-cm/p/3003428 |
| + pedestal / semi-pedestal | pedestal ~17–19 x 17 x 65–72; semi-pedestal ~27 x 33 | floor column vs a short wall-mounted shroud over the trap | Cersanit Roma pedestal K680-002, ~150–189 [S] — https://www.dedeman.ro/ro/piedestal-lavoar-cersanit-roma-k680-002-17-5-x-17-x-70-cm/p/3047172 ; semi-pedestal K07-014, 209 [S] — https://www.dedeman.ro/ro/semipiedestal-lavoar-cersanit-roma-k07-014-27-x-33-cm/p/3013999 |
| Furniture basin (on vanity, with integrated top) | W = cabinet width 40–120, D 37–48, thin 1.5–2 cm edge | sits on the cabinet and overhangs it by ~1 cm | Geberit Modo cabinet + basin 60, 1556 [S] — https://www.dedeman.ro/ro/masca-baie-lavoar-geberit-modo-cu-sertare-alb-suspendat-60-x-48-5-x-65-cm/p/3054343-3054344 |
| Countertop vessel (pe blat): round, oval, rectangular, square | round Ø35–46; oval 50–60 x 35–40; rect 45–60 x 35–40; H 12–15 | bowl sits fully above the top; usually no tap hole, which needs a tall or wall tap; material ceramic, stone, glass or steel | Kadda AB52-V1 round 41, ~240 [S] — https://www.dedeman.ro/ro/lavoar-baie-kadda-ab52-v1-montaj-pe-blat-alb-rotund-41-cm/p/3020852 ; Kadda AB53-V22 rect 49 — https://www.dedeman.ro/ro/lavoar-baie-kadda-ab53-v22-montaj-pe-blat-alb-dreptunghiular-49-cm/p/3020855 ; Kadda AB57-V6 oval — https://www.dedeman.ro/ro/lavoar-oval-montaj-pe-blat-kadda-ab57-v6/p/3020858 ; Kadda AB8428 round, black — https://www.dedeman.ro/ro/lavoar-baie-kadda-ab8428-montaj-pe-blat-negru-rotund-41-5-cm/p/3035392 |
| Semi-recessed | W 50–60, projects ~15–20 cm beyond a shallow cabinet | front half of the bowl overhangs the cabinet | Kolo Nova Pro M31856 55 — https://www.dedeman.ro/ro/lavoar-semi-incastrat-baie-kolo-nova-pro-m31856-alb-dreptunghiular-55-cm/p/3021497 |
| Drop-in / undermount (încastrat / sub blat) | 48–65 x 39–45 | only the bowl cavity is visible; the countertop gets a cut-out | Arthema 0058 under-counter 48x39, ~315 [S] — https://www.dedeman.ro/ro/lavoar-incastrat-baie-oval-montaj-sub-blat-0058/p/3002340 ; Kadda 207 oval 56 — https://www.dedeman.ro/ro/lavoar-incastrat-baie-kadda-207-alb-oval-56-cm/p/3014443 |
| Corner basin | 32–46 x 32–46 (triangular/quarter-round plan) | 45° back, wall-hung | Kadda AB184A 32.5 — https://www.dedeman.ro/ro/lavoar-rotund-montaj-pe-colt-kadda-ab184a/p/3020859 ; Dura Bagno 1094 45.5, 86 [S] — https://dedeman.ro/ro/lavoar-pe-colt-alb-1094/p/3032673 |
| Double basin | 120 (also 100–160) x 46–50 | 2 bowls, 2 tap holes | Goodmob Isik 120, 1098 [S] — https://www.dedeman.ro/ro/lavoar-dublu-baie-goodmob-isik-120-alb-dreptunghiular-120-cm/p/3049630 ; Kadda AC8003-120D, 459 [S] — https://www.dedeman.ro/ro/lavoar-dublu-baie-kadda-ac8003-120d/p/3026124 |

## 4. Vanity unit (mobilier / mască lavoar) — R\* (vanity or pedestal basin)

| Variant axis | Values | Example |
|---|---|---|
| Mounting | wall-hung (body 45–65 high, mounted at 80–85 cm to the top) · floor-standing (on legs or a plinth, ~80–85 high) | Savini Due Creta 60 (floor) is listed in the vanity category: https://www.dedeman.ro/ro/mobilier-baie-baza-lavoar/c/1390 |
| Front | 1–2 drawers · 1–2 doors · open shelf · drawer + niche | Martplast Mondo 60, 2 drawers — https://www.dedeman.ro/ro/masca-baie-lavoar-martplast-mondo-cu-sertare-alb-suspendat-pal-60-x-48-x-58-cm/p/3031540 ; Ideal Standard i.Life S 60, 2 drawers, matte white — https://www.dedeman.ro/ro/masca-baie-pentru-lavoar-ideal-standard-i-life-s-cu-sertare-suspendat-pal-alb-mat-60-x-37-5-x-63-cm/p/3048358 |
| Width | 40/50/60/70/80/100/120 (60 and 80 are the most common); depth 37–48 | Cersanit Crea 60, oak — https://www.dedeman.ro/ro/masca-baie-lavoar-dulap-cersanit-crea-60-cm-stejar/p/3034521-3034516-3034520 |
| Finish | white gloss, matte white, oak/walnut, anthracite/grey concrete, matte black, sage green | Savini Due wall-hung modular top + drawers in white/grey/oak — https://www.dedeman.ro/ro/set-baie-blat-pentru-lavoar-console-lavoare-sertare-suspendate-savini-due-alb-gri-stejar/p/3040699-3040704-3044997-3044988 |
| Handle | bar or knob handle · profile/J-pull · push-to-open (no handle) | — |
| Top-with-vessel | separate top 80–160 x 46–50 x 1.8–3.8 on brackets + a vessel basin | Savini Due Linea top 120x46 oak, 439 [S] — https://www.dedeman.ro/ro/blat-pentru-lavoar-baie-savini-due-linea-2803/22-pal-stejar-120-x-46-cm/p/3044424 |

## 5. Taps (baterii) — R (basin + shower/bath)

| Type | Dims | 3D attrs | Example |
|---|---|---|---|
| Basin mono, standard | H 14–17, spout 10–12 | body: cylindrical, square or "edge"; lever on top or side | Grohe Start 235502432 matte black, 16.5 H, 399 [S] — https://www.dedeman.ro/ro/baterie-baie-pentru-lavoar-grohe-start-235502432-montaj-stativ-monocomanda-finisaj-negru-mat/p/3048968 |
| Basin tall (for vessel) | H 19–32 | long column body | Grohe Start 237462432 matte black, 19.2 H, 500–559 [S] — https://www.dedeman.ro/ro/baterie-baie-pentru-lavoar-grohe-start-237462432-montaj-stativ-monocomanda-finisaj-negru-mat/p/3048970 ; Hansgrohe Vernis Blend XL, 499 [S] — https://www.dedeman.ro/ro/baterie-baie-pentru-lavoar-hansgrohe-vernis-blend-xl-71552000-montaj-stativ-monocomanda-finisaj-cromat-ventil-inclus/p/3044098 |
| Wall-mounted / concealed basin | spout 17–23 out of the wall + round/square escutcheon | nothing on the top; needs an in-wall box | Ferro Tina White concealed — https://www.dedeman.ro/ro/baterie-baie-pentru-lavoar-incastrata-ferro-tina-white-monocomanda-finisaj-alb/p/3034199 |
| Bath/shower exposed mixer | 150 mm centres, spout 15–20 | horizontal body with diverter | Hansgrohe My Sport 71242000 — https://www.dedeman.ro/ro/baterie-baie-pentru-cada/-dus-hansgrohe-my-sport-71242000-montaj-aplicat-monocomanda-finisaj-cromat/p/3023391 |
| Thermostatic exposed | 150 centres, two side knobs | cylindrical body, 2 dials | Grohe Grohtherm 800 + Tempesta set 34565 — https://www.dedeman.ro/ro/set-dus-baterie-termostatata-para-furtun-suport-bara-grohe-34565-2-functii-1-75-m/p/3023947 |
| Concealed bath/shower | plate Ø/□ 15–20 | flat plate + lever only | Hansgrohe Vernis Blend 71466670 matte black, 627 [S] (needs iBox) — https://www.dedeman.ro/ro/baterie-baie-incastrata-pentru-cada/-dus-hansgrohe-vernis-blend-71466670-montaj-incastrat-monocomanda-finisaj-negru-mat/p/3044345 |

Finishes: chrome (default), matte black, brushed steel, brushed gold/brass, white, gunmetal.

## 6. Shower — R\* (shower or bathtub; most new RO flats choose a shower)

| Type | Typical dims | 3D attrs | Example |
|---|---|---|---|
| Walk-in fixed panel | W 70/80/90/100/120 x H 195–200, glass 6–8 mm | 1 panel + wall profile + ceiling/wall stabiliser bar; clear/frosted/patterned glass; profile chrome/black/gold | Kadda W-66 90x200, clear, chrome, 296 [S] — https://www.dedeman.ro/ro/perete-dus-walk-in-kadda-w-66-sticla-transparenta-profil-cromat-90-x-200-cm/p/3057180 ; Seldus EMY105 90x200, black profile — https://www.dedeman.ro/ro/perete-dus-tip-walk-in-seldus-emy105-sticla-transparenta-profil-negru-90-x-200-cm/p/3052052 ; Seldus ST6 120x200, gold, 569 [S] — https://www.dedeman.ro/ro/perete-dus-tip-walk-in-seldus-st6-sticla-transparenta-profil-auriu-120-x-200-cm/p/3052058 ; West WST07 frosted — https://www.dedeman.ro/ro/perete-dus-tip-walk-in-west-wst07-sticla-sablata-profil-cromat-90-x-200-cm/p/3050382 |
| Quadrant (semirotundă), sliding doors | 80x80, 90x90, 100x100 x H 185–200, R 50–55 | curved front, 2 curved sliding doors | Cersanit S158-005 90x90x185 — https://www.dedeman.ro/ro/cabina-dus-semirotunda-cersanit-s158-005-90-x-90-x-185-cm-sticla-securizata-transparenta-profil-cromat/p/3041213 ; Kadda 2018 A 80x80 with tray, frosted, ~525 [S] — https://www.dedeman.ro/ro/cabina-dus-semirotunda-cadita-kadda-2018-a-80-x-80-x-195-cm-sticla-securizata-mata-profil-alb/p/3006420 ; Kolo New Geo 80 — https://dedeman.ro/ro/cabina-dus-semirotunda-kolo-new-geo-560-110-00-3-80-x-80-x-190-cm/p/3035703 |
| Square / rectangular corner enclosure | 80–120 x 70–90 x H 195–200 | sliding, pivot or bi-fold door + fixed side | Profiltek Salma ~100x78 sliding — https://www.dedeman.ro/ro/cabina-dus-dreptunghiulara-profiltek-salma-profil-cromat-98/101-x-77/79-x-195-cm/p/3041159-3041162 ; West WST23 120x80 hinged, grey profile — https://www.dedeman.ro/ro/cabina-dus-dreptunghiulara-west-wst23-profil-gri-120-x-80-x-200-cm/p/3053003 |
| Niche door only (between walls) | 70–120 x 185–200 | pivot / bi-fold / sliding | Geberit New Geo pivot 80x190, 1412 [S] — https://www.dedeman.ro/ro/usa-pivotanta-pentru-cabina-dus-sticla-securizata-aluminiu-geberit-new-geo-560-115-00-2-profil-argintiu-80-x-190-cm/p/3050087 ; Sanotechnik Duet bi-fold 95–97 — https://www.dedeman.ro/ro/usa-pliabila-pentru-cabina-dus-sticla-securizata-aluminiu-sanotechnik-duet-m12100fl-95-97-x-195-cm/p/3045475 |
| Pentagonal | 90x90 with a chamfered front | 45° door panel | Not found at Dedeman in this pass (UNKNOWN); sold by Ravak/Kolo through specialist shops. |
| Shower tray (cădiță) | 80x80, 90x90, 100x80, 120x80/90, 140x90, 160x80; H 2.7–3 (slim stone resin) / 5–15 (acrylic/ceramic) | material: acrylic (smooth white), ceramic, stone resin (slate texture, white/anthracite/cream); low profile; drain central or linear | West Freya 90x90x2.7 composite — https://www.dedeman.ro/ro/cadita-dus-patrata-west-freya-compozit-alb-design-slim-90-x-90-x-2-7-cm-gratar-inclus/p/3051088 ; Profiltek Matis 120x90x3 anthracite — https://www.dedeman.ro/ro/cadita-dus-dreptunghiulara-profiltek-matis-compozit-antracit-design-slim-120-x-90-x-3-cm-antialunecare/p/3050180 ; West Electra 140x90, 1378 [S] — https://www.dedeman.ro/ro/cadita-dus-dreptunghiulara-west-electra-compozit-alb-design-slim-140-x-90-x-2-7-cm-gratar-inclus-antialunecare/p/3051217 |
| Tiled floor + linear drain (rigolă) | drain length 60–120, 7 cm wide | floor flush, sloped tile, steel grate (tile-in/slot) | West Dalia acrylic 100x80 with rigolă — https://www.dedeman.ro/ro/cadita-dus-rectangulara-west-dalia-alba-100-x-80-x-5-cm-rigola-inclusa/p/3029460/ |
| Shower set / column | rail 60–90; column 90–120; rain head Ø20–30 or □; hand shower Ø10–12 | rail + hand shower · column with rain head + hand shower (thermostatic or not) · concealed (wall arm + head) | Grohe Precision Flow 34800000 thermostatic system — https://www.dedeman.ro/ro/sistem-dus-grohe-precision-flow-34800000-baterie-termostatata/p/3043980 ; Grohe New Tempesta DIV 26381001 (no mixer) — https://www.dedeman.ro/ro/sistem-dus-grohe-new-tempesta-div-26381001/p/3031632 ; Tempesta 27644 rail set — https://www.dedeman.ro/ro/set-dus-para-furtun-suport-culisant-bara-grohe-tempesta-27644-3-functii-1-75-m/p/3017921 |

Glass: clear (default), smoked (fumé), frosted (sablată), patterned. Profiles: chrome, matte black, gold, white, grey.

## 7. Bathtub (cadă) — R\* / O

| Type | Dims | 3D attrs | Example |
|---|---|---|---|
| Rectangular built-in | 140/150/160/170/180 x 70/75/80, H 40–58 | front panel (mască) yes/no; tiled surround; drain end | Kolo Supero 170x70 — https://www.dedeman.ro/ro/cada-baie-rectangulara-acril-supero-5343-170x70-cm/p/3001041 ; West Elegancia 170x70, 1185 [S] — https://www.dedeman.ro/ro/cada-baie-dreptunghiulara-west-elegancia-acril-170-x-70-cm-suport-inclus/p/3003702 ; West Amalia 170x70, 943 [S] — https://www.dedeman.ro/ro/cada-baie-dreptunghiulara-west-amalia-acril-170-x-70-cm/p/3033880 |
| Freestanding | 155–180 x 66–80 | oval / rectangular / clawfoot (legs); free-standing tap | West Hades rect 178x80 — https://www.dedeman.ro/ro/cada-freestanding-rectangulara-west-hades-acril-alb-178-x-80-cm/p/3029269 ; Martplast Epoca oval with legs 168x80, 5655 [S] — https://www.dedeman.ro/ro/cada-baie-freestanding-ovala-martplast-epoca-compozit-suport-inclus-168-x-80-cm/p/3015189 ; West Roya oval 160x66, 4940 [S] — https://www.dedeman.ro/ro/cada-baie-freestanding-ovala-west-roya-acril-160-x-66-cm/p/3028741 |
| Asymmetric corner (left/right hand) | 140x90, 150x96–100, 160–170x100–120 | handedness L/R | West Artemide 150x100, 1184 [S] — https://www.dedeman.ro/ro/cada-baie-asimetrica-pe-colt-varianta-montaj-pe-dreapta-west-artemide-masca-si-suport-incluse-acril-150-x-100-cm/p/3024315 ; Cersanit Kaliope 170x110 — https://www.dedeman.ro/ro/cada-baie-asimetrica-pe-colt-varianta-montaj-dreapta-cersanit-kaliope-s301-115-170-x-110-cm/p/3030151 |
| Symmetric corner | 120x120 – 150x150 | quarter-round front | Category page: https://www.dedeman.ro/ro/cazi-cu-hidromasaj-de-colt/c/2083 |
| Bath screen (paravan) | 70–90 x 130–150; 1, 2 or folding panels; sliding over 180 | fixed / swing / folding; clear, smoked or frosted glass | K-711 80x140 — https://www.dedeman.ro/ro/paravan-cada-k-711-stanga/-dreapta-80-x-140-cm/p/3013556 ; West WST08 frosted 90x140, 1276 [S] — https://www.dedeman.ro/ro/paravan-cada-west-wst08-stanga/-dreapta-90-x-140-cm-sticla-securizata-sablata/p/3053708 |

## 8. Mirror / mirror cabinet — R

| Type | Dims | 3D attrs | Example |
|---|---|---|---|
| Plain or LED mirror, rectangular | 50–120 x 60–80 | frame: none, thin metal or backlit halo; touch switch; demister | — |
| LED round | Ø60/70/80/100 | backlit vs front ring; optional shelf | Goodmob Avonia Ø60, IP44, 726 [S] — https://www.dedeman.ro/ro/oglinda-baie-cu-led-goodmob-avonia-diametru-60-cm-actionare-touch-rotunda/p/3049631 ; Class Mirrors M8 Ø100 — https://www.dedeman.ro/ro/oglinda-baie-cu-led-class-mirrors-m8-diametru-100-cm-actionare-touch-rotunda-alb/p/3035789 ; H-501 80x65 with shelf, 749 [S] — https://www.dedeman.ro/ro/oglinda-baie-cu-led-h-501-80-x-65-cm-actionare-touch-rotunda-cu-etajera-alb/p/3033782 |
| Oval / arched / pill | 50–60 x 80–100 | arched top or rounded ends | Exists on the market (eMAG, Badezimmer); no Dedeman URL confirmed in this pass (UNKNOWN). |
| Mirror cabinet (dulap cu oglindă) | W 50–80 (up to 120), D 14–16, H 68–90; 1/2/3 doors | box depth; door count; LED strip/spots; side shelf | Neo 55x14x71, 1 door, ~260–288 [S] — https://www.dedeman.ro/ro/dulap-baie-cu-oglinda-iluminare-si-polita-1-usa-dreapta-neo-alb-55-x-14-x-71-cm/p/3022782 ; Wave 80x16x68, 3 doors, 577 [S] — https://www.dedeman.ro/ro/dulap-baie-cu-oglinda-wave-3-usi-alb-80-x-16-x-68-cm/p/3015432 ; Savini Due Fly 77, 3 doors with LED — https://www.dedeman.ro/ro/dulap-baie-cu-oglinda-si-iluminare-3-usi-savini-due-fly-77-x-16-x-71-cm/p/3021551 |

## 9. Towel radiator (calorifer port-prosop) — R (hydraulic on the heating loop, or electric)

| Type | Dims (W x H) | 3D attrs | Example |
|---|---|---|---|
| Straight ladder, hydraulic or electric | 40/45/50/60 x 70/80/120/150/180 (depth ~3–5) | tube profile round/flat/square; tube spacing; electric versions carry a heating element plus a control box at the bottom | Radox Scala 500x1200 electric, white — https://www.dedeman.ro/ro/calorifer-electric-baie-radox-scala-portprosop-drept-alb-500-x-1200-mm-accesorii-montaj/p/2033038 ; Radox Round 500x800 chrome — https://www.dedeman.ro/ro/calorifer-electric-baie-radox-round-portprosop-drept-cromat-500-x-800-mm-accesorii-montaj/p/2035018 ; Radox Aztec 600x1265 black designer, 3580 [S] — https://www.dedeman.ro/ro/calorifer-electric-baie-radox-aztec-portprosop-drept-negru-600-x-1265-mm-accesorii-montaj/p/2035174 |
| Curved ladder / designer vertical panel | similar sizes | bowed front or flat panel | Purmo, Riho and Radox designer ranges (no URL verified) |
| Towel rail / ring / hooks (no heat) | rail 45–80 | wall bar, ring, double bar | Dedeman accessories category (not individually verified) |

Finishes: white, chrome, matte black, anthracite, gold.

## 10. Storage — O

| Type | Dims | 3D attrs | Example |
|---|---|---|---|
| Tall column (wall-hung or floor) | 30–50 x 30–35 x 143–175 | 1–2 doors, open niche, push-to-open | Cersanit Smart S568-006 42x170 push — https://www.dedeman.ro/ro/dulap-baie-suspendat-cersanit-smart-s568-006-1-usa-alb-deschidere-push-170-x-42-cm/p/3021974 ; Frame 35x143 grey, 811 [S] — https://www.dedeman.ro/ro/dulap-baie-suspendat-frame-2-usi-rigoletto-gri-143-x-35-x-32-cm/p/3032235 |
| Wall cabinet above WC/washer | 50–65 x 22 x 68 | 2 doors | Badenmob Eco 17412 60x68x22, 235 [S] — https://www.dedeman.ro/ro/dulap-suspendat-pentru-masina-de-spalat-badenmob-eco-17412-2-usi-alb-60-x-68-x-22-cm/p/3056904 |
| Washing-machine surround (floor) | 64–97 x 27–30 x 190–195 | machine opening at the bottom, doors above | Badenmob Eco 19537 64x195x27, ~405 [S] — https://www.dedeman.ro/ro/dulap-pe-sol-pentru-masina-de-spalat-badenmob-eco-19537-2-usi-alb-64-x-195-x-27-cm/p/3056901 |
| Corner wall cabinet / shelves / tiled niche | 35 x 82; niche 30–60 x 30–40 x 8–10 | corner: quarter plan; niche: a recess in the wall mesh | Corner cabinet 35x82.5 — https://www.dedeman.ro/ro/dulap-baie-suspendat-montaj-pe-colt-1-usa-alb-35-x-82-5-cm/p/3000445 |

## 11. Accessories and services

| Item | Req | Dims / 3D | Example |
|---|---|---|---|
| Toilet-roll holder + brush | R | wall-mounted (~15 x 8) or free-standing stand (~20 x 14 x 65) | Kadda BIE-1186 matte-black stand, ~80 [S] — https://www.dedeman.ro/ro/suport-stativ-hartie-igienica-perie-wc-inclusa-kadda-bie-1186-fara-clapeta-negru-mat-20-x-14-x-67-cm/p/3053759 ; Kadda Square BSE-0174, 120 [S] — https://www.dedeman.ro/ro/perie-wc-cu-suport-pentru-hartie-igienica-kadda-square-bse-0174-metal-finisaj-negru-56-x-16-x-20-cm/p/3045898 |
| Soap dispenser, glass shelf (~50–60 x 12), corner shower shelf, towel hooks, laundry basket (~40 x 30 x 55) | O | small props; matched finish | Dedeman accessory categories (not individually verified) |
| Washing machine (+ dryer stacked in a column) | O (common in RO flats) | 60 x 55–60 x 85; stacked ~60 x 60 x 170 | see the Badenmob surround above |
| Electric water heater (boiler) | O (R where there is no gas boiler) | 50 L ~45 Ø x 60; 80 L ~45 Ø x 75–80; 100 L ~45 Ø x 95; flat/prismatic models ~50 x 27 D | Ariston Velis Tech 100 (80 L, 27 cm deep, IPX4), ~1799 [S] — https://www.dedeman.ro/ro/boiler-electric-ariston-velis-tech-100-afisaj-digital-ipx4-functie-eco-80-l-1500-w/p/2033764 ; Ariston Pro R 80 V (cylinder) — https://www.dedeman.ro/ro/boiler-electric-ariston-pro-r-80-v-80-l-1800-w/p/2018961 |
| Extractor fan | R (interior bathrooms) | Ø100/125 grille ~15 x 15 | Vents 100 M1TH, humidity sensor + timer, ~169 [S] — https://www.dedeman.ro/ro/ventilator-baie-axial-vents-100-m1th-cu-timer-cu-senzor-umiditate-plastic-ip34-14-w-2300-rpm-98-mc/h-d-100-mm/p/2004109 ; Vents 100 D, ~55 [S] — https://www.dedeman.ro/ro/ventilator-baie-axial-vents-100-d-cu-plasa-anti-insecte-plastic-ip34-14-w-2300-rpm-95-mc/h-d-100-mm/p/2005413 |
| Lighting (IP zones, IEC 60364-7-701) | R | Zone 0 = inside the bath/tray: IPX7, SELV 12 V only. Zone 1 = above the bath/tray up to 2.25 m: IPX4 (IPX5 where there are water jets). Zone 2 = 0.6 m outside zone 1: IPX4. Outside the zones: IP20 is acceptable, IP44 is recommended. Sockets: not within 0.6 m of the bath/shower and RCD 30 mA (I7 norm). | Use these as placement validation rules in the 3D planner. |

## Minimum standard set for a typical RO bathroom

R: WC (wall-hung + frame is now the default in new builds; floor monobloc in renovations) · washbasin + vanity 60–80 · basin tap · shower (walk-in or enclosure on a tray) **or** bathtub 170x70 with screen · shower/bath mixer + rail set · mirror or mirror cabinet · towel radiator · extractor fan · toilet-roll holder + brush · IP44 ceiling light.
O: bidet / WC with bidet function, freestanding tub, double basin, tall column, cabinet over the WC, washing machine (+ dryer), electric boiler, LED mirror with demister, niche shelves, laundry basket.

## Implications for the Casa3D product model (proposed)

Each element needs a `type` enum plus parametric fields that drive the mesh: `width, depth, height, projection`, `plan_shape (round|oval|rect|square|quadrant|pentagon|asym_L|asym_R)`, `mount (floor|wall|counter|under|semi|corner|freestanding)`, `finish/colour`, and the sub-options listed above (door type, glass type, profile colour, handle type, tap-hole count, rimless, seat type, flush-plate style). A product SKU then binds to a type plus its parameters and a GLB/Blender asset.

---
Gaps: no live verification of most prices; Hornbach, Leroy Merlin, Sanitino, Romstal, Obsentum and Badezimmer were not crawled (an Altex category page returned 503; Dedeman covered every category, so this pass stayed there). Pentagonal enclosures, oval/arched LED mirrors and the Purmo/Riho designer radiators have no confirmed RO URL. A follow-up pass on sanitino.ro or romstal.ro is recommended for premium brands (Villeroy & Boch, Duravit, Hansgrohe, Ravak).
