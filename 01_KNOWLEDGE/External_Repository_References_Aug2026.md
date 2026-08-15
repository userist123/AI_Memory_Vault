---
id: "bb3e720a-0ac2-4243-a44e-350e5e2cb6dc"
type: knowledge
lifecycle: REVIEW
category: external-repository-references
tags: [github, reference, ui-design, automation, mcp, android, windows-automation, web-template]
created: "2026-08-15"
updated: "2026-08-15"
provenance:
  source_type: import
  source_ref: "GitHub repository pages, user-submitted list"
confidence: medium
verification: unverified
relations: []
---

# External Repository References (August 2026)

Eight external GitHub repositories submitted by the user for reference. Descriptions below are sourced directly from each repository's public README/landing page. Status (active/archived/deprecated) is stated explicitly where the repository itself declares it.

## 1. Ivy Wallet

- **URL**: https://github.com/Ivy-Apps/ivy-wallet
- **Status**: ARCHIVED (owner-archived Aug 11, 2025; project no longer maintained since Nov 2024).
- **Type**: Open-source Android money manager app.
- **Stack**: Kotlin, Jetpack Compose, MVI architecture, Room DB, Hilt DI.
- **Relevance**: Reference for MVI/Compose architecture patterns; not actively maintainable as a dependency.

## 2. PyWinAssistant

- **URL**: https://github.com/a-real-ai/pywinassistant
- **Status**: Active, MIT licensed, public release Dec 31, 2023.
- **Type**: "Visualization-of-Thought" artificial assistant that controls a full Windows computer via natural language, using the native Windows UI Automation (UIA) API rather than vision-based screen parsing.
- **Stack**: Python.
- **Relevance**: Directly relevant to the local multi-agent development system workstream (agents needing real Windows desktop control/automation) discussed earlier in this project's context. Worth deeper evaluation before adoption -- verify license terms and current maintenance activity.

## 3. hicetnunc

- **URL**: https://github.com/hicetnunc2000/hicetnunc
- **Status**: DEPRECATED (explicitly stated by the repository itself).
- **Type**: hicetnunc UI -- experimental UI for a Tezos blockchain NFT marketplace/smart-contract designs.
- **Stack**: React, SCSS.
- **Relevance**: Historical reference only; not recommended for active integration given explicit deprecation.

## 4. MaterialYouNewTab

- **URL**: https://github.com/prem-k-r/MaterialYouNewTab
- **Status**: Active (maintainership transferred from original creator XengShi to Prem).
- **Type**: Browser "New Tab" replacement extension styled after Google's Material You design language.
- **Stack**: JavaScript, HTML, CSS; multi-browser extension.
- **Relevance**: Reference for Material You visual design tokens/theming patterns (color, elevation, shape) applicable to the project's design-system/theming priorities.

## 5. LiquidGlass

- **URL**: https://github.com/ybouane/liquidglass
- **Status**: Active.
- **Type**: Library implementing a "liquid glass" UI visual effect (refraction, blur, chromatic aberration) for web interfaces.
- **Stack**: TypeScript/WebGL.
- **Relevance**: Directly applicable to this project's UI/UX priorities (microinteractions, gradient meshes, modern visual effects) as specified in the project's design instructions. Candidate for a bento-grid/hero visual-effect component.

## 6. business-website-template (HooBank)

- **URL**: https://github.com/itsDaiton/business-website-template
- **Status**: Active.
- **Type**: Modern responsive business website design template ("HooBank").
- **Stack**: React, Tailwind CSS, Vite.
- **Relevance**: Directly aligned with this project's prioritized stack (React 18+, Tailwind CSS 3+, Vite 5+). Useful as a starting reference for marketing/landing-page layout conventions.

## 7. penpot-mcp

- **URL**: https://github.com/montevive/penpot-mcp
- **Status**: Active.
- **Type**: Penpot MCP Server -- connects Claude AI and other LLMs to Penpot (open-source design tool) designs via the Model Context Protocol, enabling AI-powered design workflow automation.
- **Stack**: Python, Model Context Protocol (MCP).
- **Relevance**: HIGH -- directly relevant to the multi-agent/bridge architecture work discussed earlier (Obsidian-MCP integration point, external agent coordination). This is a concrete, working example of an MCP server bridging a design tool to LLM agents; worth deeper architectural review as a template for exposing this project's own Vault/design assets via MCP.

## 8. gh-web-ui

- **URL**: https://github.com/mitevpi/gh-web-ui
- **Status**: Active (prototype stage per repository description).
- **Type**: Package for building web-based UIs inside Rhino Grasshopper (parametric/3D design tool), using native web components.
- **Stack**: JavaScript/C# (Grasshopper plugin ecosystem).
- **Relevance**: LOW -- niche to Rhino/Grasshopper parametric design workflows; no direct overlap with this project's current web/AI stack. Retained for reference only.

## Summary Table

| Repository | Status | Stack | Relevance |
|---|---|---|---|
| Ivy Wallet | Archived | Kotlin/Compose | Low (architecture reference only) |
| PyWinAssistant | Active | Python | Medium-High (Windows automation for local agents) |
| hicetnunc | Deprecated | React/SCSS | Low (historical only) |
| MaterialYouNewTab | Active | JS/HTML/CSS | Medium (design tokens/theming) |
| LiquidGlass | Active | TypeScript/WebGL | Medium-High (UI visual effects) |
| business-website-template | Active | React/Tailwind/Vite | Medium (landing page reference) |
| penpot-mcp | Active | Python/MCP | High (agent-design-tool bridge pattern) |
| gh-web-ui | Active (prototype) | JS/C# | Low (niche domain) |
