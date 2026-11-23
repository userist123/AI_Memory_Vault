"""Real Docker end-to-end probes for the web-build image (skipped when Docker or the image is missing).

Build the image first:
  docker build -f images/web-build/Dockerfile --build-context scripts=images/web-build -t aas-web-build:dev kit
Covers P1b (canary writes), P2c/P7a (no network in build), P8b (link created inside the container), kit blank build.
"""
from __future__ import annotations

import re
import shutil
import subprocess
import uuid
from pathlib import Path

import pytest
from aas_build import runner
from aas_build.runner import BuildError

PRODUCT = Path(__file__).resolve().parents[2]
KIT = PRODUCT / "kit"
DOCKER = shutil.which("docker") or ""
IMAGE_TAG = "aas-web-build:dev"


def _image_id() -> str | None:
    if not DOCKER:
        return None
    r = subprocess.run([DOCKER, "image", "inspect", IMAGE_TAG, "--format", "{{.Id}}"], capture_output=True, text=True, timeout=60)  # noqa: S603
    return r.stdout.strip() if r.returncode == 0 and r.stdout.strip().startswith("sha256:") else None


IMAGE_ID = _image_id()
pytestmark = [pytest.mark.skipif(IMAGE_ID is None, reason="docker or aas-web-build:dev image not available")]


def make_ctx(root: Path, vite_config: str | None = None) -> Path:
    ctx = root / "ctx"
    ctx.mkdir()
    for name in ("package.json", "pnpm-lock.yaml", "pnpm-workspace.yaml", ".npmrc", "index.html", "tsconfig.json", "capacitor.config.ts",
                 "aas-env.d.ts"):  # same studio files as core/aas_build/service.py (S9b tsc step needs the ambient decls)
        shutil.copy2(KIT / name, ctx / name)
    shutil.copy2(KIT / "vite.config.ts", ctx / "vite.config.ts")
    shutil.copytree(KIT / "template" / "src", ctx / "src")
    if vite_config is not None:
        (ctx / "vite.config.ts").write_text(vite_config, encoding="utf-8")
    return ctx


def build(root: Path, ctx: Path):  # type: ignore[no-untyped-def]
    out = root / "out"
    out.mkdir()
    bid = "t" + uuid.uuid4().hex[:15]
    return runner.run_web_build(docker=DOCKER, build_id=bid, ctx=ctx, out=out, image=IMAGE_ID or "", dest=root / "site" / "r1")


