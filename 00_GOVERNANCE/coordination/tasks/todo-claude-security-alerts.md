# todo-claude-security-alerts

- Agent: claude (main session), started 2026-10-25
- Branch: claude/dependabot-code-scanning-issues-ecb0c5 (from origin/main 3bdb3c9fd)
- Task: resolve every open GitHub security alert on userist123/AI_Memory_Vault
  (owner choice 2026-10-25: close third-party secrets; fix Dependabot and CodeQL in code, majors included)

## Done
- Secret scanning #1-#15 resolved on GitHub: all were third-party examples/placeholders/public keys
  in vendored skills from commit a09e298cf3, files removed from main in 2dc9b2be7
  (#1, #9 used_in_tests; the rest false_positive).
- CodeQL fixes (f9ef99def): hud.js XSS, voice_server.py ReDoS, auditor secret logging,
  TradingBot salted PBKDF2 with legacy migration, SRI on the GSAP CDN scripts, hostname checks, mock sanitizer.
- Dependabot fixes (b31702405): bumps, overrides and lockfile regen in the imported apps and skill templates;
  0 alerts left in a vulnerable range per the reviewer's mapping.
- Review round 1 (vault-reviewer): no blockers. Fixed after it: speech input bound raised so text after a
  long code block is still spoken (+ regression test); KDF iteration count bounded when read; failed
  legacy migration is logged.

## Next
1. Delta review, PR, CI once.
2. After merge: dismiss the 28 Dependabot alerts on the removed 06_INBOX/RAW_IMPORTS uv.lock files as inaccurate,
   and confirm the remaining alerts closed.

## Residuals (owner informed)
- bot/trading-journal-COMPLET-v2 and nu-sterge/trading1 did not `next build` before this change either
  (duplicate /pricing route, typed-route errors); no lockfile in either.
- site/frontend forces webpack-dev-server 6, svgo 2 and postcss-selector-parser 7 past what CRA declares;
  the build was checked by the worker only.

## Blockers
- none
