"""A/B/C benchmark harness for a Claude Code design skill on UI tasks.

Arms differ ONLY by one --plugin-dir. Subcommands: run, measure, judge-pack.
Real `claude -p` runs cost money; use --dry-run to inspect commands.
"""
from __future__ import annotations

import argparse
import colorsys
import html as htmllib
import json
import math
import os
import random
import re
import shutil
import socket
import statistics
import subprocess
import sys
import tempfile
import time
from pathlib import Path

TASKS = {
    "T1": "Build a landing page for 'Spițerul', a small independent bicycle repair co-op in Cluj-Napoca "
          "that also runs evening repair classes. Include services with prices, the class schedule, "
          "and location/contact.",
    "T2": "Build the main screen of an internal web dashboard for a hospital pharmacy team that tracks "
          "medicine stock levels, batches close to expiry, and pending ward orders. Use realistic sample data.",
    "T3": "Build an event page for Corp, a three-day contemporary dance festival in Timișoara: "
          "programme, venues and tickets.",
}
SUFFIX = (" Deliver a single self-contained file named index.html in the current directory (inline CSS and JS; "
          "web fonts from Google Fonts are allowed; no other external assets or images). Work autonomously and "
          "do not ask questions. When finished, reply with one short sentence.")
ARMS = ("baseline", "official", "vault", "official_forced", "vault_forced", "director_official")
# Round 3: production path - creative-web-director (which names frontend-design) loaded next to the official plugin,
# no forced prompt. Does the director skill fire, and does it pull frontend-design in?
# Round 2: the skill was never auto-invoked in round 1, so *_forced arms name it in the prompt (as creative-web-director
# does in production) to measure the skill's effect when it is actually loaded.
FORCE_PREFIX = "Use the frontend-design skill for this task. "
DEFAULT_OFFICIAL = "~/.claude/plugins/cache/anthropic-plugin-directory/frontend-design/d1adba47161a-65ba16f3"
TIMEOUT_S = 1500
VIEWPORTS = {"desktop": (1440, 900, False), "mobile": (390, 844, True)}
MAX_SHOT_HEIGHT = 4000


# ----------------------------------------------------------------- helpers
def redact(obj, home: str | None = None):
    """Replace absolute home paths in all strings of a JSON-like structure with <HOME>."""
    homes = {home or str(Path.home())}
    homes |= {h.replace("\\", "/") for h in list(homes)} | {h.replace("/", "\\") for h in list(homes)}
    homes |= {h.replace("\\", "\\\\") for h in list(homes)}
    homes = sorted((h for h in homes if h), key=len, reverse=True)

    def red(o):
        if isinstance(o, str):
            for h in homes:
                o = o.replace(h, "<HOME>")
            return o
        if isinstance(o, list):
            return [red(x) for x in o]
        if isinstance(o, dict):
            return {red(k) if isinstance(k, str) else k: red(v) for k, v in o.items()}
        return o
    return red(obj)


def inside_git_repo(path) -> bool:
    p = Path(path).resolve()
    for d in [p, *p.parents]:
        if (d / ".git").exists():
            return True
    return False


def check_work_dir(work: Path) -> None:
    """Fail closed: work dir must not be in a git repo and must be empty."""
    if inside_git_repo(work):
        raise RuntimeError(f"refusing: {work} is inside a git repository")
    if work.exists() and any(work.iterdir()):
        raise RuntimeError(f"refusing: work dir {work} is not empty")


def full_prompt(task: str, arm: str = "baseline") -> str:
    return (FORCE_PREFIX if arm.endswith("_forced") else "") + TASKS[task] + SUFFIX


def build_command(claude: str, prompt: str, arm: str, model: str, max_turns: int, max_budget: float,
                  official_dir: str | None = None, vault_dir: str | None = None,
                  director_dir: str | None = None) -> list[str]:
    cmd = [claude, "-p", prompt, "--model", model, "--max-turns", str(max_turns),
           "--max-budget-usd", str(max_budget), "--setting-sources", "project", "--strict-mcp-config",
           "--allowedTools", "Write,Edit,Read,Skill", "--output-format", "stream-json", "--verbose"]
    if arm in ("official", "official_forced"):
        cmd += ["--plugin-dir", str(official_dir)]
    elif arm == "director_official":
        if not director_dir:
            raise ValueError("director_official arm requires --director-dir")
        cmd += ["--plugin-dir", str(official_dir), "--plugin-dir", str(director_dir)]
    elif arm in ("vault", "vault_forced"):
        if not vault_dir:
            raise ValueError("vault arm requires --vault-dir")
        cmd += ["--plugin-dir", str(vault_dir)]
    elif arm != "baseline":
        raise ValueError(f"unknown arm {arm}")
    return cmd


def budget_exhausted(cumulative: float, max_total: float) -> bool:
    return cumulative >= max_total


