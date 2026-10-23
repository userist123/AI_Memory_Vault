# Third-party notices

LogAnalyzer includes the third-party libraries listed below. Each is used unmodified, as the NuGet package pinned in the
projects' `packages.lock.json` files (every direct `PackageReference` of the 17 projects of `LogAnalyzer.slnx` is listed here;
licence, copyright and URL as stated in the package metadata in the local NuGet cache, 2026-10-22). Provenance (publisher, source
commit, package SHA-512, sanctions and vulnerability checks) is kept in the owner's local provenance record, not in this repository.

| Library | Version | Copyright | License | Used for |
|---|---|---|---|---|
| Registry (NuGet id `Registry`) | 2026.5.0 | Copyright (c) Eric Zimmerman | MIT | Recovery of deleted keys and values in offline registry hives |
| Serilog | 4.3.1 | Copyright (c) Serilog Contributors | Apache-2.0 | Logging abstraction required by Registry (no sinks are used) |
| QuestPDF (NuGet id `QuestPDF`) | 2026.7.2 | Marcin Ziąbek, QuestPDF contributors | **QuestPDF Community License** (dual-licensed: Community / Professional / Enterprise; not an OSI licence) | PDF reports |
| CommunityToolkit.Mvvm | 8.4.2 | (c) .NET Foundation and Contributors | MIT | MVVM helpers of the WPF application |
| DiscUtils.Registry | 0.16.13 | Kenneth Bell, LordMike (authors) | MIT | Reading offline registry hives |
| Microsoft.Data.Sqlite.Core | 9.0.0 | © Microsoft Corporation | MIT | SQLite data access |
| SQLitePCLRaw.bundle_e_sqlcipher | 2.1.10 | Copyright 2014-2024 SourceGear, LLC | Apache-2.0 | Encrypted local database (SQLCipher); the native SQLCipher library it bundles carries its own upstream licence, not checked here |
| Microsoft.Extensions.DependencyInjection | 10.0.10 | © Microsoft Corporation | MIT | Application composition |
| System.Diagnostics.EventLog | 10.0.10 | © Microsoft Corporation | MIT | Windows event log access |
| System.DirectoryServices | 10.0.9 | © Microsoft Corporation | MIT | Directory queries (Connectors) |
| System.IO.Hashing | 9.0.0 | © Microsoft Corporation | MIT | Non-cryptographic checksums |
| System.Management | 10.0.10 | © Microsoft Corporation | MIT | WMI queries |
| System.Security.Cryptography.ProtectedData | 10.0.10 | © Microsoft Corporation | MIT | DPAPI protection of local secrets |
| YamlDotNet | 18.1.0 | Copyright (c) Antoine Aubry and contributors | MIT | Reading YAML rule files |

Build and test only (not shipped in the published application): Microsoft.NET.ILLink.Tasks 10.0.9 (MIT, © Microsoft Corporation,
trimming at publish time), Microsoft.NET.Test.Sdk 17.14.1 (MIT, © Microsoft Corporation), xunit 2.9.3 and xunit.runner.visualstudio
2.8.2 (Apache-2.0, Copyright (C) .NET Foundation).

Source of Registry: https://github.com/EricZimmerman/Registry (tag 2026.5.0).
Source of Serilog: https://github.com/serilog/serilog (tag v4.3.1).
Project of QuestPDF: https://www.questpdf.com/ (licence overview: https://www.questpdf.com/pricing). Other project URLs, from the
package metadata: https://github.com/CommunityToolkit/dotnet, https://github.com/DiscUtils/DiscUtils,
https://docs.microsoft.com/dotnet/standard/data/sqlite/, https://github.com/aaubry/YamlDotNet/wiki, https://dot.net/ (Microsoft packages).

### QuestPDF: licence condition (owner action)

QuestPDF 2026.7.2 ships its licence as `LICENSE.md` inside the package (License Selection Guide version 3.0, effective 6 July 2026).
Use under the free **Community License** is allowed only when the user meets its eligibility terms, among them: an organisation with
annual gross revenue under USD 1,000,000 in its most recently completed fiscal year (consolidated), an individual under the same
threshold, a charitable or academic institution, or an OSI-licensed open-source project. The same text states that **public-sector
entities and government agencies (other than academic institutions) and publicly traded companies are not eligible for the Community
License, regardless of revenue**. Community use therefore requires the owner's eligibility check (revenue terms and the kind of
organisation that deploys LogAnalyzer) before production use; otherwise a Professional or Enterprise licence is needed. This is an
open owner item (`OWN-QUESTPDF-LICENCE` in `release-gate/findings-register.draft.json`). The package also carries the licences of its
native dependencies (`ExternalDependencyLicenses/`: Skia, HarfBuzz, qpdf, libjpeg-turbo, libpng, libwebp, zlib, expat, ...) and of the
bundled Lato font (SIL Open Font License, `LatoFont/OFL.txt`); those texts are to accompany any native library or font of the package that is shipped.

---

## Registry: MIT License

Copyright (c) Eric Zimmerman

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

---

## Serilog: Apache License 2.0

Copyright (c) Serilog Contributors

Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the
License. You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an
"AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific
language governing permissions and limitations under the License.

The full text of the Apache License, Version 2.0 is available at https://www.apache.org/licenses/LICENSE-2.0.txt.
