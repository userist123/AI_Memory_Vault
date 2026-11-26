"""S9b review L4 - CI: the product tests are scanned with default gitleaks rules (only exact fixture path + rule
allowed), and the lint tools are installed hash-checked (actionlint = sha256-verified release binary instead of the
sdist-only actionlint-py; zizmor with pip --require-hashes)."""
from __future__ import annotations

import re
from pathlib import Path

PRODUCT = Path(__file__).resolve().parents[2]
WF = PRODUCT.parents[2] / ".github" / "workflows" / "ai-app-studio.yml"
CFG = PRODUCT / ".gitleaks-tests.toml"


def test_tests_gitleaks_run_uses_the_product_config() -> None:
    wf = WF.read_text(encoding="utf-8")
    assert '--config "$PRODUCT_DIR/.gitleaks-tests.toml"' in wf and '"$PRODUCT_DIR/tests"' in wf


def test_tests_config_allowlists_only_exact_path_and_rule() -> None:
    cfg = CFG.read_text(encoding="utf-8")
    assert "useDefault = true" in cfg
    blocks = cfg.split("[[allowlists]]")[1:]
    assert blocks
    for b in blocks:
        paths = re.findall(r"'''(.*?)'''", b)
        assert len(paths) == 1, b
        if "__pycache__" in paths[0]:
            continue
        assert paths[0].endswith(r"\.py$") and "targetRules" in b, b
        assert len(re.findall(r'"([a-z-]+)"', b.split("targetRules", 1)[1].split("\n", 1)[0])) == 1


def test_lint_tools_are_hash_checked() -> None:
    wf = WF.read_text(encoding="utf-8")
    assert "actionlint-py" not in wf
    assert re.search(r'ACTIONLINT_SHA256: "[0-9a-f]{64}"', wf)
    assert 'echo "${ACTIONLINT_SHA256}' in wf
    assert re.search(r"pip install .*--require-hashes", wf)
    assert re.search(r"zizmor==\$\{ZIZMOR_VERSION\} --hash=sha256:[0-9a-f]{64}", wf)
