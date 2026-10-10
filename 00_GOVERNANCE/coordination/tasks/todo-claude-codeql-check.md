# todo-claude-codeql-check
STATUS: DONE        UPDATED: 2026-10-10T12:00Z
TASK: Make the PR check "Code scanning results / CodeQL" end pass/fail instead of "neutral".
BRANCH / PR: claude/blissful-cannon-6w2cnz / #259 and #261 merged    BASE: main @ 6ce6633f
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
VERIFICATION SO FAR: cleanup runs 38047621817 and 38048531016 stopped on transient HTTP 503 from code scanning;
  run 38049583815 finished: c-cpp, go, ruby, rust removed from refs/heads/main. PR #262: "CodeQL" check = success
  ("No new alerts in code changed by this pull request"). The check is computed when the PR's first analysis lands,
  so it can show python/csharp as missing for a few minutes until those analyses finish; that is not stale config.
  Not touched: stale /language:java-kotlin and /language:swift on main (PR check does not name them; not approved).
