# Application updates on air-gapped stations — feasibility and usefulness (2026-10-11)

Owner question: "partea cu update-ul aplicației: vezi cât de fezabilă este și cât de utilă". Code facts are CODE_VERIFIED by grep on 2026-10-11;
estimates are marked as such.

## 1. What has to be updated, and how often

| Content | Changes how often | Where it lives today (verified) | Value of updating |
|---|---|---|---|
| Detection data: lists of RMM / remote tools, security agents, rule lists, air-gap lists, sequence rules | monthly (new tools, new abuse techniques; see RESEARCH_2026-10-11_AMENINTARI_ACTUALE.md) | `LogAnalyzer.Dfir.Core/Analysis/Data/*.json`, compiled in as embedded resources (`LogAnalyzer.Dfir.Core.csproj` line 16) — changing them today needs a new build | **high**: stale lists miss new tools |
| Plain-language catalogues (event meanings, MITRE descriptions, registry areas, check guides) | quarterly | C# code (`EventMeaning`, `ItemMeaning`, `CodeExplainer`, `CheckGuides`) | medium |
| Reference tables a controller compares against (supported Windows builds, vulnerable driver names, NSRL-style known software) | monthly–quarterly | not present yet | **high** for the control (S03 "Windows version" needs the table) |
| Application binaries (fixes, new screens) | per release | the published folder | high, but rarer; needs the most care |

## 2. Options

**A. Signed offline data pack (recommended first).** A ZIP brought on a registered medium, containing JSON data files plus a manifest
(pack id, version, created, file list with SHA-256) and an ECDSA P-256 signature over the manifest. The application:
1. verifies the signature with a public key compiled into the application;
2. refuses a version lower than or equal to the installed one (anti-downgrade);
3. verifies every file's SHA-256 against the manifest;
4. stores the pack next to the application data and loads its lists instead of the embedded ones; the embedded ones stay as fallback;
5. writes the import (who, when, pack version, SHA-256 of the ZIP, medium serial) to the audit chain, and every report prints the data pack version.

The verification pattern already exists: `EditionPolicy.Verify` (ECDSA P-256 with `ImportSubjectPublicKeyInfo`, plus a `highWaterVersion`
anti-downgrade check) and `release-gate/Sign-EditionPolicy.ps1` for signing. Effort (estimate): **S–M** (loader indirection for `Wp11Data`, pack
verifier, import screen with one button, tests). Risk: low — data only, no code is executed from the pack.

**B. Signed offline application update (binaries).** Same package format with the published application folder inside. Because the running
executable is locked, a small separate updater process (or a scheduled replace at next start) swaps the folder, keeps the previous version for
rollback, verifies the new version starts (`--self-test` already exists in `App.OnStartup`), and rolls back otherwise. Effort (estimate): **M–L**.
Risks: an update mechanism is itself an attack path (the signing key must be held offline by the owner, never on stations); on classified stations
installing software is usually an administrative procedure with approval and a register entry (link it to the transfer register and the update
register of the procedure profile, manual check M05).

**C. Online update (connected edition only).** A feed checked over HTTPS with the same signature. Useful only for the unclassified, connected
edition; never in the classified edition. Effort **M**; lowest priority.

**Not recommended:** ClickOnce/MSIX auto-update (needs a reachable server, not suited to air gap); unsigned packs; executing scripts from a pack.

## 3. Recommendation

1. Build option A now: it gives the largest benefit (current detection lists, Windows support table) for the smallest risk and effort.
2. Make every report state the application version, the data pack version and the SHA-256 of the executable (also a legal-research gap, G3).
3. Option B later, after the publish format is settled (Release, ReadyToRun, single file), with an owner-held offline signing key and a
   documented approval step.
4. Option C only if the connected edition is used in practice.

## 4. Open points
- Who holds the signing key and where (offline, owner). Key rotation: ship two public keys (current + next).
- Whether the organisation requires an approval record before importing a pack on a classified station (likely yes; one field on the import screen).
