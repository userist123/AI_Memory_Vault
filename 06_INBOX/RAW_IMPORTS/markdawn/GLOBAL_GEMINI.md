# ~/.gemini/GEMINI.md
# Fișier GLOBAL — citit de Gemini CLI și de backend-ul Gemini din Google Antigravity
# pe orice proiect, combinat cu GEMINI.md local (cel mai specific are prioritate).

## Profil developer

Full-stack, activitate și în mediu militar/guvernamental (IT, securitate, conformitate). Stack: C# (WPF/XAML), Python, JavaScript/React/Next.js, PowerShell, SQL. Modele locale prin Ollama (Gemma/Llama) pentru scenarii care necesită izolare de cloud.

## Mod de execuție preferat

- Execuție directă pe baza fișierelor de context deja existente (AGENTS.md/GEMINI.md local, spec-uri date anterior) — nu ceri reconfirmare pentru lucruri deja definite.
- Livrezi cod complet și funcțional; dacă un task e prea mare pentru o singură trecere, spui explicit ce ai livrat și ce rămâne, nu ascunzi lipsurile în comentarii vagi.
- MVP = funcțional end-to-end, nu machetă vizuală fără logică.

## Estetică & arhitectură implicită

Teme dark, aspect modern-tehnic ("tactical/cyber", nu corporate generic). Sisteme de token-uri de culoare centralizate, nu culori hardcodate în componente individuale. Separare clară UI/logică (MVVM sau echivalent). Evită dependențe cu telemetrie ascunsă fără confirmare.

## Notă pentru Antigravity

Acest fișier e citit și de agentul Gemini din Google Antigravity la nivel global (`~/.gemini/config/...`). Pentru un agent custom persistent pe toate proiectele, folosește `/agents create` din Antigravity CLI, care salvează la `~/.gemini/config/agents/{agent_name}/agent.md`.

## Prioritate

`GEMINI.md` local al unui proiect anume suprascrie orice conflict cu acest fișier global.
