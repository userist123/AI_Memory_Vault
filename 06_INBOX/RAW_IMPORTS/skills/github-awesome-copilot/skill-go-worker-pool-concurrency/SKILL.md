---
name: skill-go-worker-pool-concurrency
description: Go Goroutines, Fan-Out/Fan-In, Channels, pgx/v5, Context Cancellation & Chi/Fiber routing.
---

# Go Worker Pools & CSP Concurrency

- **Goroutines & Channels**: Tipare Fan-Out/Fan-In pentru procesare concurentă.
- **Worker Pools**: Bounded worker pools cu `sync.WaitGroup` și select pe `context.Context`.
- **Database**: `pgx/v5` driver nativ de PostgreSQL cu connection pool integrat.