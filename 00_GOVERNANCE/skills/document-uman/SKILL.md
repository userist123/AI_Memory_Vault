---
name: document-uman
description: Write and build Word (.docx), Excel (.xlsx) and PDF documents start to finish so they read and look like the work of a competent human professional, not AI - styles-based layout, Romanian typography, clean metadata, prose without AI tells, and a mandatory checker run before delivery. Use automatically whenever the owner asks for any document - cerere, adresă, raport, memoriu, scrisoare, contract, ofertă, proces-verbal, lucrare, tabel, foaie de calcul, PDF. Triggers - "fă-mi un document", "redactează", "scrie o cerere", "fă un raport", "tabel Excel", "fă un PDF", "document Word", "să nu pară făcut de AI", "să pară scris de om", "natural", "profesionist".
---

# Document uman (human-made documents)

Distilled on 2026-11-22 from ~50 expert YouTube tutorials, official writing guides, Romanian legal
texts and three rounds of blind review. Items marked [RO] are Romanian conventions from general
knowledge.

Works together with `anthropic-skills:docx`, `anthropic-skills:xlsx`, `anthropic-skills:pdf` (tooling)
and `surse-web` (facts). This skill decides HOW the result must look and read.

## 0. Workflow (always this order)

Files in this skill folder:
- `genuri.md` - genre templates (cerere, adresă, notificare/somație, plângere prealabilă, contract,
  proces-verbal, decizie, referat, raport, e-mail) + lessons from blind reviews. READ IT for the genre.
- `docx_ro.py` - builder: A4, margins, styles, ro-RO proofing, clean metadata, nbsp, signature blocks,
  tables, "Pagina X din Y", `finalizeaza_in_word()`.
- `check_doc.py` - text/layout/metadata checker. `render_doc.py` - Word->PDF + PNG pages.
- `docx_complex.py`, `xlsx_ro.py`, `pdf_ro.py` - long reports, workbooks, PDF dossiers (section 7b).
- `cifre_ro.py` - rounding (Excel-compatible), exact allocation, numbers in words, DOOM "de",
  plausible document numbers, prose-vs-model reconciliation. Use it for every number in a document.

1. Purpose, reader, what the reader must do after reading. One sentence. If missing, infer the safe
   default and note it in the report to the owner (never ask - owner rule).
2. Genre: open `genuri.md`; if an official standard form exists for this request (F.1, ANAF, ONRC...),
   find it on the institution's site and follow it instead of free text.
3. Outline (headings only) before prose. Main request in the first paragraph.
4. Write the text (section 2 rules). Real facts only: names, dates, amounts, article numbers, from the
   owner or verified sources (laws: re-check the current text). Unknown fact -> placeholder
   `[de completat: ...]`, never invented, never textbook sample values.
5. Build with `docx_ro` (styles only), save, then `finalizeaza_in_word(path)`.
6. `python -P -E <skill dir>\check_doc.py <file>` - fix every FAIL,
   review each WARN.
7. `python -P -E ...\render_doc.py <file>` and look at every PNG as a Romanian clerk would
  . For important documents (legal effect, money, authorities) also get a
   blind review from a fresh subagent with the role "experienced Romanian clerk/lawyer + AI-text
   detector". Then deliver with PDF made by Word.

## 1. What gives AI away - density matters, not single marks

No single sign proves AI; a cluster does. Keep each one rare, never stacked.

- Em dash `—` where a person would use a comma, period or parentheses.
  [RO] Romanian uses the spaced en dash ` – ` normally; keep it sparse (max ~1 per paragraph).
- "Not X, but Y" / "nu doar X, ci și Y" / "nu este vorba de X, ci de Y".
- Forced triplets: "rapid, eficient și sigur" in every paragraph. Use 2 or 4 when
  the content has 2 or 4.
- Empty vocabulary: delve, elevate, innovative, crucial, seamless, robust, landscape, leverage
 . [RO] Equivalents: "joacă un rol crucial", "în era digitală",
  "o abordare holistică", "este important de menționat/subliniat", "în concluzie", "în contextul
  actual", "o gamă largă de", "facilitează", "optimizează", "inovator", "esențial", "sper că acest
  mesaj vă găsește bine", "nu ezitați să mă contactați".
- "Serves as" instead of "is" ("servește drept", "reprezintă" used everywhere).
- Preamble, restating and closing summaries: "So to summarize", "To clarify".
  A one-page letter has no "În concluzie".
- Hedge stacks: "ar putea, eventual, să contribuie" - max one hedge per sentence, only for real
  uncertainty.
