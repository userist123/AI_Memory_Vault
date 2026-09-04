# 03_IMPLEMENTATION

Canonical home for executable product/runtime code.

Rules:
- Production implementation only; tests, reports, notebooks, raw imports and generated artifacts belong elsewhere.
- Runtime packages live under `packages/`.
- Product implementations live under `products/`.
- Compatibility shims are temporary, minimal, and explicitly tracked.
- Imports from `06_INBOX`, `40_EXPERIMENTS`, `50_ARTIFACTS`, and `80_ARCHIVE` are prohibited.
