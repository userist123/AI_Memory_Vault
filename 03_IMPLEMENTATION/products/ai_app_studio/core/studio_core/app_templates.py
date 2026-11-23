"""App templates (S9b): `tabs` and `list-detail`, React + TypeScript, mobile-first, accessible.

Every file is inside the write allow-list (src/** and public/**, appfiles). Styles are mobile-first (base rules =
phone layout, `@media (min-width: ...)` widens), focus is always visible, touch targets are >= 44 px, colours keep
WCAG AA contrast in light and dark mode. `blank` stays in app_kind.py.
"""
from __future__ import annotations

MAIN_TSX = """import { createRoot } from "react-dom/client";
import App from "./App";
import "./app.css";

createRoot(document.getElementById("root")!).render(<App />);
"""

ICON_SVG = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">
  <rect width="64" height="64" rx="14" fill="#2563eb"/>
  <path d="M20 44 32 18l12 26" fill="none" stroke="#ffffff" stroke-width="5" stroke-linejoin="round"/>
</svg>
"""

BASE_CSS = """:root {
  color-scheme: light dark;
  --bg: #ffffff;
  --fg: #111827;
  --muted: #4b5563;
  --line: #d1d5db;
  --accent: #1d4ed8;
  --accent-fg: #ffffff;
  font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
  line-height: 1.5;
}
@media (prefers-color-scheme: dark) {
  :root { --bg: #0f172a; --fg: #f1f5f9; --muted: #cbd5e1; --line: #334155; --accent: #93c5fd; --accent-fg: #0f172a; }
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--fg); }
button { font: inherit; color: inherit; min-height: 44px; }
:focus-visible { outline: 3px solid var(--accent); outline-offset: 2px; }
@media (prefers-reduced-motion: reduce) { * { transition: none !important; animation: none !important; } }
"""

TABS_APP = """import { useRef, useState, type KeyboardEvent } from "react";

type Tab = { id: string; label: string; title: string; body: string };

const TABS: Tab[] = [
  { id: "home", label: "Home", title: "Welcome", body: "Start here. Replace this text with your app's home screen." },
  { id: "search", label: "Search", title: "Search", body: "Put a search field and results here." },
  { id: "profile", label: "Profile", title: "Profile", body: "Show the user's settings and details here." },
];

export default function App() {
  const [active, setActive] = useState(0);
  const refs = useRef<(HTMLButtonElement | null)[]>([]);

  const select = (i: number) => {
    const next = (i + TABS.length) % TABS.length;
    setActive(next);
    refs.current[next]?.focus();
  };

  const onKey = (e: KeyboardEvent<HTMLButtonElement>) => {
    if (e.key === "ArrowRight") select(active + 1);
    else if (e.key === "ArrowLeft") select(active - 1);
    else if (e.key === "Home") select(0);
    else if (e.key === "End") select(TABS.length - 1);
    else return;
    e.preventDefault();
  };

  const tab = TABS[active];
  return (
    <div className="shell">
      <main
        className="panel"
        role="tabpanel"
        id={`panel-${tab.id}`}
        aria-labelledby={`tab-${tab.id}`}
        tabIndex={0}
      >
        <h1>{tab.title}</h1>
        <p>{tab.body}</p>
      </main>
      <nav className="tabbar" aria-label="Main">
        <div role="tablist" aria-label="Sections">
          {TABS.map((t, i) => (
            <button
              key={t.id}
              ref={(el) => {
                refs.current[i] = el;
              }}
              id={`tab-${t.id}`}
              role="tab"
              type="button"
              aria-selected={i === active}
              aria-controls={`panel-${t.id}`}
              tabIndex={i === active ? 0 : -1}
              onClick={() => setActive(i)}
              onKeyDown={onKey}
            >
              {t.label}
            </button>
          ))}
        </div>
      </nav>
    </div>
  );
}
"""

TABS_CSS = BASE_CSS + """
.shell { min-height: 100dvh; display: flex; flex-direction: column; }
.panel { flex: 1; padding: 1rem; }
.panel h1 { font-size: 1.5rem; margin: 0 0 0.5rem; }
.tabbar { position: sticky; bottom: 0; border-top: 1px solid var(--line); background: var(--bg);
  padding-bottom: env(safe-area-inset-bottom); }
