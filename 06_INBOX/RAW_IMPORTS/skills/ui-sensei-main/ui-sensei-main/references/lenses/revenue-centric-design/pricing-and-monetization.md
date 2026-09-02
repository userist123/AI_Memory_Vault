# RCD Theme: Pricing & Monetization

Part of the `revenue-centric-design` lens — see `revenue-centric-design.md` for the hard gambling-use restriction and attribution requirement before applying anything below.

## Principle: "Default is the decision you made for the user"

- **Apply when:** designing a pricing page, plan selector, or any form with pre-selected options.
- **The move:** the pre-selected plan or toggle state is not neutral — it's a design decision that will define the majority's behavior, since most users accept the default rather than actively choosing. Set the default to the plan/option that's genuinely best for most users, not just the one that maximizes short-term revenue — defaults that feel exploitative erode trust and increase churn.
- **Evidence:** default-effect research across behavioral economics broadly (not RCD-specific) shows default options are chosen at dramatically higher rates than any actively-selected alternative.
- **Visual:** the "recommended" or pre-highlighted tier in a pricing table; a toggle already set to annual billing if annual is the intended default.

## Principle: "Expansion is born of usage"

- **Apply when:** deciding when and how to prompt an upgrade.
- **The move:** surface the upgrade prompt at the moment the user hits a real limit of their current plan — not via an interruption unrelated to what they were doing. The friction they feel *is* the pitch; a well-timed limit-hit prompt converts better than a generic "upgrade now" banner.
- **Evidence:** cited by the source as a recurring pattern across SaaS upgrade flows — the prompt with the highest conversion is the one triggered by an actual constraint the user just experienced, not an arbitrary interruption.
- **Visual:** a soft paywall shown exactly when a usage cap is hit (e.g. "You've used all 3 free exports this month — upgrade to continue"), rather than a persistent nag banner.

## Related, general behavioral-economics concept worth pairing here (not RCD-specific, broadly documented)

**Decoy effect / asymmetric dominance:** adding a third pricing option that's clearly worse than one tier but not clearly worse than another shifts preference toward the tier it makes look good by comparison — documented to shift selection toward the target tier substantially in controlled pricing experiments. Use with restraint: over-engineering the comparison, or making the decoy too obviously bad, undermines trust once users notice the pattern.

---
**Provenance:** See `revenue-centric-design.md` for full attribution requirement and licensing detail.