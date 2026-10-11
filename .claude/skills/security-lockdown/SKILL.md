---
name: security-lockdown
description: Owner's standing pre-production security gate for any site or application (web, API, desktop such as WPF/.NET, mobile, CLI) - act as Principal AppSec engineer, run a zero-trust audit of the codebase and git history, fix what is found, and write SECURITY_AUDIT.md scoring the 19 checks. Runs automatically when a site or app is finished or about to ship, and on request ("audit de securitate", "hardening", "e gata de productie?"). On a project that already has SECURITY_AUDIT.md, re-check and update it instead of starting over.
when_to_use: A site/app reaches "done", before any deploy or publish, after a large feature, or when the owner asks for a security audit, hardening, lockdown or production readiness. Romanian triggers - "securitate", "audit", "hardening", "gata de productie", "lansare", "release", "publica aplicatia".
---

# security-lockdown

Role: Principal Cybersecurity Engineer and AppSec auditor. Zero trust: every input, route and
dependency is hostile until shown otherwise. The owner does not type commands; you run the
audit, fix, verify and report. Ask only before destructive or outward actions (rewriting git
history, rotating a live key, force-pushing, changing production settings).

## Zero friction for the owner

- Never stop their work for this: no questionnaire, no "should I check X?". Detect the stack
  and decide; mark checks that do not apply N/A with the reason.
- Full audit only at the finish line (release, deploy, publish, "e gata"). During normal work,
  when you touch auth, input handling, secrets or a dependency, check just that area in passing.
- Fix in the same change set as the work it belongs to; do not open side quests or refactors.
- Tell the owner one line: verdict, how many fixed, what (if anything) needs them. The detail
  lives in `SECURITY_AUDIT.md`.

## Workflow

1. **Scope.** Detect stack and entry points (framework, API routes, DB layer, auth, hosting).
   If `SECURITY_AUDIT.md` exists, start from it: re-verify every row, do not trust old ticks.
2. **Scan** (in parallel subagents when the codebase is large): secrets and git history,
   authn/authz, input handling, API surface, build output and dependencies.
3. **Fix** each finding in the code with the smallest correct change and a test that fails
   before the fix where practical. Never weaken a test or a check to make it pass.
4. **Verify** by running: the test suite, the build, `npm audit --omit=dev` (or the stack's
   equivalent), a secret scan of the tree and of history (`gitleaks detect` if installed,
   otherwise `git log -p` grep for key patterns), and a request against the running app for
   headers, CORS and error bodies. Paste exit codes and key output into the report.
5. **Report** in `SECURITY_AUDIT.md` (template below). "Production ready" only when every
   critical and high row is PASS with evidence; otherwise say exactly what blocks it.

## The 19 checks

Secrets, env and git
1. No API keys, private keys or tokens hardcoded in source, config or client bundles.
2. Secrets live in env (`.env.local` locally, the host's secret store in production) and are
   validated at startup with a strict schema (Zod / T3 Env); the app refuses to boot otherwise.
3. `.env*` (except a value-free `.env.example`) is in `.gitignore` and not tracked.
4. Git history has no committed secrets. If one is found: report it, rotate the key (owner
   action), and propose the history rewrite; never rewrite or force-push without the owner.

Authentication, authorization, access
5. Every admin route and private endpoint goes through auth middleware; default deny.
6. Ownership checks on every resource access by id (anti-IDOR), server-side, with a test that
   user A cannot read or change user B's object.
7. Passwords hashed with Argon2id (or bcrypt cost >= 12), salted; never logged.
8. Session cookies `HttpOnly`, `Secure`, `SameSite=Lax` or `Strict`; sensible expiry; CSRF
   protection on state-changing requests where cookies authenticate.

Input, XSS, injection
9. Every form and API payload validated server-side with a strict schema (Zod / Yup); unknown
   fields rejected.
10. No XSS sinks: no unsanitised `dangerouslySetInnerHTML` / `innerHTML` / `v-html`; user
    content escaped or sanitised (DOMPurify) at render.
11. Content-Security-Policy set (no `unsafe-inline` scripts; nonces or hashes), plus
    `frame-ancestors 'none'`.
12. Database access only through parameterised queries or a safe ORM API; no string-built
    SQL or NoSQL operators from user input.

API, CORS, rate limits, headers
13. Rate limiting on login, register, password reset, checkout and every submit endpoint.
14. CORS: no `*` on private endpoints or with credentials; explicit allow-list of origins.
15. Security headers: HSTS, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`,
    `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy`.

Production lockdown
16. Debug mode off; no verbose or sensitive logging (tokens, passwords, PII) in production.
17. Error responses are generic for users; stack traces and internals only in server logs.
18. Dependencies: audit clean of high/critical (or each exception justified), vulnerable ones
    updated, unused packages removed, lockfile committed.
19. Nothing sensitive is publicly served: no production sourcemaps, `.bak`, backups, dumps,
    logs, `.git` or env files in `public/`, `dist/` or the deployed output.

## The same checks on non-web applications

Apply the intent of each check to the platform; do not invent web controls where there is no web.
- **Desktop (WPF/.NET, Electron, Tauri):** secrets never in the binary or `appsettings.json`
  (DPAPI / Windows Credential Manager / OS keychain); signed builds; auto-update over HTTPS
  with signature check; no `Process.Start`/shell with user input; safe deserialisation
  (no `BinaryFormatter`, typed JSON); parameterised SQL incl. SQLite; file paths validated
  against traversal; logs free of secrets; release builds without debug symbols published;
  licence/activation checks server-side, not only in the client.
- **Mobile:** secrets in Keychain/Keystore, none in the bundle; TLS only (no cleartext
  exceptions); deep links and intents validated; WebView JS bridges minimal; obfuscation and
  no debuggable release build; tokens refreshable and revocable server-side.
- **API / backend services and CLIs:** checks 1-9 and 12-18 as written; for a CLI, 13-15 are
  usually N/A and arguments are treated as untrusted input.

## SECURITY_AUDIT.md template

```markdown
# Security audit - <project> - <date>
Scope: <stack, commit>. Commands run: <list with exit codes>.

## Findings fixed
| # | Severity | Finding | File | Fix (commit) | Evidence |

## Open findings
| # | Severity | Finding | Why open | Owner action needed |

## The 19 checks
| # | Check | Status (PASS / FAIL / N/A) | Evidence |

## Verdict
Production ready: YES / NO - <one line why>.
```

Severity: critical (remote compromise, secret leak, auth bypass), high (IDOR, injection,
stored XSS), medium (missing headers, weak limits), low (hygiene). N/A needs a reason (for
example "no passwords: OAuth only").
