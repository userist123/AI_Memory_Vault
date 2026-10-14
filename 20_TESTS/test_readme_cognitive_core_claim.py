"""D-06: the READMEs must not call the cognitive core unwired while the controller wires it.

`memory/controller.py` imports `cognitive_core.{working_memory,global_workspace,reasoning,
executive}` and invokes them from `search()` behind flags that default to False
(VAULT_STATE.md agrees: "wired, OFF by default"). Both READMEs said "NOT WIRED INTO
PRODUCTION SEARCH" / "NECONECTAT LA CĂUTAREA DIN PRODUCȚIE".
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
CONTROLLER = REPO / "03_IMPLEMENTATION" / "packages" / "memory" / "controller.py"
READMES = ("README.md", "README.en.md")
STALE = re.compile(r"NOT WIRED|NECONECTAT|no production consumer|niciun consumator în producție", re.IGNORECASE)


def _cognitive_core_rows(text: str) -> list[str]:
    return [line for line in text.splitlines()
            if line.startswith("|") and re.search(r"Cognitive Core|Nucleul cognitiv", line)]


def test_controller_still_wires_the_cognitive_core():
    source = CONTROLLER.read_text(encoding="utf-8")
    assert re.search(r"^from cognitive_core\.", source, re.MULTILINE), \
        "controller.py no longer imports cognitive_core; revisit the README rows and this test"
    for flag in ("enable_working_memory", "enable_global_workspace", "enable_reasoning", "enable_executive"):
        assert re.search(rf"{flag}: bool = False", source), f"{flag} no longer defaults to False"


@pytest.mark.parametrize("readme", READMES)
def test_readme_does_not_call_the_cognitive_core_unwired(readme):
    rows = _cognitive_core_rows((REPO / readme).read_text(encoding="utf-8"))
    assert rows, f"{readme}: cognitive core status row not found"
    for row in rows:
        assert not STALE.search(row), f"{readme}: stale claim: {row}"
        assert re.search(r"OFF by default|OPRIT implicit", row), f"{readme}: row must say OFF by default"


def test_negative_control_the_old_wording_is_caught():
    old = ("| Cognitive Core | recall | **IMPLEMENTED AND TESTED — NOT WIRED INTO PRODUCTION SEARCH** "
           "(`executive` and the global workspace have no production consumer) |")
    assert STALE.search(_cognitive_core_rows(old)[0])
