/**
 * Test-only mock backend for Playwright: REST /api/v1 + WS /api/v1/events?afterSeq=N (client offers subprotocols
 * ["aas.v1", "aas.token.<token>"], the server echoes only "aas.v1"),
 * a preview origin server (Host must be p-<id>.localhost:<port>, DESIGN 4 CSP header on every response) and a
 * canary listener that counts every request it receives.
 */
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import { WebSocketServer, type WebSocket } from "ws";

export const PORTS = { ui: 4174, api: 47875, preview: 47880, canary: 47999 };
export const TOKEN = "test_token_0123456789abcdef0123456789";
export const UI_ORIGIN = `http://127.0.0.1:${PORTS.ui}`;
export const PROJECT = "abcdef0123456789";

export const PREVIEW_CSP =
  "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
  "font-src 'self'; media-src 'self' blob:; connect-src 'self'; worker-src 'self'; manifest-src 'self'; " +
  "frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; " +
  `frame-ancestors http://tauri.localhost tauri://localhost ${UI_ORIGIN}`;

export interface Frame {
  seq: number;
  prev?: number | null;
  kind: string;
  runId?: string;
  projectId?: string;
  ts?: number;
  payload?: unknown;
}

function readJson(req: IncomingMessage): Promise<unknown> {
  return new Promise((resolve) => {
    let data = "";
    req.on("data", (c: Buffer) => {
      if (data.length < 65536) data += c.toString("utf8");
    });
    req.on("end", () => {
      try {
        resolve(data ? (JSON.parse(data) as unknown) : null);
      } catch {
        resolve(null);
      }
    });
  });
}

function listen(server: Server, port: number): Promise<void> {
  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(port, "127.0.0.1", () => resolve());
  });
}

export class MockBackend {
  approvals: unknown[] = [];
  frames: Frame[] = [];
  sockets = new Set<WebSocket>();
  afterSeqs: number[] = [];
  approvalGets = 0;
  projects: Array<{ id: string; name: string }> = [];
  /** JSON bodies of POST /runs, POST /projects, and the run ids of POST /runs/<id>/stop, plan decisions */
  runStarts: unknown[] = [];
  projectCreates: unknown[] = [];
  runStops: string[] = [];
  decisions: Array<{ url: string; body: unknown }> = [];
  /** S7: owner plan-gate setting (GET/PUT /api/v1/settings/plan-gate) */
  planGate = true;
  planGatePuts: unknown[] = [];
  rejectedUpgrades = 0;
  canaryHits: string[] = [];
  /** every API request as "<origin> <method> <url>" (origin "-" when absent), before auth */
  apiHits: string[] = [];
  /** what the WS handshake echoes: the contract value, or a broken server (token echoed / nothing) */
  wsEcho: "aas.v1" | "token" | "none" = "aas.v1";
  private api!: Server;
  private preview!: Server;
  private canary!: Server;
  private wss!: WebSocketServer;

  async start(): Promise<void> {
    this.api = createServer((req, res) => this.onApi(req, res));
    this.wss = new WebSocketServer({
      noServer: true,
      handleProtocols: () =>
        this.wsEcho === "aas.v1" ? "aas.v1" : this.wsEcho === "token" ? `aas.token.${TOKEN}` : false,
    });
    this.api.on("upgrade", (req, socket, head) => {
      const offered = String(req.headers["sec-websocket-protocol"] ?? "")
        .split(",")
        .map((s) => s.trim());
      const url = new URL(req.url ?? "/", "http://127.0.0.1");
      const ok =
        url.pathname === "/api/v1/events" &&
        req.headers.host === `127.0.0.1:${PORTS.api}` &&
        req.headers.origin === UI_ORIGIN &&
        offered.includes("aas.v1") &&
        offered.includes(`aas.token.${TOKEN}`);
      if (!ok) {
        this.rejectedUpgrades++;
        socket.destroy();
        return;
      }
      // contract (DESIGN 5.4): afterSeq is a query parameter of the upgrade URL, as in the backend's events(ws, afterSeq)
      const after = Number(url.searchParams.get("afterSeq") ?? "0");
      this.wss.handleUpgrade(req, socket, head, (ws) => this.onSocket(ws, Number.isSafeInteger(after) && after > 0 ? after : 0));
    });
    this.preview = createServer((req, res) => this.onPreview(req, res));
    this.canary = createServer((req, res) => {
      this.canaryHits.push(`${req.method} ${req.url}`);
      res.writeHead(200, { "Content-Type": "image/png", "Access-Control-Allow-Origin": "*" });
      res.end();
    });
    await listen(this.api, PORTS.api);
    await listen(this.preview, PORTS.preview);
    await listen(this.canary, PORTS.canary);
  }

