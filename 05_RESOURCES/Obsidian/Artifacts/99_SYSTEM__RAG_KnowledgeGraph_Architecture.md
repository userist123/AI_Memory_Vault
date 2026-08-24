---
type: core
category: system-architecture
tags: [system, rag, knowledge-graph, meta]
created: 2026-08-09
updated: 2026-08-09
status: active
priority: high
---

# RAG + Knowledge Graph — Arhitectură

## Scop
Vault-ul funcționează în două moduri simultan:
1. **Knowledge Graph nativ Obsidian** — linking manual, Graph View, backlinks
2. **RAG extern** — retrieval prin embeddings, pentru context injectat în sesiuni AI (Claude, ChatGPT, etc.)

## Taxonomie de Tag-uri (orizontală)
Structură pe 3 niveluri:
```
#domain/security
#domain/trading
#domain/dev
#status/active
#status/archived
#priority/critical
```
Evită tag-uri libere fără prefix — degradează retrieval-ul pe termen lung.

## Frontmatter → Metadata pentru Embeddings
Fiecare notă trebuie să aibă frontmatter complet (vezi `System_Architecture.md`). La export pentru embedding, metadata (type, category, tags, related) devine filtru de pre-retrieval, nu doar organizare vizuală.

## Strategie de Chunking (pentru RAG extern)
- **1 notă = 1 chunk conceptual** — dacă o notă crește peste ~500-800 cuvinte, împarte în subnote linkate, nu lăsa un fișier monolit
- Titluri `##` clare → chunking semantic ușor pentru orice pipeline de embedding (nu depinde de un tool anume)
- `related:` din frontmatter → folosit pentru graph-aware retrieval (recuperare + noduri conectate, nu doar match direct)

## Pipeline recomandat (când vault-ul ajunge la volum)
1. Export periodic `.md` → script (Python) care parsează frontmatter + body
2. Embedding per notă (model la alegere — local sau API)
3. Vector store local (ex: Chroma, LanceDB) — evită dependență cloud dat fiind contextul de lucru
4. Retrieval filtrat întâi pe `type`/`tags` (metadata filter), apoi similarity search — reduce zgomot semnificativ față de similarity search pur

## Plugin-uri Obsidian relevante
- **Dataview** — indexuri dinamice, deja folosit în `_Index.md`-uri
- **Templater** — consistență frontmatter la creare
- **Smart Connections** (opțional) — RAG local direct în Obsidian, fără pipeline extern separat

## Regulă de mentenanță Graph
- Notă fără niciun link (nod izolat) = semnal de clasificare greșită sau conținut care aparține în Inbox, nu în structura finală
- Review lunar: `Graph View` filtrat pe noduri izolate → procesare sau ștergere

---
*Acesta e ultimul fișier din structura inițială. Vault-ul e gata pentru primul import real din `06_INBOX/_raw_imports/`.*

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
