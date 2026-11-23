import { useEffect, useState } from "react";
import {
  attachmentContext,
  attachmentLabel,
  MAX_TEXT,
  type ChatAttachment,
  type EditOp,
  type EditPreview,
  type EditResult,
  type InspectorOptions,
  type InspectorSelection,
} from "../lib/inspector";

const BOX = { display: "flex", flexDirection: "column", gap: 6 } as const;

function family(cls: string, opts: InspectorOptions | null): string[] {
  if (!opts) return [];
  // utility prefixes (text, bg, p, gap, ...) have no dash; token names may (surface-2, on-primary-soft)
  const prefix = cls.slice(0, cls.indexOf("-"));
  for (const list of [opts.families.color, opts.families.spacing]) {
    if (list.includes(cls)) return list.filter((c) => c.slice(0, c.indexOf("-")) === prefix);
  }
  return [];
}

function errorText(e: unknown): string {
  const status = (e as { status?: unknown })?.status;
  const msg = String((e as { serverMessage?: unknown })?.serverMessage ?? "");
  if (status === 404) return "Textul sau clasa nu e scrisă direct în cod (e calculată). Folosește „Trimite în chat”.";
  if (status === 409 && msg.startsWith("inspector_ambiguous"))
    return "Apare de mai multe ori în cod. Folosește „Trimite în chat” ca să spui care.";
  if (status === 409 && msg.startsWith("run_in_progress")) return "AI-ul lucrează acum. Încearcă după ce termină.";
  if (status === 409) return "Aplicația s-a schimbat între timp. Selectează din nou elementul.";
  if (status === 422) return "Modificarea nu e permisă (doar text simplu sau o clasă din lista permisă).";
  return "Modificarea nu a putut fi salvată.";
}

/**
 * W4: what the owner can do with the selected preview element - edit its visible text, swap a colour token or spacing
 * class from the server's allowed list, or attach it (descriptor + cropped capture) to the next chat message.
 * Every descriptor field is shown as a text node.
 */
