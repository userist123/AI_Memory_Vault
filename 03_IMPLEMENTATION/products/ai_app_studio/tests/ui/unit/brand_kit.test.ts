import { createElement, describe, expect, fireEvent, it, render, vi, afterEach, cleanup } from "../../../apps/desktop/testkit/unit";
import { BrandKitPanel } from "../../../apps/desktop/src/components/BrandKitPanel";
import { glyphPaths, parseBrand } from "../../../apps/desktop/src/lib/brandApi";
import type { BrandApi } from "../../../apps/desktop/src/lib/brandApi";

afterEach(cleanup);

const good = {
  kit: { glyph: "star", background: "#2563EB", foreground: null, caption: "Hi", versionMajor: 1, versionMinor: 2 },
  colours: { background: "#2563EB", foreground: "#FFFFFF" },
  appLabel: "Demo",
  nextVersionCode: 3,
  nextVersionName: "1.2.2",
  glyphs: [{ name: "star", svg: '<svg><path d="M12 2l3 7" key="x"/></svg>' }],
  previewPng: "AAAA",
};

describe("brand api parsing", () => {
  it("accepts a well-formed view", () => {
    const v = parseBrand(good);
    expect(v?.kit.glyph).toBe("star");
    expect(v?.glyphs[0]?.paths).toEqual(["M12 2l3 7"]);
    expect(v?.nextVersionCode).toBe(3);
  });
  it("refuses a bad glyph name or colour", () => {
    expect(parseBrand({ ...good, kit: { ...good.kit, glyph: "../x" } })).toBeNull();
    expect(parseBrand({ ...good, colours: { background: "red", foreground: "#FFFFFF" } })).toBeNull();
  });
  it("never returns markup from a glyph and drops a non-base64 preview", () => {
    expect(glyphPaths('<svg><script>alert(1)</script><path d="M0 0"/></svg>')).toEqual(["M0 0"]);
    expect(parseBrand({ ...good, previewPng: "<img onerror=1>" })?.previewPng).toBe("");
  });
});

describe("BrandKitPanel", () => {
  it("loads the kit, saves a changed glyph and shows the next version", async () => {
    const view = parseBrand(good)!;
    const save = vi.fn(async (_p: string, kit: { glyph: string }) => ({ ...view, kit: { ...view.kit, glyph: kit.glyph }, nextVersionCode: 4, nextVersionName: "1.2.3" }));
    const api: BrandApi = { load: async () => view, save };
    const two = { ...good, glyphs: [...good.glyphs, { name: "heart", svg: '<svg><path d="M1 1"/></svg>' }] };
    const v2 = parseBrand(two)!;
    api.load = async () => v2;
    const r = render(createElement(BrandKitPanel, { projectId: "prj_0123456789abcdef", api }));
    await r.findByTestId("brand-glyph-heart");
    expect(r.getByTestId("brand-label").textContent).toBe("Demo");
    fireEvent.click(r.getByTestId("brand-glyph-heart"));
    fireEvent.click(r.getByTestId("brand-save"));
    const msg = await r.findByTestId("brand-msg");
    expect(msg.textContent).toContain("1.2.3");
    expect(save).toHaveBeenCalledWith("prj_0123456789abcdef", expect.objectContaining({ glyph: "heart" }));
  });
  it("blocks saving a malformed colour", async () => {
    const view = parseBrand(good)!;
    const api: BrandApi = { load: async () => view, save: vi.fn() };
    const r = render(createElement(BrandKitPanel, { projectId: "prj_0123456789abcdef", api }));
    const bg = await r.findByTestId("brand-bg");
    fireEvent.change(bg, { target: { value: "#12" } });
    expect((r.getByTestId("brand-save") as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("wow2 brand kit fields", () => {
  it("parses appName and storeScreenshots (strictly) and the panel saves them", async () => {
    const v = parseBrand({ ...good, kit: { ...good.kit, appName: "Notite", storeScreenshots: true } })!;
    expect(v.kit.appName).toBe("Notite");
    expect(v.kit.storeScreenshots).toBe(true);
    expect(parseBrand({ ...good, kit: { ...good.kit, storeScreenshots: "yes" } })!.kit.storeScreenshots).toBe(false);
    expect(parseBrand(good)!.kit.appName).toBe("");
    const save = vi.fn(async (_p: string, kit: unknown) => ({ ...v, kit: kit as typeof v.kit }));
    const api: BrandApi = { load: async () => v, save };
    const { findByTestId, getByTestId } = render(createElement(BrandKitPanel, { projectId: "prj_0123456789abcdef", api }));
    fireEvent.change(await findByTestId("brand-appname"), { target: { value: "Altceva" } });
    fireEvent.click(getByTestId("brand-storeshots"));
    fireEvent.click(getByTestId("brand-save"));
    await vi.waitFor(() => expect(save).toHaveBeenCalled());
    expect(save.mock.calls[0]![1]).toMatchObject({ appName: "Altceva", storeScreenshots: false });
  });
});
