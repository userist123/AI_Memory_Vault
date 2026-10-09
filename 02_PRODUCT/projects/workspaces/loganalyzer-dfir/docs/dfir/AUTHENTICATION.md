# LogAnalyzer authentication (WP-AUTH, owner decision 33)

Status: implemented in code and covered by unit tests with software certificates (`LogAnalyzer.Dfir.Tests/AuthTests.cs`).
The real SafeNet card + keyboard reader path is **not** exercised by automated tests; use the lab checklist at the end.

## What the owner decided
- Users sign in with a **contact smart card** inserted in the **smart-card slot of the keyboard** plus the **card PIN**.
  The card and the PIN are managed by **SafeNet Authentication Client / Tools**. The application never sees the PIN.
- The **primary administrator** may **always** sign in with **account + password**, without a card, also after enrolling a card.
- Every other account uses **card + PIN only**. Accounts are created by the global administrator (decision 17).
- Roles: **global administrator** and **operator**. Same model in the classified and the unclassified edition.

## How sign-in works

### Card + PIN (all accounts)
1. The application asks Windows for certificates in the **current user** `My` store whose private key lives on a smart card
   (the Windows Certificate Propagation service puts the certificates of an inserted card there; the SafeNet minidriver / smart-card
   key storage provider holds the key). Nothing else is read; the host is not changed.
2. A certificate without a card key, or a tag that carries no certificate (a **UID-only RFID badge**), is refused with a message:
   no private key means it is not accepted.
