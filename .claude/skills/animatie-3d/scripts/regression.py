"""Test de regresie vizuala pentru blender_template.py (doar stdlib + ffmpeg in PATH).

  python -I regression.py --update            # (re)genereaza referintele
  python -I regression.py                     # compara cu referintele (PSNR < 28 dB = REGRESIE)
  python -I regression.py --only commercial_bottle,reveal
  python -I regression.py --template C:/cale/alt_template.py   # testeaza alt template fata de referinte
Cod de iesire: 1 daca exista REGRESIE, 0 altfel.
"""
import argparse
import datetime
import hashlib
import json
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")

BLENDER = r"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"
DEFAULT_TEMPLATE = Path(__file__).resolve().parent / "blender_template.py"
REFS = Path(r"E:\claude-work\animatie-3d\_tools\regression_refs")
TMP = Path(r"E:\claude-work\animatie-3d\_tools\regression_tmp")
THRESHOLD = 28.0
TIMEOUT = 300
SPEAKER = r"E:/claude-work/animatie-3d/2026-11-22_test-exploded/assets/speaker.glb"

# caz: (constante de inlocuit (valori ca text Python), cadru)
CASES = {
    "reveal": ({}, 100),
    "turntable": ({"MODE": '"turntable"'}, 30),
    "lowerthird": ({"MODE": '"lowerthird"', "SUBTITLE": '"Director"'}, 100),
    "endscreen": ({"MODE": '"endscreen"', "SECONDS": "8.0"}, 200),
    "stats": ({"MODE": '"stats"', "SECONDS": "5.0"}, 140),
    "kinetic": ({"MODE": '"kinetic"', "SECONDS": "8.0"}, 230),
    "particles": ({"MODE": '"particles"'}, 50),
    "commercial_bottle": ({"MODE": '"commercial"', "SECONDS": "10.0", "TEXT": '"Aurora"',
                           "MATERIAL": '"sticla"', "COLOR": "(1.0, 0.45, 0.08, 1)"}, 140),
    "commercial_can": ({"MODE": '"commercial"', "SECONDS": "10.0", "PRODUCT": '"can"',
                        "COLOR": "(0.85, 0.05, 0.1, 1)"}, 250),
    "exploded": ({"MODE": '"exploded"', "SECONDS": "6.0", "MODEL": 'r"%s"' % SPEAKER}, 90),
    # 9:16, cadrul 4 (camera de jos): aici camera ajungea sub podea -> cadre negre (bug 2026-11-22)
    # studio alb: text de packshot închis la culoare, contur atenuat (bug alb-pe-alb găsit de agentul de test)
    "commercial_light": ({"MODE": '"commercial"', "SECONDS": "10.0", "STUDIO": '"light"', "TEXT": '"Verdeață"',
                          "MATERIAL": '"sticla"', "COLOR": "(0.30, 0.62, 0.04, 1)"}, 290),
    "commercial_916": ({"MODE": '"commercial"', "SECONDS": "10.0", "ASPECT": '"9:16"', "MATERIAL": '"sticla"',
                        "COLOR": "(0.95, 0.75, 0.1, 1)"}, 150),   # cadrul 4 = 44-54% din durată
}


def make_scene(template_text, consts, dest):
    text = template_text
    for name, value in consts.items():
        pat = re.compile(r"^(%s[ \t]*=[ \t]*)(.*?)([ \t]+#.*)?$" % re.escape(name), re.M)
        hits = pat.findall(text)
        if len(hits) != 1:
            raise SystemExit("EROARE: constanta %s gasita de %d ori in template (asteptat 1)" % (name, len(hits)))
        text = pat.sub(lambda m: m.group(1) + value + (m.group(3) or ""), text, count=1)
    dest.write_text(text, encoding="utf-8")


