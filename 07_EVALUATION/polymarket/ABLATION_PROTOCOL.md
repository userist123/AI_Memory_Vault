# Ablation Protocol

## Purpose

Establish which components of the system earn their place, by removing each and
measuring what happens. A component whose removal changes nothing is not
helping, whatever its author intended.

## The ladder

Section 22 of the brief lists these. The whole ladder is run, not a subset.

```
no memory
memory retrieval only
graph retrieval
evidence retrieval
memory + graph
memory + evidence
full system
full system without calibration
full system without abstention
full system without resolution-risk filter
full system without liquidity filter
```

The combinations are there for a reason. Dropping memory and dropping the graph
separately says nothing about dropping both: if they are substitutes, each looks
useless alone while the pair is essential. Single drops alone would report both
as dead weight.

## Direction

`full_system` is the baseline; the ablated variant is the challenger. A
component that helps therefore produces `CHALLENGER_WORSE` — **removing
something useful should look bad.** The convention is asserted in the tests,
because an inverted sign here would report every useful component as dead
weight and every dead component as useful.

## The correction, and why it is not optional

Eleven comparisons at 95% confidence, under a true null where nothing helps,
produce at least one apparent finding with probability

```
1 - 0.95 ** 11 = 0.43
```

Forty-three percent. Reporting the one rung that cleared the threshold, without
saying eleven were run, is the textbook shape of a result that does not
replicate. Section 33 names it directly: avoid p-hacking.

So every run applies **Holm-Bonferroni** across the whole family. Holm rather
than plain Bonferroni: dividing alpha by eleven for every hypothesis is
conservative enough to hide real effects, and a method that can only produce
nulls is no more honest than one that can only produce findings.

Both verdicts are reported — the raw one and the corrected one — so the effect
of the correction is visible rather than applied silently. A reader who
disagrees with the correction can see exactly what it changed.

### The family size comes from the ladder

Not from the comparisons that looked interesting. Correcting across the three
you decided to report is the same error wearing a correction, and nothing in
the code can detect it: `family_size` is however many rungs were passed in.
This is a discipline the protocol imposes, not a guarantee the implementation
provides, and it is written here because that distinction matters.

## What cannot be promoted

A rung whose confidence interval contains zero scores `crossing_fraction` 1.0,
which is above every Holm threshold. **No correction can turn a null into a
finding**, and an implementation that could would be the bug. There is a test
for exactly this.

An underpowered rung — fewer than 100 paired observations — stays
`UNDERPOWERED` after correction. A correction cannot turn too little data into
a conclusion.

## Reading the report

`components_that_earn_their_place()` returns the components whose removal made
the system measurably worse **after correction**. It is allowed to return an
empty tuple, and on a system with no edge it should.

An empty result does not mean the run failed. It means no component was shown
to help, which is a finding about the system and is reported as one.

## What an ablation does not measure

Anything outside the ladder. Interactions between components not paired in the
ladder were not tested, and the report says so rather than implying coverage it
does not have.

Nor does an ablation say *why* a component helps. It says removal hurt. The
mechanism is a separate question and this protocol does not answer it.
