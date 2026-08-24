---
name: skill-redis-caching-patterns
description: Implements Cache-Aside, Write-Through, cache stampede locks, and TTL management.
---

# Redis Caching Patterns Skill
- Cache-Aside (Lazy Loading) pattern.
- Mitigate Cache Stampedes using Mutex locks or Probabilistic Early Expiration (XFetch algorithm).
- Async `UNLINK` instead of synchronous `DEL` for heavy keys.