def docker_run(*args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run([DOCKER, "run", "--rm", "--cap-drop", "ALL", *args], capture_output=True, text=True, timeout=180)  # noqa: S603


def test_blank_kit_app_builds_with_network_none(tmp_path: Path) -> None:
    res = build(tmp_path, make_ctx(tmp_path))
    assert "index.html" in res.files and any(f.startswith("assets/") and f.endswith(".js") for f in res.files)
    assert "<div id=\"root\">" in (res.dest / "index.html").read_text(encoding="utf-8")
    assert len(res.dist_sha256) == 64


NET_CONFIG = """import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
// test-only: a build-time network attempt must fail the build
const r = await fetch('https://registry.npmjs.org/react', { signal: AbortSignal.timeout(8000) });
console.log('NETWORK-REACHED', r.status);
export default defineConfig({ plugins: [react()], base: './' });
"""


def test_build_time_network_attempt_fails(tmp_path: Path) -> None:  # P7a / P2c
    with pytest.raises(BuildError, match="exit code") as ei:
        build(tmp_path, make_ctx(tmp_path, NET_CONFIG))
    msg = str(ei.value)
    assert "NETWORK-REACHED" not in msg
    assert re.search(r"fetch failed|EAI_AGAIN|ENOTFOUND|ENETUNREACH|getaddrinfo", msg), msg  # the DNS/fetch failure, not another error
    assert not (tmp_path / "site").exists()
    assert not (tmp_path / "out").exists()


def test_network_probes_fail_in_build_network_and_succeed_on_bridge(tmp_path: Path) -> None:
    probe = "getent hosts registry.npmjs.org; echo dns=$?; wget -q -T 5 -O /dev/null https://registry.npmjs.org/ ; echo https=$?"
    none = docker_run("--network", "none", "--entrypoint", "sh", IMAGE_ID or "", "-c", probe)
    assert "dns=0" not in none.stdout and "https=0" not in none.stdout, none.stdout
    bridge = docker_run("--network", "bridge", "--entrypoint", "sh", IMAGE_ID or "", "-c", probe)  # [+] positive control
    if "dns=0" not in bridge.stdout:
        pytest.skip("host has no internet, positive control impossible")
    assert "https=0" in bridge.stdout


CANARY_CONFIG = """import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import fs from 'node:fs';
// test-only (P1b): try to write canaries outside /out/dist; every attempt must fail or stay in the container
const tried = [];
for (const p of ['/src/canary.txt', '/out/../canary.txt', '/etc/canary.txt', '/opt/aas/canary.txt']) {
  try { fs.writeFileSync(p, 'x'); tried.push('WROTE ' + p); } catch (e) { tried.push('denied ' + p); }
}
export default defineConfig({
  plugins: [react(), { name: 'report', closeBundle() { fs.writeFileSync('/out/dist/report.txt', tried.join('\\n')); } }],
  base: './',
});
"""


def test_p1b_canary_writes_outside_out_are_denied(tmp_path: Path) -> None:
    ctx = make_ctx(tmp_path, CANARY_CONFIG)
    before = sorted(p.name for p in ctx.iterdir())
    res = build(tmp_path, ctx)
    report = (res.dest / "report.txt").read_text(encoding="utf-8") if "report.txt" in res.files else ""
    assert report and "WROTE" not in report, report
    assert sorted(p.name for p in ctx.iterdir()) == before  # ctx unchanged
    assert not (tmp_path / "canary.txt").exists()


LINK_CONFIG = """import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import fs from 'node:fs';
// test-only (P8b): create a symlink in /out/dist pointing outside it
export default defineConfig({
  plugins: [react(), { name: 'links', closeBundle() {
    fs.symlinkSync('/etc/passwd', '/out/dist/leak.txt');
  } }],
  base: './',
});
"""

HARDLINK_CONFIG = """import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import fs from 'node:fs';
// test-only (P8b): hardlink a file outside /out (fails cross-device) and a second name inside /out/dist
export default defineConfig({
  plugins: [react(), { name: 'links', closeBundle() {
    try { fs.linkSync('/tmp/w/index.html', '/out/dist/outside.txt'); console.log('OUTSIDE-HARDLINK-MADE'); }
    catch (e) { console.log('outside hardlink denied ' + e.code); }
    fs.writeFileSync('/out/dist/a.txt', 'a');
    fs.linkSync('/out/dist/a.txt', '/out/dist/b.txt');
  } }],
  base: './',
});
"""


def _refusal(msg: str, reader_text: str) -> None:
    # the bind mount refused to create the link (container exit != 0 with the errno) OR the host reader refused it
    assert reader_text in msg or re.search(r"exit code \d+: .*(EPERM|ENOTSUP|EOPNOTSUPP|EXDEV|EACCES|EIO)", msg, re.S), msg


def test_p8b_symlink_created_in_container_is_refused(tmp_path: Path) -> None:
    with pytest.raises(BuildError) as ei:
        build(tmp_path, make_ctx(tmp_path, LINK_CONFIG))
    _refusal(str(ei.value), "link or reparse point in output refused")
    assert not (tmp_path / "site" / "r1").exists() and not (tmp_path / "out").exists()
    print("P8b symlink outcome:", str(ei.value)[:160].replace("\n", " "))


def test_p8b_hardlink_created_in_container_is_refused(tmp_path: Path) -> None:
    with pytest.raises(BuildError) as ei:
        build(tmp_path, make_ctx(tmp_path, HARDLINK_CONFIG))
    _refusal(str(ei.value), "hardlinked file in output refused")
    assert "OUTSIDE-HARDLINK-MADE" not in str(ei.value)
    assert not (tmp_path / "site" / "r1").exists() and not (tmp_path / "out").exists()
    print("P8b hardlink outcome:", str(ei.value)[:160].replace("\n", " "))


def test_lockfile_mismatch_refused(tmp_path: Path) -> None:
    ctx = make_ctx(tmp_path)
    (ctx / "pnpm-lock.yaml").write_text((ctx / "pnpm-lock.yaml").read_text(encoding="utf-8") + "\n# edited\n", encoding="utf-8")
    with pytest.raises(BuildError, match="exit code 3") as ei:
        build(tmp_path, ctx)
    assert "unapproved lockfile" in str(ei.value)
    assert not (tmp_path / "site").exists() and not (tmp_path / "out").exists()


WRITER_CONFIG = """import { defineConfig } from 'vite';
import fs from 'node:fs';
// test-only: write 300 MB into /out/dist (60 MB files, under the 64 MB fsize ulimit), then linger so only the watchdog ends it
fs.mkdirSync('/out/dist', { recursive: true });
const mb = Buffer.alloc(1024 * 1024, 120);
for (let f = 0; f < 5; f++) {
  const fd = fs.openSync(`/out/dist/big${f}.txt`, 'w');
  for (let i = 0; i < 60; i++) fs.writeSync(fd, mb);
  fs.closeSync(fd);
}
console.log('WROTE-300MB');
await new Promise((r) => setTimeout(r, 120000));
export default defineConfig({ base: './' });
"""


def test_out_writer_over_cap_is_killed_and_out_removed(tmp_path: Path) -> None:
    with pytest.raises(BuildError, match="during the build; container killed"):
        build(tmp_path, make_ctx(tmp_path, WRITER_CONFIG))
    assert not (tmp_path / "out").exists()
    assert not (tmp_path / "site").exists()


def test_tmp_is_noexec_with_production_flags(tmp_path: Path) -> None:
    ctx, out = tmp_path / "ctx", tmp_path / "out"
    ctx.mkdir()
    out.mkdir()
    argv = runner.build_web_argv(docker=DOCKER, build_id="t" + uuid.uuid4().hex[:15], ctx=ctx, out=out, image=IMAGE_ID or "")
    assert argv[-1] == runner.ENTRYPOINT
    script = "cp /bin/busybox /tmp/bb && chmod 0755 /tmp/bb && /tmp/bb true; echo tmpexec=$?; /bin/busybox true; echo binexec=$?"
    i = argv.index(IMAGE_ID or "")
    probe = [*argv[:i], "--entrypoint", "sh", argv[i], "-c", script]  # same flags as a real build, only the command differs
    r = subprocess.run(probe, capture_output=True, text=True, timeout=180)  # noqa: S603
    assert "binexec=0" in r.stdout, (r.stdout, r.stderr)  # [+] positive control: exec from the image works
    assert "tmpexec=0" not in r.stdout and "tmpexec=126" in r.stdout, (r.stdout, r.stderr)
    assert "Permission denied" in r.stderr, r.stderr
