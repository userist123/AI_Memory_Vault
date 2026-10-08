# B06 — instructions for the owner

**Status of B06: waiting on owner labels.** The metrics of the Book-to-Memory track were calibrated on a machine-made
corpus; nothing here has been checked by a person. This packet lists what only a person can decide. No label is filled in.

## What to do

1. Open `labels_to_fill.csv` (UTF-8, comma-separated) in a spreadsheet. `items_readable.md` shows the same items as text.
2. For each row you can judge, fill:
   * `label`: one of the values in `allowed_values` of that row (lower case).
   * `labeler_id`: your short id, the same on every row you fill (required for a labelled row).
   * `other_relevant_ids` (H1 rows only, optional): ids of other vault notes that answer the query better or equally.
   * `comment` (optional).
   Leave `label` blank when you cannot judge; blank rows are counted as unlabelled and nothing is assumed for them.
3. Do not edit any other column and do not reorder or delete rows.
4. Save as `labels_<your id>.csv` and run:

   `python 30_SCRIPTS/evaluation/b2m_ingest_labels.py --labels labels_<your id>.csv`

   Several labelers each fill their own copy; pass all files and the report adds the agreement between them.

## What is asked

| Rows | Count | Question | Allowed labels |
|---|---:|---|---|
| H1 gold relevance (`h1_gold_relevance`) | 80 | Does the gold note answer the query? | `yes`, `partial`, `no` |
| Candidate-note faithfulness (`candidate_note_faithfulness`) | 51 | Is the definition faithful to its quoted source passage? | `faithful`, `partial`, `unfaithful`, `not_a_term` |

`priority` 1 (83 rows) is the held-out H1 cases and every candidate note: if you only have time for part of the packet, do those.
A rate is reported only for a type with at least 20 labelled items; below that the report says `INSUFFICIENT_LABELS` and gives no rate.

## What the labels are used for

* The share of H1 gold notes a person judges relevant (with a Wilson 95% interval), and the held-out baseline recall@10
  recomputed over the cases whose gold a person confirmed.
* The share of candidate notes whose definition is faithful, overall and by the confidence the note claims about itself
  (all the notes claim `high`), which tests whether that claim is calibrated.
* With two or more labelers: Cohen's kappa and Krippendorff's alpha between them. Items on which labelers disagree are
  listed as disputed and left out of the rates, never settled by a vote.

Nothing in this packet promotes a note or changes a lifecycle state; a label is evidence for the calibration report only.
