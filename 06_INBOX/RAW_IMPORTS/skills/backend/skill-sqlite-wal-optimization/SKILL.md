---
name: skill-sqlite-wal-optimization
description: Configures WAL mode, pragmas, mmap size, and handles busy timeouts in high-read embedded contexts.
---

# SQLite WAL Optimization Skill
- `PRAGMA journal_mode=WAL;`
- `PRAGMA busy_timeout=5000;`
- `PRAGMA synchronous=NORMAL;`
- `PRAGMA mmap_size=268435456;`
- Concurrent readers with atomic `BEGIN IMMEDIATE` transactions.