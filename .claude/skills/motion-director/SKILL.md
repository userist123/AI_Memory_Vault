---
name: motion-director
description: Owner's standing directive for programmatic video and motion graphics with Remotion (React + TypeScript) - act as senior motion designer, studio-grade easing and springs, cinematic typography on an asymmetric grid, clear Sequence structure, preview then render to MP4. Uses the official Remotion plugin skills for the API. Apply automatically; the owner describes the scene, never writes code or commands.
when_to_use: Any request for a video, animation, motion graphics, logo reveal, kinetic typography, intro/outro, explainer, product promo, metric counter or social clip. Romanian triggers - "video", "animatie", "clip", "intro", "logo reveal", "motion".
---

# motion-director

The owner describes the scene in plain words ("15 s cinematic text reveal for X"); you deliver
a working Remotion composition and the rendered MP4. Never ask them to type `npx` commands.
Ask only for what is theirs: text, brand assets, music rights, length and format if unclear.

## 1. Setup (once per video project)

- A separate project folder, never inside the AI_Memory_Vault repository.
- New project: scaffold with the official Remotion skills (`remotion` plugin); installing
  `remotion` and its npm packages for that project is part of the job, nothing global.
- GPU, set once per project so every preview and render uses it without flags: in
  `remotion.config.ts` call `Config.setChromiumOpenGlRenderer("angle")` (Chromium draws on the
  NVIDIA GPU through ANGLE/Direct3D on Windows) and `Config.setHardwareAcceleration("if-possible")`
  (hardware video encoding where Remotion supports it; it falls back to CPU otherwise). The
  owner's PC has an RTX 5060 with 8 GB VRAM: keep `Config.setConcurrency()` at half the CPU
  cores for normal scenes and 1-2 for heavy WebGL/3D scenes so VRAM is not exhausted.
- On the first render of a project, confirm the GPU is really used (`nvidia-smi` shows
  Chromium/ffmpeg load during the render) and record the result in `PLAN.md`; if a setting is
  not supported by the installed Remotion version, remove it and say so rather than guessing.
- Brand: reuse the project's `DESIGN.md` palette and type scale when one exists
  (`creative-web-director`); otherwise write a short one first.

## 2. Motion standard (studio grade, no "PowerPoint 2009")

- **Easing**: `spring({ fps, frame, config: { damping: 12, mass: 0.5 } })` or expo-out
  (`Easing.out(Easing.exp)`); no linear moves except for intentional mechanical motion.
- **Stagger**: 0.05-0.1 s between letters or words (compute in frames from `fps`).
- **Typography**: cinematic type on an asymmetric grid, high contrast, dark base with precise
  accent colour; a deliberate type pairing, never the default system font alone.
- **Camera**: smooth scale and translate driven by `useCurrentFrame()` + `interpolate()` with
  clamped extrapolation.
- **Rhythm**: hold key frames long enough to read (about 1 s per 3 words); one idea per beat.
- **Structure**: each beat its own `<Sequence from={...} durationInFrames={...}>`, timing
  constants in one place, typed props, components small enough to reuse.

## 3. Verify, then render

1. Type-check and lint; open the composition in Remotion Studio (`npx remotion studio`) and
   check every scene at its key frames (stills via `npx remotion still`).
2. Render on the owner's PC with the apps installed there (checked 2026-10-11: FFmpeg 9.0.2 on
   PATH with working `h264_nvenc`, `hevc_nvenc`, `av1_nvenc`; NVIDIA driver 616.92; Blender 5.2.2
   LTS portable at `D:\Apps\blender-5.2.2-windows-x64\blender.exe`, OptiX sees the RTX 5060).
   Drive C: is nearly full: write projects, intermediates and renders to D:. Remotion draws the frames on the GPU (ANGLE), then the PC's FFmpeg encodes on the
   RTX 5060 (NVENC):
   - `npx remotion render <Composition> out/<name>.mov --codec=prores --prores-profile=4444`
     (lossless intermediate; 60 fps when the brief asks for it);
   - `ffmpeg -i out/<name>.mov -c:v h264_nvenc -preset p7 -rc vbr -cq 18 -b:v 0 -pix_fmt yuv420p
     -movflags +faststart out/<name>.mp4` (`hevc_nvenc` for 4K or smaller files); delete the
     intermediate after the MP4 checks out.
   - Quick drafts may skip the intermediate: `npx remotion render ... out/<name>.mp4 --codec=h264
     --crf=18`.
   Report the output path, length, resolution, size and which encoder ran.
3. 3D scenes that need real lighting (product shots, fly-throughs): Blender with Cycles on OptiX
   (RTX) renders them to an image sequence that Remotion composites. Render headless with
   `D:\Apps\blender-5.2.2-windows-x64\blender.exe -b <scene>.blend -E CYCLES -o //frames/f_#### -a
   -- --cycles-device OPTIX`.
4. Assets: only the owner's own or properly licensed fonts, images and audio; list sources in
   the project README.

## 4. Limits

- Owner's standing permission (2026-10-11): when a render tool is missing on the PC, install it
  yourself, without asking, from the official source only (winget package of the vendor, or
  the vendor's own download), pinned to an exact version, then report what was installed.
  Prefer drive D: (C: is nearly full). Installed so far: Blender 5.2.2 LTS, official portable
  zip from download.blender.org, sha256 checked against the official `.sha256` file.

- Cloud rendering (Lambda/Cloud Run), uploads and publishing only with the owner's explicit go.
- Pin what you scaffold: `npx create-video@<the version in the project's package.json, or the
  current stable you name in PLAN.md>`, never `@latest --yes` blind. Never run `npx skills update`
  or any other command that rewrites installed skills.
- Start `npx remotion studio` only to verify a scene, and stop it when done.
- Third-party services (ElevenLabs voice, Mapbox/MapTiler/Google maps, AWS) only when the owner
  asks for that feature; keys go in the project's `.env`, never in chat or in code.
- Remotion licence: free for individuals and small teams; a company licence may be required
  for larger companies. Say so once when a video is for a business, then follow the owner.
- Durable decisions (brand motion rules, reusable scenes) go to the Vault via
  `memory_propose`, not a second memory store.
