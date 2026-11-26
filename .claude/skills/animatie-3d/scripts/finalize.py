"""Encodează cadrele PNG în MP4 și rulează verificările automate. Același pas pentru Blender și web.

  python -I finalize.py <folder_cadre> <iesire.mp4|.mov|.webm> [--crf 18] [--loop-check]   (fps și nr. de cadre din events.json)
  .mp4 = H.264; .mov = ProRes 4444 (cu alfa dacă ALPHA); .webm = VP9 (cu alfa dacă ALPHA)

Verifică: numărul de cadre scrise (fără goluri), ffprobe (h264, yuv420p, dimensiuni, durată),
cadre negre (blackdetect), porțiuni înghețate (freezedetect), opțional diferența prim/ultim cadru
pentru bucle. Scrie <iesire>_report.json și termină cu cod 1 dacă ceva e greșit.
Necesită ffmpeg/ffprobe în PATH.
"""
import argparse
import glob
import json
import os
import re
import subprocess
import sys



# Look-uri de grading verificate pe randări (2026-11-22). Subtile intenționat: randarea e deja în AgX.
LOOKS = {
    "none": "",
    "warm": "colorbalance=rs=0.03:bs=-0.03:rm=0.03:bm=-0.03:rh=0.02:bh=-0.02",
    "cool": "colorbalance=rs=-0.01:bs=0.012:rm=-0.03:bm=0.04:bh=0.01",
    "teal-orange": "colorbalance=rs=-0.02:bs=0.025:rm=-0.02:bm=0.03:rh=0.05:gh=0.01:bh=-0.05,eq=saturation=1.08",  # umbre mici: negrul rămâne negru
    "cinematic": "colorbalance=rs=-0.03:bs=0.04:rh=0.03:bh=-0.03,curves=preset=medium_contrast,eq=saturation=0.95",
    "bw": "hue=s=0,eq=contrast=1.08",
}

def run(cmd):
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")


# Colour: Blender writes sRGB PNGs (AgX view transform). Untagged ffmpeg RGB->YUV conversion uses the BT.601 matrix,
# while players assume BT.709 for HD, which shifts colours (measured 2026-11-26: red 224,48,32 played back as
# 237,63,28). So convert explicitly with the BT.709 matrix, limited (tv) range, and tag the stream.
BT709_TAGS = ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"]


def video_filter(alpha, look, lut, pix_fmt):
    """ffmpeg -vf chain: grading in RGB, even size, explicit BT.709/tv conversion to the target pixel format."""
    vf = ["format=rgba" if alpha else "format=rgb24"]   # grading in RGB; alpha passes untouched
    if LOOKS[look]:
        vf.append(LOOKS[look])
    if lut:
        lp = os.path.abspath(lut).replace("\\", "/").replace(":", r"\:")
        vf.append(f"lut3d=file='{lp}':interp=tetrahedral")
    vf.append("pad=ceil(iw/2)*2:ceil(ih/2)*2")
    vf.append("scale=in_range=full:out_range=tv:out_color_matrix=bt709:flags=accurate_rnd+full_chroma_int")
    vf.append(f"format={pix_fmt}")
    # Tag the frames themselves: encoders (VP9, ProRes, H.264) take colour properties from the frame, so the
    # -color_trc / -color_primaries output options alone leave transfer and primaries "unknown" (ffmpeg 9.0.2).
    vf.append("setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=tv")
    return ",".join(vf)


def codec_args(ext, alpha, crf):
    """Encoder arguments for the container chosen by extension (colour-tagged, BT.709)."""
    if ext == ".mov":    # ProRes 4444: standard for overlays in Premiere / DaVinci / Final Cut
        return ["-c:v", "prores_ks", "-profile:v", "4444", "-pix_fmt", "yuva444p10le" if alpha else "yuv444p10le"] + BT709_TAGS
    if ext == ".webm":   # VP9; alpha needs yuva420p and -auto-alt-ref 0 (libvpx limitation)
        return (["-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "24", "-row-mt", "1",
                 "-pix_fmt", "yuva420p" if alpha else "yuv420p"] + (["-auto-alt-ref", "0"] if alpha else []) + BT709_TAGS)
    return (["-c:v", "libx264", "-preset", "slow", "-crf", str(crf), "-pix_fmt", "yuv420p"] + BT709_TAGS +
            ["-x264-params", "colorprim=bt709:transfer=bt709:colormatrix=bt709", "-movflags", "+faststart"])