export function InspectorPanel(props: {
  selection: InspectorSelection;
  loadOptions: (projectId: string) => Promise<InspectorOptions>;
  /** step 1: dry run, names the file:line the edit would change (nothing is written) */
  preview: (projectId: string, baseRevision: number, sel: InspectorSelection, op: EditOp) => Promise<EditPreview>;
  /** step 2: only after the owner confirmed the shown target */
  edit: (projectId: string, baseRevision: number, sel: InspectorSelection, op: EditOp) => Promise<EditResult>;
  capture: (sel: InspectorSelection) => Promise<string | null>;
  onAttach: (a: ChatAttachment) => void;
  onClose: () => void;
}) {
  const { selection, loadOptions } = props;
  const el = selection.element;
  const [opts, setOpts] = useState<InspectorOptions | null>(null);
  const [text, setText] = useState(el.text);
  const [swaps, setSwaps] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [pending, setPending] = useState<{ op: EditOp; target: EditPreview } | null>(null);

  useEffect(() => {
    let live = true;
    setText(el.text);
    setSwaps({});
    setPending(null);
    setStatus("");
    setError("");
    loadOptions(selection.projectId)
      .then((o) => live && setOpts(o))
      .catch(() => live && setOpts(null));
    return () => {
      live = false;
    };
  }, [selection, el.text, loadOptions]);

  const ask = (op: EditOp) => {
    if (busy || !opts) return;
    setBusy(true);
    setError("");
    setStatus("");
    setPending(null);
    props
      .preview(selection.projectId, opts.revision, selection, op)
      .then((target) => setPending({ op, target }))
      .catch((e: unknown) => setError(errorText(e)))
      .finally(() => setBusy(false));
  };

  const run = (op: EditOp) => {
    if (busy || !opts) return;
    setBusy(true);
    setError("");
    setStatus("");
    setPending(null);
    props
      .edit(selection.projectId, opts.revision, selection, op)
      .then((r) => {
        setOpts({ ...opts, revision: r.revision });
        setStatus(
          r.rebuild === "busy"
            ? `Salvat în ${r.path} (rev ${r.revision}). Reconstruiește când se termină lucrul curent.`
            : `Salvat în ${r.path} (rev ${r.revision}). Se reconstruiește previzualizarea…`,
        );
      })
      .catch((e: unknown) => setError(errorText(e)))
      .finally(() => setBusy(false));
  };

  const attach = () => {
    if (busy) return;
    setBusy(true);
    setError("");
    props
      .capture(selection)
      .catch(() => null)
      .then((sha) => {
        props.onAttach({
          projectId: selection.projectId,
          label: attachmentLabel(el),
          context: attachmentContext(selection, sha),
          captureSha: sha,
        });
        setStatus(sha ? "Atașat la mesajul următor, cu captura elementului." : "Atașat la mesajul următor (fără captură).");
      })
      .finally(() => setBusy(false));
  };

  const swappable = el.classes.filter((c) => family(c, opts).length > 1);
  const textEditable = el.text.trim().length > 0 && el.text.length < MAX_TEXT;

  return (
    <section className="run-controls inspector-panel" data-testid="inspector-panel" aria-label="Element selectat" style={BOX}>
      <header className="row">
        <span className="eyebrow">Element selectat</span>
        <code data-testid="inspector-tag">{`<${el.tag}>`}</code>
        <small className="muted" data-testid="inspector-path">
          {el.path}
        </small>
        <span className="spacer" />
        <button className="btn" data-testid="inspector-close" onClick={props.onClose} aria-label="Închide">
          ×
        </button>
      </header>
      {textEditable ? (
        <div className="row">
          <input
            data-testid="inspector-text"
            value={text}
            maxLength={MAX_TEXT}
            onChange={(e) => setText(e.target.value)}
            aria-label="Textul elementului"
          />
          <button
            className="btn primary"
            data-testid="inspector-save-text"
            disabled={busy || !opts || !text.trim() || text === el.text}
            onClick={() => ask({ op: "text", to: text })}
          >
            Salvează textul
          </button>
        </div>
      ) : (
        <p className="muted" data-testid="inspector-no-text">
          {el.text ? "Text prea lung pentru editare directă." : "Elementul nu are text propriu."}
        </p>
      )}
      {swappable.map((c) => {
        const alts = family(c, opts);
        const pick = swaps[c] ?? c;
        return (
          <div className="row" key={c} data-testid="inspector-swap">
            <code>{c}</code>
            <select
              data-testid={`inspector-swap-${c}`}
              value={pick}
              onChange={(e) => setSwaps({ ...swaps, [c]: e.target.value })}
              aria-label={`Înlocuiește ${c}`}
            >
              {alts.map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </select>
            <button
              className="btn"
              data-testid={`inspector-apply-${c}`}
              disabled={busy || pick === c}
              onClick={() => ask({ op: "class", from: c, to: pick })}
            >
              Aplică
            </button>
          </div>
        );
      })}
      {pending ? (
        <div className="row" data-testid="inspector-confirm-box">
          <span data-testid="inspector-target">{`Va edita ${pending.target.path}:${pending.target.line}`}</span>
          <button className="btn primary" data-testid="inspector-confirm" disabled={busy} onClick={() => run(pending.op)}>
            Confirmă
          </button>
          <button className="btn" data-testid="inspector-cancel" disabled={busy} onClick={() => setPending(null)}>
            Anulează
          </button>
        </div>
      ) : null}
      <div className="row">
        <button className="btn" data-testid="inspector-attach" disabled={busy} onClick={attach}>
          Trimite în chat
        </button>
      </div>
      {status ? (
        <p className="note" data-testid="inspector-status">
          {status}
        </p>
      ) : null}
      {error ? (
        <p className="warn" role="alert" data-testid="inspector-error">
          {error}
        </p>
      ) : null}
    </section>
  );
}
