# Security audit - Casa3D (casa-mea-3d) - 2026-10-22
Scope: `02_PRODUCT/projects/workspaces/casa3d` (app: Next.js 15.5.28, React 19, PGlite/Postgres; packages/twin-core), branch `claude/casa3d-p0-hardening`, base `origin/main` f0ee40d6. Phase P0 of `../SALES_READINESS_PLAN.md` §3.1.
Method: security-lockdown skill; three independent read-only auditors (zones: secrets/history/logging, authn/authz/share routes, input/XSS/SQL/errors) plus runtime checks; fixes reviewed by an independent reviewer.

Commands run (exit codes):
- `npx tsc --noEmit` → 0; `npx vitest run` (app) → 0, 112 files / 680 tests; twin-core `npx vitest run` → 0, 59 tests; `npm run build` → 0.
- `npm audit` (app) → 0 vulnerabilities; twin-core → 0. `npm audit` before the fix: next critical (GHSA-2464-fhr4-w9xw), postcss high, vite high.
- `gitleaks git --log-opts="--all -- 02_PRODUCT/projects/workspaces/casa3d"` (v8.30.1) → 144 commits, no leaks.
- `gitleaks dir 02_PRODUCT/projects/workspaces/casa3d` → 6 hits, all Next.js build keys generated locally in `app/.next/` (preview mode, server-action encryption): build output, not tracked (`.gitignore`), not served.
- Runtime (`npm start`, `next start` without local trust, curl + Chrome DevTools): see the evidence column.

## Findings fixed
| # | Severity | Finding | File | Fix | Evidence |
|---|---|---|---|---|---|
| F-01 | critical | Next.js 15.5.27: unauthenticated RCE in the Image Optimization API with SVG (GHSA-2464-fhr4-w9xw) and Server Actions DoS (GHSA-p3m5-59mp-jv5r) | `package.json` | next 15.5.28; postcss 8.5.31 override; vite via `npm audit fix` | `npm audit` → 0 |
| F-02 | high | `Host: localhost` granted owner/admin trust; `npm run dev` listened on all interfaces, so anyone on the LAN had admin | `lib/public-gate.ts`, `scripts/run-local.mjs` | local trust opt-in (`CASA3D_LOCAL_TRUST=1`, only via `npm start`/`npm run dev`, forced `-H 127.0.0.1`, every `-H` form refused); `start:hosted` without trust | hosted instance + spoofed Host → 401/404/login; `tests/local-trust.test.ts` |
| F-03 | high | On any open failure of the on-disk database, `lib/db.ts` deleted the owner's data directory (`rmSync`) and/or silently fell back to an empty in-memory DB | `lib/db.ts` | open failure is a visible startup error; nothing deleted; no in-memory fallback; TLS certificate verified for remote Postgres | `tests/audit-fixes.test.ts` A-2 |
| F-04 | medium | No CSP / security headers; Google Fonts loaded from a third party | `middleware.ts`, `lib/security-headers.ts`, `app/layout.tsx` | per-request nonce CSP + `strict-dynamic`; inert CSP + `sandbox` on `/api`; frame-ancestors none, nosniff, XFO, Permissions-Policy, COOP, HSTS on https; fonts self-hosted (@fontsource, OFL-1.1) | 12 pages in Chrome, 0 CSP violations; `tests/security-headers.test.ts` |
| F-05 | medium | Middleware headers override stricter route headers in Next (photo CSP `sandbox`, `/go` no-referrer) | `lib/security-headers.ts` | strictest policy chosen per path in middleware | runtime headers on `/api/*` and `/go/*` |
| F-06 | medium | CSRF: 34 cookie-authenticated write routes accepted cross-site requests (`text/plain` forms pass SameSite=Lax from sibling subdomains) | `middleware.ts`, `lib/public-gate.ts` | every non-GET `/api/*` with a foreign `Sec-Fetch-Site`/`Origin` → 403 | curl: cross-site PUT/POST → 403, same-origin → 201; UI writes OK in Chrome |
| F-07 | medium | Error logging printed DB error objects with `query`/`params` (share tokens, PIN hashes, client names) | `lib/log.ts` + 11 routes | `logError` logs place, name, code, short message only | `tests/audit-fixes.test.ts` A-1 |
| F-08 | medium | Authenticated JSON bodies read without a size limit (`req.json()`), render scene read fully before the 40 MB check | `lib/http.ts`, renders and admin routes | streaming limits: 512 KB default, 3.5 MB draft, 40 MB scene, 16 KB admin | curl 600 KB → 413; test C-1 |
| F-09 | medium | Project snapshot accepted unknown keys, coordinates like 1e300 (Infinity areas/budget) and 50 000-char room names | `lib/repo.ts` | unknown top-level keys dropped (list = `Snapshot` interface, tested), coordinates within ±1000 m, room names cleaned to 80 chars | test C-2 |
| F-10 | medium | Demo mode + `ANTHROPIC_API_KEY` = unauthenticated, unlimited AI on the owner's key | `lib/ai.ts` | AI off in demo | test B-2 |
| F-11 | medium | Login limit per IP only: IPv6 rotation inside a /64 or a spoofable IP header = unlimited guessing and scrypt CPU load | `lib/auth.ts`, `lib/rate-limit.ts` | IPv6 /64 keys, global cap 60 / 15 min, configurable trusted IP header (`CASA3D_CLIENT_IP_HEADER`) | test B-3, `tests/auth.test.ts` |
| F-12 | medium | Share links never expired | `lib/share.ts` + all public share queries | default 90 days (0 = none), one `shareActive()` predicate, last access hour-rounded | `tests/share-expiry.test.ts` |
| F-13 | low | Change-order approval (money, days) needed only the link even when the designer enabled a PIN | `app/api/share/[token]/changes/[change]/route.ts`, `components/ChangeOrders.tsx` | PIN session required when PIN is on; PIN field shown in the approval UI | test B-4 |
| F-14 | low | scrypt N=2^14 for the remote-access password | `lib/auth.ts` | new passwords N=2^17 (stored), old hashes still verify, unknown N capped | test B-5 |
| F-15 | low | Local-proxy bypass: a proxy rewriting Host to localhost kept local trust | `lib/public-gate.ts` | non-loopback `x-forwarded-for` → not local | test B-8; curl → 401 |
| F-16 | low | Admin token compared with `!==`; no startup config validation | `lib/outbound.ts`, `lib/env-check.ts`, `instrumentation.ts` | `timingSafeEqual` on SHA-256; invalid env stops startup with a clear message | `ADMIN_TOKEN=scurt npm start` → exit 1; test A-5 |
| F-17 | low | 404 of the public share GET without `no-store` | `app/api/share/[token]/route.ts` | `no-store` | test B-7 |