def parse_stream(text: str) -> dict:
    result = None
    init_skills: list[str] = []
    invoked: list[str] = []
    tools: dict[str, int] = {}
    for line in text.splitlines():
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if not isinstance(ev, dict):
            continue
        t = ev.get("type")
        if t == "system" and ev.get("subtype") == "init":
            init_skills = sorted(s for s in (ev.get("skills") or []) if isinstance(s, str) and "design" in s)
        elif t == "assistant":
            content = (ev.get("message") or {}).get("content")
            for b in content if isinstance(content, list) else []:
                if isinstance(b, dict) and b.get("type") == "tool_use":
                    name = b.get("name", "?")
                    tools[name] = tools.get(name, 0) + 1
                    if name == "Skill":
                        sk = (b.get("input") or {}).get("skill")
                        if sk is not None:
                            invoked.append(sk)
        elif t == "result":
            result = ev
    r = result or {}
    return {"total_cost_usd": r.get("total_cost_usd"), "num_turns": r.get("num_turns"),
            "duration_ms": r.get("duration_ms"), "is_error": r.get("is_error"), "subtype": r.get("subtype"),
            "init_skills_design": init_skills, "skill_invoked": invoked, "tool_calls": tools}


# --------------------------------------------------------------------- run
def cmd_run(args, runner=None) -> int:
    out = Path(args.out)
    tasks = [t.strip() for t in args.tasks.split(",") if t.strip()]
    arms = [a.strip() for a in args.arms.split(",") if a.strip()]
    for t in tasks:
        if t not in TASKS:
            print(f"unknown task {t}", file=sys.stderr)
            return 2
    for a in arms:
        if a not in ARMS:
            print(f"unknown arm {a}", file=sys.stderr)
            return 2
    official = os.path.expanduser(args.official_dir)
    vault = args.vault_dir
    if any(a.startswith("vault") for a in arms) and (not vault or not Path(vault).is_dir()):
        print("error: --vault-dir is required (and must exist) for the vault arm", file=sys.stderr)
        return 2
    claude = shutil.which("claude") or "claude"
    runner = runner or _run_claude
    records: list[dict] = []
    cumulative = 0.0
    for task in tasks:
        for arm in arms:
            cmd = build_command(claude, full_prompt(task, arm), arm, args.model, args.max_turns,
                                args.max_budget_usd, official, vault, getattr(args, "director_dir", None))
            run_dir = out / "runs" / f"{task}_{arm}"
            if args.dry_run:
                print(f"[{task}/{arm}] cwd={run_dir / 'work'}\n  " + subprocess.list2cmdline(cmd))
                continue
            if budget_exhausted(cumulative, args.max_total_usd):
                records.append({"task": task, "arm": arm, "status": "skipped_budget"})
                _write_json(out / "RUNS.json", records)
                continue
            work = run_dir / "work"
            check_work_dir(work)
            work.mkdir(parents=True, exist_ok=True)
            rec = runner(cmd, work)
            idx = work / "index.html"
            rec.update({"task": task, "arm": arm, "index_exists": idx.is_file(),
                        "index_bytes": idx.stat().st_size if idx.is_file() else 0})
            rec = redact(rec)
            cost = rec.get("total_cost_usd")
            if cost is None and rec.get("exit_code") == "timeout":
                cost = args.max_budget_usd  # conservative: unknown spend counts as the per-run cap
            cumulative += float(cost or 0.0)
            _write_json(run_dir / "run.json", rec)
            records.append(rec)
            _write_json(out / "RUNS.json", records)
    return 0


def _run_claude(cmd: list[str], work: Path) -> dict:
    t0 = time.time()
    try:
        p = subprocess.run(cmd, cwd=str(work), capture_output=True, timeout=TIMEOUT_S)
        stdout = p.stdout.decode("utf-8", "replace")
        code = p.returncode
    except subprocess.TimeoutExpired as e:
        stdout = (e.stdout or b"").decode("utf-8", "replace") if isinstance(e.stdout, bytes) else (e.stdout or "")
        code = "timeout"
    rec = parse_stream(stdout)
    rec["exit_code"] = code
    if rec.get("duration_ms") is None:
        rec["duration_ms"] = int((time.time() - t0) * 1000)
    return rec


