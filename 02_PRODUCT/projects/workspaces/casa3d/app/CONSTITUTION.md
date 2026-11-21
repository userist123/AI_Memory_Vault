# Casa mea 3D — reguli nenegociabile

1. Digital Twin-ul este sursa de adevăr geometric. Unitatea internă este metrul.
2. AI-ul propune; nu decide geometria. Geometry Engine validează orice modificare.
3. Utilizatorul aprobă modificările importante. ERROR blochează aplicarea; WARNING cere confirmare.
4. Nu inventa prețuri, dimensiuni, disponibilitate sau certificări. `UNKNOWN` este preferabil unei valori inventate.
5. Orice dată comercială externă are `source`, `sourceUrl`, `verifiedAt`, `verificationType`, `confidence`.
6. Fără scraping automat al magazinelor fără bază legală. Surse permise: feed/API furnizor, fișiere trimise de furnizor, catalog verificat manual, platforme de afiliere.
7. Fără modele 3D fără drept de utilizare. Fiecare GLB are licența furnizorului atașată.
8. Nu rescrie motorul geometric existent dacă poate fi refolosit. Orice schimbare de comportament are test.
9. Fiecare fază trece testele de acceptare (STOP GATE) înainte de următoarea.
10. Nicio funcție prezentată ca disponibilă nu e simulată. Ce nu e gata se ascunde sau se marchează experimental.
11. Fără infrastructură complexă fără nevoie reală (fără PostGIS, IFC, AR, OCR, multi-țară în fazele timpurii). Excepție: pista BIM (art. 14).
12. România este prima jurisdicție. Verificările de conformitate sunt asistive, nu certificări juridice.
13. Monetizare inițială: afiliere + Pro Designer + furnizori. AR, IFC și enterprise vin după validarea comercială.
14. Pista BIM (amendament al proprietarului, 2026-11-21): ghidul `docs/bim/GHID_BIM.md` se aplică pe fazele lui, una câte una,
    cu STOP GATE (art. 9). IFC, OCR și recunoașterea automată a planurilor intră doar în faza ghidului care le cere, rulează local
    (fără trimiterea planurilor către servicii cloud), sunt marcate experimentale până trec testele de acceptare (art. 10) și nu
    schimbă monetizarea de la art. 13. Snapshot rămâne sursa de adevăr (art. 1): disciplinele și vederea federată se derivă din el
    (`core/elements.ts`), nu dintr-un al doilea model. Structura și instalațiile se reprezintă și se verifică geometric; nu se
    dimensionează și nu se declară conforme (art. 12). Starea fazelor: `docs/bim/FAZA0_INVENTAR.md`.