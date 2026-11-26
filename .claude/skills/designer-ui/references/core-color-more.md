---
id: core-color-more
title: Colour (intermediate)
tier_min: T3
page_types: ["any"]
topics: ["modern-palette", "quote-contrast", "de-familiarise-by-subtraction", "monochrome-plus-accent", "oklch-palettes", "quote-marker", "three-colour-editorial", "tinted-grays"]
rules: 10
---

# Colour (intermediate)

Deeper colour techniques for products.
Support = number of independent educators who teach the rule.

## modern-palette
- Map every neutral and accent hue onto a 12-step semantic scale where each index has a fixed functional UI role across both light and dark modes. _(support 1, intermediate, R0826)_
  - values: Radix Colors 12-step architecture: Step 1 = App background; Step 2 = Subtle background (sidebars/cards/striped rows); Step 3 = UI component resting background; Step 4 = Hovered component background; Step 5 = Active/selected component background; Step 6 = Subtle borders/dividers; Step 7 = UI element border & focus rings; Step 8 = Hovered UI element border; Step 9 = Solid accent background (primary buttons/badges); Step 10 = Hovered solid accent background; Step 11 = Low-contrast secondary text (advisory APCA Lc >= 60); Step 12 = High-contrast primary text (advisory APCA Lc >= 90).
  - don't: Do not use Steps 9–10 for large container backgrounds or use Steps 6–8 for text, as they violate the contrast architecture of the scale. | codex: define colour by semantic role and use the same colour consistently for the same meaning.
- Alternatively, when using a 10-step functional token scale, assign Steps 1–3 to component surfaces, Steps 4–6 to borders, Steps 7–8 to high-contrast solid fills, and Steps 9–10 to secondary and primary text. _(support 1, intermediate, R0827)_
  - values: Vercel Geist 10-step color system: Background 1 & 2 (page canvas: #FFFFFF / #FAFAFA in light, #000000 / #0A0A0A in dark); Steps 1–3 = default/hover/active component backgrounds; Steps 4–6 = default/hover/active borders; Steps 7–8 = solid high-contrast fills; Step 9 = secondary text/icons; Step 10 = primary high-contrast text.
  - do: Alternatively, when using a 10-step functional token scale, assign Steps 1–3 to component surfaces, Steps 4–6 to borders, Steps 7–8 to high-contrast solid fills, and
  - don't: Do not mix 10-step (Geist/Tailwind) and 12-step (Radix) numbering indices within the same token namespace without semantic aliases (e.g., --bg-subtle, --border-default, --text-primary).

## quote-contrast
- Measure the rendered contrast of quote text against its final surface, including any overlay, image or texture: 4.5:1 for normal text and 3:1 for large text. _(support 1, intermediate, R0708)_
  - values: WCAG large text = 18pt regular or 14pt bold; no serif-specific ratio exists.
  - do: Check the actual pixels, treat italic serif as large text only when it meets the size criterion, and recheck after any fill or grain change.
  - don't: Do not assume large-looking italics qualify as large text, and do not use an alpha value as a substitute for the contrast check.
- Never dim a dark-mode quote with `opacity` on its container; use alpha colors for the fill and border so the text stays at full strength. _(support 1, intermediate, R0709)_
  - values: container `opacity: 1`; inactive text at .6 or more.
  - do: Keep the container at `opacity: 1`, use `rgba` or `hsla` for background and border, and keep any dimmed secondary text at alpha .6 or more after measuring.
  - don't: Do not use container opacity except briefly in enter and exit animations.
  - caveat: a scrub pre-state may dim to a measured floor (v8 off-quote .45, large text); the resting state never dims.

## de-familiarise-by-subtraction
- De-familiarise by subtraction: per iteration change one hue of the 60% and one face; emphasis uses the page's own device, never an italic word with a drawn underline. _(support 1, intermediate, R0805)_
  - values: One hue and one face per iteration.
  - do: Per iteration change one hue of the 60% and one typeface, and emphasise with the page's own device.
  - don't: Do not emphasise with an italic word plus a drawn underline.

## monochrome-plus-accent
- Keep the interface near-monochrome (greys/white) and let a single warm accent (orange/amber glow) carry the brand. _(support 1, intermediate, R0001)_

## oklch-palettes
- Build colour palettes in OKLCH. _(support 1, intermediate, R0093)_

## quote-marker
- Quote marker (DIRECT failure v8 L169-170): one device for every quote, a band over the lower .38em in the accent (.32 alpha dark, .6 paper); no fill box, no left bar. _(support 1, intermediate, R0748)_
  - values: Band: lower .38em; alpha .32 dark, .6 paper.
  - do: Mark every quote with one accent band over the lower .38em of the text, alpha .32 on dark and .6 on paper.
  - don't: Do not use a fill box or a left bar behind quotes.

## three-colour-editorial
- For editorial event sites, use a three-colour palette: a very dark brand tone, a warm off-white, and one saturated accent that matches the hero image. _(support 1, intermediate, R0094)_
  - values: #002D37 #FEF0DD #F7A724; #012A23 #F9EFDB #C12589
  - do: Petrol Blue #002D37 / Ivory #FEF0DD / Saffron Gold #F7A724; Deep Teal #012A23 / Bone #F9EFDB / Magenta #C12589

## tinted-grays
- Tint your dark grays slightly toward the brand hue instead of using pure neutral gray. _(support 1, intermediate, R0009)_
  - values: 4% teal tint
  - do: #16201F (4% teal)
  - don't: #18181B (0% tint)

## Checklist

- [ ] De-familiarise by subtraction: per iteration change one hue of the 60% and one face; emphasis uses the page's own device, never an italic word with a drawn underline.
- [ ] Map every neutral and accent hue onto a 12-step semantic scale where each index has a fixed functional UI role across both light and dark modes.
- [ ] Alternatively, when using a 10-step functional token scale, assign Steps 1–3 to component surfaces, Steps 4–6 to borders, Steps 7–8 to high-contrast solid fills, and Steps 9–10 to secondary and primary text.
- [ ] Keep the interface near-monochrome (greys/white) and let a single warm accent (orange/amber glow) carry the brand.
- [ ] Build colour palettes in OKLCH.
- [ ] Measure the rendered contrast of quote text against its final surface, including any overlay, image or texture: 4.5:1 for normal text and 3:1 for large text.
- [ ] Never dim a dark-mode quote with `opacity` on its container; use alpha colors for the fill and border so the text stays at full strength.
- [ ] Quote marker (DIRECT failure v8 L169-170): one device for every quote, a band over the lower .38em in the accent (.32 alpha dark, .6 paper); no fill box, no left bar.