def target_pix_fmt(ext, alpha):
    return {".mov": "yuva444p10le" if alpha else "yuv444p10le",
            ".webm": "yuva420p" if alpha else "yuv420p"}.get(ext, "yuv420p")


def main():
    sys.stdout.reconfigure(encoding="utf-8"); sys.stderr.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser()
    p.add_argument("frames")
    p.add_argument("out")
    p.add_argument("--fps", type=int, default=None, help="implicit: fps-ul din events.json, altfel 30")
    p.add_argument("--events", default=None, help="implicit: <folder_cadre>/../events.json, dacă există")
    p.add_argument("--crf", type=int, default=18)
    p.add_argument("--loop-check", action="store_true", help="animația trebuie să se reia fără salt")
    p.add_argument("--look", default="none", choices=sorted(LOOKS),
                   help="grading final (ffmpeg, după randare, fără re-randare): " + ", ".join(sorted(LOOKS)))
    p.add_argument("--lut", default=None, help="fișier .cube (LUT 3D de brand); se aplică după --look")
    p.add_argument("--allow-freeze", type=float, default=1.0, help="secunde de imagine statică tolerate")
    a = p.parse_args()

    files = sorted(glob.glob(os.path.join(a.frames, "f_*.png")))
    problems = []
    if not files:
        sys.exit("niciun f_*.png în " + a.frames)
    nums = [int(re.search(r"f_(\d+)\.png$", f).group(1)) for f in files]
    missing = sorted(set(range(nums[0], nums[-1] + 1)) - set(nums))
    if missing:
        problems.append(f"lipsesc {len(missing)} cadre, ex. {missing[:10]}")

    # contractul cu randarea: câte cadre, ce fps (prinde randări neterminate, cadre vechi rămase, fps greșit)
    ev_path = a.events or os.path.join(os.path.dirname(os.path.abspath(a.frames)), "events.json")
    ev = json.load(open(ev_path, encoding="utf-8")) if os.path.exists(ev_path) else {}
    if ev:
        if ev.get("partial"):
            problems.append("events.json e de la o randare PARȚIALĂ (--frames); rulează randarea finală completă")
        if os.path.normcase(ev.get("frames_dir", "")) != os.path.normcase(os.path.abspath(a.frames)):
            problems.append(f"events.json descrie alt folder de cadre: {ev.get('frames_dir')}")
        exp = (ev.get("frame_start", 1), ev["frame_end"]) if "frame_end" in ev else None
        if exp and (nums[0], nums[-1], len(files)) != (exp[0], exp[1], exp[1] - exp[0] + 1):
            problems.append(f"cadre {nums[0]}..{nums[-1]} ({len(files)}), așteptate {exp[0]}..{exp[1]} "
                            f"({exp[1] - exp[0] + 1}): randare neterminată sau cadre vechi în folder")
        if a.fps and ev.get("fps") and a.fps != ev["fps"]:
            problems.append(f"--fps {a.fps} diferit de fps-ul randării ({ev['fps']}): video-ul ar rula greșit")
    else:
        print("ATENȚIE: fără events.json, nu pot verifica numărul de cadre așteptat și fps-ul", file=sys.stderr)
    a.fps = a.fps or ev.get("fps", 30)
    a.loop_check = a.loop_check or bool(ev.get("loop"))   # turntable: verificarea de buclă e automată

    pattern = os.path.join(os.path.abspath(a.frames), "f_%04d.png")
    ext = os.path.splitext(a.out)[1].lower()
    alpha = bool(ev.get("alpha"))
    if alpha and ext == ".mp4":
        problems.append("randarea are fundal transparent (ALPHA), dar .mp4 nu păstrează alfa: "
                        "folosește .mov (ProRes 4444, editoare) sau .webm (VP9, web)")
    if problems and any(".mp4" in x and "alfa" in x for x in problems):
        # the encoder would silently drop the alpha channel: stop, but leave a report so variants.py records it
        report = {"mp4": os.path.abspath(a.out), "frames": len(files), "problems": problems, "warnings": [], "ok": False}
        with open(os.path.splitext(a.out)[0] + "_report.json", "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
        print("FINALIZE", json.dumps(report, ensure_ascii=False))
        sys.exit(1)
    codec = codec_args(ext, alpha, a.crf)
    if a.lut and not os.path.isfile(a.lut):
        sys.exit(f"LUT inexistent: {a.lut}")
    vf = video_filter(alpha, a.look, a.lut, target_pix_fmt(ext, alpha))
    enc = run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(a.fps), "-start_number", str(nums[0]),
               "-i", pattern, "-vf", vf] + codec + [a.out])
    if enc.returncode:
        sys.exit("ffmpeg a eșuat: " + enc.stderr[-800:])

    pr = json.loads(run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-count_frames",
                         "-show_entries", "stream=codec_name,pix_fmt,width,height,r_frame_rate,nb_read_frames"
                         ":format=duration,size", "-of", "json", a.out]).stdout)
    st, fm = pr["streams"][0], pr["format"]
    if int(st["nb_read_frames"]) != len(files):
        problems.append(f"MP4 are {st['nb_read_frames']} cadre, PNG-uri {len(files)}")

    det = run(["ffmpeg", "-hide_banner", "-i", a.out, "-vf",
               f"blackdetect=d=0.1:pic_th=0.98,freezedetect=n=0.001:d={a.allow_freeze}", "-an", "-f", "null", "-"]).stderr
    blacks = [] if alpha else re.findall(r"black_start:([\d.]+) black_end:([\d.]+)", det)  # transparent = „negru”
    freezes = re.findall(r"freeze_start: ([\d.]+)", det)
    dur = float(json.loads(run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "json",
                                a.out]).stdout)["format"]["duration"])
    warnings = []
    intro_ok = max(0.5, float(ev.get("settled", 0)))  # până se așază titlul, negrul e voit (intrare)
    for b0, b1 in ((float(x), float(y)) for x, y in blacks):
        # negru la început (până la intrare) sau la sfârșit = voit -> doar avertisment
        (warnings if b1 <= intro_ok or b0 >= dur - 0.5 else problems).append(
            f"porțiune neagră {b0:.2f}-{b1:.2f}s")
    if freezes:
        problems.append(f"imagine înghețată > {a.allow_freeze}s începând la {freezes[:5]} s")

    loop_diff = None
    if a.loop_check and len(files) > 2:
        # diferența dintre ultimul cadru și primul (la buclă curată ar trebui să fie mică, ca între 2 cadre vecine)
        def psnr(x, y):
            o = run(["ffmpeg", "-hide_banner", "-i", x, "-i", y, "-lavfi", "psnr", "-f", "null", "-"]).stderr
            m = re.search(r"average:([\d.inf]+)", o)
            return float(m.group(1)) if m and m.group(1) != "inf" else 99.0
        seam, neighbour = psnr(files[-1], files[0]), psnr(files[0], files[1])
        loop_diff = {"psnr_ultim_prim": seam, "psnr_vecine": neighbour}
        if seam + 6 < neighbour:
            problems.append(f"bucla sare: PSNR ultim->prim {seam:.1f} dB vs vecine {neighbour:.1f} dB")

    report = {"mp4": os.path.abspath(a.out), "frames": len(files), "codec": st["codec_name"], "pix_fmt": st["pix_fmt"],
              "size": [st["width"], st["height"]], "fps": st["r_frame_rate"], "duration_s": float(fm["duration"]),
              "mb": round(int(fm["size"]) / 1e6, 2), "loop": loop_diff, "problems": problems, "warnings": warnings, "ok": not problems}
    with open(os.path.splitext(a.out)[0] + "_report.json", "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print("FINALIZE", json.dumps(report, ensure_ascii=False))
    sys.exit(0 if not problems else 1)


if __name__ == "__main__":
    main()
