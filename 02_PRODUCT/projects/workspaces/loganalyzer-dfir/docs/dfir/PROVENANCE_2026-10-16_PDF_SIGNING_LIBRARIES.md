# Provenance check: PDF signing libraries for LogAnalyzer (2026-10-16)

Scope: library provenance gate for adding a digital signature (target: PAdES baseline B-B,
optionally B-T) to PDF reports that LogAnalyzer already renders with QuestPDF, on
`net10.0-windows` WPF builds for air-gapped classified stations.

Gate applied: no Russian / Chinese / North-Korean origin, no sanctioned entity (including
transitive dependencies); licence must allow closed/internal use (no GPL/AGPL).

Method: research only. Nothing was downloaded, installed or executed. Sources are public pages
(nuget.org, GitHub repositories and the GitHub Advisory Database via its API, vendor legal
notices, OpenSanctions search). Evidence labels: `DOCUMENT_VERIFIED` (read on the cited page),
`CODE_VERIFIED` (read in the upstream source at the cited tag), `CLAIMED_ONLY`, `UNVERIFIED`.

---

## 1. System.Security.Cryptography.Pkcs

| Field | Finding | Evidence |
|---|---|---|
| Latest stable | 10.0.13 (listed 2026-10-13); 11.0.0-rc.2 is prerelease | DOCUMENT_VERIFIED, <https://www.nuget.org/packages/System.Security.Cryptography.Pkcs> |
| Publisher / owners | Microsoft (nuget owners: `Microsoft`, `dotnetframework`) | DOCUMENT_VERIFIED, same page |
| Country | United States (Microsoft Corporation) | public record |
| Source | .NET runtime (`dotnet/runtime`, built from the `dotnet/dotnet` VMR) | DOCUMENT_VERIFIED, <https://github.com/dotnet/dotnet>, <https://github.com/dotnet/runtime> |
| Licence | MIT | DOCUMENT_VERIFIED, nuget page |
| Deps for net10.0 | **none** | DOCUMENT_VERIFIED, nuget page |
| Deps for net8.0 (only relevant to other TFMs) | Microsoft.Bcl.Cryptography >= 10.0.13, System.Formats.Asn1 >= 10.0.13 (both Microsoft, MIT) | DOCUMENT_VERIFIED, nuget page |
| In shared framework? | **Yes for WPF**: `System.Security.Cryptography.Pkcs.dll` ships inside `Microsoft.WindowsDesktop.App` 10.x (not inside `Microsoft.NETCore.App`). `System.Formats.Asn1.dll` ships inside `Microsoft.NETCore.App`. A `net10.0-windows` WPF project can use `SignedCms`, `CmsSigner`, `Rfc3161TimestampRequest` with **no PackageReference**. | inspected a local .NET 10 runtime install (file listing only) |
| Advisories | GHSA-846f-f764-76mx / CVE-2026-84008 (HIGH, DoS in `SignedCms` signature checking), 10.0.0 - 10.0.12, fixed 10.0.13. GHSA-2fmv-rv2c-59x7 / CVE-2026-70162 (MODERATE, DoS in `EnvelopedCms.Decode` on Windows), 10.0.0 - 10.0.12, fixed 10.0.13. Same CVEs hit 8.0.0 - 8.0.1 (fixed 8.0.2) and 9.0.0 - 9.0.20 (fixed 9.0.21). Older: CVE-2023-29331 (6.x/7.x). | DOCUMENT_VERIFIED, GitHub Advisory DB (GraphQL `securityVulnerabilities`, package `System.Security.Cryptography.Pkcs`), <https://github.com/advisories/GHSA-846f-f764-76mx>, <https://github.com/advisories/GHSA-2fmv-rv2c-59x7> |
| Package signing | Microsoft packages are author-signed (Microsoft Corporation) plus nuget.org repository signature | CLAIMED_ONLY; check offline with `dotnet nuget verify --all <nupkg>` before importing into the air-gapped feed. <https://learn.microsoft.com/nuget/reference/signed-packages-reference> |
| Sanctions | Microsoft is not a listed entity | not a realistic concern; not screened further |

**Verdict: ACCEPT** (use the framework copy; no PackageReference needed for WPF).

Conditions:
- The station runtime (`Microsoft.WindowsDesktop.App`) must be **10.0.13 or later**. Because the
  assembly comes from the shared framework, the CVE fix arrives through the runtime servicing
  update, not through NuGet. A self-contained / folder deployment must be published with SDK
  servicing that carries runtime 10.0.13+. At least one local development runtime seen during
  this check was below 10.0.13 and should be updated.
- If any non-WPF project (for example a plain `net10.0` Core library) uses CMS, add an explicit
  `PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.13"` there.
