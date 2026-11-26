import { useEffect, useRef, useState } from "react";
import type { BuildState, ExportTarget, PanelOutcome } from "../lib/exportApi";

export interface ExportPanelApi {
  exportArtifact: (projectId: string, target: ExportTarget, approvalId?: string) => Promise<PanelOutcome>;
  approve: (approvalId: string) => Promise<void>;
  startBuild: (projectId: string, target: "web" | "android") => Promise<BuildState>;
  buildStatus: (projectId: string) => Promise<BuildState>;
  openFolder: (projectId: string) => Promise<void>;
}

type Pending = { target: ExportTarget; approvalId: string; sha256: string; approved: boolean };
type Done = { path: string; sha256: string; bytes: number };

const LABEL: Record<ExportTarget, string> = { web: "web zip", android: "debug APK", desktop: "unsigned Windows exe" };
const BUILD_TIMEOUT_MS = 20 * 60 * 1000;

function folderOf(path: string): string {
  const i = Math.max(path.lastIndexOf("\\"), path.lastIndexOf("/"));
  return i > 0 ? path.slice(0, i) : path;
}

function size(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

/**
 * The owner's Export panel (owner report 2026-11-23 "Nu merge sa o export APK"). "Export APK": when the server answers
 * build_required (no APK, or one of an older revision), the owner's click builds it (owner build route, the agent plan
 * gate does not apply to the owner's own click), then asks again; the server answers with the exact artifact sha256;
 * the owner approves it here; the file is written to the export folder; the panel shows the path and
 * "Deschide folderul". An approval already granted from the Approvals list is not decided twice.
 */
export function ExportPanel(props: { projectId: string; api: ExportPanelApi; pollMs?: number }) {
  const { projectId, api } = props;
  const pollMs = props.pollMs ?? 1500;
  const [busy, setBusy] = useState(false);
  const [building, setBuilding] = useState("");
  const [pending, setPending] = useState<Pending | null>(null);
  const [done, setDone] = useState<Done | null>(null);
  const [msg, setMsg] = useState<string>("");
  const [folderMsg, setFolderMsg] = useState("");
  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  const waitBuild = async (): Promise<BuildState> => {
    const end = Date.now() + BUILD_TIMEOUT_MS;
    for (;;) {
      await new Promise((r) => setTimeout(r, pollMs));
      if (!alive.current) return { state: "failed", step: "", message: "closed" };
      const st = await api.buildStatus(projectId);
      if (st.state !== "building") return st;
      if (alive.current) setBuilding(st.step === "web" ? "Building the web app first…" : "Building the APK (Gradle, about a minute)…");
      if (Date.now() > end) return { state: "failed", step: "", message: "the build is taking too long" };
    }
  };

  const show = (target: ExportTarget, o: PanelOutcome) => {
    if (o.kind === "approval") setPending({ target, approvalId: o.approvalId, sha256: o.sha256, approved: o.approved });
    else if (o.kind === "done") setDone({ path: o.path, sha256: o.sha256, bytes: o.bytes });
    else if (o.kind === "blocked") setMsg(`Export blocked (secret scan): ${o.findings.join(", ")}`);
    else setMsg(`Export failed: ${o.message}`);
  };

  const start = async (target: ExportTarget) => {
    setBusy(true);
    setMsg("");
    setFolderMsg("");
    setPending(null);
    setDone(null);
    try {
      let o = await api.exportArtifact(projectId, target);
      if (o.kind === "build_required" && target === "android") {
        setBuilding("Building the APK…");
        const st0 = await api.startBuild(projectId, "android");
        const st = st0.state === "building" ? await waitBuild() : st0;
        if (!alive.current) return;
        setBuilding("");
        if (st.state !== "ok") {
          setMsg(`APK build failed: ${st.message || "unknown error"}`);
          return;
        }
        o = await api.exportArtifact(projectId, target);
      }
      if (alive.current) show(target, o);
    } catch {
      if (alive.current) setMsg("Export failed.");
    } finally {
      if (alive.current) {
        setBuilding("");
        setBusy(false);
      }
    }
  };

  const confirm = async () => {
    if (!pending) return;
    const p = pending;
    setBusy(true);
    setPending(null);
    try {
      if (!p.approved) await api.approve(p.approvalId);
      const o = await api.exportArtifact(projectId, p.target, p.approvalId);
      if (alive.current) show(p.target, o);
    } catch {
      if (alive.current) setMsg("Export failed.");
    } finally {
      if (alive.current) setBusy(false);
    }
  };

  const openFolder = () => {
    setFolderMsg("");
    void api.openFolder(projectId).catch(() => {
      if (alive.current) setFolderMsg("The folder could not be opened: copy the path above.");
    });
  };

  return (
    <div className="export-controls" data-testid="export-panel">
      <button className="btn" data-testid="export-web" disabled={busy || !projectId} onClick={() => void start("web")}>
        Export web (zip)
      </button>
      <button className="btn" data-testid="export-android" disabled={busy || !projectId} onClick={() => void start("android")}>
        Export APK
      </button>
      <button className="btn" data-testid="export-desktop" disabled={busy || !projectId} onClick={() => void start("desktop")}>
        Export desktop (exe)
      </button>
      {building ? (
        <p role="status" data-testid="export-building">
          {building}
        </p>
      ) : null}
      {pending ? (
        <div className="export-confirm" role="dialog" aria-label="Approve export" data-testid="export-confirm">
          <p>
            Export the {LABEL[pending.target]} with sha256 <code data-testid="export-sha">{pending.sha256}</code>?
          </p>
          <button className="btn primary" data-testid="export-approve" onClick={() => void confirm()}>
            {pending.approved ? "Export (already approved)" : "Approve and export"}
          </button>
          <button className="btn" data-testid="export-cancel" onClick={() => setPending(null)}>
            Cancel
          </button>
        </div>
      ) : null}
      {done ? (
        <div className="ok" role="status" data-testid="export-result">
          <p>
            Exported ({size(done.bytes)}) to:
          </p>
          <input readOnly aria-label="Export path" data-testid="export-path" value={done.path} onFocus={(e) => e.currentTarget.select()} />
          <p>
            Folder: <code>{folderOf(done.path)}</code>
          </p>
          <button className="btn" data-testid="export-open-folder" onClick={openFolder}>
            Deschide folderul
          </button>
          {folderMsg ? <p className="warn">{folderMsg}</p> : null}
        </div>
      ) : null}
      {msg ? (
        <p className="warn" role="status" data-testid="export-error">
          {msg}
        </p>
      ) : null}
    </div>
  );
}
