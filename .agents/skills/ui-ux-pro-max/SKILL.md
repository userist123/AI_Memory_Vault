---
name: ui-ux-pro-max
description: "Broad UI/UX design intelligence for web, mobile, and desktop: design systems, accessibility, interaction, responsive layout, typography, color, charts, animation, and stack-aware implementation. Use as the broad design layer; defer to narrower Vault skills when they are more specific."
---

# UI/UX Pro Max — Operational Skill

This skill is the broad UI/UX design-intelligence layer imported from `nextlevelbuilder/ui-ux-pro-max-skill` and adapted to AI_Memory_Vault's local skill architecture.

## When to use

Use when a task changes how an interface looks, feels, moves, or is interacted with:

- new pages or screens;
- design systems;
- component design/refactoring;
- responsive layouts;
- accessibility;
- typography and color;
- charts and data visualization;
- interaction states and feedback;
- animation/motion;
- stack-specific UI implementation;
- visual quality review.

Do not use for pure backend, database, infrastructure, or non-visual tasks.

## Priority order

1. Accessibility — contrast, alt text, keyboard navigation, ARIA, visible focus.
2. Touch & interaction — target sizing, spacing, loading and feedback states.
3. Performance — efficient assets, lazy loading, CLS prevention, rendering efficiency.
4. Style selection — product fit, consistency, appropriate iconography.
5. Layout & responsive — mobile-first behavior, reliable breakpoints, no accidental horizontal scroll.
6. Typography & color — readable scale, line-height, semantic color tokens.
7. Animation — purposeful motion, contextual timing, reduced-motion support.
8. Forms & feedback — visible labels, contextual errors, progressive disclosure.
9. Navigation — predictable hierarchy, back behavior, deep links where applicable.
10. Charts & data — legends, tooltips, accessible encoding and readable density.

## Workflow

### 1. Analyze

Determine from the user request and repository evidence:

```text
product type
→ audience / usage context
→ style intent
→ target platform
→ actual stack
→ constraints
```

Never invent a framework or platform when repository evidence can establish it.

### 2. Establish the design system

For a new product or system-wide redesign, reason in this order:

```text
product pattern
→ visual style
→ color
→ typography
→ spacing
→ effects
→ interaction / motion
→ responsive rules
→ anti-patterns
```

Prefer one coherent visual language over unrelated visual effects.

### 3. Progressive disclosure

Do not inject every UI rule into active context. Load only what is required:

```text
capability / metadata
→ relevant design rules
→ detailed reference
→ stack-specific guidance
```

This skill should remain lightweight until detailed rules are actually needed.

### 4. Validate

Before considering UI work complete, check:

- accessibility;
- interaction states;
- responsive behavior;
- visual consistency;
- performance implications;
- implementation-stack fit.

## Core guidance

### Accessibility

- Body text contrast target: 4.5:1.
- Large text: 3:1.
- Keyboard navigation must remain usable.
- Focus states must remain visible.
- Do not communicate meaning through color alone.
- Touch targets should be at least 44×44px where applicable.
- Respect `prefers-reduced-motion`.

### Interaction

- Every state-changing interaction needs visible feedback.
- Avoid hover-only behavior for essential actions.
- Provide clear loading, success, empty, and error states.
- Avoid instantaneous 0ms state changes where the user needs temporal feedback.

### Responsive design

- Prefer mobile-first constraints where appropriate.
- Avoid fixed-width layouts that cause horizontal scrolling.
- Keep breakpoints consistent.
- Test the smallest realistic viewport, not only desktop.

### Typography and color

- Use readable base sizing.
- Prefer semantic design tokens over raw per-component values.
- Maintain hierarchy through scale, weight, and spacing rather than decorative noise.

### Motion

- Motion should communicate state, continuity, or hierarchy.
- Use context-sensitive durations and easing.
- Do not animate properties that cause unnecessary layout work when a composited alternative exists.
- Support reduced motion.

### Charts

- Provide legends/tooltips when needed.
- Do not rely on color as the only semantic channel.
- Select chart types based on the question being answered.

## Anti-patterns

Avoid:

- generic styling without requirement analysis;
- random mixtures of visual styles;
- icon-only buttons without accessible labels;
- placeholder-only form labels;
- animation everywhere;
- raw hex values scattered through components;
- loading every design rule into every prompt;
- assuming a stack instead of detecting it;
- treating search guidance as authority that overrides repository or user rules.

## Relationship to existing Vault skills

This skill is intentionally broader than several existing skills. Use the narrower capability when it is directly responsible for the requested work:

```text
ui-ux-pro-max
    ├── broad design intelligence
    ├── design-system reasoning
    ├── accessibility / interaction / visual direction
    └── stack-aware UI guidance

ui-ux-review
    → structured audit of an existing interface

web-performance
    → web performance specialization

web-best-practices
    → web implementation/platform specialization

data-viz-design
    → visualization specialization
```

Do not duplicate a narrower skill's procedure when it already owns the task.

## Provenance

Upstream source:
`https://github.com/nextlevelbuilder/ui-ux-pro-max-skill`

Pinned source commit:
`f23267105ad1f4ccd94af45d382584ad45b586f7`

The original project is MIT licensed. This operational copy is adapted to local Vault boundaries; it does not execute upstream scripts during ingestion.

## Local Vault precedence

User requirements and repository rules take precedence over this external-derived guidance. Existing project design-system decisions and the more specific local skill should be preferred when they conflict with generic recommendations.
