import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
import {
  type ApprovalRecord,
  type PlanRecord,
  type ProjectRef,
  type Template,
  createProject,
  deleteProject,
  getActiveRun,
  pickerLocked,
  type ActiveRun,
  errorText,
  projectTitle,
  decideApproval,
  decidePlan,
  getPlanGate,
  getRunnerProfile,
  setRunnerProfile,
  type RunnerProfile,
  setPlanGate,
  fetchApprovals,
  listProjects,
  startRun,
  stopRun,
} from "./lib/api";
import { DEMO_PROJECT, runDemo } from "./lib/demo";
import { previewLabel } from "./lib/events";
import { LiveClient } from "./lib/live";
import { loadSession, previewOrigin, type Session } from "./lib/session";
import { createStore, DEVICES, type DeviceKind, pickShownFile, type State } from "./lib/store";
import { ApprovalRegion } from "./components/ApprovalRegion";
import { CaptureThumb } from "./components/Captures";
import { type PreviewState, StateSwitcher, withPreviewState } from "./components/StateSwitcher";
import { FlowTestsPanel } from "./components/FlowTestsPanel";
import { createCaptureLoader } from "./lib/captures";
import { DiffViewer } from "./components/DiffViewer";
import { EmulatorMirror } from "./components/EmulatorMirror";
import { ProjectPicker } from "./components/ProjectPicker";
import { ExportPanel } from "./components/ExportPanel";
import { BrandKitPanel } from "./components/BrandKitPanel";
import { brandApi } from "./lib/brandApi";
import { exportArtifact, openExportFolder, ownerBuildStatus, startOwnerBuild } from "./lib/exportApi";
import { RunControls } from "./components/RunControls";
import { ChatPanel } from "./components/ChatPanel";
import { fetchChat, sendChat } from "./lib/chatApi";
import { InspectorOverlay } from "./components/InspectorOverlay";
import { InspectorPanel } from "./components/InspectorPanel";
import {
  fetchInspectorOptions,
  inspectorCapture,
  inspectorEdit,
  type ChatAttachment,
  type EditOp,
  type InspectorSelection,
} from "./lib/inspector";
import { elapsed, Timeline } from "./components/Timeline";
import { DEFAULT_PREVIEW_THEME, ThemedPreview, ThemeSwitcher, type PreviewTheme } from "./components/ThemeSwitcher";
import type { PresetId } from "./components/PresetPicker";
import { QaBadge } from "./components/QaBadge";
import { AutoPolishButton } from "./components/AutoPolishButton";
import { fetchQa, polishStatus, runQa, startPolish } from "./lib/qaApi";

const FOLLOW_PAUSE_MS = 10_000;
const PROJECT_ID_RE = /^prj_[0-9a-f]{16}$/;
type RightTab = DeviceKind | "mirror";

function useNow(ms = 500): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), ms);
    return () => clearInterval(t);
  }, [ms]);
  return now;
}

function fmtTokens(n: number): string {
  return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : String(n);
}

function CostPill({ cost }: { cost: State["cost"] }) {
  const over = cost.budgetUsd !== null && cost.costUsd > cost.budgetUsd;
  return (
    <span className={`pill cost${over ? " over" : ""}`} data-testid="cost-pill" title="tokens in / out, cost vs run budget">
      {fmtTokens(cost.inputTokens)} in · {fmtTokens(cost.outputTokens)} out · ${cost.costUsd.toFixed(2)}
      {cost.budgetUsd !== null ? ` / $${cost.budgetUsd.toFixed(2)}` : ""}
    </span>
  );
}

