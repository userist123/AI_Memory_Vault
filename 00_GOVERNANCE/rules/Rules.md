---
type: core
category: rules
status: active
version: 1.0.0
id: "e08b0d08-8527-4ddf-a260-09f5f6f7c499"
document_kind: policy
document_status: active
provenance_status: incomplete
relations: []
policy_scope: vault-governance
---

# Rules

## 1. Information Integrity

- Nu inventa informatii.
- Daca ceva nu este verificat, marcheaza-l explicit.
- Pastreaza sursa si data cand sunt disponibile.
- Nu transforma o ipoteza in fapt.
- Nu suprascrie o informatie verificata cu una mai slaba fara motiv.

## 2. Memory

- Citeste memoria relevanta inainte de a crea continut nou.
- Preferă actualizarea unei note existente atunci cand acelasi concept exista deja.
- Foloseste note atomice: un concept principal per nota.
- Pastreaza legaturi `[[wikilinks]]`.
- Nu stoca parole, token-uri, chei API sau secrete.
- Nu sterge istoria critica; arhiveaza cu motiv.

## 3. Retrieval

Retrieval-ul trebuie sa combine, cand este disponibil:

- semantic similarity;
- keyword match;
- metadata;
- tags;
- graph relationships;
- recency;
- confidence;
- project relevance.

## 4. Reasoning

Inainte de actiuni importante:

1. identifica obiectivul;
2. identifica constrangerile;
3. recupereaza memoria relevanta;
4. construieste un plan;
5. verifica planul;
6. executa;
7. valideaza rezultatul;
8. extrage lectiile.

## 5. Goal Drift

Daca raspunsul sau planul incepe sa se indeparteze de obiectiv:

- opreste ramificarea;
- revino la obiectiv;
- noteaza schimbarea daca este relevanta;
- cere confirmare pentru schimbari majore de directie.

## 6. Destructive Actions

Actiunile cu risc de pierdere, modificare masiva sau impact asupra infrastructurii necesita:

- identificarea exacta a tintei;
- verificarea preconditiilor;
- backup / rollback unde este posibil;
- validare dupa executie.

## 7. Import

Datele brute importate din alte AI-uri intra mai intai in:

`06_INBOX/RAW_IMPORTS/`

Nu intra direct in memoria permanenta.

## 8. Quality

O nota buna trebuie sa fie:

- clara;
- reutilizabila;
- suficient de atomica;
- legata de alte note;
- cu metadata;
- cu sursa cand exista;
- cu confidence.

## 9. Security

Nu stoca:

- parole;
- API keys;
- private keys;
- token-uri;
- date de autentificare;
- identificatori personali inutili.

## 10. Completion

Nu declara un task "Done" doar pentru ca ai generat un raspuns. "Done" inseamna ca rezultatul a fost verificat la nivelul potrivit de risc.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
