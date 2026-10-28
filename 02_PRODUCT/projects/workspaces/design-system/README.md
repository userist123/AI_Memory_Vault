# Design system tokens

One source of design tokens for every project in the Vault, in the DTCG 2025.10 format, built to CSS custom
properties with Style Dictionary (pinned to 5.6.0). Casa3D is the first consumer. Plan and decisions: `PLAN.md`.

## Layout
- `tokens/core/` — shared by every brand: spacing (4 px grid), control sizes, motion durations and easings (IBM Carbon).
- `tokens/brands/<brand>/` — the brand's colours (OKLCH source + exact `hex` output), type, radii, shadows, and
  `brand.json`: `aliases` (CSS names the app already uses → token path) and `copyTo` (extra output files).
- `build.mjs` — one Style Dictionary run per brand → `dist/<brand>/tokens.css` (git-ignored, local preview) and the
  `copyTo` targets (committed; these are what the drift test checks).
- `test/` — `node:test` suites (see below).

## Use
```bash
npm ci --ignore-scripts
npm run build
npm test
```

Change a value: edit the token JSON, run `npm run build`, commit the JSON together with the regenerated CSS files.
Never edit a generated `tokens.css` by hand: the drift test fails.

## Add a brand
Create `tokens/brands/<name>/` with its token files and a `brand.json` (`name`, optional `aliases`, optional
`copyTo`). The build and the drift test pick it up automatically.

## Tests
- `types` — every token has a known `$type` and a value of the right DTCG shape; paths are unique.
- `color` — OKLCH components and `hex` agree within 1/255 per channel.
- `parity` — every custom property of Casa3D's original `:root` block (frozen in `test/fixtures/`) still resolves to
  the same value.
- `drift` — committed outputs equal a fresh build; every alias points at a token.
