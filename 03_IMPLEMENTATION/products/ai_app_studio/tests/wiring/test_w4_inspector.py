"""W4 click-to-edit inspector: descriptor schema, source location (text match / class token), owner-only REST edit
through the normal revisioned write_file operation + audit, rebuild, the kit inspector being materialised into every
build context, and the cropped capture for "Trimite in chat"."""
from __future__ import annotations

import io
import time
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest
from aas_build import capture, service
from apps.backend import inspector_routes as ir
from pydantic import ValidationError
from starlette.testclient import TestClient
from studio_core import project_kind
from studio_core.config import Settings
from studio_core.errors import ConflictError, NotFound, ValidationFailed

from .conftest import OWNER
from .test_build_service import DOCKER, IMAGE, SEEN, make_fake, new_project
from .test_export_apk_owner import fake_builds

AUTH = {"Authorization": "Bearer " + "t" * 40}


def desc(**over: Any) -> dict[str, Any]:
    d: dict[str, Any] = {"v": 1, "kind": "select", "path": "main>h1", "tag": "h1", "text": "Habits",
                         "rect": {"x": 16, "y": 40, "w": 200, "h": 32}, "classes": ["title"]}
    d.update(over)
    return d


# ---------------------------------------------------------------- descriptor schema
def test_valid_descriptor_parses() -> None:
    d = ir.Descriptor.model_validate(desc())
    assert d.tag == "h1" and d.rect.w == 200


@pytest.mark.parametrize("bad", [
    desc(v=2), desc(v="1"), desc(kind="click"), desc(extra=1), desc(tag="H1<script>"), desc(tag=""),
    desc(text="x" * 201), desc(text="a\x00b"), desc(text="a\u202eb"), desc(classes=["c"] * 21),
    desc(classes=["bad class"]), desc(classes=["x" * 65]), desc(path="a" * 301), desc(path="a\nb"),
    desc(rect={"x": 0, "y": 0, "w": -1, "h": 1}), desc(rect={"x": 0, "y": 0, "w": 1}),
    desc(rect={"x": 0, "y": 0, "w": 1, "h": 1, "z": 1}), desc(rect={"x": 1e9, "y": 0, "w": 1, "h": 1}),
    desc(rect={"x": "1", "y": 0, "w": 1, "h": 1}), desc(classes="title"),
])
def test_malformed_descriptors_are_rejected(bad: dict[str, Any]) -> None:
    with pytest.raises(ValidationError):
        ir.Descriptor.model_validate(bad)


def test_nan_and_infinity_are_rejected() -> None:
    for v in (float("nan"), float("inf")):
        with pytest.raises(ValidationError):
            ir.Descriptor.model_validate(desc(rect={"x": v, "y": 0, "w": 1, "h": 1}))


# ---------------------------------------------------------------- text edits (pure)
APP = """export default function App() {
  return (
    <main>
      <h1 className="title">
        Habits
      </h1>
      <p className="lead p-2 text-muted">No habits yet.</p>
      <button className="btn bg-primary p-2">Add habit</button>
    </main>
  );
}
"""


def files(**more: str) -> dict[str, str]:
    return {"src/App.tsx": APP, "src/main.tsx": "import App from './App';\n", **more}


def test_text_edit_replaces_the_unique_jsx_text() -> None:
    path, out = ir.plan_text_edit(files(), ir.Descriptor.model_validate(desc()), "My habits")
    assert path == "src/App.tsx" and ">\n        My habits\n      </h1>" in out
    assert out.count("Habits") == 0 and "No habits yet." in out


def test_text_edit_in_a_string_literal_escapes_the_quote() -> None:
    data = 'export const ITEMS = ["Drink water", "Walk"];\n'
    d = ir.Descriptor.model_validate(desc(tag="li", text="Drink water", classes=[]))
    path, out = ir.plan_text_edit(files(**{"src/data.ts": data}), d, 'Drink "more" water\\')
    assert path == "src/data.ts" and out == 'export const ITEMS = ["Drink \\"more\\" water\\\\", "Walk"];\n'


