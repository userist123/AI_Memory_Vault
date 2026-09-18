---
id: "knw-ashby-ultrastable-system"
type: knowledge
lifecycle: ACTIVE
category: cybernetics-foundations
tags: [ashby, ultrastability, double-feedback, homeostat, adaptation]
created: "2026-09-18T19:28:10+00:00"
updated: "2026-09-18T19:28:10+00:00"
provenance:
  source_type: official
  source_ref: "ashby-design-for-a-brain-1960"
  source_license: "Public Domain / Open Educational Access (wrossashby.info)"
confidence: very_high
verification: verified
relations:
  - type: depends_on
    target_id: "knw-ashby-homeostasis-and-stability"
  - type: applies_to
    target_id: "knw-ashby-homeostat-apparatus"
  - type: related_to
    target_id: "knw-ashby-step-mechanisms"
---

# ⚙️ Sistemul Ultrastabil și Bucla Dublă de Feedback (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby.
- **Lucrare**: *Design for a Brain*, Capitolul 7 (The Ultrastable System, §7/1–7/26) & Capitolul 9.
- **Licență**: Domeniu Public / W. Ross Ashby Estate.

---

## 2. Arhitectura Buclei Duble (Two-Tiered Feedback)

Sistemele reactive simple cu o singură buclă de feedback (precum regulatorul centrifugal Watt sau termostatul) eșuează când dinamica internă a mediului se modifică structural. Ashby a conceput arhitectura **ultrastabilă**, care combină două bucle concurente de feedback:

```text
                  +-------------------------------+
                  |          MEDIUL (E)           |
                  +-------------------------------+
                     ^                         |
   Acțiuni (A)       |                         | Perturbări senzoriale (S)
                     |                         v
                  +-------------------------------+
                  |   SISTEMUL REACTIV PRIMAR     | <---+
                  |      (Variabile Principale)   |     | Reconfigurare
                  +-------------------------------+     | de parametri
                     |                                  |
                     | Ieșiri către variabile           |
                     v esențiale                        |
                  +-------------------------------+     |
                  |     VARIABILE ESENȚIALE       |     |
                  |      (Toleranțe Vitale)       |     |
                  +-------------------------------+     |
                     | Limită depășită                  |
                     v (Trăgaci)                        |
                  +-------------------------------+     |
                  |     MECANISM ÎN TREPTE        |-----+
                  |   (Step-Mechanisms / Param)   |
                  +-------------------------------+
```

### A. Bucla Primară (Frequent, Continuous)
Operează continuu între senzorii și efectorii organismului și mediu. Cât timp variabilele esențiale rămân în limite normale, bucla primară utilizează parametrii stabiliți pentru a anula perturbațiile tranzitorii.

### B. Bucla Secundară (Infrequent, Discontinuous)
Dacă mediul se schimbă atât de sever încât acțiunile buclei primare nu mai pot preveni depășirea limitelor variabilelor esențiale, deviația declanșează **mecanismul în trepte** (step-mechanism). Acesta modifică brusc parametrii funcționali interni (ponderile sinaptice), căutând o nouă configurație a câmpului în care sistemul redevine stabil.
