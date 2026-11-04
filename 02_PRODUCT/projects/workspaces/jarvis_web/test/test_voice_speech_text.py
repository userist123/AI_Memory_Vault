"""speech_text: markdown stripping behaviour and the ReDoS timing probe."""
from __future__ import annotations

import importlib.util
import time
from pathlib import Path

VOICE_SERVER = Path(__file__).resolve().parents[1] / "voice_server.py"
_spec = importlib.util.spec_from_file_location("jarvis_web_voice_server", VOICE_SERVER)
voice_server = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(voice_server)
speech_text = voice_server.speech_text


def test_markdown_link_keeps_label():
    assert speech_text("See [the docs](docs/page.md) now") == "See the docs now"


def test_markdown_cleanup():
    text = "# Title\n- item one\n* item two\n**bold** `code` ```block\nx``` end"
    assert speech_text(text) == "Title item one item two bold code end"


def test_bare_url_removed():
    assert speech_text("go to https://example.com/x please") == "go to please"


def test_output_capped():
    assert len(speech_text("a" * 20000)) == 6000


def test_text_after_long_code_block_is_spoken():
    code = "```\n" + "x = 1\n" * 1200 + "```"
    assert speech_text("Here is the code: " + code + " Summary: done.") == "Here is the code: Summary: done."


def test_input_bounded_before_processing():
    # Content past MAX_SPEECH_INPUT is never processed.
    limit = voice_server.MAX_SPEECH_INPUT
    assert "TAIL" not in speech_text("```" + "a" * limit + "TAIL")


def _timed(text: str) -> float:
    start = time.perf_counter()
    speech_text(text)
    return time.perf_counter() - start


def test_redos_probes_are_fast():
    probes = [
        "[" * 100_000,
        "[\\" * 50_000,
        "[a](" * 25_000,
        "[a](b" * 20_000,
        " \n" * 50_000,
        "`" * 100_000,
    ]
    for probe in probes:
        assert _timed(probe) < 0.5, probe[:10]