@pytest.mark.parametrize("new", ["<img src=x onerror=alert(1)>", "{process.exit()}", "a}", "", "  ", "a\nb", "x" * 201,
                                 "a\u2028b"])
def test_text_edit_refuses_markup_expressions_and_controls(new: str) -> None:
    with pytest.raises(ValidationFailed):
        ir.plan_text_edit(files(), ir.Descriptor.model_validate(desc()), new)


def test_text_not_in_source_is_not_found() -> None:
    with pytest.raises(NotFound):
        ir.plan_text_edit(files(), ir.Descriptor.model_validate(desc(text="Computed 42")), "x")


def test_ambiguous_text_is_refused() -> None:
    other = "export const B = () => <section><h2>Habits</h2></section>;\n"
    d = ir.Descriptor.model_validate(desc(tag="div", classes=[]))  # neither tag nor classes break the tie
    with pytest.raises(ir.AmbiguousEdit):
        ir.plan_text_edit(files(**{"src/B.tsx": other}), d, "x")


def test_code_expression_with_angle_brackets_is_not_a_jsx_text_match() -> None:
    code = ("export function f(count: number, Habits: number) {\n  return count > Habits < 3 ? 1 : 0;\n}\n")
    f = files(**{"src/util.tsx": code})
    path, out = ir.plan_text_edit(f, ir.Descriptor.model_validate(desc()), "My habits")
    assert path == "src/App.tsx" and "count > Habits < 3" not in out and "My habits" in out
    only_code = {"src/util.tsx": code}  # the only "match" is a comparison: nothing to edit
    with pytest.raises(NotFound):
        ir.plan_text_edit(only_code, ir.Descriptor.model_validate(desc()), "x")


def test_jsx_text_after_an_opening_tag_with_arrow_attributes_still_matches() -> None:
    src = 'export const A = () => <button onClick={() => go()} className="b">Habits</button>;\n'
    path, out = ir.plan_text_edit({"src/A.tsx": src}, ir.Descriptor.model_validate(desc(tag="button")), "Done")
    assert out.endswith('>Done</button>;\n')


def test_tag_breaks_a_tie() -> None:
    other = "export const B = () => <section><h2>Habits</h2></section>;\n"
    path, out = ir.plan_text_edit(files(**{"src/B.tsx": other}), ir.Descriptor.model_validate(desc(tag="h2")), "Mine")
    assert path == "src/B.tsx" and "<h2>Mine</h2>" in out


def test_imports_class_names_and_kit_files_are_never_text_targets() -> None:
    f = {"src/App.tsx": 'import x from "react";\nexport const A = () => <p className="react">hi</p>;\n',
         "src/aas-inspector.ts": 'const s = "react";\n'}
    with pytest.raises(NotFound):
        ir.plan_text_edit(f, ir.Descriptor.model_validate(desc(tag="p", text="react", classes=["react"])), "vue")


def test_truncated_text_is_not_editable() -> None:
    with pytest.raises(ValidationFailed):
        ir.plan_text_edit(files(), ir.Descriptor.model_validate(desc(text="x" * 200)), "y")


# ---------------------------------------------------------------- class swaps (pure)
def test_class_swap_within_the_same_family() -> None:
    d = ir.Descriptor.model_validate(desc(tag="button", text="Add habit", classes=["btn", "bg-primary", "p-2"]))
    path, out = ir.plan_class_swap(files(), d, "p-2", "p-4")
    assert path == "src/App.tsx" and 'className="btn bg-primary p-4"' in out
    assert 'className="lead p-2 text-muted"' in out  # the other element keeps its class
    _, out = ir.plan_class_swap(files(), d, "bg-primary", "bg-danger")
    assert 'className="btn bg-danger p-2"' in out


