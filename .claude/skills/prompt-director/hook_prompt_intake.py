"""UserPromptSubmit hook (prompt-director, intake mode).

Adds one context line when the owner's own message is a task or a request, so the main session
compiles it into a META-PROMPT + TASK PROMPT pair (prompt-director SKILL.md section 6) before working.

Decision cascade (precision first; when unsure, stay silent - the skill still applies to obvious tasks):
  1. turns the harness starts itself -> silent (subagent hand-backs, task notifications, messages from
     other sessions, webhooks, slash-command / bash-mode echoes);
  2. stop orders, acknowledgements and "continue" follow-ups -> silent;
  3. explicit request phrase ("poți să", "te rog", "vreau", "fă-mi", imperative + clitic) -> task;
  4. question (ends with '?', or starts with a question word) -> silent;
  5. request verb present -> task; otherwise silent.
Pasted blocks are ignored when the owner also typed words; a paste alone is classified on the paste.

Design notes (sources in SKILL.md section 7): a prompt-type hook cannot add context on this event, only
block, so classification is local; the context is phrased as a fact, not a system command, because
command-like hook text can trip prompt-injection defences. Stdlib only, no network, never blocks.
"""
import json
import re
import sys
import unicodedata

MAX_CHARS = 20000  # classify at most this much text; long pastes do not change the verdict

AUTOMATED = re.compile(
    r"^\s*(<(task-notification|agent-message|teammate-message|child-session-event|webhook-payload|wake|"
    r"cross-session|scheduled-task|system-reminder|command-name|command-message|command-args|local-command)\b"
    r"|\[subagent hand-back\]|caveat:|!)",
    re.IGNORECASE,
)
PASTE_OPEN = "<pasted_content"
PASTE_CLOSE = "</pasted_content"

CLITIC_IMPERATIVE = r"(?!(?:ce|cum|cine|unde|c[âa]nd|care|de|nu|s[ăa])-)\w+[ăaeiî]-(?:mi|ne|le|l|o|i)\b"

# Strong request phrases: a task even inside a question.
EXPLICIT_REQUEST = re.compile(
    r"\bpo[țt]i\s+s[ăa]\b|\bpute[țt]i\s+s[ăa]\b|\bai\s+putea\b|\bte\s+rog\b|"
    r"\bvreau\b(?!\s+s[ăa]\s+(?:[șs]tiu|aflu|[îi]n[țt]eleg|v[ăa]d\s+dac[ăa]))|"
    r"\ba[șs]\s+vrea\b|\bam\s+nevoie\b|\bplease\b|\b" + CLITIC_IMPERATIVE,
    re.IGNORECASE,
)
# Polite interrogative requests ("Poți verifica...?", "Ai putea...?"): a task only when the message
# does not open with a question word ("Ce-mi poți spune...?" stays a question).
POLITE_REQUEST = re.compile(
    r"\bpo[țt]i\s+(?!fi\b)\w+(?:a|e|i|î)\b|\bcould\s+you\b|\bcan\s+you\b",
    re.IGNORECASE,
)

REQUEST = re.compile(
    r"\b("
    r"f[ăa]|construie[șs]te|cre(?:e)?az[ăa]|scrie|redacteaz[ăa]|deschide|[îi]nchide|opre[șs]te|porne[șs]te|"
    r"repar[ăa]|rezolv[ăa]|corecteaz[ăa]|adaug[ăa]|pune|instaleaz[ăa]|dezinstaleaz[ăa]|configureaz[ăa]|seteaz[ăa]|"
    r"modific[ăa]|schimb[ăa]|actualizeaz[ăa]|mut[ăa]|copiaz[ăa]|[șs]terge|elimin[ăa]|cur[ăa][țt][ăa]|salveaz[ăa]|"
    r"redenume[șs]te|caut[ăa]|g[ăa]se[șs]te|cerceteaz[ăa]|verific[ăa]|analizeaz[ăa]|testeaz[ăa]|ruleaz[ăa]|"
    r"genereaz[ăa]|implementeaz[ăa]|preg[ăa]te[șs]te|trimite|descarc[ăa]|export[ăa]|import[ăa]|public[ăa]|"
    r"arat[ăa]|explic[ăa]|spune|traduce|rezum[ăa]|compar[ăa]|optimizeaz[ăa]|refactorizeaz[ăa]|integreaz[ăa]|"
    r"leag[ăa]|conecteaz[ăa]|[îi]ntreab[ăa]|termin[ăa]|reia|ref[ăa]|organizeaz[ăa]|planific[ăa]|calculeaz[ăa]|"
    r"make|build|create|write|fix|add|install|update|remove|delete|search|find|check|run|implement|generate|"
    r"deploy|refactor|rename|open|show|explain|move|send|publish|"
    r"s[ăa]\s+(?:porneasc[ăa]|fac[ăa]|faci|mearg[ăa]|func[țt]ioneze|ruleze|apar[ăa]|fie)"
    r")\b|^\s*s[ăa]\s+\w+",
    re.IGNORECASE,
)

