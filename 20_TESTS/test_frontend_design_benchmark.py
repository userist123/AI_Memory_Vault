import importlib.util
import json
import math
from pathlib import Path
from types import SimpleNamespace

import pytest

_P = Path(__file__).resolve().parents[1] / "30_SCRIPTS" / "evaluation" / "frontend_design_benchmark.py"
_spec = importlib.util.spec_from_file_location("fdb", _P)
fdb = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fdb)

st = fdb.static_tells


def css(body):
    return f"<html><head><style>{body}</style></head><body></body></html>"


@pytest.mark.parametrize("fam,exp", [("Inter, sans-serif", True), ("'Fraunces', serif", False),
                                     ("system-ui", True), ("'Segoe UI', Arial", True)])
def test_generic_font(fam, exp):
    assert st(css(f"body {{ font-family: {fam}; }}"))["generic_font"] is exp


def test_generic_font_prefers_body_over_first():
    h = css("h1 { font-family: Inter; } body { font-family: 'Fraunces', serif; }")
    assert st(h)["generic_font"] is False
    assert st(css("h1 { font-family: Poppins; }"))["generic_font"] is True


def test_cream_and_terracotta():
    assert st(css("a{background:#f4f1ea}"))["cream_bg"]
    assert st(css("a{background:#f2efe8}"))["cream_bg"]
    assert not st(css("a{background:#ffffff}"))["cream_bg"]
    assert st(css("a{color:#d97757}"))["terracotta"]
    assert st(css("a{color:rgb(220,120,90)}"))["terracotta"]
    assert not st(css("a{color:#2244aa}"))["terracotta"]


def test_near_black():
    for c in ("#0b0b0b", "#111", "#111111", "#0a0a0a"):
        assert st(css(f"a{{color:{c}}}"))["near_black_tint"], c
    assert not st(css("a{color:#1112aa}"))["near_black_tint"]
    assert not st(css("a{color:#000}"))["near_black_tint"]


def test_purple_gradient_hex_rgb_and_negatives():
    assert st(css("a{background:linear-gradient(135deg,#667eea,#764ba2)}"))["purple_gradient"]
    assert st(css("a{background:radial-gradient(circle, rgb(120,60,200), #fff)}"))["purple_gradient"]
    assert st(css("a{background:linear-gradient(#8b5cf6,#fff)}"))["purple_gradient"]
    assert not st(css("a{background:linear-gradient(#ff0000,#00ff00)}"))["purple_gradient"]
    assert not st(css("a{background:#7c3aed}"))["purple_gradient"]  # not in a gradient
    assert not st(css("a{background:linear-gradient(#999,#aaa)}"))["purple_gradient"]  # low saturation
    assert fdb.hue_sat((120, 60, 200))[0] == pytest.approx(266.4, abs=1)


def test_middot_arrow_numbered():
    h = "<style>.x{content:' \u00b7 '}</style><p>a \u00b7 b \u00b7 c</p><a href=#>Go \u2192</a><button>Buy \u2192</button><p>\u2192 out</p><p>01 02 03 10</p>"
    t = st(h)
    assert t["middot_meta"] == 2
    assert t["arrow_ctas"] == 2
    assert t["numbered_markers"] == 3
    n = st("<p>plain text 2024</p>")
    assert (n["middot_meta"], n["arrow_ctas"], n["numbered_markers"]) == (0, 0, 0)


def test_shadow_radius_gradient_fade():
    h = css("a{box-shadow:0 1px 3px rgba(0,0,0,0.1)} b{box-shadow:0 2px rgba(0, 0, 0, .08)} "
            "c{border-radius:8px} d{border-radius:8px} e{border-radius:12px} f{background:linear-gradient(red,blue)}")
    t = st(h)
    assert t["soft_shadow"] == 2 and t["radius_values"] == 2 and t["gradients"] == 1
    assert st(css("a{box-shadow:0 1px rgba(0,0,0,0.5)}"))["soft_shadow"] == 0
    fade = "<style>.r{opacity:0;transform:translateY(20px);transition:opacity .6s}</style><script>new IntersectionObserver(()=>{})</script>"
    assert st(fade)["fade_reveal"] is True
    assert st("<style>.r{transition:opacity .6s}</style>")["fade_reveal"] is False
    assert st("<script>new IntersectionObserver(()=>{})</script>")["fade_reveal"] is False