def test_allow_list_is_the_w1_design_system() -> None:
    """Integration W1+W4: the swappable classes are exactly one utility per W1 semantic token (single source)."""
    from studio_core import app_templates as at
    assert ir.FAMILIES == at.utility_classes()
    assert ir.FAMILIES["color"] == [f"{p}-{t}" for p in ("text", "bg", "border") for t in at.SEMANTIC_COLOR_TOKENS]
    assert ir.FAMILIES["spacing"] == [f"{p}-{s}" for p in ir.SPACING_PREFIXES for s in at.SPACING_STEPS]
    assert {"primary", "surface-2", "on-primary-soft", "danger"} <= set(at.SEMANTIC_COLOR_TOKENS)
    assert "bg-accent" not in ir.FAMILIES["color"] and "p-12" not in ir.FAMILIES["spacing"]  # not W1 tokens
    # every allowed class really exists in the kit CSS shipped to the project
    css = (Path(at.KIT_TEMPLATE) / "src" / "ui" / "utilities.css").read_text(encoding="utf-8")
    for c in ir.FAMILIES["color"] + ir.FAMILIES["spacing"]:
        assert f".{c} {{" in css, c


def test_class_swap_with_a_dashed_token_keeps_the_prefix_family() -> None:
    d = ir.Descriptor.model_validate(desc(tag="div", text="", classes=["bg-surface"]))
    f = {"src/A.tsx": 'export const A = () => <div className="bg-surface">a</div>;\n'}
    _, out = ir.plan_class_swap(f, d, "bg-surface", "bg-surface-2")
    assert 'className="bg-surface-2"' in out
    _, out = ir.plan_class_swap(f, d, "bg-surface", "bg-on-primary-soft")
    assert 'className="bg-on-primary-soft"' in out
    for bad in ("text-surface-2", "border-on-primary-soft", "p-2"):
        with pytest.raises(ValidationFailed):
            ir.plan_class_swap(f, d, "bg-surface", bad)


@pytest.mark.parametrize(("old", "new"), [("p-2", "bg-danger"), ("p-2", "bg-accent"), ("p-2", "p-99"), ("p-2", "p-2"), ("btn", "p-4"),
                                          ("p-4", "p-6"), ("p-2", "p-4 evil"), ("bg-primary", "px-2")])
def test_class_swap_outside_the_allowed_list_is_refused(old: str, new: str) -> None:
    d = ir.Descriptor.model_validate(desc(tag="button", text="Add habit", classes=["btn", "bg-primary", "p-2"]))
    with pytest.raises(ValidationFailed):
        ir.plan_class_swap(files(), d, old, new)


def test_class_swap_ambiguous_is_refused() -> None:
    d = ir.Descriptor.model_validate(desc(tag="span", text="", classes=["p-2"]))  # matches two className strings loosely
    f = {"src/A.tsx": 'export const A = () => <><span className="p-2">a</span><span className="p-2">b</span></>;\n'}
    with pytest.raises(ir.AmbiguousEdit):
        ir.plan_class_swap(f, d, "p-2", "p-4")