def _write_json(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


# ------------------------------------------------------------ static tells
GENERIC_FONTS = {"inter", "roboto", "arial", "helvetica", "system-ui", "-apple-system", "segoe ui",
                 "sans-serif", "open sans", "montserrat", "poppins"}
_HEX = re.compile(r"#([0-9a-fA-F]{3,8})\b")
_RGB = re.compile(r"rgba?\(\s*([\d.]+)\s*[, ]\s*([\d.]+)\s*[, ]\s*([\d.]+)")


_OKLCH = re.compile(r"oklch\(\s*([\d.]+)(%?)\s+([\d.]+)(%?)\s+([\d.]+)(?:deg)?\s*(?:/[^)]*)?\)", re.I)


def oklch_to_rgb(L: float, C: float, H: float) -> tuple[int, int, int]:
    a = C * math.cos(math.radians(H))
    b = C * math.sin(math.radians(H))
    l_ = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m_ = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s_ = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
    lin = (4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
           -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
           -0.0041960863 * l_ - 0.7034186147 * m_ + 1.7076147010 * s_)

    def gamma(v):
        v = min(1.0, max(0.0, v))
        return 12.92 * v if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055
    return tuple(int(round(gamma(v) * 255)) for v in lin)


def parse_colors(s: str) -> list[tuple[int, int, int]]:
    out = []
    for m in _HEX.finditer(s):
        h = m.group(1)
        if len(h) in (3, 4):
            out.append(tuple(int(c * 2, 16) for c in h[:3]))
        elif len(h) in (6, 8):
            out.append((int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)))
    for m in _RGB.finditer(s):
        out.append(tuple(min(255, int(round(float(g)))) for g in m.groups()))
    for m in _OKLCH.finditer(s):
        L = float(m.group(1)) / (100.0 if m.group(2) else 1.0)
        C = float(m.group(3)) / (100.0 * 0.4 if m.group(4) else 1.0)
        out.append(oklch_to_rgb(L, C, float(m.group(5))))
    return out


def _near(c, target, tol) -> bool:
    return math.dist(c, target) <= tol


def hue_sat(rgb) -> tuple[float, float]:
    h, _l, s = colorsys.rgb_to_hls(*(v / 255 for v in rgb))
    return h * 360, s


def _visible_text(html: str) -> str:
    s = re.sub(r"(?is)<(script|style)\b.*?</\1\s*>", " ", html)
    s = re.sub(r"(?s)<!--.*?-->", " ", s)
    s = re.sub(r"(?s)<[^>]+>", " ", s)
    return htmllib.unescape(s)


def _first_family(decl: str) -> str:
    fam = decl.split(",")[0].strip().strip("'\"").strip().lower()
    return re.sub(r"\s*!important$", "", fam)


def _generic_font(html: str) -> bool:
    css = "\n".join(re.findall(r"(?is)<style[^>]*>(.*?)</style>", html)) or html
    fam = None
    for sel, body in re.findall(r"([^{}]+)\{([^{}]*)\}", css):
        if re.search(r"(^|[\s,])(body|html|:root)\s*($|,)", sel.strip()):
            m = re.search(r"font-family\s*:\s*([^;}]+)", body)
            if m:
                fam = _first_family(m.group(1))
                break
    if fam is None:
        m = re.search(r"font-family\s*:\s*([^;}]+)", css)
        if m:
            fam = _first_family(m.group(1))
    return fam in GENERIC_FONTS


def static_tells(html: str) -> dict:
    colors = parse_colors(html)
    cream = any(_near(c, (0xF4, 0xF1, 0xEA), 18) for c in colors)
    terra = any(_near(c, (0xD9, 0x77, 0x57), 24) for c in colors)
    near_black = bool(re.search(r"(?i)#(?:0b0b0b|111111|111|0a0a0a)(?![0-9a-f])", html))
    purple = False
    for g in re.finditer(r"(?is)(?:linear|radial|conic)-gradient\((?:[^()]|\([^()]*\))*\)", html):
        for c in parse_colors(g.group(0)):
            hue, sat = hue_sat(c)
            if 245 <= hue <= 295 and sat > 0.35:
                purple = True
    vis = _visible_text(html)
    middot = vis.count(" · ")
    arrows = 0
    for m in re.finditer(r"(?is)<(a|button)\b[^>]*>(.*?)</\1\s*>", html):
        arrows += _visible_text(m.group(2)).count("→")
    numbered = len(re.findall(r"\b0[1-9]\b", vis))
    shadow = len(re.findall(r"rgba\(\s*0\s*,\s*0\s*,\s*0\s*,\s*(?:0?\.1|0?\.08)\s*\)", html))
    radii = {re.sub(r"\s+", " ", v.strip().lower())
             for v in re.findall(r"border-radius\s*:\s*([^;}]+)", html, re.I)}
    gradients = len(re.findall(r"gradient\(", html))
    fade = bool("IntersectionObserver" in html and re.search(
        r"(?is)(?:transition|animation|@keyframes)[^}]*?(?:translateY|opacity)", html))
    generic = _generic_font(html)
    score = (generic + cream + terra + near_black + purple + min(middot, 3) / 3 + min(arrows, 3) / 3
             + (numbered >= 3) + (shadow >= 2) + fade)
    return {
        "generic_font": generic, "cream_bg": cream, "terracotta": terra, "near_black_tint": near_black,
        "purple_gradient": purple, "middot_meta": middot, "arrow_ctas": arrows,
        "numbered_markers": numbered, "soft_shadow": shadow, "radius_values": len(radii),
        "gradients": gradients, "fade_reveal": fade,
        "has_focus_visible": ":focus-visible" in html or ":focus" in html,
        "has_reduced_motion": "prefers-reduced-motion" in html,
        "tell_score": float(score),
    }


