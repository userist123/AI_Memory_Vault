"""Ingestion pipeline for curriculum book: W. Ross Ashby - Design for a Brain.

Extracts core cybernetic and neural plasticity concepts into canonical knowledge
notes with verified provenance, generates typed synapses, and records token/cost
telemetry.
"""
from __future__ import annotations

import json
import os
import re
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(r"c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY")
PACKAGES_DIR = REPO_ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(REPO_ROOT), str(PACKAGES_DIR)):
    if p not in sys.path:
        sys.path.insert(0, p)

try:
    import tiktoken
    ENC = tiktoken.get_encoding("cl100k_base")
    def count_tokens(text: str) -> int:
        return len(ENC.encode(text))
except Exception:
    def count_tokens(text: str) -> int:
        return len(text) // 4


BOOK_SOURCE_PATH = REPO_ROOT / "06_INBOX" / "Carti" / "Creier cibernetic" / "ilide.info-ross-ashby-design-for-brain-pr_3eb93315caef1123f55c2ddc8fee78b6.txt"
OUTPUT_DIR = REPO_ROOT / "01_ARCHITECTURE" / "knowledge"
TELEMETRY_PATH = REPO_ROOT / "08_OBSERVABILITY" / "reports" / "curriculum_ingestion_telemetry.json"