## Open findings
| # | Severity | Finding | Why open | Owner action needed |
|---|---|---|---|---|
| O-1 | medium (multi-tenant only) | Tenancy is the anonymous `casa_owner` cookie, not a server-side account | single-owner app today; replaced by accounts in phase P2 (plan §1, §3.1.2) | decide target users / pricing (plan §5) before P2 |
| O-2 | low | Rate limits are in memory (lost on restart, per process) | one process on the owner's PC; DB/Redis-backed limits belong with hosting (P3) | none now |
| O-3 | low | Share tokens stored in clear text (primary key + FKs) | needs a migration of 4 tables; tokens are 256-bit and the DB is local | accept for P0 or schedule |
| O-4 | low | Logout only clears the cookie (a stolen session stays valid up to 30 days); changing the password revokes all | single owner | accept |
| O-5 | low | Links created before 2026-10-22 never expire | not breaking links already sent to clients | revoke old links in Presentation if wanted |
| O-6 | low | Some `repo.ts` catch blocks turn any `e.message` into a 400 (could expose an internal TypeError text) | no reproducible leak found | none |
| O-7 | info | 86 open Dependabot alerts in the repository belong to other projects (imported/, RAW_IMPORTS, skills); the 10 Casa3D alerts (vite, postcss) are fixed by this branch and close on merge | out of Casa3D scope | triage per project |
| O-8 | ops | Plan §3.1.7–3.1.8: secrets in the host's secret store and rotated at go-live; request logs without personal data, error tracking, uptime check | only relevant once hosted (P3) | at go-live |

## The 19 checks
| # | Check | Status | Evidence |
|---|---|---|---|
| 1 | No hardcoded secrets in source, config or client bundles | PASS | grep of source and `.next/static` (0 hits); gitleaks dir: only local build keys in untracked `.next/` |
| 2 | Secrets in env, validated at startup | PASS | `ANTHROPIC_API_KEY`, `ADMIN_TOKEN`, `DATABASE_URL` server-only; `lib/env-check.ts` + `instrumentation.ts` stop startup on invalid values; missing values fail closed |
| 3 | `.env*` ignored, only value-free `.env.example` tracked | PASS | `git ls-files`, `git check-ignore`; `.data/auth.json` ignored |
| 4 | No secrets in git history | PASS | gitleaks: 144 commits on the Casa3D path, no leaks |
| 5 | Auth middleware on every private route, default deny | PASS | `middleware.ts` runs on all paths; `gate()` denies by default; only share/login/static are public |
| 6 | Ownership checks on every resource by id (anti-IDOR) | PASS | every lib function scopes child → project → owner; share uuids bound to the share's project (auditor B) |
| 7 | Password hashing | PASS | scrypt N=2^17 (new), salted, timing-safe, never logged |
| 8 | Session cookies and CSRF | PASS | HttpOnly, SameSite=Lax, Secure on https/tunnel, 30 days, HMAC; CSRF check on every `/api` write (F-06) |
| 9 | Server-side validation, size limits | PASS | streaming body limits on every route (F-08); snapshot schema hardened (F-09); share routes already strict |
| 10 | No XSS sinks | PASS | no `dangerouslySetInnerHTML`/`innerHTML`; uploads type-sniffed, SVG refused, `/api` CSP sandbox |
| 11 | CSP without unsafe-inline scripts, frame-ancestors none | PASS | nonce + `strict-dynamic`; `unsafe-inline` only on style-src (React style attributes) |
| 12 | Parameterised SQL | PASS | every value passed as parameter; interpolations are code-controlled (auditor C) |
| 13 | Rate limits on login and every submit endpoint | PASS (in-memory, O-2) | login per IP//64 + global; share writes per IP and per token; PIN lockout in DB; renders one at a time; AI off in demo |
| 14 | CORS | PASS | no `Access-Control-*` anywhere |
| 15 | Security headers | PASS | HSTS (https), XFO DENY, nosniff, Referrer-Policy (no-referrer on share/go), Permissions-Policy, COOP |
| 16 | No debug / sensitive logging | PASS | `logError` (F-07); no tokens, PINs or IPs logged |
| 17 | Generic error responses | PASS | `handle()` returns details only for `HttpError`; 500 → "Eroare internă." |
| 18 | Dependencies audit clean, lockfile committed | PASS | `npm audit` 0 (app, twin-core); lockfiles committed |
| 19 | Nothing sensitive publicly served | PASS | `public/` = models, textures, demo JSON; no production source maps; render/export paths validated |

## Verdict
Production ready: NO for selling to other users (accounts and hosting are phases P2–P3, see O-1, O-2, O-8); YES for the current use: the owner's PC with client links through the tunnel and the separate demo, with every critical and high finding fixed and verified.
