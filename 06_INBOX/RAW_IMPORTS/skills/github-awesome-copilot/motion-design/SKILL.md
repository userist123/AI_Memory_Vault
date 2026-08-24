---
name: motion-design
description: Încarcă acest skill când adaugi animații, tranziții, microinteracțiuni sau efecte de scroll într-un UI web. Impune motion cu scop (feedback, orientare, ierarhie), timing corect și respectarea prefers-reduced-motion.
---

# Motion Design

Animația dezvăluie informație și dă feedback. Dacă nu face niciuna, se șterge.

## Cele 3 scopuri legitime ale mișcării

1. **Feedback** — confirmă acțiunea userului (buton apăsat, salvare reușită, item șters).
2. **Orientare** — arată de unde vine și unde pleacă un element (navigare, expand/collapse, reorder).
3. **Ierarhie/atenție** — ghidează ochiul spre ce contează acum (o singură dată, subtil).

Tot restul e zgomot: fără parallax gratuit, fără elemente care pulsează permanent, fără confetti.

## Timing și easing

| Interacțiune | Durată | Easing |
|---|---|---|
| Hover / micro-feedback | 100-150ms | ease-out |
| Tranziții UI (dropdown, modal, tab) | 200-300ms | ease-out / ease-in-out |
| Elemente care intră în viewport | 300-500ms | ease-out |
| Grafice / numere care cresc | 600-800ms | ease-out |
| Page transitions | 300-400ms | ease-in-out |

- **Peste 500ms pentru UI = lent perceput.** Sub 100ms = imperceptibil, inutil.
- Elementele care IES pot fi mai rapide (150-200ms) decât cele care INTRĂ.
- Stagger la liste: 30-60ms delay între itemi, max ~8 itemi staggered.

## Reguli tehnice (web)

1. Animează DOAR `transform` și `opacity` (GPU-compositable). Niciodată `width`, `height`, `top`, `left`, `margin` — provoacă layout thrashing.
2. `will-change` doar temporar, pe durata animației.
3. Scroll-reveal: `IntersectionObserver`, un singur trigger, translateY(12-24px) + fade — nu mai mult.
4. **`prefers-reduced-motion: reduce` = obligatoriu.** Dezactivează tot ce e decorativ, păstrează doar feedback instant (opacity).
5. Un singur element „hero" animat per ecran. Două lucruri care se mișcă simultan concurează pentru atenție.

## Microinteracțiuni standard

- Buton: scale(0.97) la press, revenire 120ms.
- Card hover: translateY(-2px) + umbră ușor mărită, 150ms.
- Toast/notificare: slide + fade in 250ms, auto-dismiss cu bară de progres.
- Skeleton loading în loc de spinner pentru conținut > 300ms așteptare.