3. The certificate must be enrolled for an account (SHA-256 thumbprint + issuer + serial) and neither the account nor the card may be disabled.
4. **Offline trust** (below) must pass. This happens before the PIN prompt, so a refused certificate never asks for a PIN.
5. The application generates a random challenge (`LogAnalyzer-card-signin-v1`, 32 random bytes, UTC ticks), asks the card to sign it
   (RSA PKCS#1 v1.5 or ECDSA, SHA-256, through the .NET `RSA` / `ECDsa` classes). **Windows / SafeNet shows the PIN prompt here.**
6. The signature is verified with the public key of the certificate. Only then is a session created.

There is no custom cryptography: certificate parsing, chain building, signature verification and hashing are .NET (X509, CNG) APIs.
The only hand-written parser is the CRL reader (`System.Formats.Asn1`), whose signature check again uses the .NET RSA/ECDSA classes.

### Account + password (primary administrator only)
- Always available to the primary administrator, with or without an enrolled card, with or without a card in the slot.
- Any other account (including other administrators) trying this mode is refused (`password_mode_not_allowed`) and the attempt is audited.
- Password: PBKDF2-SHA256 (.NET `Rfc2898DeriveBytes`), 600 000 iterations, random 16-byte salt, constant-time comparison.
  Rules: at least 14 characters, not equal to the account name, not in the embedded deny-list of common passwords.
- Lockout: after N failed attempts (default 5, configurable 3..20) password sign-in is locked for 30 s, doubling per consecutive lockout
  up to **15 minutes**. The lock **always expires on its own**, so the primary administrator is never permanently locked out. A card
  (if enrolled) keeps working during a password lockout. Failures against unknown accounts never lock the primary account and are
  indistinguishable from wrong passwords.
- Changing the password needs the current password and is audited. Passwords and PINs are never written to any file or log.

## Setup

### 1. First start
On the first start, with no account, the sign-in window asks for the **primary administrator**: account name, person and password.
Nothing is created before that. The password stays valid after a card is enrolled.

### 2. SafeNet Authentication Client
- Install SafeNet Authentication Client (SAC) / Tools on the station and confirm in SafeNet Authentication Client Tools that the card is
  seen in the keyboard reader and that its certificate is visible. The card PIN, PIN policy, PIN change and unblocking are managed there.
- Confirm the certificate appears in `certmgr.msc` → Personal → Certificates for the signed-in Windows user while the card is inserted.
- PIN prompting (every use vs. cached) is a SafeNet / Windows policy, not an application setting. Owner decision 34: the **PIN is required at
  every card sign-in and unlock**, so configure SAC without PIN caching / single logon. IT applies it; the application does not change host settings.

### 2b. Auth folder permissions (IT, at deployment; decision 34)
Write access to `%PROGRAMDATA%\LogAnalyzer\auth\` must be limited to Administrators and SYSTEM, because anyone who can write there can reset the
authentication store. LogAnalyzer does not change host settings, so IT runs this once, from an elevated prompt, after the first start has created the folder:

```
icacls "%PROGRAMDATA%\LogAnalyzer\auth" /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-545:(OI)(CI)RX"
```

(S-1-5-32-544 = Administrators, S-1-5-18 = SYSTEM, S-1-5-32-545 = Users, read only.)

Every sign-in writes the auth audit, so LogAnalyzer is started from **Windows accounts in the local Administrators group** (live collection
needs it anyway; the manifest `highestAvailable` elevates them). Started from a standard account, the sign-in window refuses with the cause
("folderul de autentificare nu poate fi scris") and nobody is signed in.

### 3. Trust: import the organisation CA
Sign in as administrator → **Autentificare și conturi** → **Importă certificat CA…**. Import the root CA certificate of the organisation
(and the issuing CA if there is one; DER or PEM). Only certificates with `CA:true` are accepted.
They are copied to `%PROGRAMDATA%\LogAnalyzer\auth\trust\`.
The Windows root store is **not** used: a card certificate is accepted only if its chain ends at a CA imported here.

### 4. Revocation: import CRLs (offline)
Export the CRL(s) of the issuing CA on a connected machine (or from the CA) and carry them over; **Importă CRL…** (DER or PEM).
Rules enforced at import: the CRL must be signed by an imported CA; delta CRLs are refused; a CRL **older** than the installed one is refused (no roll-back).
Files go to `%PROGRAMDATA%\LogAnalyzer\auth\crl\`. **Nothing is fetched from the network in any edition.**
The administrator must re-import before `nextUpdate`; an expired CRL counts as a missing CRL.

**Missing-CRL policy** (administrator-configurable; the default depends on the edition):
| Edition | Default | Effect |
|---|---|---|
| Classified | **Refuse** | no current CRL for an issuer in the chain = sign-in refused (`crl_missing`) |
| Unclassified | **Warn** | sign-in allowed, a warning is shown and audited |

### 5. Users and cards
- **Autentificare și conturi** → create the account (DOMAIN\user, role operator or administrator). It can use a card only.
- Insert the user's card, **Detectează cardul**, select the certificate, **Înrolează…** for the account. The card signs a challenge
  (the user types the PIN) - this proves possession of the key. Stored: thumbprint (SHA-256), issuer, serial, subject. Never the key.
- One user can have several cards (replacement). A signed-in user can enrol an additional card for their own account.
- Disabling a user or a card is immediate: a live session of that account/card ends at the next check (every 2 s) and sign-in is refused.
- The primary administrator cannot be disabled or demoted from the application.
- Clearances and need-to-know stay in the **users register** (decision 17); the admin page lists accounts that have no row there.

## Roles
| | Operator | Global administrator |
|---|---|---|
| Analysis, cases, reports, read of registers/profile | yes | yes |
| Edit/import/save media register, users register, procedure profile | **no** | yes |
| Users, cards, roles, trust store, CRLs, authentication parameters | no (own card enrolment only) | yes |
The gate is enforced in `RegisterStore.Save` and `ProfileStore.Save` (core), not only in the UI; the authentication service re-checks the role on every admin call.

## Session lock
- Inactivity lock (default 10 min, 1..480) for **every** session.
- A **card session also locks when the card is removed** from the keyboard slot. A **password session** (primary administrator) locks on inactivity only.
- While locked, the main window is hidden. Unlock repeats the session's own method (card + PIN, or the password); another person's card cannot unlock it.

## Audit
`%PROGRAMDATA%\LogAnalyzer\auth\auth_audit.jsonl` is a hash chain (the same `HashChain` as the case custody). Each line: event, who, account, card
thumbprint (first 16 hex), reader, result, reason, time, detail. Events: sign-in (ok/failed/refused), lockout, session lock/unlock/end, primary admin
creation, password set/changed/recovered, account created/enabled/disabled, role change, card enrolled/disabled/enabled, trust and CRL import,
parameter change, denied actions, integrity accepted, store re-initialised. The signed-in account is also written as **who** in the case custody
and audit chains and in the register audit (instead of the operating-system account). Note: "bootstrap-password retirement" does not apply -
the primary administrator password is permanent by design (decision 33).

`accounts.json` is protected by an audited hash: if it is edited outside the application, **card sign-in is refused**, the primary administrator can still
sign in with the password (a warning is shown) and **accepts the current state** on the admin page (audited).

## Recovery (forgotten primary administrator password) - offline, audited, no backdoor
The application contains no reset code path. Recovery needs **operating-system administrator access to the station** and is visible in the audit:
1. Close LogAnalyzer.
2. Delete `%PROGRAMDATA%\LogAnalyzer\auth\primary_password.json` (back it up first if policy requires).
3. Start LogAnalyzer. The window **Recuperare parolă** appears (accounts and enrolled cards are untouched). Set a new password.
   The audit records `auth.password_recovered`.

Full re-initialisation (accounts lost, cards must be enrolled again): close the application and move `accounts.json` and `primary_password.json` out of the folder
(**keep** `auth_audit.jsonl`, `trust\` and `crl\`). The next start is a first run; the audit records `auth.store_reinitialised`.

## What the application does and does not verify
Does:
- the signature of a random challenge by the card key against the certificate public key; the certificate chain to an imported CA (custom root trust, downloads off);
  validity period at the time of sign-in; key usage digital signature and EKU client authentication / smart-card logon when those extensions are present;
  revocation against imported CRLs (signature of the CRL, `thisUpdate`/`nextUpdate`, serial lists).

Does not:
- the PIN (Windows / SafeNet do), PIN retry counters and card blocking (SafeNet), card cloning protection beyond the key never leaving the card;
- OCSP (never; no network), delta CRLs, indirect CRLs / issuing-distribution-point scoping, name constraints beyond what the .NET chain engine enforces;
- the integrity of the station: a person with operating-system administrator rights can replace `accounts.json` and the password file together
  (detected for `accounts.json` by the audited hash, **not** for a replaced password file). The auth folder should be writable only by administrators (set by the installer / IT, not by LogAnalyzer);
- that the card holder is the person in the users register (the administrator binds them at enrolment).

## Lab checklist (manual; real SafeNet card and keyboard reader)
Record the card model and the keyboard/reader model in the test report (decision 34: any PKI contact card supported by the SafeNet minidriver, PC/SC keyboard reader).
1. SafeNet Authentication Client Tools shows the card in the keyboard reader and the user certificate; certmgr shows it under the user's Personal store.
2. First start: create the primary administrator; password sign-in works.
3. Import the organisation CA and a current CRL. Enrol the primary administrator's card; sign out; card sign-in works (PIN prompt is the Windows/SafeNet one).
4. Password sign-in of the primary administrator still works with **no card** in the slot, and with the card enrolled.
5. Create an operator, enrol an operator card. Operator card sign-in works; operator cannot open the edit buttons of the registers/profile, cannot import CA/CRL.
6. Operator password attempt is refused with the card-only message.
7. Wrong PIN / cancel the PIN prompt: no session; wrong PIN three times: SafeNet behaviour (card block) as configured by SAC.
8. Remove the card from the keyboard slot during an operator session: the window hides within ~2 s; reinsert and unlock with PIN. Repeat in a password session: nothing locks.
9. Revoke the operator certificate on the CA, import the new CRL: sign-in refused (`revoked`). Expired CRL: classified build refuses, unclassified warns.
10. A UID-only RFID badge / a card without certificate: refused with the "UID" message.
11. Five wrong passwords: lockout message; it expires; the card still works during it.
12. Disable the operator card while the session is open: the session ends within ~2 s.
13. Verify the audit chain on the admin page (valid), edit one byte of `auth_audit.jsonl`: reported broken.
14. Recovery procedure above, on a test station.
15. Auth folder permissions (section 2b): as a standard Windows user, creating or deleting a file in `%PROGRAMDATA%\LogAnalyzer\auth\` is denied.
16. PIN on every sign-in (decision 34): sign out and sign in again with the card; the PIN is asked again (no SafeNet PIN cache).
