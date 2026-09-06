# R016 — graph expansion measured through the production path

First paired measurement that actually runs `MemoryController.search()`. v1's
runner built its own retriever and never touched the controller.

## Result: graph expansion does not help, and on this set it hurts slightly

| Arm | ran | candidate recall | context recall | answer correctness |
|---|---:|---:|---:|---:|
| graph OFF | 30/30 | 0.77 | 0.23 | 0.23 |
| graph ON (strict) | 23/30 | 1.00 | 0.22 | 0.22 |

McNemar on the paired cases:

| Metric | worse with graph | better with graph |
|---|---:|---:|
| candidate recall | 0 | 0 |
| context recall | **2** | **0** |
| answer correctness | **2** | **0** |

Zero cases improved. Two got worse. The graph-on candidate recall of 1.00 is
survivorship, not a gain: it is computed over the 23 cases that ran, after
strict mode excluded the 7 where expansion could not produce anything.

**Recommendation: leave `enable_graph_expansion=False`.** Turning it on costs
work and buys nothing measurable on this corpus.

## The larger finding is not about the graph

Candidate recall is 0.77 while context recall is 0.23. The gold note is
retrieved as a candidate three times out of four and then dropped before the
context pack more than two thirds of the time.

The bottleneck is ranking and packing, not candidate generation and not the
graph. r004 fixed candidate generation; nothing has yet looked at what happens
between 200 candidates and 10 packed notes. That is where the next work is.

## Two defects found only by running it

**The r004/r009 budget interaction.** Expansion computed
`min(2*len(notes), 20) - len(notes)`. Once r004 raised the lexical candidate
limit to 200, that is `min(400, 20) - 200`, i.e. negative, so every query
expanded exactly nothing while reporting status `ok`. Two independently
correct changes cancelling each other, invisible to every test because the
tests use small mock corpora where the formula still works.

This is left UNRESOLVED on purpose. The 20-candidate cap is a deliberate
context-budget guarantee asserted by `test_graph_expansion.py`; r004's limit of
200 is a deliberate recall guarantee. They are incompatible and the resolution
is an owner decision, not a constant to flip while measuring. The default is
unchanged; `graph_expansion_budget` exists as an explicit override and this
measurement used it at 10.

**Reporting `ok` for zero work.** Strict mode originally caught only
`degraded_*` statuses. Expansion that ran, found nothing and reported success
made the arm identical to the baseline while looking healthy — the exact
failure mode strict mode exists to prevent. It now also fails on zero
expansion, which is what produced the 7 excluded cases above.

An earlier version of that check read `graph_expanded_ids` before the field was
populated and fired on every query: a false alarm inside the guard meant to
catch false results. Fixed by moving it after the assignment.

## Caveats

The graph class carries 10 cases against a ceiling of ~33 disjoint ones, so
this measurement can detect a large effect and not a subtle one. "No
improvement" here means "no improvement detectable at n=10", not "the graph
cannot help". The direction of the two discordant pairs is nonetheless
unfavourable, and nothing in the data argues for enabling it.
