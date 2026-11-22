# Security program

**Status:** active since 2026-11-22. Owner directive (standing): every program, site, service, agent or tool built here is protected against attackers and malicious input **before** it is called done.

**How this file was made:**
- Internet research by Codex, with sources, on five tracks: agent/MCP, web, Windows desktop/daemon/Python, supply chain/CI, red team. The tracks are listed in the Evidence section.
- A threat model of this repository's real code by `fable-consultant`.
- Opus synthesised both and checked the key claims independently.
- Second, independent source: Gemini (Hydrogen and Neon) on the same five tracks.
  - The first Gemini attempt failed: the agent bridge had disabled its transport, and a daemon restart fixed it.
  - The two sources were compared track by track. Agreements, disagreements and claims that could not be verified are in section 5.
  - Opus checked the key claims independently.

Statuses: **DONE** (implemented and tested, branch named), **OPEN** (planned), **OWNER** (needs an owner decision).

---

## 1. Release gate: must pass before anything is called done

For every deliverable, whatever its type:
1. **Threat model.** Written down: assets, trust boundaries, entry points, top attack paths with file:line. For non-trivial work, `fable-consultant` reviews it.
2. **Probes before code.** A threat model / probe list is written before implementation, and the reviewer gets the same list (CLAUDE.md hard rule).
3. **Tests first.** Every security control has a test that failed before the control existed.
4. **Independent review.** A `vault-reviewer` security pass, with at most two rounds; after round 2 the main session decides and records any residual.
5. **No secrets.** None in code, history, client bundles or logs. Gitleaks and GitHub push protection are on.
6. **Dependencies.** Locked with versions (hashes where the ecosystem supports them). Dependency review passes on the PR, and the OSV scan shows no new high or critical finding.
7. **CI of the change.** Green, including `Supply-chain security` (actionlint, zizmor) and the existing CodeQL, gitleaks, security-boundary and untrusted-content gates.
8. **Residuals.** Every residual is written in the PR and in this file. "Done" is never claimed with a critical item open.

The per-type checklists below add to this gate.

## 2. Threat model of this repository (fable-consultant, 2026-11-22)

**Assets:**
- ACTIVE/verified notes (what agents treat as true) and the owner's attestation.
- Note confidentiality (inbox, private, RAW).
- Audit logs.
- `hmac.key`, the REST and Telegram tokens, the GitHub token.

**Trust boundaries:**
- The owner and the local agents run as the **same Windows user**, so there is no OS boundary between them (`04_CONFIG/access_policy.yaml` says so itself).
- Agents reach the vault through the MCP and CLI tools, which bind only the tools.
- Imported content passes through git review and CI.
- Cloud agents are limited by egress ceilings on `vault_*`.
- On the network side: a loopback REST gateway, Telegram, and loopback Ollama.

