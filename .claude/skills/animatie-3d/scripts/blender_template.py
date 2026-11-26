"""Template Blender 5.x pentru animații 3D randate headless. Verificat pe Blender 5.2.2 LTS, 2026-11-21.

Rulare (OBLIGATORIU --python-exit-code 1, altfel o excepție iese cu cod 0):
  blender.exe -b --factory-startup --python-exit-code 1 -P scena.py -- --out <folder_abs> --mode preview

Argumente utile:
  --mode preview|final   preview = 1/3 din rezoluția finală (același raport); final = rezoluție plină
  --engine eevee|cycles  ACELAȘI motor la preview și final (altfel aprobi altceva decât livrezi)
  --frames 1,30,60       randează doar aceste cadre (preview pe cadre cheie)
  --check-only           doar verificarea de încadrare, fără randare
Formatul, durata și fps-ul stau în secțiunea SCENA (ASPECT, SECONDS, FPS), deci nu se pot pierde între comenzi;
--aspect/--seconds/--fps le suprascriu doar dacă sunt date. Fiecare randare scrie <proiect>/events.json
(fps, cadre, durată, momente cheie), citit de finalize.py și sound.py.
Copiază fișierul în folderul proiectului și editează DOAR secțiunea SCENA.
Implicit produce un text/logo reveal de nivel „template profesionist”; vezi docstring-ul din build_scene.
"""
import argparse
import json
import math
import os
import re
import sys

import bpy
from bpy_extras import anim_utils
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Vector

assert bpy.app.version >= (5, 0, 0), "template scris pentru Blender 5.x (API Actions cu slot-uri)"


# ---------- argumente ----------
def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True, help="folder ABSOLUT pentru cadre PNG")
    p.add_argument("--mode", choices=["preview", "final"], default="preview")
    p.add_argument("--engine", choices=["eevee", "cycles"], default="eevee")
    p.add_argument("--fps", type=int, default=None, help="implicit FPS din SCENA")
    p.add_argument("--seconds", type=float, default=None, help="implicit SECONDS din SCENA")
    p.add_argument("--frames", default="", help="ex. 1,30,60 - doar aceste cadre; auto = 8 cadre de preview din "
                   "momentele animației; hold = 3 cadre pentru proba la rezoluție plină (fără cadrele de după ieșire)")
    p.add_argument("--aspect", choices=["16:9", "9:16", "1:1", "4:5"], default=None,
                   help="implicit ASPECT din SCENA; preview și final păstrează același raport")
    p.add_argument("--width", type=int, default=0)
    p.add_argument("--height", type=int, default=0)
    p.add_argument("--samples", type=int, default=0, help="0 = implicit pe mod/motor")
    p.add_argument("--transparent", action="store_true")
    p.add_argument("--save-blend", action="store_true", help="salvează scene.blend lângă --out")
    p.add_argument("--check-only", action="store_true")
    return p.parse_args(argv)


# ---------- utilitare de animație ----------
def _fcurves(obj):
    # Blender 5.x: action.fcurves NU mai există -> channelbag pe slot-ul obiectului
    ad = obj.animation_data
    cb = anim_utils.action_get_channelbag_for_slot(ad.action, ad.action_slot)
    return cb.fcurves if cb else []


def key(obj, path, value, frame, interp="BEZIER", easing="AUTO"):
    """Cheie pe obj.<path> la cadrul dat + interpolarea segmentului care PORNEȘTE din ea.
    interp: CONSTANT LINEAR BEZIER SINE QUAD CUBIC QUART QUINT EXPO CIRC BACK BOUNCE ELASTIC
    easing: AUTO EASE_IN EASE_OUT EASE_IN_OUT
    Reguli: intrări -> EXPO/BACK + EASE_OUT; ieșiri -> EASE_IN; rotație continuă -> LINEAR.
    """
    setattr(obj, path, value)
    obj.keyframe_insert(data_path=path, frame=frame)
    for fc in _fcurves(obj):
        if fc.data_path == path:
            for kp in fc.keyframe_points:
                if int(round(kp.co.x)) == frame:
                    kp.interpolation = interp
                    kp.easing = easing


def turntable(obj, end_frame, axis=2, turns=1.0, start_frame=1):
    """Rotire continuă fără sacadare la buclă: cheia finală e la end_frame+1 (nerandat),
    deci ultimul cadru randat nu dublează primul."""
    r = list(obj.rotation_euler)
    key(obj, "rotation_euler", tuple(r), start_frame, "LINEAR")
    r[axis] += 2 * math.pi * turns
    key(obj, "rotation_euler", tuple(r), end_frame + 1, "LINEAR")


def look_at(obj, target=(0, 0, 0)):
    d = Vector(target) - obj.location
    obj.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


# ---------- materiale, lumini, lume ----------
def material(name, color=(0.8, 0.8, 0.8, 1), metallic=0.0, roughness=0.4, emission=None, strength=0.0,
             specular=None):
    """Metal plin (metallic>0.6) într-o lume întunecată FĂRĂ HDRI iese maro-noroios: nu are ce reflecta.
    Fără HDRI: metallic <= 0.5. Pentru aur/crom adevărat: world(hdri=...)."""
    m = bpy.data.materials.new(name)
    if not m.node_tree:  # use_nodes e depreciat (dispare în 6.0); în 5.x materialele noi au deja noduri
        m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    inp = bsdf.inputs  # numele socket-urilor, NU indecși numerici
    inp["Base Color"].default_value = color
    inp["Metallic"].default_value = metallic
    inp["Roughness"].default_value = roughness
    if specular is not None:
        inp["Specular IOR Level"].default_value = specular
    if emission:
        inp["Emission Color"].default_value = emission
        inp["Emission Strength"].default_value = strength
    return m


def world(color=(0.02, 0.02, 0.03, 1), strength=1.0, hdri=None):
    w = bpy.data.worlds.new("World")
    bpy.context.scene.world = w
    if not w.node_tree:
        w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes.get("Background")
    if hdri:
        if not os.path.exists(hdri):
            raise FileNotFoundError(hdri)
        env = nt.nodes.new("ShaderNodeTexEnvironment")
        env.image = bpy.data.images.load(hdri)
        nt.links.new(env.outputs["Color"], bg.inputs["Color"])
    else:
        bg.inputs["Color"].default_value = color
    bg.inputs["Strength"].default_value = strength


def three_point_lights(target=(0, 0, 0), power=800):
    for name, loc, p in (("Key", (4, -4, 5), power), ("Fill", (-5, -3, 2), power * 0.35),
                         ("Rim", (0, 5, 4), power * 0.6)):
        bpy.ops.object.light_add(type="AREA", location=loc)
        light = bpy.context.object
        light.name = name
        light.data.energy = p
        light.data.size = 3
        look_at(light, target)


def dark_stage(floor_z=-1.2, color=(0.006, 0.006, 0.007, 1)):
    """Fundal întunecat cu podea care prinde umbra, FĂRĂ cusătură la orizont și fără hotspot:
    lume aproape neagră = culoarea podelei în depărtare, plan foarte mare, podea mată (fără reflexie)."""
    world(color, strength=1.0)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, floor_z))
    floor = bpy.context.object
    floor.name = "Floor"
    floor.data.materials.append(material("Floor", color, roughness=0.95, specular=0.05))
    return floor


def fit_camera(cam, obj, fill=0.6, height=0.35, target=None, frame=None):
    """Pune camera pe axa -Y la distanța la care `obj` ocupă `fill` din lățimea cadrului și o leagă cu
    Track To de o țintă (Empty), ca să poți anima apoi camera (dolly) fără să-și piardă privirea.
    Măsoară la `frame` (implicit ultimul cadru), deci obiectul trebuie să fie ÎN POZA FINALĂ la acel cadru:
    fie îl apelezi înainte de orice cheie cu frame=1 (cum face build_scene), fie după chei cu frame=cadrul final.
    height = înălțimea camerei relativ la lățimea obiectului. Întoarce ținta."""
    s = bpy.context.scene
    s.frame_set(frame or s.frame_end)  # măsoară obiectul în poza finală (nu la scale 0 din intro)
    objs = obj if isinstance(obj, (list, tuple)) else [obj]  # poate primi și o listă (titlu + subtitlu)
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    xs, zs = [p.x for p in pts], [p.z for p in pts]
    center = Vector(((min(xs) + max(xs)) / 2, 0, (min(zs) + max(zs)) / 2)) if target is None else Vector(target)
    aspect = s.render.resolution_x / s.render.resolution_y
    w = max(max(xs) - min(xs), (max(zs) - min(zs)) * aspect, 1e-3)  # obiect înalt -> limitează înălțimea
    cam.data.sensor_fit = "HORIZONTAL"
    dist = (w / fill) / 2 / math.tan(cam.data.angle_x / 2)
    cam.location = center + Vector((0, -dist, w * height))
    bpy.ops.object.empty_add(location=center)
    tgt = bpy.context.object
    tgt.name = "CamTarget"
    c = cam.constraints.new("TRACK_TO")
    c.target = tgt
    c.track_axis = "TRACK_NEGATIVE_Z"
    c.up_axis = "UP_Y"
    return tgt


def text_3d(body, size=1.0, extrude=0.12, bevel=0.02, font="C:/Windows/Fonts/arialbd.ttf"):
    """Text 3D cu diacritice (fontul Windows explicit; fontul implicit Blender nu are ș/ț)."""
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.object.text_add()
    t = bpy.context.object
    t.data.body = body
    t.data.font = bpy.data.fonts.load(font)
    t.data.size = size
    t.data.extrude = extrude
    t.data.bevel_depth = bevel
    t.data.align_x = "CENTER"
    t.data.align_y = "CENTER"
    t.rotation_euler = (math.radians(90), 0, 0)  # cu fața spre camera de pe -Y
    return t


def import_svg_logo(path, depth=0.15, target_width=3.0, bevel=0.01, layer_gap=0.008):
    """Importă un SVG, îl scalează la `target_width` (aplicat în geometrie, deci animațiile de
    scale pornesc de la 1), îl extrudează și îl unește într-un singur mesh centrat în origine.
    Fiecare culoare din SVG vine ca obiect separat, cu materialul ei (culoarea de brand în diffuse_color);
    straturile se decalează ușor spre cameră în ordinea din SVG, ca formele suprapuse să nu pâlpâie."""
    bpy.ops.object.select_all(action="DESELECT")  # altfel join() înghite și alte obiecte selectate
    before = set(bpy.data.objects)
    bpy.ops.import_curve.svg(filepath=path)  # verificat în 5.2 (addon io_curve_svg, activ implicit)
    new = sorted([o for o in bpy.data.objects if o not in before and o.type == "CURVE"], key=lambda o: o.name)
    if not new:
        raise RuntimeError("SVG fără curbe: " + path)
    bpy.context.view_layer.update()
    # lățimea întregului logo (toate formele), ca factorul de scară să fie COMUN
    xs = [(o.matrix_world @ Vector(c)).x for o in new for c in o.bound_box]
    ys = [(o.matrix_world @ Vector(c)).y for o in new for c in o.bound_box]
    k = target_width / max(max(xs) - min(xs), max(ys) - min(ys), 1e-9)
    meshes = []
    for i, o in enumerate(new):
        # FIECARE formă se convertește SEPARAT: unite ca o singură curbă, formele suprapuse devin găuri
        # (umplere even-odd) și rămâne un singur material. Ca mesh-uri unite, își păstrează culoarea.
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True); bpy.context.view_layer.objects.active = o
        o.scale = (k, k, k); o.location = (o.location.x * k, o.location.y * k, i * layer_gap * target_width)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True, properties=False)  # altfel scalează și raza punctelor -> extrudare uriașă
        o.data.extrude = depth / 2  # extrude e pe ambele părți
        o.data.bevel_depth = bevel
        bpy.ops.object.convert(target="MESH")
        meshes.append(bpy.context.object)
    bpy.ops.object.select_all(action="DESELECT")
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    logo = bpy.context.object
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    logo.location = (0, 0, 0)
    logo.rotation_euler = (math.radians(90), 0, 0)  # cu fața spre camera de pe -Y
    logo.name = "Logo"
    return logo


# ---------- render ----------
def setup_render(a):
    s = bpy.context.scene
    s.render.fps = a.fps
    s.frame_start = 1
    s.frame_end = max(1, round(a.fps * a.seconds))
    final = a.mode == "final"
    fw, fh = {"16:9": (1920, 1080), "9:16": (1080, 1920), "1:1": (1080, 1080), "4:5": (1080, 1350)}[a.aspect]
    k = 1 if final else 1 / 3
    s.render.resolution_x = a.width or int(fw * k) // 2 * 2
    s.render.resolution_y = a.height or int(fh * k) // 2 * 2
    s.render.resolution_percentage = 100
    s.render.image_settings.file_format = "PNG"
    s.render.image_settings.color_mode = "RGBA" if a.transparent else "RGB"
    s.render.film_transparent = a.transparent
    s.view_settings.view_transform = "AgX"
    s.render.use_motion_blur = final
    if a.engine == "cycles":
        s.render.engine = "CYCLES"
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for dev_type in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = dev_type
                prefs.refresh_devices()
                if any(d.type == dev_type for d in prefs.devices):
                    break
            except TypeError:
                continue
        for d in prefs.devices:
            d.use = d.type != "CPU"
        s.cycles.device = "GPU"
        s.cycles.samples = a.samples or (128 if final else 16)
        s.cycles.use_denoising = True
    else:
        s.render.engine = "BLENDER_EEVEE"  # în 5.x NU există BLENDER_EEVEE_NEXT
        s.eevee.taa_render_samples = a.samples or (64 if final else 16)
    os.makedirs(a.out, exist_ok=True)
    s.render.filepath = os.path.join(os.path.abspath(a.out), "f_")  # cale ABSOLUTĂ, nu //


