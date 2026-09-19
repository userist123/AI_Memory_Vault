---
id: "knw-ashby-homeostat-apparatus"
type: knowledge
lifecycle: ARCHIVED
archive_reason: scrise de agent fara citate din sursa; provenance si verificare auto-declarate
category: cybernetics-hardware
tags: [ashby, homeostat, commutator, uniselector, ultrastability, simulation]
created: "2026-09-18T19:28:10+00:00"
updated: "2026-09-18T19:28:10+00:00"
provenance:
  source_type: official
  source_ref: "ashby-design-for-a-brain-1960"
  source_license: "claimed: Public Domain / Open Educational Access (wrossashby.info) — not verified against the publisher"
confidence: low
verification: unverified
relations:
  - type: depends_on
    target_id: "knw-ashby-ultrastable-system"
  - type: part_of
    target_id: "knw-ashby-step-mechanisms"
  - type: related_to
    target_id: "knw-context-packing-p1-0001"
---

# 🔌 Aparatul Homeostat: Arhitectură și Căutare Aleatoare (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby.
- **Lucrare**: *Design for a Brain*, Capitolul 8 (The Homeostat, §8/1–8/17).
- **Construcție originală**: Finalizat în 1948 la Barnwood House Hospital, Gloucester, UK.
- **Licență**: Domeniu Public / W. Ross Ashby Estate.

---

## 2. Structura Tehnică a Homeostatului

Homeostatul original era format din **patru unități electromecanice identice**, fiecare având:
1. **Paleta Mobilă cu Magnet (Moving Magnet Indicator)**: Un ac indicator montat pe un magnet mobil cufundat într-o baie de electrolit (potențiometru toroidal), a cărui deviație unghiulară $\theta_i$ reprezintă variabila de stare.
2. **Bobine de Rețea (Coils)**: Patru bobine înconjoară magnetul, fiecare primind curent proporțional cu starea altei unități sau cu propria stare (feedback intern).
3. **Comutator și Uniselector (Step-Mechanism)**: Un comutator rotativ automatizat cu releu pas-cu-pas (uniselector telefonic cu 25 de poziții), având rezistențe diferite lipite pe fiecare contact.
4. **Trăgaciul Releului (Relay Trigger)**: Când acul unității atinge un curent extrem sau atinge marginea vasului (ieșirea din limitele variabilei esențiale), un releu se declanșează și avansează uniselectorul cu o treaptă, selectând aleator o nouă rezistență și polaritate.

### Semnificația Algoritmică: Căutare Fără Proiectant
Homeostatul a demonstrat experimental pentru prima oară că un sistem fizic poate învăța să se adapteze la orice conexiuni perverse impuse de experimentator (de exemplu, inversarea polarităților a două unități) prin **căutare aleatoare discretă în spațiul parametrilor**, oprindu-se automat de îndată ce atinge un câmp stabil.
