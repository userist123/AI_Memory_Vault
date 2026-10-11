#!/usr/bin/env python3
"""One-time install of the reviewed design stack on the owner's machine.

    python 30_SCRIPTS/setup/install_design_stack.py            # dry run: prints the plan
    python 30_SCRIPTS/setup/install_design_stack.py --apply    # does it, once
    python 30_SCRIPTS/setup/install_design_stack.py --apply --force   # again, ignoring the marker

Scope is exactly what 07_EVALUATION/reports/external_skill_intake_design_stack_2026-10-11.md
cleared: enable frontend-design and ralph-loop (already installed from the official
marketplace), confirm superpowers, add chrome-devtools-mcp pinned to 1.9.0 (the version the
official marketplace pins) with telemetry and CrUX off and an isolated browser profile, and copy
the owner's standing directives (creative-web-director, security-lockdown, graph-orchestrator)
and motion-director, plus 5 Replica skills (vendored at a pinned commit, triggers narrowed), to
~/.claude/skills, and installs the official Remotion plugin from its own marketplace.
impeccable and `npx skills` were rejected by that review and are not installed.
A marker file (~/.claude/design-stack.installed) makes a second --apply a no-op.
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
OWNER_SKILLS = ("creative-web-director", "security-lockdown", "graph-orchestrator", "motion-director")  # .claude/skills/<name>
REPLICA_SKILLS = ("replica-recon", "replica-architect", "replica-entrepreneur", "replica-build", "replica-brand")
REPLICA_GATE = ("Replica pack: use only when the owner explicitly asks to clone, rebuild or reverse-engineer "
                "an existing app or site (normally through graph-orchestrator). ")
MARKER_NAME = "design-stack.installed"
VERSION = "2026-10-11.3"  # bump when the stack changes: an older marker re-runs the install once
PLUGINS_INSTALL = (("remotion-dev/claude-code-plugin", "remotion@remotion"),)  # official Remotion plugin
PLUGINS_ENABLE = ("frontend-design@claude-plugins-official", "ralph-loop@claude-plugins-official",
                  "superpowers@claude-plugins-official")
MCP_NAME = "chrome-devtools"
MCP_PACKAGE = "chrome-devtools-mcp@1.9.0"
MCP_ARGS = ("--no-usage-statistics", "--no-performance-crux", "--isolated")
MCP_ENV = ("CHROME_DEVTOOLS_MCP_NO_USAGE_STATISTICS=1",)


def mcp_command(windows: bool) -> list[str]:
    launcher = ["cmd", "/c", "npx"] if windows else ["npx"]  # Windows needs cmd /c to start npx
    env = [x for e in MCP_ENV for x in ("-e", e)]
    # name before -e: `-e <env...>` is variadic and would swallow it
    return ["claude", "mcp", "add", "--scope", "user", MCP_NAME, *env, "--",
            *launcher, "-y", MCP_PACKAGE, *MCP_ARGS]


def plan(home: Path, windows: bool) -> list[tuple[str, list[str] | None]]:
    steps: list[tuple[str, list[str] | None]] = []
    for repo, plugin in PLUGINS_INSTALL:
        steps.append((f"add marketplace {repo}", ["claude", "plugin", "marketplace", "add", repo]))
        steps.append((f"install plugin {plugin}", ["claude", "plugin", "install", plugin]))
    for p in PLUGINS_ENABLE:
        steps.append((f"enable plugin {p}", ["claude", "plugin", "enable", p]))
    steps.append((f"remove an older {MCP_NAME} MCP entry if any", ["claude", "mcp", "remove", "--scope", "user", MCP_NAME]))
    steps.append((f"add MCP {MCP_NAME} ({MCP_PACKAGE}, {' '.join(MCP_ARGS)})", mcp_command(windows)))
    for name in (*OWNER_SKILLS, *REPLICA_SKILLS):
        steps.append((f"copy skill {name} to {home / '.claude' / 'skills'}", None))
    return steps


def skill_source(name: str) -> Path:
    if name in REPLICA_SKILLS:
        return REPO / ".agents" / "skills" / name  # vendored at a pinned commit, see PROVENANCE.json
    return REPO / ".claude" / "skills" / name


def gate_description(text: str) -> str:
    """Narrow a Replica skill's trigger: upstream descriptions also fire on ordinary questions."""
    head, sep, rest = text.partition("\ndescription:")
    if not sep:
        return text
    value, nl, tail = rest.partition("\n")
    if value.strip() in (">-", ">", "|", "|-"):  # block scalar: the gate becomes its first line
        indent = tail[:len(tail) - len(tail.lstrip(" "))] or "  "
        return f"{head}{sep}{value}{nl}{indent}{REPLICA_GATE}{tail.lstrip(' ')}"
    return f"{head}{sep} {REPLICA_GATE}{value.lstrip()}{nl}{tail}"


def copy_skill(name: str, home: Path) -> None:
    dst = home / ".claude" / "skills" / name
    if dst.is_symlink():  # `npx skills add` links skills; drop the link, never follow it
        dst.unlink()
    elif dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(skill_source(name), dst)
    if name in REPLICA_SKILLS:
        md = dst / "SKILL.md"
        md.write_text(gate_description(md.read_text(encoding="utf-8")), encoding="utf-8")


def run(cmd: list[str]) -> tuple[int, str]:
    exe = shutil.which(cmd[0])
    if exe is None:
        return 127, f"{cmd[0]} not found on PATH"
    r = subprocess.run([exe, *cmd[1:]], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=180)
    return r.returncode, (r.stdout + r.stderr).strip()


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--home", type=Path, default=Path.home())
    a = ap.parse_args(argv)
    for stream in (sys.stdout, sys.stderr):  # claude prints ✔/✘; a cp1252 console must not crash the run
        try:
            stream.reconfigure(errors="replace")
        except (AttributeError, ValueError):
            pass
    windows = os.name == "nt"
    marker = a.home / ".claude" / MARKER_NAME
    if a.apply and marker.exists() and not a.force:
        try:
            data = json.loads(marker.read_text(encoding="utf-8"))
            done = data.get("version") if isinstance(data, dict) else None
        except ValueError:  # a corrupt marker re-runs the install, which rewrites it
            done = None
        if done == VERSION:
            print(f"already installed (version {done}); nothing to do. --force to redo.")
            return 0
    steps = plan(a.home, windows)
    if not a.apply:
        for desc, cmd in steps:
            print(f"- {desc}" + (f"\n    {' '.join(cmd)}" if cmd else ""))
        print("\nDry run. Add --apply to do it.")
        return 0
    failed = []
    for desc, cmd in steps:
        if cmd is None:
            copy_skill(desc.split()[2], a.home)
            print(f"ok   {desc}")
            continue
        code, out = run(cmd)
        benign = cmd[1:3] == ["mcp", "remove"] or (code != 0 and "already" in out.lower())
        print(f"{'ok  ' if code == 0 or benign else 'FAIL'} {desc}" + (f"\n     {out[-400:]}" if out and code else ""))
        if code != 0 and not benign:
            failed.append(desc)
    if failed:
        print(f"\n{len(failed)} step(s) failed; marker not written, run again after fixing.", file=sys.stderr)
        return 1
    marker.parent.mkdir(parents=True, exist_ok=True)
    marker.write_text(json.dumps({"version": VERSION, "mcp": MCP_PACKAGE}) + "\n", encoding="utf-8")
    print(f"\nDone. Marker {marker}. Restart Claude Code to load the plugins, MCP server and skill.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
