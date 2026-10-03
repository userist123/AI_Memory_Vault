# Book-to-Memory Source Inventory Audit

Date: 2026-10-03
Branch: `research/book-to-memory`
PR: #206

## Purpose

This audit reconciles the declared Book-to-Memory source manifest with the research maps currently present in the branch.

It deliberately distinguishes:
- source identity/inventory;
- repository-copy evidence;
- external bibliographic evidence;
- engineering hypotheses;
- runtime evidence.

A Vault-derived corpus or an existing ACTIVE note is **not** accepted as a substitute for the source copy when exact source/page evidence is required.

## Inventory status

The manifest declares 20 source entries.

| Source key | Current map | Exact repository-copy evidence |
|---|---:|---|
| 7688_jkt_au | yes | blocked: source copy absent from branch |
| comparison_cognitive_architectures | no | blocked |
| wiener_cybernetics | no | blocked |
| kandel_2001_molecular_biology_of_memory | no | blocked |
| quantum_consciousness_framework | no | blocked |
| 2504.05840v1 | no | blocked |
| sarfraz22a | no | blocked |
| wcs_1488 | no | blocked |
| machine_learning_report | no | blocked |
| 2601.09113v1 | no | blocked |
| memory_in_the_age_of_ai_agents | no | blocked |
| ashby_intro_to_cybernetics | no | blocked |
| why_we_forget | yes | blocked: exact repository copy absent |
| squire_kandel_mind_to_molecules | yes | blocked |
| ashby_design_for_a_brain | no | blocked |
| schacter_tulving_memory_systems_1994 | yes | blocked |
| minsky_society_of_mind | no | blocked |
| laird_soar_cognitive_architecture | yes | blocked |
| newell_how_can_human_mind_occur | no | blocked |
| newell_unified_theories_of_cognition | yes | blocked |

## Current map coverage

Six of the 20 manifest entries have dedicated maps in PR #206:
- 7688_jkt_au / Soar;
- why_we_forget;
- squire_kandel_mind_to_molecules;
- schacter_tulving_memory_systems_1994;
- laird_soar_cognitive_architecture;
- newell_unified_theories_of_cognition.

The remaining 14 entries are inventory-only at this stage.

## Evidence rule

The branch currently has enough information to preserve source identity and research scope, but not enough to claim exact page-level evidence from the repository copies.

Therefore:
1. no new atomic note is created from an unavailable source copy;
2. no page number is inferred from a secondary map;
3. no existing ACTIVE Vault note is promoted or re-labelled as book evidence;
4. no production mechanism is derived from these sources;
5. no H1 held-out case is selected from a map alone.

## Next admissible step

When the exact source snapshot becomes available to the research execution environment:
1. hash and freeze the source snapshot;
2. verify bibliographic identity and edition;
3. extract only problem-relevant chapters/sections;
4. record exact page ranges;
5. write falsifiable engineering hypotheses;
6. register controls, contamination risks and failure criteria;
7. create experiments before any implementation;
8. keep all outputs research-only until empirical validation and explicit owner approval.

## Classification caution

The manifest is a mixed research-source inventory. Names alone do not establish whether an entry is a book, paper, report, survey, or other source. Classification should be performed from the source metadata/copy rather than inferred from filenames.

## Conclusion

The inventory is complete as a manifest reconciliation, but literature processing is **not complete**. The exact source-copy availability gate remains the limiting dependency. Completing the maps by guessing would reduce reproducibility rather than improve it.