def test_focus_motion_and_score():
    t = st(css("a:focus-visible{outline:1px solid} @media (prefers-reduced-motion: reduce){*{animation:none}}"))
    assert t["has_focus_visible"] and t["has_reduced_motion"]
    t0 = st("<p>hi</p>")
    assert not t0["has_focus_visible"] and not t0["has_reduced_motion"] and t0["tell_score"] == 0.0
    bad = css("body{font-family:Inter;background:#f4f1ea;color:#111} a{color:#d97757}")
    assert st(bad)["tell_score"] == 4.0


FAKE = "\n".join([
    "warning: not json",
    json.dumps({"type": "system", "subtype": "init", "skills": ["frontend-design:frontend-design", "pdf", "ui-design"]}),
    "{broken json",
    json.dumps({"type": "assistant", "message": {"content": [
        {"type": "tool_use", "name": "Skill", "input": {"skill": "frontend-design:frontend-design"}},
        {"type": "tool_use", "name": "Write", "input": {"file_path": "index.html"}},
        {"type": "text", "text": "x"}]}}),
    json.dumps({"type": "assistant", "message": {"content": [{"type": "tool_use", "name": "Write", "input": {}}]}}),
    json.dumps({"type": "result", "total_cost_usd": 0.1, "num_turns": 2}),
    json.dumps({"type": "result", "subtype": "success", "is_error": False, "total_cost_usd": 0.42,
                "num_turns": 7, "duration_ms": 1234}),
])


def test_parse_stream():
    r = fdb.parse_stream(FAKE)
    assert r["init_skills_design"] == ["frontend-design:frontend-design", "ui-design"]
    assert r["skill_invoked"] == ["frontend-design:frontend-design"]
    assert r["total_cost_usd"] == 0.42 and r["num_turns"] == 7 and r["duration_ms"] == 1234
    assert r["tool_calls"] == {"Skill": 1, "Write": 2}
    assert r["subtype"] == "success" and r["is_error"] is False
    assert fdb.parse_stream("garbage")["total_cost_usd"] is None


@pytest.mark.parametrize("arm,n", [("baseline", 0), ("official", 1), ("vault", 1)])
def test_build_command(arm, n):
    cmd = fdb.build_command("claude", "P", arm, "sonnet", 25, 0.9, "/off", "/vault")
    assert cmd.count("--plugin-dir") == n
    assert cmd[:3] == ["claude", "-p", "P"]
    assert "--strict-mcp-config" in cmd and cmd[cmd.index("--allowedTools") + 1] == "Write,Edit,Read,Skill"
    if arm == "official":
        assert cmd[cmd.index("--plugin-dir") + 1] == "/off"
    if arm == "vault":
        assert cmd[cmd.index("--plugin-dir") + 1] == "/vault"
    with pytest.raises(ValueError):
        fdb.build_command("claude", "P", "vault", "sonnet", 1, 1.0, "/off", None)


def test_git_refusal(tmp_path):
    (tmp_path / ".git").mkdir()
    work = tmp_path / "a" / "b" / "work"
    assert fdb.inside_git_repo(work)
    with pytest.raises(RuntimeError, match="git"):
        fdb.check_work_dir(work)


def test_nonempty_work_refused(tmp_path, monkeypatch):
    monkeypatch.setattr(fdb, "inside_git_repo", lambda p: False)
    w = tmp_path / "w"
    w.mkdir()
    (w / "x").write_text("1")
    with pytest.raises(RuntimeError, match="not empty"):
        fdb.check_work_dir(w)


def test_budget_logic():
    assert not fdb.budget_exhausted(5.49, 5.5)
    assert fdb.budget_exhausted(5.5, 5.5)


def test_run_budget_stop_and_redaction(tmp_path, monkeypatch):
    monkeypatch.setattr(fdb, "inside_git_repo", lambda p: False)
    home = str(Path.home())
    calls = []

    def runner(cmd, work):
        calls.append(cmd)
        return {"exit_code": 0, "total_cost_usd": 1.0, "note": f"{home}/secret"}

    args = SimpleNamespace(out=str(tmp_path), tasks="T1", arms="baseline,official,vault", model="sonnet",
                           max_turns=5, max_budget_usd=1.0, max_total_usd=2.0, official_dir="/o",
                           vault_dir=str(tmp_path), dry_run=False)
    assert fdb.cmd_run(args, runner=runner) == 0
    runs = json.loads((tmp_path / "RUNS.json").read_text("utf-8"))
    assert len(calls) == 2
    assert runs[2] == {"task": "T1", "arm": "vault", "status": "skipped_budget"}
    assert runs[0]["note"] == "<HOME>/secret"
    assert (tmp_path / "runs" / "T1_baseline" / "run.json").is_file()


