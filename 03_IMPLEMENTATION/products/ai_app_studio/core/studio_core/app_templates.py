"""App templates (S9b, W1): `blank`, `tabs` and `list-detail`, React + TypeScript, mobile-first, accessible, built from
the kit design system (kit/DESIGN.md): semantic tokens (src/ui/globals.css), the component catalogue (src/ui/*.tsx)
and one of six style presets chosen at project creation (template id ``<template>:<preset>``).

The sources live in the studio kit (kit/template/**) and are read from there, so the kit files, the Docker kit build
and new projects never drift apart. Every file is inside the write allow-list (src/** and public/**, appfiles).
"""
from __future__ import annotations

import re
from pathlib import Path
from typing import Any

KIT_TEMPLATE = Path(__file__).resolve().parents[2] / "kit" / "template"
_SRC = KIT_TEMPLATE / "src"
_UI = _SRC / "ui"

ICON_SVG = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">
  <rect width="64" height="64" rx="14" fill="#2563eb"/>
  <path d="M20 44 32 18l12 26" fill="none" stroke="#ffffff" stroke-width="5" stroke-linejoin="round"/>
</svg>
"""

# Style presets (token sets in kit/template/src/ui/globals.css). theme: "system" follows the device, "dark" starts dark.
PRESETS: dict[str, dict[str, str]] = {
    "clean": {"label": "Clean", "theme": "system"},
    "material": {"label": "Material tonal", "theme": "system"},
    "dark-pro": {"label": "Dark pro", "theme": "dark"},
    "warm": {"label": "Warm editorial", "theme": "system"},
    "playful": {"label": "Playful", "theme": "system"},
    "minimal": {"label": "Minimal mono", "theme": "system"},
}
DEFAULT_PRESET = "clean"

# kit/DESIGN.md "The 7 rules" (same text, test-enforced). Data for the runner system prompt: style rules only.
DESIGN_RULES: tuple[tuple[str, str], ...] = (
    ("type-scale", "Typography: at most 2 font families (var(--font-body) and var(--font-display)) and only the type "
                   "scale --text-xs to --text-2xl; no ad-hoc font sizes."),
    ("grid", "Spacing: a 4/8 px grid only (var(--space-1) to var(--space-8)); no arbitrary margins or paddings."),
    ("color-tokens", "Colour: only the semantic tokens var(--color-*) from src/ui/globals.css, never raw hex values in "
                     "components or screens; text keeps WCAG AA contrast (4.5:1, 3:1 for large text and UI parts)."),
    ("dark-mode", "Dark mode: tonal, not inverted; it comes from the same tokens (data-theme=\"dark\" on html), so never "
                  "hard-code light-only colours."),
    ("touch-targets", "Touch: every interactive element is at least 44x44 px (var(--touch)) and keeps a visible "
                      ":focus-visible ring."),
    ("safe-areas", "Safe areas: the studio kit page already sets viewport-fit=cover; pad fixed headers, tab bars, sheets and full-screen "
                   "pages with var(--safe-top), var(--safe-bottom), var(--safe-left) and var(--safe-right)."),
    ("states", "States: every screen that loads or lists data has a loading state (Skeleton), an empty state "
               "(EmptyState) and an error state (EmptyState tone=\"error\" with a retry action); actions give feedback "
               "within 200 ms (pressed and hover micro-interactions, Toast), and motion respects prefers-reduced-motion. "
               "Wire the states to the studio preview switch: import { useAasState } from \"./aas-states\" and let its "
               "value (loading, empty, error or populated; null in normal use) override the screen's real state."),
)

TEMPLATE_NAMES = ("blank", "tabs", "list-detail")
_ID = re.compile(r"^[a-z][a-z-]{0,30}$")


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8").replace("\r\n", "\n")


def _token_names(kind: str) -> tuple[str, ...]:
    """Names of the ``--<kind>-<name>`` custom properties declared in src/ui/globals.css, in first-seen order."""
    css = _read(_UI / "globals.css")
    return tuple(dict.fromkeys(re.findall(rf"--{kind}-([a-z0-9]+(?:-[a-z0-9]+)*)\s*:", css)))


# The semantic tokens of the design system: the single source of truth for every consumer (the W4 inspector's
# class allow-list, the utility classes in src/ui/utilities.css).
SEMANTIC_COLOR_TOKENS: tuple[str, ...] = _token_names("color")  # bg, surface, fg, muted, primary, ...
SPACING_STEPS: tuple[str, ...] = _token_names("space")  # 1..8 (4/8 px grid)
COLOR_UTILITIES = {"text": "color", "bg": "background-color", "border": "border-color"}
SPACING_UTILITIES = {"p": ("padding",), "px": ("padding-inline",), "py": ("padding-block",),
                     "pt": ("padding-top",), "pb": ("padding-bottom",), "pl": ("padding-left",),
                     "pr": ("padding-right",), "m": ("margin",), "mx": ("margin-inline",), "my": ("margin-block",),
                     "mt": ("margin-top",), "mb": ("margin-bottom",), "gap": ("gap",)}


# Kit components whose look is one string-union prop: (component, css class prefix, prop, source file, regex of the union).
# The values and the default are PARSED from the component source in src/ui, so the catalogue stays the only copy
# (wow2 #6: the preview shows ui-btn--danger etc., never the token utilities, so the inspector swaps these).
_MODIFIER_SPECS: tuple[tuple[str, str, str, str, str], ...] = (
    ("Button", "ui-btn", "variant", "Button.tsx", r"type\s+ButtonVariant\s*=\s*([^;]+);"),
    ("Toast", "ui-toast", "tone", "Toast.tsx", r"\btone\?:\s*([^;]+);"),
)


def component_modifiers() -> dict[str, dict[str, Any]]:
    """{css prefix: {component, prop, default, values}} for the kit components with a tone/variant prop."""
    out: dict[str, dict[str, Any]] = {}
    for comp, prefix, prop, fname, union_re in _MODIFIER_SPECS:
        src = _read(_UI / fname)
        m = re.search(union_re, src)
        d = re.search(rf"\b{prop}\s*=\s*\"([a-z]+)\"", src)
        if m is None or d is None:
            raise RuntimeError(f"kit component {comp}: cannot read its {prop} values")
        values = re.findall(r'"([a-z]+)"', m.group(1))
        if d.group(1) not in values:
            raise RuntimeError(f"kit component {comp}: default {prop} is not one of its values")
        out[prefix] = {"component": comp, "prop": prop, "default": d.group(1), "values": values}
    return out


def component_classes() -> list[str]:
    """Every allowed ``ui-<comp>--<value>`` class (the inspector's `component` family)."""
    return [f"{prefix}--{v}" for prefix, spec in component_modifiers().items() for v in spec["values"]]


_STATES_IMPORT = re.compile(r'''^[ \t]*import[^;\n]*\bfrom[ \t]*["'][^"']*aas-states["']''', re.M)


def preview_capabilities(files: dict[str, str]) -> dict[str, bool]:
    """What the studio preview switchers can do for a project (wow2 #4/#5), from its source files:
    ``states``: src/aas-states.ts exists AND another source imports it (else ?aas-state= changes nothing);
    ``theme``: src/ui/theme.ts exists (apps from before the W1 design system have none)."""
    states = "src/aas-states.ts" in files and any(
        _STATES_IMPORT.search(c) for p, c in files.items()
        if p != "src/aas-states.ts" and p.startswith("src/") and isinstance(c, str))
    return {"states": bool(states), "theme": "src/ui/theme.ts" in files}


def utility_classes() -> dict[str, list[str]]:
    """Token utility class names by family ({"color": [text-primary, ...], "spacing": [p-1, ...]})."""
    return {"color": [f"{p}-{t}" for p in COLOR_UTILITIES for t in SEMANTIC_COLOR_TOKENS],
            "spacing": [f"{p}-{s}" for p in SPACING_UTILITIES for s in SPACING_STEPS]}


def utilities_css() -> str:
    """Expected content of kit/template/src/ui/utilities.css (generated from the tokens, checked by the tests)."""
    out = ["/* AI App Studio token utilities (generated from globals.css by studio_core.app_templates.utilities_css;",
           " * do not edit by hand). One class per semantic token: the click-to-edit inspector swaps these. */"]
    out += [f".{p}-{t} {{ {prop}: var(--color-{t}); }}" for p, prop in COLOR_UTILITIES.items()
            for t in SEMANTIC_COLOR_TOKENS]
    out += [f".{p}-{s} {{ " + " ".join(f"{prop}: var(--space-{s});" for prop in props) + " }"
            for p, props in SPACING_UTILITIES.items() for s in SPACING_STEPS]
    return "\n".join(out) + "\n"


def preset_ts(preset: str) -> str:
    """src/ui/preset.ts of a project: the per-project preset choice."""
    if preset not in PRESETS:
        raise KeyError(preset)
    ids = " | ".join(f'"{p}"' for p in PRESETS)
    return ("// Style preset chosen in the studio at project creation (kit/DESIGN.md). Written by the studio; keep the "
            "names.\n"
            f"export type PresetId = {ids};\n"
            'export type ThemeChoice = "system" | "dark";\n'
            f'export const PRESET: PresetId = "{preset}";\n'
            f'export const DEFAULT_THEME: ThemeChoice = "{PRESETS[preset]["theme"]}";\n')


def _ui_files(preset: str) -> dict[str, str]:
    files = {f"src/ui/{p.name}": _read(p) for p in sorted(_UI.iterdir())
             if p.is_file() and p.suffix in (".ts", ".tsx", ".css") and p.name != "preset.ts"}
    files["src/ui/preset.ts"] = preset_ts(preset)
    return files


def _app_files(template: str) -> dict[str, str]:
    root = _SRC if template == "blank" else KIT_TEMPLATE / template
    return {"src/main.tsx": _read(_SRC / "main.tsx"), "src/App.tsx": _read(root / "App.tsx"),
            "src/app.css": _read(root / "app.css"), "public/icon.svg": ICON_SVG,
            # W3: the kit's preview-state hook ships with every template; the agent may edit it
            "src/aas-states.ts": _read(_SRC / "aas-states.ts")}


def parse_template_id(value: object) -> tuple[str, str] | None:
    """``"tabs"`` -> ("tabs", default preset); ``"tabs:warm"`` -> ("tabs", "warm"); anything else -> None."""
    if not isinstance(value, str):
        return None
    template, sep, preset = value.partition(":")
    if not sep:
        preset = DEFAULT_PRESET
    if not _ID.fullmatch(template) or not _ID.fullmatch(preset) or template not in TEMPLATE_NAMES or preset not in PRESETS:
        return None
    return template, preset


def files_for(template: str, preset: str = DEFAULT_PRESET) -> dict[str, str]:
    if template not in TEMPLATE_NAMES or preset not in PRESETS:
        raise KeyError(f"{template}:{preset}")
    return {**_app_files(template), **_ui_files(preset)}


TEMPLATES: dict[str, dict[str, str]] = {t: files_for(t) for t in TEMPLATE_NAMES if t != "blank"}