def framing_report(hero, margin=0.0, step=None, max_pts=3000):
    """Proiectează vârfurile REALE ale obiectului principal în cameră la fiecare `step` cadre.
    Întoarce cadrele în care iese din cadru sau e foarte mic. Nu randează nimic, e rapid.
    (Bounding box-ul ar fi prea conservator la obiecte rotite -> alarme false.)"""
    s = bpy.context.scene
    step = step or max(1, s.render.fps // 6)
    bad = []
    for f in sorted(set(list(range(s.frame_start, s.frame_end + 1, step)) + [s.frame_end])):
        if any(a <= f <= b for a, b in FRAMING_SKIP):   # ex. cadru macro: produsul e tăiat intenționat
            continue
        s.frame_set(f)
        cam = s.camera   # cu markere legate de camere (tăieturi), camera activă se schimbă pe cadru
        dg = bpy.context.evaluated_depsgraph_get()
        world_pts = []
        for h in (hero if isinstance(hero, (list, tuple)) else [hero]):
            ev = h.evaluated_get(dg)
            if max(ev.matrix_world.to_scale()) < 0.02:  # încă invizibil (înainte de intrare): nu contează
                continue
            try:
                me = ev.to_mesh()
                vs_all = me.vertices
                stride = max(1, len(vs_all) // max_pts)
                world_pts += [ev.matrix_world @ vs_all[i].co for i in range(0, len(vs_all), stride)]
                ev.to_mesh_clear()
            except RuntimeError:  # obiect fără geometrie (empty etc.)
                world_pts += [ev.matrix_world @ Vector(c) for c in ev.bound_box]
        pts = [world_to_camera_view(s, cam, p) for p in world_pts]
        if not pts:
            continue
        us, vs, zs = [p.x for p in pts], [p.y for p in pts], [p.z for p in pts]
        out = min(zs) <= 0 or min(us) < margin or max(us) > 1 - margin or min(vs) < margin or max(vs) > 1 - margin
        area = max(0, min(max(us), 1) - max(min(us), 0)) * max(0, min(max(vs), 1) - max(min(vs), 0))
        # obiect scalat aproape de zero într-o intrare e normal în prima jumătate de secundă
        if out or (area < 0.01 and f > s.frame_start + s.render.fps // 2):
            bad.append({"frame": f, "u": [round(min(us), 3), round(max(us), 3)],
                        "v": [round(min(vs), 3), round(max(vs), 3)], "area": round(area, 4)})
    return bad


def render(a):
    s = bpy.context.scene
    if a.frames:
        for f in [int(x) for x in a.frames.split(",") if x.strip()]:
            s.frame_set(f)
            s.render.filepath = os.path.join(os.path.abspath(a.out), f"f_{f:04d}.png")
            bpy.ops.render.render(write_still=True)
    elif COUNTERS:   # text care se schimbă pe cadru: randare cadru cu cadru (handler-ul rulează sigur la frame_set)
        for f in range(s.frame_start, s.frame_end + 1):
            s.frame_set(f)
            s.render.filepath = os.path.join(os.path.abspath(a.out), f"f_{f:04d}.png")
            bpy.ops.render.render(write_still=True)
    else:
        bpy.ops.render.render(animation=True)


# ---------- PREMIUM: lumină de studio, post-procesare, atmosferă, animație pe litere ----------
ASSETS = "E:/claude-work/animatie-3d/_assets" if os.path.isdir("E:/") else os.path.expanduser("~/Videos/animatie-3d/_assets")


def ensure_hdri(asset_id="studio_small_09", res="2k"):
    """HDRI CC0 de pe Poly Haven, descărcat o singură dată în ASSETS (cache comun tuturor proiectelor).
    Alte HDRI-uri de studio bune: brown_photostudio_02, studio_small_08, photo_studio_loft_hall."""
    import json as _j, urllib.request as _u
    os.makedirs(ASSETS, exist_ok=True)
    dest = os.path.join(ASSETS, f"{asset_id}_{res}.hdr")
    if not os.path.exists(dest):
        hdr = {"User-Agent": "anim3d-skill/1.0"}
        meta = _j.loads(_u.urlopen(_u.Request(f"https://api.polyhaven.com/files/{asset_id}", headers=hdr), timeout=30).read())
        data = _u.urlopen(_u.Request(meta["hdri"][res]["hdr"]["url"], headers=hdr), timeout=120).read()
        open(dest, "wb").write(data)
        with open(os.path.join(ASSETS, "LICENTE.txt"), "a", encoding="utf-8") as f:
            f.write(f"{asset_id}: Poly Haven, CC0 (https://polyhaven.com/a/{asset_id})" + chr(10))
    return dest


def ensure_model(asset_id, res="1k"):
    """Model CC0 de pe Poly Haven (glTF + texturi), descărcat o singură dată în ASSETS/models/<id>/.
    Bune pentru reclame: lemon, food_lime_01, food_kiwi_01, food_apple_01, food_avocado_01, food_pomegranate_01,
    food_ginger_01, food_lychee_01, food_pears_asian_01, bananas, croissant. Întoarce calea .gltf."""
    import json as _j, urllib.request as _u
    d = os.path.join(ASSETS, "models", asset_id)
    hdr = {"User-Agent": "anim3d-skill/1.0"}
    marker = os.path.join(d, ".complet")
    if not os.path.exists(marker):
        meta = _j.loads(_u.urlopen(_u.Request(f"https://api.polyhaven.com/files/{asset_id}", headers=hdr),
                                   timeout=30).read())
        g = meta["gltf"][res]["gltf"]
        files = {os.path.basename(g["url"]): g["url"]}
        files.update({rel: v["url"] for rel, v in g.get("include", {}).items()})
        for rel, url in files.items():
            dest = os.path.join(d, rel.replace("/", os.sep))
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            open(dest, "wb").write(_u.urlopen(_u.Request(url, headers=hdr), timeout=120).read())
        open(marker, "w").write("ok")
        with open(os.path.join(ASSETS, "LICENTE.txt"), "a", encoding="utf-8") as f:
            f.write(f"{asset_id} (model): Poly Haven, CC0 (https://polyhaven.com/a/{asset_id})" + chr(10))
    return next(os.path.join(d, f) for f in os.listdir(d) if f.endswith(".gltf"))


def _model_proto(asset_id, size=1.0):
    """Importă modelul o dată, unește piesele într-un singur mesh normalizat (latura mare = size, centrat),
    ascuns; copiile din orbită îi folosesc datele (linked)."""
    path = ensure_model(asset_id)
    bpy.ops.object.select_all(action="DESELECT")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    ms = [o for o in new if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in ms:
        o.select_set(True)
    bpy.context.view_layer.objects.active = ms[0]
    for o in new:   # fără părinți (glTF are noduri), transformările aplicate în geometrie
        if o.type == "MESH":
            mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True, properties=False)
    if len(ms) > 1:
        bpy.ops.object.join()
    proto = bpy.context.view_layer.objects.active
    for o in new:
        if o is not proto and o.name in bpy.data.objects:
            bpy.data.objects.remove(o)
    pts = [v.co for v in proto.data.vertices]
    mn, mx = _bbox(pts); c = (mn + mx) / 2; k = size / max((mx - mn).length / 1.732, 1e-6)
    proto.data.transform(Matrix.Translation(-c)); proto.data.transform(Matrix.Scale(k, 4))
    proto.location = (0, 0, 0); proto.hide_render = True; proto.hide_set(True); proto.name = f"Proto_{asset_id}"
    return proto


def studio_world(hdri, strength=1.0, bg=(0.0, 0.0, 0.0, 1), horizon=(0.3, 0.8)):
    """HDRI doar pentru reflexii/lumină; camera vede fundal negru (Light Path: Is Camera Ray)."""
    w = bpy.data.worlds.new("W"); bpy.context.scene.world = w
    nt = w.node_tree; nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord"); mp = nt.nodes.new("ShaderNodeMapping")
    env = nt.nodes.new("ShaderNodeTexEnvironment"); env.image = bpy.data.images.load(hdri)
    b1 = nt.nodes.new("ShaderNodeBackground"); b1.inputs["Strength"].default_value = strength
    b2 = nt.nodes.new("ShaderNodeBackground"); b2.inputs["Color"].default_value = bg
    lp = nt.nodes.new("ShaderNodeLightPath"); mix = nt.nodes.new("ShaderNodeMixShader")
    o = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(tc.outputs["Generated"], mp.inputs["Vector"]); nt.links.new(mp.outputs["Vector"], env.inputs["Vector"])
    # întunecă banda orizontului: podeaua (reflexii razante) vede negru, textul vede studioul de sus
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = horizon[0]; mr.inputs["From Max"].default_value = horizon[1]
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"
    nt.links.new(tc.outputs["Generated"], sep.inputs[0]); nt.links.new(sep.outputs["Z"], mr.inputs["Value"])
    sock = lambda n, ident: next(i for i in n.inputs if i.identifier == ident)
    sock(mul, "Factor_Float").default_value = 1.0
    nt.links.new(env.outputs["Color"], sock(mul, "A_Color")); nt.links.new(mr.outputs[0], sock(mul, "B_Color"))
    nt.links.new(next(o for o in mul.outputs if o.identifier == "Result_Color"), b1.inputs["Color"])
    nt.links.new(lp.outputs["Is Camera Ray"], mix.inputs["Fac"])
    nt.links.new(b1.outputs[0], mix.inputs[1]); nt.links.new(b2.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], o.inputs["Surface"])
    return mp

def area(name, loc, energy, color=(1,1,1), size=2.0, target=(0,0,0), shape="RECTANGLE", sx=None, sy=None):
    bpy.ops.object.light_add(type="AREA", location=loc); l = bpy.context.object; l.name = name
    l.data.energy = energy; l.data.color = color; l.data.shape = shape
    if shape == "RECTANGLE": l.data.size = sx or size; l.data.size_y = sy or size
    else: l.data.size = size
    look_at(l, target); return l

def compositor(bloom=0.2, threshold=2.0, vignette=0.35, dispersion=0.012):
    """Post-procesare (Blender 5.x: scene.compositing_node_group + NodeGroupOutput; NU scene.node_tree):
    Glare Bloom doar pe highlight-uri -> Lens Distortion cu dispersie mică (aberație cromatică) -> vignetă."""
    s = bpy.context.scene
    ng = bpy.data.node_groups.new("Post", "CompositorNodeTree"); s.compositing_node_group = ng
    ng.interface.new_socket("Image", in_out="OUTPUT", socket_type="NodeSocketColor")
    N = ng.nodes.new; L = ng.links.new
    rl = N("CompositorNodeRLayers"); out = N("NodeGroupOutput")
    g = N("CompositorNodeGlare"); g.inputs["Type"].default_value = "Bloom"
    g.inputs["Threshold"].default_value = threshold; g.inputs["Strength"].default_value = bloom
    g.inputs["Size"].default_value = 0.6 if g.inputs["Size"].type == "VALUE" else 8
    ld = N("CompositorNodeLensdist"); ld.inputs["Dispersion"].default_value = dispersion
    em = N("CompositorNodeEllipseMask"); em.inputs["Size"].default_value = (1.25, 1.25)
    bl = N("CompositorNodeBlur"); 
    mix = N("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"
    sock = lambda node, ident: next(i for i in node.inputs if i.identifier == ident)
    L(rl.outputs["Image"], g.inputs["Image"]); L(g.outputs["Image"], ld.inputs["Image"])
    px = round(0.16 * max(s.render.resolution_x, s.render.resolution_y))  # relativ: preview = final, la altă scară
    bl.inputs["Size"].default_value = (px, px) if bl.inputs["Size"].type == "VECTOR" else px
    L(em.outputs[0], bl.inputs["Image"])
    L(bl.outputs[0], sock(mix, "B_Color")); L(ld.outputs[0], sock(mix, "A_Color"))
    sock(mix, "Factor_Float").default_value = vignette
    L(mix.outputs[2] if len(mix.outputs) > 2 else mix.outputs[0], out.inputs[0])
    return g


def impact_pulse(glare, cam, frame, bloom=0.2, peak=0.4, lens_gain=0.03):
    """La impact: bloom-ul crește scurt (4-8 cadre) și camera face un „punch-in” de ~3% care rămâne."""
    fps = bpy.context.scene.render.fps
    st = glare.inputs["Strength"]
    for f, v in ((frame - 2, bloom), (frame + 1, peak), (frame + max(4, fps // 4), bloom)):
        st.default_value = v; st.keyframe_insert("default_value", frame=f)
    L0 = cam.data.lens
    for f, v in ((frame - 2, L0), (frame + max(3, fps // 8), L0 * (1 + lens_gain))):
        cam.data.lens = v; cam.data.keyframe_insert("lens", frame=f)
    cam.data.lens = L0


def camera_shake(cam, frame, amount, dur_s=0.35):
    """Tremur scurt de cameră la impact (modificator NOISE pe curbele de locație, doar în intervalul impactului):
    impactul se SIMTE, nu doar se vede. `amount` în unități de scenă (~0,5% din distanța până la text)."""
    fps = bpy.context.scene.render.fps
    for i, fc in enumerate(c for c in _fcurves(cam) if c.data_path == "location"):
        m = fc.modifiers.new("NOISE")
        m.scale = 2.5; m.strength = amount; m.phase = 7.0 * (i + 1); m.depth = 1
        m.use_restricted_range = True
        m.frame_start = frame; m.frame_end = frame + max(4, int(fps * dur_s))
        m.blend_in = 1; m.blend_out = max(2, int(fps * dur_s * 0.7))


def outro(letters, kind, end, unit=1.0, dur_s=0.7):
    """Ieșire la final. exit: literele pleacă decalat în sus și se micșorează (EASE_IN, accelerează spre ieșire);
    fade: imaginea se stinge în negru (expunere animată; la ALPHA folosește exit). Întoarce secunda de start."""
    s = bpy.context.scene; fps = s.render.fps
    n = max(1, int(fps * dur_s)); f0 = end - n
    if kind == "fade":
        s.view_settings.exposure = 0.0; s.view_settings.keyframe_insert("exposure", frame=f0)
        s.view_settings.exposure = -10.0; s.view_settings.keyframe_insert("exposure", frame=end)
        s.view_settings.exposure = 0.0
        return f0 / fps
    if kind == "exit":
        dur = max(2, n // 2); spread = n - dur   # decalajul se întinde pe fereastra rămasă: ULTIMA literă
        # termină exact la `end` (înainte, cu pas întreg >= 1, literele de după ~n/2 porneau după end și rămâneau
        # pe ecran la scale 1 — bug văzut la fraze lungi, 2026-11-22)
        s.frame_set(max(s.frame_start, f0 - 1))   # poza EVALUATĂ chiar înainte de ieșire (atributul curent poate fi
        rest = {L.name: L.location.copy() for L in letters}   # al altui cadru -> literele ar coborî sub podea)
        for i, L in enumerate(letters):
            a = f0 + round(spread * i / max(1, len(letters) - 1)); b = a + dur
            loc = rest[L.name]
            key(L, "location", tuple(loc), a, "QUAD", "EASE_IN"); key(L, "scale", (1, 1, 1), a, "BACK", "EASE_IN")
            key(L, "location", tuple(loc + Vector((0, 0, 0.35 * unit))), b, "QUAD", "EASE_IN")
            key(L, "scale", (0.001,) * 3, b, "BACK", "EASE_IN")
        return f0 / fps
    return None


def dust(count=45, box=((-5, 5), (-9, 2), (-0.3, 3.0)), size=0.018, strength=2.5, drift=(0.25, 0, 0.35), seed=7):
    """Praf/scântei care plutesc lent. Cu DOF devin bokeh: semnătura vizuală a unui reveal „cinematic”."""
    import random
    r = random.Random(seed); s = bpy.context.scene
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=size)
    base = bpy.context.object; base.name = "Dust"
    base.data.materials.append(material("DustMat", (1, .8, .55, 1), emission=(1, .8, .55, 1), strength=strength))
    objs = [base] + [base.copy() for _ in range(count - 1)]
    for o in objs[1:]:
        s.collection.objects.link(o)
    for o in objs:
        p0 = Vector([r.uniform(*box[i]) for i in range(3)])
        d = Vector([drift[i] * r.uniform(0.5, 1.5) for i in range(3)])
        o.scale = (r.uniform(0.4, 1.6),) * 3
        key(o, "location", tuple(p0), s.frame_start, "LINEAR"); key(o, "location", tuple(p0 + d), s.frame_end, "LINEAR")
    return objs


def god_rays(target=(0, 0, 0.9), density=0.006, beam=(0, 7, 4.5), energy=2500, color=(1, .85, .65)):
    """Volum de ceață + spot în spatele textului îndreptat spre cameră -> raze prin litere."""
    s = bpy.context.scene
    s.eevee.volumetric_tile_size = "4"; s.eevee.volumetric_samples = 64; s.eevee.use_volumetric_shadows = True
    bpy.ops.object.select_all(action="DESELECT")
    # ceața doar între cameră și puțin în spatele textului: sursa razei (y=7) rămâne ÎN AFARA volumului,
    # altfel apare ca un „bec” luminos sus în cadru (vizibil mai ales în 9:16)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, -9, 2)); v = bpy.context.object; v.name = "Haze"
    v.scale = (30, 22, 6)
    m = bpy.data.materials.new("HazeMat")
    nt = m.node_tree; nt.nodes.clear()
    pv = nt.nodes.new("ShaderNodeVolumePrincipled"); pv.inputs["Density"].default_value = density
    o = nt.nodes.new("ShaderNodeOutputMaterial"); nt.links.new(pv.outputs[0], o.inputs["Volume"])
    v.data.materials.append(m)
    for o in s.objects:  # doar raza luminează ceața; restul luminilor nu (altfel imaginea se spală)
        if o.type == "LIGHT":
            o.data.volume_factor = 0.0
    bpy.ops.object.light_add(type="SPOT", location=beam); sp = bpy.context.object; sp.name = "Beam"
    sp.data.energy = energy; sp.data.color = color; sp.data.spot_size = math.radians(35); sp.data.spot_blend = 0.6
    sp.data.diffuse_factor = 0.0; sp.data.specular_factor = 0.0  # luminează DOAR ceața (fără dâră/umbre pe podea)
    look_at(sp, target)
    return sp


def split_letters(text_obj):
    """Text -> listă de obiecte-literă, în ordinea de citire (rând cu rând, stânga->dreapta), fiecare cu originea
    în centrul ei. Diacriticele (ă, ș, ț, î, â) rămân lipite de litera lor; textul pe mai multe rânduri
    (TEXT cu "\n") se împarte întâi pe rânduri, ca literele de pe rânduri diferite să nu se unească."""
    n_lines = text_obj.data.body.strip("\n").count("\n") + 1   # rândurile le dă TEXTUL, nu geometria
    bpy.ops.object.select_all(action="DESELECT")
    text_obj.select_set(True); bpy.context.view_layer.objects.active = text_obj
    bpy.ops.object.convert(target="MESH")
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
    parts = []
    for o in bpy.context.selected_objects:  # toate măsurătorile ÎNAINTE de join (după join obiectele dispar)
        ws = [o.matrix_world @ Vector(c) for c in o.bound_box]
        parts.append({"o": o, "x0": min(w.x for w in ws), "x1": max(w.x for w in ws),
                      "z0": min(w.z for w in ws), "z1": max(w.z for w in ws)})
    # O literă poate ieși din MAI MULTE piese (bevel lat, contururi interioare, diacritice), deci nu ghicim
    # rândurile din mărimea pieselor: le tăiem pe Z la cele mai mari (n_lines - 1) goluri dintre centre.
    by_z = sorted(parts, key=lambda p: -(p["z0"] + p["z1"]) / 2)
    zc = [(p["z0"] + p["z1"]) / 2 for p in by_z]
    cuts = sorted(sorted(range(1, len(by_z)), key=lambda i: zc[i - 1] - zc[i], reverse=True)[:n_lines - 1])
    rows, prev = [], 0
    for c in cuts + [len(by_z)]:
        rows.append({"items": by_z[prev:c]}); prev = c
    groups = []
    for r in rows:
        r["items"].sort(key=lambda p: p["x0"])
        row_groups = []
        for p in r["items"]:
            w = p["x1"] - p["x0"]
            if row_groups and (min(p["x1"], row_groups[-1]["x1"]) - max(p["x0"], row_groups[-1]["x0"])) > 0.3 * min(w, row_groups[-1]["x1"] - row_groups[-1]["x0"]):
                row_groups[-1]["objs"].append(p["o"]); row_groups[-1]["x1"] = max(row_groups[-1]["x1"], p["x1"])
            else:
                row_groups.append({"objs": [p["o"]], "x0": p["x0"], "x1": p["x1"]})
        groups += row_groups
    letters = []
    for g in groups:
        bpy.ops.object.select_all(action="DESELECT")
        for o in g["objs"]: o.select_set(True)
        bpy.context.view_layer.objects.active = g["objs"][0]
        if len(g["objs"]) > 1: bpy.ops.object.join()
        L = bpy.context.view_layer.objects.active
        bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
        letters.append(L)
    for i, L in enumerate(letters): L.name = f"L{i:02d}"
    return letters


def stagger_reveal(letters, start=1, stagger=3, dur=24, rise=0.8, tilt=70, overshoot=4, style="rise"):
    """Fiecare literă intră decalat, depășește ușor ținta și se așază (pornire -> overshoot la 70% -> settle).
    style: rise (se ridică din podea, basculând) | drop (cade de sus și sare puțin) |
           flip (se întoarce pe axa verticală, ca o carte) | pop (apare din nimic, cu recul de mărime)."""
    V = lambda z: Vector((0, 0, z))
    for i, L in enumerate(letters):
        f0 = start + i * stagger; f1 = f0 + int(dur * 0.7); f2 = f0 + dur
        loc = L.location.copy(); rot = L.rotation_euler.copy()
        if style == "drop":
            key(L, "location", tuple(loc + V(rise * 1.6)), f0, "QUAD", "EASE_IN")       # cade accelerând
            key(L, "location", tuple(loc), f0 + int(dur * 0.45), "QUAD", "EASE_OUT")   # atinge locul
            key(L, "location", tuple(loc + V(rise * 0.08)), f1, "QUAD", "EASE_IN")     # mic salt
            key(L, "location", tuple(loc), f2, "SINE", "EASE_OUT")
        elif style == "flip":
            key(L, "rotation_euler", (rot.x, rot.y, rot.z + math.radians(-100)), f0, "BACK", "EASE_OUT")
            key(L, "rotation_euler", tuple(rot), f2, "BACK", "EASE_OUT")
        elif style == "pop":
            key(L, "scale", (0.001,) * 3, f0, "BACK", "EASE_OUT")
            key(L, "scale", (1.12,) * 3, f1, "SINE", "EASE_IN_OUT")
            key(L, "scale", (1, 1, 1), f2, "SINE", "EASE_IN_OUT")
            key(L, "rotation_euler", (rot.x, rot.y + math.radians(-12), rot.z), f0, "BACK", "EASE_OUT")
            key(L, "rotation_euler", tuple(rot), f2, "BACK", "EASE_OUT")
            continue   # pop își face singur scale-ul de intrare
        else:  # rise
            key(L, "location", tuple(loc - V(rise)), f0, "EXPO", "EASE_OUT")
            key(L, "location", tuple(loc + V(rise * 0.03)), f1, "SINE", "EASE_IN_OUT")
            key(L, "location", tuple(loc), f2, "SINE", "EASE_IN_OUT")
            key(L, "rotation_euler", (rot.x + math.radians(tilt), rot.y, rot.z), f0, "EXPO", "EASE_OUT")
            key(L, "rotation_euler", (rot.x - math.radians(overshoot), rot.y, rot.z), f1, "SINE", "EASE_IN_OUT")
            key(L, "rotation_euler", tuple(rot), f2, "SINE", "EASE_IN_OUT")
        key(L, "scale", (0.001, 0.001, 0.001), f0 - 1, "CONSTANT"); key(L, "scale", (1, 1, 1), f0, "CONSTANT")
    return start + (len(letters) - 1) * stagger + dur  # cadrul în care s-a așezat ultima literă


EVENTS = {}  # momente cheie (secunde) pentru sunet; scrise în <out>/../events.json, citite de sound.py

# ---------- SCENA: editează doar de aici în jos ----------
MODE = "reveal"            # reveal (titlu/logo care intră) | turntable (produs, buclă) | lowerthird (plăcuță nume, transparent) | endscreen | mockup (telefon/laptop) | exploded (piesele se desfac) | stats (cifre care numără) | kinetic (fraze una după alta) | particles (particule care formează textul/logo-ul) | commercial (reclamă de produs în 5 cadre)
                           # | mockup (telefon/laptop procedural cu ecranul clientului; folosește și STUDIO, COLOR)
DEVICE = "phone"           # mockup: phone (71x147x8 mm, ecran ~19,5:9) | laptop (16:10, deschis 110°)
SCREEN = None              # mockup: cale ABSOLUTĂ către ecranul clientului: .png/.jpg sau .mp4/.mov/.webm
SCREEN_FIT = "cover"       # mockup: cover (umple ecranul, taie surplusul) | contain (tot ecranul vizibil, benzi negre)
SCREEN_GLARE = 0.06        # mockup: reflexia de sticlă de pe ecran (liniar, adunat peste imagine); 0 = fără
SCREEN_SHINE = 0.10        # mockup: dâra de lumină care traversează ecranul la așezare; 0 = fără
MODEL = None               # turntable: cale .glb/.gltf/.obj/.fbx/.stl; None = TEXT/SVG pe soclu
STUDIO = "dark"            # turntable: dark (negru, reflexii dramatice) | light (cicloramă albă, e-commerce)
TT_FILL = None             # turntable: cât din cadru ocupă produsul; None = calibrat (0.65 / 1:1 0.72 / 9:16 0.74)
BG_EMIT = 4.5              # turntable light: emisia cicloramei; 4.5 = RGB real ~250 sus / 255 jos (AgX); 0 = gri natural
CONTACT_SHADOW = 0.93      # turntable light: umbra de sub produs = cât din lumina podelei taie la contact (0 = fără)
TURNS = 1.0                # turntable: câte ture complete pe durata clipului (buclă perfectă la orice valoare întreagă)
EXPLODE = 1.0              # exploded: cât de departe pleacă piesele (1 = normal, 1.5 = larg, 0.6 = discret)
EXPLODE_AXIS = "radial"    # exploded: radial (din centru în toate direcțiile) | z (urcă stivuite, ca un teardown)
EXPLODE_BACK = True        # exploded: True = se reasamblează la final; False = rămâne explodat
STATS = [("1.250+", "clienți mulțumiți"), ("98%", "rată de succes"), ("15", "ani de experiență")]
                           # stats: 1-4 perechi (valoare, etichetă); valoarea numără de la 0 (punct = mii, virgulă = zecimale)
FONT_LABEL = "segoe"       # stats: fontul etichetelor (cheie din FONTS sau cale .ttf)
PHRASES = ["Ai nevoie de un site?", "Îl facem în 7 zile.", "Scrie-ne azi!"]
                           # kinetic: 1-5 fraze, una după alta (cârlig -> beneficiu -> îndemn); ultima rămâne.
                           # SECONDS ~ 2,5 s/frază + 1 s ținere (3 fraze ≈ 8 s); ANIM3D_WARN spune exact cât
KINETIC_STYLES = ("rise", "pop", "drop")   # kinetic: stilul de intrare al fiecărei fraze, pe rând (rise|drop|flip|pop)
PRODUCT = "bottle"         # commercial: produs procedural când MODEL = None: bottle | jar | can | box
LABEL_IMAGE = None         # commercial: cale ABSOLUTĂ PNG/JPG cu designul etichetei sau logo-ul (PNG: transparența se respectă);
                           # înlocuiește textul brandului de pe etichetă (TEXT rămâne în packshot)
LABEL_FIT = "contain"      # commercial: contain (încape toată, centrată) | cover (umple eticheta, taie surplusul)
INGREDIENTS = True         # commercial: True = sfere/cuburi stilizate | listă de modele Poly Haven CC0, ex.
                           # ["lemon", "food_lime_01"] (descărcate o dată în _assets/models) | False = fără
SPLASH = False             # commercial: True = splash de lichid (coroană + picături) când produsul aterizează în packshot
CONDENSATION = 0.0         # commercial: picături de condens pe produs (0 = fără, 1 = normal, 1,5 = mult); „rece, de băut”
ASPECT = "16:9"            # 16:9 | 9:16 | 1:1 | 4:5  (aici, nu în shell: nu se poate pierde între comenzi)
SECONDS = 4.0              # min. ~3 s; 5 s dacă ai SUBTITLE
FPS = 30
TEXT = "Bună ziua"
SVG = None                # cale absolută către un logo .svg -> înlocuiește textul (animat ca un singur obiect)
MATERIAL = "aur"           # aur | crom | plastic | neon | sticla | iridescent | svg (culorile originale ale logo-ului SVG)
COLOR = None               # culoare RGBA pentru plastic/neon, ex. (0.85, 0.05, 0.12, 1); None = implicit
HDRI_ID = "studio_small_09"
ALPHA = False              # True = fundal transparent (overlay în editoare): fără podea vizibilă, ceață, halo;
                           # finalize.py -> .mov (ProRes 4444) sau .webm (VP9 cu alfa)
DUST = True                # particule bokeh în aer; False pentru un overlay curat
OUTRO = "none"             # reveal: none (ține titlul până la final) | exit (literele pleacă) | fade (se stinge în negru)
SHAKE = 1.0                # reveal: tremurul camerei la impact (0 = fără, 1 = discret, 2 = puternic)
REVEAL = "rise"            # reveal: rise (din podea) | drop (cade de sus) | flip (se întoarce ca o carte) | pop
SUBTITLE = ""              # rând mic sub titlu, apare după impact (ex. "Since 2026"); "" = fără
CARD_W = None              # puterea softbox-ului legat de cameră; None = după material
SWEEP_W = None             # puterea luminii care trece peste text la impact; None = după material
STRIPS = None              # crom: (sus, jos) puterea benzilor de reflexie; None = după material
FONT = "segoe-black"       # cheie din FONTS sau cale absolută .ttf (verifică întâi diacriticele, vezi SKILL.md)
BACKGROUND = "negru"       # negru | glow (strălucire colorată difuză în spatele textului)
BG_COLOR = (0.15, 0.35, 1.0, 1)  # culoarea strălucirii pentru BACKGROUND = "glow"
PARTICLES = 6000           # particles: câte particule formează textul/logo-ul (memorie puțină -> 3000)
PARTICLE_STYLE = "nor"     # particles: nor (nor împrăștiat care se adună) | vartej (spirală în jurul axei verticale)
PARTICLE_KEEP = 0.0        # particles: ce parte din particule rămâne ca sclipiri pe text (0-0,05; mai mult arată pătat)

# fonturi Windows verificate pe 2026-11-21 că au ȘȚĂÎÂ șțăîâ (Gill Sans, Rockwell, Bauhaus, Stencil NU au ș/ț)
FONTS = {"segoe-black": "seguibl.ttf", "arial-black": "ariblk.ttf", "arial": "arialbd.ttf", "impact": "impact.ttf",
         "segoe": "segoeuib.ttf", "bahnschrift": "bahnschrift.ttf", "georgia": "georgiab.ttf",
         "trebuchet": "trebucbd.ttf", "verdana": "verdanab.ttf", "calibri": "calibrib.ttf"}


def font_path(name=None):
    name = name or FONT
    p = name if os.path.isabs(name) else "C:/Windows/Fonts/" + FONTS.get(name, name)
    if not os.path.exists(p):
        raise FileNotFoundError(f"font inexistent: {p}")
    return p


def backdrop(kind=None, color=None, dist=14.0):
    """Fundal. "glow" = plan mare departe în spate, cu emisie în gradient radial (centru colorat -> negru la
    margini): dă adâncime și culoare de brand fără decor care să concureze cu textul; podeaua îl reflectă difuz."""
    kind = kind or BACKGROUND
    if kind != "glow":
        return None
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, dist, 1.5), rotation=(math.radians(90), 0, 0))
    bd = bpy.context.object; bd.name = "Backdrop"; bd.scale = (40, 22, 1)
    m = bpy.data.materials.new("BackdropMat"); nt = m.node_tree; nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord"); gr = nt.nodes.new("ShaderNodeTexGradient"); gr.gradient_type = "SPHERICAL"
    mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (3.2, 5.8, 1.0)
    mp.inputs["Location"].default_value = (-1.6, -2.9, 0)
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0, 0, 0, 1); ramp.color_ramp.elements[1].color = color or BG_COLOR
    ramp.color_ramp.elements[1].position = 1.0
    em = nt.nodes.new("ShaderNodeEmission"); em.inputs["Strength"].default_value = 0.18
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(tc.outputs["UV"], mp.inputs["Vector"]); nt.links.new(mp.outputs["Vector"], gr.inputs["Vector"])
    nt.links.new(gr.outputs["Fac"], ramp.inputs["Fac"]); nt.links.new(ramp.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs[0], o.inputs["Surface"])
    bd.data.materials.append(m)
    return bd


def brand_materials(obj, roughness=0.2):
    """MATERIAL = "svg": fiecare culoare din SVG (diffuse_color pus de importer) devine plastic lucios lăcuit."""
    for i, m in enumerate(list(obj.data.materials)):
        c = tuple(m.diffuse_color) if m else (0.8, 0.8, 0.8, 1)
        nm = material(f"Brand{i}", c, metallic=0.0, roughness=roughness)
        nm.node_tree.nodes["Principled BSDF"].inputs["Coat Weight"].default_value = 1.0
        nm.node_tree.nodes["Principled BSDF"].inputs["Coat Roughness"].default_value = 0.03
        obj.data.materials[i] = nm


def hero_material(kind=None, color=None):
    """Rețete Principled verificate prin randare (2026-11-21). kind="svg" (subtitlul unui logo color) = plastic.
    Implicit MATERIAL/COLOR citite LA APEL (nu la definire), ca un override de mai jos să conteze."""
    kind = kind or MATERIAL
    color = COLOR if color is None else color
    if kind == "svg":
        kind, color = "plastic", color or (0.9, 0.9, 0.9, 1)
    if kind == "crom":
        return material("Chrome", color or (0.9, 0.91, 0.92, 1), metallic=1.0, roughness=0.05)
    if kind == "plastic":
        m = material("Plastic", color or (0.75, 0.04, 0.08, 1), metallic=0.0, roughness=0.18)
        m.node_tree.nodes["Principled BSDF"].inputs["Coat Weight"].default_value = 1.0
        m.node_tree.nodes["Principled BSDF"].inputs["Coat Roughness"].default_value = 0.03
        return m
    if kind == "sticla":   # sticlă clară cu refracție (EEVEE: raytracing de refracție pe material)
        m = material("Glass", color or (0.92, 0.96, 1.0, 1), metallic=0.0, roughness=0.02)
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Transmission Weight"].default_value = 1.0; b.inputs["IOR"].default_value = 1.45
        m.use_raytrace_refraction = True; m.surface_render_method = "DITHERED"
        return m
    if kind == "iridescent":
        # curcubeu care se schimbă cu unghiul: Layer Weight (Facing) -> rampă de culori -> metal lucios.
        # Thin Film din Principled practic NU se vede în EEVEE (verificat 2026-11-22: text gri-argintiu).
        m = material("Iridescent", (0.9, 0.9, 0.9, 1), metallic=1.0, roughness=0.14)
        nt = m.node_tree; b = nt.nodes["Principled BSDF"]
        # fețele frontale privesc toate spre cameră (Facing ~0 -> o singură culoare): adăugăm poziția în LUME
        # (gradient de-a lungul cuvântului, același pe toate literele) + zgomot, apoi fract -> rampă ciclică
        lw = nt.nodes.new("ShaderNodeLayerWeight"); lw.inputs["Blend"].default_value = 0.35
        geo = nt.nodes.new("ShaderNodeNewGeometry"); sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 1.2
        def madd(a, b, k):
            n = nt.nodes.new("ShaderNodeMath"); n.operation = "MULTIPLY_ADD"; n.inputs[1].default_value = k
            nt.links.new(a, n.inputs[0]); nt.links.new(b, n.inputs[2]); return n.outputs[0]
        nt.links.new(geo.outputs["Position"], sep.inputs[0]); nt.links.new(geo.outputs["Position"], nz.inputs["Vector"])
        f = madd(sep.outputs["X"], lw.outputs["Facing"], 0.22)
        f = madd(sep.outputs["Z"], f, 0.12)
        f = madd(nz.outputs["Fac"], f, 0.35)
        fr = nt.nodes.new("ShaderNodeMath"); fr.operation = "FRACT"; nt.links.new(f, fr.inputs[0])
        rp = nt.nodes.new("ShaderNodeValToRGB"); el = rp.color_ramp.elements
        stops = [(0.0, (0.55, 0.2, 1.0, 1)), (0.22, (0.1, 0.75, 1.0, 1)), (0.45, (0.2, 1.0, 0.55, 1)),
                 (0.65, (1.0, 0.8, 0.2, 1)), (0.85, (1.0, 0.25, 0.6, 1)), (1.0, (0.55, 0.2, 1.0, 1))]   # ciclică
        el[0].position, el[0].color = stops[0]; el[1].position, el[1].color = stops[-1]
        for pos, col in stops[1:-1]:
            e = el.new(pos); e.color = col
        nt.links.new(fr.outputs[0], rp.inputs["Fac"]); nt.links.new(rp.outputs["Color"], b.inputs["Base Color"])
        b.inputs["Thin Film Thickness"].default_value = 450.0   # contează în Cycles
        return m
    if kind == "neon":
        c = color or (0.0, 0.85, 1.0, 1)
        return material("Neon", (0.02, 0.02, 0.02, 1), roughness=0.3, emission=c, strength=6.0)
    return material("Gold", color or (1.0, 0.766, 0.336, 1), metallic=1.0, roughness=0.12)


def import_model(path, size=2.0):
    """Importă un model 3D, îl pune pe un pivot (Empty) cu baza pe podea (z=0), centrat pe axa de rotație, și îl
    scalează ca latura cea mai mare să fie `size`. Întoarce (pivot, meshes). Rotești PIVOTUL, nu piesele."""
    ext = os.path.splitext(path)[1].lower()
    if not os.path.exists(path):
        raise FileNotFoundError(path)
    bpy.ops.object.select_all(action="DESELECT")
    before = set(bpy.data.objects)
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)   # glTF e Y-up; importerul îl convertește la Z-up
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext == ".stl":
        bpy.ops.wm.stl_import(filepath=path)
    else:
        raise ValueError(f"format nesuportat: {ext} (merg .glb .gltf .obj .fbx .stl)")
    new = [o for o in bpy.data.objects if o not in before]
    return _pivot_for([o for o in new if o.type == "MESH"], new, size)


def _pivot_for(meshes, objs, size):
    if not meshes:
        raise RuntimeError("modelul nu conține niciun mesh")
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    mn = Vector([min(p[i] for p in pts) for i in range(3)]); mx = Vector([max(p[i] for p in pts) for i in range(3)])
    bpy.ops.object.empty_add(location=((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z)); pivot = bpy.context.object
    pivot.name = "Pivot"
    bpy.context.view_layer.update()
    for o in objs:
        if o.parent is None and o is not pivot:
            mw = o.matrix_world.copy(); o.parent = pivot; o.matrix_world = mw
    k = size / max(mx.x - mn.x, mx.y - mn.y, mx.z - mn.z, 1e-6)
    pivot.location = (0, 0, 0); pivot.scale = (k, k, k)
    bpy.context.view_layer.update()
    return pivot, meshes


def cyclorama(color=(0.8, 0.8, 0.8, 1), roughness=0.5, radius=4.0, depth=8.0, height=10.0, width=60.0):
    """Fundal de studio fără colț: podea care se curbează lin în perete (fără linie de orizont)."""
    import bmesh
    prof = [(0, y) for y in (-30.0, -10.0, 0.0)]
    for i in range(1, 13):  # sfert de cerc
        t = math.radians(90 * i / 12)
        prof.append((radius * (1 - math.cos(t)), radius * math.sin(t)))
    prof = [(z, y) for z, y in prof] + [(height, radius)]
    me = bpy.data.meshes.new("Cyclo"); bm = bmesh.new()
    rows = []
    for x in (-width / 2, width / 2):
        rows.append([bm.verts.new((x, depth + y, z)) for z, y in prof])
    for i in range(len(prof) - 1):
        bm.faces.new((rows[0][i], rows[1][i], rows[1][i + 1], rows[0][i + 1]))
    bm.to_mesh(me); bm.free()
    cy = bpy.data.objects.new("Cyclorama", me); bpy.context.scene.collection.objects.link(cy)
    for p in me.polygons:
        p.use_smooth = True
    me.materials.append(material("CycloMat", color, roughness=roughness, specular=0.2))
    return cy


def contact_shadow(pivot, meshes, strength=None, reach=1.1, soft=1.3):
    """Disc de umbră sub produs (studio light): ancorează produsul pe fundalul alb. Rotund, centrat pe axă: nu strică bucla.
    Fundalul e MULT peste alb (radianță ~5-10) și AgX comprimă vârful, deci o alfa liniară nu se vede (0,4 alfa = 6 unități).
    Alfa = 1 - (1-strength)^g: scade radianța în trepte egale de expunere, deci pe ecran umbra coboară lin.
    g = 1 sub produs, 0 la marginea discului (alfa exact 0 acolo: fără margine). reach = cât iese din baza produsului,
    în raze; partea vizibilă e ~80% din asta (restul e încă peste alb)."""
    strength = CONTACT_SHADOW if strength is None else strength
    if strength <= 0:
        return None
    strength = min(strength, 0.995)
    bpy.context.view_layer.update()
    # raza BAZEI din vârfuri (zecimea de jos), nu din colțurile bbox-ului (acelea dau ~1,41x la obiecte rotunde)
    pts = []
    for o in meshes:
        if o.type == "MESH" and len(o.data.vertices):
            pts += _sample_pts(o, 4000)
        else:
            pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    h = max(p.z for p in pts)
    low = [p for p in pts if p.z <= 0.1 * h] or pts
    r = max(math.hypot(p.x, p.y) for p in low)
    r_out = r * (1 + reach)
    bpy.ops.mesh.primitive_circle_add(vertices=128, radius=r_out, fill_type="NGON", location=(0, 0, 0.002))
    d = bpy.context.object; d.name = "ContactShadow"
    m = bpy.data.materials.new("ContactShadowMat"); nt = m.node_tree; nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord")
    ln = nt.nodes.new("ShaderNodeVectorMath"); ln.operation = "LENGTH"
    mr = nt.nodes.new("ShaderNodeMapRange"); mr.clamp = True   # distanța de la axă -> g (1 sub produs ... 0 la margine)
    mr.inputs["From Min"].default_value = r * 0.85; mr.inputs["From Max"].default_value = r_out
    mr.inputs["To Min"].default_value = 1.0; mr.inputs["To Max"].default_value = 0.0
    pw = nt.nodes.new("ShaderNodeMath"); pw.operation = "POWER"; pw.inputs[1].default_value = soft
    ex = nt.nodes.new("ShaderNodeMath"); ex.operation = "POWER"; ex.inputs[0].default_value = 1.0 - strength
    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
    tr = nt.nodes.new("ShaderNodeBsdfTransparent"); bk = nt.nodes.new("ShaderNodeBsdfDiffuse")
    bk.inputs["Color"].default_value = (0, 0, 0, 1)
    mx = nt.nodes.new("ShaderNodeMixShader"); o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(tc.outputs["Object"], ln.inputs[0]); nt.links.new(ln.outputs["Value"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], pw.inputs[0]); nt.links.new(pw.outputs[0], ex.inputs[1])
    nt.links.new(ex.outputs[0], inv.inputs[1])
    nt.links.new(inv.outputs[0], mx.inputs["Fac"]); nt.links.new(tr.outputs[0], mx.inputs[1])
    nt.links.new(bk.outputs[0], mx.inputs[2]); nt.links.new(mx.outputs[0], o.inputs["Surface"])
    m.surface_render_method = "BLENDED"
    d.data.materials.append(m)
    return d


def product_studio(pivot, meshes, shadow_reach=1.1):
    """Decorul de produs comun (turntable, exploded). light = cicloramă albă cu emisie + umbră de contact;
    dark = podea neagră lucioasă + HDRI ascuns de cameră (+ BACKGROUND glow)."""
    s = bpy.context.scene
    if STUDIO == "light":
        s.view_settings.look = "AgX - Medium High Contrast"
        studio_world(ensure_hdri(HDRI_ID), strength=1.0, bg=(0.9, 0.9, 0.9, 1), horizon=(-1.0, -0.5))
        cy = cyclorama((0.9, 0.9, 0.9, 1), roughness=0.55)
        # alb ADEVĂRAT (RGB real măsurat ~250 sus / 255 jos): ciclorama primește și emisie albă
        bsdf = cy.data.materials[0].node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Emission Color"].default_value = (1, 1, 1, 1)
        bsdf.inputs["Emission Strength"].default_value = BG_EMIT
        contact_shadow(pivot, meshes, reach=shadow_reach)   # fără ea produsul ar pluti (text: umbra soclului)
    else:
        s.view_settings.look = "AgX - High Contrast"
        studio_world(ensure_hdri(HDRI_ID), strength=1.2)
        backdrop()
        bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, 0))
        bpy.context.object.data.materials.append(
            material("Floor", (0.004, 0.004, 0.005, 1), roughness=0.12, specular=0.35))


def product_lights(cam, meshes, h):
    """Key/fill/rim/top + softbox legat de cameră; pe dark, light linking doar pe produs."""
    key_w, fill_w, rim_w = (900, 350, 600) if STUDIO == "light" else (700, 200, 1500)
    lights = [
        area("Key", (-3.5, -4, 4.5), key_w, (1, .96, .9), shape="RECTANGLE", sx=3, sy=2, target=(0, 0, h * 0.5)),
        area("Fill", (4, -3, 2), fill_w, (.9, .95, 1), shape="RECTANGLE", sx=3, sy=3, target=(0, 0, h * 0.4)),
        area("Rim", (0, 4.5, h + 2.5), rim_w, (1, 1, 1), shape="RECTANGLE", sx=4, sy=1, target=(0, 0, h * 0.6)),
        area("Top", (0, 0, h + 4), 300, (1, 1, 1), shape="RECTANGLE", sx=3, sy=3, target=(0, 0, 0))]
    # softbox legat de cameră: fețele metalice/lăcuite care privesc spre cameră au ce reflecta (altfel ies negre)
    card = area("Card", (0, 0, 0), CARD_W if CARD_W is not None else 250, (1, .97, .92), shape="RECTANGLE", sx=12, sy=3)
    card.data.use_shadow = False; card.parent = cam; card.location = (0, 0, 4); card.rotation_euler = (0, 0, 0)
    lights.append(card)
    if STUDIO != "light":   # podeaua neagră lucioasă ar reflecta softbox-urile ca pete albe: luminile bat doar produsul
        rcv = bpy.data.collections.new("ProductReceivers")
        for m in meshes: rcv.objects.link(m)
        for l in lights: l.light_linking.receiver_collection = rcv
    return lights


def build_turntable(a):
    """Produs (sau TEXT/SVG) care se rotește în buclă perfectă: fără intrare, fără praf, cameră fixă,
    cheia de 360° la end+1. Studio dark = podea neagră lucioasă + HDRI ascuns; light = cicloramă albă."""
    s = bpy.context.scene; end = s.frame_end
    s.eevee.use_raytracing = True
    s.eevee.ray_tracing_options.resolution_scale = "1"
    s.eevee.use_fast_gi = True
    s.render.dither_intensity = 1.0
    if MODEL:
        pivot, meshes = import_model(MODEL, size=2.0)
    else:
        hero = (import_svg_logo(SVG, depth=0.25, target_width=2.0, bevel=0.02) if SVG
                else text_3d(TEXT, size=1.0, extrude=0.2, bevel=0.03, font=font_path()))
        if SVG and MATERIAL == "svg":
            brand_materials(hero)
        else:
            hero.data.materials.clear(); hero.data.materials.append(hero_material())
        bpy.context.view_layer.update()
        pivot, meshes = _pivot_for([hero], [hero], 2.0)
        # text/logo pe un SOCLU rotund fix: altfel cuvântul lat plutește peste o umbră ovală mare.
        # Soclul nu se rotește (bucla rămâne perfectă), textul se rotește pe el.
        bpy.context.view_layer.update()
        hp = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
        rr = max(math.hypot(p.x, p.y) for p in hp) * 1.08; th = 0.08
        bpy.ops.mesh.primitive_cylinder_add(vertices=128, radius=rr, depth=th, location=(0, 0, th / 2))
        ped = bpy.context.object; ped.name = "Pedestal"
        bev = ped.modifiers.new("Bevel", "BEVEL"); bev.width = 0.015; bev.segments = 3
        bpy.ops.object.shade_smooth()
        ped.data.materials.append(material("PedestalMat", (0.92, 0.92, 0.92, 1) if STUDIO == "light"
                                           else (0.01, 0.01, 0.012, 1), roughness=0.25 if STUDIO == "light" else 0.08))
        pivot.location.z = th   # textul stă pe soclu
        meshes = meshes + [ped]  # încadrare + umbră de contact țin cont de soclu
        bpy.context.view_layer.update()
    product_studio(pivot, meshes, shadow_reach=1.1 if MODEL else 0.3)
    # volumul măturat de rotație (cilindru), ca încadrarea să țină pe toată tura, nu doar la cadrul 1
    # din VÂRFURI, nu din colțurile bbox-ului (la obiecte rotunde colțurile dau ~1,41x raza reală -> produs mic)
    pts = []
    for o in meshes:
        if o.type == "MESH" and len(o.data.vertices):
            pts += _sample_pts(o, 4000)
        else:   # text (curbă): colțurile bbox-ului
            pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    r = max(math.hypot(p.x, p.y) for p in pts); h = max(p.z for p in pts)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, h / 2)); sweep = bpy.context.object
    sweep.name = "SweptVolume"; sweep.scale = (2 * r, 2 * r, h); sweep.hide_render = True; sweep.display_type = "WIRE"
    bpy.context.view_layer.update()
    bpy.ops.object.camera_add(); cam = bpy.context.object; s.camera = cam; cam.data.lens = 70
    portrait = s.render.resolution_y > s.render.resolution_x
    square = abs(s.render.resolution_x - s.render.resolution_y) < 2
    fill = TT_FILL or (0.74 if portrait else 0.72 if square else 0.65)  # calibrat pe randare (sticlă, 2026-11-21)
    tgt = fit_camera(cam, sweep, fill=fill, height=0.32, frame=1)
    cam.data.dof.use_dof = True; cam.data.dof.focus_object = tgt; cam.data.dof.aperture_fstop = 5.6
    product_lights(cam, meshes, h)
    turntable(pivot, end, axis=2, turns=TURNS)
    EVENTS["loop"] = True   # finalize.py face --loop-check singur; sound.py pune doar dronă fără fade
    if STUDIO == "light":   # pe alb: fără vignetă (pătează albul) și fără bloom (fundalul trece pragul și spală produsul)
        compositor(bloom=0.0, vignette=0.0)
    else:
        compositor(bloom=0.12, vignette=0.35)
    return meshes


def _sample_pts(o, n=1500):
    """Puncte în spațiul lumii dintr-un mesh (vârfuri eșantionate); curbe/text: colțurile bbox-ului."""
    if o.type == "MESH" and len(o.data.vertices):
        vs = o.data.vertices   # bpy_prop_collection NU acceptă slice cu pas ([::k] -> TypeError la modele mari)
        return [o.matrix_world @ vs[i].co for i in range(0, len(vs), max(1, len(vs) // n))]
    return [o.matrix_world @ Vector(c) for c in o.bound_box]


def _bbox(pts):
    return (Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)]))


def split_parts(meshes, keep=0.12, max_parts=24):
    """Model dintr-o singură bucată -> piese libere (separate LOOSE); fragmentele mici (șuruburi, cusături) se lipesc
    de cea mai apropiată piesă mare (diagonala >= keep x diagonala modelului). Întoarce piesele rămase."""
    parts = []
    for o in meshes:
        bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
        parts += [x for x in bpy.context.selected_objects if x.type == "MESH"]
    bpy.context.view_layer.update()
    info = {o: _bbox([o.matrix_world @ Vector(c) for c in o.bound_box]) for o in parts}
    mn, mx = _bbox([p for b in info.values() for p in b]); diag = (mx - mn).length
    size = {o: (b[1] - b[0]).length for o, b in info.items()}
    seeds = sorted([o for o in parts if size[o] >= keep * diag], key=lambda o: -size[o])[:max_parts]
    if len(seeds) < 2:
        return seeds or parts[:1]
    groups = {sd: [] for sd in seeds}
    for o in parts:
        if o in groups:
            continue
        c = (info[o][0] + info[o][1]) / 2
        sd = min(seeds, key=lambda q: ((info[q][0] + info[q][1]) / 2 - c).length)
        groups[sd].append(o)
    for sd, rest in groups.items():
        if rest:
            bpy.ops.object.select_all(action="DESELECT")
            for o in rest:
                o.select_set(True)
            sd.select_set(True); bpy.context.view_layer.objects.active = sd
            bpy.ops.object.join()
    return seeds


def build_exploded(a):
    """Vedere explodată: produsul (MODEL cu >= 2 piese) stă asamblat, piesele se depărtează decalat (cea mai mare
    rămâne pe loc ca ancoră), țin poza explodată, apoi se reasamblează (EXPLODE_BACK). Rotație lentă ±18°."""
    s = bpy.context.scene; end = s.frame_end; fps = s.render.fps
    s.eevee.use_raytracing = True; s.eevee.ray_tracing_options.resolution_scale = "1"; s.eevee.use_fast_gi = True
    s.render.dither_intensity = 1.0
    if not MODEL:
        raise ValueError("MODE = exploded cere MODEL: cale .glb/.gltf/.obj/.fbx/.stl, ideal cu piese separate")
    pivot, meshes = import_model(MODEL, size=2.0)
    bpy.context.view_layer.update()
    for o in meshes:   # fiecare piesă direct pe pivot: altfel o piesă-părinte își târăște copiii după ea
        mw = o.matrix_world.copy(); o.parent = pivot; o.matrix_world = mw
    bpy.context.view_layer.update()
    if len(meshes) < 2:
        meshes = split_parts(meshes)
    if len(meshes) < 2:
        raise RuntimeError("modelul e o singură piesă (nici separarea pe bucăți libere n-a dat 2 piese mari): "
                           "vederea explodată cere un model cu piese separate")
    bpy.context.view_layer.update()
    P = {o: _sample_pts(o) for o in meshes}
    mn, mx = _bbox([p for v in P.values() for p in v]); L = (mx - mn).length
    box = {o: _bbox(P[o]) for o in meshes}
    cen = {o: (box[o][0] + box[o][1]) / 2 for o in meshes}
    ext = {o: box[o][1] - box[o][0] for o in meshes}
    vol = {o: max(ext[o].x, 1e-4) * max(ext[o].y, 1e-4) * max(ext[o].z, 1e-4) for o in meshes}
    if EXPLODE_AXIS == "z":   # teardown pe verticală: piesa de jos rămâne pe podea, restul urcă în ordinea înălțimii
        order = sorted(meshes, key=lambda o: (box[o][0].z, cen[o].z))
        anchor = order[0]
        gap = min(0.3 * L, 1.2 * L / max(1, len(order) - 1)) * EXPLODE
        off = {o: Vector((0, 0, gap * i)) for i, o in enumerate(order)}
        movers = order[1:]
    else:   # radial: cea mai mare piesă e ancora; celelalte pleacă dinspre centrul ei
        anchor = max(meshes, key=lambda o: vol[o])
        off = {anchor: Vector((0, 0, 0))}
        movers = [o for o in meshes if o is not anchor]
        c0 = cen[anchor]
        dirs = {}
        for o in movers:
            d = cen[o] - c0
            dirs[o] = (d.normalized() if d.length > 0.05 * L else Vector((0, 0, 1)), d.length)
        for o in movers:
            # piese pe aceeași direcție (ex. capac, grilă, inel stivuite): fiecare cu un pas în plus, altfel se suprapun
            rank = sum(1 for q in movers if q is not o and dirs[q][0].dot(dirs[o][0]) > 0.9 and dirs[q][1] < dirs[o][1])
            off[o] = dirs[o][0] * (0.22 * L + 0.7 * dirs[o][1] + 0.16 * L * rank) * EXPLODE
    for o in movers:   # nicio piesă sub podea
        low = box[o][0].z + off[o].z
        if low < 0:
            off[o].z -= low
    # ordinea: cele care pleacă mai departe pornesc primele; la reasamblare invers
    movers.sort(key=lambda o: -off[o].length)
    n = len(movers)
    A, B = max(2, round(0.12 * end)), round(0.42 * end)
    C, D = round(0.55 * end), round(0.78 * end)
    st = max(1, min(4, (B - A) // (2 * n)))
    dur = max(6, (B - A) - st * (n - 1))
    dur_back = max(6, (D - C) - st * (n - 1))
    for i, o in enumerate(movers):
        dl = (pivot.matrix_world @ o.matrix_parent_inverse).to_3x3().inverted() @ off[o]
        base = o.location.copy(); f0 = A + i * st
        key(o, "location", base, f0, "QUART", "EASE_IN_OUT"); key(o, "location", base + dl, f0 + dur)
        if EXPLODE_BACK:
            g0 = C + (n - 1 - i) * st
            key(o, "location", base + dl, g0, "QUART", "EASE_IN_OUT"); key(o, "location", base, g0 + dur_back)
    rot = math.radians(18)
    key(pivot, "rotation_euler", (0, 0, -rot), 1, "LINEAR"); key(pivot, "rotation_euler", (0, 0, rot), end, "LINEAR")
    # volumul ocupat (asamblat + explodat, la -18°/0°/+18°) pentru încadrare
    allp = []
    for o in meshes:
        for p in P[o][::3]:
            for q in (p, p + off[o]):
                for ang in (-rot, 0.0, rot):
                    c, sn = math.cos(ang), math.sin(ang)
                    allp.append(Vector((q.x * c - q.y * sn, q.x * sn + q.y * c, q.z)))
    asm = []   # doar asamblat (pentru cadrajul strâns de la început și final)
    for o in meshes:
        for p in P[o][::3]:
            for ang in (-rot, 0.0, rot):
                c, sn = math.cos(ang), math.sin(ang)
                asm.append(Vector((p.x * c - p.y * sn, p.x * sn + p.y * c, p.z)))

    def vol_box(name, pts):
        a0, a1 = _bbox(pts)
        bpy.ops.mesh.primitive_cube_add(size=1, location=(a0 + a1) / 2); vb = bpy.context.object
        vb.name = name; vb.scale = a1 - a0; vb.hide_render = True; vb.display_type = "WIRE"
        return vb
    vmn, vmx = _bbox(allp); h = vmx.z
    vbE = vol_box("ExplodedVolume", allp); vbA = vol_box("AssembledVolume", asm)
    bpy.context.view_layer.update()
    bpy.ops.object.camera_add(); cam = bpy.context.object; s.camera = cam; cam.data.lens = 60
    portrait = s.render.resolution_y > s.render.resolution_x
    square = abs(s.render.resolution_x - s.render.resolution_y) < 2
    fill = TT_FILL or (0.8 if portrait else 0.78 if square else 0.72)
    # camera se retrage odată cu explozia și revine la reasamblare: produsul e mare și asamblat, și explodat
    tE = fit_camera(cam, vbE, fill=fill, height=0.45, frame=1)   # mai de sus: discurile/capacele se văd, nu doar din profil
    camE, tgtE = cam.location.copy(), tE.location.copy()
    cam.constraints.remove(cam.constraints[-1]); bpy.data.objects.remove(tE)
    tgt = fit_camera(cam, vbA, fill=fill * 0.75, height=0.45, frame=1)
    camA, tgtA = cam.location.copy(), tgt.location.copy()
    lead = max(2, st)   # camera pornește puțin înaintea pieselor
    for ob, va, ve in ((cam, camA, camE), (tgt, tgtA, tgtE)):
        key(ob, "location", va, max(1, A - lead), "QUART", "EASE_IN_OUT")
        key(ob, "location", ve, A + dur)   # = sosirea piesei celei mai depărtate (pornește prima)
        if EXPLODE_BACK:   # se apropie abia când pleacă spre casă piesa cea mai depărtată (ultima), puțin mai lent
            key(ob, "location", ve, C + (n - 1) * st, "QUART", "EASE_IN_OUT")
            key(ob, "location", va, C + (n - 1) * st + dur_back + lead)
    # zoom lent continuu (lens 58 -> 62, ±3% față de cadraj): în poze ținute ceva se mișcă mereu, chiar și la un
    # produs simetric (cilindru) pe care rotația nu se vede; finalize.py ar raporta altfel „imagine înghețată”
    cam.data.lens = 58; cam.data.keyframe_insert("lens", frame=1)
    cam.data.lens = 62; cam.data.keyframe_insert("lens", frame=end)
    for fc in _fcurves(cam.data):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
    cam.data.dof.use_dof = True; cam.data.dof.focus_object = tgt; cam.data.dof.aperture_fstop = 8.0
    s.frame_set(1)
    product_studio(pivot, meshes, shadow_reach=0.6)
    product_lights(cam, meshes, h)
    last = A + (n - 1) * st + dur
    EVENTS.update(intro=A / fps, impact=last / fps)
    if EXPLODE_BACK:
        back = C + (n - 1) * st + dur_back
        EVENTS.update(sweep=back / fps, settled=back / fps)
    else:
        EVENTS["settled"] = last / fps
    compositor(bloom=0.0 if STUDIO == "light" else 0.12, vignette=0.0 if STUDIO == "light" else 0.35)
    return meshes


FRAMING_SKIP = []   # (cadru_start, cadru_final) excluse din framing_report (macro intenționat)
COUNTERS = []   # (obiect text, cadru start, cadru final, spec) - corpul textului se schimbă pe cadru (handler)


def num_spec(txt):
    """„1.250+” -> prefix, valoare, zecimale, separatori, sufix. Stil românesc: punct = mii, virgulă = zecimale.
    Un punct urmat de alt număr de cifre decât 3 e tratat ca zecimală (stil englezesc), păstrat la afișare.
    Fără cifre -> None (text static)."""
    m = re.match(r"^(\D*?)(\d[\d.,]*\d|\d)(\D*)$", txt.strip())
    if not m:
        return None
    pre, num, suf = m.groups()
    dec, dsep, group = 0, ",", False
    if "," in num:
        ip, dp = num.split(",", 1); dec = len(dp); group = "." in ip; ip = ip.replace(".", "")
        val = float(ip + "." + dp)
    elif "." in num:
        parts = num.split(".")
        if all(len(x) == 3 for x in parts[1:]):
            group = True; val = float("".join(parts))
        else:
            dsep = "."; dec = len(parts[-1]); val = float(num)
    else:
        val = float(num)
    return {"pre": pre, "val": val, "dec": dec, "dsep": dsep, "group": group, "suf": suf, "final": txt.strip()}


def num_text(sp, p):
    if p >= 1.0:
        return sp["final"]
    v = sp["val"] * p
    ip, _, dp = f"{v:.{sp['dec']}f}".partition(".")
    if sp["group"]:
        ip = f"{int(ip):,}".replace(",", ".")
    return sp["pre"] + ip + (sp["dsep"] + dp if sp["dec"] else "") + sp["suf"]


def _count_handler(scene, depsgraph=None):
    f = scene.frame_current
    for t, f0, f1, sp in COUNTERS:
        p = min(1.0, max(0.0, (f - f0) / max(1, f1 - f0)))
        body = num_text(sp, 1 - (1 - p) ** 4)   # QUART ease-out: urcă repede, încetinește spre valoare
        if t.data.body != body:
            t.data.body = body


def _wrap2(txt, limit=18):
    if len(txt) <= limit or " " not in txt or "\n" in txt:
        return txt
    w = txt.split()
    cut = min(range(1, len(w)), key=lambda i: abs(len(" ".join(w[:i])) - len(" ".join(w[i:]))))
    return " ".join(w[:cut]) + "\n" + " ".join(w[cut:])


def build_stats(a):
    """Infografic: 1-4 cifre-cheie (STATS) care numără de la 0 la valoare, cu bară de accent și etichetă.
    Orizontal pe 16:9/1:1, una sub alta pe 9:16. Aceeași scenă premium ca reveal-ul (podea neagră lucioasă,
    HDRI ascuns, softbox pe cameră, contur cald/rece pe cifre, sweep la final, praf, bloom)."""
    s = bpy.context.scene; end = s.frame_end; fps = s.render.fps
    if not STATS or len(STATS) > 4:
        raise ValueError("STATS trebuie să aibă 1-4 perechi (valoare, etichetă)")
    s.eevee.use_raytracing = True; s.eevee.ray_tracing_options.resolution_scale = "1"; s.eevee.use_fast_gi = True
    s.view_settings.look = "AgX - High Contrast"; s.render.dither_intensity = 1.0
    studio_world(ensure_hdri(HDRI_ID), strength=1.2)
    if not ALPHA:
        backdrop()
    portrait = s.render.resolution_y > s.render.resolution_x
    nm_mat = hero_material()
    lb_mat = material("Label", (0.8, 0.8, 0.83, 1), roughness=0.45, emission=(1, 1, 1, 1), strength=0.35)
    acc = COLOR or (1.0, 0.55, 0.15, 1)
    bar_mat = material("Accent", acc, roughness=0.3, emission=acc, strength=4.0)
    items = []
    for val, label in STATS:
        n = text_3d(str(val), size=1.0, extrude=0.16, bevel=0.03, font=font_path()); n.data.bevel_resolution = 4
        n.data.materials.append(nm_mat)
        lb = text_3d(_wrap2(label), size=0.34, extrude=0.02, bevel=0.004, font=font_path(FONT_LABEL))
        lb.data.materials.append(lb_mat)
        items.append({"n": n, "l": lb, "sp": num_spec(str(val))})
    bpy.context.view_layer.update()
    # cea mai lată valoare finală dictează mărimea (toate cifrele au același corp de literă)
    nw = max(it["n"].dimensions.x for it in items); lw = max(it["l"].dimensions.x for it in items)
    nh = items[0]["n"].dimensions.y; lh = max(it["l"].dimensions.y for it in items)
    cw = max(nw, lw) * 1.12
    gap_v = 0.18 * nh   # număr -> bară -> etichetă
    block_h = nh + gap_v + 0.05 + gap_v + lh
    if portrait:
        W, H = cw, len(items) * block_h + (len(items) - 1) * 0.35 * nh
    else:
        W, H = len(items) * cw + (len(items) - 1) * 0.25 * nw, block_h
    u = 4.4 / max(W, H * 1.6, 1e-3)   # aceeași „amprentă” ca reveal-ul: luminile/sweep-ul sunt calibrate pe ea
    for it in items:
        for o in (it["n"], it["l"]):
            o.data.size *= u; o.data.extrude *= u; o.data.bevel_depth *= u
    cw, nh, lh, gap_v, block_h = cw * u, nh * u, lh * u, gap_v * u, block_h * u
    base = 0.08 * u
    bars = []
    for i, it in enumerate(items):
        if portrait:
            x = 0.0
            z0 = base + (len(items) - 1 - i) * (block_h + 0.35 * nh)
        else:
            x = (i - (len(items) - 1) / 2) * (cw + 0.25 * nw * u)
            z0 = base
        it["l"].location = (x, 0, z0 + lh / 2)
        zb = z0 + lh + gap_v
        bpy.ops.mesh.primitive_cube_add(size=1, location=(x, 0, zb))
        b = bpy.context.object; b.name = f"Bar{i}"; b.scale = (cw * 0.45, 0.06 * u, 0.045 * u)
        b.data.materials.append(bar_mat); bars.append(b)
        it["n"].location = (x, 0, zb + gap_v + nh / 2)
        it["b"] = b
    bpy.context.view_layer.update()
    # align_y CENTER e pe linia fontului, nu pe conturul real: așez cifra și eticheta după bbox-ul măsurat
    for it in items:
        zb = it["b"].location.z; hb = it["b"].scale.z / 2
        nz0 = min((it["n"].matrix_world @ Vector(c)).z for c in it["n"].bound_box)
        it["n"].location.z += (zb + hb + gap_v) - nz0
        lz1 = max((it["l"].matrix_world @ Vector(c)).z for c in it["l"].bound_box)
        it["l"].location.z += (zb - hb - 0.7 * gap_v) - lz1
    bpy.context.view_layer.update()
    framed = [o for it in items for o in (it["n"], it["l"], it["b"])]
    bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, 0))
    fl = bpy.context.object
    fl.data.materials.append(material("Floor", (0.004, 0.004, 0.005, 1), roughness=0.12, specular=0.35))
    if ALPHA:
        fl.is_holdout = True
    bpy.ops.object.camera_add(); cam = bpy.context.object; s.camera = cam; cam.data.lens = 50
    # 9:16: fill pe ÎNĂLȚIME 0,5 + conținutul spre stânga-sus, în zona sigură Reels/TikTok (ca la reveal)
    tgt = fit_camera(cam, framed, fill=0.46 if portrait else 0.84, height=0.12, frame=1)   # corpurile sunt încă finale
    if portrait:
        vis = 2 * (cam.location - tgt.location).length * math.tan(cam.data.angle_x / 2)
        dx, dz = 0.035 * vis, 0.10 * vis * s.render.resolution_y / s.render.resolution_x
        cam.location.x += dx; tgt.location.x += dx; cam.location.z -= dz; tgt.location.z -= dz
    c = tgt.location.copy(); v = cam.location - c
    key(cam, "location", tuple(c + v * 1.06), 1, "LINEAR"); key(cam, "location", tuple(c + v), end, "LINEAR")
    # animație: fiecare cifră intră (scale BACK), numără 1,6 s; bara crește, eticheta urcă după
    f_first = max(2, int(fps * 0.12)); st = int(fps * 0.35); cnt = int(fps * 1.6)   # start aproape imediat: Reels
    last = 0
    for i, it in enumerate(items):
        f0 = f_first + i * st
        n, lb, b = it["n"], it["l"], it["b"]
        key(n, "scale", (0.0001,) * 3, f0, "BACK", "EASE_OUT"); key(n, "scale", (1, 1, 1), f0 + int(fps * 0.6))
        p = n.location.copy()
        key(n, "location", tuple(p - Vector((0, 0, 0.15 * u))), f0, "EXPO", "EASE_OUT"); key(n, "location", tuple(p), f0 + int(fps * 0.8))
        bs = tuple(b.scale)   # citit ÎNAINTE de prima cheie (care setează scale la 0)
        key(b, "scale", (0.0001, bs[1], bs[2]), f0 + int(fps * 0.2), "EXPO", "EASE_OUT")
        key(b, "scale", bs, f0 + int(fps * 1.0))
        q = lb.location.copy()
        key(lb, "scale", (0.0001,) * 3, f0 + int(fps * 0.5), "EXPO", "EASE_OUT"); key(lb, "scale", (1, 1, 1), f0 + int(fps * 1.1))
        key(lb, "location", tuple(q - Vector((0, 0, 0.08 * u))), f0 + int(fps * 0.5), "EXPO", "EASE_OUT")
        key(lb, "location", tuple(q), f0 + int(fps * 1.1))
        if it["sp"]:
            COUNTERS.append((n, f0, f0 + cnt, it["sp"]))
        last = max(last, f0 + cnt)
    if COUNTERS and _count_handler not in bpy.app.handlers.frame_change_pre:
        bpy.app.handlers.frame_change_pre.append(_count_handler)
    # lumini: softbox pe cameră, bandă sus, contur cald/rece DOAR pe cifre (light linking), sweep la final
    card = area("Card", (0, 0, 0), CARD_W if CARD_W is not None else {"plastic": 220, "neon": 0, "crom": 40}.get(MATERIAL, 900),
                (1, .95, .88), shape="RECTANGLE", sx=40, sy=4 * len(items) if portrait else 5)
    # 9:16 (cifre una sub alta): softbox-ul trebuie să acopere toate rândurile în reflexie, altfel cifra de sus iese
    # neagră cu contur auriu (test agent 2026-11-22: sy=12 pentru 3 rânduri = auriu uniform)
    card.data.use_shadow = False; card.parent = cam; card.location = (0, 0, 6); card.rotation_euler = (0, 0, 0)
    top = area("TopStrip", (0, -1.5, 5 + c.z), 700, (1, .97, .9), shape="RECTANGLE", sx=8, sy=0.4, target=tuple(c))
    top.data.use_shadow = False
    rcv = bpy.data.collections.new("RimReceivers")
    for it in items:
        rcv.objects.link(it["n"])
    for nm, loc, col in (("RimL", (-4, 4, 5 + c.z), (0.35, 0.55, 1.0)), ("RimR", (4, 4, 5 + c.z), (1.0, 0.55, 0.25))):
        r = area(nm, loc, 2000, col, size=1.5); r.light_linking.receiver_collection = rcv
    sw = area("Sweep", (-7, -2.5, c.z), SWEEP_W if SWEEP_W is not None else 450, (1, 1, 1), shape="RECTANGLE",
              sx=0.08, sy=3 + 2 * c.z, target=tuple(c))
    sw.light_linking.receiver_collection = rcv
    key(sw, "location", (-7, -2.5, c.z), last - int(fps * 0.2), "SINE", "EASE_IN_OUT")
    key(sw, "location", (7, -2.5, c.z), last + int(fps * 0.5), "SINE", "EASE_IN_OUT")
    if DUST and not ALPHA:
        dust(box=((-5, 5), (-9, 2), (-0.3, 3.0 + 2 * c.z)))
    glare = compositor(bloom=0.2, vignette=0.35)
    impact_pulse(glare, cam, last, bloom=0.2, peak=0.35, lens_gain=0.0)
    EVENTS.update(intro=f_first / fps, impact=last / fps, sweep=(last + int(fps * 0.1)) / fps, settled=last / fps)
    if OUTRO == "fade":
        EVENTS["outro"] = outro([], "fade", end)
    s.frame_set(1)
    return framed


def build_kinetic(a):
    """Tipografie cinetică 3D: 2-4 fraze (PHRASES) una după alta, ex. cârlig -> beneficiu -> îndemn (CTA).
    Fiecare frază intră pe litere (stil diferit pe rând: KINETIC_STYLES), stă cât e nevoie s-o citești
    (~15 caractere/s, min. 0,8 s), pleacă (exit), apoi intră următoarea. Ultima rămâne, cu sweep + puls de bloom.
    Scena premium a reveal-ului (podea neagră lucioasă, HDRI ascuns, contur cald/rece, praf)."""
    s = bpy.context.scene; end = s.frame_end; fps = s.render.fps
    phrases = [p.strip() for p in PHRASES if p.strip()]
    if not 1 <= len(phrases) <= 5:
        raise ValueError("PHRASES trebuie să aibă 1-5 fraze nevide")
    s.eevee.use_raytracing = True; s.eevee.ray_tracing_options.resolution_scale = "1"; s.eevee.use_fast_gi = True
    s.view_settings.look = "AgX - High Contrast"; s.render.dither_intensity = 1.0
    studio_world(ensure_hdri(HDRI_ID), strength=1.2)
    if not ALPHA:
        backdrop("glow" if MATERIAL == "sticla" else None)
    portrait = s.render.resolution_y > s.render.resolution_x
    limit = 12 if portrait else 18
    mat = hero_material()
    groups = []
    for i, txt in enumerate(phrases):
        body = _wrap2(txt, limit)
        h = text_3d(body, size=1.0, extrude=0.16, bevel=0.03, font=font_path()); h.data.bevel_resolution = 4
        h.data.materials.append(mat)
        bpy.context.view_layer.update()
        unit = 4.4 / max(h.dimensions.x, h.dimensions.y * 2.2, 1e-3)
        h.data.size *= unit; h.data.extrude *= unit; h.data.bevel_depth *= unit
        bpy.context.view_layer.update()
        z0 = min((h.matrix_world @ Vector(c)).z for c in h.bound_box)
        h.location.z += 0.05 - z0   # toate frazele stau pe podea (z = 0)
        bpy.context.view_layer.update()
        groups.append({"hero": h, "txt": txt, "unit": unit, "h": h.dimensions.y})
    bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, 0))
    fl = bpy.context.object
    fl.data.materials.append(material("Floor", (0.004, 0.004, 0.005, 1), roughness=0.12, specular=0.35))
    if ALPHA:
        fl.is_holdout = True
    bpy.ops.object.camera_add(); cam = bpy.context.object; s.camera = cam; cam.data.lens = 85
    tgt = fit_camera(cam, [g["hero"] for g in groups], fill=0.62 if portrait else 0.6, height=0.10, frame=1)
    if portrait:   # zona sigură Reels/TikTok: conținutul spre stânga-sus (ca la reveal)
        vis = 2 * (cam.location - tgt.location).length * math.tan(cam.data.angle_x / 2)
        dx, dz = 0.035 * vis, 0.10 * vis * s.render.resolution_y / s.render.resolution_x
        cam.location.x += dx; tgt.location.x += dx; cam.location.z -= dz; tgt.location.z -= dz
    c = tgt.location.copy(); v = cam.location - c
    # camera e încadrată pe fraza cea mai înaltă; la fiecare frază coboară/urcă lin pe centrul ei (frazele stau toate
    # pe podea: ridicate ar pluti departe de reflexie). Plus dolly lent continuu.
    hmax = max(g["h"] for g in groups)
    dzs = [-(hmax - g["h"]) / 2 for g in groups]
    key(cam, "location", tuple(c + v * 1.07), 1, "LINEAR"); key(cam, "location", tuple(c + v), end, "LINEAR")
    # cronologie
    styles = list(KINETIC_STYLES) or ["rise"]
    t = max(2, int(fps * 0.15)); all_letters = []; plan = []
    for i, g in enumerate(groups):
        letters = split_letters(g["hero"]); g["letters"] = letters; start_i = t
        stagger = max(1, min(round(fps * 0.05), round(fps * 0.3 / max(1, len(letters) - 1))))
        settled = stagger_reveal(letters, start=t, stagger=stagger, dur=int(fps * 0.6), rise=0.8 * g["unit"],
                                 style=styles[i % len(styles)])
        hold = max(0.7, len(g["txt"]) / 18.0 + 0.15)   # ~18 caractere/s: citit, nu silabisit
        last = i == len(groups) - 1
        if not last:
            ex_end = settled + int(fps * hold) + int(fps * 0.4)
            outro(letters, "exit", ex_end, g["unit"], dur_s=0.4)
            t = ex_end - int(fps * 0.15)   # următoarea frază intră cât pleacă ultimele litere (fără gol negru)
        plan.append((start_i, settled))
        all_letters += letters
    settled_last = plan[-1][1]
    for i, (f_in, _) in enumerate(plan):   # ținta camerei pe fraza curentă (camera o urmează prin Track To)
        tz = c.z + dzs[i]
        if i == 0:
            key(tgt, "location", (c.x, c.y, tz), 1, "SINE", "EASE_IN_OUT")
        else:
            key(tgt, "location", (c.x, c.y, c.z + dzs[i - 1]), max(2, f_in - int(fps * 0.25)), "SINE", "EASE_IN_OUT")
            key(tgt, "location", (c.x, c.y, tz), f_in + int(fps * 0.35), "SINE", "EASE_IN_OUT")
    need = settled_last / fps + max(1.0, len(groups[-1]["txt"]) / 15.0 + 0.5)
    if need > end / fps + 0.05:
        print("ANIM3D_WARN", json.dumps({"problem": f"frazele nu încap în SECONDS = {end / fps:.1f}: ultima se așază la "
                                         f"{settled_last / fps:.2f}s; pune SECONDS = {math.ceil(need * 2) / 2:.1f}"},
                                        ensure_ascii=False))
    # lumini (ca la reveal): softbox pe cameră, bandă sus, contur DOAR pe litere (light linking), sweep la final
    card_w = CARD_W if CARD_W is not None else {"plastic": 220, "svg": 220, "neon": 0, "crom": 40}.get(MATERIAL, 900)
    card = area("Card", (0, 0, 0), card_w, (1, .95, .88), shape="RECTANGLE", sx=40, sy=5)
    card.data.use_shadow = False; card.parent = cam; card.location = (0, 0, 6); card.rotation_euler = (0, 0, 0)
    top = area("TopStrip", (0, -1.5, 5 + c.z), 700, (1, .97, .9), shape="RECTANGLE", sx=8, sy=0.4, target=tuple(c))
    top.data.use_shadow = False
    rcv = bpy.data.collections.new("RimReceivers")
    for L in all_letters:
        rcv.objects.link(L)
    for nm, loc, col in (("RimL", (-4, 4, 5 + c.z), (0.35, 0.55, 1.0)), ("RimR", (4, 4, 5 + c.z), (1.0, 0.55, 0.25))):
        r = area(nm, loc, 2000, col, size=1.5); r.light_linking.receiver_collection = rcv
    sw = area("Sweep", (-7, -2.5, c.z), SWEEP_W if SWEEP_W is not None else 450, (1, 1, 1), shape="RECTANGLE",
              sx=0.08, sy=3 + 2 * c.z, target=tuple(c))
    sw.light_linking.receiver_collection = rcv
    impact_f = settled_last - int(fps * 0.6 * 0.3)
    key(sw, "location", (-7, -2.5, c.z), impact_f - int(fps * 0.3), "SINE", "EASE_IN_OUT")
    key(sw, "location", (7, -2.5, c.z), impact_f + int(fps * 0.3), "SINE", "EASE_IN_OUT")
    if DUST and not ALPHA:
        dust(box=((-5, 5), (-9, 2), (-0.3, 3.0 + 2 * c.z)))
    glare = compositor(bloom=0.2, vignette=0.35)
    impact_pulse(glare, cam, impact_f, bloom=0.2, peak=0.4, lens_gain=0.02)
    EVENTS.update(intro=plan[0][0] / fps, impact=impact_f / fps, sweep=(impact_f - int(fps * 0.1)) / fps,
                  settled=settled_last / fps)
    if len(groups) > 1:
        EVENTS["subtitle"] = plan[0][1] / fps   # „tick” la prima frază așezată
    if OUTRO == "fade":
        EVENTS["outro"] = outro([], "fade", end)
    elif OUTRO == "exit":
        EVENTS["outro"] = outro(groups[-1]["letters"], "exit", end, groups[-1]["unit"])
    s.frame_set(1)
    return all_letters


