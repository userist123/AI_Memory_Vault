"""W3 - flow tests: the agent writes Playwright-style smoke tests to tests/flows/*.spec.ts; the studio runs them.

Security model under test: a spec file is DATA. It is never executed as Node/JS on the host: flows.parse_spec reads a
closed subset of the Playwright test API (fixed statement shapes, closed method/option tables, relative goto only, no
page.evaluate / route / request / addInitScript, no variables other than locator aliases) and the studio's own Python
Playwright drives Chromium with the preview browser args, a same-origin route filter and fixed timeouts. Nothing in
the spec can choose a config, a timeout above the caps, a browser flag or another origin."""
from __future__ import annotations

import hashlib
import http.server
import os
import threading
from pathlib import Path
from typing import Any

import pytest
from aas_build import capture, flows, service
from aas_build.preview_server import PreviewServer
from studio_core import activity, appfiles
from studio_core.errors import Unavailable, ValidationFailed

from .conftest import OWNER
from .test_build_service import DOCKER, IMAGE, SEEN, make_fake

GOOD = """import { test, expect } from '@playwright/test';

// navigation + persistence + dark mode
test.describe('todo', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
  });

  test('adds an item and keeps it after reload', async ({ page }) => {
    const input = page.getByPlaceholder('New item');
    await input.fill('Milk');
    await page.getByRole('button', { name: 'Add', exact: true }).click();
    await expect(page.getByRole('listitem')).toHaveCount(1);
    await page.reload();
    await expect(page.getByText('Milk')).toBeVisible();
    await expect(page.locator('li').first()).toContainText(/milk/i);
  });

  test("dark mode", async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await expect(page.getByTestId('missing')).not.toBeVisible();
    await page.keyboard.press('Tab');
    await expect(page).toHaveURL('/');
    await expect(page).toHaveTitle(/todo/);
  });
});
"""


def new_project(studio) -> str:  # type: ignore[no-untyped-def]
    return studio.create_project(OWNER, "p1", "blank", "")["id"]


# ---------------------------------------------------------------- parser: accepted subset
def test_parse_accepts_the_playwright_subset() -> None:
    tests = flows.parse_spec(GOOD, "todo")
    assert [t.title for t in tests] == ["todo › adds an item and keeps it after reload", "todo › dark mode"]
    first = tests[0]
    assert first.steps[0].name == "goto" and first.steps[0].args == ("/",)  # beforeEach is prepended
    assert [s.kind for s in first.steps] == ["page", "action", "action", "expect", "page", "expect", "expect"]
    fill = first.steps[1]
    assert fill.name == "fill" and fill.args == ("Milk",) and fill.locator[0].name == "getByPlaceholder"
    rx = first.steps[-1].args[0]
    assert isinstance(rx, flows.Regex) and rx.source == "milk" and rx.flags == "i"
    dark = tests[1]
    assert dark.steps[1].name == "emulateMedia" and dark.steps[1].args == ({"colorScheme": "dark"},)
    assert dark.steps[3].negated is True and dark.steps[3].name == "toBeVisible"


