---
type: procedure
category: procedure-analysing-reference-videos-of-websites-frames-ocr
tags:
- candidate
- mcp-proposal
created: '2026-10-10'
updated: '2026-10-10'
provenance:
  source_type: ai
  source_ref: owner instruction in claude-code session 01EqS7NMBmYzDHFqZ5rZoZmH, 2026-10-10
confidence: low
verification: unverified
relations: []
lifecycle: REVIEW
id: 5dc09261-b46b-4f72-ba37-4291e8bf2fff
---
# Procedure: analysing reference videos of websites (frames + OCR + speech + web validation)

Owner-provided procedure (2026-10-10) for analysing reference videos (e.g. screen recordings of award-winning sites) before building.

1. Ingest: never reason from the raw .mp4. Sample keyframes with ffmpeg/OpenCV at a fixed interval (1 frame every 1-2 s, not just 12 evenly spaced frames) and extract the audio track.
2. Read each frame with a vision model (OCR + visual QA): URL bar, on-screen headlines, UI chrome, effect type. Transcribe speech with Whisper (local) to get spoken context and timestamps.
3. Extract anchors: domains/brand names in the address bar (e.g. terminal-industries.com), on-screen copy, and motion cues (object rotation coupled to trackpad scroll => GSAP ScrollTrigger / canvas scrubbing; nebulae, refractive spheres, DNA helices on black => WebGL/three.js GLSL particles).
4. Validate with targeted web search: "<domain> tech stack", "<brand> gsap threejs", community threads (GSAP forum has a thread replicating the Terminal Industries sticky slider).
5. Answer in a structured form: identified sites, techniques, stack, and how to reproduce.
Agent architecture for automating it: ffmpeg/Whisper and frame sampling -> vision model (Claude / Qwen2.5-VL locally) -> orchestrator (LangGraph/CrewAI) with a web-search tool (Tavily/Serper/SearXNG) -> structured report.
Lesson from the session: the first analysis used 12 frames and skipped the Romanian voice-over of the second clip; both steps 1 and 2 must run in full.
