"""PM3a-c: single wsl.exe call site, fixed argv allow-list, hash pin, shell-inert tokens (M3)."""
from __future__ import annotations

import ast
import hashlib
import re
from pathlib import Path

import pytest
from tests.winpaths import system_root

from apps.device_host import launcher, protocol
from tests.local_gate.conftest import PRODUCT, live

APPS = PRODUCT / "apps"
BIN = PRODUCT / "wsl" / "opt-aas-bin"


def _wsl_call_sites(src: str) -> list[int]:
    """Lines where a subprocess/os spawn is made, and string literals mentioning wsl.exe."""
    tree = ast.parse(src)
    hits: list[int] = []
    spawn = {"Popen", "run", "call", "check_call", "check_output", "system", "popen", "startfile",
             "spawnv", "spawnve", "execv", "execve", "create_subprocess_exec", "create_subprocess_shell"}
    for node in ast.walk(tree):
        if isinstance(node, ast.Call):
            f = node.func
            name = f.attr if isinstance(f, ast.Attribute) else (f.id if isinstance(f, ast.Name) else "")
            if name in spawn:
                hits.append(node.lineno)
    return hits


def _mentions_wsl(src: str) -> bool:
    tree = ast.parse(src)
    return any(isinstance(n, ast.Constant) and isinstance(n.value, str) and "wsl.exe" in n.value.lower()
               for n in ast.walk(tree))


@pytest.mark.probe("PM3a")
def test_single_wsl_call_site_in_apps() -> None:
    files = [p for p in APPS.rglob("*.py")]
    wsl_files = [p for p in files if _mentions_wsl(p.read_text(encoding="utf-8"))]
    assert [p.relative_to(APPS).as_posix() for p in wsl_files] == ["device_host/launcher.py"]
    spawns = _wsl_call_sites((APPS / "device_host" / "launcher.py").read_text(encoding="utf-8"))
    assert len(spawns) == 1, spawns
    # every other module under apps/device_host spawns nothing
    for p in (APPS / "device_host").glob("*.py"):
        if p.name != "launcher.py":
            assert _wsl_call_sites(p.read_text(encoding="utf-8")) == [], p.name


@pytest.mark.probe("PM3a")
def test_scanner_positive_control_flags_second_site() -> None:
    mutated = (APPS / "device_host" / "launcher.py").read_text(encoding="utf-8") + \
        "\nimport subprocess\nsubprocess.run(['wsl.exe', '-u', 'root'])\n"
    assert len(_wsl_call_sites(mutated)) == 2 and _mentions_wsl(mutated)


EXPECTED = {
    "preflight": ["-d", "Ubuntu-S9AI-D", "--", "/opt/aas/bin/aas-preflight"],
    "serve-phone": ["-d", "Ubuntu-S9AI-D", "--", "/opt/aas/bin/aas-serve", "phone"],
    "serve-tablet": ["-d", "Ubuntu-S9AI-D", "--", "/opt/aas/bin/aas-serve", "tablet"],
}


@pytest.mark.probe("PM3a")
@pytest.mark.parametrize("verb", sorted(EXPECTED))
def test_argv_snapshot(verb: str) -> None:
    argv = launcher.argv_for(verb)
    assert Path(argv[0]).name.lower() == "wsl.exe" and Path(argv[0]).is_absolute()
    assert argv[1:] == EXPECTED[verb]
    launcher.validate_argv(argv)


BAD = [
    ["-u", "root"],
    ["--user", "root"],
    ["--exec"],
    ["--cd", "/"],
    ["extra"],
]


class _Spy:
    def __init__(self) -> None:
        self.calls = 0

    def __call__(self, *a, **k):  # type: ignore[no-untyped-def]
        self.calls += 1
        raise AssertionError("Popen must not be reached")


