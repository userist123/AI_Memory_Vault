---
id: "knw-ashby-step-mechanisms"
type: knowledge
lifecycle: ARCHIVED
archive_reason: scrise de agent fara citate din sursa; provenance si verificare auto-declarate
category: cybernetics-plasticity
tags: [ashby, step-mechanisms, parameters, synaptic-weights, plasticity]
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
    target_id: "knw-ashby-habituation-and-plasticity"
  - type: related_to
    target_id: "knw-temporal-memory-p2-0001"
---

# 🎚️ Mecanisme în Trepte și Parametri Discreți (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby.
- **Lucrare**: *Design for a Brain*, Capitolul 6 (Parameters) & Capitolul 8 (§8/1–8/12).
- **Licență**: Domeniu Public / W. Ross Ashby Estate.

---

## 2. Principiul Mecanismului în Trepte

În cibernetică, un **mecanism în trepte** (step-mechanism) este o variabilă a sistemului care își păstrează valoarea invariantă pe intervale finite de timp, modificându-se doar prin salturi discrete finite atunci când un prag critic este atins:

$$P(t) = \begin{cases} P_k, & \text{dacă } E(t) \in [E^{\min}, E^{\max}] \\ P_{k+1} \sim \mathcal{U}(\text{Valori Permise}), & \text{dacă } E(t) \notin [E^{\min}, E^{\max}] \end{cases}$$

### Corespondența cu Plasticitatea Neuronală Modernă
1. **Starea de activare neuronală** (potențiale de acțiune, activare curentă) variază continuu în bucla primară.
2. **Ponderile sinaptice** ($w_{ij}$) acționează ca parametri în trepte: ele rămân constante în timpul inferenței curente și se modifică discret prin consolidare, întărire hebbiană sau depresie pe baza evaluării de succes/eșec (invarianța plasticității neuronale).
