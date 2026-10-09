# Checkpoint — claude-wp-auth (LogAnalyzer WP-AUTH: smart-card sign-in, decision 33)

- **Task:** queue item 6b of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`; owner decision 33 and
  decision 17 in `docs/dfir/CONTRACT_AUDIT_STAGE1.md` §8.
- **Branch:** `loganalyzer/wp-auth-smartcard`. It already carries decision 33 and the queue entry. Main (with WP14a, #246) is merged in
  @ `2cd0049d`. Build on WP14a: `LogAnalyzer.Dfir.Core/Registers/UsersRegister.cs`, `RegisterIo.cs`, `RegisterCommon.cs`,
  the register audit chain, and the App `RegisterViewModel` / `RegisterView` (its edit banner says authentication is missing; replace it with
  the real role gate).

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## What the owner decided (decision 33, corrected 2026-10-09)
- Users sign in with a **contact** smart card inserted in the **smart-card slot of the keyboard** (not contactless) + card PIN.
  The card and the PIN are managed by SafeNet Authentication Client / SafeNet Authentication Tools.
- The **primary administrator may always sign in with account + password, without a card**. The password stays valid after
  they enrol a card; for them the card is optional, never required.
- Other accounts are created by the global administrator (decision 17) and use card + PIN only.

## Spec
1. **No custom crypto, no PIN handling.**
   - The app uses Windows smart-card support (SafeNet minidriver / CNG key storage provider, PC/SC reader built into the keyboard).
   - Sign-in sequence:
     - list certificates with a private key on smart-card providers (Windows `X509Store` My/CurrentUser, filtered to
       smart-card keys);
     - generate a random challenge;
     - sign it with the card key (`GetRSAPrivateKey`/`GetECDsaPrivateKey`; this triggers the SafeNet/Windows PIN prompt);
     - verify the signature with the certificate public key.
   - The app never sees, stores or logs the PIN.
   - A UID-only RFID badge is refused with a clear message: no private key means it is not accepted.
2. **Certificate trust, offline:**
   - Chain to the organisation CA certificates imported into the app (`%PROGRAMDATA%\LogAnalyzer\auth\trust\`).
   - Revocation by CRLs imported by the administrator. No network fetch in any edition (`X509RevocationMode.Offline`, custom
     root trust).
   - An expired or not-yet-valid certificate, a broken chain or a revoked certificate means sign-in is refused, with the reason.
   - A missing CRL raises a warning and is an administrator-configurable policy: default **refuse** on the classified edition,
     **warn** on unclassified. Record it under Blockers for the owner.
3. **Card → user mapping in the WP14a users register:**
   - Store certificate thumbprint + issuer + serial; never the private key.
   - One user can have several cards (replacement). Disabling a user or a card is immediate.
4. **Primary admin: account + password, always allowed (decision 33):**
   - First run with no users: create the primary admin with account + password.
     - Hash with PBKDF2-SHA256 (≥ 600k iterations), or Argon2id if a vetted library is already referenced.
     - Strong-password rule: length ≥ 14, not equal to the account name, not in a small embedded deny-list.
     - Lockout after N failures with back-off; every password sign-in is written to the auth audit.
   - The password **stays valid** after the admin enrols a card; card + PIN becomes an extra option for them.
   - Only the primary admin account can ever use a password. Any other account trying the password mode is refused.
   - Password change: requires the current password; it is audited.
   - Forgotten-password recovery: a documented offline procedure (re-initialise the auth store, audited). No silent backdoor.
5. **Roles:** global administrator and operator.
   - Gate the registers (media, users, procedure profile) and the admin actions (trust store, CRL import, user/card management)
     on the administrator role.
   - Everything else needs a signed-in user.
   - The classified edition has the same model.
6. **Audit:**
   - Every sign-in, failure, lockout, enrolment, card disable, role change, trust/CRL import and bootstrap-password retirement goes
     to a hash-chained auth audit (reuse `HashChain`): who, card thumbprint, reader, result, reason.
   - The signed-in identity is used as `Who` in the case custody/audit chains instead of `Environment.UserName`, where those
     are written.
7. **UI:**
   - Sign-in window before the main window: card mode (default), plus a password mode for the primary administrator only.
   - Card enrolment page for the signed-in user.
   - Admin page: users ↔ cards, trust store, CRLs.
   - Session lock after inactivity (configurable). A card-based session also locks when the card is removed from the keyboard slot.
     A password session (primary admin) locks on inactivity only.
8. **Testability:**
   - Abstract the card behind an interface. Unit tests use software certificates generated in tests (self-signed CA + user
     certificate with a private key) to prove challenge/verify, chain, revocation (test CRL), expiry and mapping. They also cover:
     the primary admin password valid both before and after card enrolment; password mode refused for every other account;
     lockout; role gates; card removal locking only card sessions.
   - Real SafeNet card tests are manual (lab checklist in docs).
9. **Docs:** `docs/dfir/AUTHENTICATION.md`:
   - setup with SafeNet Authentication Client, importing the CA and CRLs, enrolment;
   - recovery procedure;
   - what the app does and does not verify;
   - lab checklist.

## Verification required before push
- Build 0 errors.
- Dfir 48 / UI 6 known Linux-only failures only; Edition green.
- Full Python suite once on the final head → 0 failed.
- New .md files are listed in `20_TESTS/fixtures/unreadable_notes_allowlist.json`.
- Security gates are fixed only through their documented exception mechanism, never by obfuscation.

## Done
- 2026-10-09T13:25Z claude-orchestrator: WP14a merged (#246); main merged into this branch.
- 2026-10-09T12:30Z claude-orchestrator: owner decision 33 recorded; queue entry 6b; spec written; branch created.
- 2026-10-09T12:45Z claude-orchestrator: owner correction: contact card in the keyboard slot (not contactless); primary admin may
  always use account + password. Decision 33 and this spec updated.

- 2026-10-09 claude-wp-auth: items 1-9 implemented (commits on this branch). Core `LogAnalyzer.Dfir.Core/Auth/` (AuthService, CertificateTrust + offline CRL parser,
  PasswordRules, OperatorIdentity), real card provider `LogAnalyzer.Dfir.Windows/Auth/WindowsSmartCardProvider.cs`, UI `SignInWindow`, `AuthView`/`AuthViewModel`,
  `SessionGuard` (App/Services/AuthApp.cs), identity in case custody/audit + register audit, admin gates in `RegisterStore.Save` / `ProfileStore.Save`.
  Tests: `LogAnalyzer.Dfir.Tests/AuthTests.cs` (+ `AuthTestKit.cs`), 49 new. Docs: `docs/dfir/AUTHENTICATION.md`. Edition scanner green without any exception.

## Design notes (non-obvious)
- Cards and password live in a separate auth store (`%PROGRAMDATA%\LogAnalyzer\auth\`: accounts.json, primary_password.json, policy.json, trust/, crl/, auth_audit.jsonl),
  joined to the users register by account name (the admin page lists accounts with no register row). The register stays data-only and carries no credentials.
- Thumbprint stored is SHA-256 (not SHA-1). CRLs are parsed with System.Formats.Asn1 and verified with the .NET RSA/ECDSA classes; the chain uses custom root trust, downloads off, revocation NoCheck + our offline CRL lookup.
- accounts.json integrity: audited hash; mismatch refuses card sign-in, primary-admin password still works and can accept the state.
- Recovery: delete primary_password.json (password reset) or move accounts.json+primary_password.json (re-init); both audited. No code path resets anything.
- Registers/profile reads stay open to operators; saves need an unlocked administrator (core gate + UI notice).
- Not done / out of scope: OCSP, delta/indirect CRLs, real-card automated tests (manual lab checklist in the doc), ACL hardening of the auth folder (IT/installer).

## Next
- Orchestrator: open the PR, run CI.

## Blockers / owner questions
- Missing-CRL policy: default refuse (classified) / warn (unclassified). To be confirmed by the owner.
- Exact card model and keyboard reader model (for the lab checklist).
- PIN prompting frequency (every sign-in vs. cached) is a SafeNet/Windows policy; if the owner needs a PIN on every sign-in, SAC must be configured accordingly (the app does not change host settings).
- Should operators be able to READ the registers/profile (current behaviour) or should the pages be hidden for them?
- Auth folder ACL (administrators-only write) must be set by the installer/IT; the app does not change host settings.
