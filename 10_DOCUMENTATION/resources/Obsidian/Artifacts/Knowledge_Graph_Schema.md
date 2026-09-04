---
id: "976d88c1-8f9b-4f8b-a15d-15a7f59266af"
document_kind: specification
document_status: active
category: ai-architecture
provenance_status: incomplete
implementation_status: documentation_only
relations: []
---

# Knowledge Graph Schema

## Overview

This document defines the **Knowledge Graph** structure for the AI Vault, including node types, edge types, graph construction, metrics, and integration with RAG.

---

## Graph Model

### Node Types

| Node Type | Description | Example |
|-----------|-------------|---------|
| **Note** | Individual knowledge unit | `[[Identity]]`, `[[RAG_Structure]]` |
| **Concept** | Abstract idea or topic | `Cybersecurity`, `Trading`, `ADHD` |
| **Entity** | Named person, place, thing | `ChatGPT`, `Obsidian`, `Romania` |
| **Event** | Time-bound occurrence | `Project_Start`, `Decision_Made` |
| **Tag** | Classification label | `#project/active`, `#knowledge/technical` |

---

### Edge Types

| Edge Type | Direction | Meaning | Example |
|-----------|-----------|---------|---------|
| **references** | Directed | Note A mentions Note B | `[[Goals]]` → `[[Identity]]` |
| **extends** | Directed | Note A builds on Note B | `RAG_Structure` → `System_Architecture` |
| **implements** | Directed | Note A implements Note B | `Procedure` → `Rule` |
| **contradicts** | Directed | Note A conflicts with Note B | `Decision_A` → `Decision_B` |
| **related_to** | Undirected | Thematic connection | `Experience` ↔ `Lesson` |
| **caused_by** | Directed | Causal relationship | `Error` → `Root_Cause` |
| **led_to** | Directed | Outcome relationship | `Decision` → `Outcome` |
| **part_of** | Directed | Hierarchical containment | `Chapter` → `Book` |
| **instance_of** | Directed | Type relationship | `Specific_Project` → `Project_Type` |

---

## Graph Construction

### 1. Node Creation

**From Notes:**

```python
def create_note_node(note):
    return {
        "id": note.id,
        "type": "note",
        "label": note.title,
        "properties": {
            "type": note.type,  # knowledge, project, etc.
            "category": note.category,
            "tags": note.tags,
            "created": note.created,
            "updated": note.updated,
            "status": note.status,
            "source": note.source,
            "word_count": len(note.content.split())
        }
    }
```

**From Tags:**

```python
def create_tag_node(tag):
    return {
        "id": f"tag:{tag}",
        "type": "tag",
        "label": tag,
        "properties": {
            "count": count_notes_with_tag(tag),
            "related_tags": find_related_tags(tag)
        }
    }
```

**From Concepts (Extracted):**

```python
def create_concept_node(concept, mentions):
    return {
        "id": f"concept:{concept}",
        "type": "concept",
        "label": concept,
        "properties": {
            "mentions": mentions,
            "related_notes": find_notes_about(concept),
            "first_mentioned": earliest_mention(concept)
        }
    }
```

---

### 2. Edge Creation

**From Wikilinks:**

```python
def create_reference_edges(note):
    edges = []
    links = extract_wikilinks(note.content)
    
    for link in links:
        edges.append({
            "source": note.id,
            "target": link,
            "type": "references",
            "weight": 1.0
        })
    
    return edges
```

**From Co-occurrence:**

```python
def create_cooccurrence_edges(notes, window_size=5):
    edges = []
    
    for note in notes:
        concepts = extract_concepts(note.content, window_size)
        
        for i, concept1 in enumerate(concepts):
            for concept2 in concepts[i+1:]:
                edges.append({
                    "source": f"concept:{concept1}",
                    "target": f"concept:{concept2}",
                    "type": "related_to",
                    "weight": 1.0 / distance(concept1, concept2)
                })
    
    return edges
```

**From Shared Tags:**

```python
def create_tag_edges(notes):
    edges = []
    
    for note in notes:
        for tag in note.tags:
            edges.append({
                "source": note.id,
                "target": f"tag:{tag}",
                "type": "has_tag",
                "weight": 1.0
            })
    
    return edges
```

---

## Graph Schema (Formal)

### Node Schema

```typescript
interface Node {
  id: string;           // Unique identifier
  type: NodeType;       // note | concept | entity | event | tag
  label: string;        // Display name
  properties: {
    [key: string]: any; // Flexible properties
  };
}

type NodeType = "note" | "concept" | "entity" | "event" | "tag";
```

### Edge Schema

```typescript
interface Edge {
  source: string;       // Source node ID
  target: string;       // Target node ID
  type: EdgeType;       // references | extends | etc.
  weight: number;       // 0.0 - 1.0
  properties?: {
    [key: string]: any; // Optional properties
  };
}

type EdgeType = 
  | "references"
  | "extends"
  | "implements"
  | "contradicts"
  | "related_to"
  | "caused_by"
  | "led_to"
  | "part_of"
  | "instance_of"
  | "has_tag";
```

