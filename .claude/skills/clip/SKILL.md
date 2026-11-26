---
name: clip
description: Short command for Clips Kitty. Use when the owner types /clip followed by a Twitch, YouTube or Kick link or a local video path, or asks to process a stream into vertical clips. Queues it through the clips-kitty MCP tools, reports the job, and later lists/exports the clips the owner picks. The full procedure lives in the clips-kitty skill.
argument-hint: <link or video path> [gta] [max N] [focus "..."]
---

# /clip

Argument: `$ARGUMENTS` - a link (Twitch / YouTube / Kick) or a full path to a local video,
optionally followed by `gta`, `max N` and/or `focus "..."`.

Load and follow the `clips-kitty` skill; it is the single source for tools, parameters, waiting,
failures, rights and publishing. This command only maps the arguments:

| Argument | Call |
|---|---|
| link | `queue_video(url=..., captions=true)` |
| local path | `queue_local_file(path=..., captions=true)` |
| `gta`, or the URL, filename or owner's words clearly say GTA (the title is not known at queue time) | add `gaming=true`, `pipeline={"id":"colingpt9/gta-vi-highlights"}` |
| `max N` | `max_clips=N` |
| `focus "..."` | `focus` = the owner's words verbatim |

Order: `engine_status` -> `queue_status` -> rights check -> queue -> report the job id and end the
turn (no polling loop) -> later `job_status` -> `list_clips` -> owner picks -> `export_clip` to
`%USERPROFILE%\Videos\ClipsKitty-exports` -> ffprobe check.

/clip never publishes: no `publish_when_done`, no publish or schedule plan. If the owner wants
to post, follow section 6 of `clips-kitty`.