@pytest.mark.probe("PM3a")
@pytest.mark.parametrize("extra", BAD, ids=lambda e: "_".join(e))
def test_wrapper_refuses_extra_tokens_no_spawn(extra: list[str], monkeypatch: pytest.MonkeyPatch) -> None:
    spy = _Spy()
    monkeypatch.setattr(launcher, "_popen", spy)
    good = launcher.argv_for("serve-phone")
    for argv in (good + extra, good[:1] + extra + good[1:], good[:3] + extra + good[3:]):
        with pytest.raises(launcher.LauncherRefused):
            launcher.validate_argv(argv)
        with pytest.raises(launcher.LauncherRefused):
            launcher.spawn_argv(argv)
    assert spy.calls == 0


@pytest.mark.probe("PM3a")
@pytest.mark.parametrize("verb", ["serve-root", "serve", "shell", "serve-phone ", "", "preflight;id", "serve-s9emu"])
def test_unknown_verb_refused(verb: str, monkeypatch: pytest.MonkeyPatch) -> None:
    spy = _Spy()
    monkeypatch.setattr(launcher, "_popen", spy)
    with pytest.raises(launcher.LauncherRefused):
        launcher.argv_for(verb)
    with pytest.raises(launcher.LauncherRefused):
        launcher.spawn(verb)
    assert spy.calls == 0


@pytest.mark.probe("PM3a")
@pytest.mark.parametrize("distro", ["Ubuntu", "Ubuntu-S9AI-D2", "docker-desktop", "ubuntu-s9ai-d"])
def test_other_distro_refused(distro: str) -> None:
    argv = launcher.argv_for("serve-phone")
    argv[2] = distro
    with pytest.raises(launcher.LauncherRefused):
        launcher.validate_argv(argv)
    argv = launcher.argv_for("serve-phone")
    argv[0] = r"C:\tmp\wsl.exe"
    with pytest.raises(launcher.LauncherRefused):
        launcher.validate_argv(argv)


# ---------- PM3b hash pin ----------

def _manifest() -> dict[str, str]:
    out = {}
    for line in (BIN / "MANIFEST.sha256").read_text(encoding="utf-8").splitlines():
        h, name = line.split()
        out[name] = h
    return out


@pytest.mark.probe("PM3b")
def test_manifest_matches_sources() -> None:
    man = _manifest()
    files = sorted(p.name for p in BIN.iterdir() if p.is_file() and p.name not in ("MANIFEST.sha256", ".gitkeep"))
    assert sorted(man) == files
    for name, h in man.items():
        assert hashlib.sha256((BIN / name).read_bytes()).hexdigest() == h, name
    assert launcher.pinned_hashes() == man


@pytest.mark.probe("PM3b")
def test_tampered_script_refused_no_spawn(monkeypatch: pytest.MonkeyPatch) -> None:
    real = {name: (BIN / name).read_bytes() for name in _manifest()}
    spy = _Spy()
    monkeypatch.setattr(launcher, "_popen", spy)
    tampered = dict(real)
    tampered["aas_devhost.py"] = real["aas_devhost.py"].replace(b"restrict=on", b"restrict=of", 1)
    monkeypatch.setattr(launcher, "_read_installed", lambda name: tampered[name])
    with pytest.raises(launcher.Tampered):
        launcher.spawn("serve-phone")
    assert spy.calls == 0


@pytest.mark.probe("PM3b")
def test_matching_hash_spawns(monkeypatch: pytest.MonkeyPatch) -> None:
    real = {name: (BIN / name).read_bytes() for name in _manifest()}
    seen: dict = {}

    def fake_popen(argv, **kw):  # type: ignore[no-untyped-def]
        seen["argv"], seen["kw"] = argv, kw
        return "proc"

    monkeypatch.setattr(launcher, "_read_installed", lambda name: real[name])
    monkeypatch.setattr(launcher, "_popen", fake_popen)
    assert launcher.spawn("serve-phone") == "proc"
    assert seen["argv"] == launcher.argv_for("serve-phone")
    env = seen["kw"]["env"]
    assert not {k.upper() for k in env} & {"WSLENV", "BASH_ENV", "ENV", "USERPROFILE", "HOME"}
    assert seen["kw"]["cwd"].lower().endswith("system32")


