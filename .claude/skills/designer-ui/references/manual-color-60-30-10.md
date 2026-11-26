---
id: manual-color-60-30-10
title: Colour roles 60-30-10 (99 analysed palettes)
tier_min: T1
page_types: ["any"]
topics: ["color", "palette", "60-30-10", "accent", "contrast", "luxury-80-20", "anti-ai-look"]
data: data/quanova_palettes_99.csv, data/INDEX_palettes.md
---

# Color combination for web design and apps — learned from @quanovaai (Instagram), 2026-11-22

Source: https://www.instagram.com/quanovaai/ — all 203 posts read (owner's logged-in browser), 99 of them
are colour-palette reels. Every cover shows the palette as three stacked cards (name, %, CMYK/RGB/HEX).
Data: `data/quanova_palettes_99.csv` (99 rows, 80 with accent HEX, HEX↔RGB verified: 0 mismatches).
Contact sheets: `planse/sheet_00..12.png` (tile index = CSV idx).
Limitation: each reel shows 3 palettes in the video; only the cover palette (1 per reel) was transcribed.
The account offers the other two only via DM ("Comment COLOR").

## 1. The rule (repeated in all 99 captions)
"60% main, 30% second, 10% loud." The 10% is placed on ONE functional detail:
- product: "The label is the 10%", "Look at the handle", "Watch the knobs", "The scroll wheel does the work",
  "Look at the prop tips", "Watch the shutter button", "Look at the seconds hand", "The sticker does the selling";
- website: "The 10% is what you click", "The 10% is the button you tap to order";
- interiors: "60% walls, 30% sofa/desk/counter, 10% loud", "The 10% is the lamps and cups";
- brand: "The 10% is what makes it a brand", "The 10% is what you remember".
Experiment format: "Same site, same 60 and 30. Only the 10% changes. Which one wins: 1, 2 or 3?"
→ the accent alone changes the personality of the page.

## 2. What the 99 palettes show (statistics)
**60% main** — almost always a warm, food/textile-named light neutral: Mascarpone Cream, Crema Ivory,
Biscotti Cream, Travertine Cream, Panna Cotta, Oat Milk, Rice Paper, Bone Canvas, Ecru Linen…
Cool variants exist (Arctic Chalk, Mist Blue, Sage Fog, Pale Celadon, Seafoam Mist) and dark-mode ones
(Graphite Noir, Carbon Onyx, Obsidian Black, Raven Lacquer, Gunmetal Night, Midnight Navy).
Never pure #FFFFFF / #000000 as the named main.

**30% second** — deep, desaturated, "material" colours: lacquer, velvet, leather, suede, ink, wine:
greens (Highland, Pine Velvet, Wellington, Hunter, Forest, Juniper, Spruce Velvet, Ivy/Jade/Laurel Lacquer),
blues (Prussian Blue, Royal/Oxford/Ink/Steel Navy, Petrol Ink, Deep Indigo), browns (Cognac/Mahogany Leather,
Ganache, Truffle, Tobacco Suede, Espresso), wines (Bordeaux Lacquer, Merlot Velvet, Claret, Mulberry),
greys/blacks (Graphite Enamel, Charcoal Slate, Piano Black, Gunmetal). On a dark main, the second is
a metal: Glacier/Pearl/Sterling/Platinum Silver, Brushed Steel.