- Exposure is mainly on *verification* of untrusted CMS (the HIGH CVE is in signature checking).
  LogAnalyzer verifying its own reports is lower risk, but the fixed runtime is still required.

---

## 2. PDFsharp (empira) and variants

### 2.1 Publisher and licence

| Field | Finding | Evidence |
|---|---|---|
| Packages | `PDFsharp` (core, netstandard2.0/net8/9/10), `PDFsharp-WPF` (net8/9/10-windows7.0, net462), `PDFsharp-GDI`; MigraDoc variants | DOCUMENT_VERIFIED, <https://www.nuget.org/packages/PDFsharp>, <https://www.nuget.org/packages/PDFsharp-WPF/6.2.4> |
| Owner on nuget | `PDFsharp-Team` | DOCUMENT_VERIFIED |
| Legal entity | empira Software GmbH, Kirchstr. 19, 53840 Troisdorf, Germany; Amtsgericht Siegburg HRB 12057; VAT DE 811 655 791; managing director listed on the legal notice | DOCUMENT_VERIFIED, <https://www.pdfsharp.com/LegalNotice> |
| Country | Germany (EU) | same |
| Sanctions | No match for "empira" on OpenSanctions (aggregates EU, US OFAC, UK, UN lists), checked 2026-10-15 | DOCUMENT_VERIFIED, <https://www.opensanctions.org/search/?q=empira>. Official screening (EU Sanctions Map, OFAC SLS, UK OFSI) was not run: UNVERIFIED there |
| Repository | <https://github.com/empira/PDFsharp>, created 2023-04, ~1k stars, 24 commits, contributors: the `PDFsharp-Team` account (company: empira Software GmbH) and one individual. It is a **release mirror** (one code drop per release); development is private, so outside review of changes is limited. | CODE_VERIFIED via GitHub API |
| **Licence <= 6.2.4** | **MIT** (`LICENSE` at tag `v6.2.4`; nuget shows MIT for 6.2.4) | CODE_VERIFIED, `LICENSE@v6.2.4`; DOCUMENT_VERIFIED, <https://www.nuget.org/packages/PDFsharp/6.2.4> |
| **Licence >= 6.5.0 and 7.0.0-preview-2** | **AGPL-3.0 / commercial dual licence** ("PDFsharp and MigraDoc are published under a Dual License consisting of AGPL and a commercial license"). nuget shows `AGPL-3.0-only` for 6.5.0 (released 2026-10-14). | CODE_VERIFIED, `LICENSE@v6.5.0`; DOCUMENT_VERIFIED, <https://docs.pdfsharp.net/General/License/License.html> |
| Release cadence | 6.0.0 (2023-11), 6.1.0 (2024-05), 6.1.1 (2024-07), 6.2.0 (2025-05), 6.2.1-6.2.3 (2025-07..11), 6.2.4 (2026-01-06), 6.5.0 (2026-10-14, licence change), 7.0.0-preview-2 (2026-10-13) | DOCUMENT_VERIFIED, nuget version list |
| Advisories | None in the GitHub Advisory DB for `PDFsharp`, `PdfSharp`, `PDFsharp-MigraDoc` | DOCUMENT_VERIFIED (GraphQL query, 2026-10-15) |
| Package signing | nuget.org repository signature only is expected; author signature not confirmed | UNVERIFIED; run `dotnet nuget verify --all` offline |

### 2.2 Transitive dependencies (6.2.4, all TFMs identical)

| Dependency | Version floor | Publisher / country | Licence | Advisories |
|---|---|---|---|---|
| System.Security.Cryptography.Pkcs | >= 8.0.1 | Microsoft / US | MIT | **8.0.1 is inside the vulnerable range** of CVE-2026-84008 and CVE-2026-70162 (fixed 8.0.2). On `net10.0-windows` the shared framework copy (10.0.x, higher assembly version) wins at runtime, but NuGet audit will still flag the 8.0.1 node; pin `10.0.13` explicitly to clear it. |
| Microsoft.Extensions.Logging.Abstractions | >= 8.0.3 | Microsoft / US (owners Microsoft, aspnet, dotnetframework) | MIT | none |
| -> Microsoft.Extensions.DependencyInjection.Abstractions | >= 8.0.2 | Microsoft / US | MIT | none |
| (netstandard2.0 only) System.Buffers, System.Memory | 4.5.x | Microsoft / US | MIT | not relevant for net10.0 |

Evidence: <https://www.nuget.org/packages/PDFsharp/6.2.4>,
<https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/8.0.3>. Answer to
"does it pull System.Security.Cryptography.Pkcs": **yes**, and its default signer is built on
`SignedCms`.

### 2.3 Signature capability (6.2.x)

