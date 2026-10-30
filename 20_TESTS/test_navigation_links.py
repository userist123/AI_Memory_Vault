"""The navigation-link gate: every path an agent is told to follow in the navigation files exists.

The first test runs the gate on the real repository; the others pin its rules on small synthetic inputs, so a
change that makes it blind (or noisy) fails here first.
"""
import importlib.util
import json
import subprocess
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("navigation_links",
                                               REPO / "30_SCRIPTS" / "verification" / "navigation_links.py")
nl = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(nl)

REASON = "a reason long enough to pass"


def test_the_real_navigation_files_have_no_unlisted_dead_path():
    broken = nl.find_broken(REPO, nl.tracked_files(REPO))
    allowlist = json.loads(nl.ALLOWLIST.read_text(encoding="utf-8"))
    problems, stale = nl.apply_allowlist(broken, allowlist)
    assert problems == [], "\n".join(problems)
    assert stale == [], "\n".join(stale)


def _git_repo(tmp_path, gitignore=""):
    subprocess.run(["git", "init", "-q"], cwd=tmp_path, check=True)
    (tmp_path / ".gitignore").write_text(gitignore, encoding="utf-8")
    return tmp_path


def _broken(tmp_path, docs, extra_files=(), gitignore=""):
    repo = _git_repo(tmp_path, gitignore)
    files = sorted(set(docs) | set(extra_files))
    return [(b["file"], b["kind"], b["target"]) for b in
            nl.find_broken(repo, files, read=lambda rel: docs.get(rel, ""))]


def test_each_kind_of_dead_reference_is_found(tmp_path):
    docs = {"AGENTS.md": "See [the rules](00_GOVERNANCE/protocols/Gone.md), [[Missing Note]] and "
                         "`00_GOVERNANCE/protocols/Also_Gone.md`.\n"}
    assert _broken(tmp_path, docs) == [
        ("AGENTS.md", "tick", "00_GOVERNANCE/protocols/Also_Gone.md"),
        ("AGENTS.md", "md", "00_GOVERNANCE/protocols/Gone.md"),
        ("AGENTS.md", "wiki", "Missing Note"),
    ]


def test_live_references_pass(tmp_path):
    docs = {"00_GOVERNANCE/protocols/A.md": "[up](../../AGENTS.md#rules) [[Memory_Protocol]] "
                                            "[[Memory_Protocol|Memory Protocol]] `02_PRODUCT/projects/` "
                                            "`00_GOVERNANCE/protocols/A.md`\n",
            "AGENTS.md": ""}
    extra = ["00_GOVERNANCE/protocols/Memory_Protocol.md", "02_PRODUCT/projects/x/README.md"]
    assert _broken(tmp_path, docs, extra) == []


def test_wikilinks_resolve_like_the_vault_resolver(tmp_path):
    # retrieval/vault_index.VaultIndex.resolve: title (frontmatter or first heading), then file name with
    # `_`/`-` read as spaces. Obsidian itself would not resolve the first two; agents go through the vault.
    docs = {"AGENTS.md": "[[Memory Protocol]] [[no-fabrication policy]] [[Canonical Frontmatter Spec]]\n",
            "x/Canon.md": "---\ntitle: Canonical Frontmatter Spec\n---\nbody\n",
            "00_GOVERNANCE/protocols/Memory_Protocol.md": "", "y/No-Fabrication_Policy.md": ""}
    assert _broken(tmp_path, docs) == []
    docs["AGENTS.md"] = "[[Memory Protocols]]\n"
    assert _broken(tmp_path, docs) == [("AGENTS.md", "wiki", "Memory Protocols")]


def test_note_title_matches_the_vault_index_rule():
    assert nl.note_title("---\nid: x\ntitle: \"The Title\"\n---\n# Heading\n") == "The Title"
    assert nl.note_title("---\nid: x\n---\n\n# Heading Here\ntext") == "Heading Here"
    assert nl.note_title("no heading") is None


def test_code_fences_inline_code_placeholders_and_external_links_are_skipped(tmp_path):
    docs = {"CLAUDE.md": "```\n[x](00_GOVERNANCE/gone.md) `00_GOVERNANCE/gone.md` [[gone]]\n```\n"
                         "`[x](gone.md)` [a](https://example.org/x) [b](mailto:a@b.c) [c](#anchor)\n"
                         "[d](../../actions) `00_GOVERNANCE/<name>.md` `07_EVALUATION/reports/YYYY/x.md`\n"
                         "[[<note>]] `.claude/*` not-a-path `foo/bar.md`\n"}
    assert _broken(tmp_path, docs) == []


def test_files_outside_the_navigation_scope_are_not_checked(tmp_path):
    docs = {"07_EVALUATION/reports/r.md": "[x](gone.md)\n", ".agents/skills/s/SKILL.md": "`.github/gone.md`\n"}
    assert _broken(tmp_path, docs) == []


def test_paths_gitignore_keeps_local_on_purpose_pass(tmp_path):
    docs = {"README.md": "`06_INBOX/RAW_IMPORTS/` `04_MEMORY/` `04_MEMORY` `.claude/settings.local.json`\n"}
    gitignore = "/06_INBOX/*\n/04_MEMORY/\n.claude/settings.local.json\n"
    assert _broken(tmp_path, docs, gitignore=gitignore) == []


def test_the_allowlist_needs_a_reason_and_goes_stale():
    broken = [{"file": "AGENTS.md", "line": 3, "kind": "tick", "target": "99_SYSTEM/"},
              {"file": "AGENTS.md", "line": 4, "kind": "tick", "target": "00_X/"}]
    allow = {"entries": [{"file": "AGENTS.md", "target": "99_SYSTEM/", "reason": REASON},
                         {"file": "AGENTS.md", "target": "00_X/", "reason": "short"},
                         {"file": "CLAUDE.md", "target": "gone/", "reason": REASON}]}
    problems, stale = nl.apply_allowlist(broken, allow)
    assert problems == ["ALLOWLIST_NO_REASON:AGENTS.md:00_X/"]
    assert stale == ["ALLOWLIST_STALE:CLAUDE.md:gone/"]
    # an unlisted dead link is a problem
    problems, _ = nl.apply_allowlist(broken, {"entries": []})
    assert problems == ["BROKEN_LINK:AGENTS.md:3:tick:99_SYSTEM/", "BROKEN_LINK:AGENTS.md:4:tick:00_X/"]
