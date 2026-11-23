import { useEffect, useRef, useState } from "react";
import type { BrandApi, BrandKit, BrandView } from "../lib/brandApi";

const HEX = /^#[0-9a-fA-F]{6}$/;

/**
 * Brand kit of the Android app (W5): pick the launcher glyph (bundled Lucide subset), the colours, the store caption and
 * the version line; the preview sheet (adaptive circle/squircle, themed monochrome, legacy, safe zone, splash) is
 * rendered by the server. The next APK build uses the saved kit; the versionCode goes up by one per successful build.
 */
export function BrandKitPanel(props: { projectId: string; api: BrandApi }) {
  const { projectId } = props;
  const apiRef = useRef(props.api); // the parent builds a new api object per render: load once per project instead
  apiRef.current = props.api;
  const [view, setView] = useState<BrandView | null>(null);
  const [kit, setKit] = useState<BrandKit | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState("");
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    setView(null);
    setKit(null);
    apiRef.current
      .load(projectId)
      .then((v) => {
        if (!alive.current) return;
        setView(v);
        setKit(v.kit);
      })
      .catch(() => {
        if (alive.current) setMsg("The brand kit could not be loaded.");
      });
    return () => {
      alive.current = false;
    };
  }, [projectId]);

  if (!kit || !view) {
    return (
      <div className="brand-kit" data-testid="brand-kit">
        <p role="status">{msg || "Loading brand kit…"}</p>
      </div>
    );
  }

  const valid = HEX.test(kit.background) && (!kit.foreground || HEX.test(kit.foreground));
  const save = async () => {
    setBusy(true);
    setMsg("");
    try {
      const v = await apiRef.current.save(projectId, kit);
      if (!alive.current) return;
      setView(v);
      setKit(v.kit);
      setMsg(`Saved. The next APK will be ${v.nextVersionName} (code ${v.nextVersionCode}).`);
    } catch (e) {
      if (alive.current) setMsg(`Not saved: ${e instanceof Error ? e.message : "error"}`);
    } finally {
      if (alive.current) setBusy(false);
    }
  };
  const set = (patch: Partial<BrandKit>) => setKit({ ...kit, ...patch });
  const part = (v: string) => Math.max(0, Math.min(999, Number(v) || 0));

  return (
    <div className="brand-kit" data-testid="brand-kit">
      <h3>Brand kit</h3>
      <p>
        App name: <strong data-testid="brand-label">{view.appLabel}</strong> · next version{" "}
        <span data-testid="brand-version">
          {view.nextVersionName} ({view.nextVersionCode})
        </span>
      </p>
      <div role="radiogroup" aria-label="Icon glyph" data-testid="brand-glyphs" style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
        {view.glyphs.map((g) => (
          <button
            key={g.name}
            type="button"
            role="radio"
            aria-checked={kit.glyph === g.name}
            aria-label={g.name}
            title={g.name}
            className="btn"
            data-testid={`brand-glyph-${g.name}`}
            onClick={() => set({ glyph: g.name })}
            style={{ minWidth: 44, minHeight: 44, outline: kit.glyph === g.name ? "2px solid currentColor" : "none" }}
          >
            <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              {g.paths.map((d, i) => (
                <path key={i} d={d} />
              ))}
            </svg>
          </button>
        ))}
      </div>
      <label>
        Background{" "}
        <input data-testid="brand-bg" type="text" value={kit.background} maxLength={7} onChange={(e) => set({ background: e.target.value })} />
      </label>{" "}
      <label>
        Glyph colour (empty = automatic){" "}
        <input data-testid="brand-fg" type="text" value={kit.foreground ?? ""} maxLength={7} onChange={(e) => set({ foreground: e.target.value || null })} />
      </label>{" "}
      <label>
        App name (empty = project title){" "}
        <input data-testid="brand-appname" type="text" value={kit.appName} maxLength={30} onChange={(e) => set({ appName: e.target.value })} />
      </label>{" "}
      <label>
        <input data-testid="brand-storeshots" type="checkbox" checked={kit.storeScreenshots} onChange={(e) => set({ storeScreenshots: e.target.checked })} />{" "}
        Store screenshots on export
      </label>{" "}
      <label>
        Store caption{" "}
        <input data-testid="brand-caption" type="text" value={kit.caption} maxLength={60} onChange={(e) => set({ caption: e.target.value })} />
      </label>{" "}
      <label>
        Version{" "}
        <input data-testid="brand-major" type="number" min={0} max={999} value={kit.versionMajor} onChange={(e) => set({ versionMajor: part(e.target.value) })} />
        .
        <input data-testid="brand-minor" type="number" min={0} max={999} value={kit.versionMinor} onChange={(e) => set({ versionMinor: part(e.target.value) })} />
      </label>{" "}
      <button className="btn" data-testid="brand-save" disabled={busy || !valid} onClick={() => void save()}>
        Save brand kit
      </button>
      {msg ? (
        <p role="status" data-testid="brand-msg">
          {msg}
        </p>
      ) : null}
      {view.previewPng ? (
        <img
          data-testid="brand-preview"
          alt="Icon set preview: adaptive circle and squircle, themed monochrome, legacy icons, safe zone and splash"
          src={`data:image/png;base64,${view.previewPng}`}
          style={{ maxWidth: "100%" }}
        />
      ) : null}
    </div>
  );
}