# ----------------------------------------------------------------- measure
METRICS_JS = r"""(() => {
  const vis = el => { const r = el.getClientRects(); if (!r.length) return false;
    const cs = getComputedStyle(el); return cs.visibility !== 'hidden' && cs.display !== 'none' && parseFloat(cs.opacity) > 0; };
  const own = el => Array.from(el.childNodes).some(n => n.nodeType === 3 && n.textContent.trim().length > 0);
  // CSS Color 4 parser (no canvas): returns {r,g,b,a} in 0..255 sRGB (clamped) or {unparsed:<fmt>}; never guesses.
  const clamp01 = v => Math.min(1, Math.max(0, v));
  const enc = v => { const s = v < 0 ? -1 : 1, x = Math.abs(v);
    return s * (x > 0.0031308 ? 1.055 * Math.pow(x, 1/2.4) - 0.055 : 12.92 * x); };
  const fromLin = (r, g, b, a) => ({r: clamp01(enc(r))*255, g: clamp01(enc(g))*255, b: clamp01(enc(b))*255, a: a});
  const oklabToRgb = (L, A, B, a) => { const l_ = L + 0.3963377774*A + 0.2158037573*B, m_ = L - 0.1055613458*A - 0.0638541728*B,
      s_ = L - 0.0894841775*A - 1.2914855480*B, l = l_**3, m = m_**3, s = s_**3;
    return fromLin(4.0767416621*l - 3.3077115913*m + 0.2309699292*s, -1.2684380046*l + 2.6097574011*m - 0.3413193965*s,
      -0.0041960863*l - 0.7034186147*m + 1.7076147010*s, a); };
  const xyz50ToRgb = (X, Y, Z, a) => { // Bradford D50 -> D65, then linear sRGB
    const x = 0.9554734527042182*X - 0.023098536874261423*Y + 0.0632593086610217*Z,
          y = -0.028369706963208136*X + 1.0099954580058226*Y + 0.021041398966943008*Z,
          z = 0.012314001688319899*X - 0.020507696433477912*Y + 1.3303659366080753*Z;
    return fromLin(3.2409699419045226*x - 1.537383177570094*y - 0.4986107602930034*z,
      -0.9692436362808796*x + 1.8759675015077202*y + 0.04155505740717559*z,
      0.05563007969699366*x - 0.20397695888897652*y + 1.0569715142428786*z, a); };
  const labToRgb = (L, A, B, a) => { const k = 24389/27, e = 216/24389, fy = (L + 16)/116, fx = A/500 + fy, fz = fy - B/200;
    const f3 = t => (t**3 > e ? t**3 : (116*t - 16)/k);
    const X = 0.3457/0.3585 * f3(fx), Y = (L > k*e ? fy**3 : L/k), Z = (1 - 0.3457 - 0.3585)/0.3585 * f3(fz);
    return xyz50ToRgb(X, Y, Z, a); };
  const num = (t, pct) => { t = t.trim(); if (t === 'none') return 0;
    if (t.endsWith('%')) return parseFloat(t) / 100 * pct; const v = parseFloat(t); return isNaN(v) ? NaN : v; };
  const hue = t => { t = t.trim(); if (t === 'none') return 0; let v = parseFloat(t);
    if (/turn$/.test(t)) v *= 360; else if (/grad$/.test(t)) v *= 0.9; else if (/rad$/.test(t)) v *= 180/Math.PI; return v; };
  const parse = s => { s = (s || '').trim().toLowerCase();
    if (s === 'transparent') return {r:0,g:0,b:0,a:0};
    const m = s.match(/^([a-z0-9-]+)\((.*)\)$/); if (!m) return {unparsed: s.slice(0, 24) || 'empty'};
    const fn = m[1]; let body = m[2], alpha = 1;
    const sl = body.split('/'); if (sl.length > 2) return {unparsed: fn};
    if (sl.length === 2) { alpha = num(sl[1], 1); body = sl[0]; }
    let parts = body.replace(/,/g, ' ').trim().split(/\s+/);
    if (fn === 'rgba') { if (parts.length === 4) { alpha = num(parts[3], 1); parts = parts.slice(0, 3); } }
    if (isNaN(alpha)) return {unparsed: fn};
    alpha = clamp01(alpha); let out = null;
    const need = n => parts.length === n && parts.every(x => x !== '');
    if ((fn === 'rgb' || fn === 'rgba') && need(3)) {
      const c = parts.map(t => num(t, 255)); if (c.some(isNaN)) return {unparsed: fn};
      out = {r: Math.min(255, Math.max(0, c[0])), g: Math.min(255, Math.max(0, c[1])), b: Math.min(255, Math.max(0, c[2])), a: alpha};
    } else if (fn === 'oklch' && need(3)) { const L = num(parts[0], 1), C = num(parts[1], 0.4), h = hue(parts[2]) * Math.PI/180;
      if ([L, C, h].some(isNaN)) return {unparsed: fn}; out = oklabToRgb(L, C*Math.cos(h), C*Math.sin(h), alpha);
    } else if (fn === 'oklab' && need(3)) { const L = num(parts[0], 1), A = num(parts[1], 0.4), B = num(parts[2], 0.4);
      if ([L, A, B].some(isNaN)) return {unparsed: fn}; out = oklabToRgb(L, A, B, alpha);
    } else if (fn === 'lab' && need(3)) { const L = num(parts[0], 100), A = num(parts[1], 125), B = num(parts[2], 125);
      if ([L, A, B].some(isNaN)) return {unparsed: fn}; out = labToRgb(L, A, B, alpha);
    } else if (fn === 'lch' && need(3)) { const L = num(parts[0], 100), C = num(parts[1], 150), h = hue(parts[2]) * Math.PI/180;
      if ([L, C, h].some(isNaN)) return {unparsed: fn}; out = labToRgb(L, C*Math.cos(h), C*Math.sin(h), alpha);
    } else if (fn === 'color' && parts.length === 4 && (parts[0] === 'srgb' || parts[0] === 'srgb-linear')) {
      const c = parts.slice(1).map(t => num(t, 1)); if (c.some(isNaN)) return {unparsed: 'color(' + parts[0] + ')'};
      out = parts[0] === 'srgb' ? {r: clamp01(c[0])*255, g: clamp01(c[1])*255, b: clamp01(c[2])*255, a: alpha} : fromLin(c[0], c[1], c[2], alpha);
    } else return {unparsed: fn === 'color' ? 'color(' + (parts[0] || '') + ')' : fn};
    return out; };
  const lum = c => { const f = v => { v/=255; return v<=0.03928 ? v/12.92 : Math.pow((v+0.055)/1.055, 2.4); };
    return 0.2126*f(c.r)+0.7152*f(c.g)+0.0722*f(c.b); };
  const over = (top, base) => { const a = top.a + base.a*(1-top.a); if (a <= 0) return {r:0,g:0,b:0,a:0};
    const mix = k => (top[k]*top.a + base[k]*base.a*(1-top.a)) / a; return {r: mix('r'), g: mix('g'), b: mix('b'), a: a}; };
  const first = f => (f || '').split(',')[0].trim().replace(/^["']|["']$/g, '');
  const h1 = document.querySelector('h1');
  let upper = 0; const ratios = [];
  const all = document.querySelectorAll('body *');
  all.forEach(el => { if (!own(el) || !vis(el)) return; const cs = getComputedStyle(el);
    const fs = parseFloat(cs.fontSize), ls = parseFloat(cs.letterSpacing);
    if (cs.textTransform === 'uppercase' && !isNaN(ls) && ls > 0.04*fs) upper++; });
  let skipped = 0, n = 0, zeroOp = 0, unparsed = 0, transparentFg = 0, candidates = 0; const badFmt = {};
  document.querySelectorAll('body *').forEach(el => { if (own(el) && getComputedStyle(el).display !== 'none' &&
    el.getClientRects().length && parseFloat(getComputedStyle(el).opacity) === 0) zeroOp++; });
  for (const el of document.querySelectorAll('p,li,td,span,a')) {
    if (n >= 300) break; if (!own(el) || !vis(el)) continue; n++; candidates++;
    // background: stack translucent layers until an opaque one; an image/gradient layer only matters if no opaque colour is found
    const layers = []; let base = null, sawImage = false, bad = null, a = el;
    while (a) { const cs = getComputedStyle(a);
      if (cs.backgroundImage && cs.backgroundImage !== 'none') sawImage = true;
      const c = parse(cs.backgroundColor);
      if (c.unparsed) { bad = c.unparsed; break; }
      if (c.a > 0.01) { if (c.a >= 0.99) { base = c; break; } layers.push(c); }
      a = a.parentElement; }
    if (bad) { unparsed++; badFmt[bad] = (badFmt[bad]||0) + 1; continue; }
    if (!base && sawImage) { skipped++; continue; }
    let bg = base || {r:255,g:255,b:255,a:1};
    for (let i = layers.length - 1; i >= 0; i--) bg = over(layers[i], bg);
    let fg = parse(getComputedStyle(el).color);
    if (fg.unparsed) { unparsed++; badFmt[fg.unparsed] = (badFmt[fg.unparsed]||0) + 1; continue; }
    if (fg.a < 0.01) { transparentFg++; continue; }
    fg = over(fg, bg);
    const l1 = lum(fg), l2 = lum(bg); ratios.push((Math.max(l1,l2)+0.05)/(Math.min(l1,l2)+0.05)); }
  const vw = (typeof window.__vw === 'number') ? window.__vw : document.documentElement.clientWidth;
  let maxRight = 0;
  document.querySelectorAll('body, body *').forEach(el => { const r = el.getBoundingClientRect();
    if (r.width > 0 || r.height > 0) maxRight = Math.max(maxRight, r.right + window.scrollX); });
  const sw = Math.max(document.documentElement.scrollWidth, document.body ? document.body.scrollWidth : 0);
  const overflow_px = Math.max(0, sw - vw, window.innerWidth - vw);
  ratios.sort((x,y)=>x-y);
  const med = ratios.length ? (ratios.length%2 ? ratios[(ratios.length-1)/2] : (ratios[ratios.length/2-1]+ratios[ratios.length/2])/2) : null;
  return { h1_font: h1 ? first(getComputedStyle(h1).fontFamily) : null,
    body_font: first(getComputedStyle(document.body).fontFamily),
    horizontal_overflow: (sw > vw + 1) || (window.innerWidth > vw + 1), overflow_px: Math.round(overflow_px), scroll_width: sw,
    inner_width: window.innerWidth, max_element_right_px: Math.round(maxRight), zero_opacity_text_elements: zeroOp,
    min_text_contrast_reason: ratios.length ? null : (candidates === 0 ? 'no_measurable_text' : (skipped === candidates ? 'all_background_images' : (unparsed ? 'unparsed_color:' + Object.keys(badFmt).join(',') : (transparentFg ? 'all_transparent' : 'no_measurable_text')))),
    unparsed_text_elements: unparsed, unparsed_color_formats: badFmt, bg_image_skipped: skipped,
    prefers_dark: matchMedia('(prefers-color-scheme: dark)').matches,
    upper_tracked_labels: upper, min_text_contrast: ratios.length ? ratios[0] : null,
    median_text_contrast: med, has_lang: !!(document.documentElement.getAttribute('lang') || '').trim(),
    page_height: Math.max(document.documentElement.scrollHeight, document.body ? document.body.scrollHeight : 0) };
})()"""

