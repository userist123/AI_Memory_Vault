import { useCallback, useEffect, useRef, useState, type CSSProperties } from "react";
import { checklistToText, type BaselineState, type ChangesApi, type CheckRow, type Flow } from "../lib/baselinesApi";

const FRAME_LABEL = { phone: "Telefon", tablet: "Tabletă", desktop: "Desktop" } as const;
type Mode = "side" | "overlay" | "diff";

function BlobImg(props: { projectId: string; sha256: string; api: ChangesApi; alt: string; style?: CSSProperties; testid?: string }) {
  const { projectId, sha256, api } = props;
  const [url, setUrl] = useState("");
  useEffect(() => {
    let live = true;
    let made = "";
    setUrl("");
    if (!sha256) return;
    api
      .image(projectId, sha256)
      .then((u) => {
        made = u;
        if (live && u.startsWith("blob:")) setUrl(u);
        else URL.revokeObjectURL(u);
      })
      .catch(() => undefined);
    return () => {
      live = false;
      if (made) URL.revokeObjectURL(made);
    };
  }, [projectId, sha256, api]);
  if (!url) return <div className="capture-empty">{sha256 ? "se încarcă…" : "fără imagine"}</div>;
  return <img src={url} alt={props.alt} referrerPolicy="no-referrer" decoding="async" style={props.style} data-testid={props.testid} />;
}

function Compare(props: { projectId: string; row: CheckRow; api: ChangesApi }) {
  const { projectId, row, api } = props;
  const [mode, setMode] = useState<Mode>(row.diffSha256 ? "diff" : "side");
  const [opacity, setOpacity] = useState(50);
  const label = `${row.route} · ${FRAME_LABEL[row.frame]}`;
  return (
    <div className="changes-compare" data-testid="changes-compare">
      <div role="tablist" aria-label="Mod de comparare">
        {(["side", "overlay", "diff"] as Mode[]).map((m) => (
          <button key={m} role="tab" aria-selected={mode === m} data-testid={`changes-mode-${m}`} onClick={() => setMode(m)}
            disabled={m === "diff" && !row.diffSha256}>
            {m === "side" ? "Alăturat" : m === "overlay" ? "Suprapus" : "Diferențe"}
          </button>
        ))}
      </div>
      {mode === "side" ? (
        <div className="capture-row">
          <figure className={`capture ${row.frame}`}>
            <BlobImg projectId={projectId} sha256={row.baselineSha256} api={api} alt={`${label} aprobat`} testid="changes-baseline-img" />
            <figcaption>Aprobat</figcaption>
          </figure>
          <figure className={`capture ${row.frame}`}>
            <BlobImg projectId={projectId} sha256={row.currentSha256} api={api} alt={`${label} acum`} testid="changes-current-img" />
            <figcaption>Acum</figcaption>
          </figure>
        </div>
      ) : mode === "overlay" ? (
        <figure className={`capture ${row.frame}`} style={{ position: "relative" }}>
          <BlobImg projectId={projectId} sha256={row.baselineSha256} api={api} alt={`${label} aprobat`} />
          <BlobImg projectId={projectId} sha256={row.currentSha256} api={api} alt={`${label} acum`}
            style={{ position: "absolute", left: 0, top: 0, opacity: opacity / 100 }} />
          <figcaption>
            <label>
              Aprobat ↔ acum{" "}
              <input type="range" min={0} max={100} value={opacity} onChange={(e) => setOpacity(Number(e.target.value))}
                data-testid="changes-opacity" aria-label="Transparența capturii noi" />
            </label>
          </figcaption>
        </figure>
      ) : (
        <figure className={`capture ${row.frame}`}>
          <BlobImg projectId={projectId} sha256={row.diffSha256} api={api} alt={`${label} diferențe`} testid="changes-diff-img" />
          <figcaption>Roșu = schimbat, albastru = zone volatile ignorate</figcaption>
        </figure>
      )}
    </div>
  );
}

function Checklist(props: { projectId: string; api: ChangesApi }) {
  const { projectId, api } = props;
  const [flows, setFlows] = useState<Flow[]>([]);
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    api.checklist(projectId).then((f) => live && setFlows(f)).catch(() => undefined);
    return () => {
      live = false;
    };
  }, [projectId, api]);
  const save = async () => {
    setError("");
    try {
      setFlows(await api.saveChecklist(projectId, text));
      setEditing(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : "nu s-a putut salva");
    }
  };
  const badge = { pass: "✓ trecut", fail: "✗ picat", skip: "– sărit", unknown: "? netestat" } as const;
  return (
    <section className="checklist" data-testid="acceptance-checklist" aria-label="Listă de acceptanță">
      <span className="eyebrow">Listă de acceptanță</span>
      {editing ? (
        <>
          <textarea value={text} onChange={(e) => setText(e.target.value)} rows={8} maxLength={20000}
            data-testid="checklist-text" aria-label="Fluxuri (# titlu) și afirmații (- text)" />
          <button onClick={() => void save()} data-testid="checklist-save">Salvează</button>
          <button onClick={() => setEditing(false)}>Renunță</button>
        </>
      ) : (
        <>
          {flows.length ? (
            <ul>
              {flows.map((f) => (
                <li key={f.id} data-testid={`flow-${f.id}`} data-status={f.status}>
                  <strong>{f.title}</strong> <span className={`flow-${f.status}`}>{badge[f.status]}</span>
                  {f.message ? <span> — {f.message}</span> : null}
                  <ul>
                    {f.statements.map((s, i) => (
                      <li key={i}>{s}</li>
                    ))}
                  </ul>
                </li>
              ))}
            </ul>
          ) : (
            <p className="muted">Nicio afirmație încă.</p>
          )}
          <button onClick={() => (setText(checklistToText(flows) || "# Flux principal\n- "), setEditing(true))} data-testid="checklist-edit">
            Editează lista
          </button>
        </>
      )}
      {error ? <p className="warn" role="alert">{error}</p> : null}
    </section>
  );
}

