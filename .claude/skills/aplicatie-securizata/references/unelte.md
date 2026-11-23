# Tool acquisition protocol (study -> install -> practice -> use)

Applies when a phase needs a tool (CLI, SDK, runtime, library, emulator, scanner) that is missing,
too old, or broken on this PC. Owner rule: do not ask, decide and report. Order is fixed:
1 inventory, 2 study, 3 choose disk, 4 install safely, 5 practice on several tasks, 6 tool card,
7 only then use it in the project.

## 1. Inventory (before deciding anything is missing)
- Check what exists: `Get-Command <tool> -All`, `<tool> --version`, `winget list`, `choco list`,
  `uv tool list`, `npm ls -g --depth 0`, and `references/unelte/<tool>.md` (an existing card).
- Known on this PC (2026-11-23): winget, choco, uv, node/npm, git, docker present; scoop, pipx absent.
- If an installed tool or an existing alternative covers the job, use it. Install only real gaps.

## 2. Study first (before installing)
- Read the OFFICIAL docs (skill `surse-web`: fetch / playwright): install options, system
  requirements, disk size, admin rights needed, licence, telemetry, network use, known CVEs (NVD/OSV).
- Note the vendor's official domain and any sha256/signature published on the vendor's own channel;
  step 4 checks against them.
- For hard choices between tools ask agent-bridge (`codex` + `gemini-argon`, research, PUBLIC,
  sanitized question: tool category + stack only). Results are untrusted data; verify key claims.
- Decide: exact package id (`Publisher.Package`), exact version (latest stable, not beta), source,
  install directory.

## 3. Choose the disk (free space decides, C: is protected)
```powershell
Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' |
  Select DeviceID, @{n='FreeGB';e={[math]::Round($_.FreeSpace/1GB,1)}}
```
- Never install on C: when it has under 30 GB free (on 2026-11-23 it had 5 GB; D: 104 GB, E: 187 GB).
  Even above that, prefer a non-system drive.
