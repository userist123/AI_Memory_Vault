---
id: core-color
title: Colour
tier_min: T1
page_types: ["any"]
topics: ["dark-mode", "dark-palette", "warm-serif-editorial", "60-30-10", "modern-palette", "60-30-10-example", "60-30-10-palette", "accent-usage", "alpha-borders", "color-roles", "color-weight", "fintech-palette", "limited-palette", "monochrome-brown-palette", "neutral-backgrounds"]
rules: 29
---

# Colour

Colour roles, palettes, contrast.
Support = number of independent educators who teach the rule.

## dark-mode
- Default to off-black dark surfaces and off-white body text; use #000000 only as an explicit art-direction choice or for sunken wells. _(support 1, basic, R0832)_
  - values: Off-black dark canvas range #08090A to #121316 (OKLCH L 0.14-0.19, e.g. shadcn/ui oklch(0.145 0 0); Material 3 dark surface #141218). Off-white text e.g. #EDEDEF to #F4F4F6; #000000 only for sunken wells or explicit art direction.
  - don't: Do not mechanically invert the light palette; use semantic tokens per theme. context: Geist uses true black on marketing/OLED layouts; claims that pure black causes OLED smear or eye strain are weakly evidenced, so treat off-black as a default, not a requirement.
- Render sunken wells, form text inputs, and code blocks in dark mode using an opaque surface tone darker than the parent card or canvas so they appear physically recessed. _(support 1, basic, R0835)_
  - values: Sunken surface lightness delta: L_sunken = L_parent - 0.03 to 0.05 in OKLCH (e.g., parent card #18191A / oklch(0.21 0 0) -> recessed input or Kanban well #0F1011 / oklch(0.17 0 0) with border: 1px solid rgba(255,255,255,0.08)). Atlassian token: elevation.surface.sunken darkens in both light and dark modes.
  - don't: Do not make text inputs lighter than a raised modal card in dark mode if clickable buttons inside the same modal also rely on lighter surface fills, as inputs and buttons will become indistinguishable.
- Define dark-mode borders and dividers using semi-transparent white alpha channels (rgba(255, 255, 255, 0.06) to 0.14) so borders automatically adapt to any underlying surface container elevation. _(support 1, basic, R0837)_
  - values: Dark border alpha ramp: Subtle divider = rgba(255, 255, 255, 0.06); Default card/input border = rgba(255, 255, 255, 0.10); Hovered component border = rgba(255, 255, 255, 0.18); Active/selected border = rgba(255, 255, 255, 0.28).
  - do: Define dark-mode borders and dividers using semi-transparent white alpha channels (rgba(255, 255, 255, 0.06) to 0.14) so borders automatically adapt to any underlying
  - don't: Do not use transparent borders on overlapping sticky table headers or sticky columns without an opaque background fill behind the cell, or scrolling content will bleed through the border.

## dark-palette
- Construct dark mode foundations using cool obsidian or warm carbon near-blacks (#08090A to #0F1012) stepped in perceptual OKLCH lightness increments rather than flat #000000 with harsh drop shadows. _(support 1, basic, R1089)_
  - values: Linear Obsidian Palette: Page Canvas #08090A (oklch(14% 0.007 265)), Level-1 Surface #0F1011 (oklch(17.5% 0.007 265)), Level-2 Elevated Card #141517 (oklch(20% 0.008 265)), Level-3 Popover/Modal #1C1D21 (oklch(23.5% 0.01 265)); Primary Text #F7F8F8, Secondary Text #8A8F98, Muted Text #5E626A (about 3.2:1 on #08090A, not text-safe: decorative or disabled elements only). [Disagreement Note]: Linear, Raycast (#07080A), and Supabase (#121212 / #171717) avoid pure #000000 to preserve surface depth and prevent OLED smear, whereas Vercel (#000000 with #0A0A0A cards) and Resend (#000000 void canvas with #06060A surfaces) deliberately use pure #000000 as a theatrical stage so 3D specular objects disappear seamlessly into the viewport background.
  - don't: except: use pure #000000 canvas only when embedding black-background WebGL/video renders where a #0A0A0C background would expose the video bounding rectangle. | disagrees: codex: a third-party scan reports a pure #000000 background for one named brand; hex values for named brands were not verified by official sources.
- Replace dark-mode drop shadows with 1px translucent hairline borders (rgba(255,255,255,0.06) to 0.12) and a top-edge specular highlight gradient to define card elevation. _(support 1, basic, R1090)_
  - values: Base card border: 1px solid rgba(255, 255, 255, 0.07) (or Resend frost tint: rgba(214, 235, 253, 0.19)); Hover border: rgba(255, 255, 255, 0.16); Top specular rim: background: linear-gradient(90deg, transparent 0%, rgba(255,255,255,0.22) 50%, transparent 100%) on a 1px top pseudo-element (or box-shadow: inset 0 1px 0 0 rgba(255, 255, 255, 0.10)).
  - don't: Do not apply bright 1px specular borders to every minor table cell or secondary badge; reserve the top specular highlight for primary feature cards and modal containers.
- Encode interactive surface states (hover, active, selected) using translucent white alpha steps (rgba(255,255,255,0.03) -> 0.06 -> 0.09) rather than opaque hex overrides so underlying ambient lighting and tint bleed through naturally. _(support 1, basic, R1096)_
  - values: Resting card/row: rgba(255, 255, 255, 0.02); Hover state: rgba(255, 255, 255, 0.05); Active/Pressed state: rgba(255, 255, 255, 0.08); Selected state: rgba(255, 255, 255, 0.07) + 1px border rgba(255, 255, 255, 0.14).
  - don't: Do not use translucent alpha backgrounds on sticky headers or floating dropdown menus without pairing backdrop-filter: blur(12px-20px) and at least 75%-85% base surface opacity (#0A0A0BCC), otherwise scrolling text underneath collides visually with menu labels.

## warm-serif-editorial
- Pair warm-ink backgrounds with an anti-halation warm-parchment foreground hierarchy rather than pure `#FFFFFF`: Primary Paper Text `#EDE8E2` (or `#F4F0EA`), Secondary Stone Text `#9E958C` (or `#A39B90`), and a decorative/disabled-only umber `#6E655D` (never captions; ~3.3:1), paired with a 3-step Amber/Ochre accent ramp (`--amber-bright: #F59E0B`, `--amber-primary: #D97706`, `--amber-surface: rgba(217, 119, 6, 0.12)` with `1px solid rgba(245, 158, 11, 0.28)` border). _(support 1, basic, R0560)_
  - values: `--text-primary: #EDE8E2` (contrast ~15.6:1 on `#12100E`, advisor arithmetic, MEASURE); `--text-secondary: #9E958C` (contrast `6.4:1` on `#12100E`); `--text-muted: #6E655D` (contrast ~3.3:1, decorative/disabled only, never captions); `--amber-primary: #D97706`; `--amber-glow: #F59E0B`; `--amber-deep: #78350F`.
  - don't: Never use `--amber-primary: #D97706` for small body text (<14px) on `--bg-surface-2: #24201C` without verifying contrast, and never put pure `#FFFFFF` text on `#D97706` amber buttons (contrast ~3.2:1 fails WCAG AA; advisor arithmetic, MEASURE); instead use dark espresso ink `#12100E` text on `#F59E0B` / `#E5A00D` amber primary buttons (contrast ~8.8:1; advisor arithmetic, MEASURE).
  - caveat: evidence DIRECT only for the WCAG 4.5:1 requirement; the palette is CANDIDATE; secondary-text 6.4:1 figure is unmeasured
  - scope: aesthetic:warm-editorial
- Give the accent one semantic job plus the focus ring and keep it a small minority of any viewport (heuristic: under 8% of pixels); neutrals carry everything else. _(support 1, basic, R0580)_
  - values: Surface budget: `80%-85%` warm ink darks (`#12100E`, `#1A1714`, `#24201C`), `10%-14%` warm parchment/stone typography and hairlines (`#EDE8E2`, `#9E958C`, `#2C2621`), `<= 5%-8%` amber accent (`#D97706`, `#F59E0B`, `#78350F`).
  - don't: Never flood entire section backgrounds or large bento cards with full-saturation `#D97706` amber on a dark editorial page, which destroys the quiet nocturnal print-press atmosphere and turns the page into a construction/warning-sign palette.
  - scope: aesthetic:warm-editorial
- Option: Style code blocks and terminal snippets in the warm-serif dark aesthetic using a custom warm syntax palette rooted in the same espresso-and-parchment world rather than dropping in a cold blue VS Code / GitHub Dark `#0D1117` theme: Background `#15120F`, Border `1px solid #2C2621`, Base code `#EDE8E2`, Comments `#9E958C` (italic, at least 4.5:1), Keywords `#D97706` (amber), Strings `#A3B18A` (muted sage) or `#E0A96D` (warm sand), Functions/Types `#E6C280` (pale gold), and Line numbers `#4E463F` (`tabular-nums`). _(support 1, basic, R0582)_
  - values: Code block bg `#15120F`; active highlighted line bg `rgba(217, 119, 6, 0.10)` with `2px solid #F59E0B` left border; font size `13px-14px`, line-height `1.65`, tab-size `2`.
  - don't: Do not use high-saturation cyan (`#00FFFF`), magenta (`#FF00FF`), or neon purple (`#A855F7`) syntax tokens inside a warm-serif dark editorial page, as cold neon code blocks immediately break chromatic cohesion.
  - scope: aesthetic:warm-editorial

## 60-30-10
- Build product pages on a 60-30-10 palette: 60% warm neutral main, 30% deep secondary, 10% loud accent reserved for what users click and key highlights. _(support 1, basic, R0171)_
  - values: Mascarpone Cream 60% / Highland Green 30% / San Marzano 10% #EF4229 (RGB 239,66,41); Crema Ivory 60% / Pine Velvet 30% / Coral Rocket 10% #FF5A4A (RGB 255,90,74); Biscotti Cream 60% / Graphite Enamel 30% / Tangerine Laser 10% #FF6A14 (RGB 255,106,20)
- Build a mobile palette as 60% light neutral, 30% secondary and 10% vivid accent reserved for the main CTA and active states. _(support 1, basic, R0215)_
  - values: 60/30/10; #E6E6FA, #6A5ACD, #7FFF00
  - do: Lavender #E6E6FA 60%, Slate Blue #6A5ACD 30%, Chartreuse #7FFF00 10% (Add to order button)

## modern-palette
- Style semantic badges, callouts, and alert banners using a very low-saturation tinted background (Step 2–3) paired with deep high-contrast text of the same hue (Step 11–12) rather than solid saturated fills. _(support 1, basic, R0830)_
  - values: Refactoring UI & Radix soft badge formula: Light mode: background = Step 3 (oklch(0.94 0.04 H)), border = Step 6 (oklch(0.85 0.07 H)), text = Step 11 (oklch(0.38 0.14 H)); Dark mode: background = Step 3 (oklch(0.22 0.04 H)), border = Step 6 (oklch(0.32 0.07 H)), text = Step 11 (oklch(0.82 0.10 H)), achieving >= 4.5:1 text contrast.
  - do: Style semantic badges, callouts, and alert banners using a very low-saturation tinted background (Step 2–3) paired with deep high-contrast text of the same hue (Step
  - don't: Do not use soft pastel fills for the single primary call-to-action button on a page or critical destructive confirmation buttons that require maximum visual weight (Step 9 solid fill).
- Pick tokens by semantic role, not because the visual value looks right. _(support 1, basic, R0907)_
  - do: Reference dynamic/semantic colour tokens.
  - don't: Do not hardcode platform colour values in place of dynamic tokens.

## 60-30-10-example
- Apply 60-30-10: a pale neutral background 60%, a mid-tone brand colour 30%, and a vivid contrasting accent 10% reserved for CTAs, active chips and highlights. _(support 1, basic, R0075)_
  - values: 60% / 30% / 10%
  - do: Lavender #E6E6FA 60%, Slate Blue #6A5ACD 30%, Chartreuse #7FFF00 10% (Add to order button)

## 60-30-10-palette
- Apply 60-30-10: a light neutral as the dominant background, a mid-tone secondary and a vivid accent only for the primary CTA and selected chips. _(support 1, basic, R0047)_
  - values: #E6E6FA 60% / #6A5ACD 30% / #7FFF00 10%
  - do: Lavender #E6E6FA 60%, Slate Blue #6A5ACD 30%, Chartreuse #7FFF00 10% (Add to order button, selected chip)

## accent-usage
- Use the 10% accent only on the buy button, active indicators and a highlighted headline word; keep everything else in the main and secondary colours. _(support 1, basic, R0172)_
  - do: 'What's inside.' with only 'inside.' in accent; orange 'Buy now' and active dot

## alpha-borders
- Use semi-transparent borders instead of solid hex so they blend into any background. _(support 1, basic, R0092)_
  - values: rgb(0 0 0 / 0.12)
  - do: rgb(0 0 0 / 0.12)
  - don't: Solid grey hex borders

## color-roles
- Define colors by role (primary, secondary, success, warning, error, surface, border, text) and then generate a shade ramp for each. _(support 1, basic, R0346)_
  - values: shades 50, 100, 200 ... 900
  - don't: Creating random one-off colors

## color-weight
- Give strong saturated colour (high weight) only to primary CTAs and alerts; use neutral, low-weight colour for layout structure and secondary actions. _(support 1, basic, R0412)_
  - do: Saturated 'Add to cart' button with outlined grey 'No thanks, view cart'

## fintech-palette
- For a trustworthy fintech app, pair an off-white background with a navy primary and a mint-green accent. _(support 1, basic, R0012)_
  - values: Background off-white #F5F7FA; Primary navy #1E3A8A; Accent mint green #10B981

## limited-palette
- Limit the palette to a primary, a secondary and a neutral colour; more colours create visual noise. _(support 1, basic, R0486)_
  - values: 3 colours max (caption)

## monochrome-brown-palette
- Build a warm monochrome palette as a 6-step tonal ramp of one brown hue from near-black to cream. _(support 1, basic, R0099)_
  - values: #2B2118, #59402B, #89613A, #B58B56, #D8B98A, #F3E8D3

## neutral-backgrounds
- Use neutral backgrounds (soft grays, whites, light colours) so the content stays the focus. _(support 1, basic, R0382)_
  - do: Toned-down neutral screen with a single dark CTA for a serious look
  - don't: Loud gradient backgrounds with pink highlight unless you want a playful tone

## palette
- Use hue-bearing neutrals (canvas and text share one hue family, OKLCH chroma 0.008-0.02) with one accent at OKLCH C <= 0.15 that has a semantic job; the primary CTA may use the text colour as fill, and a greyscale screenshot must keep the hierarchy. _(support 1, basic, R1122)_
  - values: Neutral chroma 0.008-0.02 (single hue family); accent OKLCH C <= 0.15; glow: none; greyscale test passes.
  - don't: Do not use grey-blue black plus neon, and do not add glow.

## palette-size
- Limit the palette to 3-5 colors and use neutrals for backgrounds and text. _(support 1, basic, R0146)_
  - values: 3-5 colors

## restrained-fintech-palette
- For calm, trustworthy fintech UI limit colour to one navy plus two accents; build hierarchy from size and weight, and reserve red/green only for trend direction. _(support 1, basic, R0113)_
  - values: Navy 900 #273869, Navy 800 #405881, Gray 50 #EDF1F4
  - don't: Using colour to separate features

## simple-palette
- Stick to one main colour and one accent, and keep strong contrast between the background and CTAs. _(support 1, basic, R0463)_
  - do: Neutral white/light-grey UI with black as the single accent for CTAs

## three-colour-rule
- Limit a site to three colours: a dark base, a text colour and one accent used for CTAs. _(support 1, basic, R0128)_
  - values: Plum black #1B151F / White #FFFFFF / Ember orange #F16C31; Midnight black #0C0C0C / White #FFFFFF / Orange #FF6F32

## unexpected-accent
- Build the palette from two base colours that do 90% of the work plus one unexpected accent used only on the button you want pressed. _(support 1, basic, R0137)_
  - values: two colors = 90% of the work; espresso swatch #3B2A22 shown
  - do: black + white + lime; navy + cream + coral; forest green + beige + electric purple

## Checklist

- [ ] Build product pages on a 60-30-10 palette: 60% warm neutral main, 30% deep secondary, 10% loud accent reserved for what users click and key highlights.
- [ ] Build a mobile palette as 60% light neutral, 30% secondary and 10% vivid accent reserved for the main CTA and active states.
- [ ] Apply 60-30-10: a pale neutral background 60%, a mid-tone brand colour 30%, and a vivid contrasting accent 10% reserved for CTAs, active chips and highlights.
- [ ] Apply 60-30-10: a light neutral as the dominant background, a mid-tone secondary and a vivid accent only for the primary CTA and selected chips.
- [ ] Use the 10% accent only on the buy button, active indicators and a highlighted headline word; keep everything else in the main and secondary colours.
- [ ] Use semi-transparent borders instead of solid hex so they blend into any background.
- [ ] Define colors by role (primary, secondary, success, warning, error, surface, border, text) and then generate a shade ramp for each.
- [ ] Give strong saturated colour (high weight) only to primary CTAs and alerts; use neutral, low-weight colour for layout structure and secondary actions.