  async stop(): Promise<void> {
    for (const s of this.sockets) s.terminate();
    this.wss.close();
    await Promise.all(
      [this.api, this.preview, this.canary].map(
        (s) =>
          new Promise<void>((r) => {
            s.closeAllConnections();
            s.close(() => r());
          }),
      ),
    );
  }

  lastSeq(): number {
    return this.frames.length ? this.frames[this.frames.length - 1]!.seq : 0;
  }

  /** Record frames (seq auto-assigned when 0) and send them, with `prev`, to every open socket. */
  push(...frames: Array<Omit<Frame, "seq"> & { seq?: number }>): void {
    for (const f of frames) {
      const prev = this.lastSeq();
      const full: Frame = { runId: "run-1", projectId: PROJECT, ts: Date.now(), payload: {}, ...f, seq: f.seq ?? prev + 1 };
      this.frames.push(full);
      const raw = JSON.stringify({ ...full, prev });
      for (const s of this.sockets) s.send(raw);
    }
  }

  /** Record a frame but do NOT send it live (simulates an event lost on the socket). */
  record(f: Omit<Frame, "seq">): void {
    this.frames.push({ runId: "run-1", projectId: PROJECT, ts: Date.now(), payload: {}, ...f, seq: this.lastSeq() + 1 });
  }

  /** Send a raw string to every socket without recording it (forged/invalid frames). */
  pushRaw(raw: string): void {
    for (const s of this.sockets) s.send(raw);
  }

  private onSocket(ws: WebSocket, after: number): void {
    this.sockets.add(ws);
    ws.on("close", () => this.sockets.delete(ws));
    this.afterSeqs.push(after);
    let prev = after; // the first frame's prev is afterSeq itself (backend: sent = afterSeq)
    for (const f of this.frames) {
      if (f.seq > after) {
        ws.send(JSON.stringify({ ...f, prev }));
        prev = f.seq;
      }
    }
  }

  private cors(req: IncomingMessage, res: ServerResponse): boolean {
    if (req.headers.origin === UI_ORIGIN) {
      res.setHeader("Access-Control-Allow-Origin", UI_ORIGIN);
      res.setHeader("Access-Control-Allow-Headers", "authorization, content-type");
      res.setHeader("Access-Control-Allow-Methods", "GET, POST, PUT");
    }
    if (req.method === "OPTIONS") {
      res.writeHead(204);
      res.end();
      return true;
    }
    return false;
  }

