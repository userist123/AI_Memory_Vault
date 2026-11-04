# Interpretation — navigation benchmark, run 2026-10-30

**Pre-registered decision: no demonstrated difference.** Sonnet found the gold fact in the gold file in 43 of
48 runs in every condition: plain search (G), flat index (I) and vault routes (V). Per-question V better /
worse than the best baseline: 1 / 1 (sign test p = 1.0).

**Cost:** V used the most context: median 70.5k tokens per run against 61.9k for G (+14%) and 63.7k for I,
one more turn (median 5 vs 4) and the highest estimated cost (2.94 vs 2.62 and 2.06 USD API-equivalent for 48
runs each). On this question set the route layer used more context and turns; wall-clock time was not measured.

**The 15 failed runs are consistent with gold-set defects** (each condition lost 5; 12 named another file — most
of which state the same fact — and 3 named the gold file but failed the regex). That the agents navigated well is an
interpretation of the cause, not something this run demonstrates:
- N13 and N16/N17: the same fact also lives, reworded, in another tracked file
  (`00_GOVERNANCE/phase_0/CORPUS_HEALTH_BASELINE.md`; a sibling validation report; a workflow `.yml` and a
  test). The uniqueness check compared the exact sentence, not the fact.
- N08 and N06: the fact is also in the code the note documents (`security/skill_exfiltration_scanner.py`,
  `interfaces/telegram_vault_bot.py`); agents cited the code.
- N06: the regex `(?<![\d.])3(?![\d.])` rejects "3." at the end of a sentence — a grading bug.
The gold set had already needed 9 of 24 items fixed at cross-family validation (`GOLD_VALIDATION.md`); the
string-equality uniqueness check missed these further duplicates. Post-hoc sensitivity (`SENSITIVITY.md`;
it cannot replace the pre-registered result): counting the fact whatever file is
named, and fixing the N06 regex, gives G 46/48, I 47/48, V 47/48 — every condition at the ceiling, i.e. by the
pre-registered rule the question set would be "not discriminating".

**What this means for the goal "beats anything that exists, and provably":** not demonstrated here — one
repository, 24 single-fact questions, one model, where an agent with grep found the fact in a median of 4
turns. Claims that the vault's navigation layer outperforms search or an index must not be made from this run.

**What this benchmark did not measure**, where the route layer is built to matter:
- clients without file access (claude.ai web export, Telegram/Ollama bot): they cannot grep at all;
- the access policy (per-principal domains, classification, trust) and verbatim citations with sha256 and
  line ranges, which plain search does not provide;
- questions with no lexical overlap with the answer (topical / paraphrased), multi-hop questions, and smaller
  models (Haiku, a local model) that search less well.
A next, pre-registered run should target those, with a gold set whose uniqueness check is semantic
(cross-family judged), not string equality.

Owner check: `OWNER_SAMPLE.md` (29 runs, fixed seed).

Cross-family review: Codex, read-only, from a self-contained bundle; its objections (overstated plausibility
claim, cause stated as fact, omitted 9/24 validation fixes and the path/fact split) are reflected above.