**10% accent** — 73 unique accents. Hue families: red 29, orange 17, pink/magenta 7, blue 6,
teal 5, green 4, yellow 3, purple 2. Saturation: min 0.74, median 1.00 (HSL) — always at full saturation.
→ Default: a warm red-orange accent (#EF4229 … #FF6A14) on cream + deep green/navy/brown.
Typical HEX: San Marzano #EF4229, Coral Rocket #FF5A4A, Tangerine Laser #FF6A14, Signal Orange #FF5F1F,
Safety Orange #FF5F00, Racing Red #E3262B, Rosso Corsa #D40000, Cherry Red #E3173E, Hot Pink #FF3EA5,
Magenta Blast #FF1F8A, Electric Blue #2563FF, Ultra Cobalt #2340FF, Electric Violet #7B4DFF,
Surf Turquoise #00C2B5, Aqua Volt #00E0C6, Acid Lime #C5FF1F, Venom Green #39FF7A, Sunflower #FFC93C.
Full list in the CSV.

**Premium variant (5 posts)**: "Luxury uses 2 colors, not 3. 80% main, 20% second. No loud color."
E.g. Graphite Noir + Glacier Silver #CDD1D6 (dark + metal).

## 3. Pairing patterns
- Temperature contrast: cool/dark second (green, navy, graphite) + warm accent (red, orange) — the most common.
  Warm second (cognac, chocolate) + cool accent (Azure/Cobalt blue, turquoise).
- Complementary pairs: green second ↔ red/tomato accent; navy ↔ orange; brown ↔ blue; graphite ↔ anything.
- Unified naming theme per palette (Mascarpone / Highland / San Marzano = Italian; Racing Red on carbon = automotive).
  A name helps the palette tell a story — useful when presenting to a client.
- "Six loud colors look cheap. 60% ivory, 30% royal navy (or espresso), 10% signal orange,
  and the same page looks expensive."

## 4. Signs of an "AI-made" site they warn against
- "Purple gradient, glass cards, emojis everywhere. 3 fixes: three colors (60-30-10), one bold font,
  no glass and one grid."
- From Anthropic's frontend-design skill (quoted by them): cream background + serif + clay-orange accent
  as an automatic default, identical cards with the same soft shadow, small all-caps labels above every
  heading, an arrow on every button.
  ⚠ Tension: cream + orange is exactly their favourite palette. Conclusion: the palette is not the problem;
  the template is. Pair it with a distinct typography and layout, and choose the accent from the brand,
  not by default.
- Design skills they recommend: Hallmark (Together AI), unslop, Oil UI ("premium" → concrete choices),
  Emil Kowalski's animation skills (200ms ease-out + reduced-motion), MotionSites.

## 5. Accessibility check (WCAG, my calculation over all 73 accents)
- Only 14/73 accents reach 4.5:1 with WHITE text; 57/73 reach it with dark text (#111).
- So: on an accent button use **dark text** (as their cards do), or darken the accent for the button background.
- Accent text on a light background only at a large size (≥24px or ≥18.66px bold → 3:1).
- In dark mode the accents pass easily.
Examples: Tangerine Laser #FF6A14 → white 2.87, #111 6.59; Surf Turquoise #00C2B5 → white 2.24, #111 8.44;
Azure Laser #1F7BFF → white 3.94, #111 4.80.

## 6. How to apply in a project (procedure)
1. Choose the mood → the 60% main (warm cream for premium/food/lifestyle; cool grey/mint for tech/health;
   dark for luxury/gaming).
2. Choose a deep, desaturated, material 30% second, at the opposite temperature to the accent.
3. Choose ONE saturated accent (S≈1). Decide in advance the exact element that gets it
   (primary CTA, logo, active state). Budget: 1 CTA per screen + at most 1–2 details.
4. Tokens by role: `--bg` (60), `--second` (30), `--accent` (10), `--on-accent` (dark),
   `--text`, `--muted`. Never more than 3 brand colours + neutrals for text.
5. Show the client 3 variants: the same 60/30, three different accents.
6. For "luxury": drop the accent, 80/20 dark + metal.
7. Check the contrast (≥4.5 body text, ≥3 large text/UI) before delivery.

## Dark editorial pages (iter2 advisor lessons; HEURISTIC)
- A dark editorial page runs near 85/10/5 (ink / paper / accent), not 60/30/10.
- The accent has one semantic job (plus the focus ring) and stays under about 8% of viewport pixels.
- Amber is never text on paper; white text on `#D97706` fails (about 3.2:1); use `#12100E` on `#F59E0B` (about 8.8:1).
- Every contrast figure is "advisor arithmetic" until measured: `#EDE8E2` on `#12100E` about 15.6:1; muted umber
  `#6E655D` about 3.3:1 = decorative/disabled only, never captions (use `#9E958C` or lighter for captions and comments).
- Marker on paper is the accent at .5-.7 alpha under ink marks; amber is never text on paper (use `--amber-ink`).

## Iteration 4 lessons (CANDIDATE; UNPROVEN until v9 is scored)
- Quote marker alphas: .32 on dark, .6 on paper (AR39).
- A tick field in `--text-3` replaces any muted fill that measures below 3:1.

## Iteration 5 lessons (CANDIDATE; UNPROVEN until v10 is scored)
- AR54: the 60% may change hue at constant luminance (`#0D1612`, raised `#141F1A`, overlay `#1C2923`); paper, ink and the amber 10% stay. Recompute every contrast pair.