def test_vault_dir_required(tmp_path):
    args = SimpleNamespace(out=str(tmp_path), tasks="T1", arms="vault", model="s", max_turns=1,
                           max_budget_usd=1, max_total_usd=1, official_dir="/o", vault_dir=None, dry_run=True)
    assert fdb.cmd_run(args) == 2


def test_redact_variants():
    h = "C:\\Users\\Someone"
    out = fdb.redact({"a": "C:\\Users\\Someone\\x", "b": ["C:/Users/Someone/y", 3]}, home=h)
    assert out == {"a": "<HOME>\\x", "b": ["<HOME>/y", 3]}


def test_judge_pack_deterministic_and_key_outside(tmp_path):
    for t in fdb.TASKS:
        for a in fdb.ARMS:
            d = tmp_path / "runs" / f"{t}_{a}"
            d.mkdir(parents=True)
            for vp in ("desktop", "mobile"):
                (d / f"shot_{vp}.png").write_bytes(f"{t}{a}{vp}".encode())
    args = SimpleNamespace(out=str(tmp_path), seed=20261021)
    fdb.cmd_judge_pack(args)
    key1 = (tmp_path / "judge_key.json").read_text("utf-8")
    fdb.cmd_judge_pack(args)
    assert key1 == (tmp_path / "judge_key.json").read_text("utf-8")
    key = json.loads(key1)
    for t in fdb.TASKS:
        assert sorted(key[t].values()) == sorted(fdb.ARMS)
        letter = "A"
        assert (tmp_path / "judge" / t / f"{letter}_desktop.png").read_bytes() == \
            f"{t}{key[t][letter]}desktop".encode()
    assert not list((tmp_path / "judge").rglob("judge_key.json"))
    briefs = (tmp_path / "judge" / "BRIEFS.md").read_text("utf-8")
    assert fdb.TASKS["T2"] in briefs and "Deliver a single" not in briefs
    assert "bicycle" not in key1 and "baseline" not in briefs


def test_task_text_constants():
    assert "Spi\u021berul" in fdb.TASKS["T1"] and "Timi\u0219oara" in fdb.TASKS["T3"]
    assert fdb.SUFFIX.startswith(" Deliver a single")


def test_clip_width_shows_overflow():
    assert fdb.clip_width(390, 600) == 600
    assert fdb.clip_width(1440, 1000) == 1440
    assert fdb.clip_width(390, None) == 390


def test_cdp_buffers_events_and_console_errors():
    import json as _j

    class WS:
        def __init__(self, msgs):
            self.q = [_j.dumps(m) for m in msgs]
            self.sent = []

        def send(self, d):
            self.sent.append(d)

        def recv(self):
            if not self.q:
                raise TimeoutError
            return self.q.pop(0)

        def settimeout(self, t):
            pass

    c = fdb.CDP.__new__(fdb.CDP)
    c.n = 0
    c.errors = []
    c.events = []
    c.ws = WS([
        {"method": "Page.loadEventFired", "params": {}},
        {"method": "Runtime.consoleAPICalled", "params": {"type": "error", "args": [{"value": "boom"}]}},
        {"method": "Runtime.consoleAPICalled", "params": {"type": "log", "args": [{"value": "fine"}]}},
        {"id": 1, "result": {"ok": 1}},
    ])
    assert c.call("Page.navigate") == {"ok": 1}
    assert c.errors == ["boom"]
    assert c.wait_event("Page.loadEventFired", 1) is True  # arrived before the response, not dropped


def test_metrics_js_page_level_overflow_semantics():
    js = fdb.METRICS_JS
    assert "scrollWidth" in js and "window.__vw" in js and "innerWidth" in js
    assert "max_element_right_px" in js
    assert "horizontal_overflow: (sw > vw + 1)" in js