DEFAULT_CHROME = os.path.join(os.environ.get("LOCALAPPDATA", ""), "ms-playwright", "chromium-1248",
                              "chrome-win64", "chrome.exe")


class CDP:
    def __init__(self, ws_url: str):
        import websocket  # websocket-client
        self.ws = websocket.create_connection(ws_url, timeout=30, suppress_origin=True)
        self.n = 0
        self.errors: list[str] = []
        self.events: list[dict] = []

    def _handle(self, msg: dict) -> None:
        m = msg.get("method")
        p = msg.get("params", {})
        if m == "Runtime.exceptionThrown":
            d = p.get("exceptionDetails", {})
            self.errors.append(str(d.get("exception", {}).get("description") or d.get("text")))
        elif m == "Runtime.consoleAPICalled" and p.get("type") == "error":
            self.errors.append(" ".join(str(a.get("value", a.get("description", ""))) for a in p.get("args", [])))
        elif m == "Log.entryAdded" and p.get("entry", {}).get("level") == "error":
            self.errors.append(str(p["entry"].get("text")))

    def _recv(self, timeout: float):
        self.ws.settimeout(timeout)
        try:
            msg = json.loads(self.ws.recv())
        except Exception:
            return None
        finally:
            self.ws.settimeout(30)
        if "method" in msg:
            self.events.append(msg)  # buffer every event, never drop
            self._handle(msg)
        return msg

    def call(self, method: str, **params):
        self.n += 1
        self.ws.send(json.dumps({"id": self.n, "method": method, "params": params}))
        while True:
            msg = self._recv(30)
            if msg is None:
                raise RuntimeError(f"{method}: timeout")
            if msg.get("id") == self.n:
                if "error" in msg:
                    raise RuntimeError(f"{method}: {msg['error']}")
                return msg.get("result", {})

    def wait_event(self, name: str, timeout: float = 30) -> bool:
        end = time.time() + timeout
        while True:
            if any(e.get("method") == name for e in self.events):
                self.events = [e for e in self.events if e.get("method") != name]
                return True
            if time.time() >= end:
                return False
            self._recv(0.5)

    def pump(self, seconds: float) -> None:
        end = time.time() + seconds
        while time.time() < end:
            self._recv(0.2)


