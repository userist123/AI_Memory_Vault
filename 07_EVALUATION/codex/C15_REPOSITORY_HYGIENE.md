# R001 C15 — Repository hygiene forensics

Evidence level: `CODE_VERIFIED` + `TEST_VERIFIED` for the scan and guard.

## Baseline

- Branch: `codex/r001-c15-hygiene-v1`
- Baseline: `061c61ea0dcca24a9e517a9d47b24becd667bbdd`

## Findings

An exact repository search outside `06_INBOX/RAW_IMPORTS` found no `fileciteturn` token and no `filecite` immediately followed by `turn` without the Unicode delimiters. Three README lines contain the structured citation form `fileciteturn...`; they are located at README lines 489, 491 and 525. They were introduced by historical README/evaluation commits, including `23e107fff` (README rebuild) and later forensic updates. The delimiters are present, so this lane does not classify them as malformed and does not rewrite legitimate citation syntax.

## Guard added

`tests/test_repository_hygiene.py` scans repository-generated text formats, excluding raw imported material, and rejects the known concatenated `fileciteturn` / `filecite turn` forms. It does not reject the structured Unicode citation syntax currently present in the README.

## Scope and safety

No source PDFs, raw imports, canonical memory, security controls, or other agent evaluation lanes were modified. No content was executed.
