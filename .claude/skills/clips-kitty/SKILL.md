---
name: clips-kitty
description: Turn long videos and streams into short vertical clips with Clips Kitty, running locally on this computer (engine on 127.0.0.1:8766, MCP server clips-kitty). Use when someone wants clips, Shorts, Reels or TikToks made from a YouTube, Twitch or Kick link or a video file, or wants to read back, caption-check or export clips Clips Kitty already made, or asks whether a clip may be posted. Covers the full tool flow (status, queue, check back, list, export, verify), failure handling, the rights check, and publishing only when the owner explicitly asks.
when_to_use: Any request to make, find, list, read the captions of, export or post clips from a long video, stream or VOD; the /clip command defers here. Also when a clips-kitty tool returns an error, "Not queued", an empty clip list or a paused queue. Not for new 3D animation (animatie-3d) or AI-generated video (Higgsfield).
---

# Clips Kitty

Clips Kitty finds the moments worth posting in a long video, crops them to 9:16 with the speaker
kept in frame, burns in captions and writes titles. Processing runs on this PC: no upload of the
footage. Tool names below are the live MCP schemas checked on 2026-11-26.

## 0. Before anything: is the engine up?

1. `engine_status`. If it says the engine is not answering on 127.0.0.1:8766, start it with
   `powershell -File %USERPROFILE%\ClipsKitty-audit\start-clips-kitty-engine.ps1` (loopback only,
   port 8766; it prints `OK: Clips Kitty <version>`), then call `engine_status` once more. Still
   down: report BLOCKED with both logs, `%USERPROFILE%\ClipsKitty-audit\engine-8766.log` (stdout)
   and `engine-8766.log.err` (stderr, same folder), and stop.
   Port 8765 belongs to agent-bridge; never point Clips Kitty there.
2. `queue_status`: room left, running job, PAUSED or not. A paused queue looks like a broken app.
   Resume (`POST http://127.0.0.1:8766/queue/resume`, verified in `server/api.py`) restarts the WHOLE
   queue, including older waiting jobs (which may carry a publish follow-up) and a queue the owner
   paused on purpose. So resume only when the job you just queued is the only one waiting; otherwise
   report the pause and the waiting jobs, and leave it paused.
3. Raw HTTP to the engine: `/queue/resume` is the only route ever called directly. Never call
   `/woopsocial/*`, `/publish/*`, `/uploadpost/*`, schedule or job routes with curl or similar:
   that would bypass the MCP deny rules.

## 1. Rights check (before queueing anything for posting)

Decide, do not ask; record the decision in the reply.
- Owner's own channel or file, or footage with written permission / a licence: proceed.
- Someone else's stream or video: clips may be made locally for review, but say plainly that
  posting them needs the rights holder's permission or a licence (YouTube: use content you don't own
  only with permission, a licence or a copyright exception such as fair use). Never plan
  publishing for such clips.
- Reused-content rules apply even with permission: YouTube may demonetize a whole channel whose
  videos lack "a meaningful difference" from the original; TikTok makes "unoriginal or reused
  material without anything new" ineligible for the For You feed; Instagram says avoid
  "unoriginal or aggregated content" and watermarked reels. Recommend added commentary or edits.
- A Short over 1 minute "with an active copyright claim of any type, including manual claims" is
  blocked globally on YouTube [1]; keep clips
  of third-party material (music, broadcasts) under 60 s or out of the post list.

## 2. Queue (returns in seconds; processing takes tens of minutes)

Default `captions=true` (vertical 9:16, 10-60 s clips). Set only what was asked. The two tools take
different parameters; the engine silently drops anything a tool does not forward, so never pass a
`queue_video`-only parameter to `queue_local_file` and assume it worked.