def render(case, template_text, frame):
    """Returneaza (cale_png | None, motiv_eroare | None)."""
    consts, _ = CASES[case]
    work = TMP / case
    if work.exists():
        shutil.rmtree(work)
    work.mkdir(parents=True)
    scena = work / "scena.py"
    make_scene(template_text, consts, scena)
    out = work / "out"
    out.mkdir()
    cmd = [BLENDER, "-b", "--factory-startup", "--python-exit-code", "1", "-P", str(scena), "--",
           "--out", str(out), "--mode", "preview", "--frames", str(frame)]
    err = None
    for attempt in (1, 2):
        try:
            r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace",
                               timeout=TIMEOUT)
        except subprocess.TimeoutExpired:
            return None, "timeout %d s" % TIMEOUT
        pngs = sorted(out.rglob("*.png"))
        if r.returncode == 0 and pngs:
            return pngs[-1], None
        blob = (r.stdout or "") + (r.stderr or "")
        err = "cod %d, %s" % (r.returncode, "niciun PNG" if r.returncode == 0 else blob.strip()[-300:].replace("\n", " | "))
        if attempt == 1 and re.search(r"out of memory|access violation|0xC0000005", blob, re.I):
            continue
        break
    return None, err


def psnr(a, b):
    r = subprocess.run(["ffmpeg", "-hide_banner", "-i", str(a), "-i", str(b), "-lavfi", "psnr", "-f", "null", "-"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    m = re.search(r"average:(\S+)", r.stderr)
    if not m:
        return None
    return float("inf") if m.group(1) == "inf" else float(m.group(1))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--update", action="store_true")
    ap.add_argument("--only", default="")
    ap.add_argument("--template", default=str(DEFAULT_TEMPLATE))
    a = ap.parse_args()

    tpl = Path(a.template)
    template_text = tpl.read_text(encoding="utf-8")
    names = [n.strip() for n in a.only.split(",") if n.strip()] or list(CASES)
    for n in names:
        if n not in CASES:
            raise SystemExit("caz necunoscut: %s (valide: %s)" % (n, ", ".join(CASES)))
    REFS.mkdir(parents=True, exist_ok=True)
    TMP.mkdir(parents=True, exist_ok=True)
    t0 = time.time()

    if a.update:
        meta_path = REFS / "refs.json"
        meta = json.loads(meta_path.read_text(encoding="utf-8")) if meta_path.exists() else {}
        meta.setdefault("cases", {})
        failed = 0
        for n in names:
            png, err = render(n, template_text, CASES[n][1])
            if err:
                print("%-18s | EROARE la randare: %s" % (n, err))
                failed += 1
                continue
            shutil.copyfile(png, REFS / (n + ".png"))
            meta["cases"][n] = {"frame": CASES[n][1], "constants": CASES[n][0]}
            print("%-18s | referinta salvata (%s)" % (n, REFS / (n + ".png")))
        meta["template_sha256"] = hashlib.sha256(template_text.encode("utf-8")).hexdigest()
        meta["template"] = str(tpl)
        meta["date"] = datetime.datetime.now().isoformat(timespec="seconds")
        meta_path.write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")
        print("Total: %.0f s" % (time.time() - t0))
        sys.exit(1 if failed else 0)

    rows, bad = [], 0
    for n in names:
        ref = REFS / (n + ".png")
        if not ref.exists():
            rows.append((n, "-", "REGRESIE", "lipseste referinta (ruleaza --update)"))
            bad += 1
            continue
        png, err = render(n, template_text, CASES[n][1])
        if err:
            rows.append((n, "-", "REGRESIE", "randare esuata: " + err))
            bad += 1
            continue
        p = psnr(ref, png)
        if p is None:
            rows.append((n, "-", "REGRESIE", "ffmpeg psnr a esuat (rezolutii diferite?)"))
            bad += 1
            continue
        if p < THRESHOLD:
            diff = REFS / ("diff_%s.png" % n)
            subprocess.run(["ffmpeg", "-hide_banner", "-y", "-i", str(ref), "-i", str(png),
                            "-filter_complex", "hstack", str(diff)], capture_output=True)
            rows.append((n, "%.2f" % p, "REGRESIE", str(diff)))
            bad += 1
        else:
            rows.append((n, "inf" if p == float("inf") else "%.2f" % p, "OK", "-"))
    print("%-18s | %-7s | %-8s | %s" % ("caz", "psnr", "stare", "cale diff"))
    for r in rows:
        print("%-18s | %-7s | %-8s | %s" % r)
    print("Total: %.0f s, %d regresii din %d" % (time.time() - t0, bad, len(rows)))
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
