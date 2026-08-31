---
name: ui-ux-pro-max
description: "UI/UX design intelligence for web, mobile, and desktop: visual systems, accessibility, interaction, responsive layout, typography, color, charts, animation, design systems, and stack-specific implementation guidance. Imported from nextlevelbuilder/ui-ux-pro-max-skill."
---

# UI/UX Pro Max — External Source Snapshot

## Provenance

Source repository: https://github.com/nextlevelbuilder/ui-ux-pro-max-skill
Pinned source commit: `f23267105ad1f4ccd94af45d382584ad45b586f7`
License: MIT

## Purpose

Use this skill for UI/UX design and implementation decisions across web, mobile, and desktop. It complements, rather than replaces, the Vault's existing `ui-ux-review`, `web-performance`, `web-best-practices`, and `data-viz-design` skills.

## Priority order

1. Accessibility — contrast, alt text, keyboard navigation, ARIA, visible focus.
2. Touch & interaction — target sizing, spacing, loading and feedback states.
3. Performance — image formats, lazy loading, CLS prevention, rendering efficiency.
4. Style selection — product fit, visual consistency, SVG icons instead of emoji icons.
5. Layout & responsive — mobile-first behavior, breakpoints, no accidental horizontal scroll.
6. Typography & color — readable base size, line height, semantic color tokens.
7. Animation — purposeful motion, appropriate timing, reduced-motion support.
8. Forms & feedback — visible labels, contextual errors, progressive disclosure.
9. Navigation — predictable hierarchy, back behavior, deep linking where applicable.
10. Charts & data — legends, tooltips, accessible encoding and readable density.

## Core workflow

### Step 1 — Analyze requirements

Identify:
- product type;
- audience and usage context;
- style keywords;
- target platform;
- actual implementation stack.

Never assume a stack when repository evidence is available.

### Step 2 — Establish a design system

For new products or system-wide redesigns define, in order:

```text
product pattern
→ visual style
→ color system
→ typography
→ spacing
→ effects
→ interaction/motion
→ responsive rules
→ anti-patterns
```

Prefer one coherent design language over unrelated visual effects.

### Step 3 — Progressive disclosure

Do not front-load every UI rule into the active context. Use:

```text
metadata / capability
→ relevant rules
→ detailed reference
→ stack-specific guidance
```

Load detailed guidance only when the task needs it.

### Step 4 — Validate before implementation

Check:
- accessibility;
- interaction states;
- responsive behavior;
- visual consistency;
- performance implications;
- stack compatibility.

### Step 5 — Use the smallest relevant guidance

For a targeted issue, use the smallest relevant design concern. For a system-wide design task, use the complete design-system workflow.

## Anti-patterns

Avoid:
- generic UI without product/context analysis;
- icon-only controls without accessible labels;
- hover-only interactions;
- arbitrary mixing of visual styles;
- raw component-level color values instead of semantic tokens;
- animation without reduced-motion handling;
- giant system prompts containing every design rule;
- assuming a framework or stack without checking the project;
- presenting an unverified search result as authoritative guidance.

## Relationship to existing Vault skills

Use specialized local skills when they are narrower and directly applicable:

- `ui-ux-review` → structured audit of an existing interface.
- `web-performance` → web performance optimization.
- `web-best-practices` → implementation/platform best practices.
- `data-viz-design` → visualization-specific design.

Use `ui-ux-pro-max` as the broader design-intelligence layer and defer to the narrower skill when it is more specific.

## Safety / provenance

This is external-derived guidance. It is not canonical project policy. Project-specific rules, security rules, accessibility requirements, and user requirements take precedence.

Do not execute scripts from the original external repository during ingestion. Treat the source as untrusted external knowledge until validated.