# ---------- PM3c shell-inert tokens ----------

@pytest.mark.probe("PM3c")
def test_tokens_match_safe_charset() -> None:
    for verb in EXPECTED:
        for tok in launcher.argv_for(verb)[1:]:
            assert re.fullmatch(r"[A-Za-z0-9/._-]+", tok), tok
    with pytest.raises(launcher.LauncherRefused):
        launcher.validate_argv(launcher.argv_for("serve-phone")[:-1] + ["phone;id"])


@live
@pytest.mark.probe("PM3c")
def test_distro_shell_echoes_tokens_unchanged(sh) -> None:  # type: ignore[no-untyped-def]
    tail = launcher.argv_for("serve-tablet")[4:]
    import subprocess
    r = subprocess.run([launcher.WSL_EXE, "-d", launcher.DISTRO, "--", "/bin/echo", *tail],
                       capture_output=True, text=True, timeout=60)
    assert r.stdout.strip() == " ".join(tail)
    # positive control: a token with ';' is re-parsed by the distro shell
    r2 = subprocess.run([launcher.WSL_EXE, "-d", launcher.DISTRO, "--", "/bin/echo", "phone;echo", "INJECTED"],
                        capture_output=True, text=True, timeout=60)
    assert r2.stdout.split() != ["phone;echo", "INJECTED"]


@live
@pytest.mark.probe("PM3a")
@pytest.mark.parametrize("args,code", [("phone extra", 64), ("-u", 64), ("--exec", 64), ("root", 64),
                                       ("s9emu", 64), ("", 64)])
def test_distro_wrapper_refuses_bad_args(sh, args: str, code: int) -> None:  # type: ignore[no-untyped-def]
    r = sh(f"/opt/aas/bin/aas-serve {args}; echo rc=$?\npgrep -f 'qemu-system.*aas-' || echo no-emulator\n")
    assert f"rc={code}" in r.stdout and "no-emulator" in r.stdout, r.stdout + r.stderr


@live
@pytest.mark.probe("PM3a")
def test_distro_wrapper_refuses_root(sh) -> None:  # type: ignore[no-untyped-def]
    r = sh("/opt/aas/bin/aas-serve phone; echo rc=$?\n", user="root")
    assert "rc=65" in r.stdout, r.stdout + r.stderr


# ---------- protocol (schema / size caps) ----------

@pytest.mark.probe("PM3c")
def test_protocol_rejects_bad_requests() -> None:
    ok = protocol.encode_request(1, "tap", x=10, y=20)
    assert ok.endswith(b"\n")
    for op, args in [("tap", {"x": -1, "y": 0}), ("tap", {"x": 1}), ("tap", {"x": 1, "y": 2, "z": 3}),
                     ("shell", {"cmd": "id"}), ("text", {"t": "a\nb"}), ("text", {"t": "x" * 201}),
                     ("launch", {"pkg": "com.android.settings"}), ("tap", {"x": True, "y": 1}),
                     ("video_start", {"size": "4000x4000"})]:
        with pytest.raises(protocol.ProtocolError):
            protocol.encode_request(2, op, **args)


@pytest.mark.probe("PM3c")
def test_protocol_rejects_bad_device_lines() -> None:
    assert protocol.parse_device_line(b'{"ch":"c","id":1,"ok":true}\n')["ok"] is True
    for line in [b"x" * (protocol.MAX_LINE + 1), b"not json\n", b'{"ch":"z"}\n', b'{"ch":"c","id":1}\n',
                 b'{"ch":"v","seq":1,"b64":"' + b"A" * (protocol.MAX_B64 + 4) + b'"}\n',
                 b'{"ch":"e","kind":"booted","evil":"x"}\n', b'{"ch":"v","seq":1,"b64":"!!!!"}\n']:
        with pytest.raises(protocol.ProtocolError):
            protocol.parse_device_line(line)


