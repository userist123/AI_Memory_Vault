---
id: "knw-ashby-homeostasis-and-stability"
type: knowledge
lifecycle: ARCHIVED
archive_reason: scrise de agent fara citate din sursa; provenance si verificare auto-declarate
category: cybernetics-foundations
tags: [ashby, cybernetics, homeostasis, stability, essential-variables, phase-space]
created: "2026-09-18T19:28:10+00:00"
updated: "2026-09-18T19:28:10+00:00"
provenance:
  source_type: official
  source_ref: "ashby-design-for-a-brain-1960"
  source_license: "claimed: Public Domain / Open Educational Access (wrossashby.info) — not verified against the publisher"
confidence: low
verification: unverified
relations:
  - type: applies_to
    target_id: "knw-ashby-ultrastable-system"
  - type: part_of
    target_id: "knw-ashby-multistable-systems"
  - type: related_to
    target_id: "knw-agent-memory-trace-protocol-0001"
---

# 🧠 Homeostazie, Variabile Esențiale și Câmpuri de Stabilitate (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby (1903–1972), psihiatru și pionier al ciberneticii.
- **Lucrare**: *Design for a Brain: The Origin of Adaptive Behaviour* (Chapman & Hall, 1952; Ed. a 2-a revizuită 1960).
- **Licență**: Domeniu Public / Acces Educațional Deschis (Estate of W. Ross Ashby, wrossashby.info).
- **Referință Sursă**: Capitolul 4 (Stability, §4/1–4/12) și Capitolul 5 (Adaptation as Stability, §5/1–5/14).

---

## 2. Concepte Fundamentale

### A. Variabile Esențiale (Essential Variables)
Fiecare organism sau sistem autonom posedă un set finit de variabile fizico-chimice sau informaționale fundamentale $E = \{E_1, E_2, \dots, E_k\}$ a căror menținere în limite fiziologice stricte $[E_i^{\min}, E_i^{\max}]$ este indispensabilă supraviețuirii sau operării corecte.
- Exemple biologice: temperatura corporală, glicemia, saturația de oxigen, presiunea arterială.
- Exemple în arhitectura memoriei AI: bugetul de tokeni de context, rata de halucinație, integritatea lanțului SHA-256 de audit, acuratețea de recuperare.

### B. Câmpul de Comportament și Stabilitatea (Field of Behaviour)
Ashby demonstrează că conceptul de "stabilitate" nu aparține unui corp material sau unei mașini izolate, ci **unui câmp** din spațiul stărilor (phase space):
$$\frac{dx}{dt} = f(x, P)$$
Un câmp este stabil în raport cu o regiune $R$ dacă liniile de comportament (traiectoriile) converg spre o stare de echilibru din $R$ și nu părăsesc limitele variabilelor esențiale în prezența perturbărilor $D$.

### C. Adaptarea ca Stabilitate
Un comportament este definit riguros ca fiind **adaptativ** dacă și numai dacă menține valorile tuturor variabilelor esențiale în limitele lor fiziologice în fața unui set de perturbări din mediu.
