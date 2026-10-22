# todo-claude-casa3d-p0
STATUS: DONE   UPDATED: 2026-10-22T21:00:00+03:00
TASK: Casa3D P0 „Harden today's app” (SALES_READINESS_PLAN.md §3.1 items 1, 3, 4; §4 phase P0) — slice 1
BRANCH / PR: claude/casa3d-p0-hardening (worktree D:/w/p0harden, base origin/main f0ee40d6)
DONE:
- 3.1.1 Host header: local trust is opt-in (CASA3D_LOCAL_TRUST=1, never on VERCEL), set only by
  scripts/run-local.mjs (npm start / npm run dev, forced -H 127.0.0.1, every -H/--hostname form refused).
  `npm run start:hosted` = no local trust. Runtime-verified: hosted instance + `Host: localhost` → 401/404/login.
- 3.1.3 Headers: per-request nonce CSP + 'strict-dynamic' on pages; inert CSP (default-src 'none'; sandbox) on /api;
  nosniff, X-Frame-Options DENY, frame-ancestors 'none', Permissions-Policy, COOP; HSTS only on https;
  no-referrer on /share and /go. Root layout awaits headers() (all pages dynamic → nonce applied).
  Google Fonts replaced by @fontsource (OFL-1.1), same family names. Browser-verified on 12 pages, 0 CSP errors.
- 3.1.4 Share links: expires_at (default 90 days, 0 = none, 1..365), shareActive() predicate on every public
  query; last_access_at (hour-rounded, no IP); old links keep NULL = no expiry; Editor shows expiry/last open.
- Tests: 666/666 (+15), tsc 0, build 0; twin-core 59/59.
- Review round 1 (vault-reviewer): REQUEST_CHANGES F1 (-H0.0.0.0 bypass), F2 (middleware headers override route
  headers in Next 15: send-response only sets missing keys), F3 (shareDays coercion) → fixed in 31da8ca5.
- Review round 2 on delta 31da8ca5: APPROVE (no new findings).
- CI round 1: npm lockfiles (next critical GHSA-2464-fhr4-w9xw) and test_notes_strict_yaml (checkpoint not on
  allowlist) → fixed in c0cd6abd5 (next 15.5.28, postcss override, allowlist entry).
- 3.1.4 share probe list + 3.1.6 security-lockdown: 3 independent auditors (A secrets/history/logging, B auth/IDOR/share,
  C input/XSS/SQL/errors); gitleaks 8.30.1 history 144 commits clean; 17 findings fixed (app/SECURITY_AUDIT.md),
  incl. db.ts deleting the owner's data dir on open failure, CSRF on 34 routes, demo AI on owner key, body limits.
  Tests 680/680, tsc 0, build 0; runtime: bad env → exit 1, CSRF 403/201, xff → 401, 600 KB → 413, UI writes OK.
- Review of audit-fix delta: APPROVE (L1-L3 low; L2 scrypt slots + rehash and stair bounds fixed in 2dd8aeab).
- Visual verification with 20 screenshots (desktop + 390 px mobile): fixed plan slide overflow (plan cut off), moodboard
  and room slides overflow (9/9 slides now fit 16:9), deck-bar under decide-bar, PIN form layout/tone (4ccf9812).
- CI on 4ccf9812: 40 pass, 1 fail = "npm lockfiles" caused only by 02_PRODUCT/projects/imported/nu-sterge/
  trading-journal-COMPLET-v2 (next critical, new advisory 2026-10-22; not Casa3D, main will fail it too). Casa3D 0/0.
- Merged PR #316 (squash), owner authorised merge when Casa3D is green.
NEXT:
1. UI/UX research for Casa3D (owner request 2026-10-22): todo-claude-casa3d-uiux.md.
2. Owner: decide on the trading-journal next upgrade (other project) to clear the repo-wide npm audit job.
3. Notes for UI/UX: room names stay English if the project was created with EN UI; proforma shows raw hex colour names.
OWNER DECISIONS / RESIDUALS:
- Existing share links (created before this change) never expire; owner may revoke old ones in Presentation.
- A local reverse proxy that rewrites Host to localhost without cf-connecting-ip still gets local trust (pre-existing).
- Share tokens are still stored in clear text as primary key (hashing needs a FK migration) — candidate for 3.1.4.
- Dev mode (HMR) under CSP not runtime-verified.
- A future /api route serving PDF/HTML inline must be exempted in policyFor() (sandbox blocks the PDF viewer).
KEY FILES: casa3d/app/{lib/public-gate.ts, scripts/run-local.mjs, lib/security-headers.ts, middleware.ts,
app/layout.tsx, lib/share.ts, tests/{local-trust,security-headers,share-expiry}.test.ts}
