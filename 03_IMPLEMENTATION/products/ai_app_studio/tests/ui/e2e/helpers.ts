import type { Page } from "../../../apps/desktop/testkit/pw";
import { PORTS, TOKEN } from "../../../apps/desktop/testkit/pw";

const SESSION = { apiBase: `http://127.0.0.1:${PORTS.api}`, token: TOKEN, previewPort: PORTS.preview };

/** Web build: inject the dev session into the top window only (never into preview frames). */
export async function injectDevSession(page: Page): Promise<void> {
  await page.addInitScript((s) => {
    if (window === window.top) (window as unknown as { __AAS_DEV_SESSION__: unknown }).__AAS_DEV_SESSION__ = s;
  }, SESSION);
}

/**
 * Stand-in for the Tauri main window: `__TAURI_INTERNALS__.invoke('get_session')` returns the session.
 * Installed in the top window only, as Tauri does for the main webview.
 */
export async function injectTauriStub(page: Page): Promise<void> {
  await page.addInitScript((s) => {
    if (window !== window.top) return;
    let cb = 0;
    (window as unknown as { __TAURI_INTERNALS__: unknown }).__TAURI_INTERNALS__ = {
      invoke: (cmd: string) => (cmd === "get_session" ? Promise.resolve(s) : Promise.reject(new Error("denied"))),
      transformCallback: () => ++cb,
      metadata: { currentWindow: { label: "main" }, currentWebview: { label: "main" } },
    };
  }, SESSION);
}

export async function waitOpen(page: Page): Promise<void> {
  await page.getByTestId("connection").filter({ hasText: "open" }).waitFor({ timeout: 15_000 });
}
