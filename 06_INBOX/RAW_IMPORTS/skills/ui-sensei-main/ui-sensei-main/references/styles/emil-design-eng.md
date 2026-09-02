<!-- dials: variance=5 motion=6 density=5 (rough mapping — see note below; this source doesn't use a dial system natively, it's rules-based) -->

# Style: Emil Design Eng (adapted from emilkowalski/skills — emil-design-eng)

Not a visual aesthetic at all — this source is almost entirely about **motion and interaction craft**: the invisible details of easing, duration, and physicality that make an interface feel considered rather than merely functional. Where the other five style sources argue about layout, color, and density, this one argues about *how things move*. Its natural role in `ui-sensei` is as a **punctual contribution** in Step 5's precedence rule (one microinteraction, one easing decision) rather than a structural base — it doesn't have a strong opinion on layout or density to build a whole component around.

## Core philosophy

Good taste in interaction design is described as a trained instinct, not a personal preference — the ability to notice why one interface feels right and another merely works. The specific claim: in a world where most software is functionally "good enough," the felt quality of motion and detail is what actually differentiates one product from another.

## The four-step animation checklist (apply before writing any animation)

1. **Should this animate at all?** High-frequency actions (hover states, list navigation, anything triggered tens of times a day) often shouldn't — Raycast's lack of open/close animation is cited as the correct choice for something used constantly. Never animate keyboard-initiated actions; the delay works against the user's own sense of immediacy.
2. **What's the purpose?** Every animation needs a clear answer to "why does this animate?" — motion without a reason to exist is a cost, not a feature.
3. **What easing?** Entering/exiting elements should use `ease-out` (or a custom curve), never `ease-in` on UI — `ease-in` delays the moment the user is watching most closely. Built-in CSS easings are treated as too weak by default; the guidance is to reach for a stronger custom cubic-bezier rather than inventing one from scratch (easing.dev / easings.co as reference sources).
4. **What duration?** UI animations should stay under 300ms — a 180ms dropdown reads as more responsive than a 400ms one. Perceived speed matters as much as actual speed (a faster-spinning loading indicator feels quicker even at identical load time).

## Concrete interaction rules worth carrying into generated components

- **Origin-aware transforms.** Popovers, dropdowns, and tooltips should scale from their trigger point (`transform-origin`), not from center. Never animate entry from `scale(0)` — start from `scale(0.9–0.97)` combined with opacity instead. (Modals are the deliberate exception — they stay centered.)
- **Interruptibility.** Anything rapidly triggered or gesture-driven (toasts, toggles, drags) should use CSS transitions or springs that retarget from the current state — not keyframes that restart from zero.
- **GPU-only properties.** Animate `transform` and `opacity`; avoid animating layout-triggering properties.
- **Match motion to personality.** A playful component can be bouncier; a professional dashboard should read crisp and fast — motion should fit the mood of the surrounding product, not a fixed default.
- **Gesture physics.** Drag/dismiss interactions favor momentum-based dismissal, boundary damping, and friction over hard stops.
- **Typography details** carried alongside the motion guidance: cap line length around 65 characters, use tabular figures for numbers that need to align, use a single-character ellipsis rather than three periods.

## When this style contributes best

Almost never as the sole base for a component — pair it with any structural base source and let this one govern the specific interaction/motion details of whatever gets built. Particularly relevant for components with meaningful interaction surface: dropdowns, toasts, drawers, drag-to-dismiss elements, button press states.

**Tension worth flagging when the base is `brutalist.md`:** that source explicitly wants motion to feel mechanical — a hard cut, a snap-into-place — while this source's specific prescriptions (spring physics for gestures, soft `scale(0.9–0.97)` entry easing, momentum-based dismissal) lean fluid/organic. These aren't just different tastes, they're closer to opposites. If both are in play, don't silently apply emil-design-eng's specific curves — surface the conflict to the user rather than picking one side automatically.

---
**Provenance:** Adapted and distilled from `emilkowalski/skills`, skill `emil-design-eng`. Note this repo evolved from a single-skill repo (`emilkowalski/skill`) into a small collection (`emilkowalski/skills`) including sibling audit-focused skills (`review-animations`, `improve-animations`) not included in this curation — those are review/audit tools for existing codebases, not generative style candidates, so they're out of scope for `ui-sensei`'s select. MIT licensed; no attribution required beyond this note. Distilled paraphrase, not a reproduction.