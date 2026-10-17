# Third-party notices

LogAnalyzer includes the open-source libraries listed below. Each is used unmodified, as the NuGet package pinned in
`LogAnalyzer.Dfir.Windows/packages.lock.json`. Provenance (publisher, source commit, package SHA-512, sanctions and
vulnerability checks) is kept in the owner's local provenance record, not in this repository.

| Library | Version | Copyright | License | Used for |
|---|---|---|---|---|
| Registry (NuGet id `Registry`) | 2026.5.0 | Copyright (c) Eric Zimmerman | MIT | Recovery of deleted keys and values in offline registry hives |
| Serilog | 4.3.1 | Copyright (c) Serilog Contributors | Apache-2.0 | Logging abstraction required by Registry (no sinks are used) |

Source of Registry: https://github.com/EricZimmerman/Registry (tag 2026.5.0).
Source of Serilog: https://github.com/serilog/serilog (tag v4.3.1).

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
