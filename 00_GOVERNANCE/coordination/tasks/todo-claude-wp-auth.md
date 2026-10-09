# Checkpoint — claude-wp-auth (LogAnalyzer WP-AUTH: smart-card sign-in, decision 33)

- **Task:** queue item 6b of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`; owner decision 33 and
  decision 17 in `docs/dfir/CONTRACT_AUDIT_STAGE1.md` §8.
- **Branch:** `loganalyzer/wp-auth-smartcard`, from main @ `bc567d66`. It already carries decision 33 and the queue entry.
  **Start implementing only after WP14a merges** (it adds the users register this builds on); merge main first.

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## What the owner decided
- Everyone signs in with a contactless (RFID-frequency) smart card + card PIN. The card and the PIN are managed by
  SafeNet Authentication Client / SafeNet Authentication Tools.
- The primary administrator uses account + password only until enrolling their own card. After that the password is disabled
  for them, and they use card + PIN only.
- Other accounts are created by the global administrator (decision 17) and use card + PIN only.

## Spec
1. **No custom crypto, no PIN handling.**
   - The app uses Windows smart-card support (SafeNet minidriver / CNG key storage provider, PC/SC contactless reader).
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
4. **Bootstrap primary admin:**
   - First run with no users: create the primary admin with account + password, hashed with PBKDF2-SHA256 (≥ 600k iterations)
     or Argon2id if a vetted library is already referenced. Lockout after N failures with back-off.
   - After the admin enrols a card, password sign-in for that account is disabled permanently. Re-enabling needs a documented
     recovery procedure (two-person, logged). Propose it in docs; do not implement a silent backdoor.
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
   - Sign-in window before the main window: card mode, plus password mode only while the bootstrap admin has no card.
   - Card enrolment page for the signed-in user.
   - Admin page: users ↔ cards, trust store, CRLs.
   - Session lock after inactivity (configurable), and lock when the card is removed from the reader.
8. **Testability:**
   - Abstract the card behind an interface. Unit tests use software certificates generated in tests (self-signed CA + user
     certificate with a private key) to prove challenge/verify, chain, revocation (test CRL), expiry, mapping, the
     bootstrap → card transition, the password disabled after enrolment, lockout and role gates.
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
- 2026-10-09T12:30Z claude-orchestrator: owner decision 33 recorded; queue entry 6b; spec written; branch created.

## Next
- After WP14a merges: merge main, implement 1-9.

## Blockers / owner questions
- Missing-CRL policy: default refuse (classified) / warn (unclassified). To be confirmed by the owner.
- Exact card model and reader (needed for the lab checklist): IDPrime with a contactless interface? Which reader?