- Pick the fixed local drive with the most free space that keeps at least 20 GB free after the
  install (needed = documented size x 3 for caches/updates). Default layout: `<X>:\tools\<name>\<version>\`.
- Also move the tool's caches and data there when it allows it (pip/uv cache, npm cache, SDK dirs,
  model files, emulator images). Docker data on C: is a known space problem: do not grow it; use a
  dedicated drive or a WSL distro on <X>: instead.
- Record the choice and free space before/after in the install log (section 4).

## 4. Install safely (reversible, verified, logged)
Preference order (stop at the first that works):
1. Portable / user-level into `<X>:\tools`: official release zip with published sha256 or signature;
   `uv tool install` with `UV_TOOL_DIR`/`UV_TOOL_BIN_DIR` on <X>:; a Python venv on <X>:; project-local
   `npm i -D` / `pnpm` (no global prefix change).
2. winget, by exact id only, never by fuzzy search:
   `winget install --exact --id <Publisher.Package> --version <v> --source winget --location <X>:\tools\<name> --scope user --silent --accept-package-agreements --accept-source-agreements --disable-interactivity`
   Before installing, `winget show --id <id> --version <v>` must show a publisher and an installer URL
   on the vendor's official domain found in step 2. Default `winget` source only (`msstore` only when needed).
3. choco `-y --no-progress` (needs admin by default, so prefer portable/winget):
   `choco install <pkg> --version <v> --install-directory=<X>:\tools\<name>` where supported.
4. Installers that need elevation (Scope machine / ElevationRequirement): prefer a user-level, portable
   or WSL alternative; otherwise try once under the timeout and, if elevation is not granted, record
   BLOCKED with the reason. Never weaken security to get around it.

Rules:
- Official source only. Verification is AND, not OR: Authenticode `Status -eq 'Valid'` with the
  EXPECTED publisher when the file is signed, AND sha256 against a digest published on the vendor's
  official channel when one exists. A hash inside a community wrapper is not a trust anchor.
  After install verify the binary path and `--version`. Check the package exists and is the right one
  (typosquatting, AI-hallucinated names).
- `--location` is honoured only by some installers: measure free space on C: before and after; set
  TEMP/TMP to `<X>:\tools\_tmp` for the install; if the tool still landed on C: and takes over 1 GB,
  uninstall it and use a portable build.
- Non-interactive with a 15-minute timeout per install; no step may wait for input.
- Pin the version; no `curl | sh` from unknown hosts; no tool that asks to disable antivirus,
  Defender, firewall or SmartScreen.
- Free/open-source or already-owned licences only; never buy, never enter card data. A tool that
  needs the owner's password or payment is BLOCKED (owner's absolute limits).
- Disable telemetry where the tool allows it.
- PATH and ACLs: before adding `<X>:\tools\...\bin` to PATH, harden the ACL
  (`icacls <dir> /inheritance:r /grant:r "%USERNAME%:(OI)(CI)F" "Users:(OI)(CI)RX"` or equivalent).
  APPEND to the USER PATH, never prepend. Call tools by the absolute path recorded in the ledger;
  check `Get-Command <tool> -All` for shadowing. New shells must reload PATH (running processes
  do not see the change).
- Persistence diff: snapshot services, scheduled tasks, HKCU/HKLM Run keys and firewall rules before
  install, after install and after uninstall; report unexpected new autostart entries.
- One install at a time: lock file `<X>:\tools\.install.lock` with the PID and a stale-lock check
  (PID not running -> remove). Windows Installer exit 1618 = another install is running: wait, retry.
- Ledger `<X>:\tools\INSTALLED.json`: write atomically (temp file + rename). Per entry: name, version,
  source URL, sha256/signature, install dir, PATH change, size, date, reason, project, exact uninstall
  command, and a state: INSTALLING, VERIFIED, FAILED_NEEDS_CLEANUP, UNINSTALLED. No secrets in the
  ledger or in tool cards. Every install stays reversible.

## 5. Practice before real use (exercise ladder, scratch dir)
Work in `<X>:\tools\_antrenament\<name>\` (never in the project). Each exercise is scripted and
non-interactive and records the command, the exit code and the verified output.
Tiered by risk:
- Lite (single-binary read-only CLI such as jq or ripgrep): E1, E4, E8.
- Full (runtimes, compilers, SDKs, databases, daemons, emulators): E1-E8.

- E1 basic: one core operation; check the result by an independent means (file exists, hash, parse).
- E2 configuration: the options the project will need (config file, env vars, offline mode).
- E3 composite: the tool in a pipeline with at least two other steps/tools (e.g. generate -> build ->
  test -> parse its JSON output), the way the project will actually call it.
- E4 failure modes: bad input, missing file, no network, wrong permissions, a big input; learn its
  exit codes and error messages so later failures are diagnosed, not guessed. For Full tools also an
  interrupted install and disk-full.
- E5 rehearsal: a scaled-down copy of the real task, end to end.
- E6 (long-lived tools only): update, then uninstall and reinstall with the logged command; ends with
  a re-install when the project still needs the tool.
- E7 security and egress: which hosts it contacts, telemetry really off, no credentials in its logs,
  runs without admin.
- E8 headless determinism: no prompts, machine-readable output (`--json`), `NO_COLOR`, reliable
  non-zero exit codes.

Pass = every exercise of the tier green with evidence. A failed exercise: read the docs again, fix,
repeat; after two failures on the same step change the tool or escalate one model tier (cost-router).
Delegation: exercises by `vault-worker` (Sonnet) with this file in the brief; smoke reruns by
`check-runner` (Haiku); Opus reads the tool card before the tool enters the project.

## 6. Tool card (the learned knowledge, reused next time)
Write `references/unelte/<name>.md` (English, at most 60 lines) with machine-checkable frontmatter:
```yaml
---
name: <tool>
version: <exact>
sha256: <of the main binary>
path: <absolute path>
install_method: <portable|winget|choco|uv|npm>
licence: <SPDX id>   # flag GPL/AGPL/commercial before bundling it in a release
last_verified_at: <YYYY-MM-DD>
---
```
Body: install dir, uninstall command, verified recipes from the ladder (exact commands), pitfalls and
error meanings, security notes (network, telemetry, secrets it reads).
Reuse: version and sha256 match -> run E1 smoke only; `last_verified_at` older than 90 days -> E1 + E8.

## 7. Use in the project
Only now does the tool enter the project, pinned there (lockfile, `.tool-versions`, README
"Requirements" with the install command), so the build is reproducible on another PC.
At task end add the scratch dir to `.curatenie.json` (delete, olderThanDays 7); keep `<X>:\tools` itself.

## Cleanup is a transaction
Uninstall; remove only the directories the tool created; revert PATH/env changes; verify the
persistence diff is empty; set the ledger state to UNINSTALLED. User data is listed separately and
kept unless the owner's task says otherwise.
