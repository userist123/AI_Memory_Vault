---
id: "b4e88f21-7291-49fa-9481-22904c10a003"
type: knowledge
lifecycle: REVIEW
category: motion
tags:
  - motion-design
  - animations
  - web-performance
  - accessibility
  - microinteractions
created: 2026-08-17T22:55:00Z
updated: 2026-08-17T22:55:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/skills/motion-design/SKILL.md"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T22:55:00Z
relations:
  - target: "[[Design_System_Foundation]]"
    type: related_to
  - target: "[[UI_UX_Heuristic_Review]]"
    type: supports
---

# Principii de Motion Design și Microinteracțiuni UI

## TL;DR
Orice animație trebuie să servească unui scop legitim (feedback, orientare spațială, focalizarea atenției); orice mișcare pur decorativă se elimină. Din rațiuni de performanță se animează exclusiv proprietăți accelerate GPU (`transform`, `opacity`), respectând cu strictețe media query-ul `prefers-reduced-motion`.

## Key Facts
- **Cele 3 Scopuri Legitime ale Mișcării**:
  1. *Feedback:* Confirmă acțiunea imediată a utilizatorului (apăsare buton, ștergere element, confirmare salvare).
  2. *Orientare:* Clarifică originea și destinația spațială a componentelor (navigare, expand/collapse, reordonare liste).
  3. *Ierarhie / Atenție:* Direcționează privirea către punctul focal activ (o singură dată, fără pulsații continue).
- **Matricea de Timing și Easing**:
  - *Hover / Micro-feedback:* 100–150ms (`ease-out`).
  - *Tranziții UI (Dropdown, Modal, Tabs):* 200–300ms (`ease-out` / `ease-in-out`).
  - *Elemente care intră în Viewport:* 300–500ms (`ease-out`).
  - *Grafice / Contoare numerice:* 600–800ms (`ease-out`).
  - *Tranziții între pagini:* 300–400ms (`ease-in-out`).
- **Praguri Cognitive**: Orice tranziție de peste 500ms este percepută ca lentă și frustrantă; sub 100ms devine imperceptibilă. Elementele care ies din ecran sunt mai rapide (150–200ms) decât cele care intră.
- **Microinteracțiuni Standard**:
  - *Buton:* `scale(0.97)` la click/press, revenire elastică în 120ms.
  - *Card Hover:* `translateY(-2px)` + difuzie subtilă a umbrei în 150ms.
  - *Toast Notificare:* slide + fade in 250ms cu bară de auto-dismiss.
  - *Încărcare Asincronă:* Skeleton loading cu shimmer pentru latențe > 300ms (înlocuiește spinner-ul circular).

---

## 1. Reguli Tehnice și Randare Web

1. **GPU Compositing**:
   - Animațiile se aplică STRICT pe `transform` și `opacity`.
   - Interzisă animarea proprietăților geometrice (`width`, `height`, `top`, `left`, `margin`, `padding`) care provoacă *layout thrashing* și re-randare costisitoare pe CPU.
2. **Respectarea Accesibilității**:
   - `@media (prefers-reduced-motion: reduce)` este obligatoriu. Dezactivează toate translațiile și scalările, păstrând doar fade-uri instantanee de opacitate.
3. **Controlul Concurenței Vizuale**:
   - Maximum 1 singur element în mișcare activă pe ecran la un moment dat pentru a evita suprasolicitarea cognitivă.

---

## 2. Relații și Sinapse Cognitive
- `related_to`: [[Design_System_Foundation]] — Integrează curbele de timing și constantele de durată în tokenii globali.
- `supports`: [[UI_UX_Heuristic_Review]] — Criteriu esențial de audit pentru Nielsen Heuristic 1 (Vizibilitatea stării sistemului).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
