# todo-claude-casa3d-p0
STATUS: PR_OPEN   UPDATED: 2026-10-22T21:00:00+03:00
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
NEXT (in order):
1. Draft PR pushed; main session checks CI once; owner merges.
2. Remaining P0: 3.1.4 independent review of every /api/share/* route (probe list in plan); 3.1.5 DB-backed
   rate limits; 3.1.6 security-lockdown audit + SECURITY_AUDIT.md + GitHub alert triage; 3.1.7/3.1.8 ops.
OWNER DECISIONS / RESIDUALS:
- Existing share links (created before this change) never expire; owner may revoke old ones in Presentation.
- A local reverse proxy that rewrites Host to localhost without cf-connecting-ip still gets local trust (pre-existing).
- Share tokens are still stored in clear text as primary key (hashing needs a FK migration) — candidate for 3.1.4.
- Dev mode (HMR) under CSP not runtime-verified.
- A future /api route serving PDF/HTML inline must be exempted in policyFor() (sandbox blocks the PDF viewer).
KEY FILES: casa3d/app/{lib/public-gate.ts, scripts/run-local.mjs, lib/security-headers.ts, middleware.ts,
app/layout.tsx, lib/share.ts, tests/{local-trust,security-headers,share-expiry}.test.ts}