- Impersonal generic voice ("utilizatorii pot", "se recomandă ca persoanele") where a person would say
  "vă rog", "solicit", "am constatat".
- Uniform blocks: every paragraph 3-4 sentences, same length; measurable as low std-dev of paragraph
  word counts.
- Emoji, emoji bullets, check marks, bold on every other phrase, a heading over every 2 paragraphs,
  bullet lists of bare nouns.
- Gushing praise and strained analogies ("ca un far în ceață").
- Weird stiff transitions on every paragraph: "Mai mult decât atât", "De asemenea", "Totodată",
  "Moreover".
- Generic content with no specifics. This is the strongest tell of all.

Not tells in formal documents (keep them): correct grammar, passive voice where the procedure
requires it, formal register, long sentences mixed with short ones.

## 2. Writing rules

- Specifics over adjectives: date, number, name, place, article, file number.
- First person where the genre allows: "Subsemnatul ... solicit", "am constatat", "vă rog".
- Doer first, active voice: "Comisia a decis" not "S-a luat decizia de către comisie".
- Concrete verbs; nouns ending in -are/-ție turned back into verbs: "a explicat" not "a furnizat o
  explicație".
- Cut filler: "în vederea", "în scopul de a", "este de menționat faptul că" -> "pentru", drop.
- Ten words -> five; plain words the writer would actually use.
- Vary sentence and paragraph length; a one-sentence paragraph is allowed for the key point
 .
- Paraphrase what people said; quote only from a written record.
- No "very/foarte" padding, no repeating the same word in adjacent sentences.
- Revise in narrow passes, one issue each (hedges, "nu doar...ci", adverbs, logic), changing only what
  the pass flags. Final read forward for flow, backward sentence by sentence for errors
 .
- Genre structure beats creativity: a cerere has antet/destinatar, "Domnule/Doamnă ...", Subsemnatul,
  object, grounds, request, date, signature - nothing else.

## 2b. Computed facts
- Every term in working days, due date, preaviz or weekday claim is COMPUTED with
  `docx_ro.adauga_zile_lucratoare()` / `zi_lucratoare()` (Romanian holidays incl. Orthodox Easter),
  never counted by hand - a hand count missed 30.11 + 1.12 in the 2026-11-22 test.
- Every sum, VAT split, penalty and total is computed in code and re-checked once.

## 3. Romanian typography [RO]

- Diacritics with comma below: ș ț Ș Ț (U+0219, U+021B, U+0218, U+021A), never cedilla ş ţ.
  Always ă â î. Checker FAILs on cedilla.
- Quotes „..." (U+201E opening, U+201D closing); nested «...». No straight "..." in prose.
- Dates: 22.11.2026 or 22 noiembrie 2026 (months lowercase). Never 2-digit years.
- Numbers: 1.250,50 lei (dot thousands, comma decimals); currency after the amount; "%" with no space
  is common in practice (12%), keep it consistent.
- Abbreviations: nr., art., alin., lit., str., bl., ap., jud.
- Non-breaking space between number and unit, between "art." and its number.

## 4. Word (.docx)

- Everything through named styles, nothing hand-formatted on runs. In python-docx set
  font/spacing on `doc.styles['Normal']`, `Heading 1..3`, `Title`, `Caption`, `List Paragraph`.
- Default font is not Calibri-by-accident: pick one deliberately. Business/report: Arial or Calibri 11,
  line spacing 1.08-1.15. Official RO letters, theses: Times New Roman 12, line 1.15-1.5,
  0 pt between paragraphs if first-line indent is used. One family, two at most.
- Alignment: reports left-aligned; official RO letters/theses justified is the local
  norm - if justified, enable hyphenation or check for rivers of white space.
- Margins: A4; 2.5 cm all round for letters; theses ~2.5 left, 2 top/bottom/right.
- Headings: numbered multilevel 1 / 1.1 / 1.1.1 linked to heading styles; black or one dark accent;
  H1 can start a new page in long reports, not in letters.
- Long documents: cover in its own section without header/footer; TOC generated from headings;
  numbering starts at 1 on content; footer "Pagina X din Y"; header with organisation + title, small
 . Short documents (1-3 pages): no cover, no TOC, page number only if >1 page.
- Tables: caption above; figures: caption below; captions numbered (Tabelul 1, Figura 1).
- Bullets only for genuinely list-shaped content, from the list style, not glyphs typed by hand
 . Prefer prose in letters.
- Revision block (date, author, version) only for reports/procedures.

## 5. Excel (.xlsx)

- Tabs by purpose: Date / Calcule / Raport (+ Instrucțiuni, Jurnal modificări for working models);
  raw data separate from analysis.