# ---------- produse procedurale pentru MODE = commercial (sticlă cu lichid, borcan, doză, cutie) ----------
import bmesh  # noqa: E402

_AMBER = (1.0, 0.55, 0.15, 1.0)
_SEG = 72


# ---------------------------------------------------------------- materiale
def _principled(name, base, metallic=0.0, rough=0.5, transmission=0.0, ior=1.45, coat=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = tuple(base)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["IOR"].default_value = ior
    bsdf.inputs["Transmission Weight"].default_value = transmission
    if coat:
        bsdf.inputs["Coat Weight"].default_value = coat
        bsdf.inputs["Coat Roughness"].default_value = 0.05
    if transmission > 0:
        try:
            m.use_raytrace_refraction = True
        except Exception:
            pass
        try:
            m.surface_render_method = "DITHERED"
        except Exception:
            pass
    return m


def _glass(name, tint=(0.92, 0.97, 0.95, 1.0)):
    m = _principled(name, tint, 0.0, 0.02, 1.0, 1.47)
    # EEVEE refracteaza ecranul doar pana la prima suprafata: Alpha < 1 (dithered) lasa sa se vada
    # continutul (lichid/crema) prin sticla, pastrand reflexiile si refractia Principled.
    m.node_tree.nodes["Principled BSDF"].inputs["Alpha"].default_value = 0.3
    return m


def _glass_inner(name):
    """Perete interior al sticlei: EEVEE refracteaza doar prima interfata, deci suprafata interioara
    trebuie sa lase privirea sa treaca spre lichid (transparent + reflexie Fresnel slaba)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    mix = nt.nodes.new("ShaderNodeMixShader")
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    gl = nt.nodes.new("ShaderNodeBsdfGlossy")
    gl.inputs["Roughness"].default_value = 0.02
    fr = nt.nodes.new("ShaderNodeFresnel")
    fr.inputs["IOR"].default_value = 1.47
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = 0.6
    nt.links.new(fr.outputs["Fac"], mul.inputs[0])
    nt.links.new(mul.outputs["Value"], mix.inputs["Fac"])
    nt.links.new(tr.outputs["BSDF"], mix.inputs[1])
    nt.links.new(gl.outputs["BSDF"], mix.inputs[2])
    nt.links.new(mix.outputs["Shader"], out.inputs["Surface"])
    try:
        m.surface_render_method = "DITHERED"
    except Exception:
        pass
    return m


def _liquid(name, color):
    # transmisie 0,25 + strălucire slabă: pe fundal închis un lichid foarte transparent devine negru (verificat
    # 2026-11-22 cu galben: sticla părea goală, doar gâtul auriu se vedea)
    if globals().get("STUDIO") == "light":
        # pe fundal ALB lichidul transparent dispare (vedea albul prin el): aproape opac, culoare saturată, fără emisie
        sat = tuple(c ** 1.6 for c in color[:3]) + (1,)
        return _principled(name, sat, 0.0, 0.04, 0.06, 1.33)
    m = _principled(name, color, 0.0, 0.04, 0.25, 1.33)
    b = m.node_tree.nodes.get("Principled BSDF")
    if b:
        b.inputs["Emission Color"].default_value = color; b.inputs["Emission Strength"].default_value = 0.25
    return m


# ---------------------------------------------------------------- geometrie
def _smooth_by_angle(bm, angle_deg=35.0):
    lim = math.radians(angle_deg)
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > lim:
            e.smooth = False
    for f in bm.faces:
        f.smooth = True


def _to_object(name, bm, mat, parent, smooth=True, inner_mat=None):
    if inner_mat is not None:
        for f in bm.faces:
            c = f.calc_center_median()
            rad = f.normal.x * c.x + f.normal.y * c.y
            if rad < -1e-4 or (f.normal.z > 0.5 and c.z < 0.5):
                f.material_index = 1
    if smooth:
        _smooth_by_angle(bm)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    if inner_mat is not None:
        me.materials.append(inner_mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    return ob


def _revolve(profile, closed_solid=False, angle=2 * math.pi, steps=_SEG, rot_z=0.0):
    """profile: lista (r, z). Intoarce bmesh rotit in jurul Z."""
    bm = bmesh.new()
    vs = [bm.verts.new((r, 0.0, z)) for r, z in profile]
    for a, b in zip(vs[:-1], vs[1:]):
        bm.edges.new((a, b))
    res = bmesh.ops.spin(bm, geom=bm.verts[:] + bm.edges[:], cent=(0, 0, 0),
                         axis=(0, 0, 1), angle=angle, steps=steps, use_duplicate=False)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    # sterge fete degenerate
    bad = [f for f in bm.faces if f.calc_area() < 1e-10]
    if bad:
        bmesh.ops.delete(bm, geom=bad, context="FACES_ONLY")
    if closed_solid:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    else:
        s = 0.0
        for f in bm.faces:
            c = f.calc_center_median()
            s += f.normal.dot(Vector((c.x, c.y, 0.0)))
        if s < 0:
            bmesh.ops.reverse_faces(bm, faces=bm.faces)
    if rot_z:
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0),
                         matrix=__import__("mathutils").Matrix.Rotation(rot_z, 3, "Z"))
    return bm


def _arc(cx, cz, rad, a0, a1, n=8):
    """puncte (r,z) pe un arc: unghi masurat de la +X spre +Z."""
    pts = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        pts.append((cx + rad * math.cos(a), cz + rad * math.sin(a)))
    return pts


def _smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def _rounded_cyl(r, z0, z1, rad):
    """profil cilindru plin cu margini rotunjite (de la axa jos pana la axa sus)."""
    p = [(0.0, z0)]
    p += _arc(r - rad, z0 + rad, rad, -90, 0)
    p += _arc(r - rad, z1 - rad, rad, 0, 90)
    p.append((0.0, z1))
    return p


def _sample(fn, z0, z1, n):
    return [(fn(z0 + (z1 - z0) * i / n), z0 + (z1 - z0) * i / n) for i in range(n + 1)]


# ---------------------------------------------------------------- produse
def _bottle(pivot, material, color):
    R, T = 0.42, 0.03
    z_sh0, z_sh1, z_top = 1.00, 1.45, 1.74
    r_neck = 0.13

    def rout(z):
        return R + (r_neck - R) * _smoothstep((z - z_sh0) / (z_sh1 - z_sh0))

    def rin(z):
        return rout(z) - T

    z_in0 = 0.14  # fund gros
    prof = [(0.0, 0.0)]
    prof += _arc(R - 0.07, 0.07, 0.07, -90, 0)[1:]
    prof += _sample(rout, 0.07, z_top, 70)[1:]
    prof += _sample(rin, z_top, z_in0 + 0.05, 70)
    prof += _arc(rin(z_in0 + 0.05) - 0.05, z_in0 + 0.05, 0.05, 0, -90)
    prof.append((0.0, z_in0))
    glass_like = material == "sticla"
    if glass_like:
        body_mat = _glass("Bottle_Glass")
    elif material == "metal":
        body_mat = _principled("Bottle_Metal", color or (0.85, 0.85, 0.88, 1), 1.0, 0.22)
    else:
        body_mat = _principled("Bottle_Plastic", color or (0.9, 0.9, 0.95, 1), 0.0, 0.25, coat=1.0)
    meshes = [_to_object("bottle_body", _revolve(prof, True), body_mat, pivot,
                         inner_mat=_glass_inner("Bottle_Glass_Inner") if glass_like else None)]

    if glass_like:
        zl = z_in0 + 0.85 * (z_sh1 - z_in0)
        lp = [(0.0, z_in0 + 0.02)]
        lp += [(rin(z_in0 + 0.05) - 0.012 - 0.0, z_in0 + 0.05)] if False else []
        lp += [(rin(z) - 0.012, z) for z in [z_in0 + 0.04 + (zl - z_in0 - 0.04) * i / 60 for i in range(61)]]
        lp.append((0.0, zl))
        meshes.append(_to_object("bottle_liquid", _revolve(lp, True), _liquid("Bottle_Liquid", color or _AMBER), pivot))

    cap_col = color or (0.85, 0.65, 0.2, 1.0)
    cap_mat = (_principled("Bottle_Cap_Metal", cap_col, 1.0, 0.25) if material != "plastic"
               else _principled("Bottle_Cap_Plastic", cap_col, 0.0, 0.3, coat=1.0))
    meshes.append(_to_object("bottle_cap", _revolve(_rounded_cyl(0.175, 1.72, 2.0, 0.03), True), cap_mat, pivot))

    lab_col = color or (0.93, 0.9, 0.82, 1.0)
    meshes.append(_label(pivot, R + 0.004, 0.30, 0.95, lab_col))
    return meshes


def _label(pivot, r, z0, z1, col, name="label"):
    t = 0.006
    prof = [(r, z0), (r + t, z0), (r + t, z1), (r, z1), (r, z0)]
    bm = _revolve(prof, False, angle=math.pi, steps=36, rot_z=-math.pi)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _to_object(name, bm, _principled("Label_Mat", col, 0.0, 0.7), pivot)
    # marcaj pentru brand_label: banda cilindrica pe jumatatea dinspre -Y (centru la -90 grade)
    ob["is_label"] = True
    ob["label_kind"] = "cyl"
    ob["label_r"] = r + t          # raza suprafetei exterioare
    ob["label_z0"] = z0
    ob["label_z1"] = z1
    ob["label_color"] = list(col)
    return ob


def _jar(pivot, material, color):
    R, T = 0.78, 0.05
    H = 0.95
    prof = [(0.0, 0.0)]
    prof += _arc(R - 0.12, 0.12, 0.12, -90, 0)[1:]
    prof += [(R, H - 0.05), (R - 0.02, H), (R - T, H), (R - T, H - 0.04)]
    prof += [(R - T, 0.17)]
    prof += _arc(R - T - 0.08, 0.17, 0.08, 0, -90)
    prof.append((0.0, 0.09))
    # fund gros
    glass_like = material == "sticla"
    if glass_like:
        mat = _glass("Jar_Glass")
    elif material == "metal":
        mat = _principled("Jar_Metal", color or (0.85, 0.85, 0.88, 1), 1.0, 0.25)
    else:
        mat = _principled("Jar_Plastic", color or (0.95, 0.95, 0.95, 1), 0.0, 0.25, coat=1.0)
    meshes = [_to_object("jar_body", _revolve(prof, True), mat, pivot,
                         inner_mat=_glass_inner("Jar_Glass_Inner") if glass_like else None)]

    if glass_like:
        cr = R - T - 0.012
        cream = [(0.0, 0.11), (cr - 0.06, 0.11)] + _arc(cr - 0.06, 0.17, 0.06, -90, 0)[1:] + [(cr, 0.80), (0.0, 0.80)]
        meshes.append(_to_object("jar_cream", _revolve(cream, True),
                                 _principled("Jar_Cream", (0.96, 0.9, 0.82, 1), 0.0, 0.35), pivot))

    lid_col = color or (0.85, 0.65, 0.2, 1.0)
    lid = _principled("Jar_Lid_Metal", lid_col, 1.0, 0.18)
    meshes.append(_to_object("jar_lid", _revolve(_rounded_cyl(R + 0.01, H - 0.02, 1.25, 0.05), True), lid, pivot))
    meshes.append(_label(pivot, R + 0.004, 0.22, 0.70, color or (0.93, 0.9, 0.82, 1.0), "jar_label"))
    return meshes


def _can(pivot, material, color):
    R = 0.42
    col = color or (0.8, 0.1, 0.1, 1.0)
    zb, zs0, zs1, zt = 0.14, 1.45, 1.66, 1.70
    r_neck = 0.335

    def rb(z):
        if z < zb:
            return 0.34 + (R - 0.34) * _smoothstep(z / zb)
        if z > zs0:
            return R + (r_neck - R) * _smoothstep((z - zs0) / (zs1 - zs0))
        return R

    prof = [(0.0, 0.07), (0.28, 0.07), (0.33, 0.0), (0.34, 0.0)]
    prof += _sample(rb, 0.0, zs1, 80)[1:]
    prof += [(r_neck, zt), (r_neck - 0.015, zt + 0.012), (r_neck - 0.03, zt)]
    prof += [(r_neck - 0.04, zt - 0.03), (0.0, zt - 0.03)]
    if material == "plastic":
        mat = _principled("Can_Plastic", col, 0.0, 0.25, coat=1.0)
    else:
        mat = _principled("Can_Aluminium", col, 1.0, 0.18)
    meshes = [_to_object("can_body", _revolve(prof, False), mat, pivot)]

    silver = _principled("Can_Lid_Silver", (0.85, 0.86, 0.88, 1), 1.0, 0.2)
    zl = zt - 0.03
    ring_r = 0.13
    ring = [(ring_r + 0.014 * math.cos(math.radians(a)), zl + 0.012 + 0.014 * math.sin(math.radians(a)))
            for a in range(0, 361, 30)]
    meshes.append(_to_object("can_ring", _revolve(ring, False), silver, pivot))
    # pastila (tab) stilizata
    tab = _rounded_cyl(0.065, zl + 0.0, zl + 0.03, 0.012)
    bm = _revolve(tab, True)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0.0, 0.0, 0.0))
    bmesh.ops.scale(bm, verts=bm.verts, vec=(1.0, 0.6, 1.0), space=__import__("mathutils").Matrix.Identity(4))
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0.0, 0.0, 0.0))
    meshes.append(_to_object("can_tab", bm, silver, pivot))
    # banda de brand pe doza
    meshes.append(_label(pivot, R + 0.003, 0.45, 1.25, (0.97, 0.95, 0.9, 1.0), "can_label"))
    return meshes


def _box(pivot, material, color):
    W, D, H = 1.05, 0.50, 1.60
    col = color or (0.1, 0.35, 0.65, 1.0)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, verts=bm.verts, vec=(W, D, H))
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, H / 2))
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.04, segments=4, profile=0.5)
    if material == "metal":
        mat = _principled("Box_Foil", col, 1.0, 0.3)
    elif material == "plastic":
        mat = _principled("Box_Plastic", col, 0.0, 0.25, coat=1.0)
    else:
        mat = _principled("Box_Matte", col, 0.0, 0.6)
    meshes = [_to_object("box_body", bm, mat, pivot)]
    pb = bmesh.new()
    bmesh.ops.create_cube(pb, size=1.0)
    bmesh.ops.scale(pb, verts=pb.verts, vec=(W * 0.78, 0.008, H * 0.42))
    bmesh.ops.translate(pb, verts=pb.verts, vec=(0, -D / 2 - 0.002, H * 0.56))
    lab = _to_object("box_label", pb, _principled("Box_Label", (0.95, 0.93, 0.88, 1), 0.0, 0.6), pivot, smooth=False)
    lab["is_label"] = True
    lab["label_kind"] = "flat"
    lab["label_front_y"] = -D / 2 - 0.002 - 0.004   # fata panoului (spre -Y)
    lab["label_cx"] = 0.0
    lab["label_z0"] = H * 0.56 - H * 0.21
    lab["label_z1"] = H * 0.56 + H * 0.21
    lab["label_w"] = W * 0.78
    lab["label_color"] = [0.95, 0.93, 0.88, 1.0]
    meshes.append(lab)
    return meshes


def _text_mesh(text, font):
    """Text 3D (extrude 0.001/fata = 0.002 total, fara bevel) -> mesh temporar, in planul XY, extrudat pe Z."""
    cu = bpy.data.curves.new("brand_txt_tmp", "FONT")
    cu.body = text
    cu.font = bpy.data.fonts.load(font)
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    cu.extrude = 0.001
    cu.bevel_depth = 0.0
    cu.resolution_u = 6
    tmp = bpy.data.objects.new("brand_txt_tmp", cu)
    bpy.context.scene.collection.objects.link(tmp)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    bpy.data.objects.remove(tmp)
    bpy.data.curves.remove(cu)
    return me


def brand_label(pivot, meshes, text, color=None, font="C:/Windows/Fonts/seguibl.ttf"):
    """Pune numele brandului pe eticheta produsului (curbat pe bottle/jar/can, plat pe box).
    Intoarce obiectul text (mesh) parentat la pivot."""
    lab = next((m for m in meshes if m.get("is_label")), None)
    if lab is None:
        raise ValueError("produsul nu are eticheta marcata (is_label)")
    lc = list(lab["label_color"])
    if color is None:
        lum = 0.2126 * lc[0] + 0.7152 * lc[1] + 0.0722 * lc[2]
        color = (0.03, 0.03, 0.04, 1.0) if lum > 0.35 else (0.96, 0.95, 0.92, 1.0)
    color = tuple(color) + ((1.0,) if len(color) == 3 else ())

    me = _text_mesh(text, font)
    bm = bmesh.new()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)
    xs = [v.co.x for v in bm.verts]
    ys = [v.co.y for v in bm.verts]
    zs = [v.co.z for v in bm.verts]
    w0, h0 = max(xs) - min(xs), max(ys) - min(ys)
    cx0, cy0 = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    zmin = min(zs)
    zc = (lab["label_z0"] + lab["label_z1"]) / 2
    lab_h = lab["label_z1"] - lab["label_z0"]
    cyl = lab["label_kind"] == "cyl"
    if cyl:
        r = lab["label_r"]
        # ~70 % din partea vizibila (corda 2r): semi-unghi asin(0.7), lungimea arcului = 2 r asin(0.7)
        width = 2 * r * math.asin(0.7)
    else:
        width = 0.7 * lab["label_w"]
    s = min(width / w0, 0.6 * lab_h / h0)
    # centrare + scalare
    for v in bm.verts:
        v.co.x = (v.co.x - cx0) * s
        v.co.y = (v.co.y - cy0) * s
        v.co.z = (v.co.z - zmin) * s      # 0 = spatele literei, grosime = 0.002 (nu se scaleaza grosimea)
    # grosime reala fixa 0.002
    thick = max(v.co.z for v in bm.verts) or 1.0
    for v in bm.verts:
        v.co.z = v.co.z / thick * 0.002
    if cyl:
        # taie in felii verticale ca sa se poata curba fara fatete late
        step = 0.012
        wmax = w0 * s / 2
        n = int(wmax / step) + 1
        for i in range(-n, n + 1):
            geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
            bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=(i * step, 0, 0),
                                   plane_no=(1, 0, 0), clear_inner=False, clear_outer=False)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        # taie si pe y (cateva felii) pentru a pastra literele netede nu e necesar: curbura e doar pe x
        for v in bm.verts:
            x, y, z = v.co
            th = -math.pi / 2 + x / r
            rad = r - 0.0006 + z          # spatele putin sub suprafata (lipit), fata la +0.0014
            v.co = (rad * math.cos(th), rad * math.sin(th), zc + y)
    else:
        fy = lab["label_front_y"]
        for v in bm.verts:
            x, y, z = v.co
            v.co = (lab["label_cx"] + x, fy + 0.0006 - z, zc + y)
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])   # oglindire pe y inverseaza normalele
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if cyl:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    nme = bpy.data.meshes.new("brand_text")
    bm.to_mesh(nme)
    bm.free()
    for p in nme.polygons:
        p.use_smooth = False
    nme.materials.append(_principled("Brand_Text", color, 0.0, 0.75))
    ob = bpy.data.objects.new("brand_text", nme)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = pivot          # coordonate in spatiul pivotului; matrix_parent_inverse = identitate
    ob["brand_text"] = text
    meshes.append(ob)
    return ob


def label_image(pivot, meshes, image_path, fit="contain"):
    """Pune o imagine (PNG/JPG, alfa respectat) pe ETICHETA produsului: UV exact pe arcul etichetei (cyl) sau pe panoul
    din fata (flat), fara intindere. contain = incape toata, centrata (restul ramane in culoarea etichetei);
    cover = umple eticheta, taie surplusul. Materialul: mix(culoare eticheta, imagine) dupa alfa, mat (0.5)."""
    lab = next((m for m in meshes if m.get("is_label")), None)
    if lab is None:
        raise ValueError("produsul nu are eticheta marcata (is_label)")
    if fit not in ("contain", "cover"):
        raise ValueError(f"LABEL_FIT necunoscut: {fit!r} (contain | cover)")
    img = bpy.data.images.load(image_path, check_existing=True)
    iw, ih = img.size
    if iw < 1 or ih < 1:
        raise ValueError(f"imagine ilizibila: {image_path}")
    img.colorspace_settings.name = "sRGB"
    img.alpha_mode = "STRAIGHT"
    zc = (lab["label_z0"] + lab["label_z1"]) / 2
    lh = lab["label_z1"] - lab["label_z0"]
    cyl = lab["label_kind"] == "cyl"
    if cyl:
        r = lab["label_r"]
        lw = math.pi * r                  # arcul etichetei (semicilindru, centrat la -90 grade = -Y)
    else:
        lw = lab["label_w"]
    # contain pe cilindru: doar ~62% din arc e văzut din față; pe margini (±90°) imaginea ar fugi din profil și s-ar tăia
    lw_fit = lw * 0.62 if (cyl and fit == "contain") else lw
    k = (min if fit == "contain" else max)(lw_fit / iw, lh / ih)
    pw, ph = iw * k, ih * k               # dimensiunea imaginii pe eticheta, in unitati scena
    me = lab.data
    uvl = me.uv_layers.new(name="LabelUV")
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if cyl:
                th = math.atan2(co.y, co.x)
                if th > math.pi / 2:
                    th -= 2 * math.pi
                u = (th + math.pi / 2) * r
            else:
                u = co.x - lab["label_cx"]
            uvl.data[li].uv = (0.5 + u / pw, 0.5 + (co.z - zc) / ph)
    mat = me.materials[0]
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    base = tuple(lab["label_color"])
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.extension = "CLIP"                # in afara imaginii: alfa 0 -> ramane culoarea etichetei
    tex.interpolation = "Linear"
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "LabelUV"
    nt.links.new(uvn.outputs["UV"], tex.inputs["Vector"])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.inputs["A"].default_value = base
    nt.links.new(tex.outputs["Alpha"], mix.inputs["Factor"])
    nt.links.new(tex.outputs["Color"], mix.inputs["B"])
    nt.links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.5
    lab["label_image"] = image_path
    return lab


_KINDS = {"bottle": _bottle, "jar": _jar, "can": _can, "box": _box}


def procedural_product(kind="bottle", material="sticla", color=None):
    """Construieste un produs 3D. Intoarce (pivot, meshes). Fata/eticheta spre -Y, baza la z=0."""
    if kind not in _KINDS:
        raise ValueError("kind necunoscut: %s (alege din %s)" % (kind, sorted(_KINDS)))
    if material not in ("sticla", "plastic", "metal"):
        raise ValueError("material: sticla | plastic | metal")
    if color is not None:
        color = tuple(color)
        if len(color) == 3:
            color = color + (1.0,)
    pivot = bpy.data.objects.new("product_%s" % kind, None)
    pivot.empty_display_type = "PLAIN_AXES"
    bpy.context.scene.collection.objects.link(pivot)
    meshes = _KINDS[kind](pivot, material, color)
    return pivot, meshes


# ---------- condens procedural (Geometry Nodes, determinist; worker Fable 2026-11-22) ----------
_TRAIL_K = 24  # puncte pe traiectoria fiecarei picaturi care aluneca


# ----------------------------------------------------------------- helper-e noduri
def _sock(coll, name, idx=0):
    """Al idx-lea socket activ cu numele sau identificatorul dat."""
    found = [s for s in coll if (s.name == name or s.identifier == name) and s.enabled]
    if len(found) <= idx:
        raise KeyError("socket %r lipsa; exista: %s" % (name, [(s.name, s.identifier) for s in coll]))
    return found[idx]


class _B:
    """Constructor mic de arbori de noduri."""

    def __init__(self, nt):
        self.nt = nt

    def node(self, type_, **props):
        n = self.nt.nodes.new(type_)
        for k, v in props.items():
            setattr(n, k, v)
        return n

    def put(self, sock, val):
        if isinstance(val, bpy.types.NodeSocket):
            self.nt.links.new(val, sock)
        elif val is not None:
            sock.default_value = val

    def math(self, op, a, b=None, c=None):
        n = self.node("ShaderNodeMath", operation=op)
        for i, v in enumerate((a, b, c)):
            self.put(n.inputs[i], v)
        return n.outputs[0]

    def vmath(self, op, a, b=None, out=0):
        n = self.node("ShaderNodeVectorMath", operation=op)
        self.put(n.inputs[0], a)
        if b is not None:
            self.put(n.inputs[3] if op == "SCALE" else n.inputs[1], b)
        return n.outputs[out]

    def xyz(self, x=0.0, y=0.0, z=0.0):
        n = self.node("ShaderNodeCombineXYZ")
        for i, v in enumerate((x, y, z)):
            self.put(n.inputs[i], v)
        return n.outputs[0]

    def sep(self, v):
        n = self.node("ShaderNodeSeparateXYZ")
        self.put(n.inputs[0], v)
        return n.outputs

    def maprange(self, v, a, b, c, d, interp="SMOOTHSTEP"):
        n = self.node("ShaderNodeMapRange", interpolation_type=interp)
        for name, val in (("Value", v), ("From Min", a), ("From Max", b), ("To Min", c), ("To Max", d)):
            self.put(_sock(n.inputs, name), val)
        return _sock(n.outputs, "Result")

    def rand(self, lo, hi, seed, id_=None):
        n = self.node("FunctionNodeRandomValue", data_type="FLOAT")
        self.put(_sock(n.inputs, "Min"), lo)
        self.put(_sock(n.inputs, "Max"), hi)
        self.put(_sock(n.inputs, "Seed"), seed)
        if id_ is not None:
            self.put(_sock(n.inputs, "ID"), id_)
        return _sock(n.outputs, "Value")

    def cmp(self, op, a, b):
        n = self.node("FunctionNodeCompare", data_type="FLOAT", operation=op)
        self.put(n.inputs[0], a)
        self.put(n.inputs[1], b)
        return n.outputs[0]

    def bool(self, op, a, b=None):
        n = self.node("FunctionNodeBooleanMath", operation=op)
        self.put(n.inputs[0], a)
        if b is not None:
            self.put(n.inputs[1], b)
        return n.outputs[0]

    def noise(self, vec, scale, detail=2.0):
        n = self.node("ShaderNodeTexNoise", noise_dimensions="3D")
        self.put(_sock(n.inputs, "Vector"), vec)
        self.put(_sock(n.inputs, "Scale"), scale)
        self.put(_sock(n.inputs, "Detail"), detail)
        return n.outputs[0]

    def nearest_surface(self, mesh, value, sample_pos):
        n = self.node("GeometryNodeSampleNearestSurface", data_type="FLOAT_VECTOR")
        self.put(_sock(n.inputs, "Mesh"), mesh)
        self.put(_sock(n.inputs, "Value"), value)
        self.put(_sock(n.inputs, "Sample Position"), sample_pos)
        return _sock(n.outputs, "Value")

    def delete(self, geo, sel, domain="POINT"):
        n = self.node("GeometryNodeDeleteGeometry", domain=domain)
        self.put(_sock(n.inputs, "Geometry"), geo)
        self.put(_sock(n.inputs, "Selection"), sel)
        return n.outputs[0]

    def proximity(self, target, element):
        n = self.node("GeometryNodeProximity", target_element=element)
        self.put(n.inputs[0], target)
        return _sock(n.outputs, "Distance")


# ----------------------------------------------------------------- material
def water_material(name="Condens_Apa", thickness=0.012):
    """Apa: Transmission 1, IOR 1.33, roughness 0, refractie raytraced (EEVEE); merge si in Cycles."""
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    if mat.node_tree is None:
        mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    out = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
    if bsdf is None or out is None:
        nt.nodes.clear()
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Base Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    bsdf.inputs["Transmission Weight"].default_value = 1.0
    bsdf.inputs["IOR"].default_value = 1.33
    bsdf.inputs["Roughness"].default_value = 0.0
    bsdf.inputs["Metallic"].default_value = 0.0
    if "Thickness" in out.inputs and not out.inputs["Thickness"].is_linked:
        val = nt.nodes.new("ShaderNodeValue")  # grosimea folosita de EEVEE la refractie (lentila mica)
        val.outputs[0].default_value = thickness
        nt.links.new(val.outputs[0], out.inputs["Thickness"])
    mat.use_raytrace_refraction = True
    mat.surface_render_method = "DITHERED"
    if hasattr(mat, "thickness_mode"):
        mat.thickness_mode = "SPHERE"
    if hasattr(mat, "use_transparent_shadow"):
        mat.use_transparent_shadow = True
    return mat


# ----------------------------------------------------------------- forma picaturii
def _drop_mesh(b, segments, rings, flat, sag):
    """Calota turtita (fara baza), raza 1 in XY, 'jos' = -Y local; sag = ingrosare spre partea de jos."""
    sph = b.node("GeometryNodeMeshUVSphere")
    _sock(sph.inputs, "Segments").default_value = segments
    _sock(sph.inputs, "Rings").default_value = rings
    _sock(sph.inputs, "Radius").default_value = 1.0
    pos = b.node("GeometryNodeInputPosition").outputs[0]
    x, y, z = b.sep(pos)[:3]
    geo = b.delete(sph.outputs[0], b.cmp("LESS_THAN", z, -0.001))
    # profil: turtire pe Z, usor mai plin decat o semisfera scalata
    zz = b.math("MULTIPLY", b.math("POWER", b.math("MAXIMUM", z, 0.0), 0.85), flat)
    if sag > 0.0:
        zz = b.math("MULTIPLY", zz, b.math("SUBTRACT", 1.0, b.math("MULTIPLY", y, sag)))
        x = b.math("MULTIPLY", x, b.math("SUBTRACT", 1.0, b.math("MULTIPLY", y, sag * 0.45)))
    zz = b.math("SUBTRACT", zz, 0.02)  # usor ingropata in suprafata
    sp = b.node("GeometryNodeSetPosition")
    b.put(_sock(sp.inputs, "Geometry"), geo)
    b.put(_sock(sp.inputs, "Position"), b.xyz(x, y, zz))
    sm = b.node("GeometryNodeSetShadeSmooth")
    b.put(sm.inputs[0], sp.outputs[0])
    return sm.outputs[0]


def _align(b, normal):
    """Rotatie: Z local pe normala, Y local cat mai aproape de 'sus' (deci -Y = directia de scurgere)."""
    a1 = b.node("FunctionNodeAlignRotationToVector", axis="Z", pivot_axis="AUTO")
    b.put(_sock(a1.inputs, "Vector"), normal)
    a2 = b.node("FunctionNodeAlignRotationToVector", axis="Y", pivot_axis="Z")
    b.put(_sock(a2.inputs, "Rotation"), a1.outputs[0])
    b.put(_sock(a2.inputs, "Vector"), (0.0, 0.0, 1.0))
    return a2.outputs[0]


def _instance(b, points, mesh, rotation, scale, selection=None):
    n = b.node("GeometryNodeInstanceOnPoints")
    b.put(_sock(n.inputs, "Points"), points)
    b.put(_sock(n.inputs, "Instance"), mesh)
    b.put(_sock(n.inputs, "Rotation"), rotation)
    b.put(_sock(n.inputs, "Scale"), scale)
    if selection is not None:
        b.put(_sock(n.inputs, "Selection"), selection)
    return n.outputs[0]


def _distribute(b, mesh, selection, dmin, dmax, factor, seed):
    n = b.node("GeometryNodeDistributePointsOnFaces", distribute_method="POISSON")
    b.put(_sock(n.inputs, "Mesh"), mesh)
    b.put(_sock(n.inputs, "Selection"), selection)
    b.put(_sock(n.inputs, "Distance Min"), dmin)
    b.put(_sock(n.inputs, "Density Max"), dmax)
    b.put(_sock(n.inputs, "Density Factor"), factor)
    b.put(_sock(n.inputs, "Seed"), seed)
    return n


# ----------------------------------------------------------------- functia principala
def condensation(obj, density=1.0, seed=3, slide_count=4, slide_speed=0.004,
                 size_min=0.003, size_max=0.03, top_factor=0.3, patchiness=0.5,
                 slide_band=(0.45, 0.9), material=None):
    """Pune picaturi de condens pe `obj`. Intoarce obiectul nou (copil al lui `obj`).

    density      multiplicator al numarului de picaturi
    seed         samanta: aceeasi samanta = aceeasi distributie, la orice cadru
    slide_count  cate picaturi mari aluneca in jos (0 = niciuna); pozitia depinde doar de cadru
    slide_speed  viteza medie de alunecare, unitati de lume / cadru
    size_min/max diametrul picaturilor, unitati de lume (0.003-0.03 = 0.3-3 mm la 1 u = 10 cm)
    top_factor   densitatea sus fata de jos (1 = uniform, 0.3 = de ~3 ori mai rare sus)
    patchiness   0 = acoperire aproape uniforma, 1 = zone mari fara picaturi
    slide_band   intre ce inaltimi (0 = baza, 1 = varf) pornesc picaturile care aluneca
    material     material pentru picaturi (implicit water_material())
    """
    if obj is None or obj.type not in {"MESH", "CURVE", "SURFACE", "FONT", "META"}:
        raise TypeError("condensation(): obj trebuie sa fie un obiect cu geometrie")
    seed = int(seed)
    sc = obj.matrix_world.to_scale()
    s = max(1e-6, (abs(sc.x) + abs(sc.y) + abs(sc.z)) / 3.0)  # scara lume / local
    u = 1.0 / s                                                # lungime de lume -> local
    r_min, r_max = 0.5 * size_min * u, 0.5 * size_max * u
    r_mid = r_min + 0.28 * (r_max - r_min)                     # granita micro / medii
    mat = material or water_material(thickness=0.4 * size_max)

    nt = bpy.data.node_groups.new("Condens_" + obj.name, "GeometryNodeTree")
    nt.interface.new_socket("Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    b = _B(nt)
    out = b.node("NodeGroupOutput")

    info = b.node("GeometryNodeObjectInfo", transform_space="ORIGINAL")
    info.inputs[0].default_value = obj
    src = _sock(info.outputs, "Geometry")
    bbox = b.node("GeometryNodeBoundBox")
    b.put(bbox.inputs[0], src)
    bb_min, bb_max = b.sep(_sock(bbox.outputs, "Min")), b.sep(_sock(bbox.outputs, "Max"))
    zmin, zmax = bb_min[2], bb_max[2]
    diag = b.vmath("LENGTH", b.vmath("SUBTRACT", _sock(bbox.outputs, "Max"), _sock(bbox.outputs, "Min")), out=1)

    pos = b.node("GeometryNodeInputPosition").outputs[0]
    nrm = b.node("GeometryNodeInputNormal").outputs[0]
    pz, nz = b.sep(pos)[2], b.sep(nrm)[2]

    # doar fetele exterioare: raza pe normala nu loveste nimic; fara fata de dedesubt
    ray = b.node("GeometryNodeRaycast")
    b.put(_sock(ray.inputs, "Target Geometry"), src)
    b.put(_sock(ray.inputs, "Source Position"), b.vmath("ADD", pos, b.vmath("SCALE", nrm, 0.002 * u)))
    b.put(_sock(ray.inputs, "Ray Direction"), nrm)
    b.put(_sock(ray.inputs, "Ray Length"), diag)
    outer = b.bool("AND", b.bool("NOT", _sock(ray.outputs, "Is Hit")), b.cmp("GREATER_THAN", nz, -0.7))
    sepg = b.node("GeometryNodeSeparateGeometry", domain="FACE")
    b.put(_sock(sepg.inputs, "Geometry"), src)
    b.put(_sock(sepg.inputs, "Selection"), outer)
    outer_mesh = _sock(sepg.outputs, "Selection")

    # densitate: mai des jos + pete de zgomot (zone goale) + variatie fina
    t = b.maprange(pz, zmin, zmax, 0.0, 1.0, "LINEAR")
    hf = b.maprange(t, 0.05, 0.95, 1.0, top_factor)
    wpos = b.vmath("ADD", b.vmath("SCALE", pos, s), (seed * 7.31, seed * 3.17, seed * 1.93))
    big = b.noise(wpos, 2.3, 2.0)
    lo = 0.30 + 0.20 * patchiness
    mask = b.maprange(big, lo, lo + 0.16, 0.0, 1.0)
    fine = b.maprange(b.noise(wpos, 11.0, 1.0), 0.3, 0.7, 0.35, 1.0)
    f_micro = b.math("MULTIPLY", b.math("MULTIPLY", hf, mask), fine)
    f_med = b.math("MULTIPLY", hf, b.math("POWER", mask, 2.0))

    area = s * s  # densitate pe unitate de lume^2 -> local
    d_micro = _distribute(b, src, outer, 0.9 * r_mid, 26000.0 * density * area, f_micro, seed)
    d_med = _distribute(b, src, outer, 1.9 * r_max, 420.0 * density * area, f_med, seed + 1)
    idx = b.node("GeometryNodeInputIndex").outputs[0]

    # raze: multe mici, putine mari
    r_micro = b.math("ADD", r_min, b.math("MULTIPLY", b.math("POWER", b.rand(0.0, 1.0, seed + 11, idx), 2.2), r_mid - r_min))

    def med_radius(id_):
        return b.math("ADD", r_mid, b.math("MULTIPLY", b.math("POWER", b.rand(0.0, 1.0, seed + 12, id_), 2.5), r_max - r_mid))

    pts_micro, pts_med = _sock(d_micro.outputs, "Points"), _sock(d_med.outputs, "Points")
    # raza si rotatia se fixeaza pe puncte INAINTE de stergeri (indexul se schimba dupa stergere)
    pts_micro = _store(b, pts_micro, "condens_r", r_micro)
    pts_med = _store(b, pts_med, "condens_r", med_radius(idx))
    r_attr = _named(b, "condens_r")

    # picaturile medii au "inghitit" micro-picaturile din jur
    near = b.node("GeometryNodeSampleNearest", domain="POINT")
    b.put(_sock(near.inputs, "Geometry"), pts_med)
    r_near = b.node("GeometryNodeSampleIndex", data_type="FLOAT", domain="POINT")
    b.put(_sock(r_near.inputs, "Geometry"), pts_med)
    b.put(_sock(r_near.inputs, "Value"), r_attr)
    b.put(_sock(r_near.inputs, "Index"), _sock(near.outputs, "Index"))
    kill = b.cmp("LESS_THAN", b.proximity(pts_med, "POINTS"), b.math("MULTIPLY", _sock(r_near.outputs, "Value"), 1.7))
    pts_micro = b.delete(pts_micro, kill)

    parts = []
    # ---------------- picaturi care aluneca (pozitie = functie doar de cadru)
    n_sl = max(0, int(slide_count))
    if n_sl > 0:
        K = _TRAIL_K
        wall = b.bool("AND", outer, b.cmp("LESS_THAN", b.math("ABSOLUTE", nz), 0.5))
        band = b.bool("AND", b.cmp("GREATER_THAN", t, float(slide_band[0])), b.cmp("LESS_THAN", t, float(slide_band[1])))
        d_st = _distribute(b, src, b.bool("AND", wall, band), 0.12 * u, 60.0 * area, 1.0, seed + 7)
        starts = _sock(d_st.outputs, "Points")
        cnt = b.node("GeometryNodeAttributeDomainSize", component="POINTCLOUD")
        b.put(cnt.inputs[0], starts)
        count = _sock(cnt.outputs, "Point Count")

        line = b.node("GeometryNodeMeshLine", mode="OFFSET")
        _sock(line.inputs, "Count").default_value = n_sl * K
        riv = b.math("FLOOR", b.math("DIVIDE", idx, float(K)))
        kk = b.math("SUBTRACT", idx, b.math("MULTIPLY", riv, float(K)))
        frac = b.math("DIVIDE", kk, float(K - 1))
        is_head = b.cmp("GREATER_THAN", kk, K - 1.5)

        # alegere stratificata (fara dubluri cat timp exista >= slide_count puncte de start)
        pick = b.math("FLOOR", b.math("MULTIPLY", b.math("ADD", riv, b.rand(0.0, 0.999, seed + 21, riv)), b.math("DIVIDE", count, float(n_sl))))
        si = b.node("GeometryNodeSampleIndex", data_type="FLOAT_VECTOR", domain="POINT")
        b.put(_sock(si.inputs, "Geometry"), starts)
        b.put(_sock(si.inputs, "Value"), pos)
        b.put(_sock(si.inputs, "Index"), pick)
        p0 = _sock(si.outputs, "Value")
        n0 = b.nearest_surface(outer_mesh, nrm, p0)
        tang = b.vmath("NORMALIZE", b.vmath("CROSS_PRODUCT", n0, (0.0, 0.0, 1.0)))

        frame = _sock(b.node("GeometryNodeInputSceneTime").outputs, "Frame")
        w = 0.11  # stick-slip: viteza oscileaza intre 0.4x si 1.6x, mereu in jos
        tt = b.math("ADD", frame, b.rand(25.0, 110.0, seed + 22, riv))
        ph = b.rand(0.0, 6.283, seed + 23, riv)
        tt2 = b.math("ADD", tt, b.math("MULTIPLY", b.math("SINE", b.math("ADD", b.math("MULTIPLY", tt, w), ph)), 0.6 / w))
        dist = b.math("MULTIPLY", tt2, b.math("MULTIPLY", b.rand(0.6, 1.4, seed + 24, riv), slide_speed * u))
        dz = b.math("MULTIPLY", dist, frac)
        me = b.math("ADD",
                    b.math("SINE", b.math("ADD", b.math("MULTIPLY", dz, 17.0 * s), ph)),
                    b.math("MULTIPLY", b.math("SINE", b.math("ADD", b.math("MULTIPLY", dz, 43.0 * s), b.math("MULTIPLY", ph, 2.7))), 0.45))
        p = b.vmath("ADD", b.vmath("SUBTRACT", p0, b.xyz(0.0, 0.0, dz)), b.vmath("SCALE", tang, b.math("MULTIPLY", me, 0.35 * r_max)))
        px, py, pzz = b.sep(p)[:3]
        p = b.xyz(px, py, b.math("MAXIMUM", pzz, b.math("ADD", zmin, 2.5 * r_max)))
        sp = b.node("GeometryNodeSetPosition")
        b.put(_sock(sp.inputs, "Geometry"), line.outputs[0])
        b.put(_sock(sp.inputs, "Position"), b.nearest_surface(outer_mesh, pos, p))
        path = sp.outputs[0]

        # dara: sterge picaturile de pe traseu (muchiile dintre traiectorii diferite se elimina)
        trail = b.delete(path, is_head, domain="EDGE")
        pts_micro = b.delete(pts_micro, b.cmp("LESS_THAN", b.proximity(trail, "EDGES"), 0.85 * r_max))
        pts_med = b.delete(pts_med, b.cmp("LESS_THAN", b.proximity(trail, "EDGES"), 1.9 * r_max))

        m2p = b.node("GeometryNodeMeshToPoints", mode="VERTICES")
        b.put(_sock(m2p.inputs, "Mesh"), path)
        b.put(_sock(m2p.inputs, "Selection"), b.bool("AND", is_head, b.cmp("GREATER_THAN", count, 0.5)))
        heads = _store(b, m2p.outputs[0], "condens_r", b.math("MULTIPLY", b.rand(0.85, 1.15, seed + 25, idx), r_max))
        rot = _align(b, b.nearest_surface(outer_mesh, nrm, pos))
        scl = b.xyz(b.math("MULTIPLY", r_attr, 0.9), b.math("MULTIPLY", r_attr, 1.45), r_attr)
        parts.append(_instance(b, heads, _drop_mesh(b, 24, 12, 0.62, 0.38), rot, scl))

    # ---------------- instantiere micro + medii
    idn = _sock(b.node("GeometryNodeInputID").outputs, "ID")  # stabil la stergeri
    sx = b.rand(0.8, 1.25, seed + 13, idn)
    parts.append(_instance(b, pts_micro, _drop_mesh(b, 8, 4, 0.7, 0.0), _sock(d_micro.outputs, "Rotation"),
                           b.xyz(b.math("MULTIPLY", r_attr, sx), b.math("DIVIDE", r_attr, sx), r_attr)))
    sx = b.rand(0.85, 1.1, seed + 14, idn)
    el = b.maprange(r_attr, r_mid, r_max, 1.0, 1.3, "LINEAR")  # cele mari se alungesc in jos
    parts.append(_instance(b, pts_med, _drop_mesh(b, 16, 8, 0.58, 0.28), _align(b, _sock(d_med.outputs, "Normal")),
                           b.xyz(b.math("MULTIPLY", r_attr, sx), b.math("MULTIPLY", r_attr, el), r_attr)))

    join = b.node("GeometryNodeJoinGeometry")
    for g in parts:
        nt.links.new(g, join.inputs[0])
    sm = b.node("GeometryNodeSetMaterial")
    b.put(_sock(sm.inputs, "Geometry"), join.outputs[0])
    _sock(sm.inputs, "Material").default_value = mat
    nt.links.new(sm.outputs[0], out.inputs[0])

    # ---------------- obiectul nou (sursa ramane neatinsa)
    me_ = bpy.data.meshes.new("Condens_" + obj.name)
    me_.materials.append(mat)
    cond = bpy.data.objects.new("Condens_" + obj.name, me_)
    colls = obj.users_collection or (bpy.context.scene.collection,)
    colls[0].objects.link(cond)
    cond.parent = obj  # matrice locala identitate => urmeaza sursa (turntable etc.)
    cond.visible_shadow = False
    mod = cond.modifiers.new("Condens", "NODES")
    mod.node_group = nt
    return cond


def _store(b, geo, name, value, data_type="FLOAT"):
    n = b.node("GeometryNodeStoreNamedAttribute", data_type=data_type, domain="POINT")
    b.put(_sock(n.inputs, "Geometry"), geo)
    _sock(n.inputs, "Name").default_value = name
    b.put(_sock(n.inputs, "Value"), value)
    return n.outputs[0]


def _named(b, name, data_type="FLOAT"):
    n = b.node("GeometryNodeInputNamedAttribute", data_type=data_type)
    _sock(n.inputs, "Name").default_value = name
    return _sock(n.outputs, "Attribute")


def condensation_stats(cond, depsgraph=None):
    """Numarul de instante (picaturi) evaluat la cadrul curent."""
    dg = depsgraph or bpy.context.evaluated_depsgraph_get()
    return sum(1 for i in dg.object_instances if i.is_instance and i.parent and i.parent.original == cond)


def _bottle_placeholder():
    """Produs de rezervă (dacă procedural_product lipsește): sticlă simplă cu capac."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=0.38, depth=1.5, location=(0, 0, 0.75))
    body = bpy.context.object; body.data.materials.append(hero_material("sticla", None))
    bpy.ops.object.shade_smooth()
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=0.2, depth=0.35, location=(0, 0, 1.68))
    cap = bpy.context.object; cap.data.materials.append(hero_material("aur", None)); bpy.ops.object.shade_smooth()
    bpy.context.view_layer.update()
    return _pivot_for([body, cap], [body, cap], 2.0)


