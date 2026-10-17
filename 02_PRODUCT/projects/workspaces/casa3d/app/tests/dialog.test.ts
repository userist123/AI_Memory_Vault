// Dialogurile aplicației în locul lui confirm()/prompt() (audit UI F-22): coada, răspunsurile, capcana de focus și tastele.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { askConfirm, askPrompt, answerDialog, currentDialog, subscribeDialogs, canConfirm, trapFocusIndex, dialogKey, cancelAllDialogs, focusMemory } from '../lib/dialog';

test('askConfirm: true doar pe butonul principal; orice alt răspuns înseamnă „nu”', async () => {
  let calls = 0; const off = subscribeDialogs(() => calls++);
  const p = askConfirm({ title: 'Ștergeți varianta B?', confirmLabel: 'Șterge varianta', danger: true });
  assert.equal(currentDialog()!.title, 'Ștergeți varianta B?'); assert.equal(currentDialog()!.kind, 'confirm');
  answerDialog(true); assert.equal(await p, true); assert.equal(currentDialog(), null);
  const q = askConfirm({ title: 'Din nou?' }); answerDialog('da'); assert.equal(await q, false, 'un text nu e o confirmare');
  const r = askConfirm({ title: 'Escape' }); answerDialog(false); assert.equal(await r, false);
  off(); assert.ok(calls >= 6, 'ascultătorii află de fiecare deschidere și închidere');
});
test('askPrompt: textul scris, sau null la anulare; cererile simultane așteaptă la rând', async () => {
  const a = askPrompt({ title: 'Nota reviziei', input: { label: 'Notă' } }), b = askConfirm({ title: 'Al doilea' });
  assert.equal(currentDialog()!.title, 'Nota reviziei'); answerDialog('Predat clientului'); assert.equal(await a, 'Predat clientului');
  assert.equal(currentDialog()!.title, 'Al doilea'); answerDialog(true); assert.equal(await b, true);
  const c = askPrompt({ title: 'X', input: { label: 'Notă' } }); answerDialog(null); assert.equal(await c, null);
  answerDialog(true); assert.equal(currentDialog(), null, 'un răspuns fără dialog deschis nu strică nimic');
});
test('confirmarea prin tastare: butonul principal se activează doar la textul exact', () => {
  const r = { kind: 'prompt' as const, title: 'Ștergere definitivă', input: { label: 'Numele proiectului', mustEqual: 'Casa Popescu' } };
  assert.equal(canConfirm(r, 'casa popescu'), false); assert.equal(canConfirm(r, ' Casa Popescu '), true); assert.equal(canConfirm({ kind: 'confirm', title: 'x' }, ''), true);
});
test('capcana de focus și tastele: Tab circulă în dialog, Escape anulează, Enter confirmă doar în câmpul de o linie', () => {
  assert.equal(trapFocusIndex(3, 2, false), 0); assert.equal(trapFocusIndex(3, 0, true), 2); assert.equal(trapFocusIndex(3, 1, false), 2);
  assert.equal(trapFocusIndex(3, -1, false), 0, 'focusul plecat din dialog revine la primul'); assert.equal(trapFocusIndex(3, -1, true), 2); assert.equal(trapFocusIndex(0, 0, false), -1);
  assert.equal(dialogKey('Escape', false), 'cancel'); assert.equal(dialogKey('Tab', false), 'trap'); assert.equal(dialogKey('Enter', true), 'confirm'); assert.equal(dialogKey('Enter', false), null);
});
test('nicio componentă nu mai folosește confirm() / prompt() / alert() din browser', () => {
  const root = path.resolve(import.meta.dirname, '..'), dirs = ['components', 'app'];
  const walk = (d: string): string[] => readdirSync(d, { withFileTypes: true }).flatMap(e => e.isDirectory() ? (e.name === 'api' ? [] : walk(path.join(d, e.name))) : /\.tsx?$/.test(e.name) ? [path.join(d, e.name)] : []);
  const bad = dirs.flatMap(d => walk(path.join(root, d))).filter(f => /(^|[^\w.])(confirm|prompt|alert)\(/.test(readFileSync(f, 'utf8').replace(/\/\/.*$/gm, '')));
  assert.deepEqual(bad.map(f => path.relative(root, f)), []);
});
test('schimbarea paginii anulează toate dialogurile din coadă: acțiunea paginii vechi nu mai rulează', async () => {
  let ran = false, calls = 0; const off = subscribeDialogs(() => calls++);
  const a = askConfirm({ title: 'Șterge proiectul?' }).then(ok => { if (ok) ran = true; return ok; }), b = askPrompt({ title: 'Nume', input: { label: 'Nume' } }), c = askConfirm({ title: 'Al treilea' });
  const before = calls; cancelAllDialogs();
  assert.equal(currentDialog(), null, 'nu rămâne nimic afișat'); assert.deepEqual(await Promise.all([a, b, c]), [false, null, false]);
  assert.equal(calls, before + 1, 'gazda află o singură dată'); answerDialog(true); assert.equal(ran, false, 'un „da” întârziat nu ajunge la pagina veche');
  cancelAllDialogs(); assert.equal(calls, before + 1, 'fără dialoguri nu se anunță nimic'); off();
  const d = askConfirm({ title: 'Pe pagina nouă' }); assert.equal(currentDialog()!.title, 'Pe pagina nouă'); answerDialog(true); assert.equal(await d, true, 'dialogurile noi merg normal');
});
test('focusul revine la elementul de dinaintea primului dialog, după ce coada s-a golit', () => {
  const m = focusMemory<string>();
  m.show('buton-șterge'); m.show('butonul OK din primul dialog');   // al doilea dialog din coadă: focusul e deja în dialog
  assert.equal(m.hide(), 'buton-șterge'); assert.equal(m.hide(), null, 'o singură dată');
  m.show(null); assert.equal(m.hide(), null); m.show('alt buton'); assert.equal(m.hide(), 'alt buton', 'o coadă nouă ține minte din nou');
});
