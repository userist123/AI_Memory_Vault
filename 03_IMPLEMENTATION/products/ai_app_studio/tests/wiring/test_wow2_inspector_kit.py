"""wow2 #6: the preview renders kit components (ui-btn--*, ui-toast--*), not token utilities. The inspector swaps those
modifiers by editing the component's prop; the allow-list is parsed from the kit catalogue (single source)."""
from __future__ import annotations

from typing import Any

import pytest
from apps.backend import inspector_routes as ir
from studio_core import app_templates as at
from studio_core.errors import NotFound, ValidationFailed

SRC = '''import { Button, Toast } from "./ui";
export default function App() {
  return (
    <div>
      <Button onClick={a}>Save</Button>
      <Button variant="secondary" onClick={b}>Cancel</Button>
      <Button variant="danger" onClick={c}>Delete</Button>
      <Toast open message="Done" tone="success" onDismiss={d} />
    </div>
  );
}
'''
FILES = {"src/App.tsx": SRC, "src/ui/Button.tsx": 'cx("ui-btn", `ui-btn--${variant}`)\n'}


def desc(tag: str, text: str, classes: list[str]) -> ir.Descriptor:
    return ir.Descriptor.model_validate({"v": 1, "kind": "select", "path": "main>x", "tag": tag, "text": text,
                                         "rect": {"x": 1, "y": 1, "w": 10, "h": 10}, "classes": classes})


def test_allow_list_comes_from_the_kit_catalogue() -> None:
    mods = at.component_modifiers()
    assert mods["ui-btn"]["values"] == ["primary", "secondary", "ghost", "danger"] and mods["ui-btn"]["default"] == "primary"
    assert mods["ui-toast"]["values"] == ["neutral", "success", "danger"]
    assert ir.COMPONENT_CLASSES == at.component_classes()
    css = (at.KIT_TEMPLATE / "src" / "ui" / "ui.css").read_text(encoding="utf-8")
    for c in ir.COMPONENT_CLASSES:  # every non-default modifier class really exists in the kit CSS
        if not c.endswith(("--primary", "--neutral")):
            assert f".{c}" in css, c


def test_swap_existing_variant_literal() -> None:
    d = desc("button", "Cancel", ["ui-btn", "ui-btn--secondary"])
    path, out = ir.plan_class_swap(FILES, d, "ui-btn--secondary", "ui-btn--danger")
    assert path == "src/App.tsx" and '<Button variant="danger" onClick={b}>Cancel</Button>' in out
    assert 'variant="danger" onClick={c}>Delete' in out and "<Button onClick={a}>Save</Button>" in out  # others untouched


def test_swap_default_variant_inserts_the_prop() -> None:
    d = desc("button", "Save", ["ui-btn", "ui-btn--primary"])
    _, out = ir.plan_class_swap(FILES, d, "ui-btn--primary", "ui-btn--ghost")
    assert '<Button variant="ghost" onClick={a}>Save</Button>' in out


def test_swap_toast_tone() -> None:
    d = desc("div", "Done", ["ui-toast", "ui-toast--success"])
    _, out = ir.plan_class_swap(FILES, d, "ui-toast--success", "ui-toast--danger")
    assert 'message="Done" tone="danger"' in out


def test_kit_component_files_are_never_edited() -> None:
    d = desc("button", "Save", ["ui-btn", "ui-btn--primary"])
    path, _ = ir.plan_class_swap(FILES, d, "ui-btn--primary", "ui-btn--ghost")
    assert not path.startswith("src/ui/")


@pytest.mark.parametrize(("old", "new"), [("ui-btn--primary", "ui-toast--danger"), ("ui-btn--primary", "ui-btn--huge"),
                                          ("ui-btn--primary", "ui-btn--primary"), ("ui-btn--primary", "ui-btn--lg"),
                                          ("ui-btn--primary", "bg-danger"), ("ui-btn--primary", "ui-btn--ghost evil")])
def test_swaps_outside_one_component_family_are_refused(old: str, new: str) -> None:
    d = desc("button", "Save", ["ui-btn", old])
    with pytest.raises(ValidationFailed):
        ir.plan_class_swap(FILES, d, old, new)


def test_ambiguous_and_missing_targets() -> None:
    two = {"src/App.tsx": '<div><Button onClick={a}>Go</Button><Button onClick={b}>Go</Button></div>\n'}
    d = desc("button", "Go", ["ui-btn", "ui-btn--primary"])
    with pytest.raises(ir.AmbiguousEdit):
        ir.plan_class_swap(two, d, "ui-btn--primary", "ui-btn--ghost")
    with pytest.raises(NotFound):
        ir.plan_class_swap({"src/App.tsx": '<Button variant={v}>Go</Button>\n'}, d, "ui-btn--primary", "ui-btn--ghost")
    with pytest.raises(ValidationFailed):  # the element does not carry the class
        ir.plan_class_swap(FILES, desc("button", "Save", ["ui-btn"]), "ui-btn--primary", "ui-btn--ghost")


def test_existing_token_swaps_still_work() -> None:
    f = {"src/A.tsx": 'export const A = () => <div className="bg-surface p-2">a</div>;\n'}
    _, out = ir.plan_class_swap(f, desc("div", "a", ["bg-surface"]), "bg-surface", "bg-surface-2")
    assert 'className="bg-surface-2 p-2"' in out


def test_rest_exposes_the_component_family(tmp_path: Any) -> None:
    from apps.backend.main import create_app
    from starlette.testclient import TestClient
    from studio_core import project_kind
    from studio_core.config import Settings
    app = create_app(Settings(data_dir=tmp_path / "data", preview_port=0), "t" * 40)
    h = {"Authorization": "Bearer " + "t" * 40}
    try:
        with TestClient(app, base_url="http://127.0.0.1:17875") as c:
            pid = c.post("/api/v1/projects", json={"name": "Notes"}, headers=h).json()["id"]
            body = c.get(f"/api/v1/projects/{pid}/inspector", headers=h).json()
            assert body["families"]["component"] == at.component_classes()
            assert body["families"]["color"] == ir.FAMILIES["color"]
            caps = c.get(f"/api/v1/projects/{pid}/capabilities", headers=h)
            assert caps.status_code == 200 and caps.json() == {"states": True, "theme": True}  # a fresh template has both
    finally:
        project_kind.unregister("app")