# ---------------------------------------------------------------- kit inspector in every build context
def test_build_context_always_carries_the_kit_inspector(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    rev = studio.get_revision(pid)
    studio.write_file(OWNER, pid, rev, "src/aas-inspector.ts", "window.parent.postMessage('x', '*');\n")
    service.build_web(studio, pid, run_build=make_fake(), docker=DOCKER, image=IMAGE)
    assert "src/aas-inspector.ts" in SEEN["ctx_files"]
    kit = (service.KIT / "template" / "src" / "aas-inspector.ts").read_text(encoding="utf-8")
    assert "postMessage('x'" not in kit
    html = (service.KIT / "index.html").read_text(encoding="utf-8")
    assert '<script type="module" src="/src/aas-inspector.ts"></script>' in html
    assert "script-src 'self';" in html  # CSP unchanged: no inline script, no eval


def test_kit_inspector_never_evaluates_or_injects_markup() -> None:
    src = (service.KIT / "template" / "src" / "aas-inspector.ts").read_text(encoding="utf-8")
    for bad in ("eval(", "new Function", "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write", "'*'", '"*"'):
        assert bad not in src, bad
    assert "location.hostname" in src and "event.source !== win.parent" in src


# ---------------------------------------------------------------- REST (owner only)
@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    from apps.backend.main import create_app
    app = create_app(Settings(data_dir=tmp_path / "data", preview_port=0), "t" * 40)
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        yield c
    project_kind.unregister("app")


def _wait(builds: Any, pid: str) -> dict[str, Any]:
    end = time.monotonic() + 20
    while time.monotonic() < end:
        st = builds.status(pid)
        if st["state"] != "building":
            return st
        time.sleep(0.05)
    raise AssertionError("build did not finish")


def test_rest_text_edit_is_revisioned_audited_and_rebuilds(client: TestClient) -> None:
    st = client.app.state.studio
    fake_builds(client.app.state.owner_builds)
    pid = client.post("/api/v1/projects", json={"name": "Notes"}, headers=AUTH).json()["id"]
    opts = client.get(f"/api/v1/projects/{pid}/inspector", headers=AUTH).json()
    rev = opts["revision"]
    assert "p-4" in opts["families"]["spacing"] and "bg-primary" in opts["families"]["color"]
    d = desc(text="Hello from AI App Studio", path="main>h1", classes=[])
    body = {"baseRevision": rev, "element": d, "op": "text", "to": "Salut"}
    assert client.post(f"/api/v1/projects/{pid}/inspector/edit", json=body).status_code == 401  # owner token only
    # step 1 (no confirm): a dry run naming the file and line, nothing written
    dry = client.post(f"/api/v1/projects/{pid}/inspector/edit", json=body, headers=AUTH)
    assert dry.status_code == 200, dry.text
    assert dry.json()["dryRun"] is True and dry.json()["path"] == "src/App.tsx" and dry.json()["line"] >= 1
    assert "<h1>Hello from AI App Studio</h1>" in st.read_file(OWNER, pid, "src/App.tsx")["content"]
    assert st.get_revision(pid) == rev
    # step 2: the owner confirmed
    body = {**body, "confirm": True}
    r = client.post(f"/api/v1/projects/{pid}/inspector/edit", json=body, headers=AUTH)
    assert r.status_code == 200, r.text
    j = r.json()
    assert j["path"] == "src/App.tsx" and j["revision"] > rev and j["rebuild"] in ("building", "ok")
    assert "<h1>Salut</h1>" in st.read_file(OWNER, pid, "src/App.tsx")["content"]
    assert _wait(client.app.state.owner_builds, pid)["state"] == "ok"
    # stale base revision -> 409 revision_conflict, nothing written
    r = client.post(f"/api/v1/projects/{pid}/inspector/edit", json={**body, "element": desc(text="Salut", classes=[])},
                    headers=AUTH)
    assert r.status_code == 409 and r.json()["error"] == "revision_conflict"
    with st.session() as s:  # the edit is an owner write_file operation in the audit trail
        from sqlalchemy import text
        rows = s.execute(text("select actor, action, outcome from audit_events where project_id = :p "
                              "and action like '%write_file%'"), {"p": pid}).all()
    assert rows and all(r[0] == "human:owner@rest" for r in rows), rows
    assert any(r[2] in ("ok", "success") for r in rows), rows


@pytest.mark.parametrize("body", [
    {"baseRevision": 1, "element": desc(), "op": "eval", "to": "x"},
    {"baseRevision": 1, "element": desc(v=2), "op": "text", "to": "x"},
    {"baseRevision": 1, "element": desc(), "op": "text", "to": "x", "extra": True},
    {"baseRevision": 1, "element": desc(text="y" * 5000), "op": "text", "to": "x"},
    {"baseRevision": 1, "element": desc(), "op": "class", "to": "p-4"},  # class swap needs "from"
])
def test_rest_rejects_malformed_or_oversized_edits(client: TestClient, body: dict[str, Any]) -> None:
    pid = client.post("/api/v1/projects", json={"name": "Notes"}, headers=AUTH).json()["id"]
    r = client.post(f"/api/v1/projects/{pid}/inspector/edit", json=body, headers=AUTH)
    assert r.status_code == 422, r.text


def test_rest_refuses_edits_while_a_run_is_live(client: TestClient, monkeypatch: pytest.MonkeyPatch) -> None:
    pid = client.post("/api/v1/projects", json={"name": "Notes"}, headers=AUTH).json()["id"]
    monkeypatch.setattr(client.app.state.runner, "live_project", lambda: pid)
    body = {"baseRevision": 1, "element": desc(text="Hello from AI App Studio"), "op": "text", "to": "x"}
    r = client.post(f"/api/v1/projects/{pid}/inspector/edit", json=body, headers=AUTH)
    assert r.status_code == 409 and r.json()["error"] == "run_in_progress"


def test_rest_bad_project_id_is_422(client: TestClient) -> None:
    assert client.get("/api/v1/projects/prj_zz/inspector", headers=AUTH).status_code == 422


def _png(w: int, h: int) -> bytes:
    from PIL import Image
    buf = io.BytesIO()
    Image.new("RGB", (w, h), (10, 20, 30)).save(buf, format="PNG")
    return buf.getvalue()


def test_rest_capture_crops_the_selected_rect(client: TestClient, monkeypatch: pytest.MonkeyPatch) -> None:
    st = client.app.state.studio
    pid = client.post("/api/v1/projects", json={"name": "Notes"}, headers=AUTH).json()["id"]
    service.build_web(st, pid, run_build=make_fake("r1"), docker=DOCKER, image=IMAGE)
    real = capture.capture
    monkeypatch.setattr(ir.capture, "capture", lambda studio, p, frames, **kw: real(
        studio, p, frames, shoot=lambda url, fr: {f: _png(*capture.FRAMES[f]) for f in fr}, free_bytes=lambda: 10**12))
    r = client.post(f"/api/v1/projects/{pid}/inspector/capture", json={"device": "phone", "element": desc()},
                    headers=AUTH)
    assert r.status_code == 200, r.text
    j = r.json()
    from PIL import Image
    got = Image.open(io.BytesIO(client.get(f"/api/v1/captures/{j['sha256']}", headers=AUTH).content))
    assert got.size == (j["width"], j["height"]) == (200 + 2 * ir.CROP_PAD, 32 + 2 * ir.CROP_PAD)
    r = client.post(f"/api/v1/projects/{pid}/inspector/capture", json={"device": "watch", "element": desc()},
                    headers=AUTH)
    assert r.status_code == 422


def test_crop_clamps_to_the_frame() -> None:
    box = ir.crop_box(ir.Descriptor.model_validate(desc(rect={"x": -50, "y": 830, "w": 900, "h": 900})).rect, 390, 844)
    assert box == (0, 830 - ir.CROP_PAD, 390, 844)
    with pytest.raises(ValidationFailed):
        ir.crop_box(ir.Descriptor.model_validate(desc(rect={"x": 5000, "y": 0, "w": 1, "h": 1})).rect, 390, 844)


def test_conflict_types() -> None:
    assert issubclass(ir.AmbiguousEdit, ConflictError) and ir.AmbiguousEdit.code == "inspector_ambiguous"


def test_rest_ambiguous_text_is_a_409_even_in_the_dry_run(client: TestClient) -> None:
    st = client.app.state.studio
    pid = client.post("/api/v1/projects", json={"name": "Notes"}, headers=AUTH).json()["id"]
    rev = st.get_revision(pid)
    st.write_file(OWNER, pid, rev, "src/Twin.tsx", "export const T = () => <h1>Hello from AI App Studio</h1>;\n")
    body = {"baseRevision": st.get_revision(pid), "op": "text", "to": "Salut",
            "element": desc(text="Hello from AI App Studio", path="main>h1", classes=[])}
    r = client.post(f"/api/v1/projects/{pid}/inspector/edit", json=body, headers=AUTH)
    assert r.status_code == 409 and r.json()["error"] == "inspector_ambiguous", r.text
    r = client.post(f"/api/v1/projects/{pid}/inspector/edit", json={**body, "confirm": True}, headers=AUTH)
    assert r.status_code == 409
