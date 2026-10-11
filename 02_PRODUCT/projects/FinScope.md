---
id: "finscope-project-core"
type: "project"
lifecycle: "ACTIVE"
category: "finance_app"
tags: ["react", "typescript", "dexie", "vite", "finance", "pdf_parser", "ocr"]
created: "2026-08-17"
updated: "2026-08-17"
provenance:
  source_type: "execution"
  source_ref: "C:/Users/Marius/finscope"
confidence: "high"
verification: "verified"
relations:
  - target_id: "1bc7f563-35da-4c5e-91cb-9bb789bb28a2"
    type: related_to
    target: 00_CORE/Identity.md
---

# Project: FinScope

## 1. Description
**FinScope** is a local-first, privacy-focused personal financial intelligence web application built with **React 19, TypeScript, Vite, Dexie (IndexedDB), Zustand, TailwindCSS, and Recharts**.

## 2. Core Capabilities
- **Local-First Storage**: IndexedDB via Dexie for offline zero-cloud latency and total data privacy.
- **Multi-Format Ingestion**: Ingest bank statements via PDF (`pdfjs-dist`), Excel (`xlsx`), CSV (`papaparse`), and OCR scanned images (`tesseract.js`).
- **Rule-Based Engine**: Smart auto-categorization and transaction rule mapping.
- **Analytics & Forecasting**: Recharts visualization for cashflow, recurring subscriptions, and budget tracking.

## 3. Location & Environment
- **Path**: `C:\Users\Marius\finscope`
- **Orchestration**: Multi-Agent Orchestrator via secure delegation (`cognitive_core/dispatch_cli.py`, gated by `MemoryController` enforcing `I-001..I-012` and `I-RETRIEVAL`).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
