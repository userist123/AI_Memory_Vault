---
id: "47de1e29-cbd5-4a21-9697-995c2bab3ab8"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, rules]
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
# Casa3D: rules and numbers a design tool can check deterministically (RO/EU), research 2026-10-11

Scope: values a design tool can check or compute from plan data, with Romanian/EU sources first.
Method: WebSearch + WebFetch + direct download and `pdftotext` of primary PDFs where reachable. legislatie.just.ro refused connections from this sandbox (HTTP 502, then empty reply), so Romanian laws are cited from Monitorul Oficial scans where available, otherwise from secondary sources. Those secondary citations are marked.

Evidence labels:
- **PRIMARY**: read in the official text, a Monitorul Oficial scan or the manufacturer's own TDS or guideline.
- **SECONDARY**: a reputable secondary source (tax press, retailer, trade summary). Check against the official text before using it as a hard rule.
- **UNVERIFIED**: secondary sources only, conflicting, or not found in any text read. Use only as a soft hint.
- **MEMORY**: not found online in this session. Do not ship it without checking.

---

## 1. Ergonomics and clearances

### 1a. Kitchen: NKBA Kitchen Planning Guidelines with Access Standards (PRIMARY)
Source: https://kb.nkba.org/uploads/2022/05/Kitchen-Planning-Guidelines.pdf (NKBA, edition based on IRC 2015 and ICC A117.1-2009; file dated 2022-05; text extracted locally).
NKBA is a US association, so these are good-practice recommendations, not RO law.

| # | Rule | Value |
|---|---|---|
| G1 | Door clear opening | ≥ 813 mm (32″); access standard 864 mm |
| G2 | Door interference | Entry door must not hit appliances; appliance doors must not hit each other |
| G3 | Work triangle (hob, sink, fridge) | Sum ≤ 7.9 m (26′); each leg 1.2–2.7 m (4–9′); legs measured centre-front; a leg may cut an island or peninsula by ≤ 305 mm |
| G4 | Separation | No full-height, full-depth tall unit (tall oven, pantry, fridge) between two primary work centres |
| G5 | Traffic | No major traffic path through the triangle |
| G6 | Work aisle | ≥ 1067 mm for 1 cook; ≥ 1219 mm for several cooks (measured between counter fronts, tall units or appliances) |
| G6-A | Accessible | U-kitchen ≥ 1524 mm between arms; turning circle Ø 1524 mm; 762×1219 mm clear floor space at each appliance |
| G7 | Walkway | ≥ 914 mm; if two walkways meet at a right angle, one ≥ 1067 mm |
| G8 | Seating clearance behind diner | 813 mm (no traffic); 914 mm (edge past); 1118 mm (walk past) |
| G9 | Knee space per diner | 610 w × 457 d at 762 mm-high tables; 610×381 at 914 mm counters; 610×305 at 1067 mm counters |
| G11 | Sink landing | ≥ 610 mm on one side and ≥ 457 mm on the other; a landing area must be ≥ 406 mm deep and 711–1143 mm high |
| G12 | Prep area | Continuous ≥ 914 w × 610 d, next to the sink |
| G13 | Dishwasher | Nearest edge within 914 mm of the sink edge; ≥ 533 mm standing space between the dishwasher edge and any counter, appliance or cabinet at a right angle to it |
| G16 | Fridge landing | ≥ 381 mm on the handle side (or across, ≤ 1219 mm away) |
| G17 | Hob landing | ≥ 305 mm on one side and ≥ 381 mm on the other; island hob: ≥ 229 mm of counter behind |
| G18 | Hob vertical clearance | 610 mm to a protected non-combustible surface; code 762 mm to combustibles (IRC). The hood maker's figure overrides this (see 3b) |
| G19 | Hood | Ducted, ≥ 150 cfm (≈ 255 m³/h) recommended; IRC minimum 100 cfm; > 400 cfm needs make-up air |
| G20 | Hob safety | Not under an operable window |
| G21 | Microwave | Bottom ≤ 1372 mm above the floor; if under the counter, ≥ 381 mm above the floor |
| G22–23 | Microwave and oven landing | ≥ 381 mm |
| G24 | Combined landing | Longer requirement + 305 mm |
| G25 | Total countertop | 4013 mm of frontage, 610 mm deep, 381 mm clear above. Access standard: two heights, 711–914 and 914–1143 mm |
| G27 | Storage frontage | Small kitchen (< 14 m²) 35.56 m; medium (14–32.5 m²) 43.18 m; large 50.8 m |
| G30 | Sockets near sink | US code: GFCI within 1829 mm of the sink (not RO law; use the RO RCD rule in §2) |

### 1b. Kitchen furniture sizes: EN 1116 (SECONDARY)
- EN 1116:2018 fixed worktop heights are 850, 900 and 950 mm (+50 mm tolerance). Built-under appliance recess heights are min 820, 870 and 920 mm.
- Source: CATAS summary, https://catas.com/uploads/media/unien1116-eng.pdf. Standard listing: https://iteh.es/catalog/standards/cen/d99708f8-5c9c-40b2-8145-57990648bb9d/en-1116-2018
- Clause 4.2.4 sets the worktop-to-wall-unit distance, but the value was not found (UNVERIFIED).

