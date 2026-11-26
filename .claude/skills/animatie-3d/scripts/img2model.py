"""Dintr-o poză de produs -> model 3D (.glb cu culori pe vârfuri), LOCAL pe GPU (TripoSR, licență MIT).
Poza nu pleacă de pe PC. Rulează cu Python-ul din mediul TripoSR, nu cu cel de sistem:

  E:/claude-work/animatie-3d/_tools/TripoSR/.venv/Scripts/python.exe <S>/img2model.py <poza.png|jpg> <iesire.glb>
         [--mc-res 256] [--no-rembg] [--fg 0.85] [--smooth 50] [--roughness 0.35] [--despec 0] [--color-smooth 0]

- Fundalul se scoate automat (rembg, local); cu --no-rembg poza trebuie să aibă deja fundal gri/alb uniform.
- --mc-res: rezoluția plasei (256 standard, 320 mai fin, ~+60% timp). Pe RTX 5060 8 GB: ~5-15 s după încărcare.
- --smooth N: N treceri de netezire Taubin (păstrează volumul; scoate aspectul noduros „de lut”). 0 = brut.
  50 = implicit (verificat pe sticlă și mașinuță: dispar treptele plasei, volumul se schimbă sub 0,1%); 20 lasă
  încă dungi pe suprafețele aproape orizontale; 100 rotunjește puțin detaliile mici. Denivelările MARI rămân.
- --roughness R: rugozitatea materialului scris în .glb (0.35 = semilucios, implicit; 0.5 = mat, pentru pânză/carton).
- --despec 1: șterge petele albe de lumină din poză înainte de model (obiecte lucioase: sticlă, metal, plastic lăcuit).
  Oprit implicit, fiindcă șterge și detaliile albe mici reale (faruri, text alb). Verifică <iesire>_input.png.
- --color-smooth N: netezire a culorilor pe vârfuri care păstrează marginile; implicit 0 (la randare nu s-a văzut
  diferență între 0, 4 și 12 pe sticla de test).
- Fișierul are și normale pe vârfuri (umbrire netedă în Blender) și un material PBR.
- Ieșirea e orientată pentru Blender: baza pe podea, fața pozei spre -Y (camera skill-ului), înălțimea ~2.
  Merge direct în MODE = "turntable" (MODEL = <iesire.glb>).
- Calitate: bună pentru obiecte compacte fotografiate frontal, pe fundal simplu (sticle, căni, jucării, încălțăminte).
  Spatele e GHICIT de model; textul/logo-urile fine ies neclare. Spune asta proprietarului.
Tipărește IMG2MODEL {...} cu calea, nr. de vârfuri/fețe, timpii și dimensiunile.
"""
import argparse
import json
import os
import sys
import time

TOOLS = "E:/claude-work/animatie-3d/_tools"
# cache-urile pe E: (C: e aproape plin): modelul HF (~1,7 GB) și u2net pentru rembg (~170 MB)
os.environ.setdefault("HF_HOME", TOOLS + "/.hf")
os.environ.setdefault("U2NET_HOME", TOOLS + "/.u2net")
sys.path.insert(0, TOOLS + "/TripoSR")

import numpy as np  # noqa: E402
import torch  # noqa: E402
from PIL import Image  # noqa: E402


def _neighbors(mesh):
    """Matricea rară de adiacență a vârfurilor (simetrică, 0/1) din muchiile plasei."""
    from scipy import sparse
    e = mesh.edges_unique
    n = len(mesh.vertices)
    d = np.ones(len(e), dtype=np.float64)
    return sparse.coo_matrix((d, (e[:, 0], e[:, 1])), shape=(n, n)).tocsr() + \
        sparse.coo_matrix((d, (e[:, 1], e[:, 0])), shape=(n, n)).tocsr()


def _row_norm(A):
    from scipy import sparse
    deg = np.asarray(A.sum(axis=1)).ravel()
    deg[deg == 0] = 1.0
    return sparse.diags(1.0 / deg) @ A


def taubin(mesh, n, lam=0.5, mu=-0.53):
    """Netezire Taubin (lambda|mu): trece-jos pe suprafață fără contracția netezirii Laplace simple."""
    if n <= 0:
        return
    L = _row_norm(_neighbors(mesh))
    v = np.asarray(mesh.vertices, dtype=np.float64).copy()
    for _ in range(n):
        v += lam * (L @ v - v)
        v += mu * (L @ v - v)
    mesh.vertices = v


