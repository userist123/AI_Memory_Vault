import type { ComponentProps } from "react";
import { DeviceFrame } from "./DeviceFrames";
import { PRESET_IDS, PRESET_INFO, isPreset, type PresetId } from "./PresetPicker";

/**
 * W1: one-click theme switcher for the preview. It never touches the preview frame's DOM (other origin, sandboxed):
 * it only adds two whitelisted query values to the preview URL, `aas-theme` (light|dark) and `aas-preset`, which the
 * kit's src/ui/theme.ts reads from a whitelist. "split" shows the same build in light and dark side by side.
 */
export type ThemeMode = "auto" | "light" | "dark" | "split";
export interface PreviewTheme {
  mode: ThemeMode;
  /** "project" = the preset stored with the project */
  preset: PresetId | "project";
}
export const DEFAULT_PREVIEW_THEME: PreviewTheme = { mode: "auto", preset: "project" };

const MODES: { id: ThemeMode; label: string; title: string }[] = [
  { id: "auto", label: "Auto", title: "Tema aplicației (urmează sistemul)" },
  { id: "light", label: "Luminos", title: "Previzualizare în modul luminos" },
  { id: "dark", label: "Întunecat", title: "Previzualizare în modul întunecat" },
  { id: "split", label: "Ambele", title: "Luminos și întunecat, unul lângă altul" },
];

/** The preview URL with the theme query values; null stays null. Only known values are ever written. */
export function themedSrc(src: string | null, theme: PreviewTheme, side?: "light" | "dark"): string | null {
  if (!src) return null;
  let u: URL;
  try {
    u = new URL(src);
  } catch {
    return src;
  }
  u.searchParams.delete("aas-theme");
  u.searchParams.delete("aas-preset");
  const t = side ?? (theme.mode === "light" || theme.mode === "dark" ? theme.mode : null);
  if (t) u.searchParams.set("aas-theme", t);
  if (isPreset(theme.preset)) u.searchParams.set("aas-preset", theme.preset);
  return u.toString();
}

export function ThemeSwitcher(props: { value: PreviewTheme; onChange: (t: PreviewTheme) => void }) {
  const { value, onChange } = props;
  return (
    <div className="theme-switcher" role="group" aria-label="Temă previzualizare" data-testid="theme-switcher">
      {MODES.map((m) => (
        <button
          key={m.id}
          type="button"
          className={`btn small${value.mode === m.id ? " on" : ""}`}
          aria-pressed={value.mode === m.id}
          title={m.title}
          data-testid={`theme-${m.id}`}
          onClick={() => onChange({ ...value, mode: m.id })}
        >
          {m.label}
        </button>
      ))}
      <select
        aria-label="Stil previzualizare"
        data-testid="theme-preset"
        value={value.preset}
        onChange={(e) => onChange({ ...value, preset: isPreset(e.target.value) ? e.target.value : "project" })}
      >
        <option value="project">Stilul proiectului</option>
        {PRESET_IDS.map((p) => (
          <option key={p} value={p}>
            {PRESET_INFO[p].label}
          </option>
        ))}
      </select>
    </div>
  );
}

/** DeviceFrame with the chosen theme; "split" renders a light and a dark frame of the same build. */
export function ThemedPreview(props: ComponentProps<typeof DeviceFrame> & { theme: PreviewTheme }) {
  const { theme, src, ...rest } = props;
  if (theme.mode === "split" && src) {
    return (
      <div className="theme-split" data-testid="theme-split">
        <DeviceFrame {...rest} src={themedSrc(src, theme, "light")} />
        <DeviceFrame {...rest} src={themedSrc(src, theme, "dark")} />
      </div>
    );
  }
  return <DeviceFrame {...rest} src={themedSrc(src, theme)} />;
}
