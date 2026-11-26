/**
 * Session = where the backend is and the owner token.
 * Tauri: the only IPC command, `get_session`, from the shell. Web build (tests, `vite preview`): an explicitly
 * injected `window.__AAS_DEV_SESSION__`, honoured only when not running inside Tauri.
 */
export interface Session {
  apiBase: string;
  wsUrl: string;
  token: string;
  previewPort: number;
}

export const API_PORT = 17875;
export const PREVIEW_PORT = 17880;
const API_BASE_RE = /^http:\/\/127\.0\.0\.1:(\d{2,5})$/;
const TOKEN_RE = /^[A-Za-z0-9_-]{16,256}$/;

declare global {
  interface Window {
    __AAS_DEV_SESSION__?: { apiBase?: unknown; token?: unknown; previewPort?: unknown };
    __TAURI_INTERNALS__?: unknown;
  }
}

export function isTauri(): boolean {
  return typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
}

export function makeSession(apiBase: unknown, token: unknown, previewPort: unknown = PREVIEW_PORT): Session | null {
  if (typeof apiBase !== "string" || typeof token !== "string") return null;
  if (!API_BASE_RE.test(apiBase) || !TOKEN_RE.test(token)) return null;
  const pp = typeof previewPort === "number" && Number.isInteger(previewPort) && previewPort > 1023 && previewPort < 65536
    ? previewPort
    : PREVIEW_PORT;
  return { apiBase, wsUrl: `${apiBase.replace(/^http/, "ws")}/api/v1/events`, token, previewPort: pp };
}

export async function loadSession(): Promise<Session | null> {
  if (isTauri()) {
    const { invoke } = await import("@tauri-apps/api/core");
    const s = await invoke<{ apiBase: string; token: string }>("get_session");
    // previewPort is only returned by test shells; the real shell omits it (default 17880)
    return makeSession(s.apiBase, s.token, (s as { previewPort?: unknown }).previewPort);
  }
  const dev = window.__AAS_DEV_SESSION__;
  if (dev) return makeSession(dev.apiBase, dev.token, dev.previewPort);
  return null;
}

export function previewOrigin(projectId: string, port: number): string {
  return `http://p-${projectId}.localhost:${port}`;
}
