# Officer signature and audit-log anchor (Standalone tamper evidence)

Date: 2026-10-16. Status: implemented, `TEST_VERIFIED` (LogAnalyzer.Dfir.Tests `OfficerAndAnchorTests`,
LogAnalyzer.App.Tests `OfficerKeyCliTests`, `OfficerControlPageTests`). Background: owner decisions of
2026-10-16 and `RESEARCH_2026-10-16_STANDARDS_STANDALONE_CLASSIFIED_WORKSTATIONS.md` sections 3-4.

## Problem

A local administrator of the station can read the DPAPI-LocalMachine key that seals the head of the
application's audit log, rewrite the log and reseal it, and can re-sign a modified report with the
station's report key. Neither is detectable from the station alone.

## Design

1. **Officer key.** `LogAnalyzer.KeyGen officer-keygen <file>` creates an ECDSA P-256 key encrypted with a
   passphrase (PBKDF2-SHA256 600000 + AES-256-GCM, envelope `LAOK1-PASS:`, different from the station
   report key so one cannot pass for the other). It lives on the officer's medium; there is no DPAPI or
   unencrypted variant. The app opens it only inside "Semnează ca ofițer de securitate"; the decrypted
   PKCS#8 bytes and the derived key are zeroed, the `ECDsa` object is disposed after signing.
2. **Official report.** The draft report carries "NESEMNAT DE OFIȚERUL DE SECURITATE — nu este raport
   oficial" on every page. Signing renders an `…_OFICIAL_<time>.pdf` copy whose last section names the
   officer and the key fingerprint, then writes a detached `<pdf>.officer.sig` over exactly those bytes,
   and records `report.officer_signed` in the sealed audit.
3. **Pinned verification.** `verify-report <pdf> --officer-fingerprint <fp>` and the in-app "Verifică
   raport" accept a signature only when the fingerprint of the key in the signature equals a pinned
   fingerprint (typed from the paper, or the trusted registered officers). A key made by anyone else gives
   UNKNOWN KEY, never VALID. Fingerprint = first 128 bits of SHA-256(SubjectPublicKeyInfo), 8 groups of 4 hex.
4. **Officer list.** Profile table "Ofițeri de securitate": the first officer is registered on trust and the
   fingerprint is shown to be written on the proces-verbal; every other officer needs an approval signed by
   an already trusted officer. Rows not reachable through valid approvals are not trusted, whatever the
   profile file says. Registration is audited (`profile.officer_registered`, plus `profile.edited`).
5. **Anchor code.** `AuditAnchor`: code = 19 base32 characters of
   SHA-256("LogAnalyzer-audit-anchor-v1|chainId|seq|hash") + 1 check character, shown as
   `XXXXX-XXXXX-XXXXX-XXXXX (înregistrarea nr. N)` on the report (section 6) with a copy button. "Copiază
   jurnalul pentru CD-R" copies the log, its seal file and an `ancorare.txt` to a chosen folder
   (`audit.anchor_exported`). At the next control the code typed from the signed paper is searched in the
   current chain (plain chain must also be intact): check **A11** CONFORM / NECONFORM ("jurnalul aplicației a
   fost rescris după controlul anterior") / NEDETERMINAT (no code or mistyped code).
6. **Windows tamper indicators** are always reported: A05 (Security 1102, System 104), A06 (4719, now also
   CONFORM when none), A07 (4616) and the new **A12** (Security 1100 not preceded by a requested shutdown,
   System 1074, within 30 minutes).

## What a malicious administrator can still do

- Stop the application, alter or kill Windows logging (no 1100 is written when the service is killed),
  change the OS before events are written; the anchor only proves the application's own log was not
  rewritten *before the anchored entry*.
- Rewrite every entry written *after* the last anchor (no external copy of those yet).
- Wipe the officer list and register a key of their own as "first officer": the app will then let that key
  sign. Detected only when the verifier pins the fingerprint from the earlier signed paper (or reads the
  officer list printed on the draft), which is why the paper fingerprint is the trust anchor.
- Edit `LastAnchorCode` in the profile to propose a code matching a rewritten log: the officer must type
  the code from the signed paper, not accept the proposal.
- Capture the officer's passphrase (keylogger) when the officer types it on the controlled station; the
  key file itself must never be left on the station. A smart card would remove this; not implemented.
- Passphrase strings from WPF `PasswordBox`/console cannot be zeroed in managed memory.
