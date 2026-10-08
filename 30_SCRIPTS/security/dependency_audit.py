"""Audit every committed npm lockfile and .NET project for known vulnerable dependencies.

Dependabot alerts on the default branch cover the whole tree, including imported snapshots.
This script gives the same picture from CI, per manifest, without installing, building or
running the projects (note: ``dotnet restore`` evaluates MSBuild for the imported ``.csproj``
files, and MSBuild evaluation can execute code through props/targets, so the .NET half is not
code-free; the CI job has read-only permissions and no secrets):

* npm: ``npm audit --package-lock-only`` (reads the lockfile, never runs install scripts);
* .NET: ``dotnet restore`` then ``dotnet list package --vulnerable --include-transitive``.

Exit 1 when any CRITICAL advisory remains (or ``--fail-on high``). Everything found is written
to a JSON report and a Markdown summary (``$GITHUB_STEP_SUMMARY`` when set).

    python 30_SCRIPTS/security/dependency_audit.py --npm --dotnet --report out.json
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SEVERITIES = ("critical", "high", "moderate", "low")
# dotnet reports "Critical/High/Moderate/Low"; npm reports the same words in lower case.
RANK = {s: i for i, s in enumerate(SEVERITIES)}
# Third-party skill bundles are provenance-locked (hashes in 07_EVALUATION/raw_external_skills_audit):
# their findings are reported, never fixed in place and never fail the job. The owner dismisses
# the alert or removes the example from the bundle.
REPORT_ONLY = (".agents/skills/",)


def tracked(patterns: list[str]) -> list[Path]:
    out = subprocess.run(["git", "ls-files", "-z", "--", *patterns], cwd=REPO, capture_output=True, check=True)
    return sorted(REPO / p for p in out.stdout.decode("utf-8").split("\0") if p and "node_modules/" not in p)


# ── npm ──────────────────────────────────────────────────────────────────────────────────
def parse_npm_audit(data: dict) -> list[dict]:
    """One finding per vulnerable package, with the direct dependency that fixes it."""
    findings = []
    for name, vuln in sorted((data.get("vulnerabilities") or {}).items()):
        fix = vuln.get("fixAvailable")
        if isinstance(fix, dict):
            fix = f"{fix.get('name')}@{fix.get('version')}" + (" (major)" if fix.get("isSemVerMajor") else "")
        advisories = sorted({v.get("url", "") for v in vuln.get("via", []) if isinstance(v, dict)} - {""})
        findings.append({"package": name, "severity": vuln.get("severity", "low"), "range": vuln.get("range", ""),
                         "direct": bool(vuln.get("isDirect")), "fix": fix, "advisories": advisories})
    return findings


def audit_npm(lockfile: Path) -> dict:
    proc = subprocess.run(["npm", "audit", "--package-lock-only", "--json"], cwd=lockfile.parent,
                          capture_output=True, text=True, timeout=300, shell=os.name == "nt")
    try:
        return {"findings": parse_npm_audit(json.loads(proc.stdout or "{}"))}
    except json.JSONDecodeError:
        return {"error": (proc.stderr or proc.stdout).strip()[-400:]}


# ── .NET ─────────────────────────────────────────────────────────────────────────────────
def parse_dotnet_vulnerable(data: dict) -> list[dict]:
    """Findings from ``dotnet list package --vulnerable --format json`` (top-level and transitive)."""
    findings = {}
    for project in data.get("projects", []):
        for framework in project.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for pkg in framework.get(kind, []):
                    for vuln in pkg.get("vulnerabilities", []):
                        key = (pkg["id"], pkg.get("resolvedVersion"), vuln.get("advisoryurl"))
                        findings[key] = {"package": f"{pkg['id']}@{pkg.get('resolvedVersion')}",
                                         "severity": vuln.get("severity", "Low").lower(),
                                         "direct": kind == "topLevelPackages", "fix": None,
                                         "advisories": [vuln.get("advisoryurl", "")]}
    return [findings[k] for k in sorted(findings, key=lambda k: tuple(str(x) for x in k))]


def audit_dotnet(project: Path) -> dict:
    restore = subprocess.run(["dotnet", "restore", str(project), "-p:EnableWindowsTargeting=true"],
                             capture_output=True, text=True, timeout=900)
    if restore.returncode:
        return {"error": "restore failed: " + (restore.stdout + restore.stderr).strip()[-400:]}
    proc = subprocess.run(["dotnet", "list", str(project), "package", "--vulnerable", "--include-transitive",
                           "--format", "json"], capture_output=True, text=True, timeout=300)
    try:
        return {"findings": parse_dotnet_vulnerable(json.loads(proc.stdout))}
    except json.JSONDecodeError:
        return {"error": (proc.stderr or proc.stdout).strip()[-400:]}


# ── report ───────────────────────────────────────────────────────────────────────────────
def counts(findings: list[dict]) -> dict:
    c = dict.fromkeys(SEVERITIES, 0)
    for f in findings:
        c[f["severity"] if f["severity"] in c else "low"] += 1
    return c


def summarize(results: dict[str, dict], fail_on: str) -> tuple[str, bool]:
    lines = ["| manifest | critical | high | moderate | low | note |", "|---|---:|---:|---:|---:|---|"]
    failed = False
    for manifest, res in results.items():
        if "error" in res:
            lines.append(f"| `{manifest}` | | | | | not audited: {res['error'][:120].replace('|', '/')} |")
            continue
        c = counts(res["findings"])
        worst = [f for f in res["findings"] if RANK.get(f["severity"], 3) <= RANK[fail_on]]
        report_only = manifest.startswith(REPORT_ONLY)
        failed |= bool(worst) and not report_only
        note = ", ".join(sorted({f["package"] for f in worst}))[:200]
        if report_only and worst:
            note = "third-party skill bundle, report only: " + note
        lines.append(f"| `{manifest}` | {c['critical']} | {c['high']} | {c['moderate']} | {c['low']} | {note} |")
    return "\n".join(lines) + "\n", failed


def annotations(results: dict[str, dict]) -> str:
    """Workflow commands: one totals notice, then one warning per manifest at HIGH+ or not audited.

    Annotations are readable through the checks API, so the result is visible without job logs.
    """
    audited = [r for r in results.values() if "error" not in r]
    total = counts([f for r in audited for f in r["findings"]])
    lines = [f"::notice title=Dependency audit::audited {len(audited)}/{len(results)} manifests; "
             + ", ".join(f"{k} {v}" for k, v in total.items())]
    for manifest, res in results.items():
        if "error" in res:
            lines.append(f"::warning file={manifest},title=not audited::{res['error'][:300]}")
            continue
        c = counts(res["findings"])
        if c["critical"] or c["high"]:
            pkgs = ", ".join(sorted({f["package"] for f in res["findings"] if f["severity"] in ("critical", "high")}))
            lines.append(f"::warning file={manifest},title=critical {c['critical']} high {c['high']}::{pkgs[:300]}")
    return "\n".join(line.replace("\r", " ").replace("\n", " ") for line in lines)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--npm", action="store_true")
    ap.add_argument("--dotnet", action="store_true")
    ap.add_argument("--fail-on", choices=("critical", "high"), default="critical")
    ap.add_argument("--report", type=Path)
    args = ap.parse_args(argv)
    results: dict[str, dict] = {}
    if args.npm:
        if not shutil.which("npm"):
            print("npm not found", file=sys.stderr)
            return 2
        for lock in tracked(["*package-lock.json"]):
            results[lock.relative_to(REPO).as_posix()] = audit_npm(lock)
    if args.dotnet:
        if not shutil.which("dotnet"):
            print("dotnet not found", file=sys.stderr)
            return 2
        for proj in tracked(["*.csproj"]):
            results[proj.relative_to(REPO).as_posix()] = audit_dotnet(proj)
    table, failed = summarize(results, args.fail_on)
    print(table)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as fh:
            fh.write("## Dependency audit\n\n" + table)
    if os.environ.get("GITHUB_ACTIONS"):
        print(annotations(results))
    if args.report:
        args.report.write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding="utf-8")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