STOP_ORDER = re.compile(
    r"^\s*(?:stai|stop|wait|don'?t|do\s+not|nu\s*[,.!]?\s*(?:nu|f[ăa]|mai|vreau|[șs]terge|face|acum|[îi]nc[ăa]))\b",
    re.IGNORECASE,
)
ACK_ONLY = re.compile(
    r"^\s*(da|nu|ok|okay|bine|mersi|mul[țt]umesc|gata|perfect|super|yes|no|go)\b[\s\w,.!]{0,25}$",
    re.IGNORECASE,
)
CONTINUE_FOLLOWUP = re.compile(
    r"^\s*(?:(?:da|ok|bine|gata|perfect)[\s,.!]+)?(?:continu[ăa]|mergi\s+mai\s+departe|mai\s+departe|go\s+on|continue)\b",
    re.IGNORECASE,
)
QUESTION_END = re.compile(r"\?[\W_]*$")
QUESTION_START = re.compile(
    r"^\s*(?:(?:[șs]i|dar|oare)\s+)?(ce|cum|de\s+ce|unde|c[âa]nd|cine|care|c[âa]t|c[âa]te|oare|am|ai|avem|are|"
    r"este|e|exist[ăa]|se|pot|trebuie|what|how|why|where|when|who|which|is|are|do|does|did|was|were|will|has|"
    r"have|should|can)\b",
    re.IGNORECASE,
)

CONTEXT = (
    "prompt-director (intake): this owner message is a task. Owner's standing directive (prompt-director "
    "section 6): a task is first compiled into a META-PROMPT + TASK PROMPT pair (verified state, numbered steps, "
    "acceptance check), the owner gets a Romanian summary of at most 8 lines, and work starts without asking "
    "for confirmation."
)


def normalize(text: str) -> str:
    text = unicodedata.normalize("NFC", text)
    return (text.replace("ş", "ș").replace("ţ", "ț")
                .replace("Ş", "Ș").replace("Ţ", "Ț"))


def strip_pastes(text: str) -> tuple[str, bool]:
    """Remove <pasted_content ...> ... </pasted_content ...> blocks in linear time."""
    out, pos, stripped = [], 0, False
    while True:
        start = text.find(PASTE_OPEN, pos)
        if start < 0:
            out.append(text[pos:])
            break
        end = text.find(PASTE_CLOSE, start)
        if end < 0:  # unclosed opener: keep the rest as typed text
            out.append(text[pos:])
            break
        close_end = text.find(">", end)
        close_end = len(text) if close_end < 0 else close_end + 1
        out.append(text[pos:start])
        out.append(" ")
        pos, stripped = close_end, True
    return "".join(out).strip(), stripped


def is_task(prompt) -> bool:
    if not isinstance(prompt, str):
        return False
    prompt = normalize(prompt.strip()[:MAX_CHARS])
    if not prompt or AUTOMATED.match(prompt):
        return False
    typed, had_paste = strip_pastes(prompt)
    text = typed if typed else prompt
    if text.startswith("/"):
        return False
    if not had_paste and len(text.split()) < 4:
        return False
    if STOP_ORDER.match(text) or CONTINUE_FOLLOWUP.match(text) or ACK_ONLY.match(text):
        return False
    if EXPLICIT_REQUEST.search(text):
        return True
    opens_with_question = bool(QUESTION_START.match(text) or re.match(r"^\s*\w+-(?:mi|ne|i)\b", text))
    if POLITE_REQUEST.search(text) and not opens_with_question:
        return True
    if QUESTION_END.search(text) or opens_with_question:
        return False
    return bool(REQUEST.search(text))


def main():
    try:
        raw = sys.stdin.buffer.read().decode("utf-8-sig", errors="replace")
        data = json.loads(raw)
        prompt = data.get("prompt", "") if isinstance(data, dict) else ""
    except Exception:
        return
    try:
        if not is_task(prompt):
            return
    except Exception:
        return
    out = {"hookSpecificOutput": {"hookEventName": "UserPromptSubmit", "additionalContext": CONTEXT}}
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(out, ensure_ascii=False))


if __name__ == "__main__":
    main()
