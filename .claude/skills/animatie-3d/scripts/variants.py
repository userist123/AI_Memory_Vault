"""Toate formatele dintr-o singură scenă: randare + finalize + sunet + contact sheet pentru fiecare.

  python -I variants.py <proiect_abs> [--aspects 16:9 9:16 1:1] [--preview] [--mute] [--ext mp4|mov|webm]

Folosește <proiect>/scena.py neschimbat (ASPECT din fișier e suprascris cu --aspect). Fiecare format primește
subfolderul lui: <proiect>/v_16x9/{final/, events.json, <nume>.mp4, <nume>_sunet.mp4, final_sheet.png, ...}.
Formatele merg secvențial (un singur GPU); cu rețeta premium ~4-5 min fiecare la 1080p -> rulezi cu
run_in_background. La final tipărește VARIANTS_DONE cu starea fiecăruia; codul de ieșire e 1 dacă vreunul a eșuat.
"""
import argparse
import glob
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def blender_exe():
    cands = ["C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"] + \
        sorted(glob.glob("C:/Program Files/Blender Foundation/*/blender.exe"), reverse=True)
    for c in cands:
        if os.path.exists(c):
            return c
    sys.exit("nu găsesc blender.exe")


def run(cmd, log):
    with open(log, "a", encoding="utf-8") as f:
        f.write("\n$ " + " ".join(cmd) + "\n")
        r = subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT)
    return r.returncode


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser()
    p.add_argument("project")
    p.add_argument("--aspects", nargs="+", default=["16:9", "9:16", "1:1"])
    p.add_argument("--preview", action="store_true", help="rezoluție 1/3, pentru o probă rapidă a tuturor formatelor")
    p.add_argument("--mute", action="store_true", help="fără sunet")
    p.add_argument("--look", default="none", help="look de grading pentru finalize.py (același la toate formatele)")
    p.add_argument("--lut", default=None, help="fișier .cube pentru finalize.py")
    p.add_argument("--ext", default="mp4", choices=["mp4", "mov", "webm"], help="mov/webm pentru ALPHA = True")
    a = p.parse_args()
    proj = os.path.abspath(a.project)
    scena = os.path.join(proj, "scena.py")
    if not os.path.exists(scena):
        sys.exit("lipsește " + scena)
    name = os.path.basename(proj.rstrip("/\\")).split("_", 1)[-1] or "animatie"
    B = blender_exe()
    results = []
    for asp in a.aspects:
        sub = os.path.join(proj, "v_" + asp.replace(":", "x"))
        if os.path.isdir(os.path.join(sub, "final")):
            shutil.rmtree(os.path.join(sub, "final"))   # cadre vechi -> finalize ar respinge oricum
        os.makedirs(sub, exist_ok=True)
        shutil.copy2(scena, os.path.join(sub, "scena.py"))
        log = os.path.join(sub, "variants.log")
        out = os.path.join(sub, "final")
        mp4 = os.path.join(sub, f"{name}.{a.ext}")
        st = {"aspect": asp, "dir": sub}
        rc = run([B, "-b", "--factory-startup", "--python-exit-code", "1", "-P", os.path.join(sub, "scena.py"), "--",
                  "--out", out, "--aspect", asp, "--mode", "preview" if a.preview else "final"]
                 + ([] if a.preview else ["--save-blend"]), log)
        if rc:
            st["error"] = "randarea a eșuat (vezi variants.log)"; results.append(st); continue
        rc = run([sys.executable, "-I", os.path.join(HERE, "finalize.py"), out, mp4, "--look", a.look]
                 + (["--lut", a.lut] if a.lut else []), log)
        st["finalize_ok"] = rc == 0
        rep = os.path.splitext(mp4)[0] + "_report.json"
        if os.path.exists(rep):
            st["problems"] = json.load(open(rep, encoding="utf-8")).get("problems")
        final_video = mp4
        if not a.mute and a.ext == "mp4" and rc == 0:
            snd = os.path.splitext(mp4)[0] + "_sunet.mp4"
            if run([sys.executable, "-I", os.path.join(HERE, "sound.py"), mp4, os.path.join(sub, "events.json"), snd],
                   log) == 0:
                final_video = snd
            else:
                st["sound_error"] = "vezi variants.log"
        run([sys.executable, "-I", os.path.join(HERE, "contact_sheet.py"), final_video,
             os.path.join(sub, "final_sheet.png"), "--full", "3"], log)
        st["video"] = final_video
        results.append(st)
    ok = all(r.get("finalize_ok") and "error" not in r for r in results)
    print("VARIANTS_DONE", json.dumps({"ok": ok, "results": results}, ensure_ascii=False, indent=1))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