| # | Attack path | Status |
|---|---|---|
| 1 | The REST gateway bypassed the agent read contract: `/note` served RAW, ARCHIVED and quarantined notes; `/search` ignored the query; `/chat` put those notes into the prompt and posted to an unchecked `OLLAMA_HOST`; `/status` leaked the vault path. | **DONE**, `claude/security-hardening`: reads via `memory_access`, loopback-only model host, no proxy, secret required at start. `memory_search` no longer lists a quarantined note at all. |
| 2 | Owner spoofing: the principal is an unauthenticated argument everywhere, and the runtime authority layer (`ApprovalBroker`) is not wired. | **OWNER**: wire `ApprovalBroker` with a user-presence key (Windows Hello / WebAuthn / FIDO2), or a separate OS account for the key and the audit head. |
| 3 | Persistent prompt injection through `memory_propose`: the body was scanned only at read time, and the rate limit lived per process, keyed on a self-declared name. | **IN PROGRESS**, `claude/security-hardening`: scan at write time with quarantine; rate limit persisted per principal with a cross-process lock. |
| 4 | Audit log integrity: an unkeyed chain with no cross-process lock, and `str(e)` leaks text into the log. | **OPEN**: cross-process lock with a re-read of the last hash; record the exception type and a digest; anchor the head outside agent reach. |
| 5 | Imported skills under `01_KNOWLEDGE/EXTERNAL_SKILLS` are indexed by storage; a file with frontmatter `lifecycle: ACTIVE` would claim trust. Latent today. | **OPEN**: exclude it from storage roots; the importer refuses frontmatter `id`/`lifecycle`. |
| 6 | Secrets on disk: the HMAC secret is copied into the environment of child processes; the ACL check matches English group names; `icacls` is resolved by name. | **OPEN**: check by SID, call System32 `icacls` by absolute path. |
| 7 | Telegram/Ollama assistant: well defended. Gaps: the token file ACL is unchecked on Windows; one regular expression can backtrack cubically. | **OPEN** (low). |
| 8 | Denial of service: an unbounded `Content-Length` on the gateway; `vault_access` reads a file before checking its size; no size cap at index time. | **OPEN**. |
| 9 | CI is mostly sound (all actions pinned to SHA, no `pull_request_target`). The owner-approval environment is only as strong as its reviewers; the `import-external-skills` job holds write tokens. | **DONE (part)**, `claude/security-program`: default-deny permissions on the two workflows that lacked them; actionlint, zizmor, dependency review, OSV and Scorecard added. |
| 10 | Storage follows junctions in its recursive glob. Needs local write access, so it adds little. | **OPEN** (low). |

