"""Pune o melodie sub animație, cu o bătaie puternică EXACT pe momentul de impact (fără re-randare).

  python -I music.py <video.mp4> <events.json> <muzica.mp3|wav|m4a> <iesire.mp4>
         [--at impact] [--pick drop|strong|first] [--sfx] [--lufs -14] [--max-scan 150]

Cum: decodează muzica mono 11 kHz, calculează energia pe benzi joase (kick) la pas de 10 ms, plicul de atac
(creșterea energiei), tempo-ul prin autocorelație (60-200 BPM) și grila de bătăi. Alege bătaia-țintă după --pick:
  drop   = bătaia după care muzica devine brusc mai puternică (intrarea refrenului / „drop”) - implicit
  strong = cea mai puternică bătaie din piesă
  first  = prima bătaie clară
Apoi taie muzica de la (bătaie - momentul --at din events.json), deci bătaia aleasă cade pe impact. Dacă bătaia e
prea devreme în piesă (offset negativ), alege următoarea candidată. Fade-in scurt (dacă nu pornește de la 0) și
fade-out 1,2 s; buclă (events.loop): fără fade-out. --sfx amestecă dedesubt și efectele din sound.py (fără dronă).
Loudnorm în două treceri (-14 LUFS). Video copiat fără re-encodare.
Scrie <iesire>_beats.png: forma de undă a muzicii tăiate cu linie roșie la impact și linii gri pe bătăi.
Agentul nu poate asculta: privește imaginea (bătaia mare trebuie să cadă pe linia roșie).
LICENȚĂ: muzica e a proprietarului sau are licență pentru utilizare; notezi sursa în brief.md.
"""
import argparse
import json
import math
import os
import re
import subprocess
import sys
import tempfile

SR, HOP = 11025, 110   # ~10 ms


def run(cmd):
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")


def duration(path):
    r = run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path])
    return float(r.stdout.strip())


def envelope(path, max_scan):
    """energie (dB) pe benzi joase și pe tot spectrul, la pas HOP."""
    out = {}
    for name, af in (("low", "highpass=f=35,lowpass=f=180"), ("full", "highpass=f=35")):
        r = subprocess.run(["ffmpeg", "-v", "error", "-t", str(max_scan), "-i", path, "-ac", "1", "-ar", str(SR),
                            "-af", af, "-f", "s16le", "-"], capture_output=True)
        if r.returncode:
            sys.exit("nu pot decoda muzica: " + r.stderr.decode("utf-8", "replace")[-400:])
        b = r.stdout
        n = len(b) // 2
        samples = memoryview(b).cast("h")
        env = []
        for i in range(0, n - HOP, HOP):
            seg = samples[i:i + HOP]
            e = sum(x * x for x in seg) / HOP
            env.append(10 * math.log10(e + 1.0))
        out[name] = env
    return out


def require_enough_audio(env, min_seconds=3.0):
    """a track that decodes to (almost) nothing would crash tempo() with a division by zero."""
    if len(env) < min_seconds * SR / HOP:
        sys.exit("piesa e goală sau prea scurtă pentru analiza ritmului (decodare < %.0f s)" % min_seconds)


def onset_strength(env):
    o = [0.0] + [max(0.0, env[i] - env[i - 1]) for i in range(1, len(env))]
    # netezire ușoară (3 cadre)
    return [(o[max(0, i - 1)] + o[i] + o[min(len(o) - 1, i + 1)]) / 3 for i in range(len(o))]


def tempo(o):
    fps = SR / HOP
    best, lag_best = -1, None
    mean = sum(o) / len(o)
    oc = [x - mean for x in o]
    for bpm in range(60, 201):
        lag = fps * 60.0 / bpm
        l0 = int(round(lag))
        s = sum(oc[i] * oc[i + l0] for i in range(0, len(oc) - l0, 2))
        # preferință ușoară pentru 90-140 BPM (evită dublarea/înjumătățirea)
        w = 1.0 if 90 <= bpm <= 140 else 0.85
        if s * w > best:
            best, lag_best = s * w, lag
    return 60.0 * fps / lag_best, lag_best