- One empty buffer row on top and buffer column A (width ~3).
- One font (Arial/Calibri 10-11), row height 15, header row taller with wrap.
- Column widths deliberate (grid of 5: 5, 10, 15, 20...), never "####".
- Header: short labels, dark fill of one hue, white bold; long explanation in a cell note.
- Horizontal borders only, dark grey; thicker under the header; no all-borders grid; gridlines off;
  freeze panes under the header.
- Colour code inputs vs formulas consistently; few colours, high contrast.
- Real formulas, not pasted values, so the owner can change an input and see totals move.
- Number formats [RO]: `#.##0,00` display (set via format code `#,##0.00` with RO locale), dates
  `dd.mm.yyyy`, totals bold with a top border.
- Charts: no clutter - drop gridlines/legend/axis when data labels carry the values.
- Print setup on every tab: fit to width, landscape if wide, header/footer with page numbers and date
 .
- Only merge for the sheet title.
- Small-firm files look like small-firm files: no financial-modelling conventions (blue input font,
  named ranges everywhere, prose cell comments). One short "Instrucțiuni" line is enough.
- Notes must be true ("celulele galbene se completează" only if exactly those are inputs).
- Header/footer values that depend on a cell are formulas or omitted, never typed copies.
- Money logic: payments filtered by date <= calculation date; penalties computed per period between
  payments (balance x rate x days of that period), capped as the contract says; due dates with
  WORKDAY() when the contract counts working days.
- Separators: this PC's Windows is en-US (1,234.56). The file stores numbers, display follows the
  reader's Excel; for PDFs use render_doc.py (forces "," decimal and "." thousands during export).
- Save through `finalizeaza_xlsx()` - recalculates and strips the folder path (x15ac:absPath).

## 6. Metadata and file traces (machine fingerprints)

Verified 2026-11-22: python-docx writes author "python-docx", comment "generated by python-docx",
created 2013-12-23; openpyxl writes creator "openpyxl"; reportlab writes Producer "ReportLab".
Always set:
- Build from `baza_ro.docx` (created by Microsoft Word on this PC, ro-RO) - docx_ro does it; never
  from the python-docx default template (en-US/ja-JP theme, Courier style, Macintosh app.xml).
- Run `finalizeaza_in_word()` / `finalizeaza_xlsx()` on every file.
- Honest dates: make the document on the date it bears. NEVER backdate file timestamps or fabricate
  editing history - for documents with legal effect that is falsification. If an old-dated document
  must be (re)made, say so to the owner; it is a copy/redraft made today.
- No folder paths, usernames of this PC or tool names anywhere in the file.
- docx: `core_properties.author` = the owner's name as he signs, `last_modified_by` same,
  `comments` = "", `title` = real title, `created`/`modified` = now, `revision` > 1 is fine.
- xlsx: `wb.properties.creator`, `lastModifiedBy`, `title`.
- PDF: prefer Word -> PDF (`docx2pdf` / Word COM `ExportAsFixedFormat`) so Producer is Microsoft Word;
  if reportlab is used, set `canvas.setAuthor/setTitle/setCreator` and accept that Producer stays.
- File name like a person saves it: `Cerere_concediu_Popescu_2026-11-22.docx`, not `output_final_v2.docx`.
- Never leave tool notes, `[TODO]`, lorem ipsum, or "Generated by" anywhere.

## 7. PDF

- Make the PDF from a properly styled Word file (all section 4 rules carry over), export PDF/A or
  Standard.
- Reports: not a wall of text - one accent colour, a key-figure callout, a chart where numbers compare,
  clear dividers. Letters and cereri stay plain: no colours, no boxes, no icons.
- Embed fonts; check diacritics render (open the PDF, render a page to PNG, look).

## 7b. Complex documents (reports > 5 pages, models, dossiers)

