import { useState } from "react";
import { errorText, TEMPLATES, type DeleteOutcome, type ProjectRef, type Template } from "../lib/api";

/** The backend's project name rule (studio_core.services._NAME_RE). */
const NAME_RE = /^[A-Za-z0-9][A-Za-z0-9 _.-]{0,63}$/;

const TEMPLATE_LABEL: Record<Template, string> = {
  blank: "Gol",
  tabs: "Cu file (tabs)",
  "list-detail": "Listă și detalii",
};

/** "20.11.2026, 11:30" in the owner's local time; "" for anything unparsable. */
export function buildTimeText(iso: string): string {
  const t = Date.parse(iso);
  if (Number.isNaN(t)) return "";
  const d = new Date(t);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()}, ${p(d.getHours())}:${p(d.getMinutes())}`;
}

function optionText(p: ProjectRef): string {
  const when = p.lastBuildAt ? buildTimeText(p.lastBuildAt) : "";
  return `${p.name || p.id} · ${when ? `construit ${when}` : "neconstruit încă"}`;
}

/**
 * The owner's project picker in the header: switch project, "Proiect nou" (name + template), delete. Delete is the
 * server's two-step owner flow (like export): the first call records an approval request, the owner confirms here,
 * the approval is decided and the call repeated with its id. Owner actions in the studio window only.
 */
export function ProjectPicker(props: {
  projects: ProjectRef[];
  selected: string;
  /** a run is going: no switching, no deleting */
  disabled: boolean;
  onSelect: (projectId: string) => void;
  onCreate: (name: string, template: Template) => Promise<void>;
  requestDelete: (projectId: string, approvalId?: string) => Promise<DeleteOutcome>;
  approve: (approvalId: string) => Promise<void>;
  deny: (approvalId: string) => Promise<void>;
  onDeleted: (projectId: string) => void;
}) {
  const { projects, selected, disabled } = props;
  const [formOpen, setFormOpen] = useState(false);
  const [name, setName] = useState("");
  const [template, setTemplate] = useState<Template>("blank");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [pendingDelete, setPendingDelete] = useState<{ projectId: string; approvalId: string } | null>(null);

  const current = projects.find((p) => p.id === selected);
  const nameOk = NAME_RE.test(name.trim());

  const create = () => {
    if (!nameOk || busy) return;
    setBusy(true);
    setError("");
    void props
      .onCreate(name.trim(), template)
      .then(() => {
        setFormOpen(false);
        setName("");
        setTemplate("blank");
      })
      .catch((e: unknown) => setError(`Proiectul nu a putut fi creat: ${errorText(e)}`))
      .finally(() => setBusy(false));
  };

  const fail = (o: DeleteOutcome) => o.kind === "error" && setError(`Proiectul nu a putut fi șters: ${o.message}`);

  const startDelete = () => {
    if (!current || busy || disabled) return;
    const pid = current.id;
    setBusy(true);
    setError("");
    void props
      .requestDelete(pid)
      .then((o) => {
        if (o.kind === "approval") setPendingDelete({ projectId: pid, approvalId: o.approvalId });
        else if (o.kind === "done") props.onDeleted(pid);
        else fail(o);
      })
      .catch((e: unknown) => setError(`Proiectul nu a putut fi șters: ${errorText(e)}`))
      .finally(() => setBusy(false));
  };

  const confirmDelete = () => {
    if (!pendingDelete) return;
    const p = pendingDelete;
    setPendingDelete(null);
    setBusy(true);
    void props
      .approve(p.approvalId)
      .then(() => props.requestDelete(p.projectId, p.approvalId))
      .then((o) => (o.kind === "done" ? props.onDeleted(p.projectId) : fail(o.kind === "approval" ? { kind: "error", message: "aprobarea nu a fost primită" } : o)))
      .catch((e: unknown) => setError(`Proiectul nu a putut fi șters: ${errorText(e)}`))
      .finally(() => setBusy(false));
  };

  const cancelDelete = () => {
    if (!pendingDelete) return;
    const id = pendingDelete.approvalId;
    setPendingDelete(null);
    void props.deny(id).catch(() => undefined); // best effort: otherwise it stays in the approvals list for the owner
  };

  const pendingName = pendingDelete ? projects.find((p) => p.id === pendingDelete.projectId)?.name || pendingDelete.projectId : "";

  const errorLine = (
    <p className="warn" role="alert" data-testid="project-error">
      {error}
    </p>
  );

  return (
    <div className="project-picker" data-testid="project-picker-area">
      <label className="project-label">
        <span>Proiect</span>
        <select
          data-testid="project-picker"
          aria-label="Proiectul deschis"
          value={current ? current.id : ""}
          disabled={disabled || busy || projects.length === 0}
          title={disabled ? "Oprește rularea înainte de a schimba proiectul" : undefined}
          onChange={(e) => props.onSelect(e.target.value)}
        >
          {current ? null : <option value="">niciun proiect</option>}
          {projects.map((p) => (
            <option key={p.id} value={p.id}>
              {optionText(p)}
            </option>
          ))}
        </select>
      </label>
      <button className="btn small" data-testid="project-new" disabled={busy} onClick={() => setFormOpen((o) => !o)}>
        Proiect nou
      </button>
      {current ? (
        <button
          className="btn small danger"
          data-testid="project-delete"
          disabled={disabled || busy || pendingDelete !== null}
          title="Șterge proiectul deschis (cere aprobarea ta)"
          onClick={startDelete}
        >
          Șterge
        </button>
      ) : null}
      {formOpen ? (
        <form
          className="project-form"
          data-testid="project-form"
          aria-label="Proiect nou"
          onSubmit={(e) => {
            e.preventDefault();
            create();
          }}
        >
          <label>
            Nume
            <input
              data-testid="project-form-name"
              value={name}
              maxLength={64}
              autoFocus
              placeholder="ex. Jurnal zilnic"
              onChange={(e) => setName(e.target.value)}
            />
          </label>
          <label>
            Șablon
            <select
              data-testid="project-form-template"
              value={template}
              onChange={(e) => setTemplate((TEMPLATES as readonly string[]).includes(e.target.value) ? (e.target.value as Template) : "blank")}
            >
              {TEMPLATES.map((t) => (
                <option key={t} value={t}>
                  {TEMPLATE_LABEL[t]}
                </option>
              ))}
            </select>
          </label>
          {name && !nameOk ? <small className="note">Litere fără diacritice, cifre, spațiu, _ . - (max. 64)</small> : null}
          <button type="submit" className="btn primary small" data-testid="project-form-create" disabled={!nameOk || busy}>
            Creează
          </button>
          <button type="button" className="btn small" data-testid="project-form-cancel" onClick={() => setFormOpen(false)}>
            Renunță
          </button>
          {error ? errorLine : null}
        </form>
      ) : null}
      {pendingDelete ? (
        <div className="project-delete-confirm" role="dialog" aria-label="Confirmă ștergerea" data-testid="project-delete-confirm">
          <p>
            Ștergi proiectul „{pendingName}”? Fișierele lui merg în coș și se șterg definitiv după perioada de păstrare.
          </p>
          <button className="btn primary small danger" data-testid="project-delete-approve" onClick={confirmDelete}>
            Aprobă și șterge
          </button>
          <button className="btn small" data-testid="project-delete-cancel" onClick={cancelDelete}>
            Renunță
          </button>
        </div>
      ) : null}
      {error && !formOpen ? errorLine : null}
    </div>
  );
}
