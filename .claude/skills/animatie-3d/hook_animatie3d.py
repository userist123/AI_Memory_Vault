"""UserPromptSubmit hook: când promptul cere o animație 3D, adaugă o linie care pornește skill-ul animatie-3d.
Tace pentru orice alt prompt. Nu blochează niciodată (exit 0 mereu)."""
import json
import re
import sys

TRIGGER = re.compile(
    r"anima(ț|t|ţ)i(e|a|i)\s*3\s*d|3\s*d\s*anima|logo\s*3\s*d|logo\s+animat|intro\s*3\s*d|text\s*3\s*d|"
    r"video\s*3\s*d|clip\s*3\s*d|scen(ă|a)\s*3\s*d|turntable|se\s+rote(ș|s|ş)te|"
    r"rande(a|e)z(ă|a)?\s+(în|in)\s+blender|\bblender\b|three\.?js|react\s*three\s*fiber|"
    r"logo\s+reveal|intro\s+animat|text\s+animat\s+3\s*d|titlu\s+3\s*d|"
    r"intro\s+(pentru\s+)?(reels|youtube|tiktok|shorts)|model(ul)?\s+3\s*d|produs(ul)?\s+(în|in)\s+3\s*d|"
    r"prezentare\s+(de\s+)?produs\s+3\s*d|logo\s+(cu\s+fundal\s+)?transparent\s+(pentru\s+)?(un\s+)?(video|clip|overlay)|overlay\s+3\s*d|\.glb\b|"
    r"lower\s*third|bar(ă|a)\s+cu\s+num|pl(ă|a)cu(ț|t)(ă|a)\s+(cu\s+)?num|mockup\s+(3\s*d|de\s+telefon|telefon|laptop)|"
    r"end\s*screen|ecran(ul)?\s+de\s+final|outro\s+(pentru\s+)?youtube|exploded\s*view|vedere\s+explodat|(se\s+)?desfac(e|ă|a)\s+(î|i)n\s+piese|explodeaz(ă|a)\s+(produsul|modelul)|cifre\s+(animate|care\s+(cresc|numără|numara))|infografic\w*\s+(animat|3\s*d|video)|counter\s+animat|(din|dintr-o|după|dupa)\s+(o\s+)?(poz(ă|a)|fotografie|imagine)\s+(un\s+)?model\s*3\s*d|image\s*to\s*3\s*d|img2model|tipografie\s+cinetic|kinetic\s+typo|text\s+animat\s+(pentru|cu)\s+(reclam|reels|tiktok)|fraze\s+animate|reclam(ă|a)\s+(3\s*d|cgi|de\s+produs|animat)|cgi\s+(ad|commercial|reclam)|product\s+(ad|commercial|animation)|spot\s+(3\s*d|publicitar\s+3\s*d)|packshot",
    re.IGNORECASE,
)
# cereri care seamănă, dar nu sunt pentru acest skill
EXCLUDE = re.compile(r"\b(debug|depan|repar|fix)\w*\b.*\b(site|pagin)", re.IGNORECASE)
# UserPromptSubmit also fires for subagent hand-backs, task notifications and injected reminders
# (https://code.claude.com/docs/en/hooks); those are not the owner's request, so the hook stays silent.
SYNTHETIC = ("<agent-message", "<task-notification", "<system-reminder", "[subagent hand-back]")
CONTEXT = ("animatie-3d: the request looks like a new 3D animation (a clip or scene rendered locally in "
           "Blender/three.js, delivered as MP4). The `animatie-3d` skill covers this case; it does not apply to still "
           "images, AI-generated video or debugging an existing website.")


def main():
    try:
        sys.stdin.reconfigure(encoding="utf-8")
        prompt = json.load(sys.stdin).get("prompt", "") or ""
    except Exception:
        return
    if not isinstance(prompt, str) or prompt.lstrip().lower().startswith(SYNTHETIC):
        return
    if not TRIGGER.search(prompt) or EXCLUDE.search(prompt):
        return
    out = {"hookSpecificOutput": {"hookEventName": "UserPromptSubmit", "additionalContext": CONTEXT}}
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(out, ensure_ascii=False))


if __name__ == "__main__":
    main()