---

## Graph Metrics

### 1. Node-Level Metrics

| Metric | Formula | Interpretation |
|--------|---------|----------------|
| **Degree** | `deg(v) = |E(v)|` | Number of connections |
| **In-Degree** | `deg_in(v)` | Number of incoming links (popularity) |
| **Out-Degree** | `deg_out(v)` | Number of outgoing links (referencing) |
| **Betweenness Centrality** | `BC(v) = Σ (σ_st(v) / σ_st)` | Bridge importance |
| **PageRank** | `PR(v) = (1-d)/N + d * Σ (PR(u) / deg_out(u))` | Authority score |
| **Clustering Coefficient** | `CC(v) = |E(N(v))| / (deg(v) * (deg(v)-1))` | Local connectivity |

---

### 2. Graph-Level Metrics

| Metric | Formula | Interpretation |
|--------|---------|----------------|
| **Nodes** | `|V|` | Total number of nodes |
| **Edges** | `|E|` | Total number of edges |
| **Density** | `D = |E| / (|V| * (|V|-1))` | Connectivity (0-1) |
| **Average Degree** | `avg_deg = 2|E| / |V|` | Mean connections per node |
| **Diameter** | `max(d(u,v))` | Longest shortest path |
| **Average Path Length** | `avg(d(u,v))` | Mean distance between nodes |
| **Modularity** | `Q = (fraction within) - (expected fraction)` | Community structure |

---

### 3. Community Detection

**Algorithms:**

| Algorithm | Use Case | Complexity |
|-----------|----------|------------|
| **Louvain** | General community detection | O(n log n) |
| **Label Propagation** | Fast, approximate | O(n) |
| **Girvan-Newman** | Hierarchical communities | O(n m^2) |
| **Infomap** | Information flow | O(n log n) |

**Example Output:**

```json
{
  "communities": [
    {
      "id": 1,
      "label": "Technical Knowledge",
      "nodes": ["RAG", "System_Architecture", "Cybersecurity"],
      "size": 45
    },
    {
      "id": 2,
      "label": "Personal Development",
      "nodes": ["ADHD", "Psychology", "Relationships"],
      "size": 32
    },
    {
      "id": 3,
      "label": "Projects",
      "nodes": ["AI_Vault", "Trading_Bot", "Certifications"],
      "size": 18
    }
  ]
}
```

---

## Graph Visualization

### 1. Force-Directed Layout

**Algorithm:** Fruchterman-Reingold

**Parameters:**
```yaml
layout_config:
  algorithm: fruchterman_reingold
  iterations: 100
  k: 100  # optimal distance
  gravity: 0.1
  repulsion: 500
```

---

### 2. Hierarchical Layout

**For:** Part-of relationships, taxonomies

**Parameters:**
```yaml
hierarchical_config:
  direction: top_to_bottom
  level_separation: 100
  node_separation: 50
  tree_algorithm: dendrogram
```

---

### 3. Circular Layout

**For:** Community visualization

**Parameters:**
```yaml
circular_config:
  community_based: true
  radius: 200
  sort_by: degree
```

---

## Graph Queries

### 1. Path Finding

**Shortest Path:**
```cypher
MATCH path = shortestPath((a:Note {title: "Identity"})-[*]-(b:Note {title: "Goals"}))
RETURN path
```

**All Paths (up to length N):**
```cypher
MATCH path = (a:Note {title: "Identity"})-[*1..3]-(b:Note {title: "Goals"})
RETURN path
```

---

### 2. Neighborhood Queries

**Direct Neighbors:**
```cypher
MATCH (a:Note {title: "RAG_Structure"})-[:references]-(b)
RETURN b
```

**2-Hop Neighbors:**
```cypher
MATCH (a:Note {title: "RAG_Structure"})-[*1..2]-(b)
RETURN DISTINCT b
```

---

### 3. Pattern Queries

**Find Contradictions:**
```cypher
MATCH (a:Decision)-[:contradicts]->(b:Decision)
RETURN a.title, b.title
```

**Find Decision Chains:**
```cypher
MATCH path = (a:Decision)-[:led_to]->(b:Decision)
RETURN path
```

---

### 4. Aggregation Queries

**Most Connected Notes:**
```cypher
MATCH (n:Note)-[r]-(m)
RETURN n.title, count(r) as degree
ORDER BY degree DESC
LIMIT 10
```

**Tag Co-occurrence:**
```cypher
MATCH (n:Note)-[:has_tag]->(t1:Tag), (n)-[:has_tag]->(t2:Tag)
WHERE t1 <> t2
RETURN t1.label, t2.label, count(n) as co_occurrence
ORDER BY co_occurrence DESC
```

---

## Graph Integration with RAG

### 1. Graph-Enhanced Retrieval

**Process:**
1. Initial retrieval (semantic/full-text)
2. Graph traversal from retrieved nodes (1-2 hops)
3. Add related nodes to context
4. Re-rank by combined score