### 1c. Bathroom: NKBA Bathroom Planning Guidelines (PRIMARY)
Source: https://kb.nkba.org/uploads/2022/05/Bath-Planning-Guidelines.pdf (same edition basis).

| # | Rule | Recommended | Code minimum (IRC) |
|---|---|---|---|
| B1 | Door clear opening | 813 mm | 610 mm door if the structure cannot change |
| B3 | Ceiling over fixture or shower | ≥ 2032 mm over a 762×762 mm area at the shower head | |
| B4 | Clear space in front of fixtures | 762 mm to any fixture, wall or obstacle | 533 mm in front of WC, basin, bidet, tub; 610 mm in front of a shower entry |
| B5 | Basin centreline to side wall | ≥ 508 mm | 381 mm; edge of free-standing basin to wall ≥ 102 mm |
| B6 | Double basins, centre to centre | ≥ 914 mm | 762 mm |
| B7 | Basin height | 813–1092 mm (to suit user) | access standard ≤ 864 mm |
| B9 | Shower interior | ≥ 914×914 mm | 762×762 mm and a Ø 762 mm disc must fit; entry ≥ 559 mm |
| B10 | Shower controls | 965–1219 mm above the floor; tub controls between rim and 838 mm | |
| B12 | Shower seat | 432–483 mm high, 381 mm deep | |
| B13 | Waterproof wall surround | 76 mm above the shower-head rough-in | 1829 mm above the floor |
| B14 | Grab bars | 838–914 mm above the floor; walls reinforced for 113 kg | |
| B20 | WC or bidet centreline to wall or fixture | ≥ 457 mm | 381 mm |
| B21 | Separate WC compartment | 914×1676 mm | 762×1524 mm; 533 mm clear in front |

### 1d. Romanian accessibility norm NP 051-2012 (PRIMARY, Monitorul Oficial scan)
Source: MO Partea I nr. 121 bis / 5.III.2013 (Ordin 189/2013), scan at https://primariasimeria.ro/storage/2024/05/cd387ccf-eb9b-4a7f-9359-f8a2c3e0cc08.pdf. Text is OCR'd, so some characters may be garbled. Approval context: https://anpd.gov.ro/web/accesibilitate/
The norm targets public buildings. For homes, use it as an optional "accessible design" check profile.

| Clause | Rule | Value |
|---|---|---|
| V.5.3 | Ramp max slope | 5–8% for level changes > 20 cm; up to 15% for < 20 cm; max run 10 m at 5% and 6 m at 8%; 1.50 m intermediate landing |
| V.5.3 / V.5.4 | Handrails | 90–100 cm (adults); 60–75 cm (children); extend 30 cm past the ends |
| V.5.4 | Access stair | Width ≥ 1.20 m (≥ 1.00 m between handrails); ≥ 3 steps; all steps equal; existing buildings accept a 26 cm tread / 18 cm riser |
| Annex checklist | Stair steps | "lățimea treptei maxim 34 cm, înălțimea contratreptei maxim 16 cm"; ≤ 12 steps per flight (OCR text, quoted as read) |
| V.5.5 | Entrance | Clear door opening ≥ 1.00 m; no threshold; 1.50×1.50 m landing; 60 cm beside the door on the handle side |
| V.6.1 | Accessible WC | 1.50×1.50 m manoeuvre space in front of the WC and the basin; side transfer space ≥ 90 cm, preferably 1.20 m (90 cm suits 65% of wheelchair users, 1.20 m suits 90%) |
| V.6.3 | Accessories (hooks, dispensers, etc.) | 80–100 cm high |
| V.6.4 | Bathroom door | Clear opening ≥ 80 cm; opens outward (or can be removed from outside); sliding door ≥ 80 cm allowed in dwellings |
| V.6.5 | Switches and sockets for accessible baths | Outside the room; sockets 40–120 cm high, switches 120 cm |
| V.6.x | Mirror | Lower edge ≤ 90 cm |
| V.6.x | Accessible shower | Transfer area 90×130 cm; fall to the drain ≤ 2%; room floor ≤ 1% toward the shower |
| Sec. 7 | Accessible kitchen | Manoeuvre space 1.50×1.50 m (1.35 m or 1.10–1.20 m in the listed layouts); worktops 80–85 cm; units 40–140 cm high; wall units ≤ 40 cm deep; base units 60 cm deep; switches and sockets at 1.20 m |

