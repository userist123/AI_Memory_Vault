"""Propose what kind of work a request is asking for, and say why.

Level 1 was `--intent measure`: the sender already knew. This is level 2 — the
vault reads the request, proposes a kind with its reasons, and flags the case
that matters most: when the literal request and the actual question disagree.

It is a rule engine over surface patterns, not comprehension. It proposes and
explains; it never silently selects. Every proposal carries the evidence that
produced it, so a wrong one is visible rather than merely wrong.

The valuable output is not the label. It is the conflict:

    "promote the proposed edges"
        literal intent   implement   (an action verb, on production state)
        semantic intent  measure     (the object's quality is unestablished)

Treated as `implement`, that request promotes some two thousand edges. Treated
as `measure`, it samples fifty and stops at 18% precision against a 70% bar,
which is what happened. The difference is not intelligence. It is asking
whether the thing being acted on has been shown to be good enough to act on.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field

#: Surface markers per kind. Deliberately small: each was drawn from requests
#: that actually occurred, not from a thesaurus.
MARKERS: dict[str, tuple[str, ...]] = {
    "measure": (
        r"\b(?:vezi )?dac[ăa]\b", r"\bwhether\b", r"\bdoes .* help\b", r"\bajut[ăa]\b",
        r"\bcompar[ăa]?\b", r"\bcompare\b", r"\bmerit[ăa]\b", r"\bworth it\b",
        r"\bm[ăa]soar[ăa]\b", r"\bmeasure\b", r"\bbenchmark\b", r"\bevaluate\b",
    ),
    "verify": (
        r"\bverific[ăa]?\b", r"\bverify\b", r"\bcheck\b", r"\baudit\b",
        r"\bchiar (?:e|este)\b", r"\bis it (?:actually|really)\b", r"\bconfirm\b",
        r"\bce a f[ăa]cut\b", r"\bwhat did .* do\b",
    ),
    "fix": (
        r"\brepar[ăa]?\b", r"\bfix\b", r"\bstricat\b", r"\bbroken\b", r"\bcrap[ăa]\b",
        r"\bcrash", r"\bbug\b", r"\bnu (?:merge|func[țt]ioneaz[ăa])\b", r"\bfails?\b",
    ),
    "migrate": (
        r"\bmut[ăa]?\b", r"\bmove\b", r"\bredenume[șs]te\b", r"\brename\b",
        r"\b[șs]terge\b", r"\bdelete\b", r"\bremove\b", r"\bcur[ăa][țt][ăa]?\b",
        r"\bclean ?up\b", r"\bmigrate\b", r"\bconsolidate\b",
    ),
    "implement": (
        r"\bconstruie[șs]te\b", r"\bbuild\b", r"\badaug[ăa]?\b", r"\badd\b",
        r"\bcableaz[ăa]\b", r"\bwire\b", r"\bimplement", r"\bcreate\b", r"\bfac?e?\b",
        r"\bpromoveaz[ăa]\b", r"\bpromote\b", r"\benable\b", r"\bactiveaz[ăa]\b",
    ),
}

#: Objects whose quality is not established until someone measures it. An
#: action requested ON one of these is a question about quality wearing the
#: clothes of an instruction.
UNVALIDATED_OBJECTS = (
    r"\bpropuner", r"\bpropos(?:al|ed)\b", r"\bcandidat", r"\bcandidate\b",
    # Romanian attaches the definite article to the noun, so "muchiile propuse"
    # does not contain "muchii propuse". A first version missed exactly the
    # request this module exists to catch, and the measurement showed it.
    r"\bpropus[ăae]?\b", r"\bdraft\b", r"\bexperimental\b", r"\bsugesti",
    r"\bunverified\b", r"\bneverificat", r"\bREVIEW\b",
)

#: Actions that change production state. Combined with an unvalidated object,
#: these are the conflict this module exists to catch.
PRODUCTION_ACTIONS = (
    r"\bpromoveaz[ăa]\b", r"\bpromote\b", r"\bactiveaz[ăa]\b", r"\benable\b",
    r"\bmerge\b", r"\bdeploy\b", r"\bpune [îi]n produc[țt]ie\b", r"\bship\b",
)

#: Destructive actions. A migrate brief demands a proven recovery path, so a
#: request that looks like implement but destroys must not be mislabelled.
DESTRUCTIVE = (r"\b[șs]terge\b", r"\bdelete\b", r"\bremove\b", r"\bdrop\b", r"\bpurge\b")


@dataclass
class Inference:
    intent: str
    confidence: str
    reasons: list[str] = field(default_factory=list)
    conflict: str | None = None
    alternatives: list[str] = field(default_factory=list)

    def render(self) -> str:
        lines = [f"detected intent : {self.intent}", f"confidence      : {self.confidence}"]
        for r in self.reasons:
            lines.append(f"  because       {r}")
        if self.alternatives:
            lines.append(f"also plausible  : {', '.join(self.alternatives)}")
        if self.conflict:
            lines.append("")
            lines.append("CONFLICT DETECTED")
            lines.append(self.conflict)
        return "\n".join(lines)


def _hits(text: str, patterns) -> list[str]:
    found = []
    for pat in patterns:
        m = re.search(pat, text, re.I)
        if m:
            found.append(m.group(0).strip())
    return found


def infer(request: str) -> Inference:
    text = request.strip()
    scores = {kind: _hits(text, pats) for kind, pats in MARKERS.items()}
    ranked = sorted(scores.items(), key=lambda kv: (-len(kv[1]), kv[0]))
    top, top_hits = ranked[0]

    unvalidated = _hits(text, UNVALIDATED_OBJECTS)
    prod_action = _hits(text, PRODUCTION_ACTIONS)
    destructive = _hits(text, DESTRUCTIVE)

    reasons = [f"matched {h!r} → {top}" for h in top_hits[:3]]
    alternatives = [k for k, v in ranked[1:] if v and len(v) >= len(top_hits)]
    conflict = None

    # The case worth building this for: acting on something unproven.
    if prod_action and unvalidated:
        conflict = (
            f"literal intent   {top} — {prod_action[0]!r} changes production state\n"
            f"semantic intent  measure — the object ({unvalidated[0]!r}) has no established quality\n"
            "\n"
            "Acting first assumes the answer to the question actually being asked.\n"
            "Selecting: measure. Required before any promotion: a hand-verified\n"
            "sample, a pre-registered threshold, and a stop condition."
        )
        return Inference("measure", "high", reasons, conflict, alternatives)

    # Enabling something in production is itself a claim that it has been
    # shown to help. Every time that claim went unchecked this session it was
    # wrong: graph expansion reported "ok" while adding nothing, and the arm
    # would have shipped as "no significant difference".
    if prod_action and top != "measure":
        conflict = (
            f"literal intent   {top} — {prod_action[0]!r} changes what production does\n"
            "semantic intent  measure — enabling something asserts it helps\n"
            "\n"
            "Selecting: measure. If the evidence already exists, override with\n"
            "--intent implement and cite the measurement in the brief."
        )
        return Inference("measure", "medium", reasons, conflict, alternatives)

    # Destruction hiding inside a request that reads as ordinary work.
    if destructive and top not in ("migrate", "fix"):
        conflict = (
            f"literal intent   {top}\n"
            f"semantic intent  migrate — {destructive[0]!r} destroys existing material\n"
            "\n"
            "Selecting: migrate. A recovery path must exist and be verified\n"
            "before the first destructive operation."
        )
        return Inference("migrate", "high", reasons, conflict, alternatives)

    if not top_hits:
        return Inference(
            None, "none",
            ["no marker matched"],
            "This request matches no known pattern. State --intent explicitly.\n"
            "Guessing is worse than asking: a misclassified brief silently omits\n"
            "the requirements that kind of work needs, and the omission is\n"
            "invisible until the work is finished wrongly.",
            [],
        )

    # Measured rather than assumed. On requests held out from tuning this
    # module scored 50%, against 100% on the requests its own patterns were
    # drawn from, and four of five errors carried a single weak marker. So a
    # single marker refuses instead of proposing: a confident wrong label costs
    # more than an admitted ignorance.
    if len(top_hits) == 1:
        return Inference(
            None, "low", reasons,
            f"Only one weak marker matched ({top_hits[0]!r} → {top}).\n"
            "State --intent explicitly. Measured accuracy on unseen requests is\n"
            "50%, so a single-marker guess is close to a coin toss.",
            alternatives,
        )

    confidence = "high" if len(top_hits) >= 2 and not alternatives else "medium"
    return Inference(top, confidence, reasons, None, alternatives)


if __name__ == "__main__":
    import sys

    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        raise SystemExit('usage: python infer_intent.py "the request"')
    print(infer(" ".join(sys.argv[1:])).render())
