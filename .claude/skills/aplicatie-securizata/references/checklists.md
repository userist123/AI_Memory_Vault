# Liste de verificare pe tip de produs

Origin: the AI Memory Vault security programme (Codex + Gemini research, fable-consultant threat model, 2026-11-22), updated 2026-11-26 with the OWASP Top 10 for Agentic Applications 2026, MCP specification revision 2026-07-28 and OWASP MASVS. The lists are generic: the state of one project (what is already ticked) lives in that project's security document, not here. Tick an item only with a test or real evidence; re-check versions and CVEs for every new project.

Items that mention consent, approval or the owner are **controls inside the product we build** (what its users must confirm). They are not questions the building agents ask the owner; the agents follow the owner's no-approval rule from SKILL.md.

## 3. Checklists by product type

### 3.1 LLM agents, memory, MCP servers
Sources: OWASP Top 10 for LLM Applications 2025 (LLM01 Prompt Injection ... LLM10 Unbounded Consumption, https://genai.owasp.org/llm-top-10/), OWASP Top 10 for Agentic Applications 2026 (ASI01 Agent Goal Hijack, ASI02 Tool Misuse, ASI03 Identity and Privilege Abuse, ASI04 Agentic Supply Chain, ASI05 Unexpected Code Execution, ASI06 Memory & Context Poisoning, ASI07 Insecure Inter-Agent Communication, ASI08 Cascading Failures, ASI09 Human-Agent Trust Exploitation, ASI10 Rogue Agents; https://genai.owasp.org/top-10-for-agentic-applications-2026/), OWASP Agentic AI Threats & Mitigations, OWASP MCP Security Cheat Sheet, NIST AI 100-2 E2025, MCP specification 2026-07-28 security best practices and authorization (https://modelcontextprotocol.io/specification/latest/basic/security_best_practices), MINJA (NeurIPS 2025), AgentPoison (NeurIPS 2024).
- [ ] Retrieved, imported and proposed text is **data, never authority**. State changes are enforced in code, never by the model. There is no lifecycle or verification change from text.
- [ ] No instruction-only guardrail or LLM-as-judge is used as a security boundary. These are supplementary signals only.
- [ ] Narrow tools only (search, get, propose); arguments validated; paths canonicalised; minimum fields returned; secrets never placed in prompts or tool output.
- [ ] Proposals from agents are scanned at write time; text the scanner does not classify as safe is quarantined; rate limits are persistent and per principal.
- [ ] MCP servers use **stdio** for local clients. An HTTP server binds loopback only, validates `Host` and `Origin`, authenticates every request, and runs a patched SDK. Example: CVE-2025-66416 (DNS rebinding in the MCP Python SDK before 1.23.0) was verified on NVD.
- [ ] Tool descriptions and server configuration are pinned and reviewed; a change requires consent (rug-pull defence).
- [ ] Authority actions (attest, promote, approve) need fresh user presence bound to the exact content hash. Write this as an item of the project's threat model.
- [ ] Tests: adversarial fixtures (instruction override, encoded, multilingual, multi-turn) prove that no state changes and no withheld content is served.
- [ ] **Agent-output exfiltration (both sources).** No external Markdown images, links or raw HTML in rendered agent output. No outbound tool in a session that read private notes. A canary-secret test. Real case: EchoLeak, CVE-2025-32711, zero-click exfiltration from Microsoft 365 Copilot; verified.
- [ ] **Unicode canonicalisation before scanning.** NFKC, HTML unescape until stable, format characters stripped (soft hyphen, zero-width), Cyrillic/Greek/Latin look-alikes folded, line breaks collapsed, tag characters U+E0000-E007F and bidi controls removed; a CI check runs the same on instruction and skill files.
- [ ] **Separation over filtering (Gemini).** The stronger defence is capability / information-flow separation (CaMeL, FIDES preprints): untrusted text never selects tools or destinations. A lexical scanner stays a signal.
- [ ] **Separate, audience-bound tokens per caller** (MCP Security Best Practices). Test that an agent token gets 403 on owner routes.
- [ ] **Poisoning by crowding (NIST AI 100-2).** Test whether near-duplicate poisoned notes push trusted notes out of the top results.
- [ ] **MCP authorization (spec 2026-07-28).** A server accepts only tokens issued for it (audience check; clients send the RFC 8707 `resource` parameter); no token passthrough to upstream APIs; tokens never in the URL query string; least-privilege scopes with step-up (`insufficient_scope` 403), never `*`/`all`. Tests: a token for another audience gets 401; a token in the query string is rejected.
- [ ] **MCP proxy servers:** per-client consent before the third-party flow, exact redirect-URI match, single-use short-lived `state`, consent pages not frameable. **State handles are not authentication**: bind them server-side to the user id from the verified token.
- [ ] **MCP clients:** SSRF guard on OAuth discovery (HTTPS only, private and link-local ranges blocked, every redirect re-checked); authorization URLs with `javascript:`, `data:`, `file:`, `vbscript:` rejected and never opened through a shell; `iss` validated (RFC 9207) against mix-up; one-click local server install shows the exact full command and runs it sandboxed. Real case: CVE-2025-6514, `mcp-remote` 0.0.5-0.1.15 ran OS commands when connecting to an untrusted MCP server (fixed in 0.1.16; JFrog advisory, confirmed by both research agents).
- [ ] **Unbounded consumption (LLM10) and cascading failures (ASI08).** Per-user and per-run budgets: max tokens, max tool calls, max agent steps, wall-clock limit, spend cap; a circuit breaker stops a loop; tests prove the cap stops a runaway run.
- [ ] **Agent identity and inter-agent messages (ASI03, ASI07).** Each agent has its own identity and short-lived, scoped credentials; messages between agents are authenticated and treated as data; an agent cannot raise another agent's privileges.
- [ ] **Rogue agent containment (ASI10).** A kill switch that stops all agents of the product, an audit log of tool calls, and alerts on unusual tool use or egress.
- [ ] **Human-agent trust (ASI09).** The UI shows what the agent will do before an authority action, in plain words, with the exact target; the agent cannot fake confirmations or urgency.

### 3.2 Web applications and sites
Sources: OWASP Top 10:2025 (A01 Broken Access Control, A02 Security Misconfiguration, A03 Software Supply Chain Failures; verified on owasp.org), ASVS 5.0.0, OWASP Cheat Sheets, MDN, CISA 2025 npm advisory.
- [ ] Authorization on every operation and object, tested with two accounts (IDOR).
- [ ] Render plain text by default; sanitise user Markdown/HTML with a maintained sanitizer (for example DOMPurify); no unsafe DOM sinks.
- [ ] Strict CSP: start in Report-Only, then nonce- or hash-based scripts, `object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'`. Add Trusted Types where compatible.
- [ ] HSTS, `nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, a `Permissions-Policy` that disables unused features, and SRI on any CDN script.
- [ ] Cookies `__Host-`, `Secure; HttpOnly; SameSite=Lax`, plus CSRF tokens or Origin checks on state changes; rate limits on login, recovery and costly endpoints.
- [ ] No secret in a client bundle (public build variables are public); dev servers bound to localhost; never Vite `allowedHosts: true`.
- [ ] Gate: locked install, dependency audit, secret scan, CodeQL/Semgrep, ZAP Baseline on staging, MDN HTTP Observatory, and a header-assertion script against the deployed URL.
- [ ] **Authorization in the data layer**, never only in middleware. Real case: Next.js CVE-2025-29927, where an `x-middleware-subrequest` header skipped the middleware; verified on NVD. Patch frameworks promptly.
- [ ] **SSRF:** resolve the address at connect time; reject loopback, private, link-local and metadata IPs; re-check every redirect. Also validate open redirects against a local-path or host allowlist.
- [ ] **Uploads:** check magic bytes, cap size, use random names, store outside the executable web root.
- [ ] **Markdown:** if `rehype-raw` is used, follow it with `rehype-sanitize`, plus a URL allowlist that blocks `javascript:` links.
- [ ] **GitHub Pages cannot send response headers,** and a meta-tag CSP cannot enforce `frame-ancestors`. Use Vercel `vercel.json` headers or a CDN in front.
- [ ] **3D assets:** untrusted glTF/Draco models get a validator, size caps and a Worker.
- [ ] **AI-suggested packages:** check that every package exists and is the intended one before installing it (slopsquatting, USENIX Security 2025).

### 3.3 Windows desktop (.NET), local daemons, Python tools
Sources: Microsoft DLL security, BinaryFormatter security guide, ZIP/TAR best practices, MSI custom-action guidance, OWASP WSTG DNS rebinding, MITRE ATT&CK T1574.001.
- [ ] Full paths for DLL and process loads; no `BinaryFormatter`; `System.Text.Json` with size limits.
- [ ] UI runs `asInvoker`; elevated work goes to a separate helper with narrow IPC; release artefacts are signed; no `AlwaysInstallElevated`.
- [ ] Evidence files and archives are hostile input: canonical paths, link handling, entry and size limits (zip bombs), extraction into a fresh controlled directory.
- [ ] Local daemons: loopback only; `Host` and `Origin` validated; token on every request, stored with DPAPI or a restrictive ACL, never in URLs or logs; refuse to start if the bind fails (port squatting); named pipes get an explicit DACL, because the default descriptor grants read access to Everyone and anonymous (Microsoft named-pipe security docs); test a connection from an unauthorised account and expect a refusal.
- [ ] Python: `yaml.safe_load`, never pickle from others, `subprocess` without `shell=True`, atomic writes, LF line endings for files other tools hash or diff, `pip --require-hashes` or `uv.lock`.
- [ ] Gate: .NET security analyzers (CA2300, CA2326 for Newtonsoft `TypeNameHandling`, CA3003, CA3006, CA3011), BinSkim on PE files, Bandit/Semgrep, pip-audit/OSV, malicious-archive tests, daemon tests (forged Host, foreign Origin, no or invalid token).
- [ ] **Process start-up:** `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`. Never launch `.bat`/`.cmd` with arguments: `shell=False` does not protect them on Windows (BatBadBut class). Call `.exe` files directly.
- [ ] **Local listeners:** bind with `SO_EXCLUSIVEADDRUSE` (port squatting). Named pipes get `PIPE_REJECT_REMOTE_CLIENTS` and `FILE_FLAG_FIRST_PIPE_INSTANCE`. Check regularly that nothing listens on 0.0.0.0 (MCP Inspector, CVE-2025-49596).
- [ ] **Secrets and DPAPI (resolved disagreement).** DPAPI does **not** isolate a secret from other processes of the same Windows user, and on this PC the owner and the agents share one Windows user. Never put tokens in command-line arguments or inherited environments. Real isolation needs a user-presence key (Windows Hello / a hardware key) or a separate account.
- [ ] **Signed updaters** verify the hash and Authenticode before running anything.

### 3.4 Supply chain, GitHub and CI
Sources: GitHub Actions secure-use docs, GitHub artifact attestations, OpenSSF Scorecard, SLSA v1.1, Sigstore, CISA ESF, NIST SP 800-218A (SSDF profile for generative AI, https://csrc.nist.gov/pubs/sp/800/218/a/final), OWASP Secure Coding with AI Cheat Sheet; incidents tj-actions/changed-files (2025), xz-utils (2024), npm worm campaigns (2025); Snyk ToxicSkills (2026: 76 confirmed malicious skills out of 3,984 audited, verified on snyk.io).
- [ ] Every action pinned to a full SHA, enforced by a CI check (the vault repo has one: `30_SCRIPTS/verification/workflow_security_audit.py`).
- [ ] No `pull_request_target` or `workflow_run` unless reviewed as privileged.
- [ ] Default-deny `permissions` on every workflow, granted per job.
- [ ] actionlint and zizmor in CI, clean.
- [ ] Dependency review on PRs (fails on high), an OSV scan (report-only while an existing backlog is triaged, then blocking), and OpenSSF Scorecard weekly.
- [ ] Branch ruleset on `main`: PRs only, required checks, no force-push, a required human review for merges (an agent approving its own PR is not review; agents never merge), and secret scanning push protection. These are repository settings in the owner's GitHub account; the agent lists them in the delivery report as residuals if they are not set, it does not stop to ask.
- [ ] Agents with repository write access: issue, PR and comment text is adversarial input; no secrets in agent jobs; agents never merge.
- [ ] Imported skills and instruction packs are quarantined: static review, no execution on ingest, a pinned checksum, promotion only by a deliberate product step with its own review (in the vault repo the ingestion pipeline does this).
- [ ] Changes to instruction files (CLAUDE.md, AGENTS.md, skills, memory) are reviewed as security-sensitive (red-team chain 9). A pre-commit check rejects zero-width and bidi characters in them (Rules File Backdoor class). Integrity alerts cover `.claude/`, `.mcp.json` and environment overrides of API base URLs.
- [ ] **Dependabot for GitHub Actions:** weekly, grouped, 7-day cooldown (the GitHub default is 3 days since July 2026), so SHA pins get refreshed. Version updates for other ecosystems are enabled per project, sized to the pull-request volume it can absorb.
- [ ] **Dependency intake:**
  - `npm ci --ignore-scripts` with `minimumReleaseAge`;
  - NuGet Package Source Mapping with `RestoreLockedMode=true`;
  - never a bare `--extra-index-url`.
- [ ] **Actions:** template values reach `run:` only through `env:`; the OIDC `sub` claim is bound to a branch or environment; `harden-runner` egress allowlists on jobs that run agents; gate on individual Scorecard checks, not the aggregate score.
- [ ] **Releases:** SBOM plus provenance attestations for artefacts (SLSA 1.1, PyPI attestations).

### 3.5 Android apps (APK/AAB)
Sources: OWASP MASVS v2 control groups (https://mas.owasp.org/MASVS/), OWASP MASTG for the tests, Android developer security guidance. Control wording below is paraphrased; cite the control page (for example https://mas.owasp.org/MASVS/controls/MASVS-STORAGE-1/) in the project's security document.
- [ ] **STORAGE-1/2:** sensitive data only in app-private storage or the Android Keystore; nothing sensitive in logs, backups (`android:allowBackup` / data extraction rules set), clipboard or screenshots of sensitive screens.
- [ ] **CRYPTO-1/2:** platform crypto with current algorithms; keys in the Keystore, never hard-coded in the APK.
- [ ] **AUTH-1..3:** authentication and session decisions on the server; biometrics bound to a Keystore key (CryptoObject), not a boolean; step-up for sensitive actions.
- [ ] **NETWORK-1/2:** TLS only (`cleartextTrafficPermitted="false"` in the network security config); certificate pinning only where we control the endpoint and have a rotation plan.
- [ ] **PLATFORM-1..3:** every component's `android:exported` set deliberately; intents, deep links and content providers validate input and caller; WebViews without JavaScript bridges to untrusted content and without file access; sensitive screens protected from overlays.
- [ ] **CODE-1..4:** current `targetSdk` (Google Play requirement) and its behaviour changes read on developer.android.com for that API level (intent and media rules tighten each release), dependencies without known CVEs, a working update path, all external input validated.
- [ ] **RESILIENCE-1..4** (only where the threat model needs it, e.g. payments or anti-cheat): Play Integrity, tamper and debugger detection treated as signals, never as the only control.
- [ ] **PRIVACY-1..4:** minimum permissions and data, no tracker SDK the owner did not decide on (check with the Exodus signatures from `analiza-aplicatii.md`), Data safety form true to the build.
- [ ] Release: R8/minify on, signing key outside the repo (Play App Signing), no secret in `BuildConfig` or resources (a decompiled APK is public), MobSF static scan and `apkanalyzer` permission check in the gate.

### 3.6 Games we build
- [ ] Multiplayer: the server is authoritative for state, scores and economy; the client is never trusted; rate limits and replay protection on actions.
- [ ] Save files and mods are hostile input: size limits, schema validation, no code execution from saves; mods load in a restricted API without file-system or network access.
- [ ] Assets, music, fonts and code in the build all have a recorded licence or are our own (provenance from `analiza-aplicatii.md` section 5 when a feature was studied elsewhere).

## 4. Red-team chains to rehearse (safe tests only)

Source: Codex research. Rehearse only in disposable repositories and with dummy secrets.

| # | Chain |
|---|---|
| 1 | Prompt injection from a web page or PDF into an agent with shell access |
| 2 | Poisoned dependency, MCP server or editor extension |
| 3 | Issue or PR injection into a CI agent |
| 4 | Token theft from config files or shell history |
| 5 | Browser-to-localhost attack on a local AI server |
| 6 | Compromised GitHub Action |
| 7 | AI-themed social engineering (fake tools, fake interviews) |
| 8 | Exfiltration through agent output (Markdown images, PRs) |
| 9 | Persistence via instruction or memory files |
| 10 | Agent impersonation across agents |

Highest-leverage controls across all ten:
- least privilege with short-lived credentials;
- isolated agents with default-deny network access;
- in the product: user confirmation for shell commands, writes, publishing and anything that touches secrets;
- pinned and reviewed dependencies, servers, extensions and actions;
- monitoring of diffs, workflow runs, secret scans and outbound connections.

