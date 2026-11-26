---
name: comfyui
version: v0.46.0
sha256: c5f62e0f6a5a792b25dd5a812e326eed9289ce748a7de970dd47547a820a4fe0
path: E:\ai-video-studio\engines\ComfyUI\main.py
install_method: git
licence: GPL-3.0
last_verified_at: 2026-11-24
---
# ComfyUI (image/video generation engine)

- Source: https://github.com/comfyanonymous/ComfyUI tag v0.46.0, commit a277b5bf213e8a44fa34817b7770b54f86ef4cea
  (sha256 above is main.py at that commit; the trust anchor is the git commit).
- Licence GPL-3.0: fine for local use; flag it before bundling ComfyUI in a distributed release.
- Venv: E:\ai-video-studio\engines\venv (see python-venv-ai-video-studio.md); freeze in
  E:\claude-work\ai-video-studio\phase0\venv_freeze_S0.txt.
- Uninstall: stop the process, then `cmd /c rmdir /s /q E:\ai-video-studio\engines\ComfyUI` (no junctions inside).

## Start / stop (verified)
- Start: `pwsh -File E:\claude-work\ai-video-studio\phase0\start_comfy.ps1` (loads env.ps1, binds 127.0.0.1:8188,
  --disable-auto-launch --disable-api-nodes, models via phase0\extra_model_paths.yaml, output E:\ai-video-studio\bench\_comfy_out,
  temp E:\ai-video-studio\tmp\comfy, user dir E:\ai-video-studio\engines\comfy_user, log bench\comfyui.log).
- Health: `GET http://127.0.0.1:8188/system_stats` (JSON with versions and VRAM).
- Stop: kill only the venv python + its child running main.py (find by CommandLine 'main.py --listen 127.0.0.1').

## Recipes
- Benchmark harness: phase0\bench.py `run_workflow(api_workflow, stage, run, kind)` posts /prompt, polls /history,
  logs nvidia-smi every 1 s, downloads outputs, builds a contact sheet.
- FLUX.2 klein: UNETLoader + CLIPLoader(type=flux2, qwen_3_4b) + Flux2Scheduler + EmptyFlux2LatentImage, cfg 1, 4 steps.
- Wan 2.2 5B: Wan22ImageToVideoLatent (start_image optional) + wan2.2_vae; Wan 14B GGUF: UnetLoaderGGUF (ComfyUI-GGUF).

## Pitfalls
- `--disable-api-nodes` is deprecated in 0.46 (behaves like --offline); keep it, or switch to --offline later.
- Custom nodes load only at start: restart after cloning a node or installing its deps.
- comfy_kitchen CUDA backend provides scaled_mm_nvfp4 / per-tensor fp8 kernels on sm_120 (logged at start).
- "Checkpoint files will always be loaded safely" = safe loading; still only .safetensors/.gguf in models dir.

## Security notes
- Listens on 127.0.0.1 only; no ComfyUI-Manager; frontend offline. Custom nodes only by git clone at a pinned commit.
- Custom nodes run arbitrary Python: read requirements and grep for downloads / torch.load before enabling.
