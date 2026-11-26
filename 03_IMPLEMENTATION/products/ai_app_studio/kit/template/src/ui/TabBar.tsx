import { useRef, type KeyboardEvent, type ReactNode } from "react";

export interface TabItem {
  id: string;
  label: string;
  icon?: ReactNode;
}

/** ids that tie a tab to its panel: <main role="tabpanel" id={panelId(t)} aria-labelledby={tabId(t)}> */
export const tabId = (id: string) => `tab-${id}`;
export const panelId = (id: string) => `panel-${id}`;

/**
 * Bottom tab bar (phone) with the WAI-ARIA tabs pattern: ArrowRight, ArrowLeft, Home and End move and select; only
 * the active tab is in the tab order (tabIndex). Pads itself with the bottom safe area.
 */
export function TabBar(props: { tabs: TabItem[]; active: string; onChange: (id: string) => void; label?: string }) {
  const { tabs, active, onChange } = props;
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const index = Math.max(0, tabs.findIndex((t) => t.id === active));

  const select = (i: number) => {
    const next = (i + tabs.length) % tabs.length;
    const tab = tabs[next];
    if (!tab) return;
    onChange(tab.id);
    refs.current[next]?.focus();
  };

  const onKey = (e: KeyboardEvent<HTMLButtonElement>) => {
    if (e.key === "ArrowRight") select(index + 1);
    else if (e.key === "ArrowLeft") select(index - 1);
    else if (e.key === "Home") select(0);
    else if (e.key === "End") select(tabs.length - 1);
    else return;
    e.preventDefault();
  };

  return (
    <nav className="ui-tabbar" aria-label={props.label ?? "Main"}>
      <div role="tablist" aria-label="Sections" className="ui-tabbar__list">
        {tabs.map((t, i) => (
          <button
            key={t.id}
            ref={(el) => {
              refs.current[i] = el;
            }}
            id={tabId(t.id)}
            role="tab"
            type="button"
            className="ui-tabbar__tab"
            aria-selected={i === index}
            aria-controls={panelId(t.id)}
            tabIndex={i === index ? 0 : -1}
            onClick={() => onChange(t.id)}
            onKeyDown={onKey}
          >
            {t.icon ? (
              <span className="ui-tabbar__icon" aria-hidden="true">
                {t.icon}
              </span>
            ) : null}
            <span className="ui-tabbar__label">{t.label}</span>
          </button>
        ))}
      </div>
    </nav>
  );
}