def test_metrics_js_colour_parser_and_reasons():
    js = fdb.METRICS_JS
    # canvas-free CSS Color 4 parsing; unknown formats are recorded as unparsed, never guessed
    assert "getImageData" not in js and "fillStyle" not in js
    for fmt in ("oklch", "oklab", "lab", "lch", "srgb"):
        assert f"'{fmt}'" in js
    assert "unparsed_color:" in js and "unparsed_color_formats" in js
    assert "min_text_contrast_reason" in js and "no_measurable_text" in js and "all_transparent" in js
    assert "all_background_images" in js and "const over =" in js  # alpha compositing over the next layer
    assert "zero_opacity_text_elements" in js


def test_measure_pins_colour_scheme_and_reports_dark_separately():
    src = Path(fdb.__file__).read_text(encoding="utf-8")
    assert "Emulation.setEmulatedMedia" in src and "prefers-color-scheme" in src
    assert "dark_scheme" in src


def test_oklch_parsing():
    r, g, b = fdb.parse_colors("color: oklch(62.8% 0.2577 29.23)")[0]
    assert abs(r - 255) <= 3 and g <= 3 and b <= 3
    assert fdb.parse_colors("oklch(0.628 0.2577 29.23)")[0] == (r, g, b)
    terra = fdb.oklch_to_rgb(0.6683, 0.1279, 37.6)  # approx #d97757
    assert math.dist(terra, (0xD9, 0x77, 0x57)) <= 12
    assert st(css(".a{background:oklch(66.83% 0.1279 37.6)}"))["terracotta"] is True


def test_judge_pack_latin_square(tmp_path):
    for t in fdb.TASKS:
        for a in fdb.ARMS[:3]:
            (tmp_path / "runs" / f"{t}_{a}").mkdir(parents=True)
    fdb.cmd_judge_pack(SimpleNamespace(out=str(tmp_path), seed=7))
    key = json.loads((tmp_path / "judge_key.json").read_text("utf-8"))
    tasks = list(fdb.TASKS)[:3]
    assert len(tasks) == 3
    for arm in fdb.ARMS[:3]:
        assert sorted(next(lt for lt, a in key[t].items() if a == arm) for t in tasks) == ["A", "B", "C"]


def test_timeout_without_cost_counts_budget(tmp_path, monkeypatch):
    monkeypatch.setattr(fdb, "inside_git_repo", lambda p: False)
    calls = []

    def runner(cmd, work):
        calls.append(1)
        return {"exit_code": "timeout", "total_cost_usd": None}

    args = SimpleNamespace(out=str(tmp_path), tasks="T1", arms="baseline,official,vault", model="sonnet",
                           max_turns=5, max_budget_usd=1.0, max_total_usd=2.0, official_dir="/o",
                           vault_dir=str(tmp_path), dry_run=False)
    fdb.cmd_run(args, runner=runner)
    runs = json.loads((tmp_path / "RUNS.json").read_text("utf-8"))
    assert len(calls) == 2 and runs[2]["status"] == "skipped_budget"


def test_forced_arms_prefix_prompt_and_load_one_plugin():
    p_forced = fdb.full_prompt("T1", "official_forced")
    assert p_forced.startswith(fdb.FORCE_PREFIX)
    assert not fdb.full_prompt("T1", "official").startswith(fdb.FORCE_PREFIX)
    assert not fdb.full_prompt("T1", "baseline").startswith(fdb.FORCE_PREFIX)
    for arm, d in (("official_forced", "OFF"), ("vault_forced", "VLT")):
        cmd = fdb.build_command("claude", "x", arm, "sonnet", 5, 0.5, "OFF", "VLT")
        assert cmd.count("--plugin-dir") == 1 and cmd[cmd.index("--plugin-dir") + 1] == d


def test_director_arm_loads_official_and_director_without_force():
    cmd = fdb.build_command("claude", "x", "director_official", "sonnet", 5, 0.5, "OFF", "VLT", "DIR")
    assert cmd.count("--plugin-dir") == 2 and "OFF" in cmd and "DIR" in cmd and "VLT" not in cmd
    assert not fdb.full_prompt("T1", "director_official").startswith(fdb.FORCE_PREFIX)
    with pytest.raises(ValueError):
        fdb.build_command("claude", "x", "director_official", "sonnet", 5, 0.5, "OFF", "VLT")