### 1e. Bedroom, wardrobe and stairs in homes (UNVERIFIED)
| Rule | Value | Status |
|---|---|---|
| Free space on the 3 open sides of a bed | 60 cm (RO retailer and design guides); some guides allow 50–55 cm on the less-used side | UNVERIFIED (search summary of RO lifestyle press, e.g. https://www.bzi.ro/dormitor-mic-nervi-mari-cum-alegi-patul-si-dulapul-ca-sa-castigi-spatiu-nu-sa-l-pierzi-5499411) |
| Clearance in front of a hinged-door wardrobe | ~90 cm with the door fully open; sliding doors ~60 cm | UNVERIFIED (retailer blog https://simbasleep.com/blogs/night-life/how-much-space-do-you-need-around-bed). Better: compute door leaf width + 50–60 cm walkway from the actual door geometry |
| Stair comfort rule 2R + G | 600–640 mm (≈ 630); attributed to Blondel/Neufert | UNVERIFIED (https://www.upstairs.com/en-nl/staircase-inspiration/news/how-does-the-staircase-formula-work/ and calculators; sources disagree: 600–630, 600–640, 550–700). No RO residential stair norm text was found (NP 068 not reached) |

---

## 2. Electrical (I7-2011, IEC/HD 60364-7-701, lighting)

**Status:** the official I7-2011 text ("Normativ pentru proiectarea, execuția și exploatarea instalațiilor electrice aferente clădirilor", amended in 2023) could not be retrieved. Every I7 number below is SECONDARY, and the sources conflict on one point.

### 2a. Bathroom volumes (HD 60364-7-701, adopted by I7)
| Item | Value | Status / source |
|---|---|---|
| Volume 0 | Inside the bath or shower tray; trayless shower: 10 cm high over the zone 1 footprint | SECONDARY: https://www.bransamente-instalatii-electrice.ro/instalatii-electrice-in-baie/ |
| Volume 1 | Vertical above the tub/tray edge up to the higher of 2.25 m or the shower head; trayless shower: radius 1.20 m from the fixed shower head; the space under the tub is volume 1 | SECONDARY: same source, https://electricalc.ro/blog/instalatii-electrice-in-incaperi-cu-cada-de-baie-sau-dus, and UK 701 guides (https://www.voltimum.co.uk/articles/section-701-locations-containing-bath-or) |
| Volume 2 | 0.60 m horizontal strip beyond volume 1, up to 2.25 m; none for trayless showers | SECONDARY (same) |
| IP rating | Volume 0 IPX7; volumes 1 and 2 IPX4 (IPX5 where water jets are used for cleaning) | SECONDARY, agreed across sources. electricalc.ro says IP67 / IP54, so it conflicts |
| Sockets | None in volumes 0 and 1. Volume 2: only SELV/PELV or a shaver unit (EN 61558-2-5) | SECONDARY. **Conflict:** devizeroo.ro says "allowed in zone 2 with IP X4". The IEC/UK texts and electricalc.ro say no. Treat as forbidden |
| UK-only distance | 230 V sockets ≥ 3 m horizontally from the volume 1 boundary (BS 7671:2008 701.512.3) | Not RO, do not apply |
| RCD (DDR) | ≤ 30 mA on all bathroom circuits (exceptions: SELV/PELV, electrical separation) | SECONDARY (bransamente-instalatii-electrice.ro; voltimum) |
| Cable depth | Wiring in walls bounding volumes 0–2 recessed ≥ 5 cm, unless it has an RCD or other listed protection | SECONDARY |
| SELV limits | Volume 0: 12 V AC / 30 V DC; volumes 1–2: 25 V AC / 60 V DC | SECONDARY |

### 2b. General I7 numbers (SECONDARY / UNVERIFIED)
From https://devizeroo.ro/ghid-normativ (self-described as an informative guide). It cites amending orders 2.741/2011 and 959/2023.

| Item | Value |
|---|---|
| Socket height in homes | min 25 cm above the finished floor (UNVERIFIED) |
| Switch height | 80–120 cm (UNVERIFIED) |
| Lighting circuit | 1.5 mm² Cu, 10 A |
| Socket circuit | 2.5 mm² Cu, 16 A |
| Cooker / oven, electric boiler | 4 mm², 20 A |
| Air conditioning | 2.5 mm², 16 A |
| Apartment main column | 6 mm², 32 A |
| Free space in front of a panel | 0.7 m |

Not found in any source: the number of circuits per room, sockets per circuit, and distance rules from the sink or hob. **MEMORY:** I7 commonly limits sockets per circuit and requires dedicated circuits for large appliances. Do not ship numeric thresholds until the I7 text is read.

### 2c. Lighting levels
| Space | Em (lx) | Status / source |
|---|---|---|
| Bathrooms, toilets | 200 (UGR 25, Ra 80) | PRIMARY. NP 061-2002, Annex 2 "Arii comune ale clădirilor" (https://www.misiuneacasa.ro/uploads/files/normative4.pdf). NP 061 was amended by Ord. 205/2023 (https://lege5.ro/gratuit/geztgnzqhaydm/...); the amended table was not checked |
| Corridors, circulation | 100 | PRIMARY (NP 061, same table) |
| Stairs | 150 (NP 061, non-residential); residential stairs ≥ 50 lx | PRIMARY (NP 061); Trilux https://trilux.com/en/lighting-practice/indoor-lighting/specific-lighting-requirements/lighting-of-further-indoor-spaces/residential-areas |
| Rest rooms | 100 | PRIMARY (NP 061) |
| Kitchens (restaurants and hotels section) | 500 on the work surface | PRIMARY (NP 061). This is a commercial kitchen value, an upper reference for home worktop task light |
| Home living, dining, bedroom | No standard: "Design and lighting for living areas cannot be standardised" (Trilux on EN 12464-1); home office falls under EN 12464-1 workplace values | PRIMARY statement (Trilux) |
| Home targets often quoted | Living 100–300; kitchen general 300, task up to 500–750; bath 200–300 | UNVERIFIED (retail guides). Ship as editable defaults, not rules |

**Lumen method** (standard textbook form, SECONDARY, e.g. https://www.scribd.com/document/153489370/Lumen-Method):
N = ceil( E × A / (Φ_luminaire × UF × MF) )
- E: target lux. A: room area at the working plane. Φ: luminaire lumens from the datasheet. UF: utilisation factor, about 0.4–0.6 for small rooms (needs room index and reflectances; default 0.5 is an assumption). MF: maintenance factor, 0.8 typical (sources range 0.75–0.9).
- Worked check: 500 lx × 120 m² / (2720 × 0.5 × 0.8) = 55.1, so 56 luminaires.

---

## 3. Ventilation, plumbing, gas

### 3a. I5-2022 ventilation (PRIMARY)
Source: MO Partea I nr. 108 bis / 8.II.2023, https://drimand.ro/download/43%20NORMATIV%20I%205%20-%202022.pdf

Table 6.1.1.1: extract flows for dwellings (m³/h)

| Main rooms | Kitchen | Bath / shower (with or without WC) | Other shower room | WC, single | WC, multiple |
|---|---|---|---|---|---|
| 1 | 75 | 15 | – | – | – |
| 2 | 90 | 15 | 15 | 15 | 15 |
| 3 | 105 | 30 | 15 | 15 | 15 |
| 4 | 120 | 30 | 15 | 30 | 15 |
| ≥ 5 | 135 | 30 | 15 | 30 | 15 |

Table 6.1.1.2: minimum total and minimum kitchen flow when reduced by individual dampers (m³/h)

| Main rooms | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|
| Total | 35 | 60 | 75 | 90 | 105 | 120 | 135 |
| Kitchen | 20 | 30 | 45 | 45 | 45 | 45 | 45 |

Other provisions:
- **6.1.1(8):** an open-plan living room plus another room without a partition counts as 2 main rooms.
- **6.1.1(11):** in collective mechanical extract systems, a kitchen hood must **not** be connected to the common duct.
- **6.1.1(12):** boilers and water heaters must not connect to ventilation extract ducts.
- **6.1.1(13):** each main room needs at least one air inlet device.
- **6.1.1(5):** transfer air passes under doors or through transfer grilles.
- **Table 4.3.2.1 (general design values):** kitchen extract > 72, default 108 m³/h; WC > 24, default 36 m³/h per room, or 7.2 m³/h per m² of floor.
- **Annex (air changes):** home kitchens 15–20 h⁻¹.

### 3b. Hood mounting height (manufacturer manuals, SECONDARY)
- Hood bottom above the pan surface: gas or mixed hob ≥ 65 cm, electric hob ≥ 50 cm (Bauknecht/Whirlpool manuals, https://www.manualsdir.com/manuals/19940/bauknecht-df-5563-in.html).
- Other makers ask more: Privileg 65 cm for electric too; Scholtes 65 cm electric and 70 cm gas (https://www.scholtes.com/wp-content/uploads/2020/11/Manuel_SOHB1930-HOTTE-BOX_-WALL-BOX-HOOD.pdf).
- Rule for the tool: use the larger of the hood manual and hob manual values. Default 65 cm for gas and 50 cm for electric only as a fallback.

### 3c. Gas (UNVERIFIED)
- ANRE Ord. 89/2018: art. 128 minimum room volume 7.5 m³ for baths and kitchens with gas appliances. This comes from a search summary of lege5.ro; the PDF download failed. UNVERIFIED.
- The "≥ 60 cm between gas boiler and cooker" rule is attributed to ANRE/ISCIR by commercial sites only. UNVERIFIED.
- Only ANRE-authorised firms may modify gas installations (SECONDARY). The tool should flag any move of a gas hob or boiler as "needs an authorised gas installer".

### 3d. I9-2022 sanitary installations
- Approval order seen in search results; text not reached. No numbers taken from it.

---

## 4. Material quantity rules (manufacturer TDS)

| Material | Rule / value | Status / source |
|---|---|---|
| Tile adhesive, Ceresit CM 11 Plus (RO TDS 07/2025, signed 2025-10-21) | Up to 10 cm tile: 4 mm notch, 2.0 kg/m². ≤ 15 cm: 6 mm, 2.7. ≤ 25 cm: 8 mm, 3.4. ≤ 30 cm: 10 mm, 4.2. > 30 cm: 12 mm, 4.8. Exterior double-buttering 4–6 kg/m². Contact ≥ 65% interior. Bags 25 kg and 5 kg | PRIMARY: https://datasheets.tdx.henkel.com/CERESIT-CM-11-PLUS-ro_RO.pdf |
| Tile adhesive, Ceresit CM 17 (RO TDS) | 1.4–4 kg/m² (table by tile size) | PRIMARY: https://datasheets.tdx.henkel.com/CERESIT-CM-17-ro_RO.pdf |
| Grout, Ceresit CE 40 (RO TDS 08/2025) | 5×5 cm, 2 mm joint 0.5 kg/m²; 5×5, 3 mm 0.8; 10×10, 2 mm 0.4; 15×15, 3 mm 0.4; 10×20, 3 mm 0.4; joints up to 8 mm | PRIMARY: https://datasheets.tdx.henkel.com/CERESIT-CE-40-ro_RO.pdf |
| Grout formula, Mapei Ultracolor Plus (UK TDS) | kg/m² = (A+B)/(A×B) × C × D × 1.6, with A and B the tile sides, C the thickness and D the joint width, all in mm. Example: 200×200×7, 3 mm joint = 0.34; the TDS table gives 0.3 | PRIMARY: https://www.tilemountain.co.uk/pdf/Ultracolor_Plus_Datasheet.pdf |
| Paint, Savana Ultra Rezist (RO TDS Ed.4 Rev.2, 17.03.2020) | 13–15 m²/l per coat; 2 coats; coat 1 diluted ~14% water, coat 2 ~7%; prime porous supports | PRIMARY: https://nouatei.ro/wp-content/uploads/2021/10/880_fisa-tehnica.pdf |
| Paint, Savana Superalbă (retail listing) | 14–16 m²/l per coat; 2 coats; max 10% dilution; primer required | SECONDARY: https://www.dedeman.ro/ro/vopsea-superlavabila-interior-savana-superalba-alb-15-l-amorsa-4-l/p/5014502 |
| Paint, Dulux EasyCare (AkzoNobel TDS v7, July 2025, South African site) | 8–10 m²/l; 2–3 coats on new surfaces. Retail listings (UK) say 13–15 m²/l, so the manufacturer figure is more conservative | PRIMARY for that market: https://msp.images.akzonobel.com/prd/dh/ezadlx/documents/b5/2c/b7/08/dulux_easycare.pdf |
| Gypsum skim, Baumit FinoFinish (RO TDS) | ~1.6 kg/m² per mm; max 3 mm in 2 layers | SECONDARY (search summary of https://maxbau.ro/cs-content/cs-docs/FT_FINOFINISH-9752-.pdf) |
| Drywall W111-type wall (per m² of wall, board on both faces, CW at 600 mm, incl. 5% loss) | ≈ 2.10 m² board, 2.50 m CW, 0.80 m UW, 29 screws (25 mm), 0.90 kg joint compound, 3 m tape, 1.05 m² mineral wool. At 400 mm spacing: 3.40 m CW, 39 screws | SECONDARY: Knauf Brazil catalogue https://api.aecweb.com.br/cls/catalogos/saraiva/paredesknauf.pdf. A German calculator gives similar values (https://www.deutschland-rechner.de/trockenbau-rechner). The Knauf RO sheet was not found |
| Laminate perimeter gap | Egger: ≥ 8–10 mm, more for large rooms; skirting covers up to 17 mm | SECONDARY (Egger support page snippet; fetch returned 403) |
| Laminate max run without movement joint | Quick-Step: 13 m (roughly square room, stable climate); 8–10 mm gap at walls, doors, pipes, thresholds. Pergo also says 13 m | PRIMARY (Quick-Step FAQ https://www.quick-step.co.uk/en-gb/frequently-asked-questions/laminate/preparation-and-installation/what-is-the-maximum-floor-length-i-can-install-without-expansion-gap). Egger's own limit not found |
| Tile waste allowance | Straight ~10% (5–7% large format); diagonal ~15% (up to 25%); herringbone 10–20% | UNVERIFIED (retail calculators, e.g. https://www.flooringclarity.com/how-much-overage-tile-how-much-extra-tile/). Ship as editable defaults; better to compute cuts from the actual layout |

---

## 5. Business and legal (RO)

### 5a. Building permit, Legea 50/1991
**Art. 11(1): works allowed without a permit** if they do not change the load-bearing structure or the architectural appearance:
- **b)** replacing interior or exterior joinery (doors, windows), same openings; not on historic monuments.
- **d)** interior painting and plastering.
- **f)** repairs to interior installations; installing approved boilers, individual AC units, meters.
- **g)** repairing or replacing floors.
- **h)** interior and exterior finishes without changing quality or form.
- **m)** **temporary, non-structural partitioning** ("compartimentări provizorii nestructurale").

**Art. 11(3):** for buildings under art. 3 lit. b (historic monuments, their protection zones, protected areas), the exemptions do not apply, except lit. e and lit. j.
Source: SECONDARY, https://legeaz.net/legea-50-1991/art-11. That page reflects amendments only up to OUG 214/2008, so check the letters against the current consolidation.

**Permanent changes need a permit** (removing or modifying walls, merging rooms):
- Legea 166/2023 (MO 498 / 7.06.2023) reportedly simplified the file for interior re-partitioning: architecture memo, survey (releveu), proposal, plus the original designer's approval or a technical expertise.
- SECONDARY: https://www.euroavocatura.ro/legislatie/1842/... , https://www.antena3.ro/actualitate/lucrari-modificare-apartament-amenzi-10000-lei-autorizatie-constructie-667366.html

**Fines:** 1,000–100,000 lei for works without a permit (SECONDARY, https://www.capital.ro/reguli-autorizatie-de-constructie-ce-lucrari-pot-fi-facute-legal-fara-document-poti-primi-amenda-de-100-000-de-lei.html).

### 5b. VAT
- **Rate:** 21% standard from **1 Aug 2025** (Legea 141/2025, MO 699 / 25.07.2025), up from 19%. The reduced rate is a single 11%, replacing 5% and 9%.
- **Advances:** the rate at the delivery or service date applies.
- Sources (SECONDARY, consistent): https://www.fiscalitatea.ro/majorare-tva-din-1-august-2025-ce-cote-se-aplica-pentru-avansuri-exemple-utile-24307/ , https://tvrinfo.ro/romania-intra-in-august-cu-tva-de-21-si-accize-mai-mari-scumpiri-la-transport-comunicatii-si-alimente/
- **VAT exemption threshold:** 395,000 lei turnover (art. 310 Cod fiscal as amended by OG 22/2025, from 1 Sep 2025, still valid in 2026). SECONDARY: https://www.fiscalitatea.ro/plafoane-fiscale-2026-24595/
- **Micro-enterprise threshold 2026:** 100,000 EUR (509,850 lei at 5.0985). SECONDARY: same source.

### 5c. e-Factura (SECONDARY)
- **Who and when:** B2C mandatory from **1 Jan 2025** for all RO-established taxable persons, VAT-registered or not (OUG 120/2021 as amended by OUG 69/2024). This covers designers working as PFA, II or SRL; the PFA case is an inference from "persoană impozabilă".
- **Buyer code:** if the consumer gives no CNP, use a 13-zero code (OUG 138/2024, which also removes the duty to collect CNPs).
- **Deadline:** 5 **working** days from issue from 1 Jan 2026 (previously 5 calendar days). The working-days change is attributed to OUG 89/2025 (Dec 2025; act not read).
- **Fines:** 1,000–10,000 lei by taxpayer category.
- **Exemption:** simplified invoices (≤ 100 EUR incl. VAT, art. 319(12)) do not go through e-Factura.
- Sources: https://www.fiscalitatea.ro/e-factura-b2c-23791/ , https://contabilul.manager.ro/a/29150/e-factura-b2c-noutati-in-2025-sanctiuni-si-studii-de-caz.html , https://newsweek.ro/economie/termen-ro-efactura-5-zile-lucratoare

### 5d. Design-services contract essentials
- **Copyright (Legea 8/1996):** an assignment contract should state the object, which rights are assigned, exclusive or not, duration and territory, remuneration, confidentiality and termination. SECONDARY: https://www.undelucram.ro/ro/cumlucram/tot-ce-trebuie-sa-stii-despre-contractul-de-cesiune-drepturi-de-autor-1620
- **Typical clauses:** scope per phase, number of revision rounds, payment plan, delay penalties, copyright, GDPR. Seen in a commercial template: https://www.rubinian.com/contracte_modele_contract.php?id=PCON192. Not verified.
- **Civil Code basis:** the antrepriză contract articles in the Cod civil (MEMORY, not checked: art. 1851 and following).

### 5e. Market prices (SECONDARY, indicative only)
**Design fees**
- Anecdotal range: 15–100 €/m²; the 15 €/m² packages include 3D renders. Source: Facebook groups via https://playtech.ro/2026/cat-costa-un-designer-de-interior-pentru-mobilarea-unui-apartament-de-3-camere-in-bucuresti-preturi-actualizate-pentru-2026/
- Full fit-out budget: 500–800 €/m², up to 1,500 €/m² premium (MiSo Architects via https://www.forbes.ro/bugetele-pentru-design-interior-au-crescut-cu-50-in-ultimii-ani-338411).

**Labour (manoperă), lei/m²**

| Work | 2025 guide (playtech) | 2026 guide (brig.ro) |
|---|---|---|
| Tiling | ~100 | Standard 63–85; diagonal 75–130; 60×120 format 100–150 |
| Tile removal | ~35 | – |
| Painting | 25–40 | – |
| Laminate fitting | 60–120 | – |
| Interior plaster | – | 45–60 |
| Drywall, labour only | – | Walls 12–30; ceilings 18–50 |
| Drywall, labour + materials | – | Single wall 45–90; double insulated 70–175; false ceiling 87–180; full with finish 110–300 |

- City uplift: brig.ro says +30–40% in Bucharest and Cluj for tiling, +10–20% in Bucharest for drywall.
- Sources: https://playtech.ro/2025/renovarea-locuintei-in-2025-cat-costa-investitia-la-ce-preturi-sa-te-astepti-pentru-materiale-de-constructii-si-manopera/ , https://brig.ro/cat-costa/montaj-gresie , https://brig.ro/cat-costa/rigips
- Use: a seed price table the designer can edit. Never a hard rule.

---

## 6. Deterministic checks and calculators Casa3D should implement

Confidence: **H** = primary text read; **M** = primary for a related market, or consistent secondaries; **L** = conflicting or unverified (ship as a soft hint, editable).

1. **Kitchen work triangle** (H, NKBA G3/G5)
   - Input: centre-front points of hob, sink, fridge; obstacles; traffic paths.
   - Rule: sum ≤ 7.9 m; each leg 1.2–2.7 m; a leg crosses an island ≤ 305 mm; no tall unit between two centres (G4).
   - Output: warning per failed leg.
2. **Kitchen aisle and walkway** (H, NKBA G6/G7)
   - Input: facing fronts (counter, tall unit, appliance).
   - Rule: ≥ 1067 mm (1 cook) / ≥ 1219 mm (several cooks); walkway ≥ 914 mm.
   - Output: warn and show the actual distance.
3. **Appliance door and standing clearance** (H, NKBA G2/G13/G21)
   - Input: dishwasher, oven and fridge doors as swing arcs or drawers.
   - Rule: dishwasher ≤ 914 mm from the sink edge; ≥ 533 mm standing space at a right-angle corner; no door-to-door or door-to-entry collision; microwave bottom ≤ 1372 mm, or ≥ 381 mm if under the counter.
4. **Landing areas** (H, NKBA G11/G16/G17/G22–G24)
   - Input: countertop segments and appliances.
   - Rule: sink 610 + 457; hob 305 + 381; fridge, oven and microwave 381; combined = longer + 305; counter ≥ 406 deep and 711–1143 high to count.
   - Output: per-appliance pass/fail.
5. **Worktop and recess heights** (M, EN 1116)
   - Rule: worktop ∈ {850, 900, 950} +50 mm, otherwise "non-standard"; built-under recess ≥ worktop − 30 mm (820/870/920).
6. **Hood placement** (M/L, manuals + NKBA G18–G20)
   - Rule: hood-to-hob distance ≥ max(hood manual, hob manual), fallback gas 650 / electric 500 mm; hob not under an operable window; warn if ducting goes to a common building extract duct (I5-2022 6.1.1(11), H).
7. **Bathroom fixture clearances** (H, NKBA B4/B5/B6/B9/B20/B21)
   - Rules:
     - WC/bidet centre to wall or fixture: 381 mm hard minimum, 457 mm recommended.
     - Basin centre to wall: 381 mm minimum, 508 mm recommended.
     - Double basins: 762 mm minimum, 914 mm recommended.
     - Front clear space: 533 mm minimum (610 in front of a shower), 762 mm recommended.
     - Shower interior: ≥ 762×762 mm, 914×914 mm recommended; entry ≥ 559 mm.
   - Output: two levels, error below minimum and warning below recommended.
8. **Bathroom electrical volumes** (M, HD 60364-7-701 via RO secondaries)
   - Input: tub or tray outline or fixed shower head position; sockets, switches, luminaires with IP.
   - Rule:
     - Build volume 1 (footprint to 2.25 m, or 1.20 m radius for trayless showers) and volume 2 (+0.60 m).
     - Sockets and switches forbidden in volumes 0–1; in volume 2 only SELV or a shaver unit.
     - Fixture IP ≥ X7 / X4 / X4 by volume.
     - Mark circuits as needing a ≤ 30 mA RCD.
   - Output: error plus "verify with an ANRE-authorised electrician (I7 text not verified)".
9. **Electrical schedule sanity** (L, I7 secondary)
   - Input: sockets, switches, appliances.
   - Rule: socket circuits 2.5 mm²/16 A; lighting 1.5 mm²/10 A; oven/hob/boiler 4 mm²/20 A on a dedicated circuit; switch height 80–120 cm; socket ≥ 25 cm.
   - Output: soft hints only, editable presets; no sockets-per-circuit limit until I7 is read.
10. **Lighting calculator, lumen method** (H formula; L for home targets)
    - Input: room area, target lux (default from a room-type table: bath 200 from NP 061; corridor 100; stairs 150; kitchen task 500; living 100–300 editable), luminaire lumens, UF (default 0.5), MF (default 0.8).
    - Output: N = ceil(E·A/(Φ·UF·MF)) and the achieved lux for the luminaires placed.
11. **Ventilation check** (H, I5-2022 Tables 6.1.1.1 / 6.1.1.2)
    - Input: number of main rooms (an open plan counts as 2), kitchen, baths, WCs.
    - Output: required extract m³/h per room and total; compare with the hood or fan rated flow; flag a windowless bath with no fan.
12. **Permit triage** (M, Legea 50/1991 art. 11)
    - Input: change list (finishes, floors, joinery same openings, installation repairs = exempt; new or removed walls, structural openings, façade or joinery opening changes, works on a historic monument or in a protected zone = permit required).
    - Output: "Fără AC" / "Necesită AC — consultați arhitect/primărie" with the article cited. Never a legal guarantee.
13. **Tile, adhesive, grout calculator** (H)
    - Tiles = ceil(area × (1 + waste) / box coverage); waste preset straight 10%, diagonal 15%, herringbone 15–20% (L, editable).
    - Adhesive = area × CM 11 Plus kg/m² by tile size → 25 kg bags.
    - Grout = area × (A+B)/(A·B) × C × D × 1.6 (Mapei formula; CE 40 table as a cross-check).
14. **Paint calculator** (H for Savana Ultra Rezist; M otherwise)
    - Litres = wall area net of openings × coats / spreading rate.
    - Default 2 coats; rate per product TDS (Savana Ultra Rezist 13–15, Savana Superalbă 14–16, Dulux EasyCare 8–10 m²/l). Use the lower bound for ordering; add primer for new or porous plaster.
15. **Laminate and drywall quantities** (M)
    - Laminate: perimeter gap 8–10 mm; warn when a run is > 13 m without a joint (Quick-Step) or crosses a doorway without a profile.
    - Drywall per m² of wall: 2.10 m² board, 2.5 m CW at 600 mm (3.4 m at 400 mm), 0.8 m UW, 29 screws, 0.9 kg compound, 3 m tape.
16. **Offer and invoice compliance** (H for the 21% rate; M for e-Factura)
    - VAT 21% (11% reduced) for dates ≥ 2025-08-01; tax point by delivery date.
    - Flag B2C invoices that must go to e-Factura within 5 working days, with buyer code "0000000000000" when there is no CNP.
    - Warn when the designer's 12-month turnover approaches 395,000 lei (VAT exemption limit).
17. **Accessible-design profile, optional** (H, NP 051-2012)
    - Turning 1.50×1.50 m in front of WC and basin; WC side transfer ≥ 0.90 m (1.20 preferred); bath door ≥ 80 cm opening outward; worktops 80–85 cm; switches 1.20 m; ramp ≤ 8% (≤ 15% under 20 cm rise); handrails 90–100 cm.

---

## Summary
1. Strongest sources read in full: NKBA kitchen and bath guidelines (PDF text), NP 051-2012 (MO 121 bis/2013 scan), I5-2022 (MO 108 bis/2023), NP 061-2002 Annex 2, and manufacturer TDS for Ceresit CM 11 Plus, CM 17, CE 40, Mapei Ultracolor Plus, Savana Ultra Rezist and Dulux EasyCare.
2. legislatie.just.ro was unreachable from the sandbox. Legea 50/1991, Legea 141/2025, OUG 120/2021, 69/2024, 138/2024 and 89/2025 are cited from secondary tax and legal press.
3. I7-2011 (electrical) official text was not obtained. Bathroom volumes match HD 60364-7-701 across secondaries; socket and switch heights, cable sizes and per-circuit limits stay UNVERIFIED. One conflict: sockets in volume 2 (treat as forbidden).
4. Home lighting has no binding RO/EU lux standard (EN 12464-1 excludes living areas). NP 061 gives 200 lx for baths, 100 lx for corridors, 150 lx for stairs. Home values are editable defaults; the lumen method itself is standard.
5. Ventilation is the most "code-ready" RO rule: I5-2022 tables give exact m³/h by number of main rooms, and the hood must not connect to a collective extract duct.
6. Material calculators can be fully deterministic from TDS values. Tile waste percentages and labour rates are market conventions; ship them as editable presets with their source dates.
7. Business rules confirmed by consistent secondaries:
   - VAT 21% from 2025-08-01.
   - e-Factura B2C from 2025-01-01 with a 13-zero code; 5 working days from 2026.
   - VAT exemption threshold 395,000 lei.
   - Interior finishes and floors and non-structural temporary partitions are exempt from a permit; permanent re-partitioning needs a permit (simplified by Legea 166/2023).
8. Design fees (15–100 €/m²) and labour rates (tiling 63–150, painting 25–40, drywall labour 12–50 lei/m²) are anecdotal or aggregator data. Use them only as seed prices.
9. Recommended first wave: checks 1–4 and 7 (kitchen and bath NKBA), 8 (bath volumes), 10 (lumen), 11 (I5 ventilation), 13–14 (tile and paint), 16 (VAT and e-Factura).
10. Before shipping as hard rules, a human should read the official I7-2011 (+2023), the current Legea 50/1991 art. 11 consolidation, and the NP 061 amendment from Ord. 205/2023.
