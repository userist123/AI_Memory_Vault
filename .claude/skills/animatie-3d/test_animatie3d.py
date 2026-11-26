"""Tests for the animatie-3d skill: the UserPromptSubmit hook and the scripts that run without Blender,
plus one short Blender smoke test (skipped when Blender is not installed).

Run: python -I test_animatie3d.py        (from any directory; ffmpeg/ffprobe must be in PATH)
Scratch files go to E:/claude-work/animatie-3d/_tools/tmp when E: exists (C: is nearly full), else the system temp.
"""
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from argparse import Namespace
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SCRIPTS = ROOT / "scripts"
HOOK = ROOT / "hook_animatie3d.py"
BLENDER = Path("C:/Program Files/Blender Foundation/Blender 5.2/blender.exe")
SCRATCH = Path("E:/claude-work/animatie-3d/_tools/tmp")
HAVE_FFMPEG = bool(shutil.which("ffmpeg") and shutil.which("ffprobe"))


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def scratch_dir():
    if SCRATCH.parent.is_dir():
        SCRATCH.mkdir(parents=True, exist_ok=True)
        return tempfile.TemporaryDirectory(dir=str(SCRATCH))
    return tempfile.TemporaryDirectory()


def run_hook(prompt=None, raw=None):
    data = raw if raw is not None else json.dumps({"prompt": prompt})
    r = subprocess.run([sys.executable, "-I", str(HOOK)], input=data, capture_output=True,
                       text=True, encoding="utf-8")
    return r


def hook_context(prompt):
    r = run_hook(prompt)
    assert r.returncode == 0, r.stderr
    if not r.stdout.strip():
        return None
    return json.loads(r.stdout)["hookSpecificOutput"]["additionalContext"]


class HookTests(unittest.TestCase):
    TRIGGERS = [
        "Fă-mi un logo reveal 3D pentru firma mea",
        "vreau o animație 3D cu produsul care se rotește",
        "randează în Blender o scenă cu text 3D",
        "Fă un intro pentru Reels cu logo-ul",
        "make a turntable of this .glb model",
        "need a lower third with my name",
        "animație three.js cu particule",
    ]
    SILENT = [
        "Bună, ce faci?",
        "Fă-mi un raport din logurile de ieri",
        "/clip https://twitch.tv/x",
        "Repară site-ul three.js care nu se încarcă, fix pagina",
        # subagent hand-backs and notifications must never trigger the hook
        '<agent-message from="a1">[Subagent hand-back] Blender render finished, logo reveal 3D done</agent-message>',
        "<task-notification>\nBlender animatie 3D render complete</task-notification>",
        "<system-reminder>\nthe owner wants a logo 3D intro\n</system-reminder>",
        "  \n<agent-message from=\"x\">turntable blender</agent-message>",
        "[Subagent hand-back] animație 3D gata",
    ]

    def test_triggers_emit_context(self):
        for p in self.TRIGGERS:
            with self.subTest(p=p):
                ctx = hook_context(p)
                self.assertIsNotNone(ctx, "hook was silent")
                self.assertIn("animatie-3d", ctx)

    def test_silent_cases(self):
        for p in self.SILENT:
            with self.subTest(p=p):
                self.assertIsNone(hook_context(p), "hook should stay silent")

    def test_context_reads_as_fact_not_command(self):
        ctx = hook_context(self.TRIGGERS[0])
        for bad in ("invocă", "înainte de orice", "you must", "ignore", "MUST"):
            self.assertNotIn(bad.lower(), ctx.lower())

    def test_robust_to_bad_input_exit_zero(self):
        for raw in ("", "not json", "[]", '{"prompt": null}', '{"prompt": 5}', '{}'):
            with self.subTest(raw=raw):
                r = run_hook(raw=raw)
                self.assertEqual(r.returncode, 0)
                self.assertEqual(r.stdout.strip(), "")

    def test_output_is_valid_hook_json(self):
        r = run_hook(self.TRIGGERS[0])
        out = json.loads(r.stdout)
        self.assertEqual(out["hookSpecificOutput"]["hookEventName"], "UserPromptSubmit")


