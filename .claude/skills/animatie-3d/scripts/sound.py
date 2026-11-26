"""Coloană sonoră sintetizată (fără sample-uri, deci fără probleme de licență) sincronizată cu animația.

  python -I sound.py <video.mp4> <events.json> <iesire.mp4> [--lufs -14] [--mute-drone] [--gain 0]

events.json vine din blender_template.py: intro, impact, sweep, duration (+ opțional sweep2, subtitle), în secunde.
Straturi: dronă joasă pe tot clipul, whoosh (zgomot roz filtrat) de la intro la impact, impact sub-bas cu
clic de atac, shimmer metalic la fiecare sweep, „tick” scurt la subtitlu.
Mix -> limitator -> loudnorm în DOUĂ treceri (nivel exact; -14 LUFS = Reels/YouTube/TikTok) -> AAC 192k.
Video-ul e copiat fără re-encodare. Scrie și <iesire>_viz.png: whoosh+impact+shimmer FĂRĂ dronă (ca să se
vadă), cu linii verticale la momentele din events.json. Agentul nu poate asculta: privește imaginea.
"""
import argparse
import json
import re
import subprocess
import sys


def run(cmd):
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")


def build(a, ev, out, mute_drone, extra_af="", fmt_args=None, loglevel="error"):
    dur = float(ev["duration"])
    ms = lambda t: int(max(0.0, t) * 1000)
    shimmer = ("aevalsrc='(0.10*sin(2*PI*2093*t)+0.08*sin(2*PI*2637*t)+0.07*sin(2*PI*3136*t)+0.05*sin(2*PI*4186*t))"
               "*(0.6+0.4*sin(2*PI*9*t))*exp(-1.4*t)':s=48000:d=2.2")
    inputs = [f"aevalsrc='0.18*sin(2*PI*55*t)+0.09*sin(2*PI*82.5*t)+0.05*sin(2*PI*110.4*t)':s=48000:d={dur}"]
    fades = "" if ev.get("loop") else f"afade=t=in:d={dur * 0.15:.3f},afade=t=out:st={max(0, dur - 1.2):.3f}:d=1.2,"
    fc = f"[1:a]{fades}volume={0 if mute_drone else 0.7}[dr];"   # buclă: fără fade, altfel se aude reluarea
    labels = ["[dr]"]
    n = 2
    # straturile legate de momente apar doar dacă momentele există (three.js / scenă proprie: doar dronă)
    if "intro" in ev and "impact" in ev:
        intro, impact = float(ev["intro"]), float(ev["impact"])
        wl = max(0.3, impact - intro)
        inputs += [f"anoisesrc=color=pink:amplitude=0.6:s=48000:d={wl + 0.2}",
                   "aevalsrc='0.9*sin(2*PI*(42+60*exp(-10*t))*t)*exp(-2.6*t)+0.35*(random(0)-0.5)*exp(-40*t)'"
                   ":s=48000:d=2.5"]
        fc += (f"[{n}:a]highpass=f=250,lowpass=f=2500,afade=t=in:d={wl * 0.85:.3f}:curve=exp,"
               f"afade=t=out:st={wl * 0.85:.3f}:d={wl * 0.15 + 0.2:.3f},volume=0.9,adelay={ms(intro)}:all=1[wh];"
               f"[{n + 1}:a]lowpass=f=180,volume=1.4,adelay={ms(impact)}:all=1[im];")
        labels += ["[wh]", "[im]"]; n += 2
    if "sweep" in ev:
        inputs.append(shimmer)
        fc += f"[{n}:a]aecho=0.8:0.6:120|240:0.35|0.2,highpass=f=1500,volume=0.8,adelay={ms(float(ev['sweep']))}:all=1[sh];"
        labels.append("[sh]"); n += 1
    if "sweep2" in ev:
        inputs.append(shimmer)
        fc += f"[{n}:a]aecho=0.8:0.6:120|240:0.35|0.2,highpass=f=1500,volume=0.55,adelay={ms(float(ev['sweep2']))}:all=1[s2];"
        labels.append("[s2]"); n += 1
    for k, tc in enumerate(ev.get("cuts", [])):
        # J-cut: un whoosh scurt care pornește ~0,25 s ÎNAINTE de tăietură și culminează pe ea (regulă de montaj
        # comercial: sunetul trage privirea în cadrul următor; cercetare 2026-11-22)
        inputs.append("anoisesrc=color=pink:amplitude=0.5:s=48000:d=0.45")
        fc += (f"[{n}:a]highpass=f=400,lowpass=f=5000,afade=t=in:d=0.25:curve=exp,afade=t=out:st=0.27:d=0.18,"
               f"volume=0.75,adelay={ms(float(tc) - 0.27)}:all=1[jc{k}];")
        labels.append(f"[jc{k}]"); n += 1
    if "subtitle" in ev:
        inputs.append("aevalsrc='0.5*sin(2*PI*1320*t)*exp(-25*t)+0.3*sin(2*PI*660*t)*exp(-18*t)':s=48000:d=0.4")
        fc += f"[{n}:a]volume=0.6,adelay={ms(float(ev['subtitle']))}:all=1[tk];"
        labels.append("[tk]"); n += 1
    fc += (f"{''.join(labels)}amix=inputs={len(labels)}:normalize=0:duration=first,volume={a.gain}dB,"
           f"acompressor=threshold=0.1:ratio=4:attack=5:release=120:makeup=2,alimiter=limit=0.7{extra_af}[aout]")
    cmd = ["ffmpeg", "-y", "-loglevel", loglevel, "-i", a.video]  # loudnorm își tipărește măsurătoarea la nivel info
    for i in inputs:
        cmd += ["-f", "lavfi", "-i", i]
    cmd += ["-filter_complex", fc] + (fmt_args or []) + [out]
    return cmd


