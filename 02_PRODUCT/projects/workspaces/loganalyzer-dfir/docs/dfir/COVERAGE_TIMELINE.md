# Acoperirea în timp (master plan 6.4)

**Unde se află:**
- **Cod:** `LogAnalyzer.Dfir.Core/Coverage/CoverageTimeline.cs`.
- **Interfață:** tab-ul „Acoperire în timp” din „Investigație completă”.
- **Teste:** `CoverageTimelineTests`.

**Ce face:** completează matricea de acoperire pe familii de surse (`CoverageMatrix`, deja în „Acasă”) cu dimensiunea timpului.

- **Harta:** câte evenimente a scris fiecare sursă în fiecare zi a perioadei cazului. Are un rând pe sursă și o celulă pe zi (UTC), iar intensitatea crește după număr (1–9, 10–99, 100–999, 1.000+). O zi goală se vede, pentru că este informație.
- **Lacunele posibile:** zilele fără evenimente, strict între prima și ultima zi cu activitate a sursei, de cel puțin 2 zile, sunt marcate CANDIDATE, niciodată dovezi.
  - Explicațiile ne-rău-intenționate sunt spuse alături: stația oprită, canal fără activitate, jurnal golit sau suprascris.
  - O lacună se confirmă cu alte surse (de exemplu golirea jurnalului 1102/104, detectată separat de `LogClearAssessment`).
  - Marginile perioadei unei surse nu sunt lacune: prima zi este adesea locul unde retenția a tăiat jurnalul.
- **Evenimentele fără oră UTC:** sunt numărate separat și nu apar pe hartă.
- **Perioadele lungi:** peste 400 de zile se arată ultimele 400, iar pagina o spune.

**Verificat în laborator (2026-11-08):**
- **Insula, WS02:** 25 de surse în 8 zile, 88 de evenimente fără oră.
- **Continent, WS01:** 24 de surse în 8 zile, Security cu 26.413 evenimente.

Laboratorul e proaspăt, așa că activitatea este doar în ultimele zile și nu există lacune.
