# Blender 5.x din Python - capcane verificate

Verificat pe Blender 5.2.2 LTS, Windows 11, RTX 5060 Laptop, 2026-11-21. Dacă `blender.exe --version` arată
altă versiune majoră, rulează din nou testul de la final înainte să te bazezi pe listă.

| Capcană | Ce se întâmplă | Corect |
|---|---|---|
| Cod de ieșire | Fără flag, o excepție în script iese cu **0** | `blender -b --factory-startup --python-exit-code 1 -P scena.py -- ...` |
| Motor EEVEE | `BLENDER_EEVEE_NEXT` nu mai există (a existat în 4.2-4.5) | `scene.render.engine = "BLENDER_EEVEE"` sau `"CYCLES"` |
| F-Curves | `action.fcurves` a fost **eliminat** -> `AttributeError` | `anim_utils.action_get_channelbag_for_slot(ad.action, ad.action_slot).fcurves` (`from bpy_extras import anim_utils`) |
| Cale de ieșire | `//frames/` eșuează fără .blend salvat („cannot save”) | cale absolută: `os.path.join(os.path.abspath(out), "f_")` |
| GPU Cycles | cu `--factory-startup` rămâne pe CPU | `prefs.compute_device_type="OPTIX"`, `refresh_devices()`, `d.use=True`, `scene.cycles.device="GPU"` (vezi template) |
| Import SVG | `bpy.ops.wm.svg_import` nu există | `bpy.ops.import_curve.svg(filepath=...)` (addon io_curve_svg, activ implicit) |
| Scalare SVG | SVG-ul vine minuscul (100 px ≈ 0.028 m); `transform_apply` scalează și raza punctelor -> extrudare uriașă | `transform_apply(scale=True, properties=False)`, apoi setezi `extrude`/`bevel_depth` |
| `join()` | unește TOT ce e selectat, inclusiv podeaua adăugată anterior | `select_all(action="DESELECT")` înainte de import/adăugare |
| Animație de scale | o cheie `scale=(1,1,1)` anulează scalarea de normalizare a obiectului | normalizează în geometrie (`transform_apply`), apoi animă scale de la 1 |
| Rotația obiectului importat | `hero.rotation_euler = (...)` în scenă suprascrie rotația pusă la import (logo-ul ajunge culcat) | păstrează `x=90°` pentru logo/text cu fața la camera de pe -Y |
| Diacritice în text 3D | fontul implicit Blender nu are ș/ț | `t.data.font = bpy.data.fonts.load("C:/Windows/Fonts/arialbd.ttf")` |
| Socket-uri Principled BSDF | indecșii numerici s-au schimbat între versiuni | după nume: `"Base Color"`, `"Metallic"`, `"Roughness"`, `"Emission Color"`, `"Emission Strength"` |
| Buclă turntable | cheie finală la `end` -> ultimul cadru = primul -> sacadare | cheia de 360° la `end+1` (nerandat), interpolare `LINEAR` (helper `turntable()`) |
| Python | `-P` rulează Python-ul intern al Blender (3.13), nu Python 3.14 din sistem | scripturile de orchestrare (ffmpeg, verificări) rulează separat cu `python -I` |
| Avertisment HIP | `HIPEW initialization failed` | inofensiv (nu e placă AMD) |
| `use_nodes` depreciat | `DeprecationWarning: 'Material.use_nodes' / 'World.use_nodes' ... removed in Blender 6.0` | în 5.x materialele/lumile noi au deja `node_tree`; setezi `use_nodes` doar dacă `node_tree` lipsește (cum face template-ul) |
| Compositor | `scene.node_tree`, `scene.use_nodes` și nodul Composite NU mai există | `ng = bpy.data.node_groups.new("Post","CompositorNodeTree")`, `scene.compositing_node_group = ng`, `ng.interface.new_socket("Image", in_out="OUTPUT", socket_type="NodeSocketColor")`, ieșire în `NodeGroupOutput` |
| Setări noduri compositor | `glare.glare_type` nu există | sunt intrări: `g.inputs["Type"].default_value = "Bloom"`, `["Threshold"]`, `["Strength"]`; Blur `inputs["Size"]` e vector |
| Mix în compositor | `CompositorNodeMixRGB` dispare | `ShaderNodeMix` cu `data_type="RGBA"`; socket-urile după `identifier` (`A_Color`, `B_Color`, `Factor_Float`, ieșire `Result_Color`) |
| Light linking | funcționează și în EEVEE 5.2 | `light.light_linking.receiver_collection = colecție` |
| Animarea compositorului | setările nodurilor sunt intrări, nu proprietăți | `glare.inputs["Strength"].keyframe_insert("default_value", frame=f)` (verificat: pulsul de bloom la impact) |
| Zoom animat | | `cam.data.lens = v; cam.data.keyframe_insert("lens", frame=f)` |
| Text → litere | cu bevel lat, o literă iese din MAI MULTE piese `separate(type="LOOSE")` (34 de piese pentru 8 litere) | rândurile le iei din text (`body.count("\n")`), nu din mărimea pieselor; grupezi pe X după suprapunere |
| SVG multicolor | unite ca o singură curbă, formele suprapuse devin GĂURI (umplere even-odd) și rămâne un singur material | convertești FIECARE curbă separat în mesh, apoi unești mesh-urile; culoarea e în `material.diffuse_color` (pusă de importer) |
| Măsurarea luminozității | `ffmpeg signalstats` YAVG e în YUV **limited range**: alb pur = 235, nu 255 (am „găsit” un plafon fals de 235) | media RGB reală: `-vf "crop=...,scale=1:1:flags=area" -f rawvideo -pix_fmt rgb24 - \| od -An -tu1` |
| Umbră pe fundal foarte luminos | sub AgX, o alfa liniară pe un fundal cu radianță 5-10 abia se vede (0,4 alfa ≈ 6 unități) | alfa exponențială `1 - (1-strength)^g` (scade în trepte de expunere); vezi `contact_shadow()` |
| Contur care „se desenează” | pe o curbă **ciclică** `bevel_factor_end` e ignorat (verificat: geometria nu se schimbă) | curbă deschisă, cu primul punct repetat la final; animezi `curve.bevel_factor_end` 0→1 |
| Fundal transparent | | `scene.render.film_transparent = True`, PNG RGBA; podeaua `obj.is_holdout = True` (ascunde ce e sub ea, iese transparentă) |
| Import modele | | `.glb/.gltf`: `bpy.ops.import_scene.gltf` (convertește Y-up în Z-up); `.obj`: `wm.obj_import`; `.fbx`: `import_scene.fbx`; `.stl`: `wm.stl_import` |
| Rotire produs importat | piesele glTF vin cu ierarhie proprie | pivot (Empty) la baza modelului; reparentezi rădăcinile păstrând `matrix_world` și rotești DOAR pivotul |
| HDRI doar pentru reflexii | fundal vizibil = studio | World: Mix Shader cu `Light Path > Is Camera Ray`; `TexCoord.Generated` Z dă direcția (verificat) |
| Extensii instalate | `Failed to remove ... extensions\.local\...` la pornire | inofensiv (curățenie a unei extensii, ex. higgsfield); nu afectează randarea |
| Poza obiectelor animate | după `keyframe_insert`, `matrix_world`/`bound_box` pot arăta altă poză (ex. scale 0 din intro) | `scene.frame_set(cadrul_dorit)` ÎNAINTE de orice măsurare |
| `join()` | obiectele unite sunt șterse; citirea lor după aceea dă `ReferenceError` | măsori tot (bbox etc.) ÎNAINTE de `join()` |
| Reflexie în oglindă verticală | o lumină din spatele camerei apare pe literă la altă înălțime decât crezi | punctul la h față de CAMERĂ se reflectă la h·(2D+c)/D (D = cameră-text, c = cât e lumina în spatele camerei); verifici cu lumini roșu/verde |
| Heredoc Python în Git Bash | `\n` din codul generat ajunge linie nouă reală, deci `SyntaxError` | editezi fișierele cu Edit |
| Culori exacte pe un ecran (mockup) | sub AgX, o captură PNG iese desaturată/decalată | randezi în `Standard`, iar în compositor `CompositorNodeConvertToDisplay` (există în 5.2) aplică AgX pe tot, MAI PUȚIN pe masca AOV a ecranului (shader AOV merge în EEVEE) |
| Multe lumini area cu umbre | `Shadow buffer full` la 1080p, umbre lipsă | `use_shadow=False` pe luminile de umplere/rim; umbre doar pe 1-2 lumini |
| Eșantionare de vârfuri | `o.data.vertices[::k]` dă `TypeError: slice steps not supported` (apare doar la modele mari, unde k > 1) | `[vs[i].co for i in range(0, len(vs), k)]` (helper `_sample_pts`) |
| Piese animate dintr-un glTF | o piesă-părinte își târăște copiii; `location` e în spațiul părintelui | reparentezi fiecare mesh direct pe pivot păstrând `matrix_world`; offsetul din lume -> local cu `(parent.matrix_world @ o.matrix_parent_inverse).to_3x3().inverted() @ d` |
| Text care se schimbă pe cadru (contor) | modificat în handler în timpul `render(animation=True)` poate bloca/crăpa | `bpy.app.handlers.frame_change_pre` + randare cadru cu cadru (`frame_set` + `write_still`); vezi `COUNTERS` |
| `align_y = "CENTER"` la text | centrează pe linia fontului, nu pe contur: obiectele puse „sub text” îl taie | așezi după `bound_box` măsurat în lume |
| Culori pe vârfuri din glTF (`COLOR_0`) | glTF le cere LINIARE; scrise în sRGB ies spălate/pastel în Blender | converti sRGB -> liniar înainte de export (vezi `img2model.py`) |
| Thin Film (Principled) în EEVEE | `Thin Film Thickness` practic nu se vede în EEVEE (text gri-argintiu, verificat) | iridescență procedurală: Layer Weight + poziție în lume + zgomot -> rampă ciclică (vezi `hero_material("iridescent")`) |
| Sticlă (Transmission) în EEVEE | pe fundal negru iese gri-închis: refracția n-are ce arăta | `material.use_raytrace_refraction = True`, `surface_render_method = "DITHERED"` și fundal cu conținut (glow) |
| Decalaj cu pas întreg pe multe obiecte | pas ≥ 1 cadru: obiectele de după fereastră primesc cheia de final ÎNAINTEA celei de start și rămân la starea inițială | întinzi decalajul pe fereastră: `a = f0 + round(spread * i / (n - 1))` (bug reparat în `outro()`) |
| `else` furat la inserare | un bloc `if` inserat între `elif` și `else` la aceeași indentare devine noul „if” al lui `else` -> ramura de rezervă rulează și ea (sticlă provizorie peste produs; nicio eroare) | inserezi DUPĂ tot lanțul if/elif/else; testul `regression.py` prinde astfel de stricăciuni |
| Cameră sub podea | la distanțe mari (9:16) un unghi de jos duce camera sub planul podelei: cadre negre, dar `framing_report` zice ok | limitezi `cam.z >= podea + marjă`; `finalize.py` semnalează „porțiune neagră” |
| Manual și versiune | manualul „latest” de pe docs.blender.org e 5.3; instalat: 5.2.2 LTS (Python intern 3.13.13, `use_scripts_auto_execute=False` cu `--factory-startup`, verificat 2026-11-26) | folosești `https://docs.blender.org/manual/en/5.2/...`; smoke-ul automat e în `test_animatie3d.py` |
| `--save-blend` | apare și `scene.blend1` | copia de rezervă automată a Blender la suprascriere, e normal |

