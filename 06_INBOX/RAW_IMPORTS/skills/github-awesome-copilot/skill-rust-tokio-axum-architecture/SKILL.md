---
name: skill-rust-tokio-axum-architecture
description: Rust Axum framework, Tokio async runtime, Tower middleware layer, Rayon thread pool & SQLx.
---

# Rust Axum & Tokio Async Architecture

- **Runtime**: Tokio non-blocking async runtime (`tokio::spawn`, `tokio::select!`).
- **Framework**: Axum pe stiva Tower middleware.
- **Parallel Computing**: Rayon thread pool pentru procesare CPU-bound.
- **SQLx**: Interogări SQL verificate la compilare fără ORM penalty.