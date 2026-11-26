"""Contact sheet din cadre PNG sau dintr-un MP4, ca agentul să SE UITE la animație.

  python -I contact_sheet.py <folder_cadre|video.mp4> <iesire.png> [--n 8] [--cols 4] [--w 400]

Fiecare celulă poartă numărul REAL al cadrului (f_0020 pentru PNG-uri, numărul cadrului 1..N pentru MP4).
Dacă sunt mai puține cadre decât --n, grila se micșorează (fără celule negre goale).
Necesită ffmpeg/ffprobe în PATH.
"""
import argparse
import glob
import os
import re
import subprocess
import sys
import tempfile

FONT = r"fontfile='C\:/Windows/Fonts/arial.ttf':" if os.name == "nt" else ""  # fără fontconfig pe Windows


def label(text_expr):
    return (f"drawtext={FONT}text='{text_expr}':x=10:y=10:fontsize=28:fontcolor=white:"
            f"box=1:boxcolor=black@0.65:boxborderw=6")


def main():
    sys.stdout.reconfigure(encoding="utf-8"); sys.stderr.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser()
    p.add_argument("src")
    p.add_argument("out")
    p.add_argument("--n", type=int, default=8)
    p.add_argument("--cols", type=int, default=4)
    p.add_argument("--w", type=int, default=400)
    p.add_argument("--full", type=int, default=0,
                   help="doar pentru MP4: extrage și N cadre la rezoluție PLINĂ (primul, ..., ultimul) lângă iesire")
    a = p.parse_args()

    if not os.path.exists(a.src):
        sys.exit("nu există: " + a.src)
    if os.path.isdir(a.src):
        frames = sorted(os.path.abspath(x) for x in glob.glob(os.path.join(a.src, "f_*.png")))
        if not frames:
            sys.exit("niciun cadru f_*.png în " + a.src)
        if len(frames) <= a.n:
            pick = frames
        else:  # n cadre uniform, inclusiv primul și ultimul
            pick = [frames[round(i * (len(frames) - 1) / (a.n - 1))] for i in range(a.n)]
        cols = min(a.cols, len(pick))
        rows = -(-len(pick) // cols)
        with tempfile.TemporaryDirectory() as td:
            # etichetează fiecare cadru cu numele lui real, apoi le pune în grilă
            for i, fr in enumerate(pick):
                name = re.sub(r"\.png$", "", os.path.basename(fr))
                subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", fr, "-vf",
                                f"scale={a.w}:-2,{label(name)}", os.path.join(td, f"c_{i:04d}.png")], check=True)
            cmd = ["ffmpeg", "-y", "-loglevel", "error", "-framerate", "1", "-i", os.path.join(td, "c_%04d.png"),
                   "-vf", f"tile={cols}x{rows}:padding=4:color=white", "-frames:v", "1", a.out]
            subprocess.run(cmd, check=True)
        print("SHEET", a.out, "|", ", ".join(os.path.basename(x) for x in pick))
    else:
        nb = subprocess.run(["ffprobe", "-v", "error", "-count_frames", "-select_streams", "v:0",
                             "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", a.src],
                            capture_output=True, text=True, check=True).stdout.strip()
        total = int(nb or 1)
        n = min(a.n, total)
        idx = sorted({round(i * (total - 1) / max(1, n - 1)) for i in range(n)})
        sel = "+".join(f"eq(n\\,{i})" for i in idx)
        cols = min(a.cols, len(idx))
        rows = -(-len(idx) // cols)
        # eticheta se pune ÎNAINTE de select, deci arată numărul real al cadrului (1..N)
        vf = (f"scale={a.w}:-2,{label(r'cadru %{eif\:n+1\:d}')},select='{sel}',"
              f"tile={cols}x{rows}:padding=4:color=white")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", a.src, "-vf", vf,
                        "-frames:v", "1", "-fps_mode", "vfr", a.out], check=True)
        print("SHEET", a.out, f"| {total} cadre; celule = cadrele", [i + 1 for i in idx])
        if a.full:
            fidx = sorted({round(i * (total - 1) / max(1, a.full - 1)) for i in range(a.full)})
            base = os.path.splitext(a.out)[0]
            outs = []
            for i in fidx:
                o = f"{base}_full_cadru{i + 1:04d}.png"
                subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", a.src, "-vf", f"select='eq(n\\,{i})'",
                                "-fps_mode", "vfr", "-frames:v", "1", o], check=True)
                outs.append(o)
            print("FULL", outs)


if __name__ == "__main__":
    main()
