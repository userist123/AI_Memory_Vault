---
type: project
category: product-plan
status: review
priority: high
confidence: medium
id: "1272ff8b-c06a-45d5-a1c8-772297f1c049"
lifecycle: REVIEW
provenance_status: incomplete
provenance:
  source_type: agent_analysis
  source_ref: "casa3d app code read on 2026-10-17 (lib/auth.ts, lib/owner.ts, middleware.ts, lib/public-gate.ts, lib/db.ts, lib/schema.ts, lib/repo.ts, lib/renders.ts, lib/share.ts, lib/project-export.ts, lib/trash.ts)"
  redaction: not_applicable
tags: [casa3d, saas, hosting, multi-tenant, gdpr, plan]
verification: unverified
relations: []
---

# Casa3D: plan for selling to other users (accounts and hosting)

Status: written plan only. Nothing here has been bought, deployed, published or
created. All prices are estimates from public list prices known as of late 2026
and must be re-checked on the vendor page before any purchase.

## 0. Where the app is today (code-verified)

| Area | Current state | File |
|---|---|---|
| Login | One password for one owner. scrypt (N=16384, r=8, p=1), HMAC-SHA256 session cookie `casa_session`, 30 days, secret rotates on password change. 5 attempts per IP per 15 min. | `app/lib/auth.ts` |
| Identity | No users table. The data owner is an anonymous httpOnly `casa_owner` UUID cookie. | `app/lib/owner.ts` |
| Gate | Local requests are always allowed. `/share/<token>` and `/api/share/<token>/...` are public. `/api/admin`, `/admin`, `/api/auth/password` are local-only. `CASA3D_DEMO=1` opens everything except auth and render POSTs. | `app/middleware.ts`, `app/lib/public-gate.ts` |
| "Local" test | Host header matches localhost/127.x/[::1], no `cf-connecting-ip`, and any `x-forwarded-host` is local. | `app/lib/public-gate.ts:12-14` |
| Database | `DATABASE_URL` → Postgres (pg pool, max 5); otherwise PGlite on disk, or in memory on Vercel. | `app/lib/db.ts` |
| Tenancy | Column `owner` (text) on `projects`, `tasks`, `studio_profile`. All child tables (revisions, shares, site_photos, snags, change_orders, ...) are reached via `project_id` after an ownership check (`getProject(owner, id)` / `own()`). | `app/lib/schema.ts`, `app/lib/repo.ts` |
| Files | Site photos stored as `bytea` in the DB; renders on local disk (`CASA3D_RENDER_DIR` or `.data/renders/`). | `app/lib/schema.ts`, `app/lib/renders.ts` |
| Renders | Blender spawned from `CASA3D_BLENDER`, one job per server (else 409), 45 min timeout, scene ≤ 40 MB. | `app/lib/renders.ts` |
| Share links | 32 random bytes base64url, no expiry, revocable. Public view exposes project name, revision and full snapshot; clients can send feedback, pick room options, approve change orders. Rate-limited per IP+token and per token. | `app/lib/share.ts`, `app/lib/rate-limit.ts` |
| GDPR | Full project export ZIP exists ("art. 20"). Trash with 30-day purge exists. **No account deletion, no privacy page.** | `app/lib/project-export.ts`, `app/lib/trash.ts` |
| Hardening | No CSP or security headers in `next.config.mjs` (middleware sets only noindex/no-store). No Dockerfile, no SECURITY_AUDIT.md, no deploy doc. 52 open GitHub security alerts on `main` (repo-wide). | |
| AI | `ANTHROPIC_API_KEY` server-side; usage is paid per call by whoever owns the key. | `.env.example` |

Good news: the data layer is already scoped by `owner`, so multi-tenancy is a
change of *who the owner is* (anonymous cookie → real account), not a rewrite.

## 1. Multi-tenant accounts

### 1.1 Data model

New tables (Postgres; PGlite stays for local dev and tests):

