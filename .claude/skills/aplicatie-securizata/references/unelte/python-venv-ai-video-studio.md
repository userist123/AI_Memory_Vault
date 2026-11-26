---
name: python-venv-ai-video-studio
version: cpython-3.12.16 + torch 2.14.1+cu130
sha256: 68e047de3721e6a905f72d8e338e90961ebb19616e2104ace94761b796b0fb1c
path: E:\tools\uv-python\cpython-3.12-windows-x86_64-none\python.exe
install_method: uv
licence: PSF-2.0
last_verified_at: 2026-11-24
---
# Python 3.12 (uv managed) + ComfyUI venv

- Interpreter: uv-managed python-build-standalone cpython-3.12.16 in E:\tools\uv-python (digest checked by uv).
- Venv: E:\ai-video-studio\engines\venv (venv python.exe sha256 5ee333922d9cb0b11b0309f1ab6551a1d96998af8a65921e96419a754e620b30).
- torch 2.14.1+cu130, torchvision 0.29.1+cu130, torchaudio 2.11.0+cu130 from https://download.pytorch.org/whl/cu130.
- Freeze: E:\claude-work\ai-video-studio\phase0\venv_freeze_S0.txt (later additions listed in docs\MEASUREMENTS.md).
- Uninstall: `cmd /c rmdir /s /q E:\ai-video-studio\engines\venv`; interpreter: `uv python uninstall 3.12`
  with UV_PYTHON_INSTALL_DIR=E:\tools\uv-python.

## Environment (always first)
`. E:\claude-work\ai-video-studio\phase0\env.ps1` sets TEMP/TMP, UV_CACHE_DIR, PIP_CACHE_DIR, HF_HOME, TORCH_HOME on E:
and HF_HUB_DISABLE_TELEMETRY=1. Run scripts with `python -I` (isolated, no cwd on sys.path).

## Verified (E1 smoke, 2026-11-24)
- torch.version.cuda 13.0, device RTX 5060 Laptop GPU, capability (12,0), arch list includes sm_120.
- fp16 4096 matmul max rel err 3.3e-4 vs CPU fp32; ~28.9 TFLOPS fp16, ~19.8 TFLOPS bf16 (8192).
- Installing packages: `E:\ai-video-studio\engines\venv\Scripts\python.exe -m pip install <pkg>==<ver>` after env.ps1
  (pip cache on E:). Pin exact versions.

## Pitfalls
- Running without env.ps1 puts pip/HF caches on C: (C: has ~6 GB free).
- The venv python.exe is a launcher: the real worker is a child process of E:\tools\uv-python\...\python.exe.
