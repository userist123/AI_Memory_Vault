"""UserPromptSubmit hook: when the prompt asks to design/build/restyle a website, landing page, app screen or UI component,
add one line that starts the designer-ui skill (tier router + QA checks). Silent for every other prompt. Never blocks (exit 0)."""
import json
import re
import sys

TRIGGER = re.compile(
    r"landing\s*page|site(ul)?\s+(web|de\s+prezentare|nou)|pagin(ă|a|i)\s+web|website|web\s*design|redesign|"
    r"design(ul)?\s+(pentru|de|la)\s+(aplica|site|pagin|app|dashboard|ecran)|"
    r"\b(ui|ux)\b|interfa(ț|t)(ă|a)|ecran(ul)?\s+de\s+aplica|app\s+design|aplica(ț|t)ie\s+(mobil|web)|"
    r"dashboard|magazin\s+online|e-?commerce|portofoliu|portfolio|onboarding|"
    r"(fă|fa|construie(ș|s)te|creeaz(ă|a))(-mi)?\s+(un|o)\s+(site|pagin|landing|interfa|dashboard|formular|ecran)|"
    r"culori(le)?\s+(pentru|de\s+la|din)\s+(site|aplica|pagin)|palet(ă|a)\s+de\s+culori|tipografie|font(uri)?\s+pentru|"
    r"s(ă|a)\s+arate\s+(premium|modern)|s(ă|a)\s+nu\s+par(ă|a)\s+f(ă|a)cut\s+de\s+ai|mai\s+modern|mai\s+fluid|"
    r"nu\s+e\s+p(ă|a)tr(ă|a)(ț|t)os|text(ul)?\s+.*\s+(chenar|caset|card)",
    re.IGNORECASE,
)
# requests that look similar but belong to other skills
AUTOMATED = re.compile(r"^\s*(<(task-notification|agent-message|teammate-message|child-session-event|webhook-payload|wake|cross-session|scheduled-task|system-reminder|command-name|command-message|command-args|local-command)\b|\[subagent hand-back\])", re.IGNORECASE)
EXCLUDE = re.compile(r"\b(word|docx|excel|xlsx|pdf|blender|animatie\s*3d|animație\s*3d|clip\s+video)\b", re.IGNORECASE)


def main():
    try:
        data = json.loads(sys.stdin.buffer.read().decode("utf-8-sig", errors="replace"))
        prompt = data.get("prompt", "") if isinstance(data, dict) else ""
        if not isinstance(prompt, str):
            return
    except Exception:
        return
    if AUTOMATED.match(prompt) or not TRIGGER.search(prompt) or EXCLUDE.search(prompt):
        return
    msg = ("designer-ui: this owner message looks like web/app design work. Owner's standing rule: the `designer-ui` skill handles it - "
           "it picks the smallest fitting tier T1-T4, loads only that tier's modules, runs references/manual-qa-checks.md before delivery "
           "(320-1440 px sweep in 20 px steps, text inside boxes, one-line labels, menu, tables on mobile, reduced-motion), "
           "and from T2 on an independent reviewer judges screenshots taken while scrolling.")
    out = {"hookSpecificOutput": {"hookEventName": "UserPromptSubmit", "additionalContext": msg}}
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(out, ensure_ascii=False))


main()