- `accounts` — the tenant (a design studio). `id uuid`, `name`, `plan`, `created_at`, `deleted_at`.
- `users` — `id`, `email` (unique, lower-cased), `password_hash` (reuse the scrypt code from `auth.ts`), `email_verified_at`, `created_at`, `deleted_at`.
- `memberships` — `account_id`, `user_id`, `role` (`owner` | `designer` | `viewer`), unique pair.
- `sessions` — server-side sessions: `id` (random 32 bytes, stored hashed), `user_id`, `account_id`, `expires_at`, `revoked_at`, `user_agent`, `ip_hash`. Replaces the stateless HMAC cookie so logout, "log out everywhere" and admin revocation work.
- `invites` — `account_id`, `email`, `role`, `token_hash`, `expires_at` (7 days), `accepted_at`.
- `password_resets` — `user_id`, `token_hash`, `expires_at` (30–60 min), `used_at`.
- `usage_counters` — `account_id`, `period` (month), `renders`, `ai_calls`, `storage_bytes`.
- `audit_log` — `account_id`, `user_id`, `action`, `target`, `at` (login, invite, share created/revoked, export, delete).

Existing tables: keep the `owner` column and make it mean `account_id`
(rename later if desired). Migration for the current owner: create one
account whose `id` equals the current `casa_owner` UUID, so every existing
project stays attached with no data rewrite.

Isolation rules:
- `owner` comes **only** from the server-side session, never from a cookie
  the browser can set.
- Every child-table route must go through the ownership check. Before going
  public, add a two-tenant test suite: account A creates data, account B calls
  every API route with A's ids and must get 404 (not 403, to avoid leaking
  existence). Generate the route list from `app/app/api/**/route.ts` so new
  routes cannot be forgotten.
- Optional defence in depth later: Postgres row-level security keyed on a
  per-request `app.account_id` setting.

### 1.2 Sign-up and invites

- Phase 1: **invite-only.** The platform owner creates studio accounts from a
  local-only admin page; a studio owner invites designers by email.
- Phase 2: open self-sign-up with email verification, a trial plan and a
  CAPTCHA-free throttle (per IP and per email domain).

### 1.3 Password reset by email

- Transactional email provider (Resend, Postmark or Amazon SES; EU region where offered).
- Single-use token, hashed in DB, 30–60 min expiry, same response whether or
  not the email exists, all sessions revoked after reset.
- SPF, DKIM and DMARC on the sending domain.

### 1.4 Roles

| Role | Can |
|---|---|
| `owner` | everything in the account, billing, invite/remove members, delete account, export all |
| `designer` | create/edit projects, renders, shares; no billing or member management |
| `viewer` | read-only inside the app (for an office assistant) |
| Client (end customer) | stays **token-based** via the existing share links; no account needed. Client accounts are a later option. |

### 1.5 Per-account limits

Enforced server-side from `usage_counters`, shown in the UI:
projects (e.g. 3 on trial), storage (photos + renders, e.g. 1 GB), renders per
month, AI calls per month (the Anthropic key is paid by the platform, so this
cap is mandatory before anyone else can call it), active share links, members.

## 2. Hosting options

Key constraint from the code: **Vercel cannot run this app as-is** — on Vercel
PGlite runs in memory (data lost per instance), renders need a long-lived
process with Blender and local disk, and the render lock is in-process. Vercel
works only with Postgres + object storage + an external render worker, i.e.
after the work in phase 3.

### 2.1 Comparison (monthly, EUR, estimates to re-check)

| Option | What | App + DB | Storage/backups | Renders | Total / month | Fit |
|---|---|---|---|---|---|---|
| A. Owner's PC (today) | Cloudflare quick tunnel → named tunnel + own domain | 0 (electricity) | external disk / cloud copy ~0–5 | PC GPU | **~1–5** + domain | Demo for a few known people only. Uptime = PC uptime; home network exposed; not acceptable for paying users. |
| B. Single VPS (Hetzner, EU) + Docker | Caddy (auto TLS) + Next.js + Postgres in Docker Compose on a 4 vCPU / 8 GB box | ~8–15 | Hetzner backups +20 % (~2–3) + Storage Box 1 TB (~4) or Cloudflare R2 (~0.015/GB, no egress fee) | none on the VPS (no GPU) | **~15–25** | **Recommended first production host.** Cheap, EU data, full control; you operate updates and backups. |
| C. Vercel + managed Postgres + object storage | Vercel Pro (~20 USD/seat), Neon or Supabase (~0–25), R2/S3 | ~20–45 | included / ~1–5 | external worker required | **~25–50** + worker | Less ops, but needs phase-3 refactor first; US company as processor (DPA/SCCs). |
| D. Managed PaaS (Railway, Render, Fly.io) | Container + managed Postgres | ~15–35 | volumes / R2 | external worker | **~20–40** | Middle ground; similar refactor needs to C for files. |