def grid(o, lag):
    """faza grilei de bătăi care prinde cel mai mult atac."""
    best, ph_best = -1, 0
    for ph in range(int(lag)):
        s, k = 0.0, ph
        while k < len(o):
            i = int(round(k))
            s += max(o[max(0, i - 2):i + 3]); k += lag
        if s > best:
            best, ph_best = s, ph
    beats, k = [], float(ph_best)
    while k < len(o):
        beats.append(int(round(k))); k += lag
    return beats


def candidates(beats, o, env_full, lag, pick):
    fps = SR / HOP
    win = int(2.0 * fps)
    def strength(i): return max(o[max(0, i - 2):i + 3])
    def drop(i):   # raportul energiilor LINIARE după/înainte (media în dB ar fi trasă în jos de liniștea dintre lovituri)
        a = env_full[max(0, i - win):i]; b = env_full[i:i + win]
        if not a or not b:
            return -99
        la = sum(10 ** (x / 10) for x in a) / len(a); lb = sum(10 ** (x / 10) for x in b) / len(b)
        return 10 * math.log10(lb / la)
    if pick == "first":
        thr = sorted(strength(i) for i in beats)[len(beats) // 2]
        return [i for i in beats if strength(i) >= thr]
    key = drop if pick == "drop" else strength
    return sorted(beats, key=key, reverse=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("video"); ap.add_argument("events"); ap.add_argument("music"); ap.add_argument("out")
    ap.add_argument("--at", default="impact", help="momentul din events.json pe care cade bătaia (impact, intro, sweep...)")
    ap.add_argument("--pick", default="drop", choices=["drop", "strong", "first"])
    ap.add_argument("--sfx", action="store_true", help="amestecă dedesubt whoosh/impact/shimmer din sound.py")
    ap.add_argument("--lufs", type=float, default=-14.0)
    ap.add_argument("--max-scan", type=float, default=150.0, help="câte secunde din piesă se analizează")
    a = ap.parse_args()
    for s in (sys.stdout, sys.stderr):
        s.reconfigure(encoding="utf-8")
    ev = json.load(open(a.events, encoding="utf-8"))
    vdur = duration(a.video)
    if abs(vdur - float(ev.get("duration", vdur))) > 0.1:
        sys.exit(f"durata video {vdur:.2f}s ≠ durata din events.json {ev['duration']}: events.json e de la altă randare")
    if a.at not in ev:
        sys.exit(f"events.json nu are '{a.at}' (are: {', '.join(k for k in ev if isinstance(ev[k], (int, float)))})")
    t_ev = float(ev[a.at]); loop = bool(ev.get("loop"))
    mdur = duration(a.music)
    env = envelope(a.music, a.max_scan)
    require_enough_audio(env["low"])
    o = onset_strength(env["low"])
    if max(o) < 1.0:   # fără kick (muzică ambientală): atacul pe tot spectrul
        o = onset_strength(env["full"])
    bpm, lag = tempo(o)
    beats = grid(o, lag)
    fps = SR / HOP
    chosen = None
    for i in candidates(beats, o, env["full"], lag, a.pick):
        start = i / fps - t_ev
        if start >= 0 and start + vdur <= mdur + 0.05:
            chosen = i; break
    if chosen is None:
        sys.exit("nicio bătaie potrivită: piesa e mai scurtă decât clipul sau impactul e prea târziu")
    start = chosen / fps - t_ev
    tmp = tempfile.mkdtemp(prefix="music_")
    fi = f"afade=t=in:d=0.25," if start > 0.3 else ""
    fo = "" if loop else f"afade=t=out:st={max(0.0, vdur - 1.2):.3f}:d=1.2,"
    base = f"[0:a]atrim=start={start:.4f}:duration={vdur:.4f},asetpts=PTS-STARTPTS,{fi}{fo}volume=1.0[m]"
    inputs = ["-ss", "0", "-i", a.music]
    fc = base
    mix = "[m]"
    dyn = "acompressor=threshold=0.125:ratio=3:attack=5:release=120,alimiter=limit=0.7,"   # altfel loudnorm liniar rămâne sub țintă (vârfuri)
    if a.sfx:
        sfx_vid = os.path.join(tmp, "sfx.mp4")
        r = run([sys.executable, "-I", os.path.join(os.path.dirname(os.path.abspath(__file__)), "sound.py"),
                 a.video, a.events, sfx_vid, "--mute-drone", "--lufs", "-20"])
        if r.returncode:
            sys.exit("sound.py a eșuat: " + r.stderr[-400:])
        inputs += ["-i", sfx_vid]
        fc += ";[1:a]volume=0.55[s];[m][s]amix=inputs=2:normalize=0[mx]"
        mix = "[mx]"
    # loudnorm în două treceri
    r = run(["ffmpeg", "-hide_banner", "-loglevel", "info", *inputs, "-filter_complex",
             fc + f";{mix}{dyn}loudnorm=I={a.lufs}:TP=-1.5:LRA=11:print_format=json[o]", "-map", "[o]", "-f", "null", "-"])
    m = re.search(r"\{[^{}]*\"input_i\"[^{}]*\}", r.stderr)
    if not m:
        sys.exit("loudnorm (trecerea 1) n-a dat măsurători: " + r.stderr[-400:])
    L = json.loads(m.group(0))
    ln = (f"loudnorm=I={a.lufs}:TP=-1.5:LRA=11:measured_I={L['input_i']}:measured_TP={L['input_tp']}:"
          f"measured_LRA={L['input_lra']}:measured_thresh={L['input_thresh']}:offset={L['target_offset']}:linear=true")
    vin = 2 if a.sfx else 1
    r = run(["ffmpeg", "-y", "-v", "error", *inputs, "-i", a.video, "-filter_complex",
             fc + f";{mix}{dyn}{ln},aresample=48000[o]", "-map", f"{vin}:v", "-map", "[o]", "-c:v", "copy",
             "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", a.out])
    if r.returncode:
        sys.exit("mux eșuat: " + r.stderr[-600:])
    # vizualizare: forma de undă + linie roșie la impact + bătăile din fereastră (gri)
    W, H = 1200, 300
    viz = os.path.splitext(a.out)[0] + "_beats.png"
    marks = [f"drawbox=x={int(t_ev / vdur * W)}:y=0:w=3:h={H}:color=red@0.9:t=fill"]
    for i in beats:
        t = i / fps - start
        if 0 <= t <= vdur:
            marks.append(f"drawbox=x={int(t / vdur * W)}:y={H - 30}:w=1:h=30:color=gray@0.8:t=fill")
    run(["ffmpeg", "-y", "-v", "error", "-i", a.out, "-filter_complex",   # ce se aude în clipul final
         f"[0:a]aformat=channel_layouts=mono,showwavespic=s={W}x{H}:colors=0xFFC060,format=rgb24,{','.join(marks)}",
         "-frames:v", "1", viz])
    rm = run(["ffmpeg", "-v", "info", "-i", a.out, "-af", "loudnorm=print_format=json", "-f", "null", "-"])
    mm = re.search(r"\{[^{}]*\"input_i\"[^{}]*\}", rm.stderr)
    lufs = float(json.loads(mm.group(0))["input_i"]) if mm else None
    print("MUSIC_DONE", json.dumps({"out": a.out, "bpm": round(bpm, 1), "music_start_s": round(start, 3),
                                    "beat_in_song_s": round(chosen / fps, 3), "lands_at_s": t_ev, "at": a.at,
                                    "pick": a.pick, "lufs": lufs, "sfx": a.sfx, "viz": viz}, ensure_ascii=False))


if __name__ == "__main__":
    main()
