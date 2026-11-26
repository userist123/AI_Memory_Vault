import { DEFAULT_THEME, PRESET, type PresetId } from "./preset";

/** Every preset in src/ui/globals.css. The studio preview may ask for another one with ?aas-preset=<id>. */
export const PRESETS: readonly PresetId[] = ["clean", "material", "dark-pro", "warm", "playful", "minimal"];
export type Theme = "light" | "dark";
export interface ThemeState {
  preset: PresetId;
  theme: Theme;
  /** true when the theme came from the URL (studio theme switcher): it does not follow the system setting then */
  forced: boolean;
}

const isPreset = (v: string | null): v is PresetId => v !== null && (PRESETS as readonly string[]).includes(v);
const isTheme = (v: string | null): v is Theme => v === "light" || v === "dark";

/** Reads only two whitelisted query values (aas-preset, aas-theme); anything else in the URL is ignored. */
export function resolveTheme(search: string, prefersDark: boolean): ThemeState {
  const q = new URLSearchParams(search);
  const p = q.get("aas-preset");
  const t = q.get("aas-theme");
  const preset = isPreset(p) ? p : PRESET;
  if (isTheme(t)) return { preset, theme: t, forced: true };
  const theme: Theme = DEFAULT_THEME === "dark" || prefersDark ? "dark" : "light";
  return { preset, theme, forced: false };
}

export function applyTheme(state: { preset: PresetId; theme: Theme }, root: HTMLElement = document.documentElement): void {
  root.dataset.preset = state.preset;
  root.dataset.theme = state.theme;
}

/** Call once before rendering (src/main.tsx). Follows the system light/dark setting unless the URL forces one. */
export function initTheme(): () => void {
  const mq = typeof matchMedia === "function" ? matchMedia("(prefers-color-scheme: dark)") : null;
  const state = resolveTheme(location.search, mq?.matches ?? false);
  applyTheme(state);
  if (!mq || state.forced || DEFAULT_THEME === "dark") return () => undefined;
  const onChange = (e: MediaQueryListEvent) => applyTheme({ preset: state.preset, theme: e.matches ? "dark" : "light" });
  mq.addEventListener("change", onChange);
  return () => mq.removeEventListener("change", onChange);
}

/** For an in-app light/dark toggle. */
export function setTheme(theme: Theme): void {
  const root = document.documentElement;
  const current = root.dataset.preset ?? null;
  applyTheme({ preset: isPreset(current) ? current : PRESET, theme }, root);
}