def _ingredients(center, radius, height, count, seed=5):
    """Obiecte mici care orbitează produsul (sfere și cuburi rotunjite, lucioase, în paleta de brand).
    Toate sub un Empty `orbit` (rotit/scalat ca grup) -> mișcare deterministă din chei."""
    import random
    r = random.Random(seed)
    base = COLOR or (1.0, 0.5, 0.1, 1)
    pal = [base, tuple(min(1, c * 0.6 + 0.4) for c in base[:3]) + (1,), (0.95, 0.95, 0.92, 1), (0.25, 0.75, 0.2, 1)]
    mats = [material(f"Ingr{i}", c, roughness=0.18) for i, c in enumerate(pal)]
    bpy.ops.object.empty_add(location=center); orbit = bpy.context.object; orbit.name = "IngredientOrbit"
    protos = []
    if isinstance(INGREDIENTS, (list, tuple)) and INGREDIENTS:   # modele reale (Poly Haven CC0)
        protos = [_model_proto(a) for a in INGREDIENTS]
    objs = []
    for i in range(count):
        if protos:
            ang = 2 * math.pi * i / count + r.uniform(-0.2, 0.2)
            rad = radius * r.uniform(1.6, 2.6); z = r.uniform(-0.42, 0.42) * height
            sz = radius * r.uniform(0.32, 0.55)   # fructele se citesc doar dacă au mărime de fruct lângă produs
            pr = protos[i % len(protos)]
            o = pr.copy(); o.data = pr.data; s0 = bpy.context.scene
            s0.collection.objects.link(o); o.hide_render = False; o.hide_set(False)
            o.parent = orbit; o.location = (rad * math.cos(ang), rad * math.sin(ang), z)
            o.rotation_euler = (r.uniform(0, 6.3), r.uniform(0, 6.3), r.uniform(0, 6.3)); o.scale = (sz,) * 3
            objs.append(o)
            continue
        ang = 2 * math.pi * i / count + r.uniform(-0.2, 0.2)
        rad = radius * r.uniform(1.6, 2.6); z = r.uniform(-0.42, 0.42) * height
        sz = radius * r.uniform(0.16, 0.32)   # vizibile și în cadrele largi (0,07-0,16 erau puncte)
        if i % 3 == 2:
            bpy.ops.mesh.primitive_cube_add(size=2 * sz)
            o = bpy.context.object; bv = o.modifiers.new("b", "BEVEL"); bv.width = sz * 0.35; bv.segments = 4
        else:
            bpy.ops.mesh.primitive_uv_sphere_add(radius=sz, segments=32, ring_count=16)
            o = bpy.context.object
        bpy.ops.object.shade_smooth()
        # copil direct al orbitei (Empty fără rotație/scale la creare): locația e relativă la centru.
        # (copierea matrix_world fără view_layer.update() ar fi pus toate obiectele în centrul produsului)
        o.parent = orbit; o.location = (rad * math.cos(ang), rad * math.sin(ang), z)
        o.rotation_euler = (r.uniform(0, 3), r.uniform(0, 3), r.uniform(0, 3))
        o.data.materials.append(mats[i % len(mats)])
        objs.append(o)
    return orbit, objs


