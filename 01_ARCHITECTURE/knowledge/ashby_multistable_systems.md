---
id: "knw-ashby-multistable-systems"
type: knowledge
lifecycle: ARCHIVED
archive_reason: scrise de agent fara citate din sursa; provenance si verificare auto-declarate
category: cybernetics-architecture
tags: [ashby, multistability, modularity, dispersion, subsystems]
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
    target_id: "knw-ashby-habituation-and-plasticity"
  - type: related_to
    target_id: "knw-benchmarks-2026-0001"
---

# 🧱 Sisteme Multistabile și Izolare Locală (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby.
- **Lucrare**: *Design for a Brain*, Capitolele 12, 13 și 16 (§16/1–16/15).
- **Licență**: Domeniu Public / W. Ross Ashby Estate.

---

## 2. Deficiența Sistemului Complet Conectat (Fully-Joined System)

Dacă un sistem format din $N$ variabile este complet interconectat (fiecare variabilă depinde de toate celelalte), probabilitatea de a găsi un set stabil de parametri prin căutare aleatoare scade exponențial:
$$P(\text{Stabilitate Globală}) \approx p^N$$
unde $p < 1$ este șansa de stabilitate a unei singure conexiuni. Pentru $N > 100$, timpul de adaptare depășește vârsta universului.

### Soluția: Multistabilitate și Independență Temporară
Ashby demonstrează că creierul și marile sisteme adaptive supraviețuiesc deoarece **nu sunt complet conectate**:
1. **Subsisteme bogate în conexiuni interne, dar slab cuplate între ele** (decuplare structurală / modularitate).
2. **Independență Temporară (Temporary Independence)**: Când variabilele unui subsistem se află în echilibru, influența lor asupra altor subsisteme devine constantă sau nulă.
3. **Adaptare Cumulativă**: Subsistemul A se poate adapta la mediul său local fără a distruge echilibrul deja obținut de subsistemul B.