async function loadMirrorFixture(): Promise<ArrayBuffer> {
  const { MIRROR_FIXTURE_B64 } = await import("./fixtures/mirrorFixture");
  const bin = atob(MIRROR_FIXTURE_B64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out.buffer;
}

export function App() {
  const params = useMemo(() => new URLSearchParams(window.location.search), []);
  const demo = params.get("demo") === "1";
  const speed = Math.min(20, Math.max(0.25, Number(params.get("speed")) || 1));
  const store = useMemo(() => createStore(), []);
  const state = useSyncExternalStore(store.subscribe, store.get);
  const now = useNow();

  const [session, setSession] = useState<Session | null>(null);
  const [sessionError, setSessionError] = useState("");
  const [approvals, setApprovals] = useState<ApprovalRecord[]>([]);
  const [plans, setPlans] = useState<PlanRecord[]>([]);
  const [planGateOn, setPlanGateOn] = useState(true);
  const [runnerProfile, setRunnerProfileState] = useState<RunnerProfile>("full");
  const [approvalError, setApprovalError] = useState("");
  const [follow, setFollow] = useState(true);
  const [pausedUntil, setPausedUntil] = useState(0);
  const [activeFile, setActiveFile] = useState<string | null>(null);
  const [previewTheme, setPreviewTheme] = useState<PreviewTheme>(DEFAULT_PREVIEW_THEME);
  const [rightTab, setRightTab] = useState<RightTab>("phone");
  const [previewState, setPreviewState] = useState<PreviewState | null>(null);
  const [buildOpen, setBuildOpen] = useState(false);
  const [buildFocused, setBuildFocused] = useState(false);
  const [mirrorStream, setMirrorStream] = useState<ArrayBuffer | null>(null);
  const [demoRun, setDemoRun] = useState(0);
  const [projects, setProjects] = useState<ProjectRef[]>([]);
  // the project open in the studio (header picker); "" = none yet -> the newest one
  const [selectedProject, setSelectedProject] = useState("");
  const [runBusy, setRunBusy] = useState(false);
  const [runError, setRunError] = useState("");
  // W4 click-to-edit inspector: one toggle for the frames, the current selection, the element attached to the chat
  const [inspectOn, setInspectOn] = useState(false);
  const [inspected, setInspected] = useState<InspectorSelection | null>(null);
  const [chatAttachment, setChatAttachment] = useState<ChatAttachment | null>(null);
  const [frameEl, setFrameEl] = useState<HTMLIFrameElement | null>(null);
  const buildRef = useRef<HTMLDivElement>(null);

  // ---- S8: capture thumbnails (authenticated fetch -> blob: URLs) and the live emulator mirror ----
  const captureLoader = useMemo(() => (session ? createCaptureLoader(session) : null), [session]);
  useEffect(() => () => captureLoader?.dispose(), [captureLoader]);
  const mirrorLive = useMemo(
    () => (!demo && session && /^prj_[0-9a-f]{16}$/.test(state.projectId) ? { session, projectId: state.projectId } : null),
    [demo, session, state.projectId],
  );

  // ---- session + live stream (or demo) ----
  useEffect(() => {
    if (demo) return;
    let client: LiveClient | null = null;
    let cancelled = false;
    loadSession()
      .then((s) => {
        if (cancelled) return;
        if (!s) {
          setSessionError("No backend session. Open the studio from the desktop app, or use ?demo=1.");
          return;
        }
        setSession(s);
        client = new LiveClient(s, store, { gapDetection: params.get("gapDetection") !== "0" });
        client.start();
      })
      .catch(() => setSessionError("The backend could not be started."));
    return () => {
      cancelled = true;
      client?.stop();
    };
  }, [demo, store, params]);

  useEffect(() => {
    if (!demo) return;
    store.dispatch({ type: "reset" });
    setApprovals([]);
    setPlans([]);
    const h = runDemo(store, setApprovals, speed, setPlans);
    void loadMirrorFixture().then(setMirrorStream);
    return () => h.stop();
  }, [demo, store, speed, demoRun]);

  // ---- approvals: refetched from server records; events only signal ----
  useEffect(() => {
    if (!session) return;
    let live = true;
    fetchApprovals(session)
      .then((r) => {
        if (!live) return;
        setApprovals(r.approvals);
        setPlans(r.plans);
        setApprovalError("");
      })
      .catch(() => live && setApprovalError("Approvals could not be loaded."));
    return () => {
      live = false;
    };
  }, [session, state.approvalsVersion]);

  // ---- projects for the prompt box ----
  useEffect(() => {
    if (!session) return;
    let live = true;
    listProjects(session)
      .then((p) => live && setProjects(p))
      .catch(() => live && setRunError("Projects could not be loaded."));
    getPlanGate(session)
      .then((on) => live && setPlanGateOn(on))
      .catch(() => live && setPlanGateOn(true)); // unknown -> shown as ON (the server default)
    getRunnerProfile(session)
      .then((p) => live && setRunnerProfileState(p))
      .catch(() => live && setRunnerProfileState("full")); // unknown -> shown as full (the server default)
    return () => {
      live = false;
    };
  }, [session]);

  // ---- the open project (header picker) ----
  const running = state.runStatus === "running";
  // the server's view of the run in progress: the picker unlocks when the event stream missed a run's end
  const [serverRun, setServerRun] = useState<ActiveRun | null>(null);
  useEffect(() => {
    if (!session) return;
    let live = true;
    setServerRun(null); // the local flag just changed: until the server answers, it decides
    const check = () =>
      getActiveRun(session)
        .then((r) => live && setServerRun(r))
        .catch(() => live && setServerRun(null));
    void check();
    const timer = running ? window.setInterval(check, 5000) : undefined;
    return () => {
      live = false;
      if (timer !== undefined) window.clearInterval(timer);
    };
  }, [session, running]);
  const locked = pickerLocked(running, serverRun);
  // the prompt the owner typed, kept per project across switches (RunControls remounts per project)
  const drafts = useRef(new Map<string, string>());
  // the header always names the project whose events the timeline shows (a live run, or the run replayed on open);
  // a switch resets the store, so its projectId is "" until that project's own events arrive
  useEffect(() => {
    if (PROJECT_ID_RE.test(state.projectId)) setSelectedProject(state.projectId);
  }, [state.projectId]);
  // nothing chosen (or the chosen one is gone): the newest project
  useEffect(() => {
    if (selectedProject && projects.some((p) => p.id === selectedProject)) return;
    if (selectedProject && projects.length === 0) return; // list not loaded yet
    setSelectedProject(projects[0]?.id ?? "");
  }, [projects, selectedProject]);
  // a finished run may have published a build: refresh the "last build" times
  useEffect(() => {
    if (!session || (state.runStatus !== "finished" && state.runStatus !== "failed")) return;
    let live = true;
    listProjects(session)
      .then((p) => live && setProjects(p))
      .catch(() => undefined);
    return () => {
      live = false;
    };
  }, [session, state.runStatus]);

  /** Switch the studio to another project: timeline, diff and frames start from that project's state. */
  const openProject = (id: string) => {
    if (!id || id === selectedProject || locked) return;
    store.dispatch({ type: "reset" });
    setActiveFile(null);
    setSelectedProject(id);
  };
  const onCreateProject = async (name: string, template: Template, preset?: PresetId) => {
    if (!session) throw new Error("no backend session");
    const id = await createProject(session, name, template, preset);
    setProjects(await listProjects(session));
    if (!running) store.dispatch({ type: "reset" });
    setActiveFile(null);
    setSelectedProject(id);
  };
  const onProjectDeleted = (id: string) => {
    if (!session) return;
    if (state.projectId === id || selectedProject === id) {
      store.dispatch({ type: "reset" });
      setActiveFile(null);
      setSelectedProject("");
    }
    void listProjects(session)
      .then(setProjects)
      .catch(() => setRunError("Projects could not be loaded."));
  };
  const selectedRef = projects.find((p) => p.id === selectedProject);

  // ---- follow the AI ----
  const followActive = follow && now >= pausedUntil;
  const focus = state.focus;
  useEffect(() => {
    if (!focus || !follow || Date.now() < pausedUntil) return;
    setBuildFocused(focus.panel === "build");
    if (focus.panel === "diff" && focus.file) setActiveFile(focus.file);
    if (focus.panel === "device" && focus.device) setRightTab(focus.device);
    if (focus.panel === "mirror") setRightTab("mirror");
    if (focus.panel === "build") buildRef.current?.scrollIntoView?.({ block: "nearest" });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focus?.seq]);

  const ownerInput = useCallback((e: { target: EventTarget | null }) => {
    if (e.target instanceof Element && e.target.closest("[data-follow-toggle]")) return;
    setPausedUntil(Date.now() + FOLLOW_PAUSE_MS);
  }, []);
  // a target that has no diff yet (e.g. package.json during deps_install) falls back to the newest file
  const shownFile = pickShownFile(activeFile, state.files, state.fileOrder);

  const previewSrc = (d: DeviceKind): string | null => {
    // no build published in this run yet (previewNonce counts preview.revision/captured): show the empty-state text
    // instead of the preview server's bare 404 page
    if (demo || !session) return null;
    void d;
    if (state.previewProjectId && state.previewNonce > 0)
      return `${previewOrigin(state.previewProjectId, session.previewPort)}/?r=${state.revision}-${state.previewNonce}`;
    // nothing built in this run: the open project's last build, if it has one
    const label = selectedRef?.lastBuildAt && !(state.projectId && state.projectId !== selectedRef.id) ? previewLabel(selectedRef.id) : "";
    return label && selectedRef?.lastBuildAt ? `${previewOrigin(label, session.previewPort)}/?b=${Date.parse(selectedRef.lastBuildAt)}` : null;
  };

  const onDecide = (id: string, d: "approve" | "deny") => {
    if (demo) {
      setApprovals((a) => a.filter((r) => r.id !== id));
      return;
    }
    if (session) void decideApproval(session, id, d).catch(() => setApprovalError("Decision failed."));
  };
  // the decision carries the content hash of the card the owner saw: the server refuses it if the plan changed (S7)
  const shownHash = (id: string): string | null => {
    const p = plans.find((x) => x.id === id);
    return p && !p.unsafe ? p.contentSha256 : null;
  };
  const onPlanApprove = (id: string) => {
    if (demo) setPlans((p) => p.filter((x) => x.id !== id));
    else if (session && shownHash(id))
      void decidePlan(session, id, shownHash(id) ?? "", "approve").catch(() => setApprovalError("Decision failed."));
  };
  const onPlanEdit = (id: string, note: string) => {
    if (demo) setPlans((p) => p.filter((x) => x.id !== id));
    else if (session && shownHash(id))
      void decidePlan(session, id, shownHash(id) ?? "", "edit", note).catch(() => setApprovalError("Decision failed."));
  };
  const onPlanGate = (enabled: boolean) => {
    if (!session) return;
    void setPlanGate(session, enabled)
      .then(setPlanGateOn)
      .catch(() => setRunError("The plan gate setting could not be changed."));
  };

  const onRunnerProfile = (p: RunnerProfile) => {
    if (!session) return;
    void setRunnerProfile(session, p)
      .then(setRunnerProfileState)
      .catch(() => setRunError("The runner profile could not be changed."));
  };

  const onStartRun = (projectId: string, prompt: string, template?: Template) => {
    if (!session || runBusy) return;
    setRunBusy(true);
    setRunError("");
    void (async () => {
      const pid = projectId || (await createProject(session, projectTitle(prompt), template));
      await startRun(session, pid, prompt);
      if (!projectId) setProjects(await listProjects(session));
      setSelectedProject(pid);
    })()
      .catch((e: unknown) => setRunError(`The run could not be started: ${errorText(e)}`))
      .finally(() => setRunBusy(false));
  };
  // ---- owner chat per project (stable callbacks: ChatPanel refetches when they change) ----
  const chatLoad = useCallback((pid: string) => {
    if (!session) return Promise.reject(new Error("no session"));
    return fetchChat(session, pid);
  }, [session]);
  const chatSend = useCallback((pid: string, message: string) => {
    if (!session) return Promise.reject(new Error("no session"));
    return sendChat(session, pid, message);
  }, [session]);

  const inspectorOptions = useCallback((pid: string) => {
    if (!session) return Promise.reject(new Error("no session"));
    return fetchInspectorOptions(session, pid);
  }, [session]);
  const inspectorSave = useCallback((pid: string, rev: number, sel: InspectorSelection, op: EditOp) => {
    if (!session) return Promise.reject(new Error("no session"));
    return inspectorEdit(session, pid, rev, sel.element, op);
  }, [session]);
  const inspectorShot = useCallback((sel: InspectorSelection) => {
    if (!session) return Promise.resolve(null);
    return inspectorCapture(session, sel.projectId, sel.device, sel.element);
  }, [session]);

  const onStopRun = () => {
    if (!session || !state.runId) return;
    void stopRun(session, state.runId).catch(() => setRunError("Stop failed."));
  };

  const runLabel =
    state.runStatus === "running"
      ? `running ${elapsed(state.startedAt, null, now)}`
      : state.runStatus === "idle"
        ? "idle"
        : `${state.runStatus} in ${elapsed(state.startedAt, state.endedAt, now)}`;
  const lastGap = state.gaps[state.gaps.length - 1];

  return (
    <div className="app" onPointerDownCapture={ownerInput} onKeyDownCapture={ownerInput}>
      <header className="topbar">
        <span className="brand">AI App Studio</span>
        {!demo && session ? (
          <ProjectPicker
            projects={projects}
            selected={selectedProject}
            disabled={locked}
            onSelect={openProject}
            onCreate={onCreateProject}
            requestDelete={(pid, apr) => deleteProject(session, pid, apr)}
            approve={(id) => decideApproval(session, id, "approve")}
            deny={(id) => decideApproval(session, id, "deny")}
            onDeleted={onProjectDeleted}
          />
        ) : null}
        <span className="run-title">{state.runTitle || (demo ? "Demo run" : "No active run")}</span>
        <span className={`status ${state.runStatus}`} data-testid="run-status">
          <span className="led" aria-hidden="true" />
          {runLabel}
        </span>
        <span className="spacer" />
        {lastGap ? (
          <span className="pill gap" data-testid="gap-indicator" title={`${state.gaps.length} gap(s) in the stream`}>
            gap {lastGap.from}–{lastGap.to} · {lastGap.reason}
          </span>
        ) : null}
        {state.protocolError !== null ? (
          <span className="pill gap" data-testid="protocol-error" title="the server must negotiate the aas.v1 subprotocol">
            refused socket: subprotocol {state.protocolError ? `"${state.protocolError}"` : "missing"}
          </span>
        ) : null}
        {state.dropped > 0 ? (
          <span className="pill gap" data-testid="dropped-indicator">
            {state.dropped} frame(s) rejected
          </span>
        ) : null}
        <CostPill cost={state.cost} />
        <span className={`conn ${state.connection}`} data-testid="connection">
          {state.connection}
        </span>
        {demo ? (
          <button className="btn small" onClick={() => setDemoRun((n) => n + 1)}>
            Replay demo
          </button>
        ) : null}
        <label
          className={`follow${followActive ? " on" : follow ? " paused" : ""}`}
          data-testid="follow-toggle"
          data-follow-toggle=""
        >
          <input
            type="checkbox"
            checked={follow}
            onChange={(e) => {
              setFollow(e.target.checked);
              setPausedUntil(0);
            }}
          />
          Follow the AI
          {follow && !followActive ? <small> paused {Math.ceil((pausedUntil - now) / 1000)}s</small> : null}
        </label>
      </header>

      <aside className="left">
        <div className="left-scroll" ref={buildRef}>
          {sessionError ? <p className="banner">{sessionError}</p> : null}
          {!demo && session ? (
            <RunControls
              key={selectedProject || "none"}
              draft={drafts.current.get(selectedProject) ?? ""}
              onDraft={(text) => drafts.current.set(selectedProject, text)}
              projects={selectedRef ? [selectedRef, ...projects.filter((p) => p.id !== selectedRef.id)] : projects}
              running={state.runStatus === "running"}
              busy={runBusy}
              error={runError}
              onStart={onStartRun}
              onStop={onStopRun}
              planGate={planGateOn}
              onPlanGate={onPlanGate}
              profile={runnerProfile}
              onProfile={onRunnerProfile}
              renderExport={(pid) => (
                <>
                <BrandKitPanel key={"brand-" + pid} projectId={pid} api={brandApi(session)} />
                <ExportPanel
                  key={pid}
                  projectId={pid}
                  api={{
                    exportArtifact: (p, t, apr) => exportArtifact(session, p, t, apr),
                    approve: (id) => decideApproval(session, id, "approve"),
                    startBuild: (p, t) => startOwnerBuild(session, p, t),
                    buildStatus: (p) => ownerBuildStatus(session, p),
                    openFolder: (p) => openExportFolder(session, p),
                  }}
                />
                </>
              )}
            />
          ) : null}
          {!demo && session ? (
            <ChatPanel
              projects={projects}
              currentProjectId={state.projectId}
              version={state.chatVersion}
              runActive={state.runStatus === "running" || runBusy}
              load={chatLoad}
              send={chatSend}
              plans={plans}
              onPlanApprove={onPlanApprove}
              onPlanEdit={onPlanEdit}
              attachment={chatAttachment}
              onClearAttachment={() => setChatAttachment(null)}
            />
          ) : null}
          {!demo && session && inspected ? (
            <InspectorPanel
              selection={inspected}
              loadOptions={inspectorOptions}
              edit={inspectorSave}
              capture={inspectorShot}
              onAttach={setChatAttachment}
              onClose={() => setInspected(null)}
            />
          ) : null}
          {state.runStatus === "failed" && state.endReason ? (
            <p className="warn banner" role="alert" data-testid="run-end-reason">
              {state.endReason}
            </p>
          ) : null}
          <Timeline
            state={state}
            now={now}
            buildOpen={buildOpen}
            setBuildOpen={setBuildOpen}
            buildFocused={buildFocused && followActive}
            onOpenFile={(p) => setActiveFile(p)}
            captureLoad={captureLoader?.load ?? null}
          />
        </div>
        <ApprovalRegion
          records={approvals}
          plans={plans}
          error={approvalError}
          onDecide={onDecide}
          onPlanApprove={onPlanApprove}
          onPlanEdit={onPlanEdit}
        />
      </aside>

      <main className="center">
        <DiffViewer files={state.files} order={state.fileOrder} active={shownFile} onSelect={setActiveFile} />
      </main>

      <section className="right" aria-label="Device preview">
        <nav className="tabs">
          {(Object.keys(DEVICES) as DeviceKind[]).map((d) => (
            <button key={d} className={`tab${rightTab === d ? " active" : ""}`} onClick={() => setRightTab(d)}>
              {DEVICES[d].label}
            </button>
          ))}
          <button className={`tab${rightTab === "mirror" ? " active" : ""}`} onClick={() => setRightTab("mirror")}>
            Emulator
          </button>
          <StateSwitcher value={previewState} onChange={setPreviewState} />
          <span className="spacer" />
          <ThemeSwitcher value={previewTheme} onChange={setPreviewTheme} />
          {!demo && session ? <QaBadge projectId={selectedProject} nonce={state.previewNonce} api={{ load: (p) => fetchQa(session, p), run: (p, a) => runQa(session, p, a) }} /> : null}
          {!demo && session ? <AutoPolishButton projectId={selectedProject} disabled={state.runStatus === "running" || runBusy} api={{ start: (p) => startPolish(session, p), status: (p) => polishStatus(session, p) }} /> : null}
          <span className="tag" data-testid="revision">
            rev {state.revision}
          </span>
        </nav>
        {(Object.keys(DEVICES) as DeviceKind[]).map((d) =>
          rightTab === d ? (
            <ThemedPreview
              theme={previewTheme}
              key={d}
              device={d}
              src={withPreviewState(previewSrc(d), previewState)}
              demoRevision={demo && state.revision > 0 ? state.revision : null}
              taps={state.taps.filter((t) => t.target === d)}
              now={now}
              frameRef={setFrameEl}
            />
          ) : null,
        )}
        {!demo && session && rightTab !== "mirror" ? (
          <InspectorOverlay
            device={rightTab}
            src={previewSrc(rightTab)}
            frame={frameEl}
            active={inspectOn}
            onToggle={setInspectOn}
            onSelect={setInspected}
          />
        ) : null}
        {(Object.keys(DEVICES) as DeviceKind[]).map((d) => {
          if (rightTab !== d) return null;
          const last = [...state.captures].reverse().find((c) => c.frame === d);
          return last ? (
            <aside key={`cap-${d}`} className="frame-capture" aria-label={`Last ${d} capture`}>
              <CaptureThumb capture={last} load={captureLoader?.load ?? null} testid="frame-capture" />
            </aside>
          ) : null;
        })}
        <FlowTestsPanel qa={state.qa} captures={state.captures} load={captureLoader?.load ?? null} />
        <div className={rightTab === "mirror" ? "mirror-wrap" : "mirror-wrap hidden"}>
          <EmulatorMirror
            stream={mirrorLive ? null : mirrorStream}
            live={mirrorLive}
            taps={state.taps.filter((t) => t.target === "mirror")}
            now={now}
          />
        </div>
        {demo ? <p className="muted foot">Demo project p-{DEMO_PROJECT}: replayed fixture, no backend.</p> : null}
      </section>
    </div>
  );
}