def splash(center, radius, frame, color, scale=1.0, seed=5):
    """Splash de lichid la aterizarea produsului, ca în reclamele de băuturi: o coroană care urcă și se deschide
    în jurul bazei (cu „degete” la margine care se rup în picături) + picături balistice care zboară în afară.
    DETERMINIST, fără simulare/bake: totul e funcție de Scene Time (Geometry Nodes), deci orice cadru randat
    separat iese la fel. Coroana = grilă înfășurată (profil închis, cu grosime) cu înălțime/rază/grosime = f(t);
    picăturile = vârfuri cu atributele v0/rad/t0, mutate pe p0 + v0·t + ½·g·t² și instanțiate ca sfere alungite.

    center  baza produsului, în lume (z = podeaua)      radius  raza produsului (coroana pornește imediat în afara ei)
    frame   cadrul impactului (t = 0)                   color   culoarea lichidului (RGBA)
    scale   mărimea splash-ului (înălțime, viteze, picături); 1 = coroană ~0,8 x raza, 1,5 = spectaculos
    seed    aceeași sămânță = același splash
    Durată: coroana ~0,62 s, ultimele picături cad la ~0,85 s după `frame` („slow motion”: g = 9 unități/s²).
    Întoarce obiectul (fără umbre: shadow buffer). Luminile cu light linking trebuie să-l primească în colecție."""
    import random
    rnd = random.Random(seed)
    sc = bpy.context.scene; fps = sc.render.fps
    TC, G = 0.62, 9.0 * scale                 # durata coroanei (s), gravitația
    HMAX = 0.8 * radius * scale               # înălțimea coroanei (fără degete)
    WB = 0.065 * radius * scale               # grosimea foii la bază
    K = rnd.randint(11, 14)                   # câte degete
    q = [rnd.uniform(0, 2 * math.pi) for _ in range(4)]
    P1, P2, AMAX, PI = 0.9, 0.5, 1.0, math.pi

    # ---- aceleași formule în Python (pentru vârfurile degetelor, de unde se desprind picături)
    sst = lambda a, b_, x: _smoothstep((x - a) / (b_ - a))
    phase = lambda th: K * th + P1 * math.sin(th + q[0]) + P2 * math.sin(2 * th + q[1])
    finger = lambda th: max(0.0, math.cos(phase(th))) ** 3 * (0.55 + 0.45 * (0.5 + 0.5 * math.sin(3 * th + q[2])))

    def tip(th, t):
        tau = min(1.0, max(0.0, t / TC))
        amp = AMAX * sst(0.08, 0.45, tau) * (1 - 0.6 * sst(0.5, 0.8, tau))
        hc = HMAX * math.sin(PI * tau ** 0.6) * (1 + amp * finger(th) + 0.1 * math.sin(5 * th + q[3]))
        r = radius * (1.03 + 0.35 * tau ** 0.7) + (0.25 + 0.75 * tau) * hc
        return Vector((r * math.cos(th), r * math.sin(th), hc))

    drops = []   # (p0, v0, raza, t0)
    for m in range(math.ceil(phase(0) / (2 * PI)), math.ceil(phase(2 * PI) / (2 * PI))):
        lo, hi = 0.0, 2 * PI                  # faza e monotonă: vârful degetului m prin bisecție
        for _ in range(40):
            mid = (lo + hi) / 2
            lo, hi = (mid, hi) if phase(mid) < 2 * PI * m else (lo, mid)
        th = (lo + hi) / 2
        if finger(th) < 0.3:
            continue
        for j in range(2 if rnd.random() < 0.6 else 1):   # picătura din vârf + uneori un satelit mai mic
            tb = TC * rnd.uniform(0.40, 0.52) + 0.05 * j
            p0 = tip(th, tb); v0 = (tip(th, tb + 0.01) - tip(th, tb - 0.01)) / 0.02
            v0 = Vector((v0.x * 1.25, v0.y * 1.25, v0.z + rnd.uniform(0.3, 0.9) * scale))
            drops.append((p0, v0, WB * rnd.uniform(0.75, 1.1) * (0.6 if j else 1.0), tb))
    for i in range(30):                        # stropii balistici de la bază
        th = 2 * PI * (i + rnd.uniform(-0.4, 0.4)) / 30
        r0 = radius * rnd.uniform(1.05, 1.2)
        vr, vz = rnd.uniform(0.7, 2.4) * scale, rnd.uniform(1.6, 3.15) * scale
        rad = (0.014 + 0.036 * rnd.random() ** 2) * scale * (radius / 0.4) ** 0.5
        drops.append((Vector((r0 * math.cos(th), r0 * math.sin(th), 0.03)),
                      Vector((vr * math.cos(th), vr * math.sin(th), vz)), rad, rnd.uniform(0.0, 0.06)))

    me = bpy.data.meshes.new("Splash")
    me.from_pydata([tuple(d[0]) for d in drops], [], [])
    at = me.attributes.new("v0", "FLOAT_VECTOR", "POINT")
    at.data.foreach_set("vector", [c for d in drops for c in d[1]])
    me.attributes.new("rad", "FLOAT", "POINT").data.foreach_set("value", [d[2] for d in drops])
    me.attributes.new("t0", "FLOAT", "POINT").data.foreach_set("value", [d[3] for d in drops])

    # ---- material: lichid colorat, transmisie parțială + emisie (pe fundal închis, transparentul iese negru)
    deep = tuple(c ** 2.2 for c in color[:3]) + (1.0,)   # mai saturat: luminile de packshot + AgX spală culoarea
    mat = _liquid("Splash_Liquid", deep)
    bs = mat.node_tree.nodes.get("Principled BSDF")
    bs.inputs["Transmission Weight"].default_value = 0.7
    bs.inputs["Emission Color"].default_value = tuple(color)
    bs.inputs["Roughness"].default_value = 0.03
    bs.inputs["Specular IOR Level"].default_value = 0.8
    bs.inputs["Coat Weight"].default_value = 1.0; bs.inputs["Coat Roughness"].default_value = 0.02
    bs.inputs["Emission Strength"].default_value = 0.55
    me.materials.append(mat)

    # ---- Geometry Nodes
    nt = bpy.data.node_groups.new("Splash", "GeometryNodeTree")
    nt.interface.new_socket("Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
    nt.interface.new_socket("Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    b = _B(nt); gin = b.node("NodeGroupInput"); out = b.node("NodeGroupOutput")
    M = b.math
    mul = lambda x, y: M("MULTIPLY", x, y)
    add = lambda x, y: M("ADD", x, y)
    sub = lambda x, y: M("SUBTRACT", x, y)
    t = M("DIVIDE", sub(_sock(b.node("GeometryNodeInputSceneTime").outputs, "Frame"), float(frame)), float(fps))
    tau = M("MINIMUM", M("MAXIMUM", M("DIVIDE", t, TC), 0.0), 1.0)

    # coroana: grilă (u = în jurul axei, v = în jurul profilului închis bază -> margine -> bază)
    grid = b.node("GeometryNodeMeshGrid")
    for n_, v_ in (("Size X", 1.0), ("Size Y", 1.0), ("Vertices X", 241), ("Vertices Y", 33)):
        _sock(grid.inputs, n_).default_value = v_
    gx, gy = b.sep(b.node("GeometryNodeInputPosition").outputs[0])[:2]
    th = M("MULTIPLY_ADD", add(gx, 0.5), 2 * PI, PI / 2)     # cusătura în spate (+Y), camera e pe -Y
    ph = mul(add(gy, 0.5), 2 * PI)
    s_ = mul(sub(1.0, M("COSINE", ph)), 0.5)                 # 0 la bază, 1 la margine
    sp = M("SINE", ph)
    side = mul(M("SIGN", sp), M("POWER", M("ABSOLUTE", sp), 0.4))   # -1 perete interior .. +1 exterior
    pha = add(mul(th, float(K)), add(mul(M("SINE", add(th, q[0])), P1), mul(M("SINE", add(mul(th, 2.0), q[1])), P2)))
    fing = mul(M("POWER", M("MAXIMUM", M("COSINE", pha), 0.0), 3.0),
               M("MULTIPLY_ADD", M("SINE", add(mul(th, 3.0), q[2])), 0.225, 0.775))
    amp = mul(mul(b.maprange(tau, 0.08, 0.45, 0.0, 1.0), AMAX), b.maprange(tau, 0.5, 0.8, 1.0, 0.4))
    hc = mul(mul(M("SINE", mul(M("POWER", tau, 0.6), PI)), HMAX),
             add(add(1.0, mul(amp, fing)), mul(M("SINE", add(mul(th, 5.0), q[3])), 0.1)))
    z = mul(s_, hc)
    lean = M("MULTIPLY_ADD", tau, 0.75, 0.25)
    rb = mul(M("MULTIPLY_ADD", M("POWER", tau, 0.7), 0.35, 1.03), radius)
    cth, sth = M("COSINE", th), M("SINE", th)
    nz_ = b.noise(b.xyz(mul(cth, 1.3), mul(sth, 1.3), add(mul(s_, 1.2), mul(t, 0.8))), 2.5, 2.0)
    wob = mul(mul(sub(nz_, 0.5), 0.3 * radius * scale), s_)                       # foaia nu e un con perfect
    # grosime: plină la bază, subțire la mijloc, mărgea la margine (mai groasă pe degete); se subțiază în timp
    bead = mul(M("MULTIPLY_ADD", fing, 1.6, 0.7), M("POWER", s_, 8.0))
    w = mul(mul(M("POWER", sub(1.0, tau), 0.6), WB), add(M("MULTIPLY_ADD", s_, -0.72, 1.0), bead))
    r = add(add(add(rb, mul(mul(lean, z), M("MULTIPLY_ADD", s_, 0.5, 0.5))), wob), mul(mul(w, 0.5), side))
    setp = b.node("GeometryNodeSetPosition")
    b.put(_sock(setp.inputs, "Geometry"), grid.outputs[0])
    b.put(_sock(setp.inputs, "Position"), b.xyz(mul(r, cth), mul(r, sth), z))
    crown = b.delete(setp.outputs[0], b.bool("OR", b.cmp("LESS_THAN", t, 0.0), b.cmp("GREATER_THAN", t, TC)))

    # picăturile: traiectorii analitice; dispar când ating podeaua
    m2p = b.node("GeometryNodeMeshToPoints", mode="VERTICES")
    b.put(_sock(m2p.inputs, "Mesh"), gin.outputs[0])
    v0, rad, t0 = _named(b, "v0", "FLOAT_VECTOR"), _named(b, "rad"), _named(b, "t0")
    td = sub(t, t0)
    vel = b.vmath("ADD", v0, b.xyz(0.0, 0.0, mul(td, -G)))
    pset = b.node("GeometryNodeSetPosition")
    b.put(_sock(pset.inputs, "Geometry"), m2p.outputs[0])
    b.put(_sock(pset.inputs, "Offset"), b.vmath("ADD", b.vmath("SCALE", v0, td), b.xyz(0.0, 0.0, mul(mul(td, td), -0.5 * G))))
    pz = b.sep(b.node("GeometryNodeInputPosition").outputs[0])[2]
    pts = b.delete(pset.outputs[0], b.bool("OR", b.cmp("LESS_THAN", td, 0.0), b.cmp("LESS_THAN", pz, mul(rad, 0.6))))
    ico = b.node("GeometryNodeMeshIcoSphere")
    _sock(ico.inputs, "Radius").default_value = 1.0; _sock(ico.inputs, "Subdivisions").default_value = 3
    rot = b.node("FunctionNodeAlignRotationToVector", axis="Z")
    b.put(_sock(rot.inputs, "Vector"), vel)
    st = M("MINIMUM", M("MULTIPLY_ADD", b.vmath("LENGTH", vel, out=1), 0.1, 1.0), 1.7)   # alungire pe direcția de zbor
    thin = mul(rad, M("POWER", st, -0.5))
    inst = _instance(b, pts, ico.outputs[0], rot.outputs[0], b.xyz(thin, thin, mul(rad, st)))

    join = b.node("GeometryNodeJoinGeometry")
    nt.links.new(crown, join.inputs[0]); nt.links.new(inst, join.inputs[0])
    real = b.node("GeometryNodeRealizeInstances")
    b.put(real.inputs[0], join.outputs[0])
    sm = b.node("GeometryNodeSetShadeSmooth")
    b.put(sm.inputs[0], real.outputs[0])
    smat = b.node("GeometryNodeSetMaterial")
    b.put(_sock(smat.inputs, "Geometry"), sm.outputs[0])
    _sock(smat.inputs, "Material").default_value = mat
    nt.links.new(smat.outputs[0], out.inputs[0])

    ob = bpy.data.objects.new("Splash", me)
    sc.collection.objects.link(ob)
    ob.location = tuple(center)
    ob.visible_shadow = False
    ob.modifiers.new("Splash", "NODES").node_group = nt
    return ob


def build_commercial(a):
    """Reclamă 3D de produs (fără text ca subiect): 5 cadre cu tăieturi între camere, ca la spoturile CGI.
    1 macro pe detaliu (100 mm, glisare)  2 reveal: arc + apropiere, produsul levitează (50 mm)
    3 ingredientele orbitează, rack focus (85 mm)  4 de jos, crane up, ingredientele explodează în afară (35 mm)
    5 packshot: produsul aterizează, TEXT (brand/CTA) lângă el, push-in (70 mm).
    Rețeta din cercetarea 2026-11-22 (Fable + Codex, surse în references/moduri.md). Produs = MODEL sau procedural."""
    s = bpy.context.scene; N = s.frame_end; fps = s.render.fps
    s.eevee.use_raytracing = True; s.eevee.ray_tracing_options.resolution_scale = "1"; s.eevee.use_fast_gi = True
    s.render.dither_intensity = 1.0
    portrait = s.render.resolution_y > s.render.resolution_x
    stack = s.render.resolution_x / s.render.resolution_y <= 1.05   # 9:16 și 1:1: textul DEASUPRA produsului
    # --- produsul
    if MODEL:
        pivot, meshes = import_model(MODEL, size=2.0)
    elif "procedural_product" in globals():
        pm = {"sticla": "sticla", "plastic": "plastic", "aur": "metal", "crom": "metal"}.get(MATERIAL, "sticla")
        if PRODUCT in ("can",) and pm == "sticla":
            pm = "metal"   # doza de sticlă nu există; box acceptă orice
        pivot, meshes = procedural_product(PRODUCT, pm, COLOR)
        if LABEL_IMAGE:    # designul/logo-ul clientului pe etichetă (în loc de textul brandului)
            label_image(pivot, meshes, LABEL_IMAGE, LABEL_FIT)
        elif TEXT.strip():   # numele brandului curbat pe etichetă (diacritice ok)
            brand_label(pivot, meshes, TEXT.strip(), font=font_path())
    else:
        pivot, meshes = _bottle_placeholder()
    # (după TOT lanțul if/elif/else: inserat între elif și else, ar fi furat else-ul -> sticlă provizorie în plus)
    if CONDENSATION > 0:   # picături de condens pe corpul produsului (cel mai mare mesh, fără etichetă/text)
        cand = [o for o in meshes if o.type == "MESH" and not o.get("is_label") and "brand" not in o.name.lower()]
        body = max(cand, key=lambda o: o.dimensions.x * o.dimensions.y * o.dimensions.z)
        condensation(body, density=CONDENSATION, seed=11, slide_count=4, slide_band=(0.3, 0.62),
                     top_factor=0.8, size_min=0.004, size_max=0.04)
        for lab in [o for o in meshes if o.get("is_label")]:   # și pe etichetă: ea acoperă fața văzută de cameră
            condensation(lab, density=CONDENSATION, seed=12, slide_count=2, slide_band=(0.3, 0.9),
                         top_factor=0.8, size_min=0.004, size_max=0.04)
        # picăturile de pe o sticlă TRANSPARENTĂ: refracția lor trece prin sticlă (neopacă) -> cade pe fundal = pete
        # închise. Le facem parțial transparente (alfa 0,3): rămân reflexiile/highlight-urile, se vede sticla din spate.
        wm = bpy.data.materials.get("Condens_Apa")
        if wm and MATERIAL == "sticla":
            wb = wm.node_tree.nodes.get("Principled BSDF")
            if wb:   # mărgele de apă lucioase: fără transmisie (n-ar avea ce refracta), reflexii + alfa 0,55
                wb.inputs["Transmission Weight"].default_value = 0.0; wb.inputs["Alpha"].default_value = 0.55
                wb.inputs["Base Color"].default_value = (0.8, 0.85, 0.9, 1); wb.inputs["Roughness"].default_value = 0.0
                wb.inputs["Coat Weight"].default_value = 1.0
            wm.use_raytrace_refraction = False; wm.surface_render_method = "DITHERED"
        # EEVEE: refracția prin picături vede doar obiecte OPACE -> cu lichid transparent picăturile ar arăta fundalul
        # (pete închise). Lichidul devine aproape opac, cu o strălucire caldă din interior (verificat 2026-11-22).
        for o in meshes:
            if "liquid" in o.name.lower():
                for m in o.data.materials:
                    b = m.node_tree.nodes.get("Principled BSDF")
                    if b:
                        col = b.inputs["Base Color"].default_value
                        b.inputs["Transmission Weight"].default_value = 0.0
                        b.inputs["Emission Color"].default_value = col; b.inputs["Emission Strength"].default_value = 0.35
    bpy.context.view_layer.update()
    pts = [p for o in meshes for p in _sample_pts(o, 800)]
    mn, mx = _bbox(pts); H = mx.z - mn.z; R = max(max(abs(p.x), abs(p.y)) for p in pts)
    lift = 0.22 * H
    # --- decor: studio întunecat cu fundal gradient (glow) sau cicloramă albă
    if STUDIO == "light":
        product_studio(pivot, meshes, shadow_reach=0.8)
    else:
        s.view_settings.look = "AgX - High Contrast"
        studio_world(ensure_hdri(HDRI_ID), strength=1.0)
        bd = backdrop("glow", BG_COLOR, dist=9.0)
        bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, 0))
        bpy.context.object.data.materials.append(material("Floor", (0.004, 0.004, 0.005, 1), roughness=0.1, specular=0.4))
    # --- cronologia (fracțiuni din durată -> se adaptează la SECONDS)
    # ritm „build -> snap -> breathe” (Gemini/Fable/Codex 2026-11-22): cadre tot mai scurte până la vârf (1,6/1,5/1,3/
    # 1,0 s la 10 s), apoi packshot lung (~46%); durate egale trădează generarea automată
    cut = [1] + [max(2, round(N * f)) for f in (0.16, 0.31, 0.44, 0.54)] + [N + 1]
    sh = [(cut[i], cut[i + 1] - 1) for i in range(5)]
    land0, land1 = sh[4][0], sh[4][0] + int(fps * 0.45)
    if STUDIO != "light" and bd:   # culoarea fundalului se schimbă subtil la fiecare tăietură (mood shift)
        nt_bd = bd.data.materials[0].node_tree
        ramp = next(n for n in nt_bd.nodes if n.type == "VALTORGB"); emn = next(n for n in nt_bd.nodes if n.type == "EMISSION")
        bc = BG_COLOR; pc = COLOR or (1.0, 0.5, 0.1, 1)
        mixc = lambda t: tuple(bc[i] * (1 - t) + pc[i] * t for i in range(3)) + (1,)
        for (f0_, _), (col, stg) in zip(sh, ((mixc(0.0), 0.10), (mixc(0.15), 0.18), (mixc(0.45), 0.20),
                                             (mixc(0.6), 0.28), (mixc(0.1), 0.20))):
            ramp.color_ramp.elements[1].color = col; ramp.color_ramp.elements[1].keyframe_insert("color", frame=f0_)
            emn.inputs["Strength"].default_value = stg; emn.inputs["Strength"].keyframe_insert("default_value", frame=f0_)
        ad_bd = nt_bd.animation_data
        for fc in anim_utils.action_get_channelbag_for_slot(ad_bd.action, ad_bd.action_slot).fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = "CONSTANT"   # schimbare PE tăietură, nu tranziție
    for o in meshes:   # lichidul din sticlă se clatină la aterizare (nu stă înghețat cât afară e splash)
        if "liquid" in o.name.lower():
            r0 = o.rotation_euler.copy()
            for k, (df, ang) in enumerate(((0, 0.0), (5, 1.6), (10, -1.0), (15, 0.5), (21, 0.0))):
                key(o, "rotation_euler", (r0.x + math.radians(ang), r0.y, r0.z), land1 - int(fps * 0.1) + df, "SINE", "EASE_IN_OUT")
    # produsul: levitează (bob lent) și se rotește ușor; în cadrul 5 aterizează
    pivot.location.z = lift
    yaw0 = pivot.rotation_euler.z
    key(pivot, "rotation_euler", (0, 0, yaw0 - math.radians(35)), 1, "LINEAR")
    key(pivot, "rotation_euler", (0, 0, yaw0 + math.radians(10)), land1, "SINE", "EASE_OUT")
    key(pivot, "rotation_euler", (0, 0, yaw0 + math.radians(12)), N, "LINEAR")
    step = max(6, int(fps * 0.8))
    for f in range(1, land0, step):
        key(pivot, "location", (0, 0, lift + 0.03 * H * math.sin(f / step * 1.3)), f, "SINE", "EASE_IN_OUT")
    key(pivot, "location", (0, 0, lift * 0.9), land0, "QUAD", "EASE_IN")
    key(pivot, "location", (0, 0, 0.0), land1 - int(fps * 0.1), "QUAD", "EASE_OUT")
    key(pivot, "location", (0, 0, 0.012 * H), land1 - int(fps * 0.05), "SINE", "EASE_IN_OUT")
    key(pivot, "location", (0, 0, 0.0), land1, "SINE", "EASE_OUT")
    cz = lift + H * 0.5
    # --- ingredientele
    orbit, ingr = (None, [])
    if INGREDIENTS:
        # mărimea ingredientelor din raza produsului, plafonată la ~0,35 H: la produse late (borcan) ieșeau uriașe
        orbit, ingr = _ingredients(Vector((0, 0, cz)), min(R, 0.35 * H), H, 14)
        key(orbit, "rotation_euler", (0, 0, 0), 1, "LINEAR"); key(orbit, "rotation_euler", (0, 0, math.radians(150)), N, "LINEAR")
        key(orbit, "scale", (0.0001,) * 3, 1, "CONSTANT")   # nu în macro: o sferă din față ar acoperi detaliul
        key(orbit, "scale", (1, 1, 1), sh[1][0], "CONSTANT")
        key(orbit, "scale", (1, 1, 1), sh[3][0], "EXPO", "EASE_OUT")
        key(orbit, "scale", (1.7, 1.7, 1.4), sh[3][1], "EXPO", "EASE_OUT")       # „explozie” spre exterior în cadrul 4
        key(orbit, "scale", (0.0001,) * 3, sh[4][0], "CONSTANT")                  # packshot curat
    # --- TEXT (brand/CTA) doar în packshot
    txt = None
    if TEXT.strip():
        txt = text_3d(TEXT.strip(), size=1.0, extrude=0.08, bevel=0.015, font=font_path())
        # logo de packshot: pe închis alb curat cu emisie ușoară (auriul ieșea întunecat); pe studio ALB culoarea de
        # brand închisă, fără emisie (alb pe alb nu se citea - test agent 2026-11-22)
        if STUDIO == "light":
            bc = COLOR or (0.1, 0.1, 0.12, 1)
            txt.data.materials.append(material("PackText", tuple(c * 0.35 for c in bc[:3]) + (1,), roughness=0.35))
        else:
            txt.data.materials.append(material("PackText", (0.95, 0.95, 0.95, 1), roughness=0.3,
                                               emission=(1, 1, 1, 1), strength=0.6))
        bpy.context.view_layer.update()
        u = (0.9 * H if stack else 1.5 * H) / max(txt.dimensions.x, 1e-3)   # 9:16: lățimea cadrului e mică
        txt.data.size *= u; txt.data.extrude *= u; txt.data.bevel_depth *= u
        bpy.context.view_layer.update()
        if stack:   # deasupra produsului
            txt.location = (0, 0, H + 0.35 * H)
        else:          # în dreapta produsului, la mijlocul lui
            txt.location = (R + 0.12 * H + txt.dimensions.x / 2, 0, H * 0.5)
        txt_w = txt.dimensions.x   # citită ÎNAINTE de cheia de scară 0,0001 (după ea dimensions ar fi ~0)
        key(txt, "scale", (0.0001,) * 3, land1, "BACK", "EASE_OUT"); key(txt, "scale", (1, 1, 1), land1 + int(fps * 0.5))
    # --- splash la atingerea podelei (cadrul de impact = land1 - 0,1 s; land1 = produsul așezat după micul recul)
    fx = []
    if SPLASH:
        liq = [m_ for o in meshes if "liquid" in o.name.lower() for m_ in o.data.materials]
        lb = liq[0].node_tree.nodes.get("Principled BSDF") if liq else None
        lcol = tuple(lb.inputs["Base Color"].default_value) if lb else (COLOR or _AMBER)
        fx.append(splash((0, 0, 0), R, land1 - int(fps * 0.1), lcol))
    # --- camerele (fit pe verticală; ținta e un Empty animat)
    def camera(name, lens, fstop):
        cd = bpy.data.cameras.new(name); cd.lens = lens; cd.sensor_fit = "VERTICAL"; cd.sensor_height = 24
        c = bpy.data.objects.new(name, cd); s.collection.objects.link(c)
        bpy.ops.object.empty_add(location=(0, 0, cz)); t = bpy.context.object; t.name = name + "_tgt"
        tr = c.constraints.new("TRACK_TO"); tr.target = t; tr.track_axis = "TRACK_NEGATIVE_Z"; tr.up_axis = "UP_Y"
        cd.dof.use_dof = True; cd.dof.focus_object = t; cd.dof.aperture_fstop = fstop
        return c, t
    def place(c, t, f, tz, h_frame, yaw, pitch, interp="BEZIER", ease="AUTO", tx=0.0):
        d = (h_frame / 2) / math.tan(c.data.angle_y / 2) if c.data.angle_y else 5.0
        tgt = Vector((tx, 0, tz))
        off = Vector((math.sin(math.radians(yaw)) * math.cos(math.radians(pitch)),
                      -math.cos(math.radians(yaw)) * math.cos(math.radians(pitch)), math.sin(math.radians(pitch)))) * d
        pos = tgt + off
        # niciodată sub podea: pe 9:16 distanța e mare și unghiul de jos ar duce camera sub planul podelei -> cadre
        # complet negre (bug văzut la cadrul 4 pe vertical; framing_report nu-l prinde, finalize.py da)
        pos.z = max(pos.z, 0.12 * H)
        key(c, "location", tuple(pos), f, interp, ease); key(t, "location", tuple(tgt), f, interp, ease)
    asp = s.render.resolution_x / s.render.resolution_y
    hv = lambda h_world_w, h_world_h: max(h_world_h, h_world_w / asp)   # înălțimea de cadru care cuprinde w x h
    full = H + 0.12 * H
    c1, t1 = camera("Cam1_macro", 100, 2.8); c2, t2 = camera("Cam2_reveal", 50, 4.0)
    c3, t3 = camera("Cam3_orbit", 85, 2.8); c4, t4 = camera("Cam4_low", 35, 5.6); c5, t5 = camera("Cam5_pack", 70, 8.0)
    a0, a1 = sh[0]
    mz = 0.42 if CONDENSATION > 0 else 0.78   # cu condens, macro-ul e pe picăturile de pe corp, nu pe capac
    place(c1, t1, a0, lift + mz * H, 0.42 * H, -18, 8, "LINEAR"); place(c1, t1, a1, lift + (mz - 0.04) * H, 0.40 * H, 12, 6, "LINEAR")
    b0, b1 = sh[1]
    place(c2, t2, b0, cz, hv(2 * R, full) / 0.58, -40, 14, "EXPO", "EASE_OUT"); place(c2, t2, b1, cz, hv(2 * R, full) / 0.7, -8, 10)
    d0, d1 = sh[2]
    place(c3, t3, d0, cz, hv(5 * R, full) / 0.86, 25, 6, "LINEAR"); place(c3, t3, d1, cz, hv(5 * R, full) / 0.88, 32, 6, "LINEAR")
    if ingr:   # rack focus: întâi pe un ingredient din față, apoi pe produs
        near = min(ingr, key=lambda o: (o.matrix_world.translation - c3.location).length)
        c3.data.dof.focus_object = None
        dist_i = (near.matrix_world.translation - c3.location).length; dist_p = (Vector((0, 0, cz)) - c3.location).length
        c3.data.dof.focus_distance = dist_i; c3.data.dof.keyframe_insert("focus_distance", frame=d0 + int(fps * 0.2))
        c3.data.dof.focus_distance = dist_p; c3.data.dof.keyframe_insert("focus_distance", frame=d0 + int(fps * 1.0))
    e0, e1 = sh[3]
    place(c4, t4, e0, cz, hv(4 * R, full) / 0.55, 15, -12, "SINE", "EASE_IN"); place(c4, t4, e1, cz, hv(4 * R, full) / 0.5, 0, 4)
    g0, g1 = sh[4]
    # centrul pe orizontală al grupului produs + text: de la -R la marginea dreaptă a textului
    right = (txt.location.x + txt_w / 2) if (txt and not stack) else R
    tx = (right - R) / 2
    wpack = (right + R) * 1.08
    hpack = H * 1.12 + (0.6 * H if txt and stack else 0)
    tz = (H * 1.12 + 0.6 * H) / 2 if (txt and stack) else H * 0.55
    hp0, hp1 = hv(wpack, hpack) / (0.5 if portrait else 0.7), hv(wpack, hpack) / (0.53 if portrait else 0.75)
    # 9:16: ținta coboară -> conținutul urcă spre zona sigură Reels/TikTok (jos 35% e acoperit de interfață)
    dzp = 0.14 * hp0 if portrait else 0.0
    place(c5, t5, g0, tz - dzp, hp0, 0, 6, "EXPO", "EASE_OUT", tx=tx)
    place(c5, t5, g1, tz - dzp, hp1, 0, 5, "LINEAR", tx=tx)
    cams = [c1, c2, c3, c4, c5]
    for (f0, _), c in zip(sh, cams):   # tăieturi: markere legate de camere
        m = s.timeline_markers.new(c.name, frame=f0); m.camera = c
    s.camera = c1
    FRAMING_SKIP.append((sh[0][0], sh[0][1]))   # macro: produsul e tăiat intenționat
    # --- lumini de reclamă: key moale, 2 benzi verticale (contur pe sticlă), rim rece/cald, top; softbox pe fiecare cameră
    prod = list(meshes) + ([txt] if txt else [])
    lights = [area("Key", (-3.2, -3.6, cz + 2.5), 380, (1, .97, .92), shape="RECTANGLE", sx=2.5, sy=2.5, target=(0, 0, cz)),
              area("StripL", (-2.4, 0.9, cz), 450, (1, 1, 1), shape="RECTANGLE", sx=0.25, sy=H * 2.2, target=(0, 0, cz)),
              area("StripR", (2.4, 0.9, cz), 450, (1, 1, 1), shape="RECTANGLE", sx=0.25, sy=H * 2.2, target=(0, 0, cz)),
              area("RimCold", (-2.2, 3.2, cz + 1.5), 1600, (0.55, 0.75, 1.0), size=1.2, target=(0, 0, cz)),
              area("RimWarm", (2.2, 3.2, cz + 1.2), 1400, (1.0, 0.7, 0.4), size=1.2, target=(0, 0, cz)),
              area("Top", (0, 0, cz + H + 2.5), 350, (1, 1, 1), shape="RECTANGLE", sx=2, sy=2, target=(0, 0, cz))]
    if STUDIO == "light":   # pe alb luminile de contur nu mai sunt legate doar de produs: spălau tot (text, lichid)
        for l in lights:
            if l.name.startswith(("Rim", "Strip", "Top")):
                l.data.energy *= 0.3
    for l in lights[1:]:   # umbre doar pe Key: 6 lumini area cu umbre + condens = „Shadow buffer full” la 1080p
        l.data.use_shadow = False
    for i, c in enumerate(cams):
        # macro (1) are softbox slab: de aproape orice lumină arde; packshot-ul (5) primește cel mai mult (text auriu)
        cw = CARD_W if CARD_W is not None else (60, 160, 160, 160, 700)[i]
        cd = area(f"Card{i + 1}", (0, 0, 0), cw, (1, .96, .9), shape="RECTANGLE", sx=6, sy=3)
        cd.data.use_shadow = False; cd.parent = c; cd.location = (0, 0, 3.0); cd.rotation_euler = (0, 0, 0)
        f0, f1 = sh[i]
        for f, e in ((max(1, f0 - 1), 0.0), (f0, cd.data.energy), (f1, cd.data.energy), (f1 + 1, 0.0)):
            old = cd.data.energy; cd.data.energy = e; cd.data.keyframe_insert("energy", frame=f); cd.data.energy = old
        lights.append(cd)
    if STUDIO != "light":   # luminile bat doar produsul/ingredientele/textul, nu podeaua lucioasă
        rcv = bpy.data.collections.new("AdReceivers")
        for o in prod + ingr + fx:
            rcv.objects.link(o)
        for l in lights:
            l.light_linking.receiver_collection = rcv
    if DUST and not ALPHA and STUDIO != "light":
        dust(count=60, box=((-3, 3), (-6, 2), (0.0, cz + H)))
    glare = compositor(bloom=0.0 if STUDIO == "light" else 0.15, vignette=0.0 if STUDIO == "light" else 0.3)
    if STUDIO != "light":
        impact_pulse(glare, c5, land1 - int(fps * 0.1), bloom=0.15, peak=0.35, lens_gain=0.0)
    EVENTS.update(intro=sh[1][0] / fps, impact=(land1 - int(fps * 0.1)) / fps, sweep=sh[3][0] / fps,
                  settled=(land1 + int(fps * 0.5)) / fps, cuts=[f0 / fps for f0, _ in sh[1:]])
    s.frame_set(1)
    return prod


