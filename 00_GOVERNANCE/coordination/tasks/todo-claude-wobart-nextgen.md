# todo-claude-wobart-nextgen
STATUS: IN_PROGRESS        UPDATED: 2026-10-10T17:40:00Z
TASK: Research "next-gen WOB ART website" (vault + web) and make the Wobart site more complex (3D configurator, scroll stage, FAQ, JSON-LD, WhatsApp, CMS editors), orchestrated per cost-router.
BRANCH / PR: Wobart `ccr-a57d8bba-5jt0rc` / none    BASE: Wobart 16066a7; vault 120722d9
SPEC: Wobart `tasks/todo.md`, Wobart `PROJECT_BRAIN/RESEARCH/2026-10-10_next_gen_research.md`
DONE:
- Research: vault (Explore/haiku), codebase audit (Explore/haiku), web 2026 (general-purpose); synthesis written.
- Wobart baseline fixed: lockfile out of sync, nonexistent @radix-ui/react-scroll-area 1.1.8 → 1.2.10, zod added; lint 0 err/20 warn, build green.
- CMS schema + types + defaults extended (home.configurator, home.faq, optional/back-compat), deps three/R3F/drei.
- WP3 (vault-worker/sonnet): ProcessSection sticky stage + CSS scroll-driven; FaqSection native <details>.
- WP4 (vault-worker/sonnet): StructuredData JSON-LD from CMS, WhatsAppCta, Website Studio editors.
- WP2 (vault-worker/sonnet): WrapConfigurator (R3F, procedural car) + ConfiguratorSection; integrated in page/layout.
- vault-reviewer (opus): FIX-FIRST, 9 findings → all fixed (schema default, stale observer, hex 400, aggregateRating, poster, wa.me, focus, empty FAQ, copy).
- Wobart pushed: 402c27b feat + 4640275 fix on `ccr-a57d8bba-5jt0rc`.
- memory proposal (REVIEW, unverified): 01_ARCHITECTURE/knowledge/next-gen-premium-automotive-wrapping-website-2026-research-v_74299992.md
- Owner started Desktop Commander (device Marius-PC, Win11, RTX 5060 4 GB, C: 15 GB free, D: 276 GB free). Installed without admin:
  Blender 5.2.2 LTS portable at D:\Tools\blender-5.2.2-windows-x64, gltf-transform (npm -g), ffmpeg (winget user scope); Ollama 0.40 running
  with 7 models. Wobart cloned to D:\Projects\Wobart. Inventory: Wobart PROJECT_BRAIN/TOOLING.md.
- 3D pipeline proven end to end: scripts/blender/build_car.py (headless, 3.6 s) -> GLB 122 KB -> gltf-transform meshopt 33.5 KB with
  KHR_materials_clearcoat; committed from the PC as 795e0af on ccr-a57d8bba-5jt0rc.
- Playwright visual smoke script: Wobart scripts/visual/screenshot.mjs (phone/tablet/desktop + configurator with SwiftShader WebGL).
- GLB wired into WrapConfigurator (vault-worker/sonnet; useGLTF meshopt, Draco off, material 'Wrap' swapped); Playwright visual run
  on the fresh build: canvas on tablet/desktop, poster on phone, car renders with finish swatches; only 503 /api/content (no MONGO_URL).
  Pushed f927c54; screenshots in Wobart test_reports/visual/. Site built and opened on Marius-PC for the owner.
- Owner rejected the procedural car and the plain stage ("mizerii", "look din 1900"). Replaced with the Khronos CarConcept
  (CC-BY 4.0, Eric Chadwick / DGG; base CC0): Blender prepare_carconcept.py strips the trademarked Khronos parts and unifies the
  paint into material 'Wrap'; gltf-transform meshopt+webp 1024 (--palette false, otherwise material names are lost): 11.8 MB -> 1.42 MB.
  Stage: Poly Haven studio HDRI + Lightformers, Bloom/Vignette, PerformanceMonitor DPR, precise Box3 auto-fit (quantized accessor
  bounds are inflated and floated the car), contact shadows on the CSS backdrop (MeshReflectorMaterial floor read as a grey slab).
  GPU screenshots taken on Marius-PC via headless Chromium (scripts/visual/shot-gpu.cjs): car grounded, satin black reads well.
  Wobart commits 87346d2, e7cffa0, 00199ef, 0187a4d, 5a98f6a, 04cb015. CC BY credit added in the section disclaimer.
- Lesson recorded (Wobart tasks/lessons.md): a push chained with ';' after a red lint went out; chain with '&&' only.
- 2026-10-11 (claude-wobart-nextgen): owner wants the Terminal Industries truck effect for the car ("nu cu puncte"). Particle hero
  replaced by a PRE-RENDERED scroll story: Wobart scripts/blender/render_scrub.py (Cycles, 151 frames, 30/chapter): photoreal car ->
  scan cut (world-X ScanMask node group on every material) -> glowing X-ray wireframe hologram with laser band -> layer shells ->
  finishes cycle -> aerial on a grid. Frames -> WebP (frames_to_webp.ps1, public/story/1920|1280) scrubbed on <canvas> by
  components/canvas/FrameScrub.tsx (coarse-to-fine loading, phone crop follows the car). Live R3F fallback: CarStory.tsx (unwired).
  Findings: Wireframe modifier with even offset on duplicate verts spiked to km-long geometry (white frames + GPU OOM) -> weld first,
  no even offset, exterior parts only, bounds guard; studio lights must be visible_camera=False for a flying camera; PowerShell `$_`
  is stripped in inline DC commands -> use .ps1 files. Renders wait politely for other Blender users (queue_scrub.ps1).
NEXT (in order):
0. vault-worker polishes the configurator section UI; then PC build + GPU shots + owner review in the browser.
1. Owner: look at the site on the PC (desktop + mobile), check the 3D scene, the Process stage and the WhatsApp CTA; then attest or reject the memory proposal.
2. Follow-ups listed in Wobart PROJECT_BRAIN/RESEARCH/2026-10-10_next_gen_research.md §8 (licensed GLB, server-side JSON-LD, CWV measurement).
BLOCKERS / OWNER QUESTIONS:
- No browser in CI: visual behaviour of the 3D scene and scroll-driven CSS is UNVERIFIED until the owner runs it.
KEY FILES:
- Wobart: lib/cms-schema.ts, lib/site-content.ts, components/canvas/WrapConfigurator.tsx, components/sections/{ConfiguratorSection,FaqSection,ProcessSection}.tsx, components/seo/StructuredData.tsx, components/ui/WhatsAppCta.tsx, app/admin/website/page.tsx
VERIFICATION SO FAR: Wobart final: `pnpm lint` 0 errors/20 warnings (= baseline), `tsc --noEmit` 0, `pnpm build` 15/15, `next start` smoke / 200; vault: untrusted_content_guard BLOCKING=0, `pytest 20_TESTS/test_vault_state_accuracy.py` 21 passed. UNVERIFIED: browser rendering of 3D + scroll-driven CSS.
