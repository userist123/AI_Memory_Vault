---
name: rife-v4.26
version: RIFE v4.26 (hzwer RIFEv4.26_0921), weights converted to safetensors
sha256: safetensors ac0ca0c148212cb0885133ea43701a8a31733909f05c46b6c679865e0e6c29ce (source flownet.pkl 45c7f741...f53f, zip 1fa9b9cd...760e)
path: E:\ai-video-studio\models\interpolation_models\rife_v4.26_flownet.safetensors
install_method: manual (official HF zip -> torch.load weights_only=True in python -I -> safetensors, phase0\rife_convert.py)
licence: MIT (github.com/hzwer/Practical-RIFE, ECCV2022-RIFE; HF card has no licence field)
last_verified_at: 2026-11-24
---
# RIFE frame interpolation (own small runner, no ComfyUI node)

- Code: phase0\rife_ifnet.py (IFNet_HDv3 from the official zip, only the warp import changed) + rife_warp.py; runner phase0\rife_run.py
  (venv python, fp32; fp16 needs set_default_dtype(float16) because the warp grid is built in the default dtype).
- 1280x704, 24 -> 48 fps, 121 -> 241 frames: 3.1 s on the RTX 5060 (27 ms per generated frame, ~0.7 GB torch alloc).
- Uninstall: delete the safetensors and rife_*.py; the source pickle lives in E:\ai-video-studio\models\RIFE_src.
