"""Every finding rule id emitted by LogAnalyzer's C# code has an entry in the finding-contract catalog (stage 2, WP2).

A rule that is not in `RuleContracts.cs` would get no limitations, missing evidence or next steps; this keeps the catalog
in step with the producers (Correlation, LiveStateAnalyzer, ProcessContainmentService)."""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "02_PRODUCT/projects/workspaces/loganalyzer-dfir"
PRODUCERS = [
    ROOT / "LogAnalyzer.Dfir.Core/Analysis/Correlation.cs",
    ROOT / "LogAnalyzer.Dfir.Windows/Investigation/InvestigationPipeline.cs",
    ROOT / "LogAnalyzer.Response/Containment/ProcessContainmentService.cs",
]
CATALOG = ROOT / "LogAnalyzer.Dfir.Core/Analysis/RuleContracts.cs"
ID = re.compile(r'"([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)"')


def emitted_rule_ids():
    ids = set()
    for p in PRODUCERS:
        for line in p.read_text(encoding="utf-8").splitlines():
            if "RuleId = " in line:
                ids.update(ID.findall(line.split("RuleId = ", 1)[1]))
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