- Both tools: `captions`, `focus` (the owner's own sentence, verbatim), `max_clips`, `min_score`
  (0-100), `force`, `gaming` (+ `gaming_layout` only from the owner's chosen layout; omit for auto),
  `pipeline`, `sport={name:"soccer"|"basketball", ...}`, `vertical_live` (only if already 9:16).
  GTA V / Online / RP / VI: `gaming=true` + `pipeline={"id":"colingpt9/gta-vi-highlights"}`
  (installed under `clips-studio\data\plugins\installed\colingpt9`). Never invent a pipeline id.
- `queue_video(url, ...)` only (YouTube / Twitch / Kick link): `hashtags` (all clips), `watermark`
  (saved profile name), `caption_style` {font, font_size 24-160, color, highlight, highlight_color,
  position, uppercase, words_per_caption 1-6}, `podcast`, `long_clips` (61-180 s vertical),
  `longform` (short_clips, clips_140, highlights, edited_stream; only for horizontal 16:9 on request).
- `queue_local_file(path, ...)` only: `channel`, `source_url` (both only when known, never guessed),
  `title`. If the owner wants hashtags, watermark, caption style, podcast, long or longform on a
  local file, say the MCP tool cannot carry it and that it is set in the app.
- `publish_when_done`: NEVER set it, even if the owner asks to "process and publish". It publishes
  EVERY clip on completion (`jobs.py _run_follow_up`), with no pick, no caption check and no plan.
  Publishing always goes through section 6 after the owner has picked clips.

"Not queued" shapes (from `server\mcp.py`):
- `queue_video`: "Not queued: <video_id> was processed before ..." -> offer `list_clips` on that
  video, or `force=true` only if the owner meant to redo it (about an hour, duplicate clips);
  "Not queued: it is already waiting as job N" -> use job N; otherwise "Not queued: <raw JSON>".
- `queue_local_file`: always raw JSON, e.g. `Not queued: {"job_id": null, "already_processed": true,
  "video_id": "..."}`. Read `already_processed` / `already_queued` / `queued_job_id` and act as above;
  any other JSON is a refusal: quote it and report BLOCKED.

## 3. Check back (never block)

Report the job id and an honest estimate, then end the turn. Never check back in the same turn:
no sleep, Monitor or polling loops. Call `job_status(job_id)` on the owner's next message or on a
scheduled wake (`queue_status` shows the running job). States: queued, running, done,
failed, cancelled. On `failed`: quote the error, check `queue_status` and `engine_status`, retry
once only if the cause was transient (engine restart); otherwise report BLOCKED with the error.

## 4. Read the results

- `list_videos` first when unsure of the id: an unknown video id returns an empty clip list,
  exactly like a video with no clips.
- `list_clips(video_id)`: id, score, timestamps, title. Scores are the model's ranking 0-100, not a
  promise; titles are suggestions. Show them as a table and let the owner pick.
- `clip_captions(clip_id)`: the burned-in transcript; read it before recommending a clip (wrong
  words, names, swearing).
- GTA jobs: quote the evidence from
  `%USERPROFILE%\ClipsKitty-audit\clips-studio\data\plugins\runs\<video>-<time>\out\gta-highlight-explanations.json`.

## 5. Export and verify

`export_clip(clip_id, folder)` only for the clips the owner picked; default folder
`%USERPROFILE%\Videos\ClipsKitty-exports`. Then verify each file with
`ffprobe -v error -show_entries stream=codec_type,width,height:format=duration -of compact <file>`:
1080x1920 (or 1920x1080 for longform), duration matching the clip, an audio stream present. A
mismatch is reported, never hidden.

## 6. Publishing (only when the owner asked for it)

- Execute tools are denied in settings (`publish_plan_execute`, `schedule_clips_execute`,
  `uploadpost_publish`), and no Clips Kitty UI can execute a plan here: the desktop UI has port
  8765 hard-coded (taken by agent-bridge, per the launcher script) and the engine on 8766 serves
  no UI. So posting from Clips Kitty is currently not possible. Never work around the deny (no
  raw HTTP, no `publish_when_done`). Show the plan, then tell the owner, without a question:
  "Planul e gata, dar Clips Kitty nu poate posta de aici: interfața aplicației e blocată pe portul
  8765, ocupat de agent-bridge. Clipurile exportate sunt în <folder>; le poți încărca manual din
  YouTube Studio, TikTok sau Instagram, cu titlurile din plan."
- Plans only, read-only: YouTube -> `youtube_status` (channel, uploads left today) then
  `publish_plan(clip_ids, start_at RFC 3339 with offset, every_hours, privacy)`. Several platforms
  -> `uploadpost_status` or `woopsocial_status` first, then `schedule_clips_plan(clip_ids |
  video_id, platforms, per_day, every_hours, start_at, hashtags)`. Show the plan in full.
- Follow-up: `publish_status(clip_id)`, `uploadpost_result(clip_id)`.
- A voice-over, if one is ever added, uses the owner's cloned voice (memory vocea-proprietarului).

## 7. Platform targets (for choosing clips and caption position)

| Platform | Length | Format | Source |
|---|---|---|---|
| YouTube Shorts | up to 3 min; square or vertical | 9:16 1080x1920 is the engine default | [1] |
| Instagram Reels | recommended to non-followers only if 3 min or less [4] | 1.91:1 to 9:16, min 30 fps, min 720 px (read in Playwright 2026-11-26) [3] | [3][4] |
| TikTok | UNVERIFIED (official page did not load 2026-11-26) | 9:16 recommended (UNVERIFIED) | - |

Caption safe zone: keep captions off the bottom app bar and right-side buttons; `position:"middle"`
is the safest choice when unsure (UNVERIFIED: no official pixel safe zone found).

## Sources (read 2026-11-26)

1. YouTube Shorts length and Content ID block over 1 min: https://support.google.com/youtube/answer/15424877
2. YouTube reused content policy: https://support.google.com/youtube/answer/1311392
3. Instagram Reel size and aspect ratios: https://help.instagram.com/1038071743007909
4. Instagram recommendation eligibility (3 min, unoriginal, watermarks): https://creators.instagram.com/blog/instagram-recommendations-eligibility-tips-creators
5. TikTok integrity and authenticity (IP, unoriginal content): https://www.tiktok.com/community-guidelines/en/integrity-authenticity
6. YouTube using content you don't own: https://support.google.com/youtube/answer/2797466
7. Local checks: live MCP schemas; `clips-studio\server\mcp.py` (publish_when_done -> job "then: publish"), `server\api.py` (/queue/resume, local-file "already_processed" JSON), `server\jobs.py`
   `_run_follow_up` (publishes every clip), launcher script (`engine-8766.log`, `.log.err`).
