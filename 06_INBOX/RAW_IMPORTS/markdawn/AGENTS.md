# ~/.codex/AGENTS.md
# Fișier GLOBAL — se combină automat cu AGENTS.md din fiecare repo pe care lucrezi.
# Nu pui aici detalii specifice unui singur proiect — acelea rămân în AGENTS.md local.

## Despre mine (developer)

Full-stack developer și IT professional, lucrez și în mediu militar/guvernamental (desktop support, securitate, conformitate). Stack real folosit zilnic: C# (WPF/XAML), Python, JavaScript/React/Next.js, PowerShell, SQL. Rulez modele AI locale (Ollama — Gemma, Llama) pentru task-uri care nu trebuie să iasă din mediul local/air-gapped.

## Mod de lucru preferat

- **Execuție directă, nu prompt-uri repetate.** Când am dat deja contextul (fișier de proiect, spec, task list), nu mă întrebi din nou ce vreau — execuți și raportezi ce ai făcut. Întrebi doar când o decizie e ireversibilă sau ambiguă în mod real.
- **Cod de producție, nu schelete.** Nu livrezi placeholder-uri, TODO-uri goale sau date mock fără să spui explicit că sunt mock. Dacă ceva nu poate fi complet într-un pas, spui clar ce rămâne de făcut, nu ascunzi lipsa.
- **MVP-uri reale** — dacă cer un MVP, îl faci funcțional end-to-end (nu un shell fără logică), scalabil, cu arhitectură care nu trebuie rescrisă de la zero la următorul pas.

## Estetică UI implicită (dacă nu se specifică altceva)

Preferință generală: teme dark, aspect modern-tehnic ("tactical/cyber"), nu Bootstrap generic. Elemente apreciate: glassmorphism, gradient-uri discrete cu glow, acrylic/blur pe suprafețe elevate, sisteme de token-uri de culoare centralizate (nu culori hardcodate în componente). Dacă proiectul are propriul fișier de temă (`AGENTS.md`/`GEMINI.md` local), acela are prioritate.

## Reguli tehnice transversale

- MVVM (sau echivalentul potrivit pt. stack) — separare clară UI/logică, nu logică de business în code-behind sau în componente de view.
- Securitate implicită: nu introduci dependențe cu telemetrie ascunsă fără s-o dezactivezi/semnalezi; nu trimiți date către servicii externe fără să confirmi explicit dacă proiectul curent are cerințe de izolare.
- Git: commit-uri și PR-uri cu mesaje descriptive, nu „fix" sau „update". Dacă schimbarea are impact arhitectural, îl explici în descriere.

## Prioritate

Instrucțiunile din `AGENTS.md` local (per-proiect) suprascriu orice conflict cu acest fișier global. Acesta e doar baza implicită.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
