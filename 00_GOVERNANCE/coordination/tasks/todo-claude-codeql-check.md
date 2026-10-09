# todo-claude-codeql-check
STATUS: IN_PROGRESS        UPDATED: 2026-10-10T00:10Z
TASK: Make the PR check "Code scanning results / CodeQL" end pass/fail instead of "neutral".
BRANCH / PR: claude/blissful-cannon-6w2cnz / none yet    BASE: main @ 21da5bbf
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
2. OWNER, in the GitHub UI (no API access from the agent session: 403 on code-scanning endpoints):
   Security and quality -> Code scanning -> Tool status -> CodeQL -> select each of
   /language:c-cpp, /language:go, /language:ruby, /language:rust -> "..." -> Delete configuration.
   (These languages have no code in the repository: 0 Ruby/Rust files, 4 Go and 1 C++ files are skill examples.)
3. Push any change to an open PR and confirm the CodeQL check is green (or red with real alerts), not neutral.
BLOCKERS / OWNER QUESTIONS:
- Step 2 needs repository admin / security-events write; cannot be done from this session.
KEY FILES:
- .github/workflows/codeql.yml, .github/workflows/codeql-csharp.yml, .github/codeql/javascript-config.yml
VERIFICATION SO FAR: YAML parses; workflow_security_audit + related regression tests (see commit).