  private onApi(req: IncomingMessage, res: ServerResponse): void {
    this.apiHits.push(`${req.headers.origin ?? "-"} ${req.method} ${req.url}`);
    if (this.cors(req, res)) return;
    if (req.headers.authorization !== `Bearer ${TOKEN}`) {
      res.writeHead(401);
      res.end();
      return;
    }
    if (req.method === "GET" && req.url === "/api/v1/approvals") {
      this.approvalGets++;
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ items: this.approvals }));
      return;
    }
    if (req.method === "POST" && /^\/api\/v1\/(approvals|plans)\/[\w.:-]+\/decide$/.test(req.url ?? "")) {
      void readJson(req).then((body) => {
        this.decisions.push({ url: req.url ?? "", body });
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end("{}");
      });
      return;
    }
    if (req.url === "/api/v1/settings/plan-gate" && (req.method === "GET" || req.method === "PUT")) {
      const reply = () => {
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ enabled: this.planGate }));
      };
      if (req.method === "GET") reply();
      else
        void readJson(req).then((body) => {
          this.planGatePuts.push(body);
          if (body && typeof body === "object") this.planGate = (body as { enabled?: unknown }).enabled !== false;
          reply();
        });
      return;
    }
    if (req.method === "GET" && req.url === "/api/v1/projects") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify(this.projects));
      return;
    }
    if (req.method === "POST" && req.url === "/api/v1/projects") {
      void readJson(req).then((body) => {
        this.projectCreates.push(body);
        const id = `prj_${(this.projects.length + 1).toString(16).padStart(16, "0")}`;
        this.projects.unshift({ id, name: String((body as { name?: unknown })?.name ?? "") });
        res.writeHead(201, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ id }));
      });
      return;
    }
    if (req.method === "POST" && req.url === "/api/v1/runs") {
      void readJson(req).then((body) => {
        this.runStarts.push(body);
        res.writeHead(202, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ runId: "run_00000000000000a1", status: "running" }));
      });
      return;
    }
    const stop = /^\/api\/v1\/runs\/(run_[0-9a-f]{16})\/stop$/.exec(req.url ?? "");
    if (req.method === "POST" && stop) {
      this.runStops.push(stop[1] ?? "");
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ runId: stop[1], status: "stopping" }));
      return;
    }
    res.writeHead(404);
    res.end();
  }

  private onPreview(req: IncomingMessage, res: ServerResponse): void {
    const host = req.headers.host ?? "";
    const m = /^p-([a-z0-9]{8,32})\.localhost:(\d+)$/.exec(host);
    const headers = {
      "Content-Security-Policy": PREVIEW_CSP,
      "X-Content-Type-Options": "nosniff",
      "Cache-Control": "no-store",
      "Referrer-Policy": "no-referrer",
    };
    if (!m || m[2] !== String(PORTS.preview)) {
      res.writeHead(421, headers);
      res.end();
      return;
    }
    const path = (req.url ?? "/").split("?")[0];
    if (path === "/nocsp" || path === "/nocsp-probe.js") {
      // positive control only: same probe without the preview CSP (never served by the real preview server)
      const plain = { "X-Content-Type-Options": "nosniff", "Cache-Control": "no-store" };
      res.writeHead(200, { ...plain, "Content-Type": path === "/nocsp" ? "text/html; charset=utf-8" : "text/javascript" });
      res.end(
        path === "/nocsp"
          ? `<!doctype html><html><head><meta charset="utf-8"><title>nocsp</title></head><body><script src="/nocsp-probe.js"></script></body></html>`
          : PROBE_JS,
      );
      return;
    }
    if (path === "/") {
      res.writeHead(200, { ...headers, "Content-Type": "text/html; charset=utf-8" });
      res.end(
        `<!doctype html><html><head><meta charset="utf-8"><title>probe ${m[1]}</title></head>` +
          `<body><h1 id="h">Preview ${m[1]}</h1><script src="/probe.js"></script></body></html>`,
      );
    } else if (path === "/probe.js") {
      res.writeHead(200, { ...headers, "Content-Type": "text/javascript" });
      res.end(PROBE_JS);
    } else if (path === "/own.txt") {
      res.writeHead(200, { ...headers, "Content-Type": "text/plain" });
      res.end("own-origin-ok");
    } else {
      res.writeHead(404, headers);
      res.end();
    }
  }
}

/** P3b/P3c probe executed inside the preview frame. Results land in window.__probe. */
const PROBE_JS = [
  "(function () {",
  "  var r = { done: false };",
  "  window.__probe = r;",
  "  r.tauriGlobal = typeof window.__TAURI__;",
  "  r.tauriInternals = typeof window.__TAURI_INTERNALS__;",
  "  try { r.parentInternals = typeof window.parent.__TAURI_INTERNALS__; } catch (e) { r.parentInternals = 'blocked:' + e.name; }",
  "  try { r.parentDocument = String(!!window.parent.document); } catch (e) { r.parentDocument = 'blocked:' + e.name; }",
  "  try { window.parent.postMessage({ type: 'approval.decide', id: 'ap-forged', decision: 'approve', cmd: 'get_session' }, '*'); r.posted = true; } catch (e) { r.posted = 'error'; }",
  "  var control = location.pathname === '/nocsp';",
  `  if (!control) { try { window.top.location.href = 'http://127.0.0.1:${PORTS.canary}/top-nav'; r.topNav = 'no-throw'; } catch (e) { r.topNav = 'blocked:' + e.name; } }`,
  "  var p1 = fetch('http://ipc.localhost/get_session', { method: 'POST', body: '{}' })",
  "    .then(function () { r.ipcFetch = 'ok'; }, function (e) { r.ipcFetch = 'failed:' + e.name; });",
  "  var p2 = fetch('/own.txt').then(function (x) { return x.text(); })",
  "    .then(function (t) { r.ownFetch = t; }, function (e) { r.ownFetch = 'failed:' + e.name; });",
  // credentialed cross-origin request to the studio API (port of the mock backend); CSP connect-src 'self' must stop it
  `  var p3 = fetch('http://127.0.0.1:${PORTS.api}/api/v1/approvals?from=frame', { credentials: 'include', mode: 'no-cors' })`,
  "    .then(function () { r.apiFetch = 'sent'; }, function (e) { r.apiFetch = 'failed:' + e.name; });",
  "  Promise.all([p1, p2, p3]).then(function () { r.done = true; });",
  "})();",
].join("\n");