def smooth_colors(mesh, rgb, n, sigma=0.08, alpha=0.6):
    """Netezire bilaterală a culorilor (liniare, 0..1) pe vârfuri: media vecinilor, ponderată cu asemănarea de culoare,
    ca petele mici să dispară, dar marginile nete (etichetă, capac) să rămână. Comparația se face în spațiu ~perceptual."""
    if n <= 0:
        return rgb
    from scipy import sparse
    A = _neighbors(mesh).tocoo()
    i, j = A.row, A.col
    c = rgb.copy()
    for _ in range(n):
        g = np.sqrt(np.clip(c, 0, 1))
        w = np.exp(-((g[i] - g[j]) ** 2).sum(axis=1) / (2 * sigma * sigma))
        W = _row_norm(sparse.coo_matrix((w, (i, j)), shape=A.shape).tocsr())
        wsum = np.asarray(sparse.coo_matrix((w, (i, j)), shape=A.shape).sum(axis=1)).ravel()
        k = alpha * np.clip(wsum, 0, 1)[:, None]      # vârf fără vecini asemănători = margine: rămâne cum e
        c = (1 - k) * c + k * (W @ c)
    return c


def despecular(x, k=1.0):
    """Șterge reflexiile speculare (pete albe de lumină pe suprafețe lucioase) din poza decupată, înainte de model:
    TripoSR le ia drept geometrie (adâncituri) și le coace în culoare. x = RGBA float 0..1; întoarce RGB.
    Miez = pixeli aproape albi și mult mai luminoși decât mediana locală; zona se lărgește peste halou și se
    înlocuiește treptat cu culoarea mediană din jur. Atenție: șterge și detalii albe mici reale (faruri, text alb)."""
    from scipy import ndimage as ndi
    h, w = x.shape[:2]
    rgb, fg = x[:, :, :3], x[:, :, 3] > 0.5
    st = max(1, int(round(max(h, w) / 128.0)))
    sm, m = rgb[::st, ::st], fg[::st, ::st]
    if not m.any():
        return rgb
    idx = ndi.distance_transform_edt(~m, return_distances=False, return_indices=True)
    filled = sm[idx[0], idx[1]]                       # fundalul ia culoarea celui mai apropiat pixel de obiect
    win = 2 * int(0.12 * max(filled.shape[:2])) + 1
    med = np.stack([ndi.median_filter(filled[:, :, c], size=win, mode="nearest") for c in range(3)], -1)
    med = np.asarray(Image.fromarray((med * 255).astype(np.uint8)).resize((w, h), Image.BILINEAR), np.float32) / 255.0
    v, vm = rgb.max(-1), med.max(-1)
    sat = (v - rgb.min(-1)) / np.maximum(v, 1e-6)
    exc = v - vm
    core = fg & (v > 0.88) & (sat < 0.45) & (exc > 0.10)
    if not core.any():
        return rgb
    r = max(2, int(0.035 * max(h, w)))
    near = ndi.gaussian_filter(ndi.binary_dilation(core, iterations=r).astype(np.float32), r / 2.0)
    t = np.clip(near * 1.3, 0, 1) * np.clip(exc / 0.06, 0, 1) * k   # doar unde e mai luminos decât împrejurimile
    t = np.clip(ndi.gaussian_filter(t, 1.5), 0, 1)[:, :, None]
    return rgb * (1 - t) + med * t


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image"); ap.add_argument("out")
    ap.add_argument("--mc-res", type=int, default=256)
    ap.add_argument("--no-rembg", action="store_true")
    ap.add_argument("--fg", type=float, default=0.85, help="cât din pătrat ocupă obiectul după decupare")
    ap.add_argument("--tilt", type=float, default=0.0,
                    help="grade: corectează înclinarea când poza e făcută de sus (+) sau de jos (-) față de produs")
    ap.add_argument("--smooth", type=int, default=50, help="treceri Taubin pe plasă (0 = fără)")
    ap.add_argument("--color-smooth", type=int, default=0, help="treceri de netezire a culorilor (0 = fără)")
    ap.add_argument("--despec", type=float, default=0.0,
                    help="0..1: șterge petele de lumină (reflexii speculare) din poză înainte de model; 0 = oprit")
    ap.add_argument("--input-only", action="store_true", help=argparse.SUPPRESS)   # doar _input.png (verificare)
    ap.add_argument("--roughness", type=float, default=0.35, help="rugozitatea materialului din .glb")
    a = ap.parse_args()
    for s in (sys.stdout, sys.stderr):
        s.reconfigure(encoding="utf-8")
    from tsr.system import TSR
    from tsr.utils import remove_background, resize_foreground
    import trimesh
    dev = "cuda:0" if torch.cuda.is_available() else "cpu"
    t0 = time.time()
    model = TSR.from_pretrained("stabilityai/TripoSR", config_name="config.yaml", weight_name="model.ckpt")
    model.renderer.set_chunk_size(8192)
    model.to(dev)
    t1 = time.time()
    img = Image.open(a.image)
    if a.no_rembg:
        img = img.convert("RGB")
    else:
        import rembg
        # u2net explicit (Apache 2.0, ~170 MB, CPU): implicitul nou din rembg e bria-rmbg (1 GB, licență
        # NECOMERCIALĂ, „bad allocation” la încărcare pe acest PC)
        img = remove_background(img, rembg.new_session("u2net", providers=["CPUExecutionProvider"]))
        img = resize_foreground(img, a.fg)
        x = np.array(img).astype(np.float32) / 255.0
        if a.despec > 0:
            x[:, :, :3] = despecular(x, a.despec)
        x = x[:, :, :3] * x[:, :, 3:4] + (1 - x[:, :, 3:4]) * 0.5
        img = Image.fromarray((x * 255.0).astype(np.uint8))
        img.save(os.path.splitext(a.out)[0] + "_input.png")   # ce a văzut modelul (verifici decuparea)
    if a.input_only:
        return
    # placa (8 GB) e împărțită cu alte programe: la „out of memory” reîncerci cu bucăți mai mici, apoi pe CPU
    mesh, tries = None, []
    for chunk, d in ((8192, dev), (2048, dev), (2048, "cpu")):
        if d != dev:
            model.to(d)
        model.renderer.set_chunk_size(chunk)
        try:
            with torch.no_grad():
                codes = model([img], device=d)
                mesh = model.extract_mesh(codes, True, resolution=a.mc_res)[0]
            dev = d
            break
        except (torch.OutOfMemoryError, RuntimeError) as e:   # AcceleratorError e subclasă de RuntimeError
            if "out of memory" not in str(e).lower():
                raise
            tries.append(f"{d}/{chunk}: OOM")
            codes = None
            torch.cuda.empty_cache()
    if mesh is None:
        sys.exit("memorie insuficientă și pe placă și pe CPU: " + "; ".join(tries))
    t2 = time.time()
    # normalele spre exterior (volum pozitiv), apoi orientarea pentru Blender: TripoSR dă obiectul cu sus = +Z și
    # privit dinspre +X; rotim ca fața pozei să privească spre -Y și punem baza pe z = 0, înălțimea 2.
    if mesh.volume < 0:
        mesh.invert()
    # bucățile mici plutitoare (zgomot al câmpului de densitate) se aruncă; rămân componentele mari
    parts = mesh.split(only_watertight=False)
    dropped = 0
    if len(parts) > 1:
        big = max(len(p.faces) for p in parts)
        keep = [p for p in parts if len(p.faces) >= 0.02 * big]
        dropped = len(parts) - len(keep)
        if dropped:
            mesh = trimesh.util.concatenate(keep)
    vol0 = float(abs(mesh.volume))
    taubin(mesh, a.smooth)
    vol1 = float(abs(mesh.volume))
    if a.tilt:   # poza făcută de sus/jos: modelul iese înclinat față-spate; rotim în jurul axei laterale
        mesh.apply_transform(trimesh.transformations.rotation_matrix(np.radians(-a.tilt), [0, 1, 0]))  # verificat: poză de la ~8° de sus -> --tilt 8 = drept
    # culorile pe vârfuri vin în sRGB, glTF COLOR_0 e LINIAR: fără conversie ies spălate/pastel în Blender
    vc = mesh.visual.vertex_colors.astype(np.float64) / 255.0
    rgb = vc[:, :3]
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    vc[:, :3] = smooth_colors(mesh, lin, a.color_smooth)
    mesh.visual.vertex_colors = (np.clip(vc, 0, 1) * 255 + 0.5).astype(np.uint8)
    mesh.apply_transform(trimesh.transformations.rotation_matrix(-np.pi / 2, [0, 0, 1]))
    b = mesh.bounds
    mesh.apply_translation([-(b[0][0] + b[1][0]) / 2, -(b[0][1] + b[1][1]) / 2, -b[0][2]])
    mesh.apply_scale(2.0 / max(mesh.extents[2], 1e-6))
    # glTF e Y-up: Z-up (Blender) -> Y-up, importerul Blender îl convertește înapoi
    mesh.apply_transform(trimesh.transformations.rotation_matrix(-np.pi / 2, [1, 0, 0]))
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    def pbr(tree):   # material PBR pe vârfuri (altfel Blender pune materialul implicit, mat)
        tree["materials"] = [{"name": "img2model", "pbrMetallicRoughness": {
            "baseColorFactor": [1, 1, 1, 1], "metallicFactor": 0.0, "roughnessFactor": float(a.roughness)}}]
        for m in tree["meshes"]:
            for p in m["primitives"]:
                p["material"] = 0
    mesh.vertex_normals   # calculate acum, după toate transformările
    # include_normals: fără NORMAL în fișier Blender umbrește PLAT (fiecare triunghi se vede -> aspect de lut)
    mesh.export(a.out, include_normals=True, tree_postprocessor=pbr)
    print("IMG2MODEL", json.dumps({"out": os.path.abspath(a.out), "verts": int(len(mesh.vertices)),
                                   "faces": int(len(mesh.faces)), "watertight": bool(mesh.is_watertight),
                                   "load_s": round(t1 - t0, 1), "infer_s": round(t2 - t1, 1), "device": dev, "oom_retries": tries,
                                   "mc_res": a.mc_res,
                                   "smooth": a.smooth, "color_smooth": a.color_smooth, "roughness": a.roughness, "despec": a.despec,
                                   "post_s": round(time.time() - t2, 1), "dropped_parts": dropped,
                                   "volume_change_pct": round(100 * (vol1 - vol0) / max(vol0, 1e-12), 2)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
