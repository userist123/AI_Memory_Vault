/**
 * W3 preview state switch: Live / Populated / Loading / Empty / Error. The preview iframe is reloaded with
 * ?aas-state=<state>; the kit hook (kit/template/src/aas-states.ts, useAasState) reads it from the app's own URL.
 * No postMessage, no script injection: the studio only changes the iframe URL on the project's preview origin.
 */
export type PreviewState = "populated" | "loading" | "empty" | "error";
export const PREVIEW_STATES: readonly PreviewState[] = ["populated", "loading", "empty", "error"];
const LABELS: Record<PreviewState, string> = { populated: "Populated", loading: "Loading", empty: "Empty", error: "Error" };
export const STATE_PARAM = "aas-state";

/** The preview URL with the state flag set (or removed for live); null and non-http(s) URLs pass through untouched. */
export function withPreviewState(src: string | null, state: PreviewState | null): string | null {
  if (!src) return src;
  let url: URL;
  try {
    url = new URL(src);
  } catch {
    return src;
  }
  if (url.protocol !== "http:" && url.protocol !== "https:") return src;
  if (state && PREVIEW_STATES.includes(state)) url.searchParams.set(STATE_PARAM, state);
  else url.searchParams.delete(STATE_PARAM);
  return url.toString();
}

export function StateSwitcher(props: { value: PreviewState | null; onChange: (s: PreviewState | null) => void }) {
  const options: (PreviewState | null)[] = [null, ...PREVIEW_STATES];
  return (
    <span className="state-switcher" role="group" aria-label="Preview state" data-testid="state-switcher"
      style={{ display: "inline-flex", gap: 2, marginLeft: 8 }}>
      {options.map((s) => {
        const on = props.value === s;
        return (
          <button
            key={s ?? "live"}
            type="button"
            className={`tag${on ? " accent" : ""}`}
            style={{ border: 0, cursor: "pointer" }}
            aria-pressed={on}
            data-testid={`state-${s ?? "live"}`}
            title={s ? `Force the ${LABELS[s]} state (?${STATE_PARAM}=${s})` : "Live app state"}
            onClick={() => props.onChange(s)}
          >
            {s ? LABELS[s] : "Live"}
          </button>
        );
      })}
    </span>
  );
}
