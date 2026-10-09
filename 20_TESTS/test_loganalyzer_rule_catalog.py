"""Every finding rule id emitted by LogAnalyzer's C# code has an entry in the finding-contract catalog (stage 2, WP2).

A rule that is not in `RuleContracts.cs` would get no limitations, missing evidence or next steps; this keeps the catalog
in step with the producers (Correlation, LiveStateAnalyzer, ProcessContainmentService, PolicyTimeline)."""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "02_PRODUCT/projects/workspaces/loganalyzer-dfir"
PRODUCERS = [
    ROOT / "LogAnalyzer.Dfir.Core/Analysis/Correlation.cs",
    ROOT / "LogAnalyzer.Dfir.Windows/Investigation/InvestigationPipeline.cs",
    ROOT / "LogAnalyzer.Dfir.Core/Analysis/PolicyTimeline.cs",
    *sorted((ROOT / "LogAnalyzer.Dfir.Core/Analysis").glob("Wp11Rules*.cs")),
    *sorted((ROOT / "LogAnalyzer.Dfir.Core/Analysis").glob("Wp14Rules*.cs")),
    *sorted((ROOT / "LogAnalyzer.Dfir.Core/Analysis").glob("SequenceRules*.cs")),
    ROOT / "LogAnalyzer.Response/Containment/ProcessContainmentService.cs",
]
CATALOG = ROOT / "LogAnalyzer.Dfir.Core/Analysis/RuleContracts.cs"
ID = re.compile(r'"([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)"')
# WP14a builds its air-gap findings through one shared helper, Net(c, "<RULE-ID>", ...), so the id is an argument and not on a `RuleId = ` line.
NET_CALL = re.compile(r'\bNet\(c, "([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)"')
# WP14b builds its sequence findings through one shared helper, Build(c, "<RULE-ID>", ...), so the id is an argument as well.
BUILD_CALL = re.compile(r'\bBuild\(c, "([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)"')


def emitted_rule_ids():
    ids = set()
    for p in PRODUCERS:
        for line in p.read_text(encoding="utf-8").splitlines():
            if "RuleId = " in line:
                ids.update(ID.findall(line.split("RuleId = ", 1)[1]))
            if p.name.startswith("Wp14Rules"):
                ids.update(NET_CALL.findall(line))
            if p.name.startswith("SequenceRules"):
                ids.update(BUILD_CALL.findall(line))
    return ids


def catalog_rule_ids():
    return set(re.findall(r'\b(?:R|RF)\("([A-Z0-9-]+)"', CATALOG.read_text(encoding="utf-8")))


def test_every_emitted_rule_has_a_contract_entry():
    emitted, cataloged = emitted_rule_ids(), catalog_rule_ids()
    assert len(emitted) >= 25, emitted
    assert not (emitted - cataloged), f"rules without a contract entry: {sorted(emitted - cataloged)}"


def test_catalog_has_no_dead_entries():
    emitted, cataloged = emitted_rule_ids(), catalog_rule_ids()
    assert not (cataloged - emitted), f"catalog entries no producer emits: {sorted(cataloged - emitted)}"


def test_sequence_rules_have_a_production_consumer():
    """Global Production-Consumer Rule: the WP14b sequence rules are called by the investigation pipeline, not only by tests."""
    pipeline = (ROOT / "LogAnalyzer.Dfir.Windows/Investigation/InvestigationPipeline.cs").read_text(encoding="utf-8")
    assert "SequenceRules.Run(" in pipeline