def build_lowerthird(a):
    """Lower third 3D pe fundal TRANSPARENT (overlay în editor): plăcuță lucioasă în stânga-jos cu bară de accent,
    TEXT = numele, SUBTITLE = funcția. Intrare: plăcuța se desface de la stânga, bara crește, literele intră decalat,
    funcția apare după; licărire peste; ieșire inversă la final. Camera fixă, frontală."""
    s = bpy.context.scene; end = s.frame_end; fps = s.render.fps
    s.render.film_transparent = True
    s.eevee.use_raytracing = True; s.eevee.use_fast_gi = True
    s.view_settings.look = "AgX - High Contrast"
    studio_world(ensure_hdri(HDRI_ID), strength=1.2)
    bpy.ops.object.camera_add(location=(0, -10, 0), rotation=(math.radians(90), 0, 0))
    cam = bpy.context.object; s.camera = cam; cam.data.lens = 50; cam.data.sensor_fit = "HORIZONTAL"
    vw = 2 * 10 * math.tan(cam.data.angle_x / 2); vh = vw * s.render.resolution_y / s.render.resolution_x
    portrait = vh > vw
    # zona „title safe”: 8% din margini; pe vertical deasupra zonei de descriere/butoane (~35% jos)
    ph = vh * (0.075 if portrait else 0.12)        # înălțimea plăcuței; lățimea se adaptează la nume (mai jos)
    pw = vw * (0.80 if portrait else 0.55)         # lățimea maximă; se micșorează după text
    x0 = -vw / 2 + vw * (0.10 if portrait else 0.08)
    zc = -vh / 2 + vh * (0.38 if portrait else 0.16) + ph / 2
    accent = COLOR or BG_COLOR
    # plăcuța: originea pe muchia din STÂNGA, ca să se desfacă de acolo
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0.5, 0, 0))
    plate = bpy.context.object; plate.name = "Plate"
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    plate.location = (x0, 0.05, zc); plate.scale = (pw, 0.06, ph)
    bev = plate.modifiers.new("Bevel", "BEVEL"); bev.width = 0.03; bev.segments = 4
    pm = material("PlateMat", (0.015, 0.016, 0.02, 1), roughness=0.22)
    pm.node_tree.nodes["Principled BSDF"].inputs["Coat Weight"].default_value = 1.0
    plate.data.materials.append(pm)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.5))
    bar = bpy.context.object; bar.name = "AccentBar"
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)   # originea jos: crește în sus
    bar.location = (x0 - 0.05 * ph, 0.0, zc - ph / 2); bar.scale = (0.06 * ph, 0.08, ph)
    bar.data.materials.append(material("AccentMat", (0, 0, 0, 1), emission=accent, strength=1.0))   # AgX albește emisia puternică
    # textele, aliniate la stânga pe plăcuță
    name = text_3d(TEXT, size=ph * 0.50, extrude=0.03, bevel=0.006, font=font_path())
    name.data.align_x = "LEFT"; name.data.materials.append(hero_material())
    name.location = (x0 + ph * 0.28, -0.03, zc + (ph * 0.14 if SUBTITLE else 0))
    parts = [name]
    sub = None
    if SUBTITLE:
        sub = text_3d(SUBTITLE, size=ph * 0.27, extrude=0.015, bevel=0.003, font=font_path())
        sub.data.align_x = "LEFT"
        sub.data.materials.append(material("SubMat", (0.85, 0.86, 0.9, 1), roughness=0.4))
        sub.location = (x0 + ph * 0.30, -0.03, zc - ph * 0.24)
    bpy.context.view_layer.update()
    maxw = pw - ph * 0.5
    for t in [name] + ([sub] if sub else []):   # textul lung se micșorează să încapă pe plăcuță
        if t.dimensions.x > maxw:
            k = maxw / t.dimensions.x; t.data.size *= k; t.data.extrude *= k; t.data.bevel_depth *= k
    bpy.context.view_layer.update()
    # plăcuța = lățimea textului + margini (nu o bandă lungă cu text mic într-un capăt)
    tw = max([name.dimensions.x] + ([sub.dimensions.x] if sub else []))
    pw = min(pw, max(vw * 0.18, tw + ph * 0.75))
    plate.scale = (pw, 0.06, ph)
    letters = split_letters(name)
    # animație
    f_in = int(fps * 0.25)
    key(plate, "scale", (0.0001,) * 3, f_in - 1, "CONSTANT")
    key(plate, "scale", (0.001, 0.06, ph), f_in, "EXPO", "EASE_OUT"); key(plate, "scale", (pw, 0.06, ph), f_in + int(fps * 0.5), "EXPO", "EASE_OUT")
    key(bar, "scale", (0.06 * ph, 0.08, 0.001), f_in, "EXPO", "EASE_OUT"); key(bar, "scale", (0.06 * ph, 0.08, ph), f_in + int(fps * 0.35), "EXPO", "EASE_OUT")
    st = max(1, min(round(fps * 0.05), round(fps * 0.4 / max(1, len(letters) - 1))))
    settled = stagger_reveal(letters, start=f_in + int(fps * 0.3), stagger=st, dur=int(fps * 0.5),
                             rise=ph * 0.5, tilt=50, style=REVEAL if REVEAL != "rise" else "rise")
    if sub:
        sf = settled - int(fps * 0.2); p = sub.location.copy()
        key(sub, "scale", (0.001,) * 3, sf - 1, "CONSTANT"); key(sub, "scale", (1, 1, 1), sf, "EXPO", "EASE_OUT")
        key(sub, "location", tuple(p - Vector((ph * 0.4, 0, 0))), sf, "EXPO", "EASE_OUT")
        key(sub, "location", tuple(p), sf + int(fps * 0.45), "EXPO", "EASE_OUT")
        EVENTS["subtitle"] = sf / fps
    EVENTS.update(intro=f_in / fps, impact=(settled - int(fps * 0.15)) / fps, settled=settled / fps)
    # lumini: softbox frontal legat de cameră + licărire care trece o dată peste plăcuță
    card = area("Card", (0, 0, 0), 600, (1, .97, .92), shape="RECTANGLE", sx=30, sy=6)
    card.data.use_shadow = False; card.parent = cam; card.location = (0, 0, 4)
    area("Key", (-6, -8, 6), 500, (1, .95, .9), size=4, target=(x0 + pw / 2, 0, zc))
    sw = area("Sweep", (x0 - 2, -3, zc), 600, (1, 1, 1), shape="RECTANGLE", sx=0.1, sy=3, target=(x0 + pw / 2, 0, zc))
    key(sw, "location", (x0 - 2, -3, zc), settled, "SINE", "EASE_IN_OUT")
    key(sw, "location", (x0 + pw + 2, -3, zc), settled + int(fps * 0.8), "SINE", "EASE_IN_OUT")
    EVENTS["sweep"] = (settled + int(fps * 0.3)) / fps
    # ieșire: textele pleacă, apoi plăcuța se strânge spre stânga
    if OUTRO != "none":
        t_out = outro(letters + ([sub] if sub else []), "exit", end - int(fps * 0.25), unit=ph * 1.5, dur_s=0.6)
        fo = end - int(fps * 0.3)
        key(plate, "scale", (pw, 0.06, ph), fo, "EXPO", "EASE_IN"); key(plate, "scale", (0.001, 0.06, ph), end - 1, "EXPO", "EASE_IN")
        key(plate, "scale", (0.0001,) * 3, end, "CONSTANT")   # altfel rămâne „fantoma” bevel-ului
        key(bar, "scale", (0.06 * ph, 0.08, ph), fo, "EXPO", "EASE_IN"); key(bar, "scale", (0.06 * ph, 0.08, 0.001), end - 1, "EXPO", "EASE_IN")
        key(bar, "scale", (0.0001,) * 3, end, "CONSTANT")
        EVENTS["outro"] = t_out
    compositor(bloom=0.12, vignette=0.0, dispersion=0.0)   # aberația pe alfa dă franjuri colorate pe contur
    return [plate] + letters + ([sub] if sub else [])


def _outline(name, pts_2d, radius, color, strength, closed=True):
    """Contur tubular (curbă cu bevel) care se poate „desena” animând bevel_factor_end 0 -> 1."""
    # pe spline CICLIC bevel_factor e ignorat (verificat în 5.2): închidem conturul repetând primul punct
    pts_2d = list(pts_2d) + ([pts_2d[0]] if closed else [])
    cd = bpy.data.curves.new(name, "CURVE"); cd.dimensions = "3D"
    sp = cd.splines.new("POLY"); sp.points.add(len(pts_2d) - 1)
    for p, (x, z) in zip(sp.points, pts_2d):
        p.co = (x, 0, z, 1)
    sp.use_cyclic_u = False
    cd.bevel_depth = radius; cd.bevel_resolution = 3; cd.use_fill_caps = True
    ob = bpy.data.objects.new(name, cd); bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(material(name + "Mat", (0, 0, 0, 1), emission=color, strength=strength))
    return ob


def _round_rect(cx, cz, w, h, r, n=8):
    pts = []
    for (ox, oz, a0) in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for i in range(n + 1):
            t = math.radians(a0 + 90 * i / n)
            pts.append((cx + ox + r * math.cos(t), cz + oz + r * math.sin(t)))
    return pts


def build_endscreen(a):
    """Ecran de final YouTube: TEXT (ex. „Mulțumim pentru vizionare!”) sus, 2 rame 16:9 pentru videoclipuri
    recomandate și un cerc pentru butonul de abonare, pe scena întunecată premium. CAMERA FIXĂ: elementele pe care
    YouTube le pune peste trebuie să cadă exact în rame. Totul e așezat până la ~2 s, apoi doar respiră (praf, licăriri).
    SECONDS 5-20 (YouTube: ultimele 5-20 s ale clipului)."""
    s = bpy.context.scene; end = s.frame_end; fps = s.render.fps
    s.eevee.use_raytracing = True; s.eevee.use_fast_gi = True
    s.view_settings.look = "AgX - High Contrast"; s.render.dither_intensity = 1.0
    studio_world(ensure_hdri(HDRI_ID), strength=1.2)
    if not ALPHA:
        backdrop()
    bpy.ops.object.camera_add(location=(0, -12, 0), rotation=(math.radians(90), 0, 0))
    cam = bpy.context.object; s.camera = cam; cam.data.lens = 50; cam.data.sensor_fit = "HORIZONTAL"
    vw = 2 * 12 * math.tan(cam.data.angle_x / 2); vh = vw * s.render.resolution_y / s.render.resolution_x
    X = lambda u: (u - 0.5) * vw          # u, v în fracțiuni de cadru (v de sus în jos)
    Z = lambda v: (0.5 - v) * vh
    accent = COLOR or BG_COLOR
    # titlul
    title = text_3d(TEXT, size=vh * 0.10, extrude=0.08, bevel=0.012, font=font_path())
    title.data.materials.append(hero_material()); bpy.context.view_layer.update()
    if title.dimensions.x > vw * 0.86:
        k = vw * 0.86 / title.dimensions.x; title.data.size *= k; title.data.extrude *= k; title.data.bevel_depth *= k
    title.location = (0, 0, Z(0.17)); bpy.context.view_layer.update()
    letters = split_letters(title)
    start = int(fps * 0.3)
    st = max(1, min(round(fps * 0.06), round(fps * 0.5 / max(1, len(letters) - 1))))
    settled = stagger_reveal(letters, start=start, stagger=st, dur=int(fps * 0.6), rise=vh * 0.06, style=REVEAL)
    # rame video 16:9 (stânga, dreapta) și cerc de abonare: se „desenează” decalat
    bw = vw * 0.34; bh = bw * 9 / 16; r = bh * 0.06; tube = vh * 0.004
    boxes = [_outline(f"VideoSlot{i}", _round_rect(X(u), Z(0.53), bw, bh, r), tube, accent, 4.0) for i, u in enumerate((0.29, 0.71))]
    circ = _outline("SubscribeSlot", [(X(0.5) + vh * 0.075 * math.cos(math.radians(a_)), Z(0.84) + vh * 0.075 * math.sin(math.radians(a_)))
                                      for a_ in range(0, 360, 6)], tube, accent, 4.0)
    for i, ob in enumerate(boxes + [circ]):
        f0 = start + int(fps * 0.25) + i * int(fps * 0.15); f1 = f0 + int(fps * 0.7)
        ob.data.bevel_factor_end = 0.0; ob.data.keyframe_insert("bevel_factor_end", frame=f0)
        ob.data.bevel_factor_end = 1.0; ob.data.keyframe_insert("bevel_factor_end", frame=f1)
        for fc in (ob.data.animation_data.action and anim_utils.action_get_channelbag_for_slot(
                ob.data.animation_data.action, ob.data.animation_data.action_slot).fcurves or []):
            for kp in fc.keyframe_points:
                kp.interpolation = "EXPO"; kp.easing = "EASE_OUT"
    # placă subtilă în spatele fiecărei rame (ca zona să se citească și fără elementele YouTube)
    for i, u in enumerate((0.29, 0.71)):
        bpy.ops.mesh.primitive_plane_add(size=1, location=(X(u), 0.05, Z(0.53)), rotation=(math.radians(90), 0, 0))
        pl = bpy.context.object; pl.scale = (bw, bh, 1)
        pl.data.materials.append(material(f"SlotFill{i}", (0.02, 0.02, 0.025, 1), roughness=0.3))
    EVENTS.update(intro=start / fps, impact=(settled - int(fps * 0.6 * 0.3)) / fps, settled=settled / fps)
    # titlul stă SUS, departe de axa camerei: reflexia lui cade la h*(2D+c)/D pe planul softbox-ului,
    # deci softbox-ul trebuie să fie înalt (altfel aurul iese olive-închis)
    card = area("Card", (0, 0, 0), 1400, (1, .97, .92), shape="RECTANGLE", sx=30, sy=16)
    card.data.use_shadow = False; card.parent = cam; card.location = (0, 0, 4)
    rcv = bpy.data.collections.new("TitleReceivers")
    for L in letters: rcv.objects.link(L)
    sw = area("Sweep", (-vw, -3, Z(0.17)), 500, (1, 1, 1), shape="RECTANGLE", sx=0.1, sy=3, target=(0, 0, Z(0.17)))
    sw.light_linking.receiver_collection = rcv
    key(sw, "location", (-vw * 0.6, -3, Z(0.17)), settled, "SINE", "EASE_IN_OUT")
    key(sw, "location", (vw * 0.6, -3, Z(0.17)), settled + int(fps * 0.9), "SINE", "EASE_IN_OUT")
    EVENTS["sweep"] = (settled + int(fps * 0.3)) / fps
    if DUST:
        dust(count=35, box=((-vw / 2, vw / 2), (-6, 3), (-vh / 2, vh / 2)))
    compositor(bloom=0.2, vignette=0.0 if ALPHA else 0.35, dispersion=0.0 if ALPHA else 0.012)
    W, H = s.render.resolution_x, s.render.resolution_y
    hf = bh / vh   # înălțimea ramei ca fracție din cadru
    slots = [{"x": round((u - 0.17) * W), "y": round((0.53 - hf / 2) * H), "w": round(0.34 * W), "h": round(hf * H)}
             for u in (0.29, 0.71)]
    print("ANIM3D_ENDSCREEN", json.dumps({"rame_video_px": slots, "cerc_abonare_px": {
        "cx": round(0.5 * W), "cy": round(0.84 * H), "r": round(0.075 * H)}}))
    return letters + boxes + [circ]