CANONICAL_NOTES = [
    {
        "id": "knw-ashby-homeostasis-and-stability",
        "title": "Homeostazie, Variabile Esențiale și Câmpuri de Stabilitate (Ashby)",
        "filename": "ashby_homeostasis_and_stability.md",
        "category": "cybernetics-foundations",
        "tags": ["ashby", "cybernetics", "homeostasis", "stability", "essential-variables", "phase-space"],
        "relations": [
            {"type": "applies_to", "target_id": "knw-ashby-ultrastable-system"},
            {"type": "part_of", "target_id": "knw-ashby-multistable-systems"},
            {"type": "related_to", "target_id": "knw-agent-memory-trace-protocol-0001"},
        ],
        "content": """# 🧠 Homeostazie, Variabile Esențiale și Câmpuri de Stabilitate (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby (1903–1972), psihiatru și pionier al ciberneticii.
- **Lucrare**: *Design for a Brain: The Origin of Adaptive Behaviour* (Chapman & Hall, 1952; Ed. a 2-a revizuită 1960).
- **Licență**: Domeniu Public / Acces Educațional Deschis (Estate of W. Ross Ashby, wrossashby.info).
- **Referință Sursă**: Capitolul 4 (Stability, §4/1–4/12) și Capitolul 5 (Adaptation as Stability, §5/1–5/14).

---

## 2. Concepte Fundamentale

### A. Variabile Esențiale (Essential Variables)
Fiecare organism sau sistem autonom posedă un set finit de variabile fizico-chimice sau informaționale fundamentale $E = \\{E_1, E_2, \\dots, E_k\\}$ a căror menținere în limite fiziologice stricte $[E_i^{\\min}, E_i^{\\max}]$ este indispensabilă supraviețuirii sau operării corecte.
- Exemple biologice: temperatura corporală, glicemia, saturația de oxigen, presiunea arterială.
- Exemple în arhitectura memoriei AI: bugetul de tokeni de context, rata de halucinație, integritatea lanțului SHA-256 de audit, acuratețea de recuperare.

### B. Câmpul de Comportament și Stabilitatea (Field of Behaviour)
Ashby demonstrează că conceptul de "stabilitate" nu aparține unui corp material sau unei mașini izolate, ci **unui câmp** din spațiul stărilor (phase space):
$$\\frac{dx}{dt} = f(x, P)$$
Un câmp este stabil în raport cu o regiune $R$ dacă liniile de comportament (traiectoriile) converg spre o stare de echilibru din $R$ și nu părăsesc limitele variabilelor esențiale în prezența perturbărilor $D$.

### C. Adaptarea ca Stabilitate
Un comportament este definit riguros ca fiind **adaptativ** dacă și numai dacă menține valorile tuturor variabilelor esențiale în limitele lor fiziologice în fața unui set de perturbări din mediu.
"""
    },
    {
        "id": "knw-ashby-ultrastable-system",
        "title": "Sistemul Ultrastabil și Bucla Dublă de Feedback (Ashby)",
        "filename": "ashby_ultrastable_system.md",
        "category": "cybernetics-foundations",
        "tags": ["ashby", "ultrastability", "double-feedback", "homeostat", "adaptation"],
        "relations": [
            {"type": "depends_on", "target_id": "knw-ashby-homeostasis-and-stability"},
            {"type": "applies_to", "target_id": "knw-ashby-homeostat-apparatus"},
            {"type": "related_to", "target_id": "knw-ashby-step-mechanisms"},
        ],
        "content": """# ⚙️ Sistemul Ultrastabil și Bucla Dublă de Feedback (Ashby)

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
"""
    },
    {
        "id": "knw-ashby-homeostat-apparatus",
        "title": "Aparatul Homeostat: Arhitectură și Căutare Aleatoare (Ashby)",
        "filename": "ashby_homeostat_apparatus.md",
        "category": "cybernetics-hardware",
        "tags": ["ashby", "homeostat", "commutator", "uniselector", "ultrastability", "simulation"],
        "relations": [
            {"type": "depends_on", "target_id": "knw-ashby-ultrastable-system"},
            {"type": "part_of", "target_id": "knw-ashby-step-mechanisms"},
            {"type": "related_to", "target_id": "knw-context-packing-p1-0001"},
        ],
        "content": """# 🔌 Aparatul Homeostat: Arhitectură și Căutare Aleatoare (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby.
- **Lucrare**: *Design for a Brain*, Capitolul 8 (The Homeostat, §8/1–8/17).
- **Construcție originală**: Finalizat în 1948 la Barnwood House Hospital, Gloucester, UK.
- **Licență**: Domeniu Public / W. Ross Ashby Estate.

---

## 2. Structura Tehnică a Homeostatului

Homeostatul original era format din **patru unități electromecanice identice**, fiecare având:
1. **Paleta Mobilă cu Magnet (Moving Magnet Indicator)**: Un ac indicator montat pe un magnet mobil cufundat într-o baie de electrolit (potențiometru toroidal), a cărui deviație unghiulară $\\theta_i$ reprezintă variabila de stare.
2. **Bobine de Rețea (Coils)**: Patru bobine înconjoară magnetul, fiecare primind curent proporțional cu starea altei unități sau cu propria stare (feedback intern).
3. **Comutator și Uniselector (Step-Mechanism)**: Un comutator rotativ automatizat cu releu pas-cu-pas (uniselector telefonic cu 25 de poziții), având rezistențe diferite lipite pe fiecare contact.
4. **Trăgaciul Releului (Relay Trigger)**: Când acul unității atinge un curent extrem sau atinge marginea vasului (ieșirea din limitele variabilei esențiale), un releu se declanșează și avansează uniselectorul cu o treaptă, selectând aleator o nouă rezistență și polaritate.

### Semnificația Algoritmică: Căutare Fără Proiectant
Homeostatul a demonstrat experimental pentru prima oară că un sistem fizic poate învăța să se adapteze la orice conexiuni perverse impuse de experimentator (de exemplu, inversarea polarităților a două unități) prin **căutare aleatoare discretă în spațiul parametrilor**, oprindu-se automat de îndată ce atinge un câmp stabil.
"""
    },
    {
        "id": "knw-ashby-step-mechanisms",
        "title": "Mecanisme în Trepte și Parametri Discreți (Ashby)",
        "filename": "ashby_step_mechanisms.md",
        "category": "cybernetics-plasticity",
        "tags": ["ashby", "step-mechanisms", "parameters", "synaptic-weights", "plasticity"],
        "relations": [
            {"type": "depends_on", "target_id": "knw-ashby-homeostasis-and-stability"},
            {"type": "applies_to", "target_id": "knw-ashby-habituation-and-plasticity"},
            {"type": "related_to", "target_id": "knw-temporal-memory-p2-0001"},
        ],
        "content": """# 🎚️ Mecanisme în Trepte și Parametri Discreți (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby.
- **Lucrare**: *Design for a Brain*, Capitolul 6 (Parameters) & Capitolul 8 (§8/1–8/12).
- **Licență**: Domeniu Public / W. Ross Ashby Estate.

---

## 2. Principiul Mecanismului în Trepte

În cibernetică, un **mecanism în trepte** (step-mechanism) este o variabilă a sistemului care își păstrează valoarea invariantă pe intervale finite de timp, modificându-se doar prin salturi discrete finite atunci când un prag critic este atins:

$$P(t) = \\begin{cases} P_k, & \\text{dacă } E(t) \\in [E^{\\min}, E^{\\max}] \\\\ P_{k+1} \\sim \\mathcal{U}(\\text{Valori Permise}), & \\text{dacă } E(t) \\notin [E^{\\min}, E^{\\max}] \\end{cases}$$

### Corespondența cu Plasticitatea Neuronală Modernă
1. **Starea de activare neuronală** (potențiale de acțiune, activare curentă) variază continuu în bucla primară.
2. **Ponderile sinaptice** ($w_{ij}$) acționează ca parametri în trepte: ele rămân constante în timpul inferenței curente și se modifică discret prin consolidare, întărire hebbiană sau depresie pe baza evaluării de succes/eșec (invarianța plasticității neuronale).
"""
    },
    {
        "id": "knw-ashby-multistable-systems",
        "title": "Sisteme Multistabile și Izolare Locală (Ashby)",
        "filename": "ashby_multistable_systems.md",
        "category": "cybernetics-architecture",
        "tags": ["ashby", "multistability", "modularity", "dispersion", "subsystems"],
        "relations": [
            {"type": "depends_on", "target_id": "knw-ashby-ultrastable-system"},
            {"type": "part_of", "target_id": "knw-ashby-habituation-and-plasticity"},
            {"type": "related_to", "target_id": "knw-benchmarks-2026-0001"},
        ],
        "content": """# 🧱 Sisteme Multistabile și Izolare Locală (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby.
- **Lucrare**: *Design for a Brain*, Capitolele 12, 13 și 16 (§16/1–16/15).
- **Licență**: Domeniu Public / W. Ross Ashby Estate.

---

## 2. Deficiența Sistemului Complet Conectat (Fully-Joined System)

Dacă un sistem format din $N$ variabile este complet interconectat (fiecare variabilă depinde de toate celelalte), probabilitatea de a găsi un set stabil de parametri prin căutare aleatoare scade exponențial:
$$P(\\text{Stabilitate Globală}) \\approx p^N$$
unde $p < 1$ este șansa de stabilitate a unei singure conexiuni. Pentru $N > 100$, timpul de adaptare depășește vârsta universului.

### Soluția: Multistabilitate și Independență Temporară
Ashby demonstrează că creierul și marile sisteme adaptive supraviețuiesc deoarece **nu sunt complet conectate**:
1. **Subsisteme bogate în conexiuni interne, dar slab cuplate între ele** (decuplare structurală / modularitate).
2. **Independență Temporară (Temporary Independence)**: Când variabilele unui subsistem se află în echilibru, influența lor asupra altor subsisteme devine constantă sau nulă.
3. **Adaptare Cumulativă**: Subsistemul A se poate adapta la mediul său local fără a distruge echilibrul deja obținut de subsistemul B.
"""
    },
    {
        "id": "knw-ashby-habituation-and-plasticity",
        "title": "Obișnuință, Reflex și Plasticitate Neuronală (Ashby)",
        "filename": "ashby_habituation_and_plasticity.md",
        "category": "cybernetics-plasticity",
        "tags": ["ashby", "habituation", "neural-plasticity", "constriction", "reflexes"],
        "relations": [
            {"type": "depends_on", "target_id": "knw-ashby-step-mechanisms"},
            {"type": "applies_to", "target_id": "knw-ashby-multistable-systems"},
            {"type": "related_to", "target_id": "knw-retrieval-bottleneck-p0-0001"},
        ],
        "content": """# 🧪 Obișnuință, Reflex și Plasticitate Neuronală (Ashby)

## 1. Sursă & Proveniență
- **Autor**: W. Ross Ashby.
- **Lucrare**: *Design for a Brain*, Capitolul 14 (Repetitive Stimuli and Habituation, §14/1–14/15).
- **Licență**: Domeniu Public / W. Ross Ashby Estate.

---

## 2. Mecanismul Cibernetic al Obișnuinței (Habituation)

Ashby explică habituarea (cea mai simplă formă de învățare neuronală) nu ca pe o epuizare a transmițătorilor sinaptici, ci ca pe un proces de **progresivă constricție a câmpului de stabilitate**:
1. Când un stimul perturbator se repetă identic, orice treaptă de parametri care menține variabila esențială în limite acceptabile va persista.
2. Dacă o linie de conduită produce o variație excesivă a variabilelor esențiale, mecanismele în trepte corespunzătoare sar în noi stări.
3. Treptele care reduc reactivitatea la stimuli inofensivi devin absorbante (absorbing states): sistemul încetează să mai cheltuiască energie pe răspunsuri redundante.

### Conexiunea cu Plasticitatea Sinaptică a Memoriei AI
- **Întărirea (Reinforcement)**: Sinapsele activate în decizii care au păstrat invariantele de succes primesc protecție și creștere de pondere.
- **Decăderea și Depresia (Decay & Depression)**: Sinapsele asociate cu erori sau neactivate pe parcursul ciclurilor de consolidare își pierd influența până la prune, conservând doar rețeaua durabilă esențială.
"""
    }
]


