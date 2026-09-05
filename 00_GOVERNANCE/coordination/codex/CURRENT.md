# Codex Runtime/Security Front

- task: Security Gate — close RAW query exposure and establish regression coverage
- timestamp: 2026-09-05T12:00:00+03:00
- files touched: `memory_controller/controller.py`, `memory_controller/tests/test_query_raw_boundary.py`
- evidence: `MemoryController.query()` previously bypassed `StorageEngine.query()`, whose contract explicitly excludes `RAW`; patch delegates through the storage boundary. Regression coverage added for in-memory storage, SQLite storage, explicit RAW filters, lifecycle enum normalization, and HUMAN/AI_AGENT/ADMIN readers.
- implementation commits: `820f86ef03407de92d4c90185b1232426a3ab05f`, `8cfa92837a0846a01e39455262652da587cfe4b6`
- status: READY FOR REVIEW
- dependency note: no dependency on Antigravity or Claude Code fronts; `PROJECT_BRAIN/PROJECT_STATE.md` intentionally not modified.