convocator, cenzor report, offers, proxy form, opis, merged dossier; `build_all.py`). The older
`runda3\` set is the counter-example the blind review took apart.

Content rules learned the hard way (2026-11-22, blind review of runda3 - the tooling was fine, the
numbers and the story were not):
- ONE data model (dict/JSON) generates the Excel inputs, every Word table, every number and count in
  the prose, chart labels and the agenda. No number is typed into prose; the build ends with
  `cifre_ro.reconciliaza_text(text, model)` (must be empty) and `check_doc.py --date model.json`.
- One rounding routine: `cifre_ro.rotunjire` / `ro` / `suma` (ROUND_HALF_UP on 15 significant
  digits, like Excel). Never `round()` or `f"{x:.2f}"` (half-even: 5.964,5 -> 5.964 vs Excel 5.965).
- Reconcile every total: parts must add up to the total shown; allocations use
  `cifre_ro.repartizeaza` and the remainder goes on a visible "diferențe din rotunjire" line;
  rounded percentages get a note or no total.
- Counts in words must equal the rows they describe ("șase apartamente" next to 5 rows was the
  finding); generate them from len() with `cifre_ro.in_litere(n, gen)`.
- Ledger vs narrative: contract dates vs invoices (new supplier only after the contract date), "the
  only new contract" vs the contract list, contributions vs expenses, "no debts" vs unpaid invoices.
- One cut-off date for everything; the title says it ("Situație la 31.10.2026").
- Cash reconciliation for anything with money in and out: opening + receipts - payments = closing =
  bank statement + cash book; funds (rulment, reparații) tied to it.
- Agenda vs votes: every proposal in the report is on the agenda and on the proxy form, and vice versa.
- Dossier completeness: everything announced is attached; opis with page ranges computed from the
  parts; bookmarks numbered like the opis; continuous page labels (`uneste(..., etichete_continue=True)`).
- No half-empty pages: chapters flow (`pregateste(d, h1_pagina_noua=False)`), only annexes start a
  page; landscape tables go to the last annex; move notes under a table into its source line rather
  than leaving a total row alone on a page. `check_doc.py` FAILs a page > 60 % empty that does not end
  a chapter (PDF input, or `--word` for .docx).
- DOOM "de": `cifre_ro.cu_de(n, "lei")` -> "1.800 de lei", "103 persoane", "16,21 lei", "2.788 mc"
  (unit symbols take no "de"); the checker FAILs violations.
- Plausible document numbers: `cifre_ro.NumereDocumente` (monotonic per issuer, irregular gaps;
  own registers such as the chitanțier strictly sequential). Never a constant step.
- Post-merge metadata: see pdf_ro below; check /Info against XMP and against the file header.

Word - `docx_complex.py` (import with docx_ro; call `dc.pregateste(d)` right after `new_document`):
- Headings via `dc.titlu(d, text, nivel)`: 1. / 1.1. / 1.1.1. from ONE multilevel list linked to
  Heading 1-3, never typed (A1.2-5). Unnumbered but in TOC: `titlu_nenumerotat` (Rezumat);
  annexes `anexa(d, 1, "Titlu")`, new page, outside the numbering, own caption sequence (A19).
- Sections (A14-17): `coperta` -> `sectiune(d, format_numar="lowerRoman", start=1, leaga_antet=False)`
  + `antet_gol` + `subsol_numar` for front matter -> `sectiune(..."decimal", start=1, leaga_antet=False)`
  + `antet_raport` + `subsol_numar(sec, "pagina_din", "_UltimaPagina")` + `marcaj_final(d)` at the end
  (NUMPAGES would count cover and front matter). Wide table: `sectiune(d, orientare="peisaj")` ...
  `sectiune(d, orientare="portret")`; place it at a chapter end so it leaves no half-empty page.
- `cuprins(d)` (TOC \o "1-3" \h \z \u) and, with >= 5 tables/figures, `lista_legende(..., pagina_noua=True)`.
- Text with `dc.paragraf(d, "... {{ref:cheie}} ... {{nota:footnote text}} ... **bold**")`: REF fields to
  captions (forward references allowed; `referinte_nerezolvate(d)` must be empty before save), real
  Word footnotes (A13), `{{pag:cheie}}` for PAGEREF. Cite every table/figure in the text before it.
- `tabel_pro(...)`: caption above with SEQ + bookmark, table style "Tabel raport" (horizontal rules,
  shaded header, header row repeated, rows never split, fixed widths), numbers right-aligned with their
  headers, units in the header, totals computed in code, `sursa=` / `nota_text=` line under it, short
  tables kept on one page (A21-31). `figura(...)`: inline PNG, alt text, caption below (A32).
  Charts: opaque PNG (matplotlib, 200 dpi), greys + one accent, direct labels, no gridlines.
- `lista(d, items, numerotata=True/False)` for real lists (en dash bullets).
- `docx_ro.finalizeaza_in_word(path)` now updates every field (body, footnotes, headers/footers, TOC,
  tables of figures) twice before saving.

Excel - `xlsx_ro.py` (B36-56): `registru` (named styles Intrare/Antet/Total/Notă, properties),
`foaie` (buffer row/col, no gridlines), `tabel_excel` (named ListObject, calculated columns with full
structured refs `tbl[[#This Row],[Col]]`, total row with SUBTOTAL outside the table), `nume` (named
cells; list sources as names over Table columns), `validare_lista/data/numar/formula` (input message +
stop alert), `format_conditionat` (one meaning, one hue), `verificare` (OK/EROARE cells),
`grafic_coloane/grafic_bare` (one series, data labels, no legend/gridlines/value axis, title linked to
a cell), `protejeaza` (no password, inputs unlocked), `tiparire` (A4, 1 page wide, print titles,
centred, header + "Pagina X din Y"), `ascunde_restul` (dashboard), `salveaza` (Excel recalculation,
absPath strip, chart label format patch, scan for #REF!/EROARE), `constante_in_formule`.
Rates and tolerances live in Parametri cells, never inside formulas. Check the cached values against
the Python model the Word text uses (runda3/build_xlsx.py does it).

PDF - `pdf_ro.py` (C57-68): `word_la_pdf(docx, etichete=[(0,"Copertă","",1),(1,"","r",1),(n,"","D",1)])`
(bookmarks from headings, tags, doc properties, /Lang ro-RO, DisplayDocTitle, opens on bookmarks;
`pdfa=True` -> PDF/A-1, opaque images only); `excel_la_pdf(xlsx, foi=[...])` (RO separators, chosen
sheets as one print job); `uneste(parti, out, titlu, autor)` (dossier: one bookmark per part + its own
bookmarks one level down, page labels per part, Unicode prefixes); `verifica(pdf)`.
Limits: a merged dossier is not tagged and not PDF/A (PyMuPDF and pypdf drop the structure tree;
deliver the tagged Word PDFs of the parts as the accessible copies); no veraPDF/PAC on this PC.
Honest metadata: single-source PDFs keep Word's Producer/Creator; `uneste` sets Producer =
"MuPDF x.y" (it wrote the file, the header says so), Creator = the apps of the parts ("Microsoft®
Word 2021; Microsoft® Excel® 2021"), CreationDate/ModDate = now, XMP mirroring /Info, no stray
/Info in the Catalog. `verifica` FAILs Producer "Word" on a file written by MuPDF and /Info-XMP
mismatches. `docx_ro.finalizeaza_xlsx` restores lastModifiedBy = creator (Excel stamps the PC user);
`finalizeaza_in_word` re-applies ro-RO to every story after the TOC update (TOC runs came out en-GB).
`lista_legende(..., alte_secvente=("TabelulA1", ...))` lists annex tables too.

Checks: `check_doc.py` now also FAILs on field errors ("Error! Reference source not found",
"Eroare! ..."), leftover `{{ref:` markup, 3+ empty paragraphs, long tables without repeated header,
images without alt text, a long document (>= 8 pages, >= 10 headings) without TOC or with an
empty TOC, PDF >= 10 pages without bookmarks / without title / with non-embedded fonts, workbook
formula errors or EROARE checks, input sheets without validation, sheets without print setup; WARNs
on hard-coded constants in formulas, hand-typed captions, missing page labels, /Lang, DisplayDocTitle.
`--word` checks through Word which tables really cross a page and exports a temporary PDF to find
half-empty pages. Round-3b additions: FAIL on pages > 60 % empty not ending a chapter, DOOM "de"
errors, lastModifiedBy != author (docx/xlsx), PDF Producer vs file header / XMP mismatch, stray
Catalog /Info; `--date model.json` FAILs amounts in the text missing from the model (WARN for counts). Then render and look at every page:
the checker cannot see a half-empty page, an orphaned source line or labels colliding in a chart.

## 8. Delivery report to the owner (Romanian)

Path of the file, what was assumed, which placeholders remain, checker result (PASS/WARN counts).

## 7c. Lessons from the final phase-3 review (2026-11-22)
- Commitments against one fund/budget are summed before stating any remaining balance: if the
  terrace (19.800) and the meters (3.600) both come from the repair fund, the text must show
  23.400 vs the balance and say how the gap is covered. Check every "rămâne"/"disponibil" claim.
- Derived totals (averages, "media + cotizație") are computed from the SAME rounded values the
  reader sees, or the difference is explained - a 1-ban gap in a total is a finding.
- Every "peak/maximum/minimum" claim in prose is checked against the series (max() in code).
- Locate pages for bookmarks/opis by the UNIQUE heading text (exact match of the first line),
  never by a substring that can also occur in a table row.
- Lists of tables/figures: no empty paragraphs between entries (TOC field switches, not spacers).
- Merged PDFs: Creator = the application of the main part only (pdf_ro.uneste does it).
- Review sequence that worked: one data model -> generated text/tables/Excel -> assertion pass ->
  render + look -> blind review by a fresh reviewer -> fix -> delta review. Keep it.
