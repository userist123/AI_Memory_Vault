// F2: a project created from the UI gets a title the backend accepts (prompt chars outside [A-Za-z0-9 _.-] are
// dropped, fallback "App"), and a failed request surfaces the server's sanitised error text, not a generic line.
import { afterEach, describe, expect, it, vi } from "../../../apps/desktop/testkit/unit";
import { createProject, errorText, projectTitle } from "../../../apps/desktop/src/lib/api";

const SESSION = { apiBase: "http://127.0.0.1:47875", wsUrl: "ws://127.0.0.1:47875/api/v1/events", token: "t".repeat(40), previewPort: 47880 };
const NAME_RE = /^[A-Za-z0-9][A-Za-z0-9 _.\-]{0,63}$/;

afterEach(() => {
  vi.restoreAllMocks();
});

describe("projectTitle", () => {
  it.each([
    ["Build: a todo app, with tags", "Build a todo app with tags"],
    ["  --- notes, quick  ", "notes quick"],
    ["Aplicație: listă, cumpărături", "Aplicatie lista cumparaturi"],
    ["", "App"],
    [":::,,,", "App"],
    ["🙂🙂", "App"],
    ["x".repeat(100), "x".repeat(40)],
  ])("%j -> %j", (prompt, title) => {
    const t = projectTitle(prompt);
    expect(t).toBe(title);
    expect(NAME_RE.test(t)).toBe(true);
  });

  it("is always accepted by the backend name rule", () => {
    for (const p of ["a:b", ",", "‮ evil", "<script>", "1. step: do it", "_x", ".hidden", "Ω"]) {
      expect(NAME_RE.test(projectTitle(p))).toBe(true);
    }
  });
});

describe("errorText", () => {
  it("a 422 surfaces the server message, sanitised and capped", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ error: "validation_failed", message: "invalid project name\u001b[31m‮", details: {} }),
        { status: 422, headers: { "Content-Type": "application/json" } }),
    );
    const err = await createProject(SESSION, "x").catch((e: unknown) => e);
    const text = errorText(err);
    expect(text).toBe("invalid project name (HTTP 422)");
  });

  it("a non-JSON error falls back to the status", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("<html>oops</html>", { status: 500 }));
    const err = await createProject(SESSION, "x").catch((e: unknown) => e);
    expect(errorText(err)).toBe("HTTP 500");
  });

  it("an overlong message is capped", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ message: "m".repeat(5000) }), { status: 400 }),
    );
    const err = await createProject(SESSION, "x").catch((e: unknown) => e);
    expect(errorText(err).length).toBeLessThan(400);
  });

  it("unknown values give a neutral text", () => {
    expect(errorText(undefined)).toBe("unknown error");
    expect(errorText(new Error("bad run id"))).toBe("bad run id");
  });
});