def main():
    sys.stdout.reconfigure(encoding="utf-8"); sys.stderr.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser()
    p.add_argument("video")
    p.add_argument("events")
    p.add_argument("out")
    p.add_argument("--lufs", type=float, default=-14.0, help="-14 rețele sociale; -16 web/podcast; -23 TV")
    p.add_argument("--mute-drone", action="store_true")
    p.add_argument("--gain", type=float, default=0.0)
    a = p.parse_args()
    ev = json.load(open(a.events, encoding="utf-8"))
    vdur = float(json.loads(run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "json",
                                 a.video]).stdout)["format"]["duration"])
    if "duration" not in ev:
        ev["duration"] = vdur
    elif abs(vdur - float(ev["duration"])) > 0.1:
        sys.exit(f"durata video {vdur:.2f}s ≠ durata din events.json {float(ev['duration']):.2f}s: "
                 "events.json e de la altă randare sau fps greșit la encodare (nu tai clipul în tăcere)")

    # trecerea 1: măsoară
    m = run(build(a, ev, "-", a.mute_drone,
                  extra_af=f",loudnorm=I={a.lufs}:TP=-1.5:LRA=11:print_format=json",
                  fmt_args=["-map", "[aout]", "-f", "null"], loglevel="info"))
    js = re.search(r"\{[^{}]*\"input_i\"[^{}]*\}", m.stderr, re.S)
    if m.returncode or not js:
        sys.exit("măsurarea loudness a eșuat: " + m.stderr[-1500:])
    me = json.loads(js.group(0))
    ln = (f",loudnorm=I={a.lufs}:TP=-1.5:LRA=11:measured_I={me['input_i']}:measured_TP={me['input_tp']}"
          f":measured_LRA={me['input_lra']}:measured_thresh={me['input_thresh']}:offset={me['target_offset']}"
          f":linear=true,aresample=48000")
    # trecerea 2: aplică și scrie
    r = run(build(a, ev, a.out, a.mute_drone, extra_af=ln,
                  fmt_args=["-map", "0:v", "-map", "[aout]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
                            "-shortest", "-movflags", "+faststart"]))
    if r.returncode:
        sys.exit("ffmpeg a eșuat: " + r.stderr[-1500:])
    lu = run(["ffmpeg", "-hide_banner", "-i", a.out, "-af", "ebur128=peak=true", "-f", "null", "-"]).stderr
    i_lufs = re.findall(r"I:\s+(-?[\d.]+) LUFS", lu)
    peak = re.findall(r"Peak:\s+(-?[\d.]+) dBFS", lu)

    # vizualizare fără dronă, cu markere la evenimente
    viz = a.out.rsplit(".", 1)[0] + "_viz.png"
    tmp = a.out.rsplit(".", 1)[0] + "_viz_tmp.wav"
    run(build(a, ev, tmp, True, fmt_args=["-map", "[aout]"]))
    W, dur = 1200, float(ev["duration"])
    marks = ",".join(f"drawbox=x={int(float(ev[k]) / dur * W)}:y=0:w=2:h=300:color={c}@0.9:t=fill"
                     for k, c in (("intro", "cyan"), ("impact", "red"), ("sweep", "yellow"),
                                  ("sweep2", "yellow"), ("subtitle", "white")) if k in ev)
    run(["ffmpeg", "-y", "-loglevel", "error", "-i", tmp, "-filter_complex",
         f"showwavespic=s={W}x300:colors=0xFFC060,format=rgb24" + ("," + marks if marks else ""),
         "-frames:v", "1", viz])
    try:
        import os; os.remove(tmp)
    except OSError:
        pass
    print("SOUND_DONE", json.dumps({"out": a.out, "lufs": float(i_lufs[-1]) if i_lufs else None,
                                    "peak_dbfs": float(peak[-1]) if peak else None, "target": a.lufs,
                                    "viz": viz, "markers": "cyan=intro roșu=impact galben=sweep alb=subtitlu"},
                                   ensure_ascii=False))


if __name__ == "__main__":
    main()
