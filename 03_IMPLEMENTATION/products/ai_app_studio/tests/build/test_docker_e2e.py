"""Real Docker end-to-end probes for the web-build image (skipped when Docker or the image is missing).

Build the image first:
  docker build -f images/web-build/Dockerfile --build-context scripts=images/web-build -t aas-web-build:dev kit
Covers P1b (canary writes), P2c/P7a (no network in build), P8b (link created inside the container), kit blank build.
"""
from __future__ import annotations

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
    for name in ("package.json", "pnpm-lock.yaml", "pnpm-workspace.yaml", ".npmrc", "index.html", "tsconfig.json", "capacitor.config.ts"):
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
    with pytest.raises(BuildError, match="exit code"):
        build(tmp_path, make_ctx(tmp_path, NET_CONFIG))
    assert not (tmp_path / "site").exists()


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
// test-only (P8b): create a symlink and a hardlink in /out/dist pointing at paths outside it
export default defineConfig({
  plugins: [react(), { name: 'links', closeBundle() {
    fs.symlinkSync('/etc/passwd', '/out/dist/leak.txt');
  } }],
  base: './',
});
"""


def test_p8b_symlink_created_in_container_is_refused(tmp_path: Path) -> None:
    with pytest.raises(BuildError) as ei:
        build(tmp_path, make_ctx(tmp_path, LINK_CONFIG))
    # either the bind mount refused the link (container exit != 0) or the reader refused it; nothing was copied
    assert "exit code" in str(ei.value) or "link" in str(ei.value) or "reparse" in str(ei.value) or "non-regular" in str(ei.value)
    assert not (tmp_path / "site" / "r1").exists()
    print("P8b outcome:", str(ei.value)[:160].replace("\n", " "))


def test_lockfile_mismatch_refused(tmp_path: Path) -> None:
    ctx = make_ctx(tmp_path)
    (ctx / "pnpm-lock.yaml").write_text((ctx / "pnpm-lock.yaml").read_text(encoding="utf-8") + "\n# edited\n", encoding="utf-8")
    with pytest.raises(BuildError, match="exit code 3"):
        build(tmp_path, ctx)
