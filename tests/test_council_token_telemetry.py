import unittest

from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path

MODULE = Path(__file__).parents[1] / "99_SYSTEM" / "council_token_telemetry.py"
spec = spec_from_file_location("council_token_telemetry", MODULE)
mod = module_from_spec(spec)
spec.loader.exec_module(mod)


class CouncilTokenTelemetryTests(unittest.TestCase):
    def test_estimate_is_nonzero_and_deterministic(self):
        self.assertEqual(mod.estimate_tokens("abcdef"), 2)
        self.assertEqual(mod.estimate_tokens("abcdef"), 2)

    def test_duplicate_context_is_accounted_once(self):
        telemetry = mod.CouncilTokenTelemetry()
        value = {"id": "same", "content": "x" * 300}
        telemetry.record_context([value, value])
        self.assertGreater(telemetry.raw_context_tokens, telemetry.deduplicated_context_tokens)
        self.assertEqual(
            telemetry.saved_by_deduplication,
            telemetry.raw_context_tokens - telemetry.deduplicated_context_tokens,
        )

    def test_specialist_and_synthesis_accounting(self):
        telemetry = mod.CouncilTokenTelemetry()
        telemetry.record_specialist("input", "output")
        telemetry.record_synthesis("synthesis input", "result")
        self.assertGreater(telemetry.specialist_input_tokens, 0)
        self.assertGreater(telemetry.specialist_output_tokens, 0)
        self.assertGreater(telemetry.synthesis_input_tokens, 0)
        self.assertGreater(telemetry.synthesis_output_tokens, 0)

    def test_total_includes_all_model_call_tokens(self):
        telemetry = mod.CouncilTokenTelemetry()
        telemetry.record_specialist("specialist input", "specialist output")
        telemetry.record_synthesis("synthesis input", "synthesis output")
        expected = (
            telemetry.specialist_input_tokens
            + telemetry.specialist_output_tokens
            + telemetry.synthesis_input_tokens
            + telemetry.synthesis_output_tokens
        )
        self.assertEqual(telemetry.estimated_total_tokens, expected)

    def test_context_is_not_double_counted_in_total(self):
        telemetry = mod.CouncilTokenTelemetry()
        telemetry.record_context(["x" * 300])
        telemetry.record_specialist("input", "output")
        telemetry.record_synthesis("input", "output")
        expected = (
            telemetry.specialist_input_tokens
            + telemetry.specialist_output_tokens
            + telemetry.synthesis_input_tokens
            + telemetry.synthesis_output_tokens
        )
        self.assertEqual(telemetry.estimated_total_tokens, expected)

    def test_serializable_snapshot(self):
        telemetry = mod.CouncilTokenTelemetry(
            agents_selected=2, skills_selected=3, memory_items_selected=4
        )
        snapshot = telemetry.as_dict()
        self.assertEqual(snapshot["agents_selected"], 2)
        self.assertEqual(snapshot["skills_selected"], 3)
        self.assertEqual(snapshot["memory_items_selected"], 4)
        self.assertIn("estimated_total_tokens", snapshot)
        self.assertIn("estimated_context_savings", snapshot)


if __name__ == "__main__":
    unittest.main()