@pytest.mark.parametrize("bad, why", [
    ("test('x', async ({ page }) => { await page.evaluate(() => fetch('http://evil')); });", "evaluate"),
    ("test('x', async ({ page }) => { await page.goto('http://example.com/'); });", "relative"),
    ("test('x', async ({ page }) => { await page.goto('//example.com/'); });", "relative"),
    ("test('x', async ({ page }) => { await page.goto('/\\\\evil.com'); });", "relative"),
    ("test('x', async ({ page }) => { await page.route('**', r => r.continue()); });", "route"),
    ("test('x', async ({ page }) => { await page.addInitScript('alert(1)'); });", "addInitScript"),
    ("test('x', async ({ page, request }) => { await request.get('/'); });", "page"),
    ("test('x', async ({ page }) => { await page.waitForTimeout(600000); });", "at most"),
    ("test('x', async ({ page }) => { await page.getByText(`a${process.env.X}`).click(); });", "template"),
    ("test('x', async ({ page }) => { await page.getByText('a' + 'b').click(); });", "unexpected"),
    ("test('x', async ({ page }) => { await page.getByRole('button').click({ force: true, timeout: 999999 }); });",
     "options"),
    ("test('x', async ({ page }) => { await expect(page.getByText('a')).toBeVisible({ timeout: 999999 }); });",
     "options"),
    ("const fs = require('fs');\ntest('x', async ({ page }) => {});", "top level"),
    ("import fs from 'fs';\ntest('x', async ({ page }) => {});", "import"),
    ("import { test } from 'node:fs';\ntest('x', async ({ page }) => {});", "@playwright/test"),
    ("test.use({ baseURL: 'http://evil' });", "top level"),
    ("test('x', async ({ page }) => { console.log(1); });", "statement"),
    ("test('x', async ({ page }) => { await page.screenshot({ path: 'C:/x.png' }); });", "screenshot"),
    ("test('x', async ({ page }) => { await page.locator('a').evaluate(e => e.click()); });", "evaluate"),
    ("test('x', async ({ page }) => { await page.setExtraHTTPHeaders({}); });", "setExtraHTTPHeaders"),
    ("test('x', async ({ page }) => { await page.getByText(/(a+)+$/).click(); });", "regular expression"),
    ("test('x', async ({ page }) => { await page.context().clearCookies(); });", "context"),
    ("test('x', async ({ page }) => { await page['evaluate']('1'); });", "unexpected"),
    ("", "no test"),
])
def test_parse_refuses_everything_outside_the_subset(bad: str, why: str) -> None:
    with pytest.raises(ValidationFailed) as ei:
        flows.parse_spec(bad, "bad")
    assert why.lower() in str(ei.value).lower() + str(getattr(ei.value, "details", "")).lower(), str(ei.value)


def test_parse_caps_sizes() -> None:
    many = "\n".join(f"test('t{i}', async ({{ page }}) => {{ await page.reload(); }});" for i in range(flows.MAX_TESTS_PER_FILE + 1))
    with pytest.raises(ValidationFailed, match="tests per file"):
        flows.parse_spec(many, "many")
    steps = " ".join("await page.reload();" for _ in range(flows.MAX_STEPS + 1))
    with pytest.raises(ValidationFailed, match="steps"):
        flows.parse_spec(f"test('t', async ({{ page }}) => {{ {steps} }});", "long")
    with pytest.raises(ValidationFailed, match="bytes"):
        flows.parse_spec("//" + "x" * flows.MAX_SPEC_BYTES, "big")
    with pytest.raises(ValidationFailed, match="characters"):
        flows.parse_spec("test('t', async ({ page }) => { await page.getByText('" + "x" * 600 + "').click(); });", "s")


def test_parse_error_names_the_line() -> None:
    with pytest.raises(ValidationFailed) as ei:
        flows.parse_spec("test('ok', async ({ page }) => {\n  await page.reload();\n  await page.evaluate('1');\n});", "f")
    assert "line 3" in str(ei.value)


# ---------------------------------------------------------------- file allow-list (narrow, extension-checked)
@pytest.mark.parametrize("path", ["tests/flows/todo.spec.ts", "tests/flows/Nav_1-main.spec.ts", "tests/flows/a.SPEC.TS"])
def test_allow_list_accepts_flow_specs(path: str) -> None:
    assert appfiles.validate_path(path) == path


@pytest.mark.parametrize("path", [
    "tests/flows/todo.ts", "tests/flows/todo.spec.js", "tests/flows/todo.spec.tsx", "tests/todo.spec.ts",
    "tests/flows/sub/todo.spec.ts", "tests/flows/.x.spec.ts", "tests/flows/a.b.spec.ts", "tests/flows/con.spec.ts",
    "tests/flows/package.spec.ts", "Tests/flows/todo.spec.ts", "tests/Flows/todo.spec.ts", "tests/flows/../x.spec.ts",
    "tests/flows/todo.spec.ts:x", "tests/flows/todo .spec.ts", "tests/flows/playwright.config.ts", "tests/flows/x.json",
    "tests/fixtures/x.spec.ts", "tests/flows/" + "a" * 42 + ".spec.ts", "tests/flows/.spec.ts",
])
def test_allow_list_refuses_other_test_paths(path: str) -> None:
    with pytest.raises(ValidationFailed):
        appfiles.validate_path(path)


