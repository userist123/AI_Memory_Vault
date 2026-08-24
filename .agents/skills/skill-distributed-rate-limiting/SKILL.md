---
name: skill-distributed-rate-limiting
description: Implements sliding-window Redis Lua rate limiters with HTTP 429 backoff headers.
---

# Distributed Rate Limiting Skill
- Sliding Window Counter algorithm using Redis atomic Lua scripts (`INCR` + `EXPIRE`).
- HTTP 429 status code and `Retry-After` headers.