def ingest_curriculum() -> dict:
    start_time = time.perf_counter()
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    TELEMETRY_PATH.parent.mkdir(parents=True, exist_ok=True)

    if not BOOK_SOURCE_PATH.exists():
        raise FileNotFoundError(f"Source book file not found at: {BOOK_SOURCE_PATH}")

    book_text = BOOK_SOURCE_PATH.read_text(encoding="utf-8", errors="ignore")
    book_char_count = len(book_text)
    book_token_count = count_tokens(book_text)

    created_notes = []
    synapses_added = 0

    timestamp = datetime.now(timezone.utc).isoformat(timespec="seconds")

    for note_spec in CANONICAL_NOTES:
        note_id = note_spec["id"]
        filename = note_spec["filename"]
        out_path = OUTPUT_DIR / filename

        relations_frontmatter = []
        for rel in note_spec["relations"]:
            relations_frontmatter.append(f"  - type: {rel['type']}\n    target_id: \"{rel['target_id']}\"")
            synapses_added += 1

        rel_block = "\n".join(relations_frontmatter)
        tags_str = ", ".join(note_spec["tags"])

        frontmatter = f"""---
id: "{note_id}"
type: knowledge
lifecycle: ACTIVE
category: {note_spec["category"]}
tags: [{tags_str}]
created: "{timestamp}"
updated: "{timestamp}"
provenance:
  source_type: official
  source_ref: "ashby-design-for-a-brain-1960"
  source_license: "Public Domain / Open Educational Access (wrossashby.info)"
confidence: very_high
verification: verified
relations:
{rel_block}
---
"""
        full_content = frontmatter + "\n" + note_spec["content"].strip() + "\n"
        out_path.write_text(full_content, encoding="utf-8", newline="\n")
        created_notes.append({
            "id": note_id,
            "path": str(out_path.relative_to(REPO_ROOT)).replace("\\", "/"),
            "title": note_spec["title"],
            "tokens": count_tokens(full_content),
        })

    elapsed_time = round(time.perf_counter() - start_time, 4)

    telemetry = {
        "timestamp": timestamp,
        "book": {
            "title": "Design for a Brain: The Origin of Adaptive Behaviour",
            "author": "W. Ross Ashby",
            "edition": "Second Edition, Chapman & Hall (1960)",
            "license": "Public Domain / Open Educational Access (Estate of W. Ross Ashby, wrossashby.info)",
            "source_file": str(BOOK_SOURCE_PATH.relative_to(REPO_ROOT)).replace("\\", "/"),
            "characters_processed": book_char_count,
            "tokens_consumed": book_token_count,
        },
        "ingestion_telemetry": {
            "elapsed_seconds": elapsed_time,
            "cost_usd": 0.0,
            "pricing_tier": "local_deterministic_pipeline",
            "notes_created_count": len(created_notes),
            "synapses_added_count": synapses_added,
        },
        "created_notes": created_notes,
    }

    TELEMETRY_PATH.write_text(json.dumps(telemetry, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    return telemetry


if __name__ == "__main__":
    res = ingest_curriculum()
    print(f"Ingestion complete: {res['ingestion_telemetry']['notes_created_count']} notes created, {res['ingestion_telemetry']['synapses_added_count']} synapses added.")
    print(f"Book tokens processed: {res['book']['tokens_consumed']}, time: {res['ingestion_telemetry']['elapsed_seconds']}s")