| Requirement | PDFsharp 6.2.4 | Evidence |
|---|---|---|
| API | `DigitalSignatureHandler.ForDocument(document, IDigitalSigner, DigitalSignatureOptions)`; built-in `PdfSharpDefaultSigner(X509Certificate2, PdfMessageDigestType, Uri? tsa)` in `PdfSharp.Cryptography`; Bouncy Castle signer only as a repository snippet, not in the package | DOCUMENT_VERIFIED, <https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Signatures.html> |
| External signer (smart card / CNG key) | Yes in principle: the default signer passes the `X509Certificate2` to `CmsSigner`, so a certificate whose private key lives in a smart-card KSP works the same way it works with `SignedCms`; a custom `IDigitalSigner` can do anything | CODE_VERIFIED (`PdfSharpDefaultSigner.cs`) |
| `/SubFilter` | **`/adbe.pkcs7.detached`** (Filter `/Adobe.PPKLite`) | CODE_VERIFIED (`Pdf.Signatures/DigitalSignatureHandler.cs`) |
| signing-certificate-v2 (ESS) | **Not added** | CODE_VERIFIED: only `Pkcs9SigningTime` is added, and as an *unsigned* attribute |
| PAdES B-B (ETSI EN 319 142-1) | **No.** Baseline PAdES requires `/SubFilter /ETSI.CAdES.detached` and a signed `signing-certificate-v2`; PDFsharp produces neither. Would need a custom `IDigitalSigner` (for the attribute) **and** a patched handler (for SubFilter). | derived from the two rows above |
| B-T (RFC 3161 signature-time-stamp) | Yes, online only: `Rfc3161TimestampRequest` posted over `HttpClient` to a TSA URI; not supported on .NET Framework builds. Not usable on an air-gapped station without an internal TSA. | CODE_VERIFIED |
| Visible appearance | Yes: `DigitalSignatureOptions.Rectangle` + `AppearanceHandler` (XForm drawn with XGraphics) | DOCUMENT_VERIFIED |
| Incremental update / multiple signatures | Not documented; the handler patches placeholders in a full rewrite of the document. No incremental-save API found. Fine for one signature on a freshly generated report, not for counter-signatures. | CODE_VERIFIED (placeholder patching), DOCUMENT_VERIFIED (docs silent) |
| Re-serialisation | PDFsharp would re-open and re-write the QuestPDF output, so the signed file is no longer byte-identical to the one QuestPDF produced (matters if the unsigned PDF's SHA-256 is already in custody) | design consequence |

**Verdict:**
- **PDFsharp >= 6.5.0 / 7.x: REJECT** (AGPL; commercial licence would be a purchase decision).
- **PDFsharp 6.2.4 (last MIT): NEEDS-OWNER-DECISION**, leaning against. Provenance is clean
  (German GmbH, Microsoft-only dependencies, no advisories), but: it is a frozen line (the vendor
  moved to AGPL; future fixes will not be MIT), it does not produce PAdES baseline without patching
  its handler, and it rewrites the whole PDF. Adopting it means owning a fork of a frozen MIT line
  for the signing part anyway.

---

## 3. Alternatives

| Option | Publisher / country | Licence | Verdict | Reason |
|---|---|---|---|---|
| iText (`itext` 9.8.0, `itext7`) | Apryse Group NV (copyright line on nuget); iText originated in Belgium, Apryse is headquartered in Canada | AGPL-3.0 or commercial | **REJECT** | AGPL for closed/internal use is excluded by the gate; also drags `Microsoft.Extensions.DependencyModel`, `System.Security.Cryptography.Xml` etc. <https://www.nuget.org/packages/itext> |
| BouncyCastle.Cryptography 2.7.0 | Legion of the Bouncy Castle Inc. (Australia); nuget owner `LegionOfTheBouncyCastle` | MIT-style (MIT X Consortium based; bundled bzip2 under Apache-2.0) | **ACCEPT if needed** (not needed for B-B) | No dependencies on any TFM. Advisories CVE-2024-29857/30171/30172 fixed in 2.3.1; 2.7.0 clean. OpenSanctions: no match. Only useful for ASN.1/TSP convenience; .NET 10 already has `Rfc3161TimestampRequest`, `AsnWriter`, `SignedCms`. <https://www.nuget.org/packages/BouncyCastle.Cryptography>, <https://www.bouncycastle.org/about/license/> |
| Own incremental-update signer over QuestPDF output | in-house | n/a | **RECOMMENDED** | see 3.1 |

### 3.1 Feasibility of writing our own PAdES B-B incremental update

The report is already a finished QuestPDF file. Signing appends one incremental update to it, so
the original bytes stay untouched (its SHA-256 in custody remains valid as a prefix of the signed
file). Only framework crypto is needed (`System.Security.Cryptography.Pkcs` from
`Microsoft.WindowsDesktop.App`, `System.Formats.Asn1` from `Microsoft.NETCore.App`).

Minimal content of the appended section:

1. **Parse the tail only**: last `startxref`, the trailer (`/Root`, `/Size`, `/Info`, `/ID`), the
   catalog object and the object of the page that will carry the widget. Check whether the file
   uses a classic `xref` table or a cross-reference stream (PDF 1.5); the update must use the same
   kind. QuestPDF renders through Skia's PDF backend; confirm the actual form with a test fixture
   before relying on either.
2. **New objects** (numbers from `/Size` upward):
   - signature dictionary: `/Type /Sig /Filter /Adobe.PPKLite /SubFilter /ETSI.CAdES.detached
     /ByteRange [0 a b c] /Contents <000...0> /M (D:...) /Name /Reason /Location`. The
     `/ByteRange` placeholder is fixed width; `/Contents` is a hex placeholder sized for the CMS
     plus headroom (e.g. 16-32 KiB for RSA-3072 with chain).
   - signature field + widget merged: `/FT /Sig /T (Signature1) /V <sig> /Type /Annot /Subtype
     /Widget /Rect [...] /F 132 /P <page>` and an `/AP /N` XObject for the visible appearance (or
     `/Rect [0 0 0 0]` for an invisible signature).
   - updated page object: the original page dictionary copied with `/Annots` extended by the
     widget.
   - updated catalog: original catalog plus `/AcroForm << /Fields [field] /SigFlags 3 >>`.
3. **Cross-reference section** for only the new/changed objects, trailer with `/Prev` = old
   `startxref`, same `/Root` number, new `/Size`, and the new `startxref` / `%%EOF`.
4. **Byte range**: hash everything except the `/Contents` hex string (including its `<` `>`),
   then write the real `/ByteRange` values into the fixed-width placeholder.
5. **CMS**: `SignedCms(new ContentInfo(digestSource), detached: true)` with `CmsSigner`
   (`SubjectIdentifierType.IssuerAndSerialNumber`, SHA-256, `IncludeOption =
   X509IncludeOption.WholeChain` or end-cert). Signed attributes: content-type and
   message-digest (added by .NET automatically) plus **signing-certificate-v2**
   (OID 1.2.840.113549.1.9.16.2.47, `SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF
   ESSCertIDv2 }`, `ESSCertIDv2` = SHA-256 hash of the signer certificate DER, optionally
   `issuerSerial`), encoded with `AsnWriter`. **No `signing-time` signed attribute** (PAdES
   baseline forbids it; the claimed time goes in `/M`). Smart card: an `X509Certificate2` whose
   key is in the card's CNG KSP; Windows shows the PIN prompt. Hex-encode the result into
   `/Contents`, zero-padded.
6. **Optional B-T**: `Rfc3161TimestampRequest.CreateFromSignerInfo` against an *internal* TSA,
   token added as unsigned attribute 1.2.840.113549.1.9.16.2.14. On a standalone air-gapped
   station there is normally no TSA, so B-B with the audit-log anchor code is the realistic target.

Size estimate: roughly 400-700 lines of C# plus tests. The risky part is the parser for the
existing file's tail (xref table vs stream, object streams, compressed page objects); restricting
input to PDFs LogAnalyzer itself produced, and asserting the expected structure, keeps it
bounded. Validation: open in a PAdES validator offline (e.g. EU DSS demo run on a separate
connected machine with non-classified fixtures only) and in Adobe Reader; round-trip test that
the original bytes are an exact prefix of the signed file.

---

## 4. Recommendation

1. **System.Security.Cryptography.Pkcs: ACCEPT**, taken from the `Microsoft.WindowsDesktop.App`
   shared framework; require runtime **10.0.13+** on every station (CVE-2026-84008,
   CVE-2026-70162).
2. **PDFsharp >= 6.5.0: REJECT** (AGPL since 2026-10-14). **PDFsharp 6.2.4: NEEDS-OWNER-DECISION**,
   not recommended: frozen MIT line, not PAdES baseline (`adbe.pkcs7.detached`, no ESS
   signing-certificate-v2), full rewrite instead of incremental update.
3. **iText: REJECT** (AGPL/commercial).
4. **BouncyCastle.Cryptography 2.7.0: ACCEPT only if needed**; not needed for PAdES B-B.
5. **Build a small in-house PAdES B-B incremental signer** over the QuestPDF output using only
   framework crypto. It adds no new third-party dependency, keeps the original report bytes as a
   verifiable prefix, and supports smart-card keys through `X509Certificate2`/CNG.

Open items (UNVERIFIED): official sanctions screening via EU Sanctions Map / OFAC SLS / UK OFSI
for empira Software GmbH and Legion of the Bouncy Castle Inc.; `dotnet nuget verify` of every
nupkg before it enters the offline feed; whether QuestPDF 2026.7.2 output uses a classic xref
table or a cross-reference stream.
