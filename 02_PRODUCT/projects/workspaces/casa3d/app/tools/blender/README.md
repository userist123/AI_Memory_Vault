# Randare fotorealistă (Blender + Cycles)

1. În aplicație: vederea 3D → **Randare foto (Blender)**. Se descarcă `*.casa3d.json`: formele vederii 3D,
   materialele (lemn, parchet, gresie, textil, piele, sticlă, ceramică, crom...) și lumina aleasă (zi/seară/noapte, soare).
2. Pe un calculator cu Blender 4.2+ (placa video NVIDIA RTX → OptiX):

```powershell
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b -P casa3d_render.py -- casa.casa3d.json D:\randari\casa --samples 256 --panorama
```

Opțiuni: `--device AUTO|OPTIX|CUDA|CPU`, `--width/--height`, `--rooms living,dormitor`, `--panorama` (360° pe cameră),
`--export fbx,glb,obj` (pentru 3ds Max, SketchUp, Twinmotion, D5), `--blend` (salvează scena), `--no-render`,
`--assets D:\assets\polyhaven` (texturi foto CC0 de la polyhaven.com: laminate_floor_02, large_grey_tiles, plastered_wall,
rough_linen, velour_velvet, leather_white, plus cerurile kloofendal_43d_clear_puresky.hdr și belfast_sunset_puresky.hdr).

Ieșire: câte o imagine pe încăpere (camera se alege singură: colțul sau peretele cu vederea cea mai liberă), panorame 360°.
