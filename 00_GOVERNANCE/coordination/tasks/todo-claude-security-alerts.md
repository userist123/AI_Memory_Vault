# todo-claude-security-alerts
STATUS: IN_PROGRESS        UPDATED: 2026-10-25T19:00Z
TASK: Resolve every open GitHub security alert (Dependabot, code scanning, secret scanning) on userist123/AI_Memory_Vault.
BRANCH / PR: claude/dependabot-code-scanning-issues-ecb0c5 / none yet    BASE: main @ 3bdb3c9fd
SPEC: owner choice 2026-10-25 - close third-party secrets; fix Dependabot and CodeQL in code, majors included.
DONE:
- Secret scanning #1-#15 resolved on GitHub: third-party examples, placeholders and public keys in vendored
  skills from a09e298cf3, files removed from main in 2dc9b2be7 (#1, #9 used_in_tests; the rest false_positive).
- CodeQL fixes: hud.js XSS, voice_server.py ReDoS, auditor secret logging, TradingBot salted PBKDF2 with
  legacy migration, SRI on the GSAP CDN scripts, hostname checks, mock sanitizer.
- Dependabot fixes: bumps, overrides and lockfile regen in the imported apps and skill templates;
  0 alerts left in a vulnerable range per the reviewer's mapping.
- Review round 1: no blockers; speech-after-code-block, KDF iteration bound and migration logging fixed.
  Round 2 (delta): verified; tests for the bound and the warning added.
NEXT (in order):
1. Push, PR, CI once.
2. After merge: dismiss the 28 Dependabot alerts on the removed 06_INBOX/RAW_IMPORTS uv.lock files as
   inaccurate, and confirm the remaining alerts closed.
BLOCKERS / OWNER QUESTIONS:
- none. Residuals: bot/trading-journal-COMPLET-v2 and nu-sterge/trading1 did not `next build` before this
  change either (duplicate /pricing route); site/frontend forces webpack-dev-server 6, svgo 2 and
  postcss-selector-parser 7 past what CRA declares (build checked by the worker only).
KEY FILES:
- 02_PRODUCT/projects/workspaces/jarvis_web/voice_server.py
- 02_PRODUCT/projects/imported/{tradingbot/TradingBot,bot/TradingBot/TradingBot}/core/security.py
- 02_PRODUCT/projects/imported/site/frontend/{package.json,craco.config.js}
VERIFICATION SO FAR: new tests 7 + 8 + 8 pass; jarvis_web npm test 234 pass; full suite 5028 passed, 5 failed:
4 b2m/book_to_memory tests fail only from ignored local files (pass on a clean checkout of the branch, 58/58); 1 was this checkpoint, now allowlisted.