Domain: `.ro` or `.com` ~10–15 EUR/year. TLS: free (Caddy/Let's Encrypt or Cloudflare).

### 2.2 Where Blender rendering runs

The render is the expensive part. Options, in recommended order:

1. **No renders in the demo.** Show pre-rendered example images. Cost 0.
2. **Render queue with the owner's PC as a pull worker.** Add a `render_jobs`
   table (queued/running/done/failed, account_id, limits). The hosted app only
   enqueues. A small worker on the owner's PC polls the hosted API over
   outbound HTTPS with its own worker token, downloads the scene, runs Blender
   on the local GPU, uploads the result to object storage. No inbound tunnel,
   no home IP exposed. Cost 0 extra; throughput = one GPU, PC must be on.
3. **On-demand cloud GPU** (RunPod, Vast.ai, similar) running the same worker
   image: roughly 0.3–0.8 USD per GPU-hour, paid only while rendering. Good
   when demand is irregular.
4. **Dedicated GPU server** (e.g. Hetzner GEX line, ~180+ EUR/month plus setup)
   — only once paid render volume justifies it.

The worker design is the same for 2, 3 and 4, so build it once (phase 3).

### 2.3 Backups

- Nightly `pg_dump` (encrypted with age/GPG) to a second provider or Storage
  Box; keep 7 daily + 4 weekly + 6 monthly.
- Object storage versioning or a weekly sync copy.
- **Restore drill** monthly into a scratch database; a backup that was never
  restored does not count.
- Move site photos from `bytea` to object storage before the DB grows large
  (keeps dumps small).

## 3. Security and legal gates before going public

### 3.1 Must-fix before any public host (blocking)

1. **Remove trust in the Host header.** `isLocalRequest()` treats a request as
   local if the `Host` header says `localhost` and there is no
   `cf-connecting-ip`. On a VPS reached directly (no Cloudflare in front) an
   attacker can send `Host: localhost` and get the local-only admin and
   password routes. On a hosted build, "local" must be disabled entirely
   (env flag, default off in production) and admin must require a real admin
   session.
2. Replace the anonymous `casa_owner` cookie as the source of tenancy with the
   server-side session (section 1.1).
3. Security headers and CSP (`next.config.mjs` headers or middleware):
   CSP with nonces, HSTS, `X-Content-Type-Options`, `Referrer-Policy`,
   `frame-ancestors 'none'`, `Permissions-Policy`.
4. Share links: add optional expiry (default e.g. 90 days), show last access,
   and **independent security review of every `/api/share/*` route**
   (feedback, choices, change-order approval are writes by unauthenticated
   users). Reviewer gets this probe list: token guessing/enumeration, rate
   limit bypass via IP rotation, body size, stored XSS in feedback shown to the
   designer, replay of change-order approvals, cross-project id swapping in
   `/changes/<uuid>`, revoked-token access, cache headers on public responses.
5. Rate limits: move from in-memory to DB/Redis-backed so they survive
   restarts and multiple instances; add limits to sign-up, reset, invite, AI
   and render endpoints.
6. Run the **security-lockdown** skill end to end and write
   `SECURITY_AUDIT.md` (19 checks), including git-history secret scan; triage
   the 52 GitHub alerts that touch the Casa3D dependency tree.
7. Secrets only in the host's env/secret store; `ADMIN_TOKEN`,
   `ANTHROPIC_API_KEY`, session and worker keys rotated at go-live.
8. Logging and alerts: request logs without personal data, error tracking
   (self-hosted or EU-region), uptime check.

### 3.2 GDPR basics (Romanian/EU users)

- Roles: for the studio's own account data Casa3D is the **controller**; for
  the end-clients' data the studio enters (names, addresses, site photos),
  Casa3D is the **processor** → a Data Processing Agreement (DPA) in the terms.
- Privacy policy and terms of service pages (Romanian + English), list of
  sub-processors (host, email provider, Anthropic for AI features, GPU
  provider) with locations and transfer basis (EU-US Data Privacy Framework /
  SCCs for US providers).
- Cookies: only strictly necessary cookies (session) → no consent banner
  needed; add one only if analytics are introduced.
- Data subject rights: export exists per project; add **account-level export
  and account deletion** (soft delete, then hard purge after 30 days, which
  matches the existing trash pattern), plus deletion of backups by rotation.
- Retention policy for logs, renders and deleted projects written down.
- AI features: tell users that prompts/images go to Anthropic; offer a switch
  to disable AI per account.
- Business side (needs an accountant, not code): legal entity (PFA/SRL),
  invoicing through RO e-Factura, VAT/OSS for EU customers.

### 3.3 Payments (later phase only)

Stripe Checkout + Customer Portal + webhooks updating `accounts.plan`; no card
data touches the server. Only after accounts, limits and legal pages exist.

## 4. Phased roadmap

Effort = focused working days with agent help, single developer, excluding
waiting time. Each phase ends with tests green, an independent review, and
the owner's go-ahead.

| Phase | Goal | Main work | Effort | Monthly cost after |
|---|---|---|---|---|
| **P0 Harden today's app** | safe to show to anyone | Host-header fix (3.1.1), security headers/CSP, share expiry, security-lockdown audit, alert triage | 3–5 days | 0 |
| **P1 Demo slice** (thin first slice) | others can try Casa3D without risk to the owner's data | Separate demo instance on its own database (never the owner's), existing `CASA3D_DEMO=1` mode, seeded sample project, nightly reset, renders off (pre-rendered gallery), AI off or tightly capped, "demo" banner, privacy note. Host on a small VPS (option B) or temporarily on the PC behind a named tunnel. | 3–5 days | ~5–15 + domain |
| **P2 Accounts** | real multi-user | Tables in 1.1, server-side sessions, invite-only flow, email reset, roles, two-tenant isolation suite, migration of the owner's data | 10–15 days | + email (free tier ~0–15) |
| **P3 Production host + render queue** | paying-ready infrastructure | Dockerfile + Compose (Caddy, app, Postgres), photos and renders to object storage, `render_jobs` queue + PC pull worker, backups + restore drill, monitoring | 7–10 days | ~15–25 (+ GPU if cloud) |
| **P4 Legal + limits** | lawful to sell in RO/EU | Privacy/terms/DPA pages, account export/delete, sub-processor list, per-account limits and usage UI, independent review of `/api/share/*` | 5–8 days (+ lawyer/accountant) | 0 |
| **P5 Payments** | charge money | Stripe plans, webhooks, trial expiry, invoices via e-Factura integration | 5–8 days | Stripe fees (~1.5 % + fixed per EU card) |

Total to a sellable product: roughly **6–9 weeks** of focused work. The demo
(P0 + P1) is about **1.5–2 weeks** and can go out first.

## 5. Open questions for the owner

1. **Budget:** monthly hosting ceiling for the demo phase and for production?
2. **Target users:** independent interior designers, studios with several
   designers, renovation firms, or end homeowners? (Changes roles and pricing.)
3. **Pricing model:** per designer seat per month, per project, or credits for
   renders? Is a free tier wanted?
4. **Renders:** keep them on your PC as a worker (free, PC must be on), pay
   for cloud GPU per hour, or leave renders out of the demo?
5. **AI features:** include them for other users (you pay Anthropic per call)
   or switch them off until paid plans exist?
6. **Demo access:** fully public link, or a link you give personally to a few
   people?
7. **Market and language:** Romania only at first, or EU (English UI, VAT OSS)?
8. **Legal entity:** do you already have a PFA/SRL that can invoice?
9. **Domain/brand:** is "Casa3D" the final name (trademark and domain check
   needed before buying)?
10. **Data residency:** must all data stay in the EU (rules out some US-only
    managed services)?