.tabbar [role="tablist"] { display: grid; grid-template-columns: repeat(3, 1fr); }
.tabbar button { border: 0; background: none; padding: 0.75rem 0.25rem; color: var(--muted); }
.tabbar button[aria-selected="true"] { color: var(--accent); font-weight: 600; box-shadow: inset 0 3px 0 var(--accent); }
@media (min-width: 768px) {
  .shell { flex-direction: column-reverse; }
  .tabbar { position: static; border-top: 0; border-bottom: 1px solid var(--line); }
  .tabbar [role="tablist"] { display: flex; gap: 0.5rem; padding: 0 1rem; }
  .tabbar button { padding: 0.75rem 1rem; }
  .tabbar button[aria-selected="true"] { box-shadow: inset 0 -3px 0 var(--accent); }
  .panel { max-width: 48rem; margin: 0 auto; width: 100%; padding: 2rem 1rem; }
}
"""

LIST_DETAIL_APP = """import { useEffect, useRef, useState } from "react";

type Item = { id: string; title: string; summary: string; body: string };

const ITEMS: Item[] = [
  { id: "1", title: "First item", summary: "A short summary", body: "Details of the first item go here." },
  { id: "2", title: "Second item", summary: "Another summary", body: "Details of the second item go here." },
  { id: "3", title: "Third item", summary: "One more", body: "Details of the third item go here." },
];

export default function App() {
  const [selected, setSelected] = useState<string | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const item = ITEMS.find((i) => i.id === selected) ?? null;

  useEffect(() => {
    if (item) heading.current?.focus(); // move focus to the detail for keyboard and screen-reader users
  }, [item]);

  return (
    <div className={item ? "layout has-detail" : "layout"}>
      <nav className="list" aria-label="Items">
        <h1>Items</h1>
        <ul>
          {ITEMS.map((i) => (
            <li key={i.id}>
              <button type="button" aria-current={i.id === selected ? "true" : undefined} onClick={() => setSelected(i.id)}>
                <span className="title">{i.title}</span>
                <span className="summary">{i.summary}</span>
              </button>
            </li>
          ))}
        </ul>
      </nav>
      <main className="detail" aria-label="Details">
        {item ? (
          <article>
            <button type="button" className="back" onClick={() => setSelected(null)}>
              Back
            </button>
            <h2 ref={heading} tabIndex={-1}>
              {item.title}
            </h2>
            <p>{item.body}</p>
          </article>
        ) : (
          <p className="empty">Choose an item to see its details.</p>
        )}
      </main>
    </div>
  );
}
"""

LIST_DETAIL_CSS = BASE_CSS + """
.layout { min-height: 100dvh; }
.list h1 { font-size: 1.5rem; margin: 0; padding: 1rem; }
.list ul { list-style: none; margin: 0; padding: 0; }
.list button { display: flex; flex-direction: column; align-items: flex-start; width: 100%; text-align: left;
  border: 0; border-bottom: 1px solid var(--line); background: none; padding: 0.75rem 1rem; }
.list button[aria-current="true"] { background: color-mix(in srgb, var(--accent) 14%, transparent); }
.summary { color: var(--muted); font-size: 0.9rem; }
.detail { padding: 1rem; }
.detail h2 { margin-top: 0.5rem; }
.back { border: 1px solid var(--line); border-radius: 8px; background: none; padding: 0 1rem; }
.empty { color: var(--muted); }
/* phone: one pane at a time */
.layout .detail { display: none; }
.layout.has-detail .list { display: none; }
.layout.has-detail .detail { display: block; }
@media (min-width: 768px) {
  .layout { display: grid; grid-template-columns: 20rem 1fr; }
  .layout .list, .layout.has-detail .list { display: block; border-right: 1px solid var(--line); }
  .layout .detail { display: block; padding: 2rem; }
  .back { display: none; }
}
"""

TEMPLATES: dict[str, dict[str, str]] = {
    "tabs": {"src/main.tsx": MAIN_TSX, "src/App.tsx": TABS_APP, "src/app.css": TABS_CSS, "public/icon.svg": ICON_SVG},
    "list-detail": {"src/main.tsx": MAIN_TSX, "src/App.tsx": LIST_DETAIL_APP, "src/app.css": LIST_DETAIL_CSS,
                    "public/icon.svg": ICON_SVG},
}