def test_flow_specs_are_versioned_but_never_reach_the_build_context(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    rev = studio.get_revision(pid)
    studio.write_file(OWNER, pid, rev, "tests/flows/todo.spec.ts", GOOD)
    assert "tests" in studio.kind.file_roots
    service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    assert not any(f.startswith("tests/") for f in SEEN["ctx_files"])
    assert "src/aas-states.ts" in SEEN["ctx_files"]  # the kit state hook ships with every template


def test_templates_ship_the_kit_state_hook(studio) -> None:  # type: ignore[no-untyped-def]
    kit = (Path(service.KIT) / "template" / "src" / "aas-states.ts").read_text(encoding="utf-8")
    for template in ("blank", "tabs", "list-detail"):
        pid = studio.create_project(OWNER, f"t-{template}", template, "")["id"]
        assert studio.read_file(OWNER, pid, "src/aas-states.ts")["content"] == kit
    assert "aas-state" in kit and "useAasState" in kit and "postMessage" not in kit


# ---------------------------------------------------------------- runner (fake executor)
def _fake_exec(results: list[dict[str, Any]], console: list[str] | None = None):  # type: ignore[no-untyped-def]
    seen: dict[str, Any] = {}

    def run(origin: str, frame: str, tests: list[flows.FlowTest]) -> tuple[list[dict[str, Any]], list[str]]:
        seen.update(origin=origin, frame=frame, titles=[t.title for t in tests])
        return results[: len(tests)], list(console or [])
    return run, seen


def _png() -> bytes:
    import io

    from PIL import Image
    b = io.BytesIO()
    Image.new("RGB", (390, 844), (200, 30, 30)).save(b, format="PNG")
    return b.getvalue()


def test_run_requires_a_build_and_a_spec(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    run, _ = _fake_exec([])
    with pytest.raises(Unavailable, match="build"):
        flows.run(studio, OWNER, pid, execute=run)
    service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    with pytest.raises(ValidationFailed, match="flow_test_write"):
        flows.run(studio, OWNER, pid, execute=run)
    with pytest.raises(ValidationFailed):
        flows.run(studio, OWNER, "prj_nothex", execute=run)
    with pytest.raises(ValidationFailed):
        flows.run(studio, OWNER, pid, frame="watch", execute=run)  # type: ignore[arg-type]


def test_run_reports_pass_fail_with_screenshot_and_records_activity(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    rev = studio.get_revision(pid)
    rev = studio.write_file(OWNER, pid, rev, "tests/flows/todo.spec.ts", GOOD)["revision"]
    rev = studio.write_file(OWNER, pid, rev, "tests/flows/broken.spec.ts", "test('x', async ({ page }) => { await page.evaluate('1'); });")["revision"]
    png = _png()
    run, seen = _fake_exec([{"status": "passed", "durationMs": 12},
                            {"status": "failed", "durationMs": 40, "step": 2, "error": "expected 'dark' got 'light'\x1b[31m",
                             "screenshot": png}], console=["console.error: boom"])
    res = flows.run(studio, OWNER, pid, execute=run)
    assert seen["origin"] == f"http://p-{pid[4:]}.localhost:{studio.settings.preview_port}"
    assert seen["frame"] == "phone"
    assert res["passed"] == 1 and res["failed"] == 2 and res["total"] == 3 and res["revision"] == rev
    by = {(t["file"], t["status"]) for t in res["tests"]}
    assert ("tests/flows/broken.spec.ts", "invalid") in by and ("tests/flows/todo.spec.ts", "failed") in by
    failed = next(t for t in res["tests"] if t["status"] == "failed")
    sha = hashlib.sha256(png).hexdigest()
    assert failed["screenshotSha256"] == sha and failed["failedStep"] == 2 and "toHaveAttribute" in failed["stepSource"]
    assert "error" not in failed  # page-derived text only in the untrusted lines
    assert any("expected 'dark'" in ln and "\x1b" not in ln for ln in res["errors"])
    assert "console.error: boom" in res["console"]
    assert capture.capture_file(studio.settings, sha) is not None  # stored like a preview capture (owner UI fetches it)
    evs = activity.list_events(studio, limit=500, project_id=pid)
    qa = [e for e in evs if e["kind"] == "qa.result"][-1]["payload"]
    assert qa["suite"] == "flows" and qa["passed"] == 1 and qa["failed"] == 2
    assert qa["summary"].startswith(f"flows rev {rev}: 1/3 passed") and sha[:12] in qa["summary"]
    shots = [e for e in evs if e["kind"] == "preview.captured" and e["payload"].get("sha256") == sha]
    assert shots and shots[0]["payload"]["frame"] == "phone"


def test_run_writes_the_w6_flow_results_contract(studio) -> None:  # type: ignore[no-untyped-def]
    """Integration W3+W6: a run writes <data>/flow_results/<pid>.json = {"results": [{flow, status, at, message}]}
    (one entry per spec file), merged across runs, and the W6 acceptance checklist reads it."""
    import json as _json

    from aas_build import baselines
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    rev = studio.get_revision(pid)
    rev = studio.write_file(OWNER, pid, rev, "tests/flows/todo.spec.ts", GOOD)["revision"]
    studio.write_file(OWNER, pid, rev, "tests/flows/Nav.spec.ts", "test('n', async ({ page }) => { await page.reload(); });")

    def run(origin: str, frame: str, tests: list[flows.FlowTest]) -> tuple[list[dict[str, Any]], list[str]]:
        return [{"status": "passed", "durationMs": 1} if t.title == "n" else
                {"status": "failed", "durationMs": 2, "step": 0, "error": "boom\x1b[31m <b>x</b>"} for t in tests], []
    flows.run(studio, OWNER, pid, execute=run)
    path = Path(studio.settings.data_dir) / "flow_results" / f"{pid}.json"
    data = _json.loads(path.read_text(encoding="utf-8"))
    by = {r["flow"]: r for r in data["results"]}
    assert set(by) == {"nav", "todo"}  # flow id = spec file name, lowercased (baselines.FLOW_ID_RE)
    for r in data["results"]:
        assert set(r) == {"flow", "status", "at", "message"} and r["status"] in baselines.FLOW_STATUSES
        assert baselines.FLOW_ID_RE.match(r["flow"]) and r["at"].endswith("Z") and len(r["message"]) <= 300
    failed = [r for r in data["results"] if r["status"] == "fail"]
    assert len(failed) == 1 and "\x1b" not in failed[0]["message"] and failed[0]["flow"] == "todo"
    assert failed[0]["message"].startswith("0/2 passed: ") and "boom" in failed[0]["message"]
    assert by["nav"]["status"] == "pass" and by["nav"]["message"] == "1/1 passed"
    # a run of one file keeps the other file's last result
    run2, _ = _fake_exec([{"status": "passed", "durationMs": 1}] * 2)
    flows.run(studio, OWNER, pid, file="todo", execute=run2)
    by2 = {r["flow"]: r for r in _json.loads(path.read_text(encoding="utf-8"))["results"]}
    assert set(by2) == set(by) and by2["nav"] == by["nav"]
    # the W6 reader sees it in the owner's acceptance checklist
    baselines.set_checklist(studio, OWNER, pid, {"flows": [{"id": "todo", "title": "Todo", "statements": ["adds"]},
                                                           {"id": "nav", "title": "Nav", "statements": ["moves"]},
                                                           {"id": "none", "title": "None", "statements": ["x"]}]})
    cl = {f["id"]: f["result"]["status"] for f in baselines.get_checklist(studio.settings, pid)["flows"]}
    assert cl == {"todo": by2["todo"]["status"], "nav": by["nav"]["status"], "none": "unknown"}
    assert by2["todo"]["status"] == "pass" and by2["todo"]["message"] == "2/2 passed"
    baselines.forget_project(studio.settings, pid)  # project delete drops the results too
    assert not path.exists()


def test_run_filters_one_file_and_bounds_total_tests(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    rev = studio.get_revision(pid)
    rev = studio.write_file(OWNER, pid, rev, "tests/flows/todo.spec.ts", GOOD)["revision"]
    studio.write_file(OWNER, pid, rev, "tests/flows/other.spec.ts", "test('o', async ({ page }) => { await page.reload(); });")
    run, seen = _fake_exec([{"status": "passed", "durationMs": 1}] * 5)
    res = flows.run(studio, OWNER, pid, file="other", frame="desktop", execute=run)
    assert seen["titles"] == ["o"] and seen["frame"] == "desktop" and res["total"] == 1
    with pytest.raises(ValidationFailed):
        flows.run(studio, OWNER, pid, file="../x", execute=run)
    with pytest.raises(ValidationFailed, match="not found"):
        flows.run(studio, OWNER, pid, file="nope", execute=run)


def test_run_times_out_and_kills(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    studio.write_file(OWNER, pid, studio.get_revision(pid), "tests/flows/todo.spec.ts", GOOD)
    gate = threading.Event()
    killed: list[bool] = []

    def hang(origin: str, frame: str, tests: list[Any]) -> Any:
        gate.wait(5)
        return [], []
    with pytest.raises(Unavailable, match="timed out"):
        flows.run(studio, OWNER, pid, execute=hang, timeout=0.2, kill=lambda: (killed.append(True), gate.set()))
    assert killed == [True]
    run, _ = _fake_exec([{"status": "passed", "durationMs": 1}] * 2)
    assert flows.run(studio, OWNER, pid, execute=run)["passed"] == 2  # the lock was released


def test_run_drops_a_non_png_screenshot(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    studio.write_file(OWNER, pid, studio.get_revision(pid), "tests/flows/o.spec.ts", "test('o', async ({ page }) => { await page.reload(); });")
    run, _ = _fake_exec([{"status": "failed", "durationMs": 1, "step": 0, "error": "x", "screenshot": b"<html>"}])
    res = flows.run(studio, OWNER, pid, execute=run)
    assert res["tests"][0]["screenshotSha256"] is None


# ---------------------------------------------------------------- real Chromium against a real preview origin
def _chromium_available() -> bool:
    root = os.environ.get("PLAYWRIGHT_BROWSERS_PATH") or os.path.join(os.environ.get("LOCALAPPDATA", ""), "ms-playwright")
    return any(Path(root).glob("chromium*"))


APP = """<!doctype html><html><head><title>todo app</title>
<script src="app.js" defer></script></head><body><main>
<input placeholder="New item"><button>Add</button><ul></ul><img src="http://127.0.0.1:{cport}/leak">
</main></body></html>"""
APP_JS = """
const ul = document.querySelector('ul'), input = document.querySelector('input');
const load = () => JSON.parse(localStorage.getItem('items') || '[]');
const draw = () => { ul.innerHTML = ''; for (const t of load()) { const li = document.createElement('li'); li.textContent = t; ul.append(li); } };
document.querySelector('button').onclick = () => { localStorage.setItem('items', JSON.stringify([...load(), input.value])); input.value = ''; draw(); };
const mq = matchMedia('(prefers-color-scheme: dark)');
const theme = () => document.documentElement.dataset.theme = mq.matches ? 'dark' : 'light';
mq.addEventListener('change', theme); theme(); draw();
fetch('http://example.invalid/x').catch(() => {});
"""
FAILING = """test('wrong text', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Remove' })).toBeVisible();
});"""


@pytest.mark.browser
@pytest.mark.skipif(not _chromium_available(), reason="Playwright Chromium not installed")
def test_real_flows_run_in_the_preview_origin_only(studio) -> None:  # type: ignore[no-untyped-def]
    hits: list[str] = []

    class Canary(http.server.BaseHTTPRequestHandler):
        def do_GET(self) -> None:  # noqa: N802
            hits.append(self.path)
            self.send_response(200)
            self.end_headers()

        def log_message(self, *a: Any) -> None:
            pass

    canary = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Canary)
    threading.Thread(target=canary.serve_forever, daemon=True).start()
    page = APP.replace("{cport}", str(canary.server_address[1]))

    def fake(*, dest: Path, **kw: Any):  # type: ignore[no-untyped-def]
        dest.mkdir(parents=True)
        (dest / "index.html").write_text(page, encoding="utf-8")
        (dest / "app.js").write_text(APP_JS, encoding="utf-8")
        from aas_build.runner import BuildResult
        return BuildResult(dest=dest, files=["index.html", "app.js"], total_bytes=len(page), dist_sha256="0" * 64)
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=fake, docker=DOCKER, image=IMAGE)
    rev = studio.write_file(OWNER, pid, studio.get_revision(pid), "tests/flows/todo.spec.ts", GOOD)["revision"]
    studio.write_file(OWNER, pid, rev, "tests/flows/failing.spec.ts", FAILING)
    srv = PreviewServer(service.site_root_for(studio.settings), port=0).start()
    try:
        res = flows.run(studio, OWNER, pid, url=f"http://p-{pid[4:]}.localhost:{srv.port}/")
    finally:
        srv.stop()
        canary.shutdown()
    status = {t["title"]: t["status"] for t in res["tests"]}
    assert status == {"todo › adds an item and keeps it after reload": "passed", "todo › dark mode": "passed",
                      "wrong text": "failed"}, res
    bad = next(t for t in res["tests"] if t["status"] == "failed")
    assert bad["screenshotSha256"] and capture.capture_file(studio.settings, bad["screenshotSha256"]) is not None
    assert any("Remove" in e for e in res["errors"]), res["errors"]
    assert hits == []  # neither the <img> to the loopback canary nor any other origin left the browser