@unittest.skipUnless(HAVE_FFMPEG, "ffmpeg missing")
class FinalizeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fin = load("finalize", SCRIPTS / "finalize.py")
        cls.td = scratch_dir()
        cls.dir = Path(cls.td.name)

    @classmethod
    def tearDownClass(cls):
        cls.td.cleanup()

    def make_frames(self, name, n=30, alpha=False):
        d = self.dir / name / "final"
        d.mkdir(parents=True)
        src = "testsrc2=s=160x90:r=30:d=%s" % (n / 30)
        vf = "format=rgba,colorchannelmixer=aa=0.5" if alpha else "format=rgb24"
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", src, "-vf", vf, "-start_number", "1",
                        str(d / "f_%04d.png")], check=True)
        ev = {"fps": 30, "frame_start": 1, "frame_end": n, "frames_dir": str(d), "partial": False,
              "alpha": alpha, "settled": 0.0}
        (self.dir / name / "events.json").write_text(json.dumps(ev), encoding="utf-8")
        return d

    def probe(self, path, entries):
        r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", entries,
                            "-of", "json", str(path)], capture_output=True, text=True)
        return json.loads(r.stdout)

    def test_filter_and_codec_pure_functions(self):
        vf = self.fin.video_filter(alpha=False, look="none", lut=None, pix_fmt="yuv420p")
        self.assertIn("out_color_matrix=bt709", vf)
        self.assertIn("out_range=tv", vf)
        self.assertIn("format=yuv420p,setparams=", vf)
        self.assertTrue(vf.endswith("setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=tv"))
        self.assertIn("pad=ceil(iw/2)*2:ceil(ih/2)*2", vf)
        self.assertIn("rgba", self.fin.video_filter(True, "none", None, "yuva420p"))
        self.assertIn("colorbalance", self.fin.video_filter(False, "warm", None, "yuv420p"))
        mp4 = self.fin.codec_args(".mp4", alpha=False, crf=18)
        for flag in ("libx264", "yuv420p", "+faststart", "-colorspace", "bt709", "-color_trc"):
            self.assertIn(flag, mp4)
        self.assertTrue(any("colorprim=bt709" in x for x in mp4))
        webm = self.fin.codec_args(".webm", alpha=True, crf=18)
        self.assertIn("yuva420p", webm)
        self.assertIn("-auto-alt-ref", webm)   # required by libvpx-vp9 for alpha
        self.assertIn("yuva444p10le", self.fin.codec_args(".mov", alpha=True, crf=18))

    def test_mp4_is_bt709_tagged_and_colour_correct(self):
        d = self.make_frames("mp4")
        out = self.dir / "mp4" / "clip.mp4"
        r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "finalize.py"), str(d), str(out)],
                           capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        st = self.probe(out, "stream=pix_fmt,color_space,color_transfer,color_primaries,color_range")["streams"][0]
        self.assertEqual(st["pix_fmt"], "yuv420p")
        self.assertEqual(st["color_space"], "bt709")
        self.assertEqual(st["color_transfer"], "bt709")
        self.assertEqual(st["color_primaries"], "bt709")
        self.assertEqual(st["color_range"], "tv")
        head = out.read_bytes()[:4096 * 8]
        self.assertTrue(head.find(b"moov") != -1, "faststart: moov atom must precede mdat")
        self.assertLess(head.find(b"moov"), len(head))
        # colour accuracy: decode as a bt709 player would; pure red (224,48,32) must stay within a few levels
        red = self.dir / "red.png"
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "color=c=0xE03020:s=64x64:d=0.4,format=rgb24",
                        "-start_number", "1", str(self.dir / "r_%04d.png")], check=True)
        rd = self.dir / "redf"
        rd.mkdir()
        for i in range(1, 13):
            shutil.copy(self.dir / "r_0001.png", rd / f"f_{i:04d}.png")
        (self.dir / "ev_red.json").write_text(json.dumps({"fps": 30, "frame_start": 1, "frame_end": 12,
                                                          "frames_dir": str(rd)}), encoding="utf-8")
        rout = self.dir / "red.mp4"
        r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "finalize.py"), str(rd), str(rout),
                            "--events", str(self.dir / "ev_red.json"), "--allow-freeze", "5"],
                           capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        px = subprocess.run(["ffmpeg", "-v", "error", "-i", str(rout), "-frames:v", "1", "-vf",
                             "scale=in_color_matrix=bt709:in_range=tv,format=rgb24", "-f", "rawvideo", "-"],
                            capture_output=True).stdout[:3]
        for got, want in zip(px, (224, 48, 32)):
            self.assertLessEqual(abs(got - want), 6, f"colour shifted: {list(px)}")

    def test_alpha_mp4_rejected_and_webm_mov_ok(self):
        d = self.make_frames("alpha", alpha=True)
        r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "finalize.py"), str(d),
                            str(self.dir / "alpha" / "a.mp4")], capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(r.returncode, 1)
        self.assertIn(".mp4", r.stdout)
        rep_path = self.dir / "alpha" / "a_report.json"
        self.assertTrue(rep_path.is_file(), "alpha + .mp4 must still write _report.json")
        rep = json.loads(rep_path.read_text(encoding="utf-8"))
        self.assertFalse(rep["ok"])
        self.assertTrue(any(".mp4" in x for x in rep["problems"]), rep)
        rgb = self.make_frames("rgbcont")
        cases = ((d, "a", ".webm", True), (d, "a", ".mov", True), (rgb, "c", ".webm", False), (rgb, "c", ".mov", False))
        for frames, stem, ext, alpha in cases:
            out = frames.parent / (stem + ext)
            r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "finalize.py"), str(frames), str(out)],
                               capture_output=True, text=True, encoding="utf-8")
            with self.subTest(ext=ext, alpha=alpha):
                self.assertEqual(r.returncode, 0, ext + r.stdout + r.stderr)
                self.assertTrue(out.stat().st_size > 0)
                st = self.probe(out, "stream=pix_fmt,color_space,color_transfer,color_primaries:stream_tags=alpha_mode")["streams"][0]
                for key in ("color_space", "color_transfer", "color_primaries"):
                    self.assertEqual(st.get(key), "bt709", f"{ext} alpha={alpha} {key}: {st}")
                if ext == ".webm" and alpha:
                    self.assertEqual(st.get("tags", {}).get("alpha_mode"), "1")
                if ext == ".mov" and alpha:
                    self.assertTrue(st["pix_fmt"].startswith("yuva444p"), st)

    def test_missing_frames_and_partial_events_flagged(self):
        d = self.make_frames("gap")
        (d / "f_0010.png").unlink()
        r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "finalize.py"), str(d), str(self.dir / "gap" / "g.mp4")],
                           capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(r.returncode, 1)
        self.assertIn("lipsesc", r.stdout)

    def test_events_without_frame_start_does_not_crash(self):
        d = self.make_frames("nostart")
        ev_path = self.dir / "nostart" / "events.json"
        ev = json.loads(ev_path.read_text(encoding="utf-8"))
        del ev["frame_start"]
        ev_path.write_text(json.dumps(ev), encoding="utf-8")
        r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "finalize.py"), str(d), str(self.dir / "nostart" / "n.mp4")],
                           capture_output=True, text=True, encoding="utf-8")
        self.assertNotIn("Traceback", r.stderr)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)

    def test_fps_mismatch_flagged(self):
        d = self.make_frames("fps")
        r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "finalize.py"), str(d), str(self.dir / "fps" / "f.mp4"),
                            "--fps", "24"], capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(r.returncode, 1)
        self.assertIn("fps", r.stdout)