# ---------- MOCKUP: telefon / laptop procedural cu ecranul clientului ----------
def _rrect(w, h, r, seg=10):
    """Conturul unui dreptunghi rotunjit centrat în origine: listă de (x, y) în sens trigonometric."""
    r = max(1e-4, min(r, w / 2 - 1e-4, h / 2 - 1e-4)); pts = []
    for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90),
                       (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for i in range(seg + 1):
            t = math.radians(a0 + 90 * i / seg)
            pts.append((cx + r * math.cos(t), cy + r * math.sin(t)))
    return pts


def _slab(name, parent, w, h, r, z0, z1, mat, center=(0, 0), bevel=0.0, uv=False, seg=10):
    """Placă cu colțuri rotunjite în planul XY local, cu fața spre +Z, între z0 și z1 (z0 == z1: doar fața, pentru
    ecran/sticlă). uv=True: fața primește UV 0..1 pe dreptunghiul ei (u spre +X, v spre +Y), deci imaginea nu
    iese oglindită sau rotită. bevel = muchii rotunjite (modificator Bevel cu normale întărite)."""
    import bmesh
    bm = bmesh.new(); pts = _rrect(w, h, r, seg); n = len(pts)
    top = [bm.verts.new((x + center[0], y + center[1], z1)) for x, y in pts]
    ftop = bm.faces.new(top)
    if z1 > z0:
        bot = [bm.verts.new((x + center[0], y + center[1], z0)) for x, y in pts]
        bm.faces.new(bot[::-1])
        for i in range(n):
            bm.faces.new((bot[i], bot[(i + 1) % n], top[(i + 1) % n], top[i]))
    if uv:
        layer = bm.loops.layers.uv.new("UVMap")
        for l in ftop.loops:
            l[layer].uv = ((l.vert.co.x - center[0]) / w + 0.5, (l.vert.co.y - center[1]) / h + 0.5)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    me.materials.append(mat); o.parent = parent
    if bevel > 0 and z1 > z0:
        for p in me.polygons:
            p.use_smooth = True
        b = o.modifiers.new("Bevel", "BEVEL"); b.width = bevel; b.segments = 4
        b.limit_method = "ANGLE"; b.angle_limit = math.radians(50); b.harden_normals = True
    return o


def screen_material(path, screen_aspect, fit="cover"):
    """Ecranul: material EMISIV separat (fără BSDF, deci luminile scenei nu-l spală), cu imaginea sau clipul clientului.
    cover = scalare uniformă în UV până umple ecranul, centrat, surplusul tăiat (fără deformare).
    Video: image.source = 'MOVIE', cadrul filmului = cadrul scenei (auto refresh); la fps diferit, decalajul e animat.
    Peste imagine se adună (liniar) reflexia de sticlă: un gradient calculat din vectorul reflectat, deci se mișcă
    odată cu camera, plus o dâră scurtă animată. AOV-ul "screen" = masca folosită de screen_true_color()."""
    if not path or not os.path.exists(path):
        raise FileNotFoundError(f"SCREEN inexistent: {path!r} (cale absolută către .png/.jpg/.mp4)")
    s = bpy.context.scene
    img = bpy.data.images.load(path)
    movie = os.path.splitext(path)[1].lower() in (".mp4", ".mov", ".webm", ".mkv", ".avi", ".m4v")
    if movie:
        img.source = "MOVIE"
    iw, ih = img.size[0], img.size[1]
    if not iw or not ih:
        raise RuntimeError(f"nu pot citi dimensiunea imaginii/clipului: {path}")
    m = bpy.data.materials.new("ScreenMat"); nt = m.node_tree; nt.nodes.clear()
    N = nt.nodes.new; L = nt.links.new
    tc = N("ShaderNodeTexCoord"); mp = N("ShaderNodeMapping"); tex = N("ShaderNodeTexImage")
    tex.image = img; tex.extension = "CLIP"; tex.interpolation = "Cubic"
    ia = iw / ih; sx = sy = 1.0
    if fit not in ("cover", "contain"):
        raise ValueError(f"SCREEN_FIT necunoscut: {fit!r} (cover | contain)")
    if (ia > screen_aspect) == (fit == "cover"):
        sx = screen_aspect / ia    # cover + imagine mai lată: taie stânga/dreapta; contain + imagine mai îngustă: benzi laterale
    else:
        sy = ia / screen_aspect    # cover + imagine mai înaltă: taie sus/jos; contain + imagine mai lată: benzi sus/jos
    mp.inputs["Scale"].default_value = (sx, sy, 1); mp.inputs["Location"].default_value = (0.5 - sx / 2, 0.5 - sy / 2, 0)
    L(tc.outputs["UV"], mp.inputs["Vector"]); L(mp.outputs["Vector"], tex.inputs["Vector"])
    info = {"movie": movie, "size": [iw, ih], "uv_scale": [round(sx, 4), round(sy, 4)]}
    if movie:
        iu = tex.image_user; iu.use_auto_refresh = True; iu.use_cyclic = True; iu.frame_start = 1; iu.frame_offset = 0
        iu.frame_duration = max(1, img.frame_duration)
        clip = bpy.data.movieclips.load(path); vfps = clip.fps or s.render.fps; bpy.data.movieclips.remove(clip)
        info.update(video_fps=round(vfps, 3), video_frames=img.frame_duration)
        if abs(vfps - s.render.fps) > 0.01:   # alt fps: cadrul filmului = timpul scenei, nu numărul cadrului
            for f in range(s.frame_start, s.frame_end + 1):
                iu.frame_offset = int((f - 1) * vfps / s.render.fps) - (f - 1)
                iu.keyframe_insert("frame_offset", frame=f)
            for fc in _fcurves(nt):
                for kp in fc.keyframe_points: kp.interpolation = "CONSTANT"
    # reflexia de sticlă: R = reflect(-Incoming, Normal); gradient liniar de-a lungul direcției G (setată în glare_aim)
    geo = N("ShaderNodeNewGeometry")
    neg = N("ShaderNodeVectorMath"); neg.operation = "SCALE"; neg.inputs["Scale"].default_value = -1.0
    refl = N("ShaderNodeVectorMath"); refl.operation = "REFLECT"
    dot = N("ShaderNodeVectorMath"); dot.operation = "DOT_PRODUCT"
    gl = N("ShaderNodeMapRange"); gl.interpolation_type = "SMOOTHSTEP"
    gl.inputs["To Min"].default_value = 0.0; gl.inputs["To Max"].default_value = 0.0
    L(geo.outputs["Incoming"], neg.inputs[0]); L(neg.outputs["Vector"], refl.inputs[0]); L(geo.outputs["Normal"], refl.inputs[1])
    L(refl.outputs["Vector"], dot.inputs[0]); L(dot.outputs["Value"], gl.inputs["Value"])
    # dâra animată: bandă oblică în UV, poziția ei e o valoare cu chei (shine_sweep)
    sep = N("ShaderNodeSeparateXYZ"); L(tc.outputs["UV"], sep.inputs[0])
    diag = N("ShaderNodeMath"); diag.operation = "MULTIPLY_ADD"; diag.inputs[1].default_value = 0.35
    L(sep.outputs["Y"], diag.inputs[0]); L(sep.outputs["X"], diag.inputs[2])
    pos = N("ShaderNodeValue"); pos.outputs[0].default_value = -1.0
    dd = N("ShaderNodeMath"); dd.operation = "SUBTRACT"; L(diag.outputs[0], dd.inputs[0]); L(pos.outputs[0], dd.inputs[1])
    ab = N("ShaderNodeMath"); ab.operation = "ABSOLUTE"; L(dd.outputs[0], ab.inputs[0])
    sh = N("ShaderNodeMapRange"); sh.interpolation_type = "SMOOTHERSTEP"
    sh.inputs["From Min"].default_value = 0.0; sh.inputs["From Max"].default_value = 0.28
    sh.inputs["To Min"].default_value = SCREEN_SHINE; sh.inputs["To Max"].default_value = 0.0
    L(ab.outputs[0], sh.inputs["Value"])
    add = N("ShaderNodeMath"); add.operation = "ADD"; L(gl.outputs["Result"], add.inputs[0]); L(sh.outputs["Result"], add.inputs[1])
    tot = N("ShaderNodeVectorMath"); tot.operation = "ADD"
    L(tex.outputs["Color"], tot.inputs[0]); L(add.outputs[0], tot.inputs[1])
    em = N("ShaderNodeEmission"); em.inputs["Strength"].default_value = 1.0   # 1.0 = culoarea sursei, fără expunere
    out = N("ShaderNodeOutputMaterial"); L(tot.outputs["Vector"], em.inputs["Color"]); L(em.outputs[0], out.inputs["Surface"])
    aov = N("ShaderNodeOutputAOV"); aov.aov_name = "screen"; aov.inputs["Value"].default_value = 1.0
    vl = bpy.context.view_layer
    if "screen" not in [x.name for x in vl.aovs]:
        x = vl.aovs.add(); x.name = "screen"; x.type = "VALUE"
    return m, {"dot": dot, "range": gl, "pos": pos, "info": info}


def glare_aim(screen, cam, nodes, strength, frame):
    """Potrivește gradientul de reflexie pe poza de la `frame`: maxim în colțul dreapta-sus al ecranului, zero pe la 50%
    din diagonală spre stânga-jos. E calculat în spațiul vectorului reflectat, deci alunecă atunci când se mișcă camera
    sau dispozitivul (ca o reflexie reală), dar puterea lui e plafonată la `strength`: ecranul rămâne lizibil."""
    s = bpy.context.scene; s.frame_set(frame); bpy.context.view_layer.update()
    mw = screen.matrix_world
    xs = [c[0] for c in screen.bound_box]; ys = [c[1] for c in screen.bound_box]; z = screen.bound_box[0][2]
    tr = mw @ Vector((max(xs), max(ys), z)); bl = mw @ Vector((min(xs), min(ys), z))
    n = (mw.to_3x3() @ Vector((0, 0, 1))).normalized(); eye = cam.matrix_world.translation
    def refl(p):
        i = (eye - p).normalized()
        return 2 * n.dot(i) * n - i
    r_hot, r_lo = refl(tr), refl(tr.lerp(bl, 0.5))
    g = (r_hot - r_lo).normalized()
    nodes["dot"].inputs[1].default_value = g
    nodes["range"].inputs["From Min"].default_value = r_lo.dot(g); nodes["range"].inputs["From Max"].default_value = r_hot.dot(g)
    nodes["range"].inputs["To Max"].default_value = strength


def screen_true_color(look="AgX - High Contrast"):
    """Culorile ecranului NU trec prin AgX. AgX comprimă și desaturează culorile vii (roșu pur 255,0,0 iese ~200,60,50),
    deci un UI ar arăta spălat. Soluția: view transform-ul scenei devine Standard, iar AgX + look se aplică în compositor
    (nodul Convert to Display, apoi înapoi în liniar) pe tot cadrul, MAI PUȚIN pe pixelii ecranului (masca = AOV-ul
    "screen", antialiasată, cu motion blur). Restul scenei iese identic cu o randare AgX obișnuită; ecranul iese cu
    sRGB-ul sursei. Se apelează DUPĂ compositor(); pixelii ecranului se iau din randarea brută (fără bloom, vignetă,
    aberație), ca textul din UI să rămână curat."""
    s = bpy.context.scene; ng = s.compositing_node_group
    N = ng.nodes.new; L = ng.links.new
    out = next(n for n in ng.nodes if n.bl_idname == "NodeGroupOutput")
    rl = next(n for n in ng.nodes if n.bl_idname == "CompositorNodeRLayers"); rl.scene = s
    bpy.context.view_layer.update()
    src = out.inputs[0].links[0].from_socket
    disp = N("CompositorNodeConvertToDisplay")
    disp.display_settings.display_device = "sRGB"
    disp.view_settings.view_transform = "AgX"; disp.view_settings.look = look
    back = N("CompositorNodeConvertColorSpace"); back.from_color_space = "sRGB"; back.to_color_space = "Linear Rec.709"
    mix = N("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MIX"; mix.clamp_factor = True
    sock = lambda node, ident: next(i for i in node.inputs if i.identifier == ident)
    L(src, disp.inputs["Image"]); L(disp.outputs["Image"], back.inputs["Image"])
    L(rl.outputs["screen"], sock(mix, "Factor_Float"))
    L(back.outputs["Image"], sock(mix, "A_Color")); L(rl.outputs["Image"], sock(mix, "B_Color"))
    L(next(o for o in mix.outputs if o.identifier == "Result_Color"), out.inputs[0])
    s.view_settings.view_transform = "Standard"; s.view_settings.look = "None"


def refit_camera(cam, tgt, meshes, fill_w, fill_h, v_off=0.0, u_off=0.0, frame=None, rounds=4):
    """Încadrare pe proiecția REALĂ a vârfurilor (nu pe bbox-ul din față): mută camera + ținta până când obiectele sunt
    centrate (v_off = cât mai sus de centru, în fracțiuni de cadru) și ocupă cel mult fill_w din lățime și fill_h din
    înălțime. Ține la orice format și la obiecte văzute de sus (laptopul: baza vine spre cameră)."""
    s = bpy.context.scene
    if frame: s.frame_set(frame)
    asp = s.render.resolution_x / s.render.resolution_y
    for _ in range(rounds):
        bpy.context.view_layer.update()
        pts = []
        for o in meshes:
            vs = o.data.vertices; st = max(1, len(vs) // 2000)
            pts += [world_to_camera_view(s, cam, o.matrix_world @ vs[i].co) for i in range(0, len(vs), st)]
        us, vs_, zs = [p.x for p in pts], [p.y for p in pts], [p.z for p in pts]
        depth = sum(zs) / len(zs)
        vis_w = 2 * depth * math.tan(cam.data.angle_x / 2); vis_h = vis_w / asp
        mw = cam.matrix_world
        d = (mw.col[0].xyz.normalized() * ((min(us) + max(us)) / 2 - 0.5 - u_off) * vis_w
             + mw.col[1].xyz.normalized() * ((min(vs_) + max(vs_)) / 2 - 0.5 - v_off) * vis_h)
        cam.location += d; tgt.location += d
        k = max((max(us) - min(us)) / fill_w, (max(vs_) - min(vs_)) / fill_h)
        cam.location = tgt.location + (cam.location - tgt.location) * k
    bpy.context.view_layer.update()


def mockup_materials():
    # pe alb, aluminiul deschis se pierde în fundal: corpul implicit e mai închis în STUDIO = "light"
    dark_body = STUDIO == "light"
    body = COLOR or ((0.30, 0.30, 0.31, 1) if DEVICE == "phone" else ((0.30, 0.31, 0.33, 1) if dark_body else (0.62, 0.63, 0.65, 1)))
    glass = material("DevGlass", (0.004, 0.004, 0.005, 1), roughness=0.06, specular=0.35)
    return {"frame": material("DevMetal", body, metallic=1.0, roughness=0.3),
            "glass": glass,
            "lens": material("DevLens", (0.01, 0.012, 0.03, 1), metallic=0.6, roughness=0.08),
            "bezel": material("DevBezel", (0.004, 0.004, 0.005, 1), roughness=0.45, specular=0.15),  # rama laptopului: mată
            "key": material("DevKey", (0.01, 0.01, 0.012, 1), roughness=0.7, specular=0.06),
            "well": material("DevWell", (0.004, 0.004, 0.004, 1), roughness=0.7),
            "pad": material("DevPad", tuple(c * 0.9 for c in body[:3]) + (1,), metallic=1.0, roughness=0.4)}


def build_phone(root, mats):
    """Telefon modern în mm (71 x 147 x 8), cu fața spre +Z local: ramă metalică cu muchii rotunjite, sticlă neagră,
    ecran 64,6 x 140,6 (≈19,5:9) cu colțuri rotunjite, insulă de cameră, butoane laterale. Întoarce (piese, ecran)."""
    W, H, T = 71.0, 147.0, 8.0; f = T / 2
    sw, sh = W - 6.4, H - 6.4
    scr_mat, nodes = screen_material(SCREEN, sw / sh, SCREEN_FIT)
    parts = [_slab("PhoneBody", root, W, H, 11.5, -f, f, mats["frame"], bevel=1.5),
             _slab("PhoneGlass", root, W - 3.4, H - 3.4, 9.8, f - 0.1, f + 0.15, mats["glass"])]
    screen = _slab("Screen", root, sw, sh, 8.3, f + 0.2, f + 0.2, scr_mat, uv=True)
    iy = sh / 2 - 2.9 - 4.6
    parts += [screen,
              _slab("Island", root, 19.5, 5.8, 2.9, f + 0.25, f + 0.25, mats["glass"], center=(0, iy)),
              _slab("Lens", root, 3.0, 3.0, 1.5, f + 0.3, f + 0.3, mats["lens"], center=(5.6, iy)),
              _slab("BtnPower", root, 1.6, 15, 0.7, -1.3, 1.3, mats["frame"], center=(W / 2 + 0.1, 18), bevel=0.3),
              _slab("BtnVolUp", root, 1.6, 9, 0.7, -1.3, 1.3, mats["frame"], center=(-W / 2 - 0.1, 27), bevel=0.3),
              _slab("BtnVolDn", root, 1.6, 9, 0.7, -1.3, 1.3, mats["frame"], center=(-W / 2 - 0.1, 15), bevel=0.3),
              _slab("BtnAction", root, 1.6, 5, 0.7, -1.3, 1.3, mats["frame"], center=(-W / 2 - 0.1, 41), bevel=0.3)]
    return parts, screen, nodes, None, (0, 0, f + 0.2)


def build_laptop(root, mats, open_deg=110.0):
    """Laptop 16:10 în mm (312 x 212, bază 10, capac 5): bază din aluminiu cu tastatură (taste reale, scunde), trackpad,
    balama, capac cu ramă subțire și ecran 302 x 188,75. Capacul e copilul unui Empty aflat pe axa balamalei
    (rotation_euler.x = 180° - unghiul de deschidere), deci poate fi animat. Întoarce (piese, ecran, ..., capac)."""
    import bmesh
    from mathutils import Matrix
    W, D, TB, TL = 312.0, 212.0, 10.0, 5.0
    parts = [_slab("Base", root, W, D, 10, 0, TB, mats["frame"], bevel=1.2),
             _slab("KeyWell", root, 272, 112, 4, TB + 0.06, TB + 0.06, mats["well"], center=(0, 36)),
             _slab("Trackpad", root, 124, 68, 5, TB + 0.06, TB + 0.06, mats["pad"], center=(0, -61))]
    # tastatura: 6 rânduri de cutii scunde (un singur mesh); lățimile în unități de tastă, 14 unități pe rând
    rows = [([1] * 14, 9.0), ([1] * 12 + [2], 19.0), ([1.5] + [1] * 11 + [1.5], 19.0), ([1.75] + [1] * 10 + [2.25], 19.0),
            ([2.25] + [1] * 9 + [2.75], 19.0), ([1.25, 1.25, 1.25, 5.5, 1.25, 1.25, 1, 1.25], 19.0)]
    bm = bmesh.new(); y = 36 + 56 - 3.0; gap = 2.4; z0, z1 = TB + 0.06, TB + 1.0
    for widths, hrow in rows:
        x = -133.0
        for wu in widths:
            xa, xb, ya, yb = x + gap / 2, x + wu * 19.0 - gap / 2, y - hrow + gap / 2, y - gap / 2
            v = [bm.verts.new(p) for p in ((xa, ya, z0), (xb, ya, z0), (xb, yb, z0), (xa, yb, z0),
                                           (xa, ya, z1), (xb, ya, z1), (xb, yb, z1), (xa, yb, z1))]
            for q in ((4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
                bm.faces.new([v[i] for i in q])
            x += wu * 19.0
        y -= hrow
    me = bpy.data.meshes.new("Keys"); bm.to_mesh(me); bm.free()
    keys = bpy.data.objects.new("Keys", me); bpy.context.scene.collection.objects.link(keys)
    me.materials.append(mats["key"]); keys.parent = root; parts.append(keys)
    bm = bmesh.new()   # balamaua: cilindru pe axa X, la muchia din spate
    bmesh.ops.create_cone(bm, cap_ends=True, segments=32, radius1=5.0, radius2=5.0, depth=250.0)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "Y"))
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, D / 2 - 1.0, TB + 1.0))
    for fc in bm.faces:
        fc.smooth = len(fc.verts) == 4
    me = bpy.data.meshes.new("Hinge"); bm.to_mesh(me); bm.free()
    hinge = bpy.data.objects.new("Hinge", me); bpy.context.scene.collection.objects.link(hinge)
    me.materials.append(mats["well"]); hinge.parent = root; parts.append(hinge)
    lid = bpy.data.objects.new("Lid", None); bpy.context.scene.collection.objects.link(lid)
    lid.parent = root; lid.location = (0, D / 2, TB + 2.0); lid.rotation_euler = (math.radians(180 - open_deg), 0, 0)
    sw, sh = 302.0, 188.75; cy = D - 6.0 - sh / 2
    scr_mat, nodes = screen_material(SCREEN, sw / sh, SCREEN_FIT)
    screen = _slab("Screen", lid, sw, sh, 3.0, 0.4, 0.4, scr_mat, center=(0, cy), uv=True)
    parts += [_slab("LidShell", lid, W, D, 10, -TL, 0, mats["frame"], center=(0, D / 2), bevel=1.0),
              _slab("LidGlass", lid, W - 3, D - 3, 8.5, 0, 0.3, mats["bezel"], center=(0, D / 2)),
              screen,
              _slab("Webcam", lid, 2.6, 2.6, 1.3, 0.45, 0.45, mats["lens"], center=(0, D - 3.0))]
    return parts, screen, nodes, lid, (0, cy, 0.4)


def build_mockup(a):
    """Mockup de dispozitiv cu ecranul clientului (lansări de aplicații / site-uri). Dispozitivul intră rotindu-se din 3/4
    spre aproape frontal (QUINT ease-out, ~1,5 s), apoi ecranul stă lizibil până la final, cu o derivă lentă a camerei.
    STUDIO dark = podea neagră lucioasă care reflectă ecranul (ca la reveal); light = cicloramă albă (ca la turntable).
    Ecranul e emisiv și NU trece prin AgX (screen_true_color); reflexia de sticlă e un gradient plafonat (glare_aim)."""
    s = bpy.context.scene; end = s.frame_end; fps = s.render.fps
    light = STUDIO == "light"; phone = DEVICE == "phone"
    if DEVICE not in ("phone", "laptop"):
        raise ValueError(f"DEVICE necunoscut: {DEVICE!r} (phone | laptop)")
    s.eevee.use_raytracing = True
    s.eevee.ray_tracing_options.resolution_scale = "1"
    s.eevee.use_fast_gi = True
    s.render.dither_intensity = 1.0
    look = "AgX - Medium High Contrast" if light else "AgX - High Contrast"
    # dispozitivul: Pivot (animat, pe podea) -> Root (mm -> unități de scenă) -> piese
    pivot = bpy.data.objects.new("Pivot", None); s.collection.objects.link(pivot)
    root = bpy.data.objects.new("Device", None); s.collection.objects.link(root); root.parent = pivot
    mats = mockup_materials()
    if phone:
        k = 2.6 / 147.0
        parts, screen, nodes, lid, focus_at = build_phone(root, mats)
        root.rotation_euler = (math.radians(90), 0, 0); root.location = (0, 0, 147.0 / 2 * k + 0.004)
    else:
        k = 4.0 / 312.0
        parts, screen, nodes, lid, focus_at = build_laptop(root, mats)
        root.location = (0, -0.35, 0.004)
    root.scale = (k, k, k)
    focus = bpy.data.objects.new("Focus", None); s.collection.objects.link(focus)
    focus.parent = screen.parent; focus.location = focus_at
    yaw_end = math.radians(-7.0); yaw_hold = math.radians(-9.5); yaw_in = math.radians(-62.0 if phone else -52.0)
    pivot.rotation_euler = (0, 0, yaw_end)
    bpy.context.view_layer.update()
    # studio
    if light:
        studio_world(ensure_hdri(HDRI_ID), strength=1.0, bg=(0.9, 0.9, 0.9, 1), horizon=(-1.0, -0.5))
        cy = cyclorama((0.9, 0.9, 0.9, 1), roughness=0.55)
        bsdf = cy.data.materials[0].node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Emission Color"].default_value = (1, 1, 1, 1); bsdf.inputs["Emission Strength"].default_value = BG_EMIT
        cs = contact_shadow(pivot, parts, reach=1.1 if phone else 0.22)
        if cs:   # umbra se rotește odată cu dispozitivul; la laptop e ovală, pe forma bazei (altfel pare un disc sub el)
            cs.parent = pivot; cs.scale = (1, 1 if phone else 0.74, 1)
    else:
        studio_world(ensure_hdri(HDRI_ID), strength=1.0)
        backdrop()
        bpy.ops.object.select_all(action="DESELECT")
        bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, 0))
        bpy.context.object.data.materials.append(material("Floor", (0.004, 0.004, 0.005, 1), roughness=0.12, specular=0.35))
    # camera: 85 mm; fit_camera pune ținta și Track To, refit_camera încadrează pe proiecția reală, în poza FINALĂ
    bpy.ops.object.camera_add(); cam = bpy.context.object; s.camera = cam; cam.data.lens = 85; cam.data.clip_start = 0.5
    ratio = s.render.resolution_x / s.render.resolution_y
    tgt = fit_camera(cam, parts, fill=0.6, height=0.05 if phone else 0.85, frame=1)
    if ratio > 1.2:      fw, fh, voff, uoff = (0.80, 0.74, 0.0, 0.0)
    elif ratio > 0.9:    fw, fh, voff, uoff = (0.76, 0.72, 0.0, 0.0)
    else:                fw, fh, voff, uoff = (0.68, 0.60, 0.045, -0.03)   # vertical: ușor în sus și spre stânga
    refit_camera(cam, tgt, parts, fw, fh, voff, uoff)                     # (jos și în dreapta stă interfața aplicației)
    cam.data.dof.use_dof = True; cam.data.dof.focus_object = focus; cam.data.dof.aperture_fstop = 4.0
    cam.data.dof.aperture_blades = 7
    # animația dispozitivului
    f_in = max(2, int(fps * 0.15)); settled = int(fps * 1.6)
    key(pivot, "rotation_euler", (0, 0, yaw_in), 1, "CONSTANT")
    key(pivot, "rotation_euler", (0, 0, yaw_in), f_in, "CUBIC", "EASE_OUT")
    key(pivot, "rotation_euler", (0, 0, yaw_hold), settled, "SINE", "EASE_IN_OUT")
    key(pivot, "rotation_euler", (0, 0, yaw_end), end, "SINE", "EASE_IN_OUT")
    key(pivot, "scale", (0.84,) * 3, 1, "CONSTANT")
    key(pivot, "scale", (0.84,) * 3, f_in, "QUART", "EASE_OUT")
    key(pivot, "scale", (1, 1, 1), settled, "QUART", "EASE_OUT")
    if lid:   # laptopul se deschide în timp ce intră
        r1 = lid.rotation_euler.x
        key(lid, "rotation_euler", (math.radians(180 - 32), 0, 0), f_in, "QUART", "EASE_OUT")
        key(lid, "rotation_euler", (r1, 0, 0), settled - int(fps * 0.1), "QUART", "EASE_OUT")
    impact_f = settled - int(fps * 0.4)   # momentul în care mișcarea e practic gata (CUBIC: ~98% din drum)
    EVENTS.update(intro=f_in / fps, impact=impact_f / fps, sweep=(impact_f - int(fps * 0.1)) / fps, settled=settled / fps)
    if not light:   # intră din negru
        for f, v in ((1, -8.0), (max(3, int(fps * 0.25)), 0.0)):
            s.view_settings.exposure = v; s.view_settings.keyframe_insert("exposure", frame=f)
    # dâra de lumină pe ecran, la așezare
    if SCREEN_SHINE > 0:
        sock = nodes["pos"].outputs[0]
        for f, v in ((impact_f - int(fps * 0.25), -0.35), (impact_f + int(fps * 0.55), 1.7)):
            sock.default_value = v; sock.keyframe_insert("default_value", frame=f)
    # camera: push-in lent + orbită mică, pe toată durata (SINE: încă se mișcă în timpul ținerii)
    from mathutils import Matrix
    c = tgt.location.copy(); p0 = cam.location.copy() - c
    key(cam, "location", tuple(c + (Matrix.Rotation(math.radians(5), 3, "Z") @ p0) * 1.12), 1, "SINE", "EASE_OUT")
    key(cam, "location", tuple(c + p0), end, "SINE", "EASE_OUT")
    # lumini. Softbox-ul legat de cameră stă SUS (nu drept în spatele ei): sticla neagră a ramei privește drept spre
    # cameră și ar oglindi un softbox aflat pe axă ca un văl gri.
    dist = p0.length; elev = math.radians(24 if phone else 50)
    card = area("Card", (0, 0, 0), CARD_W if CARD_W is not None else (260 if light else 420), (1, .97, .92),
                shape="RECTANGLE", sx=10, sy=3)
    card.data.use_shadow = False; card.parent = cam
    card.location = (0, math.tan(elev) * (dist + 4), 4); card.rotation_euler = (-elev, 0, 0)
    h = 2.6 if phone else 2.7
    if light:
        lights = [area("Key", (-3.5, -4, 4.5), 900, (1, .96, .9), shape="RECTANGLE", sx=3, sy=2, target=(0, 0, h * 0.5)),
                  area("Fill", (4, -3, 2), 350, (.9, .95, 1), shape="RECTANGLE", sx=3, sy=3, target=(0, 0, h * 0.4)),
                  area("Rim", (0, 4.5, h + 2.5), 600, (1, 1, 1), shape="RECTANGLE", sx=4, sy=1, target=(0, 0, h * 0.6)),
                  area("Top", (0, 0, h + 4), 300, (1, 1, 1), shape="RECTANGLE", sx=3, sy=3, target=(0, 0, 0))]
    else:
        lights = [area("Key", (-5, -6, 5), 500, (1, .96, .9), shape="RECTANGLE", sx=3, sy=3, target=(0, 0, h * 0.5)),
                  area("TopStrip", (0, -1.5, 5.5), 700, (1, .97, .9), shape="RECTANGLE", sx=8, sy=0.4, target=(0, 0, h * 0.5)),
                  area("RimL", (-4, 4, 5), 2000, (0.45, 0.62, 1.0), size=1.5, target=(0, 0, h * 0.5)),
                  area("RimR", (4, 4, 5), 2000, (1.0, 0.62, 0.35), size=1.5, target=(0, 0, h * 0.5))]
        sw = area("Sweep", (-7, -2.5, h * 0.5), SWEEP_W if SWEEP_W is not None else 350, (1, 1, 1),
                  shape="RECTANGLE", sx=0.08, sy=3, target=(0, 0, h * 0.5))
        key(sw, "location", (-7, -2.5, h * 0.5), impact_f - int(fps * 0.3), "SINE", "EASE_IN_OUT")
        key(sw, "location", (7, -2.5, h * 0.5), impact_f + int(fps * 0.4), "SINE", "EASE_IN_OUT")
        lights.append(sw)
        for l in lights[1:]:   # doar Key face umbre: restul ar umple degeaba bufferul de umbre („Shadow buffer full”)
            l.data.use_shadow = False
    # light linking. Sticla din planul ecranului (ramă neagră, insulă, lentile) NU primește softbox-ul, iar pe negru
    # nicio lumină: ar oglindi-o ca un văl gri (insula devine albă în timpul rotației), în timp ce ecranul emisiv de
    # lângă ea nu. Pe negru luminile bat DOAR restul dispozitivului: podeaua lucioasă rămâne neagră și reflectă ecranul.
    glassy = ("PhoneGlass", "Island", "Lens", "LidGlass", "Webcam")
    matte = bpy.data.collections.new("NoGlassReceivers")
    for p in parts:
        if p.name not in glassy: matte.objects.link(p)
    for l in [card] + ([] if light else lights):
        l.light_linking.receiver_collection = matte
    glare_aim(screen, cam, nodes, SCREEN_GLARE, end)
    # post: bloom doar pe negru; aberație mică (textul din jur rămâne curat); ecranul sare peste AgX
    if light:
        compositor(bloom=0.0, vignette=0.0, dispersion=0.004)
    else:
        impact_pulse(compositor(bloom=0.12, vignette=0.3, dispersion=0.006), cam, impact_f, bloom=0.12, peak=0.24, lens_gain=0.02)
    screen_true_color(look)
    if OUTRO == "fade":
        EVENTS["outro"] = outro([], "fade", end)
    # diagnostic: colțurile ecranului în cadru la final (fracțiuni: x de la stânga, y de SUS) -> verificare de culoare
    s.frame_set(end); bpy.context.view_layer.update()
    xs = [q[0] for q in screen.bound_box]; ys = [q[1] for q in screen.bound_box]; z = screen.bound_box[0][2]
    def px(x, y):
        v = world_to_camera_view(s, cam, screen.matrix_world @ Vector((x, y, z)))
        return [round(v.x, 4), round(1 - v.y, 4)]
    print("ANIM3D_SCREEN", json.dumps({"tl": px(min(xs), max(ys)), "tr": px(max(xs), max(ys)), "bl": px(min(xs), min(ys)),
                                       "br": px(max(xs), min(ys)), **nodes["info"]}))
    if (end - settled) / fps < 2.0:
        print("ANIM3D_WARN", json.dumps({"problem": f"ecranul stă lizibil doar {(end - settled) / fps:.2f}s; "
                                         "mărește SECONDS la 4-5 s"}, ensure_ascii=False))
    return parts


