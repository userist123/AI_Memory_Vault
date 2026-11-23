/**
 * W1: style preset of a new project (kit/DESIGN.md). The ids match core/studio_core/app_templates.py PRESETS and the
 * token blocks in kit/template/src/ui/globals.css; the swatches are bg / surface-2 / primary / fg of the preset's starting theme (dark for dark-pro).
 */
export const PRESET_IDS = ["clean", "material", "dark-pro", "warm", "playful", "minimal"] as const;
export type PresetId = (typeof PRESET_IDS)[number];
export const DEFAULT_PRESET: PresetId = "clean";

export const PRESET_INFO: Record<PresetId, { label: string; hint: string; swatch: [string, string, string, string] }> = {
  clean: { label: "Curat", hint: "neutru, ca iOS, accent albastru", swatch: ["#f5f5f7", "#ebebef", "#0a63d1", "#1c1c1e"] },
  material: { label: "Material tonal", hint: "tonuri Material 3, colțuri mari", swatch: ["#fef7ff", "#ece6f0", "#6750a4", "#1d1b20"] },
  "dark-pro": { label: "Dark pro", hint: "pornește întunecat, accent turcoaz", swatch: ["#0d1117", "#21262d", "#2dd4bf", "#e6edf3"] },
  warm: { label: "Editorial cald", hint: "hârtie crem, titluri serif", swatch: ["#faf6f0", "#f0e8dc", "#a63d1e", "#2b211a"] },
  playful: { label: "Jucăuș", hint: "rotunjit, culori vii", swatch: ["#fff8f1", "#ffeedd", "#c2185b", "#2a1a3a"] },
  minimal: { label: "Minimal mono", hint: "alb-negru, colțuri drepte", swatch: ["#ffffff", "#f2f2f2", "#111111", "#111111"] },
};

export function isPreset(v: unknown): v is PresetId {
  return typeof v === "string" && (PRESET_IDS as readonly string[]).includes(v);
}

/** Radio group (arrow keys move between presets, native radio behaviour). */
export function PresetPicker(props: { value: PresetId; onChange: (p: PresetId) => void; disabled?: boolean }) {
  return (
    <fieldset className="preset-picker" data-testid="preset-picker" disabled={props.disabled}>
      <legend>Stil</legend>
      {PRESET_IDS.map((id) => {
        const info = PRESET_INFO[id];
        return (
          <label key={id} className={`preset-option${props.value === id ? " on" : ""}`} title={info.hint}>
            <input
              type="radio"
              name="aas-preset"
              value={id}
              data-testid={`preset-${id}`}
              checked={props.value === id}
              onChange={() => props.onChange(id)}
            />
            <span className="preset-swatch" aria-hidden="true">
              {info.swatch.map((c, i) => (
                <span key={i} style={{ background: c }} />
              ))}
            </span>
            <span className="preset-label">{info.label}</span>
          </label>
        );
      })}
    </fieldset>
  );
}