**Query:**
```cypher
MATCH (n:Note)-[*1..2]-(m)
WHERE n.title IN ["RAG_Structure", "System_Architecture"]
RETURN DISTINCT m
```

---

### 2. Graph-Based Context Expansion

**Algorithm:**
```python
def expand_context_with_graph(initial_notes, graph, max_hops=2, max_nodes=20):
    expanded = set(initial_notes)
    frontier = set(initial_notes)
    
    for hop in range(max_hops):
        next_frontier = set()
        
        for node in frontier:
            neighbors = graph.get_neighbors(node)
            next_frontier.update(neighbors)
        
        next_frontier -= expanded
        top_neighbors = rank_by_relevance(next_frontier)[:max_nodes // (hop + 1)]
        
        expanded.update(top_neighbors)
        frontier = top_neighbors
    
    return list(expanded)
```

---

### 3. Graph-Based Query Routing

**Use Graph Structure to Route Queries:**

| Query Pattern | Route To |
|---------------|----------|
| "What is X?" | Concept nodes → linked notes |
| "How do I do X?" | Procedure nodes → steps |
| "What did I learn about X?" | Experience/Lesson nodes |
| "What's the history of X?" | Event nodes → timeline |

---

## Graph Evolution

### 1. Growth Metrics

| Metric | Current | Target (6mo) | Target (12mo) |
|--------|---------|--------------|---------------|
| Nodes | TBD | 1000 | 5000 |
| Edges | TBD | 3000 | 15000 |
| Density | TBD | 0.01 | 0.005 |
| Avg Degree | TBD | 3 | 3 |

---

### 2. Maintenance

**Weekly:**
- [ ] Add new nodes from imported notes
- [ ] Update edges from new links
- [ ] Check for orphaned nodes

**Monthly:**
- [ ] Recompute centrality metrics
- [ ] Detect community changes
- [ ] Identify high-authority nodes

**Quarterly:**
- [ ] Full graph audit
- [ ] Remove deprecated nodes
- [ ] Optimize graph structure

---

## Graph Storage

### 1. Storage Options

| Option | Pros | Cons |
|--------|------|------|
| **Neo4j** | Mature, Cypher query, good tooling | Requires separate server |
| **NetworkX (Python)** | Simple, integrates with Python | In-memory, not persistent |
| **iGraph** | Fast, scalable | Less intuitive API |
| **GraphML** | Standard format, portable | No query language |
| **Obsidian + Dataview** | Native integration | Limited graph operations |

---

### 2. Recommended Setup

**Development:**
- NetworkX (Python) for analysis
- GraphML for persistence
- Obsidian for visualization

**Production:**
- Neo4j for storage and queries
- REST API for graph access
- Obsidian for user interface

---

## Graph Applications

### 1. Knowledge Discovery

**Find Related Concepts:**
```cypher
MATCH (a:Concept {name: "RAG"})-[*1..2]-(b:Concept)
RETURN b.name, count(*) as relevance
ORDER BY relevance DESC
```

---

### 2. Gap Analysis

**Find Orphaned Notes:**
```cypher
MATCH (n:Note)
WHERE NOT (n)-[]-()
RETURN n.title
```

---

### 3. Influence Analysis

**Find Most Influential Notes:**
```cypher
MATCH (n:Note)
RETURN n.title, size((n)<--()) as incoming_links
ORDER BY incoming_links DESC
LIMIT 10
```

---

### 4. Trend Detection

**Find Emerging Concepts:**
```cypher
MATCH (c:Concept)
WHERE c.first_mentioned > date() - duration({months: 3})
RETURN c.name, c.mentions
ORDER BY c.mentions DESC
```

---

## Graph Visualization in Obsidian

### 1. Built-in Graph View

**Settings:**
```yaml
obsidian_graph:
  show_attachments: false
  show_orphans: true
  depth: 3
  link_strength: 0.5
  repel_force: 0.5
  center_force: 0.5
  edge_length: 100
```

---

### 2. Graph Plugins

| Plugin | Purpose | Status |
|--------|---------|--------|
| **Obsidian Graph** | Native graph view | 🟢 Active |
| **Dataview** | Graph queries | 🟡 Planned |
| **Mind Map** | Tree visualization | 🟡 Planned |
| **Excalidraw** | Custom diagrams | 🟡 Planned |

---

## Related Files

- [[System_Architecture]] — Overall system design
- [[RAG_Structure]] — RAG pipeline
- [[01_KNOWLEDGE/Concepts/Graph_Theory]] — Background knowledge (future)
- [[03_PROCEDURES/Graph/Graph_Maintenance]] — Maintenance procedures (future)

---

## Metadata

```yaml
---
type: knowledge
category: technical/ai
tags:
  - knowledge_graph
  - graph
  - architecture
  - technical
  - rag
created: 2026-08-09
updated: 2026-08-09
status: active
source: manual
confidence: high
---
```

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
