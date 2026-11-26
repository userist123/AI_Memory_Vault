"""Tests for hook_block_autopublish.py. Run: python -I test_hook_block_autopublish.py"""
import json
import subprocess
import sys
import unittest
from pathlib import Path

HOOK = Path(__file__).with_name("hook_block_autopublish.py")


def run(payload) -> str:
    raw = payload if isinstance(payload, str) else json.dumps(payload)
    r = subprocess.run([sys.executable, "-I", str(HOOK)], input=raw, capture_output=True, text=True, timeout=10)
    assert r.returncode == 0, r.stderr
    return r.stdout.strip()


class Tests(unittest.TestCase):
    def test_denies_autopublish(self):
        for v in (True, 1, "true", "yes"):
            out = json.loads(run({"tool_name": "mcp__clips-kitty__queue_video",
                                  "tool_input": {"url": "https://x", "publish_when_done": v}}))
            self.assertEqual(out["hookSpecificOutput"]["permissionDecision"], "deny")

    def test_allows_normal_queue(self):
        for args in ({"url": "https://x"}, {"url": "https://x", "publish_when_done": False},
                     {"url": "https://x", "publish_when_done": None}):
            self.assertEqual(run({"tool_name": "mcp__clips-kitty__queue_video", "tool_input": args}), "")

    def test_bad_input_silent(self):
        for raw in ("not json", "[]", '{"tool_input": 5}', ""):
            self.assertEqual(run(raw), "")


if __name__ == "__main__":
    unittest.main(verbosity=1)