def clip_width(viewport_w: int, scroll_w) -> int:
    return max(int(viewport_w), int(scroll_w or 0))


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def measure_page(chrome: str, index: Path, run_dir: Path) -> dict:
    import base64
    import urllib.request
    port = _free_port()
    prof = tempfile.mkdtemp(prefix="fdb_chrome_")
    proc = subprocess.Popen([chrome, "--headless=new", f"--remote-debugging-port={port}",
                             f"--user-data-dir={prof}", "--no-first-run", "--hide-scrollbars", "about:blank"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    result: dict = {"viewports": {}}
    try:
        ws_url = None
        for _ in range(60):
            try:
                tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json", timeout=2))
                ws_url = next(t["webSocketDebuggerUrl"] for t in tabs if t.get("type") == "page")
                break
            except Exception:
                time.sleep(0.5)
        if not ws_url:
            raise RuntimeError("chrome devtools endpoint not reachable")
        cdp = CDP(ws_url)
        for dom in ("Page", "Runtime", "Log"):
            cdp.call(f"{dom}.enable")
        # Pin the scheme: headless Chromium otherwise follows the OS theme and results vary by machine.
        light = [{"name": "prefers-color-scheme", "value": "light"}]
        cdp.call("Emulation.setEmulatedMedia", features=light)
        for name, (w, h, mobile) in VIEWPORTS.items():
            cdp.errors = []
            cdp.events = []
            cdp.call("Emulation.setDeviceMetricsOverride", width=w, height=h, deviceScaleFactor=1, mobile=mobile)
            cdp.call("Page.navigate", url=index.resolve().as_uri())
            cdp.wait_event("Page.loadEventFired")
            cdp.pump(1.5)
            ph = cdp.call("Runtime.evaluate", expression="Math.max(document.documentElement.scrollHeight,"
                          "document.body?document.body.scrollHeight:0)", returnByValue=True)
            tot = int(ph.get("result", {}).get("value") or h)
            for i in range(1, 11):  # trigger scroll-reveal, then back to top
                cdp.call("Runtime.evaluate", expression=f"window.scrollTo(0,{tot * i // 10})")
                cdp.pump(0.15)
            cdp.call("Runtime.evaluate", expression="window.scrollTo(0,0)")
            cdp.pump(0.8)
            try:
                val = cdp.call("Runtime.evaluate", expression=f"window.__vw={w};" + METRICS_JS, returnByValue=True)
                exc = val.get("exceptionDetails")
                if exc:
                    m = {"eval_error": str((exc.get("exception") or {}).get("description") or exc.get("text"))}
                else:
                    m = val.get("result", {}).get("value") or {"eval_error": "empty result"}
            except Exception as e:
                m = {"eval_error": str(e)}
            if name == "desktop" and "eval_error" not in m:  # same page under dark scheme, reported separately
                try:
                    cdp.call("Emulation.setEmulatedMedia", features=[{"name": "prefers-color-scheme", "value": "dark"}])
                    cdp.pump(0.3)
                    dv = cdp.call("Runtime.evaluate", expression=f"window.__vw={w};" + METRICS_JS, returnByValue=True)
                    dm = dv.get("result", {}).get("value") or {}
                    m["dark_scheme"] = {k: dm.get(k) for k in ("prefers_dark", "min_text_contrast", "median_text_contrast",
                                                               "min_text_contrast_reason", "unparsed_text_elements")}
                except Exception as e:
                    m["dark_scheme"] = {"eval_error": str(e)}
                finally:
                    cdp.call("Emulation.setEmulatedMedia", features=light)
                    cdp.pump(0.3)
            if "eval_error" in m:
                m["min_text_contrast_reason"] = "eval_error: " + m["eval_error"]
            height = min(int(m.get("page_height") or h), MAX_SHOT_HEIGHT)
            clip_w = clip_width(w, m.get("scroll_width"))
            shot = cdp.call("Page.captureScreenshot", format="png", captureBeyondViewport=True,
                            clip={"x": 0, "y": 0, "width": clip_w, "height": height, "scale": 1})
            (run_dir / f"shot_{name}.png").write_bytes(base64.b64decode(shot["data"]))
            m["console_errors"] = list(cdp.errors)
            result["viewports"][name] = m
        cdp.ws.close()
    finally:
        proc.kill()
        try:
            proc.wait(timeout=10)
        except Exception as e:
            print(f"warning: chromium did not exit: {e}", file=sys.stderr)
        for _ in range(5):
            try:
                shutil.rmtree(prof)
                break
            except FileNotFoundError:
                break
            except OSError:
                time.sleep(0.3)
        else:
            print(f"warning: could not remove chromium profile {prof}", file=sys.stderr)
    return result


def cmd_measure(args) -> int:
    out = Path(args.out)
    chrome = args.chrome or DEFAULT_CHROME
    all_metrics: list[dict] = []
    for run_json in sorted((out / "runs").glob("*/run.json")):
        run_dir = run_json.parent
        index = run_dir / "work" / "index.html"
        if not index.is_file():
            continue
        rec = json.loads(run_json.read_text(encoding="utf-8"))
        met = {"task": rec.get("task"), "arm": rec.get("arm"),
               "static": static_tells(index.read_text(encoding="utf-8", errors="replace"))}
        try:
            met.update(measure_page(chrome, index, run_dir))
        except Exception as e:  # recorded, not hidden
            met["measure_error"] = redact(str(e))
        _write_json(run_dir / "metrics.json", met)
        all_metrics.append(met)
    _write_json(out / "METRICS.json", all_metrics)
    runs = {}
    for r in out.joinpath("RUNS.json").exists() and json.loads((out / "RUNS.json").read_text("utf-8")) or []:
        runs[(r.get("task"), r.get("arm"))] = r
    (out / "SUMMARY.md").write_text(summary_md(all_metrics, runs), encoding="utf-8")
    return 0


def summary_md(metrics: list[dict], runs: dict) -> str:
    hdr = ["task", "arm", "cost", "turns", "skill_invoked", "tell_score", "generic_font", "upper_tracked(d)",
           "min_contrast", "median_contrast", "dark_min", "dark_median", "contrast_reason", "overflow(m)", "console_err", "focus", "reduced_motion"]
    rows = ["| " + " | ".join(hdr) + " |", "|" + "---|" * len(hdr)]

    def f(v):
        return "" if v is None else (f"{v:.2f}" if isinstance(v, float) else str(v))
    for m in metrics:
        r = runs.get((m["task"], m["arm"]), {})
        s = m.get("static", {})
        d = m.get("viewports", {}).get("desktop", {})
        mo = m.get("viewports", {}).get("mobile", {})
        errs = sum(len(v.get("console_errors", [])) for v in m.get("viewports", {}).values())
        rows.append("| " + " | ".join(f(x) for x in [
            m["task"], m["arm"], r.get("total_cost_usd"), r.get("num_turns"),
            ",".join(r.get("skill_invoked") or []) or "-", s.get("tell_score"), s.get("generic_font"),
            d.get("upper_tracked_labels"), d.get("min_text_contrast"), d.get("median_text_contrast"),
            (d.get("dark_scheme") or {}).get("min_text_contrast"), (d.get("dark_scheme") or {}).get("median_text_contrast"),
            d.get("min_text_contrast_reason"),
            mo.get("horizontal_overflow"), errs, s.get("has_focus_visible"), s.get("has_reduced_motion")]) + " |")
    return "# Frontend design benchmark summary\n\n" + "\n".join(rows) + "\n"


# --------------------------------------------------------------- judge-pack
def cmd_judge_pack(args) -> int:
    out = Path(args.out)
    key: dict = {}
    judge = out / "judge"
    judge.mkdir(parents=True, exist_ok=True)
    for i, task in enumerate(TASKS):
        arms = [a for a in ARMS if (out / "runs" / f"{task}_{a}").is_dir()]
        if not arms:
            continue
        off = random.Random(args.seed).randrange(len(arms))
        k = (i + off) % len(arms)  # Latin-square rotation: each arm hits each letter once per cycle
        arms = arms[k:] + arms[:k]
        key[task] = {}
        (judge / task).mkdir(exist_ok=True)
        for letter, arm in zip("ABCDEFGH", arms):  # len(ARMS) <= 8; never silently drop an arm
            key[task][letter] = arm
            for vp in VIEWPORTS:
                src = out / "runs" / f"{task}_{arm}" / f"shot_{vp}.png"
                if src.is_file():
                    shutil.copyfile(src, judge / task / f"{letter}_{vp}.png")
    _write_json(out / "judge_key.json", key)
    briefs = ["# Task briefs\n"] + [f"## {t}\n\n{p}\n" for t, p in TASKS.items()]
    (judge / "BRIEFS.md").write_text("\n".join(briefs), encoding="utf-8")
    return 0


# --------------------------------------------------------------------- cli
def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run")
    r.add_argument("--out", required=True)
    r.add_argument("--tasks", default="T1,T2,T3")
    r.add_argument("--arms", default="baseline,official,vault")
    r.add_argument("--model", default="sonnet")
    r.add_argument("--max-turns", type=int, default=25)
    r.add_argument("--max-budget-usd", type=float, default=0.9)
    r.add_argument("--max-total-usd", type=float, default=5.5)
    r.add_argument("--official-dir", default=DEFAULT_OFFICIAL)
    r.add_argument("--vault-dir")
    r.add_argument("--director-dir")
    r.add_argument("--dry-run", action="store_true")
    m = sub.add_parser("measure")
    m.add_argument("--out", required=True)
    m.add_argument("--chrome")
    j = sub.add_parser("judge-pack")
    j.add_argument("--out", required=True)
    j.add_argument("--seed", type=int, default=20261021)
    return p


def main(argv=None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, 'reconfigure'):
            stream.reconfigure(encoding='utf-8', errors='replace')
    args = build_parser().parse_args(argv)
    try:
        return {"run": cmd_run, "measure": cmd_measure, "judge-pack": cmd_judge_pack}[args.cmd](args)
    except RuntimeError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
