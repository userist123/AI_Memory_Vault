# AI App Studio — design draft (2026-11-22)

Status: DRAFT. Framework choice pending research (Codex task 80689374, Gemini task 5a545766).

## Goal
Local Windows app that lets an AI agent build apps for phone, tablet and laptop/desktop,
showing live (a) what the AI is doing and (b) the running app on device frames and a real Android emulator.
Sibling of AI Website Studio (E:\claude-work\ai-website-studio), fork base commit 25b3eab (working tree there is WIP).

## Verified environment facts
- Android emulator: WSL2 distro `Ubuntu-S9AI-D` (vhdx in D:\WSL\Ubuntu), user <owner-user>.
  SDK ~/emu/sdk (emulator binary ~/emu/sdk/emulator/emulator), adb at /usr/bin/adb.
  AVD `s9emu`: android-33, google_apis, x86_64, 1440x2960 @560dpi, 4 GB RAM, no Play Store.
  /dev/kvm present; WSL sees 15 GB RAM, 28 cores. Docker CLI present in the distro.
- The same distro hosts an unrelated LineageOS build and a debug.keystore: App Studio must NOT
  modify `s9emu`; it uses its own AVD(s) (e.g. `aas-phone`, `aas-tablet`) and never reads ~/debug.keystore.
- Docker Desktop WSL distro running on the host.

## Architecture (Fable advisor, adjusted for the emulator)
- Shared core with AI Website Studio, new project kind `app`, separate MCP server with a minimal
  tool catalogue (no canvas/3D/BIM/computer-use), separate data root. Seam needed in services.py
  (project-kind strategy for validate/render/publish).
- Builds: studio owns package.json/lockfile/bundler config (curated pinned kit in a base image);
  dependency install = separate approved step (--ignore-scripts, hash-bound); build = offline container
  (--network none, source read-only).
- Preview: browser device frames (phone 390x844, tablet 834x1112, desktop 1440x900) on a per-project
  origin + live Android emulator stream (headless emulator in WSL2, screen stream + input injection).
- Live AI activity: append-only `activity_events` table (seq, runId, kind set by server, capped payload,
  artifact refs by sha256) linked to audit seq; sources = operation envelope (tool.call/result, file.diff,
  build.log, preview.captured, qa.result, approval.requested) + streamed CLI output for studio-launched runs.
  Transport = existing token WebSocket with afterSeq replay. Rendering: text nodes only, ANSI stripped,
  caps, server-side redaction; approval cards only from server approval records in a separate region.

## Threat model sketch (to be finalised after research)
T1 build-time code exec on host; T2 slopsquatting/malicious deps; T3 preview JS attacks studio;
T4 network exposure (incl. adb 5037 / emulator console 5554 / gRPC / Metro 8081 through WSL2 networking);
T5 prompt injection via logs/docs/screenshots; T6 activity stream attacks owner UI; T7 exfiltration;
T8 workspace escape. New with emulator: T9 malicious generated APK reaching host via adb/emulator
console/shared folders; T10 interference with the owner's other emulator/keystore in the same distro.
Each T has probes that must be red before the control exists (see Fable report in session log).

## Default product decisions (owner rule: decide safe, record)
D1 cross-platform single codebase; D2 iOS out of v1 (no cloud build of private source);
D3 on-device data only; D4 curated dependency kit, additions via approval; D5 both external Claude
Code (MCP) and built-in runner; D6 activity kept locally 30 days, redacted, no hidden reasoning;
D7 preview external network denied by default; D8 no LAN/QR device preview; D9 mode in existing shell
behind a flag; D10 signing keys owner-only, v2.

## Owner requirement (2026-11-22, explicit): "I want to see on the PC desktop what the AI is actually doing"
Primary UX goal, not a nice-to-have. v1 must show, live and visibly on the Windows desktop:
1. Studio window (Tauri): streaming step timeline (plan, current tool + target file, +N/-M pills), file being
   written shown as a live diff, build log, approval cards; never a bare spinner.
2. Device frames (phone/tablet/desktop) that reload on every revision while the AI works.
3. A visible Android emulator window (WSLg, restrict=on networking) with show_touches/pointer_location on, so
   every AI tap/swipe is drawn on screen; optionally mirrored inside the studio via h264 stream.
4. A "follow the AI" mode: the studio auto-focuses the panel/file/device the AI is currently acting on, plus a
   cursor-like marker on the preview where the AI clicks (inherited computer_use / browser_run_interaction).
Headless mode stays available for tests/CI only.
