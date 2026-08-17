# ~/.claude/CLAUDE.md
# Fișier GLOBAL — se combină cu CLAUDE.md din fiecare repo. Nu duplici aici specificul unui proiect.

## Cine sunt

Full-stack developer, activ în IT (inclusiv mediu militar/guvernamental — desktop support, securitate, conformitate HG 585/MS 172-191). Stack zilnic: C# (WPF/XAML), Python, JS/React/Next.js, PowerShell, SQL. Folosesc și modele locale prin Ollama (Gemma, Llama) când proiectul nu trebuie să depindă de cloud.

## Cum vreau să lucrezi cu mine

1. Nu-mi ceri să repet contextul dacă e deja scris în CLAUDE.md local sau într-un fișier de spec pe care ți l-am dat — citește-l și execută.
2. Livrezi cod complet, funcțional, de nivel producție — nu schelete cu TODO fără implementare, nu date de test lăsate acolo fără avertisment explicit.
3. Dacă îmi construiești un MVP, îl vreau real — funcționează cap-coadă, nu doar UI fără logică din spate.
4. Când o decizie e ireversibilă (ștergere, push pe main, modificare de infra) — te asiguri de aprobare. Restul, execuți fără să mă întrebi din nou.

## Preferințe de arhitectură & estetică

- Separare clară UI/logică (MVVM sau echivalentul relevant pentru stack). Nu bag business logic în componente de view/code-behind.
- Teme dark by default în UI, aspect modern-tehnic, nu design generic de tip Bootstrap. Apreciez sisteme de token-uri de culoare centralizate, glassmorphism/acrylic discret, nu decor gratuit.
- Fără dependențe cu telemetrie ascunsă neconfirmată.

## Suprascriere

Orice `CLAUDE.md` local dintr-un proiect are prioritate peste acest fișier global dacă există conflict — acesta e doar setul implicit de preferințe.
