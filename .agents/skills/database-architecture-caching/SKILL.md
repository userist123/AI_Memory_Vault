---
name: database-architecture-caching
description: Arhitectura Bazelor de Date & Caching (PostgreSQL, SQLite WAL, Redis, Connection Pooling, Indexing).
---

# Database Architecture, Concurrency & Caching

## 1. Concursibilitate & Persistență (SQLite & PostgreSQL)
- **SQLite WAL Mode**: `PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;`. Tranzacții atomice cu `BEGIN IMMEDIATE`.
- **PostgreSQL Connection Pooling**: PgBouncer, limită strictă de conexiuni, optimizare `max_connections`.
- **Indexare Strategică**: B-Tree pe chei străine și coloane frecvent filtrate; indecși acoperitori (covering indexes).

## 2. Strategii de Caching cu Redis
- **Cache-Aside Pattern**: Aplicația citește din Redis; la miss, citește din DB și scrie în Redis cu TTL (Time-To-Live).
- **Invalidation Policy**: Invalidare pe evenimente de scriere (`PUT`/`DELETE`), fără stocare de stări expirate.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Master_Skills_Catalog_251]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