# ---------- PM3a: wsl.exe call sites outside Python, and install.ps1 stays owner-only ----------

WSL_RE = re.compile(r"wsl(\.exe)?\b", re.IGNORECASE)
CODE_ROOTS = ("apps", "core", "kit", "scripts", "images")  # backend, desktop, agent tools, build images
TEXT_SUFFIXES = {".py", ".ps1", ".psm1", ".cmd", ".bat", ".sh", ".ts", ".tsx", ".js", ".mjs", ".cjs", ".rs",
                 ".json", ".toml", ".yaml", ".yml", ""}
SKIP_DIRS = {"node_modules", "__pycache__", ".venv", "target", "dist"}
# Third-party dependency lockfiles are data, not studio code (kit/pnpm-lock.yaml pins the npm package `is-wsl`).
SKIP_FILES = {"pnpm-lock.yaml", "uv.lock", "Cargo.lock", "package-lock.json"}


def _code_files() -> list[Path]:
    out = []
    for root in CODE_ROOTS:
        for p in (PRODUCT / root).rglob("*"):
            if (p.is_file() and p.suffix.lower() in TEXT_SUFFIXES and not SKIP_DIRS & set(p.parts)
                    and p.name not in SKIP_FILES):
                out.append(p)
    return out


def _ps1_wsl_sites(text: str) -> list[int]:
    return [i for i, line in enumerate(text.splitlines(), 1)
            if not line.lstrip().startswith("#") and re.search(r"wsl\.exe", line, re.IGNORECASE)]


@pytest.mark.probe("PM3a")
def test_ps1_wsl_call_sites_only_owner_setup() -> None:
    ps1 = sorted((PRODUCT / "wsl").glob("*.ps1"))
    with_wsl = [p.name for p in ps1 if _ps1_wsl_sites(p.read_text(encoding="utf-8"))]
    assert with_wsl == ["install.ps1"]
    # install.ps1 runs wsl.exe through exactly one helper (Invoke-Wsl) with a token allow-list
    text = (PRODUCT / "wsl" / "install.ps1").read_text(encoding="utf-8")
    assert len(_ps1_wsl_sites(text)) == 1 and text.count("& $Wsl ") == 1
    assert "^[A-Za-z0-9/._-]+$" in text


@pytest.mark.probe("PM3a")
def test_backend_and_tools_never_reference_wsl_or_install_ps1() -> None:
    hits = []
    for p in _code_files():
        rel = p.relative_to(PRODUCT).as_posix()
        text = p.read_text(encoding="utf-8", errors="replace")
        if "install.ps1" in text.lower() or "setup-avd" in text.lower():
            hits.append((rel, "install.ps1"))
        if rel != "apps/device_host/launcher.py" and WSL_RE.search(text):
            hits.append((rel, "wsl"))
    assert hits == []


@pytest.mark.probe("PM3a")
def test_backend_scan_positive_control(tmp_path: Path) -> None:
    fake = tmp_path / "apps" / "backend"
    fake.mkdir(parents=True)
    (fake / "tool.ts").write_text("spawn('powershell', ['-File', 'wsl/install.ps1'])\n", encoding="utf-8")
    (fake / "run.py").write_text("subprocess.run(['wsl', '-u', 'root'])\n", encoding="utf-8")
    globals()["PRODUCT"], saved = tmp_path, PRODUCT
    try:
        with pytest.raises(AssertionError):
            test_backend_and_tools_never_reference_wsl_or_install_ps1()
        assert _ps1_wsl_sites(f"& {system_root()}" + r"\System32\wsl.exe -u root") == [1]
    finally:
        globals()["PRODUCT"] = saved
