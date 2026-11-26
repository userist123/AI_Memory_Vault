"""AAS identity: vendored modules read AAS_* model / browser variables; the AWS_* names are ignored."""

from __future__ import annotations

from pathlib import Path

import pytest
from studio_core.ai import providers


@pytest.mark.parametrize(("cls", "name"), [(providers.ClaudeCodeProvider, "CLAUDE_CODE_MODEL"),
                                           (providers.AnthropicProvider, "ANTHROPIC_MODEL"),
                                           (providers.OpenAIProvider, "OPENAI_MODEL"),
                                           (providers.GeminiProvider, "GEMINI_MODEL")])
def test_provider_model_env(monkeypatch: pytest.MonkeyPatch, cls: type, name: str) -> None:
    monkeypatch.delenv(f"AAS_{name}", raising=False)
    monkeypatch.setenv(f"AWS_{name}", "aws-model-ignored")
    default = cls().model
    assert default != "aws-model-ignored"
    monkeypatch.setenv(f"AAS_{name}", "aas-model")
    assert cls().model == "aas-model"


def test_playwright_browsers_path_env(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    from studio_core.visual import service
    monkeypatch.delenv("AAS_PLAYWRIGHT_BROWSERS_PATH", raising=False)
    monkeypatch.setenv("AWS_PLAYWRIGHT_BROWSERS_PATH", str(tmp_path / "aws"))
    assert service.local_browsers_path() != tmp_path / "aws"
    monkeypatch.setenv("AAS_PLAYWRIGHT_BROWSERS_PATH", str(tmp_path / "aas"))
    assert service.local_browsers_path() == tmp_path / "aas"
