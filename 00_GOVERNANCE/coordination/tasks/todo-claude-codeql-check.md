# todo-claude-codeql-check
STATUS: IN_PROGRESS        UPDATED: 2026-10-10T10:40Z
TASK: Make the PR check "Code scanning results / CodeQL" end pass/fail instead of "neutral".
BRANCH / PR: claude/blissful-cannon-6w2cnz / #259 merged (23041ac9); follow-up PR for the cleanup loop    BASE: main @ 23041ac9
SPEC: check-run text on PR #256 / #254: "6 configurations present on refs/heads/main were not found":
  /language:c-cpp, go, javascript-typescript, ruby, rust (codeql.yml) and /language:csharp (codeql-csharp.yml).
  GitHub compares a PR's analyses with the configurations present on main; any main-only configuration -> neutral.
DONE:
- codeql.yml: javascript-typescript added to the matrix (jarvis_web is real JS; imported/untrusted material and
  skill templates excluded via .github/codeql/javascript-config.yml). Removes one of the five stale categories
  on the next push to main.
- codeql-csharp.yml: path filters removed, so /language:csharp is analyzed on every PR (~7 min, inside the
  ~19 min critical path of the Python jobs).
NEXT (in order):
1. Merge to main (main must upload actions, python, javascript-typescript, csharp once).
2. After merge: run workflow "CodeQL stale configuration cleanup" (codeql-config-cleanup.yml) in dry run,
   then with apply=true (owner approved deleting c-cpp/go/ruby/rust on 2026-10-10). UI fallback: Security and
   quality -> Code scanning -> Tool status -> CodeQL -> each category -> "..." -> Delete configuration.
   (These languages have no code in the repository: 0 Ruby/Rust files, 4 Go and 1 C++ files are skill examples.)
3. Push any change to an open PR and confirm the CodeQL check is green (or red with real alerts), not neutral.
BLOCKERS / OWNER QUESTIONS:
- Merge of PR #259 is the owner's (security-sensitive CI change). Cleanup itself runs from the workflow.
KEY FILES:
- .github/workflows/codeql.yml, .github/workflows/codeql-csharp.yml, .github/codeql/javascript-config.yml
VERIFICATION SO FAR: #259 CI green; CodeQL check 6 -> 4 missing. Cleanup run 38044442962 (apply) deleted only the
  newest analysis per category: the chain stopped (confirm_delete_url null). Analyses sit in many sets. Fix: delete
  every deletable analysis per round, re-list, fail if any remain (simulated locally). Not yet re-run.
  Main also holds stale /language:java-kotlin and /language:swift (not named by the PR check; not approved, untouched).
