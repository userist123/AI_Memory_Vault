import { test, expect, type Page, type BrowserContext } from '@playwright/test';

// Serial smoke flow: one project is created through the API and reused by the following tests.
test.describe.configure({ mode: 'serial' });

const NAME = 'E2E demo';
let projectId = '';
// The project belongs to the browser identity that created it (cookie); later tests reuse those cookies.
let ownerCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];

async function openAsOwner(page: Page, path: string) {
  expect(projectId).not.toBe('');
  await page.context().addCookies(ownerCookies);
  return page.goto(path);
}

function trackErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
  return errors;
}

test('home renders the project list without errors', async ({ page }) => {
  const errors = trackErrors(page);
  const res = await page.goto('/');
  expect(res?.status()).toBe(200);
  await expect(page.getByRole('heading', { level: 1, name: 'Proiecte' })).toBeVisible();
  await page.waitForLoadState('networkidle');
  expect(errors).toEqual([]);
});

test('project created through the API appears on home', async ({ page }) => {
  await page.goto('/');
  // The first visit fires several API calls in parallel and each may mint a new anonymous owner cookie;
  // wait until they settle so the project is created under the cookie the browser finally keeps.
  await page.waitForLoadState('networkidle');
  const r = await page.evaluate(async name => {
    const res = await fetch('/api/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name, template: 'demo', lang: 'ro' }) });
    return { status: res.status, body: await res.json() };
  }, NAME);
  expect(r.status).toBe(201);
  expect(typeof r.body.id).toBe('string');
  expect(r.body.id.length).toBeGreaterThan(0);
  projectId = r.body.id;
  await page.reload();
  await expect(page.locator(`a.pcard[href="/p/${projectId}"]`)).toContainText(NAME);
  ownerCookies = await page.context().cookies();
});

test('plan editor shows the demo rooms and stage tabs', async ({ page }) => {
  await openAsOwner(page, `/p/${projectId}`);
  for (const room of ['Living', 'Dormitor', 'Bucătărie', 'Baie', 'Hol']) {
    await expect(page.getByText(room, { exact: true }).first()).toBeVisible();
  }
  await expect(page.locator('[data-stage="survey"]')).toContainText('Releveu');
  await expect(page.locator('[data-stage="quote"]')).toContainText('Deviz');
});

test('3D view draws a live, non-blank WebGL canvas', async ({ page }) => {
  const errors = trackErrors(page);
  await openAsOwner(page, `/p/${projectId}`);
  await page.getByRole('button', { name: '3D', exact: true }).click();
  // wait until the largest canvas on the page is a sizeable WebGL canvas
  await page.waitForFunction(() => {
    const cs = [...document.querySelectorAll('canvas')].sort((a, b) => b.width * b.height - a.width * a.height);
    const c = cs[0];
    return !!c && c.width > 300 && c.height > 300 && !!(c.getContext('webgl2') || c.getContext('webgl'));
  }, undefined, { timeout: 30000 });
  const info = await page.evaluate(() => {
    const c = [...document.querySelectorAll('canvas')].sort((a, b) => b.width * b.height - a.width * a.height)[0];
    c.setAttribute('data-e2e-main', '1');
    const gl = (c.getContext('webgl2') || c.getContext('webgl')) as WebGLRenderingContext | null;
    return { w: c.width, h: c.height, hasGl: !!gl, lost: gl ? gl.isContextLost() : true };
  });
  expect(info.hasGl).toBe(true);
  expect(info.lost).toBe(false);
  expect(info.w).toBeGreaterThan(300);
  expect(info.h).toBeGreaterThan(300);
  // let a few frames render, then check the pixels on screen are not uniform
  await page.waitForTimeout(1500);
  const png = await page.locator('canvas[data-e2e-main="1"]').screenshot();
  const distinct = await page.evaluate(async b64 => {
    const img = new Image();
    img.src = `data:image/png;base64,${b64}`;
    await img.decode();
    const cv = document.createElement('canvas');
    cv.width = img.width; cv.height = img.height;
    const ctx = cv.getContext('2d')!;
    ctx.drawImage(img, 0, 0);
    const d = ctx.getImageData(0, 0, cv.width, cv.height).data;
    const seen = new Set<number>();
    for (let i = 0; i < d.length && seen.size < 50; i += 4 * 97) seen.add((d[i] << 16) | (d[i + 1] << 8) | d[i + 2]);
    return seen.size;
  }, png.toString('base64'));
  expect(distinct).toBeGreaterThan(10);
  expect(errors).toEqual([]);
});

test('Deviz stage shows the estimate and a RON total', async ({ page }) => {
  await openAsOwner(page, `/p/${projectId}`);
  await page.locator('[data-stage="quote"]').click();
  await expect(page.getByText('Deviz estimativ', { exact: true }).first()).toBeVisible();
  // amounts are formatted with Intl, which separates the currency with a no-break space (\u00A0)
  await expect(page.getByText(/\d[\d.]*[ \u00A0]RON/).first()).toBeVisible();
});

test('presentation page shows the project title and PDF button', async ({ page }) => {
  await openAsOwner(page, `/p/${projectId}/present`);
  await expect(page.getByRole('heading', { level: 1, name: NAME })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Salvează PDF' })).toBeVisible();
});