## Timpi măsurați pe acest PC

| Ce | Timp |
|---|---|
| Pornire Blender (fix, la fiecare rulare) | ~3 s |
| EEVEE, 640x360 | ~0,2-0,5 s / cadru |
| EEVEE, 1920x1080, 64 sample-uri, scenă simplă | ~0,65 s / cadru (120 cadre = 78 s) |
| EEVEE, 1920x1080, rețeta premium (volumetrice, motion blur, raytracing) | ~2 s / cadru (120 cadre = 4 min) |
| Cycles GPU OPTIX, 1920x1080, 32 sample-uri + denoise | ~7 s / cadru (30 cadre = 3,5 min) |
| three.js în Edge headless, 1280x720 | ~90 cadre / s |

Pentru randări finale lungi: cronometrează 3 cadre (`--frames 1,2,3`); timp/cadru = (total − 3 s) / 3. Dacă
timp/cadru × nr. cadre trece de ~2 min, pornește randarea cu `run_in_background` și numără PNG-urile scrise.

## Test rapid după un upgrade de Blender

```
blender -b --factory-startup --python-exit-code 1 -P <skill>/scripts/blender_template.py -- --out <tmp_abs> --frames 1,60
```
Trebuie să tipărească `ANIM3D_FRAMING {"ok": true...}` și `ANIM3D_DONE`, cu cod de ieșire 0.