@unittest.skipUnless(HAVE_FFMPEG, "ffmpeg missing")
class SoundTests(unittest.TestCase):
    def test_build_layers_depend_on_events(self):
        snd = load("sound", SCRIPTS / "sound.py")
        a = Namespace(video="v.mp4", gain=0.0)
        only_drone = " ".join(snd.build(a, {"duration": 3.0}, "o.wav", False))
        self.assertNotIn("[wh]", only_drone)
        full = " ".join(snd.build(a, {"duration": 3.0, "intro": 0.3, "impact": 1.2, "sweep": 1.3, "subtitle": 2.0},
                                  "o.wav", False))
        for lab in ("[wh]", "[im]", "[sh]", "[tk]"):
            self.assertIn(lab, full)
        loop = " ".join(snd.build(a, {"duration": 3.0, "loop": True}, "o.wav", False))
        self.assertNotIn("afade=t=out", loop)

    def test_end_to_end_lufs_and_duration_guard(self):
        # 5 s clip: very short (<3 s) clips land below target because loudnorm integrates over too little signal
        with scratch_dir() as td:
            td = Path(td)
            vid = td / "v.mp4"
            subprocess.run(["ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=30:d=5",
                            "-c:v", "libx264", "-pix_fmt", "yuv420p", str(vid)], check=True)
            ev = td / "events.json"
            ev.write_text(json.dumps({"duration": 5.0, "intro": 0.3, "impact": 1.7, "sweep": 1.8}), encoding="utf-8")
            out = td / "o.mp4"
            r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "sound.py"), str(vid), str(ev), str(out)],
                               capture_output=True, text=True, encoding="utf-8")
            self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
            res = json.loads(re.search(r"SOUND_DONE (\{.*\})", r.stdout).group(1))
            self.assertLess(abs(res["lufs"] - (-14.0)), 1.5)
            self.assertLessEqual(res["peak_dbfs"], -0.9)
            self.assertTrue((td / "o_viz.png").exists())
            ev.write_text(json.dumps({"duration": 9.0}), encoding="utf-8")
            r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "sound.py"), str(vid), str(ev), str(td / "o2.mp4")],
                               capture_output=True, text=True, encoding="utf-8")
            self.assertNotEqual(r.returncode, 0)


class MusicTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.m = load("music", SCRIPTS / "music.py")

    def click_env(self, bpm, seconds=20):
        fps = self.m.SR / self.m.HOP
        n = int(seconds * fps)
        period = fps * 60.0 / bpm
        env = [0.0] * n
        k = 0.0
        while k < n:
            env[int(k)] = 40.0
            k += period
        return env

    def test_tempo_detects_click_track(self):
        for bpm in (100, 120):
            with self.subTest(bpm=bpm):
                o = self.m.onset_strength(self.click_env(bpm))
                got, _ = self.m.tempo(o)
                self.assertLess(abs(got - bpm), 3.0)

    def test_grid_and_candidates(self):
        env = self.click_env(120)
        o = self.m.onset_strength(env)
        _, lag = self.m.tempo(o)
        beats = self.m.grid(o, lag)
        self.assertGreater(len(beats), 30)
        for pick in ("first", "strong", "drop"):
            self.assertTrue(self.m.candidates(beats, o, env, lag, pick))

    def test_too_short_audio_exits_cleanly(self):
        with self.assertRaises(SystemExit):
            self.m.require_enough_audio([0.0] * 10)


class ContactSheetTests(unittest.TestCase):
    @unittest.skipUnless(HAVE_FFMPEG, "ffmpeg missing")
    def test_sheet_from_frames_and_video(self):
        with scratch_dir() as td:
            td = Path(td)
            fr = td / "fr"
            fr.mkdir()
            subprocess.run(["ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=30:d=1",
                            "-start_number", "1", str(fr / "f_%04d.png")], check=True)
            sheet = td / "s.png"
            r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "contact_sheet.py"), str(fr), str(sheet),
                                "--n", "6", "--cols", "3"], capture_output=True, text=True, encoding="utf-8")
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertIn("f_0001.png", r.stdout)
            self.assertIn("f_0030.png", r.stdout)
            self.assertGreater(sheet.stat().st_size, 1000)
            vid = td / "v.mp4"
            subprocess.run(["ffmpeg", "-y", "-v", "error", "-framerate", "30", "-i", str(fr / "f_%04d.png"),
                            "-c:v", "libx264", "-pix_fmt", "yuv420p", str(vid)], check=True)
            sheet2 = td / "v.png"
            r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "contact_sheet.py"), str(vid), str(sheet2),
                                "--full", "2"], capture_output=True, text=True, encoding="utf-8")
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertTrue(list(td.glob("v_full_cadru*.png")))
            r = subprocess.run([sys.executable, "-I", str(SCRIPTS / "contact_sheet.py"), str(td / "nope"), str(sheet)],
                               capture_output=True, text=True, encoding="utf-8")
            self.assertNotEqual(r.returncode, 0)


class VariantsAndRegressionTests(unittest.TestCase):
    def test_blender_exe_found_or_exits(self):
        v = load("variants", SCRIPTS / "variants.py")
        if BLENDER.exists():
            self.assertTrue(os.path.exists(v.blender_exe()))
        else:
            with self.assertRaises(SystemExit):
                v.blender_exe()

    def test_regression_make_scene_replaces_one_constant(self):
        reg = load("regression", SCRIPTS / "regression.py")
        with scratch_dir() as td:
            dest = Path(td) / "scena.py"
            reg.make_scene('MODE = "reveal"   # comentariu\nSECONDS = 5.0\n', {"MODE": '"turntable"'}, dest)
            txt = dest.read_text(encoding="utf-8")
            self.assertIn('MODE = "turntable"   # comentariu', txt)
            self.assertIn("SECONDS = 5.0", txt)
            with self.assertRaises(SystemExit):
                reg.make_scene("A = 1\n", {"MISSING": "2"}, dest)
            with self.assertRaises(SystemExit):
                reg.make_scene("MODE = 1\nMODE = 2\n", {"MODE": "3"}, dest)


@unittest.skipUnless(BLENDER.exists(), "Blender not installed")
class BlenderSmokeTests(unittest.TestCase):
    def test_check_only_and_one_preview_frame(self):
        with scratch_dir() as td:
            td = Path(td)
            shutil.copy(SCRIPTS / "blender_template.py", td / "scena.py")
            base = [str(BLENDER), "-b", "--factory-startup", "--python-exit-code", "1", "-P", str(td / "scena.py"), "--"]
            r = subprocess.run(base + ["--out", str(td / "chk"), "--check-only"], capture_output=True, text=True,
                               encoding="utf-8", errors="replace", timeout=55)
            self.assertEqual(r.returncode, 0, r.stdout[-800:] + r.stderr[-800:])
            m = re.search(r"ANIM3D_FRAMING (\{.*\})", r.stdout)
            self.assertIsNotNone(m, r.stdout[-800:])
            self.assertTrue(json.loads(m.group(1))["ok"])
            r = subprocess.run(base + ["--out", str(td / "pv"), "--mode", "preview", "--frames", "60"],
                               capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=55)
            self.assertEqual(r.returncode, 0, r.stdout[-800:] + r.stderr[-800:])
            self.assertIn("ANIM3D_DONE", r.stdout)
            self.assertTrue(list((td / "pv").rglob("*.png")))


if __name__ == "__main__":
    unittest.main(verbosity=1)
