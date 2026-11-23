# Liste de verificare pe tip de produs

Sursa: programul de securitate al AI Memory Vault (cercetare Codex + Gemini, threat model fable-consultant, 22.11.2026). Bifează doar cu test sau dovadă reală; sursele citate au fost verificate la momentul scrierii, reverifică versiunile și CVE-urile la fiecare proiect nou.

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
- [ ] **Unicode canonicalisation before scanning.** NFKC, HTML unescape until stable, format characters stripped (soft hyphen, zero-width), Cyrillic/Greek/Latin look-alikes folded, line breaks collapsed. Done for `memory_propose` and the read flag in PR #401. Still open: tag characters U+E0000-E007F, bidi controls, and a CI check on instruction and skill files.
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
- [ ] Every action pinned to a full SHA. Verified on all 50 workflows; enforced by `30_SCRIPTS/verification/workflow_security_audit.py`.
- [ ] No `pull_request_target` or `workflow_run`. Verified.
- [ ] Default-deny `permissions` on every workflow, granted per job. The last two were fixed on `claude/security-program`.
- [ ] actionlint and zizmor in CI: new workflow `supply-chain-security.yml`; both clean today.
- [ ] Dependency review on PRs (fails on high), an OSV scan (report-only until the imported-project backlog is triaged), and OpenSSF Scorecard weekly.
- [ ] Branch ruleset on `main`: PRs only, required checks, no force-push. **OWNER:** a human reviewer for merges, since an agent approving its own PR is not review. Also enable secret scanning push protection in the repository settings.
- [ ] Agents with repository write access: issue, PR and comment text is adversarial input; no secrets in agent jobs; agents never merge.
- [ ] Imported skills and instruction packs are quarantined: static review, no execution on ingest, a pinned checksum, promotion only by the owner (the existing ingestion pipeline does this; keep it).
- [ ] Changes to instruction files (CLAUDE.md, AGENTS.md, skills, memory) are reviewed as security-sensitive (red-team chain 9). A pre-commit check rejects zero-width and bidi characters in them (Rules File Backdoor class). Integrity alerts cover `.claude/`, `.mcp.json` and environment overrides of API base URLs.
- [ ] **Dependabot for GitHub Actions** on `claude/security-program`: weekly, grouped, 7-day cooldown (the GitHub default is 3 days since July 2026; verified in the GitHub docs). Before this, nothing refreshed the SHA pins. **OWNER:** version updates for the other ecosystems (pull-request volume from the imported projects).
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

