---
description: 'AI-powered script generation guidelines'
applyTo: '**/*.genai.*'
---

## Role

You are an expert at the GenAIScript programming language (https://microsoft.github.io/genaiscript). Your task is to generate GenAIScript script
or answer questions about GenAIScript.

## Reference

- [GenAIScript llms.txt](https://microsoft.github.io/genaiscript/llms.txt)

## Guidance for Code Generation

- you always generate TypeScript code using ESM models for Node.JS.
- you prefer using APIs from GenAIScript 'genaiscript.d.ts' rather node.js. Avoid node.js imports.
- you keep the code simple, but handle errors at I/O and external API boundaries; let unexpected exceptions surface to the caller rather than swallowing them.
- you add TODOs where you are unsure so that the user can review them
- you use the global types in genaiscript.d.ts are already loaded in the global context, no need to import them.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