def build_particles(hero, start, unit, floor_z, seed=11):
    """MODE = "particles": mii de particule luminoase pornesc dintr-un nor (sau vârtej), zboară decalat și se așază pe
    suprafața textului/logo-ului; în ultimele ~0,4 s textul solid apare (alfa 0 -> 1) și particulele se sting.
    Restul scenei (podea, lumini, cameră, bloom) e rețeta de reveal; aici se face doar animația.
    Determinist, fără simulare/bake: punctele și pozițiile de start se aleg o dată în Python (seed fix) și stau ca
    atribute pe un mesh de puncte; Geometry Nodes calculează poziția DOAR din numărul cadrului (Scene Time), deci
    orice cadru randat singur iese la fel. Întoarce cadrul în care textul e complet apărut (settled)."""
    import random
    s = bpy.context.scene; fps = s.render.fps; r = random.Random(seed)
    spread, dur = fps * 0.8, fps * 1.2              # decalajul plecărilor și durata zborului unei particule
    land = start + spread + dur                      # ultima particulă a ajuns
    x0, x1 = round(land - fps * 0.3), round(land + fps * 0.1)   # crossfade particule -> text solid (0,4 s)
    # 1) puncte pe suprafață, în coordonate de LUME: fețele spre cameră dens, lateralele rar, spatele deloc
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(hero.evaluated_get(dg)); me.transform(hero.matrix_world)
    me.calc_loop_triangles()
    tris = [(t.area * (1.0 if t.normal.y < -0.5 else 0.3 if t.normal.y < 0.3 else 0.0),
             [me.vertices[i].co.copy() for i in t.vertices]) for t in me.loop_triangles]
    if sum(w for w, _ in tris) <= 0:
        raise ValueError("particles: textul/logo-ul nu are suprafață spre cameră")
    xs = [v.co.x for v in me.vertices]; cx, wx = (min(xs) + max(xs)) / 2, max(max(xs) - min(xs), 1e-6)
    zs = [v.co.z for v in me.vertices]; cz = (min(zs) + max(zs)) / 2
    bpy.data.meshes.remove(me)
    n = max(1, int(PARTICLES)); R = 1.15 * wx       # norul e cât cadrul, oricât de lat e textul (SVG = 3, text = 4,4)
    pts, st, t0, sz = [], [], [], []
    for (_, (a, b, c)) in r.choices(tris, weights=[w for w, _ in tris], k=n):
        u, v = r.random(), r.random()
        if u + v > 1: u, v = 1 - u, 1 - v
        p = a + (b - a) * u + (c - a) * v
        if PARTICLE_STYLE == "vartej":               # inel larg în jurul axei verticale, la toate înălțimile
            ang, rad = r.uniform(0, 2 * math.pi), R * r.uniform(0.45, 1.0)
            q = Vector((cx + rad * math.cos(ang), rad * math.sin(ang) * 0.8, floor_z + R * r.uniform(0.02, 0.75)))
        else:                                        # nor: fiecare particulă pleacă dintr-un punct la întâmplare
            q = Vector((cx + R * r.uniform(-1, 1), R * r.uniform(-1.4, 0.6), floor_z + R * r.uniform(0.02, 0.8)))
        pts.append(p); st.append(q); sz.append(0.011 * unit * r.uniform(0.55, 1.45))
        # plecare decalată: parțial de la stânga la dreapta (textul „se scrie”), parțial la întâmplare
        t0.append(start + spread * (0.45 * (p.x - cx + wx / 2) / wx + 0.55 * r.random()))
    pm = bpy.data.meshes.new("Particles"); pm.from_pydata([tuple(p) for p in pts], [], [])
    for nm, typ, fld, vals in (("p_start", "FLOAT_VECTOR", "vector", [x for q in st for x in q]),
                               ("p_t0", "FLOAT", "value", t0), ("p_size", "FLOAT", "value", sz),
                               ("p_keep", "FLOAT", "value", [1.0 if i < n * PARTICLE_KEEP else 0.0 for i in range(n)])):
        pm.attributes.new(nm, typ, "POINT").data.foreach_set(fld, vals)
    po = bpy.data.objects.new("Particles", pm); s.collection.objects.link(po)
    po.parent = hero; po.matrix_parent_inverse = hero.matrix_world.inverted()   # pleacă odată cu textul la OUTRO
    # 2) materialul particulelor: emisiv (prinde bloom), în culoarea materialului ales
    col = {"crom": (0.75, 0.88, 1.0, 1), "neon": COLOR or (0.0, 0.85, 1.0, 1), "plastic": COLOR or (1.0, 0.25, 0.2, 1),
           "sticla": (0.6, 0.85, 1.0, 1), "iridescent": (0.7, 0.5, 1.0, 1)}.get(MATERIAL, (1.0, 0.45, 0.08, 1))
    pmat = material("ParticleMat", col, metallic=0.0, roughness=0.4, emission=col, strength=2.5)
    # 3) Geometry Nodes: poziție = funcție de cadru
    ng = bpy.data.node_groups.new("ParticlesGN", "GeometryNodeTree")
    ng.interface.new_socket("Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
    ng.interface.new_socket("Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    N, L = ng.nodes.new, ng.links.new
    def val(x, sock):
        if isinstance(x, (int, float)): sock.default_value = x
        else: L(x, sock)
    def m(op, a, b=0.0, c=0.0, clamp=False):
        nd = N("ShaderNodeMath"); nd.operation = op; nd.use_clamp = clamp
        val(a, nd.inputs[0]); val(b, nd.inputs[1]); val(c, nd.inputs[2]); return nd.outputs[0]
    def attr(name, typ="FLOAT"):
        nd = N("GeometryNodeInputNamedAttribute"); nd.data_type = typ; nd.inputs["Name"].default_value = name
        return nd.outputs["Attribute"]
    frame = N("GeometryNodeInputSceneTime").outputs["Frame"]
    p = m("DIVIDE", m("SUBTRACT", frame, attr("p_t0")), dur, clamp=True)          # progresul particulei, 0..1
    q = m("POWER", m("SUBTRACT", 1.0, m("POWER", p, 1.5)), 3.0)                    # cât mai are de zburat (1 -> 0):
    mix = N("ShaderNodeMix"); mix.data_type = "VECTOR"                             # pornește lin, frânează lung
    sock = lambda nd, ident: next(i for i in nd.inputs if i.identifier == ident)
    L(q, sock(mix, "Factor_Float")); L(N("GeometryNodeInputPosition").outputs[0], sock(mix, "A_Vector"))
    L(attr("p_start", "FLOAT_VECTOR"), sock(mix, "B_Vector"))
    # rotire în jurul axei verticale a textului, care se stinge la sosire: vârtej = 1,2 ture; nor = ușoară curbură.
    # Cât așteaptă (q = 1), norul se rotește lent, deci nu stă înghețat.
    turn, idle = (7.5, 0.9) if PARTICLE_STYLE == "vartej" else (0.7, 0.25)
    ang = m("MULTIPLY", q, m("MULTIPLY_ADD", m("SUBTRACT", land, frame), idle / fps, turn))
    rot = N("ShaderNodeVectorRotate"); rot.rotation_type = "Z_AXIS"
    rot.inputs["Center"].default_value = (cx, 0, cz)
    L(next(o for o in mix.outputs if o.identifier == "Result_Vector"), rot.inputs["Vector"]); L(ang, rot.inputs["Angle"])
    sp = N("GeometryNodeSetPosition"); gi = N("NodeGroupInput"); go = N("NodeGroupOutput")
    L(gi.outputs[0], sp.inputs["Geometry"]); L(rot.outputs[0], sp.inputs["Position"])
    # mărime: apar din negru în primele 0,25 s; la crossfade se sting (mai puțin cele păstrate ca sclipiri)
    appear = m("DIVIDE", m("SUBTRACT", frame, 1.0), fps * 0.25, clamp=True)
    fade = m("DIVIDE", m("SUBTRACT", frame, float(x0)), float(x1 - x0), clamp=True)
    size = m("MULTIPLY", m("MULTIPLY", attr("p_size"), appear),
             m("SUBTRACT", 1.0, m("MULTIPLY", fade, m("SUBTRACT", 1.0, attr("p_keep")))))
    ico = N("GeometryNodeMeshIcoSphere"); ico.inputs["Radius"].default_value = 1.0; ico.inputs["Subdivisions"].default_value = 1
    sm = N("GeometryNodeSetMaterial"); sm.inputs["Material"].default_value = pmat
    L(ico.outputs["Mesh"], sm.inputs["Geometry"])
    iop = N("GeometryNodeInstanceOnPoints")
    L(sp.outputs["Geometry"], iop.inputs["Points"]); L(sm.outputs["Geometry"], iop.inputs["Instance"])
    L(size, iop.inputs["Scale"]); L(iop.outputs["Instances"], go.inputs[0])
    po.modifiers.new("Particles", "NODES").node_group = ng
    # 4) textul solid apare prin alfa (EEVEE: transparență „dithered”, se netezește cu sample-urile)
    for mt in {x for x in hero.data.materials if x}:
        al = mt.node_tree.nodes["Principled BSDF"].inputs["Alpha"]; mt.use_transparent_shadow = True
        for f, v in ((x0, 0.0), (x1, 1.0)):
            al.default_value = v; al.keyframe_insert("default_value", frame=f)
        ad = mt.node_tree.animation_data
        for fc in anim_utils.action_get_channelbag_for_slot(ad.action, ad.action_slot).fcurves:
            for kp in fc.keyframe_points: kp.interpolation = "LINEAR"
    return x1


def build_scene(a):
    if MODE == "mockup":
        return build_mockup(a)
    if MODE == "exploded":
        return build_exploded(a)
    if MODE == "kinetic":
        return build_kinetic(a)
    if MODE == "commercial":
        return build_commercial(a)
    if MODE == "stats":
        return build_stats(a)
    if MODE == "turntable":
        return build_turntable(a)
    if MODE == "endscreen":
        return build_endscreen(a)
    if MODE == "lowerthird":
        return build_lowerthird(a)
    """Logo/text reveal auriu, verificat vizual (2026-11-21). Rețeta:
    HDRI ascuns de cameră + bandă de orizont întunecată (podeaua neagră reflectă textul, nu studioul),
    softbox legat de cameră (fețele frontale rămân aurii oricum se mișcă), contur cald/rece cu light linking
    (nu pătează podeaua), sweep la impact, litere decalate cu overshoot, 85 mm + DOF, praf bokeh, raze în ceață,
    bloom + aberație + vignetă, AgX High Contrast. Pentru un logo SVG: hero = import_svg_logo(...) și
    letters = [hero] (fără split_letters)."""
    s = bpy.context.scene; end = s.frame_end; fps = s.render.fps
    s.eevee.use_raytracing = True
    s.eevee.ray_tracing_options.resolution_scale = "1"
    s.eevee.use_fast_gi = True
    s.view_settings.look = "AgX - High Contrast"
    s.render.dither_intensity = 1.0  # anti-banding pe gradienții întunecați
    studio_world(ensure_hdri(HDRI_ID), strength=1.2)
    if not ALPHA:
        backdrop("glow" if MATERIAL == "sticla" else None)   # pe negru sticla n-are ce refracta: iese gri-închis
    if not SVG and not TEXT.strip():
        raise ValueError("TEXT e gol (sau doar spații) și SVG nu e setat: nu am ce anima")
    ratio = s.render.resolution_x / s.render.resolution_y
    wrap = "\n" not in TEXT and " " in TEXT.strip() and (ratio < 0.8 or (ratio <= 1.05 and len(TEXT) > 10))
    if SVG:
        hero = import_svg_logo(SVG, depth=0.15, target_width=3.0, bevel=0.012)
        if MATERIAL == "svg":
            brand_materials(hero)          # culorile din SVG, ca plastic lucios
        else:
            hero.data.materials.clear()
    elif wrap:
        words = TEXT.split()   # vertical/pătrat: textul pe 2 rânduri, altfel e o fâșie subțire în cadru
        cut = min(range(1, len(words)), key=lambda i: abs(len(" ".join(words[:i])) - len(" ".join(words[i:]))))
        hero = text_3d(" ".join(words[:cut]) + "\n" + " ".join(words[cut:]), size=1.0, extrude=0.16, bevel=0.03,
                       font=font_path())
        hero.data.bevel_resolution = 4
        if MATERIAL == "neon":
            hero.data.extrude = 0.0; hero.data.bevel_depth = 0.016; hero.data.fill_mode = "NONE"
    elif MATERIAL == "neon":   # neon adevărat = tub pe contur, nu litere pline luminoase
        hero = text_3d(TEXT, size=1.0, extrude=0.0, bevel=0.016, font=font_path())
        hero.data.fill_mode = "NONE"; hero.data.bevel_resolution = 6
    else:
        hero = text_3d(TEXT, size=1.0, extrude=0.16, bevel=0.03, font=font_path())
        hero.data.bevel_resolution = 4
    if not (SVG and MATERIAL == "svg"):
        hero.data.materials.append(hero_material())
    bpy.context.view_layer.update()
    # mărime standard: luminile, sweep-ul, praful și ceața sunt calibrate pe un titlu de ~4,4 unități lățime.
    # Un text foarte lung sau o singură literă se aduce la aceeași „amprentă”, ca rig-ul să se potrivească.
    unit = 1.0
    if not SVG:
        w, h = hero.dimensions.x, hero.dimensions.y   # text: Y local = înălțimea (e rotit cu 90° pe X)
        unit = 4.4 / max(w, h * 2.2, 1e-3)
        d = hero.data
        d.size *= unit; d.extrude *= unit; d.bevel_depth *= unit
        bpy.context.view_layer.update()
    sub = None
    if SUBTITLE:   # rând mic sub titlu; intră după impact și umple „ținerea” de la final
        hz0 = min((hero.matrix_world @ Vector(c)).z for c in hero.bound_box)
        sub = text_3d(SUBTITLE, size=0.32 * unit, extrude=0.05 * unit, bevel=0.010 * unit, font=font_path())
        if MATERIAL == "neon":
            sub.data.extrude = 0.0; sub.data.bevel_depth = 0.006; sub.data.fill_mode = "NONE"
        sub.data.materials.append(hero_material())
        sub.location.z = hz0 - 0.36 * unit
        bpy.context.view_layer.update()
    framed = [hero] + ([sub] if sub else [])
    floor_z = min((o.matrix_world @ Vector(c)).z for o in framed for c in o.bound_box) - 0.02  # podeaua sub tot
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, floor_z))
    fl = bpy.context.object; fl.data.materials.append(material("Floor", (0.004,0.004,0.005,1), roughness=0.12, specular=0.35))
    if ALPHA:   # podeaua devine „holdout”: ascunde literele care urcă din ea, dar iese transparentă
        fl.is_holdout = True
    bpy.ops.object.camera_add(); cam = bpy.context.object; s.camera = cam; cam.data.lens = 85
    portrait = s.render.resolution_y > s.render.resolution_x
    tgt = fit_camera(cam, framed, fill=0.66 if portrait else 0.62, height=0.10, frame=1)  # încă static aici
    if portrait:   # zona sigură Reels/TikTok/Shorts e asimetrică (butoane pe dreapta): conținutul ușor spre stânga
        vis = 2 * (cam.location - tgt.location).length * math.tan(cam.data.angle_x / 2)
        dx = 0.035 * vis
        cam.location.x += dx; tgt.location.x += dx
        # și puțin în SUS: jumătatea de jos e ocupată de descriere/butoane (centrul zonei sigure e la ~40% de sus)
        dz = 0.10 * vis * s.render.resolution_y / s.render.resolution_x
        cam.location.z -= dz; tgt.location.z -= dz
    letters = [hero] if (SVG or MODE == "particles") else split_letters(hero)
    start = int(fps * 0.4)
    # decalaj în SECUNDE (max 0,1 s/literă), tot reveal-ul în ~0,6 s oricâte litere și oricare fps
    stagger = max(1, min(round(fps * 0.1), round(fps * 0.6 / max(1, len(letters) - 1))))
    settled = (build_particles(hero, start, unit, floor_z) if MODE == "particles" else
               stagger_reveal(letters, start=start, stagger=stagger, dur=int(fps * 0.9), rise=0.8 * unit, style=REVEAL))
    EVENTS.update(intro=start / fps, impact=(settled - int(fps * 0.9 * 0.3)) / fps,
                  sweep=(settled - int(fps * 0.3)) / fps, settled=settled / fps)
    if sub:
        f0 = settled + int(fps * 0.25); f1 = f0 + int(fps * 0.6); p = sub.location.copy()
        key(sub, "scale", (0.001,) * 3, f0, "EXPO", "EASE_OUT"); key(sub, "scale", (1, 1, 1), f1, "EXPO", "EASE_OUT")
        key(sub, "location", tuple(p - Vector((0, 0, 0.12))), f0, "EXPO", "EASE_OUT")
        key(sub, "location", tuple(p), f1, "EXPO", "EASE_OUT")
        EVENTS["subtitle"] = f0 / fps
        letters = letters + [sub]   # primește și el contur, benzi de crom, verificare de încadrare
    # lumini
    card_w = CARD_W if CARD_W is not None else {"plastic": 220, "svg": 220, "neon": 0, "crom": 40}.get(MATERIAL, 900)
    card = area("Card", (0, 0, 0), card_w, (1, .95, .88), shape="RECTANGLE", sx=40, sy=5)
    card.data.use_shadow = False
    card.parent = cam; card.location = (0, 0, 6); card.rotation_euler = (0, 0, 0)  # în spatele camerei, o urmează
    if MATERIAL == "crom":
        # cromul arată a crom doar dacă reflectă CONTRAST: bandă albă sus, linie închisă, bandă caldă jos
        # pentru FIECARE rând de text. Un punct la înălțimea h față de CAMERĂ (nu de țintă) se reflectă, în planul aflat
        # la c în spatele camerei, la h*(2D+c)/D (D = distanța cameră-text). Verificat cu benzi roșu/verde.
        up_w, lo_w = STRIPS or (500, 150)
        c_back = 6.0
        strips = []
        D = (cam.location - tgt.location).length
        k = (2 * D + c_back) / D
        s.frame_set(s.frame_end)  # literele în poza FINALĂ (la cadrul curent pot fi încă scalate la 0)
        rows = []
        for L in letters:
            zs = [(L.matrix_world @ Vector(v)).z for v in L.bound_box]
            z0, z1 = min(zs), max(zs)
            r = next((r for r in rows if abs(r["zc"] - (z0 + z1) / 2) < 0.5 * (r["z1"] - r["z0"])), None)
            if r: r["z0"] = min(r["z0"], z0); r["z1"] = max(r["z1"], z1); r["zc"] = (r["z0"] + r["z1"]) / 2
            else: rows.append({"z0": z0, "z1": z1, "zc": (z0 + z1) / 2})
        for i, r in enumerate(rows):
            h, e = r["zc"] - cam.location.z, (r["z1"] - r["z0"]) / 2
            # benzi calibrate pe randare (proba roșu/verde): orizontul închis cade puțin sub mijlocul literelor
            for nm, y0, y1, w, col in ((f"StripUp{i}", h - 0.3 * e, h + 1.3 * e, up_w, (1, 1, 1)),
                                       (f"StripLo{i}", h - 1.5 * e, h - 0.6 * e, lo_w, (1, .8, .55))):
                st = area(nm, (0, 0, 0), w, col, shape="RECTANGLE", sx=40, sy=(y1 - y0) * k)
                st.data.use_shadow = False; st.parent = cam
                st.location = (0, (y0 + y1) / 2 * k, c_back); st.rotation_euler = (0, 0, 0)
                strips.append(st)
    top = area("TopStrip", (0, -1.5, 5), 700, (1, .97, .9), shape="RECTANGLE", sx=8, sy=0.4, target=(0, 0, 0))
    top.data.use_shadow = False
    # light linking: conturul colorat luminează DOAR literele (altfel se reflectă ca o pată în podea)
    rcv = bpy.data.collections.new("RimReceivers")
    for L in letters: rcv.objects.link(L)
    for st in (strips if MATERIAL == "crom" else []):
        st.light_linking.receiver_collection = rcv  # benzile se reflectă doar în litere, nu în podea
    for nm, loc, col in (("RimL", (-4, 4, 5), (0.35, 0.55, 1.0)), ("RimR", (4, 4, 5), (1.0, 0.55, 0.25))):
        r = area(nm, loc, 2000, col, size=1.5)
        r.light_linking.receiver_collection = rcv
    sweep_w = SWEEP_W if SWEEP_W is not None else {"crom": 200}.get(MATERIAL, 450)
    sw = area("Sweep", (-7, -2.5, 1.0), sweep_w, (1, 1, 1), shape="RECTANGLE", sx=0.08, sy=3, target=(0, 0, 0.3))
    sw.light_linking.receiver_collection = rcv  # licărirea pe litere, nu o dâră albă în podea
    impact_f = round(EVENTS["impact"] * fps)
    # MOMENTUL DE IMPACT: sweep rapid (~0,6 s) centrat pe impact + puls de bloom + mic „punch-in” de zoom
    key(sw, "location", (-7, -2.5, 1.2), impact_f - int(fps * 0.3), "SINE", "EASE_IN_OUT")
    key(sw, "location", (7, -2.5, 1.2), impact_f + int(fps * 0.3), "SINE", "EASE_IN_OUT")
    EVENTS["sweep"] = (impact_f - int(fps * 0.1)) / fps
    if end - settled > int(fps * 2.8):   # clip lung: al doilea sweep, invers, ca finalul să nu fie static
        f2 = end - int(fps * 1.6)
        key(sw, "location", (7, -2.5, 1.2), f2 - int(fps * 0.1), "SINE", "EASE_IN_OUT")
        key(sw, "location", (-7, -2.5, 1.2), end, "SINE", "EASE_IN_OUT")
        EVENTS["sweep2"] = (f2 + int(fps * 0.5)) / fps
    # camera: orbit lateral + push-in care decelerează lung
    from mathutils import Matrix
    c = tgt.location; p0 = cam.location.copy() - c
    def orbit(v, yaw, pitch, dist=1.0):  # rotește camera în jurul țintei (grade)
        return c + (Matrix.Rotation(math.radians(yaw), 3, "Z") @ Matrix.Rotation(math.radians(pitch), 3, "X") @ v) * dist
    key(cam, "location", tuple(orbit(p0, 28, 0, 1.18)), 1, "QUART", "EASE_OUT")     # start: 3/4, mai departe
    key(cam, "location", tuple(orbit(p0, 9, 0, 1.0)), end, "QUART", "EASE_OUT")     # final: ușor din unghi
    cam.data.dof.use_dof = True; cam.data.dof.focus_object = tgt; cam.data.dof.aperture_fstop = 2.8
    cam.data.dof.aperture_blades = 7
    if DUST:
        dust()
    if not ALPHA:
        god_rays()   # ceața ar ieși ca un văl semi-transparent peste tot cadrul
    impact_pulse(compositor(vignette=0.0 if ALPHA else 0.35), cam, impact_f)
    if SHAKE:
        camera_shake(cam, impact_f, amount=0.005 * (cam.location - tgt.location).length * SHAKE)
    kind = "exit" if (OUTRO == "fade" and ALPHA) else OUTRO
    t_out = outro(letters, kind, end, unit=unit) if kind in ("exit", "fade") else None
    if t_out is not None:
        EVENTS["outro"] = t_out
    return letters


TEMPLATE_VERSION = "2026.11.22-11"   # crește la fiecare schimbare a template-ului; ajunge în events.json

ENUMS = {"MODE": ("reveal", "turntable", "lowerthird", "endscreen", "mockup", "exploded", "stats", "kinetic", "particles", "commercial"),
         "PRODUCT": ("bottle", "jar", "can", "box"),
         "PARTICLE_STYLE": ("nor", "vartej"),
         "MATERIAL": ("aur", "crom", "plastic", "neon", "svg", "sticla", "iridescent"), "REVEAL": ("rise", "drop", "flip", "pop"),
         "OUTRO": ("none", "exit", "fade"), "BACKGROUND": ("negru", "glow"), "STUDIO": ("dark", "light"),
         "DEVICE": ("phone", "laptop"), "SCREEN_FIT": ("cover", "contain"), "LABEL_FIT": ("cover", "contain"), "EXPLODE_AXIS": ("radial", "z"),
         "ASPECT": ("16:9", "9:16", "1:1", "4:5")}
KNOWN = set(ENUMS) | {"ALPHA", "BG_COLOR", "BG_EMIT", "CARD_W", "COLOR", "CONTACT_SHADOW", "DUST", "EXPLODE",
                      "EXPLODE_BACK", "FONT", "FONT_LABEL", "FPS", "HDRI_ID", "MODEL", "SCREEN", "SCREEN_GLARE",
                      "SCREEN_SHINE", "SECONDS", "SHAKE", "STATS", "STRIPS", "SUBTITLE", "SVG", "SWEEP_W", "TEXT",
                      "TT_FILL", "TURNS", "PHRASES", "KINETIC_STYLES", "PARTICLES", "PARTICLE_KEEP", "INGREDIENTS", "FRAMING_SKIP", "CONDENSATION", "SPLASH", "LABEL_IMAGE",
                      # interne (nu sunt parametri)
                      "EVENTS", "COUNTERS", "FONTS", "ASSETS", "TEMPLATE_VERSION", "ENUMS", "KNOWN", "MODES_USING"}
# ce parametri citește fiecare mod (un parametru setat pentru alt mod e ignorat -> avertisment)
MODES_USING = {"DEVICE": ("mockup",), "SCREEN": ("mockup",), "SCREEN_FIT": ("mockup",), "STATS": ("stats",),
               "EXPLODE_AXIS": ("exploded",), "EXPLODE": ("exploded",), "EXPLODE_BACK": ("exploded",),
               "TURNS": ("turntable",), "REVEAL": ("reveal",), "SHAKE": ("reveal", "particles"),
               "PARTICLES": ("particles",), "PARTICLE_STYLE": ("particles",), "PARTICLE_KEEP": ("particles",), "PRODUCT": ("commercial",), "INGREDIENTS": ("commercial",), "LABEL_IMAGE": ("commercial",), "LABEL_FIT": ("commercial",), "CONDENSATION": ("commercial",), "SPLASH": ("commercial",)}


def _script_sha():
    import hashlib
    try:
        path = sys.argv[sys.argv.index("-P") + 1]
        return hashlib.sha256(open(path, "rb").read()).hexdigest()
    except (ValueError, IndexError, OSError):
        return None


def validate():
    """Greșelile de tastare nu mai cad în tăcere pe valoarea implicită: enum greșit = eroare (exit 1),
    nume de parametru necunoscut = eroare (ex. MATERIALL), MODEL/SVG/SCREEN inexistente = eroare."""
    g = globals(); errs = []
    for k, ok in ENUMS.items():
        if g.get(k) not in ok:
            errs.append(f"{k} = {g.get(k)!r} nu e valid; merge: {', '.join(ok)}")
    for k in [k for k in g if k.isupper() and not k.startswith("_")]:
        if k not in KNOWN:
            errs.append(f"parametru necunoscut {k} (greșeală de tastare? cei valizi sunt în secțiunea SCENA)")
    for k in ("MODEL", "SVG", "SCREEN", "LABEL_IMAGE"):
        if g.get(k) and not os.path.isfile(g[k]):
            errs.append(f"{k}: fișierul nu există: {g[k]}")
    if MODE == "exploded" and not MODEL:
        errs.append("MODE = exploded cere MODEL (.glb cu piese separate)")
    if MODE == "mockup" and not SCREEN:
        errs.append("MODE = mockup cere SCREEN (cale absolută către captura .png/.jpg sau video .mp4)")
    if MODE == "stats" and not (1 <= len(STATS) <= 4):
        errs.append("STATS trebuie să aibă 1-4 perechi (valoare, etichetă)")
    if errs:
        raise ValueError("ANIM3D_PARAMS:\n  " + "\n  ".join(errs))
    defaults = {"PRODUCT": "bottle", "LABEL_IMAGE": None, "LABEL_FIT": "contain", "INGREDIENTS": True, "CONDENSATION": 0.0, "SPLASH": False, "PARTICLES": 6000, "PARTICLE_STYLE": "nor", "PARTICLE_KEEP": 0.0, "DEVICE": "phone", "SCREEN": None, "SCREEN_FIT": "cover", "EXPLODE_AXIS": "radial", "EXPLODE": 1.0,
                "EXPLODE_BACK": True, "TURNS": 1.0, "REVEAL": "rise", "SHAKE": 1.0}
    ign = [k for k, modes in MODES_USING.items() if MODE not in modes and g.get(k) != defaults.get(k, g.get(k))
           and not (k == "STATS")]
    if ign:
        print("ANIM3D_WARN", json.dumps({"problem": f"parametri setați, dar ignorați de MODE = {MODE}: {', '.join(ign)}"},
                                        ensure_ascii=False))


def auto_frames(kind):
    """--frames auto (8 cadre de preview) / hold (3 cadre pentru proba la rezoluție plină), din momentele EVENTS.
    hold = momentul de impact, așezarea și ultimul cadru ÎNAINTE de outro (nu cadre goale/negre de după ieșire)."""
    s = bpy.context.scene; f0, f1, fps = s.frame_start, s.frame_end, s.render.fps
    fr = lambda t: int(round(t * fps))   # EVENTS = număr_cadru / fps (nu offset de la frame_start)
    last = fr(EVENTS["outro"]) - 1 if "outro" in EVENTS else f1
    if "cuts" in EVENTS:   # commercial: planuri cu tăieturi
        b = [f0] + [fr(t) for t in EVENTS["cuts"]] + [f1 + 1]
        mids = [(b[i] + b[i + 1] - 1) // 2 for i in range(len(b) - 1)]
        c = ([mids[0], mids[2], fr(EVENTS["impact"]) + int(fps * 0.15), last] if kind == "hold"
             else mids + [fr(EVENTS["impact"]), fr(EVENTS.get("settled", EVENTS["impact"])), last])
    elif kind == "hold":
        c = [fr(EVENTS["impact"]) if "impact" in EVENTS else (f0 + last) // 2,
             fr(EVENTS["settled"]) + int(fps * 0.3) if "settled" in EVENTS else (f0 + 3 * last) // 4, last]
    elif "intro" in EVENTS and "impact" in EVENTS and not EVENTS.get("loop"):
        i, m, st = fr(EVENTS["intro"]), fr(EVENTS["impact"]), fr(EVENTS.get("settled", EVENTS["impact"]))
        c = [i + int(fps * 0.2), (i + m) // 2, m - int(fps * 0.1), m, st, (st + last) // 2, last, f1]
    else:   # buclă / fără momente: uniform pe toată durata
        c = [f0 + round(k * (f1 - f0) / 7) for k in range(8)]
    return ",".join(str(x) for x in sorted({min(f1, max(f0, x)) for x in c}))


def main():
    validate()
    a = parse_args()
    a.aspect = a.aspect or globals().get("ASPECT", "16:9")
    a.seconds = a.seconds or globals().get("SECONDS", 4.0)
    a.fps = a.fps or globals().get("FPS", 30)
    a.transparent = a.transparent or globals().get("ALPHA", False) or globals().get("MODE") == "lowerthird"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    setup_render(a)
    hero = build_scene(a)
    if a.frames in ("auto", "hold"):
        a.frames = auto_frames(a.frames)
        print("ANIM3D_FRAMES", a.frames)
    bad = framing_report(hero) if hero else []
    print("ANIM3D_FRAMING", json.dumps({"ok": not bad, "problems": bad[:10]}))
    s = bpy.context.scene
    end_s = (s.frame_end - s.frame_start + 1) / s.render.fps
    hold = end_s - EVENTS.get("settled", 0) - (end_s - EVENTS["outro"] if "outro" in EVENTS else 0)
    if "settled" in EVENTS and hold < 1.0:
        print("ANIM3D_WARN", json.dumps({"problem": f"titlul/produsul stă nemișcat doar {hold:.2f}s (se așază la "
                                         f"{EVENTS['settled']:.2f}s din {end_s:.2f}s, ieșirea ocupă finalul): nu rămâne "
                                         "timp de citit. Mărește SECONDS (min. ~3 s; +0,7 s cu OUTRO; 5 s cu SUBTITLE)."},
                                        ensure_ascii=False))
    if hero and s.render.resolution_y > s.render.resolution_x:
        # zona sigură aproximativă pentru Reels/TikTok/Shorts (sus 14%, jos 35%, stânga 11%, dreapta 18% sunt
        # acoperite de interfața aplicației). Valori orientative din ghidurile platformelor; avertisment, nu eroare.
        s.frame_set(s.frame_end if "outro" not in EVENTS else max(s.frame_start, int(EVENTS["outro"] * s.render.fps) - 1))
        dg = bpy.context.evaluated_depsgraph_get(); pts = []
        for h in (hero if isinstance(hero, (list, tuple)) else [hero]):
            ev = h.evaluated_get(dg)
            pts += [world_to_camera_view(s, s.camera, ev.matrix_world @ Vector(c)) for c in ev.bound_box]
        if pts:
            x0, x1 = min(p.x for p in pts), max(p.x for p in pts)
            top, bot = 1 - max(p.y for p in pts), 1 - min(p.y for p in pts)
            bad = [n for n, ok in (("sus", top >= 0.14), ("jos", bot <= 0.65), ("stânga", x0 >= 0.11), ("dreapta", x1 <= 0.82)) if not ok]
            if bad:
                print("ANIM3D_SAFEZONE", json.dumps({"iese_din_zona_sigura": bad, "x": [round(x0, 3), round(x1, 3)],
                                                     "y_de_sus": [round(top, 3), round(bot, 3)]}, ensure_ascii=False))
    if a.save_blend:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(os.path.abspath(a.out)), "scene.blend"))
    if a.check_only:
        return
    render(a)
    # mereu: finalize.py verifică numărul de cadre și fps-ul, sound.py durata și momentele
    EVENTS.update(duration=end_s, fps=s.render.fps, frame_start=s.frame_start, frame_end=s.frame_end,
                  aspect=a.aspect, res=[s.render.resolution_x, s.render.resolution_y], mode=a.mode,
                  partial=bool(a.frames), frames_dir=os.path.abspath(a.out), alpha=bool(a.transparent),
                  template_version=TEMPLATE_VERSION, script_sha256=_script_sha())
    with open(os.path.join(os.path.dirname(os.path.abspath(a.out)), "events.json"), "w") as f:
        json.dump(EVENTS, f)
    print("ANIM3D_DONE", json.dumps({"out": a.out, "engine": s.render.engine, "frames": ([int(x) for x in a.frames.split(",") if x.strip()] if a.frames else [s.frame_start, s.frame_end]),
                                     "res": [s.render.resolution_x, s.render.resolution_y], "fps": s.render.fps}))


main()
