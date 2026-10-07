"""dependency_audit.py: parsing of npm/dotnet audit output and the fail threshold (no network)."""
from __future__ import annotations

import importlib.util
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("dependency_audit", REPO / "30_SCRIPTS/security/dependency_audit.py")
da = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(da)

NPM = {"vulnerabilities": {
    "seroval": {"severity": "critical", "isDirect": False, "range": "<=1.6.2", "fixAvailable": True,
                "via": [{"url": "https://github.com/advisories/GHSA-x"}, "solid-js"]},
    "nodemon": {"severity": "high", "isDirect": True, "range": ">=2", "via": ["semver"],
                "fixAvailable": {"name": "nodemon", "version": "1.14.10", "isSemVerMajor": True}},
    "esbuild": {"severity": "low", "isDirect": False, "range": "0.27.3", "fixAvailable": True, "via": []},
}}

DOTNET = {"projects": [{"path": "a.csproj", "frameworks": [{"framework": "net8.0",
    "topLevelPackages": [{"id": "Pkg.A", "resolvedVersion": "1.0.0",
                          "vulnerabilities": [{"severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-a"}]}],
    "transitivePackages": [{"id": "Pkg.B", "resolvedVersion": "2.0.0",
                            "vulnerabilities": [{"severity": "Critical", "advisoryurl": "https://github.com/advisories/GHSA-b"}]}],
}, {"framework": "net8.0-windows", "topLevelPackages": [{"id": "Pkg.A", "resolvedVersion": "1.0.0",
    "vulnerabilities": [{"severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-a"}]}]}]}]}


def test_npm_findings_keep_severity_fix_and_advisories():
    f = {x["package"]: x for x in da.parse_npm_audit(NPM)}
    assert f["seroval"]["severity"] == "critical" and f["seroval"]["advisories"] == ["https://github.com/advisories/GHSA-x"]
    assert f["nodemon"]["fix"] == "nodemon@1.14.10 (major)" and f["nodemon"]["direct"]
    assert da.counts(list(f.values())) == {"critical": 1, "high": 1, "moderate": 0, "low": 1}


def test_dotnet_findings_are_deduplicated_across_frameworks():
    f = da.parse_dotnet_vulnerable(DOTNET)
    assert sorted((x["package"], x["severity"], x["direct"]) for x in f) == [
        ("Pkg.A@1.0.0", "high", True), ("Pkg.B@2.0.0", "critical", False)]


def test_threshold_fails_on_critical_by_default_and_on_high_when_asked():
    only_high = {"x/package-lock.json": {"findings": da.parse_npm_audit({"vulnerabilities": {k: NPM["vulnerabilities"][k] for k in ("nodemon",)}})}}
    assert da.summarize(only_high, "critical")[1] is False
    assert da.summarize(only_high, "high")[1] is True
    with_critical = {"y/package-lock.json": {"findings": da.parse_npm_audit(NPM)}}
    table, failed = da.summarize(with_critical, "critical")
    assert failed and "| `y/package-lock.json` | 1 | 1 | 0 | 1 | seroval |" in table


def test_third_party_skill_bundles_are_reported_but_do_not_fail():
    res = {".agents/skills/x/examples/app/package-lock.json": {"findings": da.parse_npm_audit(NPM)}}
    table, failed = da.summarize(res, "critical")
    assert not failed and "third-party skill bundle, report only: seroval" in table


def test_an_unaudited_manifest_is_reported_not_silently_passed():
    table, failed = da.summarize({"z.csproj": {"error": "restore failed: NU1101 | x"}}, "critical")
    assert not failed and "not audited: restore failed: NU1101 / x" in table


def test_the_workflow_never_installs_builds_or_runs_projects():
    wf = (REPO / ".github/workflows/dependency-audit.yml").read_text(encoding="utf-8")
    for forbidden in ("npm install", "npm ci", "npm run", "dotnet build", "dotnet test", "dotnet run"):
        assert forbidden not in wf
    src = (REPO / "30_SCRIPTS/security/dependency_audit.py").read_text(encoding="utf-8")
    assert '"--package-lock-only"' in src and '"npm", "install"' not in src


def test_annotations_carry_totals_and_unaudited_manifests_on_single_lines():
    out = da.annotations({"a/package-lock.json": {"findings": da.parse_npm_audit(NPM)},
                          "b.csproj": {"error": "restore failed:\nNU1101"}})
    lines = out.splitlines()
    assert lines[0] == "::notice title=Dependency audit::audited 1/2 manifests; critical 1, high 1, moderate 0, low 1"
    assert "::warning file=a/package-lock.json,title=critical 1 high 1::nodemon, seroval" in lines
    assert "::warning file=b.csproj,title=not audited::restore failed: NU1101" in lines
