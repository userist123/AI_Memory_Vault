# Research 2026-10-12: library provenance for LogAnalyzer

Status: research only. Nothing in this document has been added to a project, and no
package was restored, built or executed. Evidence states follow the repository
convention: `DOCUMENT_VERIFIED` (read from a primary source), `CODE_VERIFIED` (read
from package metadata or a signature), `CLAIMED_ONLY` (secondary source), `UNVERIFIED`.

## 1. The rule being applied

A third-party library may ship in LogAnalyzer only if neither the library nor any
transitive dependency is of Russian, Chinese or North Korean origin, and none is
banned or sanctioned by another state. A dependency whose publisher or maintainers
are anonymous, or whose origin cannot be established, counts as **NOT CLEARED**
until someone establishes it.

How this document applies the rule:

- "Origin" means the publisher who controls the NuGet package (the owner accounts
  recorded inside nuget.org's repository signature) and the maintainers who hold
  copyright and merge code. Small external pull requests are listed as residual
  risk (section 6), not counted as origin.
- Dependencies were walked recursively from each package's `.nuspec`, using the
  dependency group closest to `net10.0` (net10.0 > net9.0 > net8.0 > ... >
  netstandard2.0), at the minimum version each range declares.
- Packages authored and author-signed by Microsoft Corporation (`System.*`,
  `Microsoft.*`, `runtime.*`, `NETStandard.Library`) are treated as one origin
  (Microsoft Corporation, United States) and are not listed one by one. On
  `net10.0` most of the old `System.*` 4.x packages are pruned by the SDK because
  the shared framework already contains them.

## 2. Method and evidence

| Step | What was done | Evidence |
|---|---|---|
| Package ids and latest versions | `https://api.nuget.org/v3-flatcontainer/<id>/index.json` | CODE_VERIFIED |
| Metadata and dependency tree | Downloaded `.nuspec` files only, parsed them as data with `python -I`, walked recursively (185 package versions in total) | CODE_VERIFIED |
| Signatures | Fetched only the `.signature.p7s` entry from each `.nupkg` with HTTP range requests (no DLLs downloaded), decoded the certificates and the `nuget-package-owners` attribute with `openssl asn1parse` | CODE_VERIFIED |
| Publish date and hash | nuget.org registration and catalog entries (`packageHash`, SHA-512) | CODE_VERIFIED |
| Vulnerabilities | OSV batch query (`api.osv.dev/v1/querybatch`, ecosystem NuGet) for all 185 versions; NuGet catalog `vulnerabilities` and `deprecation` fields for the candidate packages | CODE_VERIFIED |
| Sanctions | Downloaded and searched, as data, the full lists below for every author, maintainer and organisation name in this document (whole-word, case-insensitive). The matcher was checked against known positives (the CSL contains 3 rows for a Russian security vendor and 140 for a Chinese telecom vendor) | CODE_VERIFIED |
| Origin of people and organisations | GitHub user API (`name`, `company`, `location`), maintainers' own sites, licence pages, signing-certificate subjects | DOCUMENT_VERIFIED unless marked |

Sanctions lists searched (downloaded 2026-10-12):

| List | Source | Version | SHA-256 of the file searched |
|---|---|---|---|
| US OFAC SDN | `https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/SDN.CSV` | Last-Modified 2026-10-09, 19 417 rows | `64be7a7991059f59a2ebb43bc5dcc7e2951e1e27cbed8cbe41b0231b3a161946` |
| US Consolidated Screening List (SDN, BIS Entity List, Denied Persons, Unverified, MEU, ITAR debarred, CMIC, SSI, ...) | `https://data.trade.gov/downloadable_consolidated_screening_list/v1/consolidated.csv` | 2026-10-12 (Entity List: 3 419 rows) | `f76d7b34f233b53b01331f2414a26baae6e1d0b7bf447cc70dba7d1653ec3090` |
| EU consolidated financial sanctions list | `https://webgate.ec.europa.eu/fsd/fsf/public/files/csvFullSanctionsList_1_1/content?token=dG9rZW4tMjAxNw` | generated 2026-09-22 | `ba8fbd1b114e7b1a73a11004f440c397ec331b6892c683293d6ba848a59864ef` |
| UN Security Council consolidated list | `https://scsanctions.un.org/resources/xml/en/consolidated.xml` | generated 2026-10-10 | `0687d6d9b6c631d81a6f02579594236912dd655c8bd0e1e25628b2b9babad546` |
| UK OFSI consolidated list (legacy format) | `https://ofsistorage.blob.core.windows.net/publishlive/2022format/ConList.csv` | "Last Updated 03/06/2026". This file appears stale; re-check against the current UK Sanctions List before release | `9585695391b51f72ca9eae1b6671e1f81fb1fa6e21a1661dad346636f2320c7f` |

Names searched: Zimmerman, ZimTech, Ziąbek/Ziabek, QuestPDF, SourceGear, Eric Sink,
Josh Close, Skinner, Aubry, Edward Cooke, Blumhardt, Datalust, Serilog, Bisbjerg,
Lagerkvist, LTR Data, Kuschnik, Bellot, ServiceStack, Pierrain, Dupuydauby, Palotas,
Jangli, Normuradov/Nurmuradov, Quamotion, Veigel, Kenneth Bell, .NET Foundation,
force-net, Zetetic, libtom. **Result: zero hits on every list.**

Government software bans checked by name: the US prohibition on a Russian
antivirus vendor's software (BIS Final Determination, 89 FR 52434, June 2024), the
FCC Covered List (`https://www.fcc.gov/supplychain/coveredlist`) and NDAA section 889
(Chinese telecom and video-surveillance vendors). None of these vendors appears
anywhere in either dependency tree.

## 3. Candidates: Eric Zimmerman's forensic libraries

### 3.1 Which libraries exist on NuGet

| Requested artefact | NuGet id | Latest version | Published | Notes |
|---|---|---|---|---|
| Registry hives | `Registry` | 2026.5.0 | 2026-04-26 | netstandard2.0 |
| LNK | `Lnk` | 2026.5.3 | 2026-05-05 | netstandard2.0 |
| Jump lists | `JumpList` | 2026.5.2 | 2026-05-05 | netstandard2.0 |
| Shell items / extension blocks | `ExtensionBlocks` | 2026.5.0 | 2026-04-26 | netstandard2.0 |
| GUID names | `GuidMapping` | 2026.5.0 | 2026-04-26 | netstandard2.0 |
| OLE compound files | `OleCf` | 2026.5.0 | 2026-04-26 | netstandard2.0 |
| Prefetch | `Prefetch` | 2026.5.2 | 2026-04-27 | netstandard2.0 |
| $MFT, $J, $LogFile, $Boot, $SDS | `MFT` | 2026.5.1 | 2026-04-26 | netstandard2.0 |
| EVTX | `evtx` | 1.5.2 | (older) | netstandard2.0. The GitHub repo is active (pushed 2026-06-17) but no newer package has been published |
| RecentFileCache.bcf | `RecentFileCache` | 1.5.2 | 2023-07-07 | net6.0 / netstandard2.0. Package is stale; repo pushed 2026-04-30 |
| Amcache.hve | `Amcache` | 2026.5.3 | 2026 | netstandard2.0 |
| AppCompatCache (ShimCache) | `AppCompatCache` | 1.2.4 | (older) | netstandard2.0 |
| ShellBags | not published as a library | n/a | n/a | No `ShellBags` package. ShellBag parsing can be built on `Registry` + `ExtensionBlocks` + `GuidMapping` |
| SRUM | not published as a library | n/a | n/a | `srum`/`SrumECmd` packages do not exist. SRUDB.dat is an ESE database and would need a separate ESE reader |
| LECmd/SQLECmd | not published as libraries | n/a | n/a | Command-line tools only |

All of them target netstandard2.0 (RecentFileCache also net6.0), so they load on
.NET 10.

### 3.2 Publisher

- **Eric Zimmerman**, GitHub `EricZimmerman`, company "ZimTech4n6", location
  Indianapolis, United States (`https://api.github.com/users/EricZimmerman`,
  `https://ericzimmerman.github.io`). DOCUMENT_VERIFIED.
- Every package below lists `Eric Zimmerman` or `Eric R. Zimmerman` as author and
  copyright holder, and the only NuGet owner inside the repository signature is
  `EricRZimmerman`. CODE_VERIFIED.
- Licence: `MIT` (SPDX expression in the nuspec, matching the GitHub repo licence).
- Every nuspec records the exact source commit, for example `Registry` 2026.5.0
  comes from `https://github.com/EricZimmerman/Registry` at tag `2026.5.0`, commit
  `15203c155e68bbe69729186011b343f25f4239f9`. These SHAs make a build from source
  possible (section 7).
- Signature: **repository-signed only**, by "NuGet.org Repository by Microsoft"
  (DigiCert chain). No author signature.

### 3.3 Per-package table

Third-party dependencies are listed by name. "MS" means Microsoft-authored packages
only.

| Package | Version | Author / org | Country | Licence | Signed? | Transitive dependencies (non-Microsoft) | Sanctions | Vulns (OSV) | Verdict |
|---|---|---|---|---|---|---|---|---|---|
| Registry | 2026.5.0 | Eric Zimmerman | US | MIT | Repo only | Serilog 4.2.0; MS: System.Text.Encoding.CodePages 9.0.0 | none | none | **CLEARED** |
| Lnk | 2026.5.3 | Eric Zimmerman | US | MIT | Repo only | ExtensionBlocks 2026.5.0, GuidMapping 2026.5.0, Serilog 4.3.1; MS: CodePages 10.0.7 | none | none | **CLEARED** |
| ExtensionBlocks | 2026.5.0 | Eric Zimmerman | US | MIT | Repo only | GuidMapping (>= 1.3.0); MS: CodePages 9.0.0 | none | none | **CLEARED** |
| GuidMapping | 2026.5.0 | Eric Zimmerman | US | MIT | Repo only | none | none | none | **CLEARED** |
| OleCf | 2026.5.0 | Eric Zimmerman | US | MIT | Repo only | none | none | none | **CLEARED** |
| JumpList | 2026.5.2 | Eric Zimmerman | US | MIT | Repo only | Lnk 2026.5.3, OleCf 2026.5.0, ExtensionBlocks, GuidMapping, Serilog 4.3.1 | none | none | **CLEARED** |
| Prefetch | 2026.5.2 | Eric Zimmerman | US | MIT | Repo only | Serilog 4.3.1 | none | none | **CLEARED** |
| MFT | 2026.5.1 | Eric R. Zimmerman | US | MIT | Repo only | Serilog 4.3.1 | none | none | **CLEARED** |
| RecentFileCache | 1.5.2 | Eric Zimmerman | US | MIT | Repo only | none | none | none | **CLEARED** (package stale since 2023; the artefact only exists on Windows 7 / Server 2008 R2) |
| evtx | 1.5.2 | Eric R. Zimmerman | US | MIT | Repo only | **Crc32.NET 1.2.0**, FluentValidation 11.11.0, Serilog 4.2.0, ServiceStack.Text 8.5.2, YamlDotNet 16.3.0; 36 MS packages incl. System.Xml.XPath 4.3.0 -> System.Text.RegularExpressions 4.3.0 | none | Transitive System.Text.RegularExpressions 4.3.0: GHSA-cmhx-cq75-c4mj (pruned on net10, but flagged by NuGet audit) | **NOT CLEARED**: Crc32.NET's author self-declares Russia (section 3.4) |
| Amcache | 2026.5.3 | Eric Zimmerman | US | MIT | Repo only | **AlphaFS.New 2.3.0**, ERZHelpers 2026.5.3, LTRData.DiscUtils.* 1.0.80, LTRData.Extensions 1.0.22, RawDiskLib 0.3.0, DeviceIOControlLib 0.1.6, WmiLight 7.2.0, CsvHelper 33.1.0, NFluent 3.1.0, Registry 2026.5.0, Serilog; 77 MS packages | none | none | **NOT CLEARED**: AlphaFS.New is published by an anonymous account (section 3.4). Also pulls a test-assertion library (NFluent), CSV writer, raw-disk and WMI code into a parser |
| AppCompatCache | 1.2.4 | Eric Zimmerman | US | MIT | Repo only | **AlphaFS.New 2.3.0**, ERZHelpers 1.5.2, RawDiskLib 0.2.1, LTRData.DiscUtils.* 1.0.49, WmiLight 6.10.0, NFluent 3.1.0, **Registry 1.5.0** (old); 111 MS packages incl. NETStandard.Library 1.6.1 -> System.Net.Http 4.3.0 | none | Transitive System.Net.Http 4.3.0: GHSA-7jgj-8wvc-jh57 | **NOT CLEARED**: AlphaFS.New; also pins an old Registry and drags vulnerable 1.x-era packages |
| ERZHelpers | 2026.5.3 / 1.5.2 | Eric Zimmerman (NuGet author field "ERZHelpers") | US | MIT | Repo only | AlphaFS.New 2.3.0, DeviceIOControlLib, LTRData.DiscUtils.Ntfs, RawDiskLib, WmiLight, Serilog | none | none | **NOT CLEARED** (only reached through Amcache/AppCompatCache; inherits AlphaFS.New) |

### 3.4 Third-party dependencies of the candidates

| Package | Version | Author / org (NuGet owner in signature) | Country | Licence | Signed? | Own deps | Sanctions | Vulns | Verdict |
|---|---|---|---|---|---|---|---|---|---|
| Serilog | 4.2.0, 4.3.1 | Serilog Contributors (owner `serilog`); lead maintainer Nicholas Blumhardt, Datalust | Australia (Datalust: Brisbane) | Apache-2.0 | Repo only | none on net9/net10 | none | none | **CLEARED** |
| Crc32.NET | 1.2.0 | "force" (owner `force`), GitHub `force-net`, commit e-mail `force@force-net.com` | **Russia** | MIT | Repo only | NETStandard.Library | none | none | **NOT CLEARED**. force-net.com is the author's Russian-language blog; its Blogger profile states location "Russia", and the blog has a Yaroslavl weather widget and Yaroslavl conference posts. Its 2017 review post names Crc32.NET, DeepCloner and Blazer as the author's own projects. Last release December 2017 |
| FluentValidation | 11.11.0 | Jeremy Skinner (owner `jskinner`), .NET Foundation project | UK | Apache-2.0 | Repo only | none on net8 | none | none | **CLEARED** (only reached through evtx) |
| ServiceStack.Text | 8.5.2 | ServiceStack, Inc. (owners `servicestack`, `mythz` = Demis Bellot) | US (Newark, Delaware; maintainer in New York) | **Not SPDX**: ServiceStack terms (`https://servicestack.net/terms`), a "limited, ... freely revocable" licence. ServiceStack has said that .Text is free of quotas (CLAIMED_ONLY) | Repo only | MS only | none | none | Origin **CLEARED**; licence **not acceptable without legal review** (revocable proprietary terms) |
| YamlDotNet | 16.3.0 | Antoine Aubry (owner `aaubry`); co-maintainer Edward Cooke | Aubry: **not stated**; Cooke: US (West Jordan, UT) | MIT | Repo only | none on net8 | none | none | **UNDETERMINED** (see the same package in section 4) |
| CsvHelper | 33.1.0 | Josh Close (owner `joshclose`) | US (Minneapolis, MN) | MS-PL OR Apache-2.0 | Repo only | none on net9 | none | none | **CLEARED** |
| NFluent | 3.1.0 | Thomas Pierrain, Cyrille Dupuydauby, Rui Carvalho, Marc-Antoine Latour (owners `NFluent`, `nfluent-team`) | France (Lyon, Paris) | Apache-2.0 | Repo only | none on net8 | none | none | **CLEARED** (but it is a test-assertion library and should not be a runtime dependency) |
| AlphaFS.New | 2.3.0 | NuGet owner **`suto`**, GitHub `suto1987`: no name, location or bio. It is an unofficial re-publication of AlphaFS, whose upstream (Peter Palotas, Sweden; Jeffrey Jangli, Netherlands) is **archived**. The copyright line also names "Alexandr Normuradov", whose origin could not be established | **Undeterminable** | MIT (upstream LICENSE.md) | Repo only (no author signature; no source commit in the nuspec) | MS only | none | none | **NOT CLEARED**: anonymous publisher of a binary that cannot be tied to an upstream commit |
| DeviceIOControlLib | 0.1.6 | LordMike = Michael Bisbjerg (owner `LordMike`) | Denmark | Licence URL (MIT-style per repo) | Repo only | MS only | none | none | **CLEARED** |
| RawDiskLib | 0.3.0 (0.2.1) | Michael Bisbjerg | Denmark | MIT | Repo only | DeviceIOControlLib | none | 0.2.1 pulls System.Net.Http 4.3.0 (GHSA-7jgj-8wvc-jh57) | **CLEARED** (0.3.0) |
| LTRData.DiscUtils.Core/Ntfs/Streams | 1.0.80 (1.0.49) | Olof Lagerkvist, LTR Data (plus original DiscUtils authors) | Sweden | MIT | **Author-signed**: EV certificate "LAGERKVIST TEKNISK RÅDGIVNING I BORÅS HB", SE, org. no. 969697-0400 (GlobalSign) + repository | LTRData.Extensions | none | none | **CLEARED** |
| LTRData.Extensions | 1.0.22 (1.0.11) | Olof Lagerkvist | Sweden | MIT | Author-signed (as above) | none | none | none | **CLEARED** |
| WmiLight | 7.2.0 (6.10.0) | Martin Kuschnik (owner `MartinKuschnik`) | Germany | MIT | Repo only | none | none | none | **CLEARED** |

### 3.5 Net result for the candidates

The **file-artefact parsers** form a self-contained, clean set: `Registry`, `Lnk`,
`JumpList`, `ExtensionBlocks`, `GuidMapping`, `OleCf`, `Prefetch`, `MFT` and
`RecentFileCache`. Their whole tree is Eric Zimmerman (US) + Serilog (Australia) +
Microsoft. No sanctions hits, no known vulnerabilities.

The **NuGet packages for EVTX, Amcache and AppCompatCache fail**. Their own code is
fine; the problem is what they pull in:

- `evtx` fails only because of `Crc32.NET`. `System.IO.Hashing.Crc32` (Microsoft,
  already referenced by `LogAnalyzer.Dfir.Core`) can replace it. A source-vendored
  fork of `evtx` that makes that one substitution and removes ServiceStack.Text
  could be re-assessed. Until then, keep the current EVTX path.
- `Amcache` and `AppCompatCache` fail because of `AlphaFS.New` (through
  `ERZHelpers`). Both artefacts are plain registry data: Amcache.hve is a hive, and
  ShimCache is a value in the SYSTEM hive. They can be parsed with the cleared
  `Registry` library and LogAnalyzer's own code, without these packages.

## 4. Current LogAnalyzer dependencies

Source: `PackageReference` items in every `.csproj` under
`02_PRODUCT/projects/workspaces/loganalyzer-dfir` (the `_recovered/` prototype is
excluded from the verdicts).

| Package | Version | Author / org | Country | Licence | Signed? | Transitive (non-Microsoft) | Sanctions | Vulns | Verdict |
|---|---|---|---|---|---|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | .NET Foundation / Microsoft (maintainer Sergio Pedri, Microsoft) | US | MIT | **Author-signed**: EV certificate ".NET Foundation", Washington registration 603 389 068 + repository | none | none | none | **CLEARED** |
| Microsoft.Extensions.DependencyInjection | 10.0.10 | Microsoft | US | MIT | Author-signed (Microsoft Corporation) + repository | MS only | none | none | **CLEARED** |
| Microsoft.Data.Sqlite.Core | 9.0.0 | Microsoft | US | MIT | Author-signed + repository | SQLitePCLRaw.core 2.1.10 | none | none | **CLEARED** |
| System.Diagnostics.EventLog, System.Management, System.Security.Cryptography.ProtectedData | 10.0.10 | Microsoft | US | MIT | Author-signed + repository | none | none | none | **CLEARED** |
| System.DirectoryServices | 10.0.9 | Microsoft | US | MIT | Author-signed + repository | none | none | none | **CLEARED** (align to 10.0.10 with the others) |
| System.IO.Hashing | 9.0.0 | Microsoft | US | MIT | Author-signed + repository | none | none | none | **CLEARED** |
| SQLitePCLRaw.bundle_e_sqlcipher (+ core, provider.e_sqlcipher, lib.e_sqlcipher) | 2.1.10 | Eric Sink, SourceGear LLC (owner `ericsink`) | US (Illinois) | Apache-2.0 | Repo only | Native `e_sqlcipher`: SQLite (public domain, Hipp/US), SQLCipher (Zetetic LLC, US), LibTomCrypt (libtom project: Tom St Denis, Canada; maintainer Steffen Jaeckel, Germany). The build script `ericsink/cb/bld/cb.cs` defines `SQLCIPHER_CRYPTO_LIBTOMCRYPT` | none | none | Origin **CLEARED**, with a **maintenance warning**: the author calls these SQLCipher builds "unofficial", and the SQLitePCLRaw 3.0 notes (`v3.md`) deprecate `bundle_e_sqlcipher` and stop the free encrypted builds. Plan a move to official Zetetic SQLCipher (`bundle_zetetic`) or a source build of SQLCipher + LibTomCrypt |
| QuestPDF | 2026.7.2 | Marcin Ziąbek, trading as "Marcin Ziąbek CodeFlint", a sole proprietorship registered in Poland (owners `MarcinZiabek`, `QuestPDF`) | Poland (Kraków) | **Not MIT, not OSI**: QuestPDF Community License / Professional & Enterprise License (nuspec points to LICENSE.md) | Repo only | none declared; ships its own native layer built on Google Skia (US) with bundled third-party native libraries | none | none | Origin **CLEARED**. Native third-party components **UNDETERMINED**: the licence/notice files bundled in the package were not read, because that would mean downloading the full `.nupkg`. **Licence blocker**: the Community License page excludes government bodies and publicly listed companies "regardless of revenue". An operator in either category needs a paid Professional/Enterprise licence |
| DiscUtils.Registry / DiscUtils.Core / DiscUtils.Streams | 0.16.13 | Kenneth Bell (original author), Michael Bisbjerg (Denmark), Quamotion / Frederik Carlier (Belgium), Bianco Veigel (owner `DiscUtils`) | Denmark / Belgium / Germany; original author UK (UNVERIFIED) | MIT | Repo only | MS only (Microsoft.Win32.Registry 5.0.0 etc.) | none | none | **CLEARED**. The project is dormant (last release 0.16.13; repo pushed 2024-02). The actively maintained fork is LTRData.DiscUtils (Sweden, EV-signed). `Registry` from section 3 covers the same need with better forensic fidelity (deleted keys, transaction logs) |
| YamlDotNet | 18.1.0 | Antoine Aubry (sole NuGet owner `aaubry`); co-maintainer Edward Cooke | Aubry: **not stated** anywhere checked (GitHub location blank; aaubry.net names no country, though its projects and certificate names point to Portugal); Cooke: US | MIT | Repo only | none on net10 | none | none | **UNDETERMINED**. The evidence points to EU residence and nothing points to an excluded country, but the owner's rule needs a positive origin. One confirming source closes this (for example a written statement from the maintainer). Fallback: vendor the source at a pinned tag after review |
| xunit, xunit.runner.visualstudio (test only) | `2.*` (resolves to 2.9.3 / 2.8.2 at time of writing) | James Newkirk, Brad Wilson (US); .NET Foundation | US | Apache-2.0 | Repo only | xunit.* family; Newtonsoft.Json 13.0.3 (James Newton-King, Microsoft) through the test SDK | none | none | **CLEARED** for test use. **Floating versions** (`2.*`, `17.*`) must be pinned; a floating range defeats provenance |
| Microsoft.NET.Test.Sdk (test only) | `17.*` | Microsoft | US | MIT | Author-signed | Newtonsoft.Json | none | none | **CLEARED**, pin the version |

Current-dependency findings that need action:

1. **QuestPDF licence**: the Community License does not cover government bodies.
   This is a licence problem, not an origin problem, and it decides whether the
   product can ship as-is to its intended operators.
2. **YamlDotNet origin is UNDETERMINED** under the strict rule (owner country not
   self-declared).
3. **SQLitePCLRaw e_sqlcipher is a deprecated, unofficial build line.**
4. **Floating test-package versions** (`17.*`, `2.*`).
5. `System.DirectoryServices` 10.0.9 is out of step with the 10.0.10 baseline.

No current dependency is of Russian, Chinese or North Korean origin, and none
appears on any sanctions list searched.

## 5. Signature summary

| Signature type | Packages |
|---|---|
| Author-signed (EV or Microsoft) + repository | All Microsoft packages; CommunityToolkit.Mvvm (.NET Foundation EV); LTRData.* (Swedish EV) |
| Repository-signed only (nuget.org), owner list embedded | All Eric Zimmerman packages (`EricRZimmerman`), Serilog, QuestPDF, SQLitePCLRaw, DiscUtils, YamlDotNet, FluentValidation, ServiceStack.Text, CsvHelper, NFluent, RawDiskLib, DeviceIOControlLib, WmiLight, Crc32.NET, AlphaFS.New |

A repository signature proves the bytes came from nuget.org under the listed owner
account. It does not prove who built them. For repository-only packages, either
build from the source commit recorded in the nuspec, or pin by hash and trust the
owner name (section 7).

## 6. Residual risk: minor contributors in the cleared Zimmerman repos

Code merged by people other than the maintainer, with origin where GitHub shows
it. Everything here is small and reviewable. Contributors with no stated origin are
marked UNDETERMINED; their changes should be read once during the source review.

| Repo | Contributor (commits) | Origin shown | What they changed |
|---|---|---|---|
| Registry | AndrewRathbun (8) | US (Michigan) | various |
| Registry | reece394 (4) | UNDETERMINED | Helium/registry.dat hive type support, settings.dat container, UWP array value fix (2025) |
| Registry | rhensing-code (1), rifatx (1) | UNDETERMINED | null-NK exception fix (2022); `GetValue` helpers (2021) |
| Registry | SuperJMN (2) | Spain | various |
| Lnk / OleCf | rhensing-code (3 / 1) | UNDETERMINED | bounds checks before array copies; test fixes (2023) |
| Lnk / ExtensionBlocks | SteAmeR (2 / 4) | Türkiye (Istanbul) | shell item formats |
| Lnk / JumpList | herosi (1 / 1) | Japan (Internet Initiative Japan) | various |
| JumpList | agoodhockey (1) | UNDETERMINED | "Use unicode" (2021) |
| GuidMapping | 0xHasanM (1) | UNDETERMINED | added lines to `GuidToName.txt` (data) |
| GuidMapping / JumpList | kacos2000 | Greece | GUID data |
| Prefetch | rhensing-code (1) | UNDETERMINED | Prefetch version 31 support (2024) |
| MFT | VTol-zz (1), Apr4h (1) | UNDETERMINED | non-resident data offset checks (2021) |
| several | reece394 | UNDETERMINED | pinning FluentAssertions to its open licence (test projects only) |
| several | Eran-YT (2 each) | not stated | build/CI |

## 7. Recommendation

### 7.1 Adopt (after the source review in 7.3)

| Package | Pinned version | Source commit (from the nuspec) | nuget.org packageHash (SHA-512, base64) |
|---|---|---|---|
| Registry | 2026.5.0 | `15203c155e68bbe69729186011b343f25f4239f9` | `X6O5Wn5QPA/LFZWxLhdIwVvYY6GI7sWGr8Vfxf6+Emm6sSFI6f3icXZEN0u0zz1TwbaMnhUIiSvIHFom8tjhzA==` |
| Lnk | 2026.5.3 | `918bea4a9c8d99b02aae2b1a5d1512aff0c07de2` | `u2YNIiIfcMs/tXltI6ybAy0eXE4mq7DKnW2bGKqQ5a2dEmgs3MfEXHKissfRkTvLIjXJUX/8FOPT1jqBX55GWA==` |
| ExtensionBlocks | 2026.5.0 | `58e35b8457bf3006f672c972619bc0fb913fb7e4` | `utN18FVFaNFqxRPzCkoUANJN91FfxJYSNARPCDC8CLfzGTib/s+jcA39NehO2MfU97doPNo5kUdVKApdn4xY1Q==` |
| GuidMapping | 2026.5.0 | `ef2e1e38623c77522c8a1e2c5d257712fdb26a2d` | `QA0ZTtkN2c5eyRXqfgMbXMG5JYu+KVhBPNduvKuUpZyM9QWND9Ta5yabGpcBUcedYY8MsxDTJ9bMagjrApVFMA==` |
| OleCf | 2026.5.0 | `7a7c5f6a58d0af01ce0aedb2c92746f5b48e1a8f` | `rESJRuW5iLmRyfu4m9XGTBkEP3GOSox0+ldfwElVeMj+lpPFBHcSGz9cEsYtVqtGxTUDot9nePT1WYmcxpR/Sg==` |
| JumpList | 2026.5.2 | `e87bacd4869cbe0633c16ad11d3d26c9f39d0a2b` | `4n5nBhL/aezKx8MvMK3iMHpCJwiBovb0sFGqhz0wr3D2h6alsHDAprsPzjJYXDKPAcnUGIuWgYdUvsDUQBYvmQ==` |
| Prefetch | 2026.5.2 | `54a3ac0d6d0006d3836c9bd368dfe32cbcaa131d` | `+8khfC2yNlJ5JlwrAvRjXQFMHjguF4scqANxti6Hr7T/67DcYVJOCvjqzjiP/N6aiGXMQvOBXJRiYx6dcZgesg==` |
| MFT | 2026.5.1 | `d6e6deee60dcb11f52bcf2940cb07a202fa35a6c` | `Waz4P+8fotI7N0zWFEFNCfYf9KNxZsq6n7V4xxovu78PI7kDYGxWHhdjyOW96MlLmc2X2P98IxKFtr9jx2J0TQ==` |
| RecentFileCache (optional, low value) | 1.5.2 | `3a49848e5fb0b5413d5a8a8624e8d1651b13e4e9` | `e0Uhxy0V8SF24IOy+wm3EPN4PxGPwzBi85s7KyFTSOm5zdniwLaju4Y3HckMTZks0yZFLjo7S3xKbqOABFa+1w==` |
| Serilog (transitive) | 4.3.1 | serilog/serilog tag v4.3.1 | `gKYM5vGIWhNvwgUPqHalH5o+uIN59Xc8tTj5sQhCo8vsaTongtok1b1HihQZ1UFEIK2TcaGTdLBeQpW9vap1PA==` |
| System.Text.Encoding.CodePages (transitive, MS; pruned on net10) | 10.0.7 | dotnet/runtime | `gWjqYZ1e8pw1Koz680hifV+yKL5IJ2hzo6U68WBpb7nG/gzJ/aKqK00uj721kS8QR5DM54vyx49Wiw8QxPqL4g==` |

Pin `Serilog` explicitly to 4.3.1 so `Registry`'s `>= 4.2.0` and `Lnk`'s
`>= 4.3.1` resolve to one version. Serilog is a logging abstraction with no sinks
here: no network or file sink is pulled in.

### 7.2 Do not adopt

- `evtx` 1.5.2: Crc32.NET (Russia). If the EZ EVTX parser is wanted, vendor its
  source at a pinned commit, replace Crc32.NET with `System.IO.Hashing.Crc32`,
  drop ServiceStack.Text (proprietary revocable licence), review, then re-assess.
- `Amcache` 2026.5.3 and `AppCompatCache` 1.2.4: AlphaFS.New (anonymous
  publisher) through ERZHelpers. Parse both artefacts with `Registry` instead.
- `AlphaFS.New`, `Crc32.NET`, `ERZHelpers`: not cleared under any path.

### 7.3 How to vendor with SHA-256 and a provenance record

Two options, in order of preference for an air-gapped classified build.

**Option A: build from source (preferred for repository-only packages).**

1. On a connected, non-classified staging machine, clone each repo and check out
   the exact commit from 7.1. Confirm `git rev-parse HEAD` matches.
2. Do a source review: the diff of each repo against the previous reviewed
   version, the contributor changes in section 6, a check for network, process,
   reflection-load or P/Invoke calls outside the expected file APIs, and a
   check that the test projects are not part of the shipped build.
3. Build with the pinned .NET SDK into a local package or a project reference.
   Record the SHA-256 of each produced DLL.
4. Carry the source archive and the built artefacts across the air gap on the
   approved transfer medium, recorded in the transfer register.

**Option B: vendor the published `.nupkg` files into an offline feed.**

1. On the staging machine, download exactly the versions in 7.1 into an empty
   directory and run `dotnet nuget verify --all <file>` on each. Record the
   repository certificate's SHA-256 fingerprint.
2. Compute `sha256sum` and compare the SHA-512 with the `packageHash` values in
   7.1 (nuget.org catalog). Any mismatch is a stop.
3. Put the files in a local folder feed in the repo (for example
   `third_party/nuget/`), and in `nuget.config` clear every other source.
4. Turn on `RestorePackagesWithLockFile=true` and `RestoreLockedMode=true` so
   `packages.lock.json` pins the content hash of every direct and transitive
   package.
5. Add `trustedSigners` in `nuget.config`: the nuget.org repository certificate
   fingerprint with an `owners` allow-list (`EricRZimmerman;serilog;Microsoft`),
   and set `signatureValidationMode=require`.
6. Keep `NuGetAudit` enabled against an offline advisory snapshot refreshed at
   each transfer.

**Provenance record**: one file per package, committed next to the vendored
artefact, for example `third_party/PROVENANCE/<id>-<version>.yaml`:

```yaml
id: Registry
version: 2026.5.0
publisher: Eric Zimmerman (NuGet owner EricRZimmerman)
origin_country: US
origin_evidence:
  - https://api.github.com/users/EricZimmerman   # location Indianapolis, company ZimTech4n6
license_spdx: MIT
copyright: Eric Zimmerman
source_repo: https://github.com/EricZimmerman/Registry
source_commit: 15203c155e68bbe69729186011b343f25f4239f9
nuget_package_hash_sha512: X6O5Wn5QPA/LFZWxLhdIwVvYY6GI7sWGr8Vfxf6+Emm6sSFI6f3icXZEN0u0zz1TwbaMnhUIiSvIHFom8tjhzA==
artifact_sha256: <filled at vendoring time>
signature: repository (NuGet.org Repository by Microsoft); author: none
repository_cert_sha256: <filled from dotnet nuget verify>
transitive_dependencies: [Serilog 4.3.1, System.Text.Encoding.CodePages (pruned on net10)]
sanctions_check: {date: 2026-10-12, lists: [OFAC SDN, US CSL, EU FSF, UN SC, UK OFSI], result: no match}
vulnerability_check: {date: 2026-10-12, source: OSV, result: none}
source_review: {reviewer: <name>, date: <date>, result: <pass/fail>, notes: <...>}
verdict: CLEARED
```

Re-run the origin, sanctions and vulnerability checks at every version bump, and
at least every six months for pinned versions.

## 8. Open items

- QuestPDF: read the third-party licence/notice files bundled inside the package
  (native Skia layer and its components), and decide on the licence tier for the
  operators.
- YamlDotNet: get a positive statement of the owner's country, or vendor the source
  after review.
- UK sanctions: re-check against the current UK Sanctions List; the legacy OFSI file
  searched here says it was last updated 03/06/2026.
- SQLitePCLRaw: plan the move away from `bundle_e_sqlcipher` before upgrading to 3.x.

## 9. Sources

Package metadata (CODE_VERIFIED):

- nuspec files: `https://api.nuget.org/v3-flatcontainer/<id>/<version>/<id>.nuspec` for every package named above
- Registration/catalog: `https://api.nuget.org/v3/registration5-gz-semver2/<id>/<version>.json`
- Signatures: the `.signature.p7s` entry of `https://api.nuget.org/v3-flatcontainer/<id>/<version>/<id>.<version>.nupkg` (range-read)
- OSV: `https://api.osv.dev/v1/querybatch`; advisories `https://github.com/advisories/GHSA-7jgj-8wvc-jh57`, `https://github.com/advisories/GHSA-cmhx-cq75-c4mj`

Origin claims (DOCUMENT_VERIFIED unless marked):

- Eric Zimmerman: `https://api.github.com/users/EricZimmerman`, `https://ericzimmerman.github.io`, repos `https://github.com/EricZimmerman/<repo>` (activity via `https://api.github.com/repos/EricZimmerman/<repo>`)
- Crc32.NET author: `https://github.com/force-net`, commits in `https://github.com/force-net/Crc32.NET`, blog `http://force-net.com/`, profile `https://www.blogger.com/profile/09205548202595699526` (location "Russia")
- AlphaFS.New: `https://www.nuget.org/packages/AlphaFS.New/2.3.0`, `https://www.nuget.org/profiles/suto`, `https://github.com/suto1987`, upstream `https://github.com/alphaleonis/AlphaFS` (archived), `https://api.github.com/users/alphaleonis`, `https://api.github.com/users/Yomodo`
- Serilog / Datalust: `https://github.com/serilog/serilog`, `https://api.github.com/users/nblumhardt`, `https://datalust.co/` (Brisbane, Australia)
- QuestPDF: `https://www.questpdf.com/license/`, `https://www.questpdf.com/license/community.html` (licensor, Poland, government-body exclusion), `https://api.github.com/users/MarcinZiabek` (Kraków), `https://github.com/QuestPDF/QuestPDF/releases/tag/2024.3.0` (native Skia layer)
- SQLitePCLRaw: `https://api.github.com/users/ericsink` (SourceGear, Illinois), `https://github.com/ericsink/SQLitePCL.raw/blob/main/v3.md` (e_sqlcipher deprecation), `https://github.com/ericsink/cb/blob/master/bld/cb.cs` (LibTomCrypt crypto provider)
- ServiceStack: `https://servicestack.net/terms` (ServiceStack, Inc., Newark, Delaware), `https://api.github.com/users/mythz`; free-of-quota statement for .Text: `https://forums.servicestack.net/t/servicestack-text-license/2952` (CLAIMED_ONLY)
- YamlDotNet: `https://github.com/aaubry/YamlDotNet`, `https://aaubry.net/`, `https://api.github.com/users/aaubry`, `https://api.github.com/users/EdwardCooke`
- FluentValidation: `https://api.github.com/users/JeremySkinner` (UK)
- CsvHelper: `https://api.github.com/users/JoshClose` (Minneapolis)
- NFluent: `https://api.github.com/users/tpierrain` (Lyon), `https://api.github.com/users/dupdob` (Paris)
- LTR Data: `https://api.github.com/users/LTRData`, `https://ltr-data.se`; EV certificate subject in the package signature
- Michael Bisbjerg: `https://api.github.com/users/LordMike` (Denmark)
- WmiLight: `https://api.github.com/users/MartinKuschnik` (Germany)
- DiscUtils: `https://github.com/DiscUtils/DiscUtils`, `https://api.github.com/users/qmfrederik` (Brussels), `https://api.github.com/users/quamotion`
- CommunityToolkit: `https://github.com/CommunityToolkit/dotnet`, `https://api.github.com/users/Sergio0694`; .NET Foundation EV certificate subject in the package signature
- xunit: `https://github.com/xunit/xunit`, `https://api.github.com/users/bradwilson`
- Contributors (section 6): `https://api.github.com/repos/EricZimmerman/<repo>/contributors` and `https://api.github.com/users/<login>`

Sanctions and bans: the five list URLs in section 2; BIS Final Determination 89 FR
52434 (June 2024); FCC Covered List `https://www.fcc.gov/supplychain/coveredlist`.
