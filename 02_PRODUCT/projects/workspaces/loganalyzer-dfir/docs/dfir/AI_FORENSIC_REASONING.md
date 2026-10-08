# Raționament AI pe baza probelor (spec §25)

Cod (mutat în `LogAnalyzer.Ai`, doar ediția neclasificată, vezi EDITIONS.md): `LogAnalyzer.Ai/EvidenceReasoner.cs` (catalog, schemă, validare, client local),
`LogAnalyzer.Ai/AiCaseAnalysis.cs` (pasul din caz). În aplicație: pagina „Investigație completă”, fila
„Analiză AI (locală)”. Teste: `LogAnalyzer.Dfir.Tests/EvidenceReasonerTests.cs`.

## Principiul

Modelul nu este parser și nu vede probele brute. Primește doar un **catalog numerotat**, construit din rezultatele verificate
ale cazului:

- constatări: regula, severitatea, clasificarea, descrierea, ATT&CK;
- evenimentele care le susțin (`E1…`);
- urmele anti-forensics DETECTED;
- golurile de probă (`G1…`).

Analiza pornește doar dacă fiecare probă mai corespunde hash-ului de la achiziție.

Răspunsul este JSON, cu schema impusă prin parametrul `format` al Ollama. Are secțiunile cerute de §25: `summary`,
`hypotheses`, `alternative_explanations`, `correlation_explanation`, `next_steps`, `questions`. Fiecare afirmație are
1–4 citări.

## Validarea (deterministă)

O afirmație este respinsă, cu motivul păstrat, dacă:

1. nu citează nimic sau citează un element inexistent;
2. citează mai mult de 4 elemente (a cita tot nu dovedește nimic);
3. numește un fișier, o adresă IPv4, un domeniu sau o dată care nu apare în elementele citate;
4. în `hypotheses` / `alternative_explanations`: nu este formulată ca posibilitate („poate”, „ar putea”, „posibil”…) sau se
   prezintă drept sigură („confirmat”, „dovedește”…).

O afirmație acceptată rămâne **UNPROVEN**. Este afișată cu citările ei (EvidenceId, locator, SHA-256), ca analistul să o
verifice. Nimic din ce scrie modelul nu modifică constatările sau clasificările.

## Transportul

- Model local servit de Ollama (`/api/chat`, `stream: false`, `think: false`, `temperature: 0`, `seed: 0`).
- Adresa trebuie să fie o adresă loopback **numerică** (`127.0.0.1` sau `[::1]`); `localhost` este refuzat (fără rezolvare
  de nume). Proxy-ul sistemului și redirecționările sunt dezactivate. Funcționează și în modul AirGapped: apelul nu iese de
  pe stație.
- Se înregistrează în caz: `Analysis/ai_catalog.txt`, `ai_raw_response.json`, `ai_reasoning.json`. În jurnalul de audit apar
  modelul, digest-ul lui (din `/api/tags`), SHA-256 al promptului și al răspunsului.

## Ce s-a încercat pe cazul real (NanAgent, 2026-10-06)

Ollama 0.35.1, modele locale `qwen3:30b-a3b` (digest `ad815644918f…`) și `qwen2.5:7b-instruct` (digest `845dbda0ea48…`).

1. **Doar citări obligatorii.** `qwen2.5:7b` a scris că „NanAgent32.exe a fost asociat cu execuția SETUP.EXE” și a citat doar
   elementul Prefetch al lui SETUP.EXE, care nu menționează NanAgent32.exe. Regula 3 o respinge.
   Același model a citat până la 40 de elemente pe afirmație, de unde regula 2.
2. **Un al doilea model ca verificator** (descompunere în afirmații atomice, fiecare căutată în elementele citate).
   Rezultatul a fost neîncrezător în ambele direcții:
   - a acceptat „SETUP.EXE … detectat ca troian (Wacatac.H!ml)”, deși detecția era pe două DLL-uri, nu pe SETUP.EXE;
   - a acceptat ipoteze prezentate ca fapte („confirmat de SRUM”);
   - a respins explicații alternative formulate corect („ar putea fi o configurație greșită”).

   **Nu a fost păstrat**: un filtru care pare o verificare, dar nu este, ar da o încredere falsă.
3. **Afirmații atomice + regulile 1–4.** Cu `qwen3:30b-a3b`, prin pasul din aplicație: 31 de afirmații acceptate și 5
   respinse (3 ipoteze formulate ca certitudini, 2 cu mențiuni absente din elementele citate), în 97 de secunde.
   Afirmația falsă despre SETUP.EXE nu a mai apărut.

## Limite

- Regulile deterministe nu pot verifica sensul. O afirmație poate combina corect elemente reale într-o concluzie greșită.
  De aceea ea rămâne UNPROVEN și se arată cu citările.
- Catalogul este limitat (implicit 80 de elemente, cele mai grave constatări mai întâi); câte au rămas pe dinafară se
  raportează (`OmittedItems`).
- Modelul nu citește din Memory Vault; nu primește conținut nevalidat.