Fixed earlier in this program, and part of the record:
- An agent could rewrite an attested note into ACTIVE+verified (PR #398, content-hash binding).
- CRLF corruption of note files (PR #398).
- The owner split tool had to refuse unverified parents (branch `claude/ake-split-apply`).

## 3. Checklists by product type

### 3.1 LLM agents, memory, MCP servers
Sources: OWASP LLM Top 10 2025, OWASP Agentic AI Threats & Mitigations, OWASP MCP Security Cheat Sheet, NIST AI 100-2 E2025, MCP Security Best Practices, MINJA (NeurIPS 2025), AgentPoison (NeurIPS 2024).
- [ ] Retrieved, imported and proposed text is **data, never authority**. State changes are enforced in code, never by the model. There is no lifecycle or verification change from text.
- [ ] No instruction-only guardrail or LLM-as-judge is used as a security boundary. These are supplementary signals only.
- [ ] Narrow tools only (search, get, propose); arguments validated; paths canonicalised; minimum fields returned; secrets never placed in prompts or tool output.
- [ ] Proposals from agents are scanned at write time; non-SAFE text is quarantined; rate limits are persistent and per principal.
- [ ] MCP servers use **stdio** for local clients. An HTTP server binds loopback only, validates `Host` and `Origin`, authenticates every request, and runs a patched SDK. Example: CVE-2025-66416 (DNS rebinding in the MCP Python SDK before 1.23.0) was verified on NVD.
- [ ] Tool descriptions and server configuration are pinned and reviewed; a change requires consent (rug-pull defence).
- [ ] Authority actions (attest, promote, approve) need fresh user presence bound to the exact content hash. See item 2 of the threat model.
- [ ] Tests: adversarial fixtures (instruction override, encoded, multilingual, multi-turn) prove that no state changes and no withheld content is served.
- [ ] **Agent-output exfiltration (both sources).** No external Markdown images, links or raw HTML in rendered agent output. No outbound tool in a session that read private notes. A canary-secret test. Real case: EchoLeak, CVE-2025-32711, zero-click exfiltration from Microsoft 365 Copilot; verified.
- [x] **Unicode canonicalisation before scanning.** NFKC, HTML unescape until stable, format characters stripped (soft hyphen, zero-width), Cyrillic/Greek/Latin look-alikes folded, line breaks collapsed. Done for `memory_propose` and the read flag in PR #401. Still open: tag characters U+E0000-E007F, bidi controls, and a CI check on instruction and skill files.
- [ ] **Separation over filtering (Gemini).** The stronger defence is capability / information-flow separation (CaMeL, FIDES preprints): untrusted text never selects tools or destinations. A lexical scanner stays a signal.
- [ ] **Separate, audience-bound tokens per caller** (MCP Security Best Practices). Test that an agent token gets 403 on owner routes.
- [ ] **Poisoning by crowding (NIST AI 100-2).** Test whether near-duplicate poisoned notes push trusted notes out of the top results.

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
- [ ] Local daemons: loopback only; `Host` and `Origin` validated; token on every request, stored with DPAPI or a restrictive ACL, never in URLs or logs; refuse to start if the bind fails (port squatting); named pipes get an explicit DACL.
- [ ] Python: `yaml.safe_load`, never pickle from others, `subprocess` without `shell=True`, atomic writes, LF line endings (fixed in PR #398 and branch `claude/lf-writers-audit-log`), `pip --require-hashes` or `uv.lock`.
- [ ] Gate: .NET security analyzers (CA2300, CA2326 for Newtonsoft `TypeNameHandling`, CA3003, CA3006, CA3011), BinSkim on PE files, Bandit/Semgrep, pip-audit/OSV, malicious-archive tests, daemon tests (forged Host, foreign Origin, no or invalid token).
- [ ] **Process start-up:** `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`. Never launch `.bat`/`.cmd` with arguments: `shell=False` does not protect them on Windows (BatBadBut class). Call `.exe` files directly.
- [ ] **Local listeners:** bind with `SO_EXCLUSIVEADDRUSE` (port squatting). Named pipes get `PIPE_REJECT_REMOTE_CLIENTS` and `FILE_FLAG_FIRST_PIPE_INSTANCE`. Check regularly that nothing listens on 0.0.0.0 (MCP Inspector, CVE-2025-49596).
- [ ] **Secrets and DPAPI (resolved disagreement).** DPAPI does **not** isolate a secret from other processes of the same Windows user, and the owner and agents share one user here (section 2). Never put tokens in command-line arguments or inherited environments. Real isolation needs a user-presence key or a separate account (threat-model item 2).
- [ ] **Signed updaters** verify the hash and Authenticode before running anything.

### 3.4 Supply chain, GitHub and CI
Sources: GitHub Actions secure-use docs, OpenSSF Scorecard, SLSA v1.1, Sigstore, CISA ESF; incidents tj-actions/changed-files (2025), xz-utils (2024), npm worm campaigns (2025); Snyk ToxicSkills (2026: 76 confirmed malicious skills out of 3,984 audited, verified on snyk.io).
- [x] Every action pinned to a full SHA. Verified on all 50 workflows; enforced by `30_SCRIPTS/verification/workflow_security_audit.py`.
- [x] No `pull_request_target` or `workflow_run`. Verified.
- [x] Default-deny `permissions` on every workflow, granted per job. The last two were fixed on `claude/security-program`.
- [x] actionlint and zizmor in CI: new workflow `supply-chain-security.yml`; both clean today.
- [x] Dependency review on PRs (fails on high), an OSV scan (report-only until the imported-project backlog is triaged), and OpenSSF Scorecard weekly.
- [ ] Branch ruleset on `main`: PRs only, required checks, no force-push. **OWNER:** a human reviewer for merges, since an agent approving its own PR is not review. Also enable secret scanning push protection in the repository settings.
- [ ] Agents with repository write access: issue, PR and comment text is adversarial input; no secrets in agent jobs; agents never merge.
- [ ] Imported skills and instruction packs are quarantined: static review, no execution on ingest, a pinned checksum, promotion only by the owner (the existing ingestion pipeline does this; keep it).
- [ ] Changes to instruction files (CLAUDE.md, AGENTS.md, skills, memory) are reviewed as security-sensitive (red-team chain 9). A pre-commit check rejects zero-width and bidi characters in them (Rules File Backdoor class). Integrity alerts cover `.claude/`, `.mcp.json` and environment overrides of API base URLs.
- [x] **Dependabot for GitHub Actions** on `claude/security-program`: weekly, grouped, 7-day cooldown (the GitHub default is 3 days since July 2026; verified in the GitHub docs). Before this, nothing refreshed the SHA pins. **OWNER:** version updates for the other ecosystems (pull-request volume from the imported projects).
- [ ] **Dependency intake:**
  - `npm ci --ignore-scripts` with `minimumReleaseAge`;
  - NuGet Package Source Mapping with `RestoreLockedMode=true`;
  - never a bare `--extra-index-url`.
- [ ] **Actions:** template values reach `run:` only through `env:`; the OIDC `sub` claim is bound to a branch or environment; `harden-runner` egress allowlists on jobs that run agents; gate on individual Scorecard checks, not the aggregate score.
- [ ] **Releases:** SBOM plus provenance attestations for artefacts (SLSA 1.1, PyPI attestations).

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
- human approval for shell commands, writes, publishing and anything that touches secrets;
- pinned and reviewed dependencies, servers, extensions and actions;
- monitoring of diffs, workflow runs, secret scans and outbound connections.

## 5. Evidence

| Track | Bridge task id | Agents |
|---|---|---|
| Agent/MCP | `1439ede3-862e-452a-9fba-974702ab5d7c` | Codex done; Gemini Argon failed |
| Web | `d4351968-c17f-479d-a7ec-6b471eeec833` | Codex done; Gemini Argon failed |
| Desktop / daemon / Python | `2008c8d7-d3f5-42cd-8edf-ffcc72f1e2d3` | Codex done; Gemini Argon failed |
| Supply chain | `c53f9119-6da8-4d9c-b87f-f60d9767fb07` | Codex done; Gemini Argon failed |
| Red team | `2fcd80b9-deee-464c-b064-9137829e11bc` | Codex done; Gemini Hydrogen failed |
| Gemini Flash retry | `b88597ff-dcf2-436a-b8e9-1e52e79a2675` | failed with the same transport error |
| Gemini, after the daemon restart | `195de1d9` (agent/MCP, Hydrogen), `c5a2e206` (web, Neon), `3b80aa57` (desktop, Hydrogen), `1780c71f` (supply chain, Neon), `19213199` (red team, Hydrogen) | all completed |

**Where the two sources agree:** every core control in section 3.

**Disagreements, resolved:**
- **DPAPI:** Gemini is right for this threat model (one shared Windows user).
- **`os.replace` atomicity on Windows:** Codex is closer to the Python docs, which promise it for POSIX only; test on the real filesystem.
- **Signed commits:** the difference is emphasis only.
- **Blanket human approval vs. sandboxes:** Gemini warns of approval fatigue. Keep human approval for authority and publishing actions, and sandbox the rest.
- **CSP `base-uri 'none'` vs `'self'`:** both are acceptable.

**Not used as fact (unverified or wrong):**
- Gemini's red-team links that resolve only through search redirects.
- CVE-2024-24576, which Gemini attributes to Python; it is a Rust standard-library CVE, though the Windows batch-file risk is real.
- CVE-2026-21852, CVE-2025-68146, CVE-2025-66414, ATLAS AML.T0099/T0110, "Trusted Types in Firefox 148", and MCP spec version "2026-07-28".
- Vendor statistics such as MINJA success rates, Veracode's 45% and Snyk's 280+.

Independent checks by Opus (web search, 2026-11-22):
- the OWASP Top 10:2025 order;
- CVE-2025-66416 / GHSA-9h52-p55h-vw2f (fixed in MCP Python SDK 1.23.0);
- Snyk ToxicSkills numbers;
- CVE-2025-32711 (EchoLeak, Microsoft 365 Copilot, CVSS 9.3);
- CVE-2025-29927 (Next.js middleware bypass);
- the Dependabot `cooldown` option, with a 3-day default since 2026-07-14.

The threat model was checked against the code before each fix: path 1 was reproduced by failing tests.
