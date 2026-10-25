# todo-claude-security-alerts

- Agent: claude (main session), started 2026-10-25
- Branch: claude/dependabot-code-scanning-issues-ecb0c5 (from origin/main 3bdb3c9fd)
- Task: resolve every open GitHub security alert on userist123/AI_Memory_Vault
  (owner choice 2026-10-25: close third-party secrets; fix Dependabot and CodeQL in code, majors included)

## Done
- Secret scanning #1-#15 resolved on GitHub: all were third-party examples/placeholders/public keys
  in vendored skills from commit a09e298cf3, files removed from main in 2dc9b2be7
  (#1, #9 used_in_tests; the rest false_positive).

## Next
1. CodeQL in code: #556 hud.js XSS, #554 voice_server.py ReDoS, #32 audit_checks.py logging,
   #562/#563 TradingBot password KDF, #592/#593 SRI, #577-#586 + #35 + #551-#553 hostname checks, #555 mock sanitizer.
2. Dependabot: bumps + lockfile regen in imported apps (site/frontend, site/backend, trading-journal x3,
   jarvis-trader-ui, rizz-coach, .agents/skills beautiful-article, loki-mode example); xlsx via the SheetJS CDN tarball.
3. 28 alerts on RAW_IMPORTS uv.lock files that no longer exist: dismiss as inaccurate after the PR.
4. vault-reviewer pass, PR, CI once.

## Blockers
- none

## Key files
- see the alert lists in the PR description
