---
id: "b4e88f21-7291-49fa-9481-22904c10a005"
type: procedure
lifecycle: REVIEW
category: audit
tags:
  - ui-ux-audit
  - heuristics
  - nielsen
  - accessibility
  - wcag
created: 2026-08-17T22:55:00Z
updated: 2026-08-17T22:55:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/skills/ui-ux-review/SKILL.md"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T22:55:00Z
relations:
  - target_id: "b4e88f21-7291-49fa-9481-22904c10a001"
    type: depends_on
    target: "[[01_KNOWLEDGE/Design_System_Foundation]]"
  - target_id: "b4e88f21-7291-49fa-9481-22904c10a003"
    type: depends_on
    target: "[[01_KNOWLEDGE/Motion_Design_Principles]]"
  - target_id: "b4e88f21-7291-49fa-9481-22904c10a002"
    type: related_to
    target: "[[01_KNOWLEDGE/Data_Visualization_Standards]]"
---

# Procedură Operațională: Audit Heuristic UI/UX și Verificare Accesibilitate

## TL;DR
Procedură sistematică de evaluare euristică (10 Euristici Nielsen + WCAG AA) pentru inspecția interfețelor grafice. Fiecare problemă identificată primește o clasificare strictă de severitate (0–4), locație precisă, euristică încălcată și propunere tehnică de remediere.

## Key Facts
- **Scop:** Identificarea deficiențelor de utilizabilitate, accesibilitate și consistență vizuală înainte de lansarea în producție.
- **Instrumente de Evaluare:** 5-Second Focal Point Test, Squint Test, Matricea Nielsen (10 reguli), Standardele WCAG 2.1 AA.
- **Scala de Severitate:**
  - `0`: Cosmetic (nu afectează fluxul).
  - `1`: Minor (deranjant, dar rezolvabil fără asistență).
  - `2`: Mediu (crește efortul cognitiv).
  - `3`: Major (frustrant, risc ridicat de abandon).
  - `4`: Blocant / Catastrofal (impas operațional, eroare distructivă fără rollback).

---

## 1. Pași de Execuție ai Auditului

### Pasul 1: Testul Focalizării și Ierarhiei Vizuale (5-Second Test)
1. Expune ecranul timp de 5 secunde evaluatorului sau rulează simularea de scanare vizuală.
2. Identifică primul element dominant: corespunde acțiunii primare a paginii?
3. Aplică **Squint Test** (reducerea contrastului vizual) pentru a confirma dacă ierarhia structurală rămâne distinctă fără detalii fine.

### Pasul 2: Evaluarea Euristicilor Nielsen (Scor 0–4 per regulă)
1. **Vizibilitatea stării:** Confirmă prezența indicatorilor de progres, loading states și feedback imediat la acțiuni.
2. **Potrivire cu lumea reală:** Verifică limbajul natural, terminologia familiară și lipsa jargonului tehnic intern.
3. **Control și libertate:** Asigură existența butoanelor clare de `Anulare`, `Undo` și ieșire fără salvare.
4. **Consistență și standarde:** Asigură utilizarea acelorași convenții și componente pe întreg sistemul.
5. **Prevenirea erorilor:** Verifică dialogurile de confirmare la acțiuni ireversibile sau distructive.
6. **Recunoaștere vs memorare:** Elementele și opțiunile de meniu trebuie să fie vizibile, nu memorate de utilizator.
7. **Flexibilitate și eficiență:** Suport pentru scurtături de tastatură și fluxuri optimizate pentru utilizatori avansați.
8. **Minimalism:** Fiecare element grafic sau text concurează pentru atenție — șterge decorul redundant.
9. **Recuperare din erori:** Mesajele de eroare trebuie să fie umane: să explice clar problema și să ofere pasul de remediere.
10. **Ajutor contextual:** Ghidaj integrat direct în câmpurile complexe (tooltips, micro-copy).

### Pasul 3: Inspecția de Accesibilitate (WCAG AA Nederogabil)
1. Măsoară contrastul de culoare: minim **4.5:1** pentru body text, **3:1** pentru text mare.
2. Verifică navigarea exclusivă din tastatură (`Tab`, `Shift+Tab`, `Enter`, `Esc`) și starea de focus vizibilă (`outline`).
3. Confirmă că nicio informație critică nu este transmisă *exclusiv* prin variație de culoare.
4. Măsoară țintele tactile pe dispozitive mobile: minimum **44×44px**.
5. Validează funcționarea sub parametrul `prefers-reduced-motion: reduce`.

### Pasul 4: Generarea Raportului Canonic

Generează matricea structurată:

```markdown
| # | Deficiență Identificată | Locație Componentă | Euristică Încălcată | Severitate (0-4) | Soluție Tehnică Recomandată |
|---|------------------------|--------------------|---------------------|------------------|-----------------------------|
| 1 | Lipsă confirmare delete| `src/components/Table` | Prevenirea erorilor | 3 (Major)        | Adăugare Modal Confirmare    |
```

---

## 2. Relații și Sinapse Cognitive
- `depends_on`: [[01_KNOWLEDGE/Design_System_Foundation]] — Standardele de tokeni, contrast și ierarhie.
- `depends_on`: [[01_KNOWLEDGE/Motion_Design_Principles]] — Criteriile de timing și accesibilitate a mișcării.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