/**
 * "Ce s-a schimbat" (WOW W6): the owner approves the current screens ("Aprobă ecranul"); after each finished run
 * (chat edit) the panel re-checks automatically and lists every screen with its changed %; a selected screen is shown
 * side by side, as an overlay with a slider, or as the server's diff image. Warnings are the same the agent gets.
 */
export function ChangesPanel(props: { projectId: string; api: ChangesApi; runStatus?: string }) {
  const { projectId, api, runStatus } = props;
  const [state, setState] = useState<BaselineState | null>(null);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");
  const [selected, setSelected] = useState("");
  const prevRun = useRef(runStatus);

  const act = useCallback(
    async (what: "approve" | "check" | "status") => {
      setBusy(what);
      setError("");
      try {
        const st = await api[what](projectId);
        setState(st);
        const changed = st.check?.rows.find((r) => r.status === "changed");
        setSelected((cur) => (st.check?.rows.some((r) => r.key === cur) ? cur : (changed?.key ?? "")));
      } catch (e) {
        setError(e instanceof Error ? e.message : "eroare");
      } finally {
        setBusy("");
      }
    },
    [api, projectId],
  );

  useEffect(() => {
    void act("status");
  }, [act]);

  useEffect(() => {
    const was = prevRun.current;
    prevRun.current = runStatus;
    if (was === "running" && runStatus === "finished" && state?.baselines.length) void act("check");
  }, [runStatus, state, act]);

  const rows = state?.check?.rows ?? [];
  const row = rows.find((r) => r.key === selected);
  const changed = rows.filter((r) => r.status === "changed").length;
  return (
    <section className="changes-panel" data-testid="changes-panel" aria-label="Ce s-a schimbat">
      <span className="eyebrow">Ce s-a schimbat</span>
      <div className="row">
        <button onClick={() => void act("approve")} disabled={!!busy} data-testid="baseline-approve"
          title="Ecranele de acum devin referința; după fiecare modificare sunt comparate cu ea">
          {busy === "approve" ? "Se capturează…" : "Aprobă ecranul"}
        </button>
        <button onClick={() => void act("check")} disabled={!!busy || !state?.baselines.length} data-testid="baseline-check">
          {busy === "check" ? "Se compară…" : "Verifică acum"}
        </button>
      </div>
      {error ? <p className="warn" role="alert" data-testid="changes-error">{error}</p> : null}
      {state && !state.baselines.length ? (
        <p className="muted" data-testid="changes-empty">Niciun ecran aprobat. Apasă „Aprobă ecranul” când îți place cum arată.</p>
      ) : null}
      {state?.baselines.length && !state.check ? (
        <p className="muted" data-testid="changes-approved">{state.baselines.length} capturi aprobate. Se compară după fiecare modificare.</p>
      ) : null}
      {state?.check ? (
        <>
          <p data-testid="changes-summary">
            {changed ? `${changed} ecran(e) schimbate peste ${state.thresholdPct}%` : "Nimic schimbat față de ecranele aprobate"} · rev {state.check.revision}
          </p>
          {state.check.warnings.length ? (
            <ul className="warn" role="alert" data-testid="changes-warnings">
              {state.check.warnings.map((w, i) => (
                <li key={i}>{w}</li>
              ))}
            </ul>
          ) : null}
          <ul className="changes-list">
            {rows.map((r) => (
              <li key={r.key}>
                <button aria-pressed={r.key === selected} onClick={() => setSelected(r.key)} data-testid="changes-row" data-status={r.status}>
                  {r.route} · {FRAME_LABEL[r.frame]} — {r.status === "changed" ? `schimbat ${r.changedPct}%` : r.status === "same" ? "la fel" : "referință lipsă"}
                </button>
              </li>
            ))}
          </ul>
          {row ? <Compare key={row.key + row.currentSha256} projectId={projectId} row={row} api={api} /> : null}
        </>
      ) : null}
      <Checklist projectId={projectId} api={api} />
    </section>
  );
}
