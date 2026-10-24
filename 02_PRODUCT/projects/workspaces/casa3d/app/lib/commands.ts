// Registrul de comenzi: un singur tabel de date pentru scurtături. Tastatura editorului, paleta Ctrl/⌘K, tooltip-urile
// uneltelor și dialogul de ajutor `?` citesc de aici, deci nu mai pot diverge. Pur: fără React, fără DOM (tests/commands.test.ts).
import type { Tool } from '@/components/PlanView';

/** Un modificator: `true` cerut, `false`/lipsă interzis, `'any'` indiferent (ca în vechiul handler: Delete, Esc, `?`). */
export type Mod = boolean | 'any';
/** `key` = `KeyboardEvent.key`; o literă se compară fără majuscule. `ctrlOrMeta` = Ctrl pe Windows/Linux, ⌘ pe mac. */
export interface Shortcut { key: string; ctrlOrMeta?: Mod; shift?: Mod; alt?: Mod }
export type CommandDomain = 'global' | 'editor';
export type CommandGroup = 'edit' | 'tools' | 'nav';
export interface CommandDef {
  id: string;
  /** cheia i18n a etichetei */
  label: string;
  group: CommandGroup;
  shortcuts: Shortcut[];
  domain: CommandDomain;
  /** `false` = doar afișare (Ctrl/⌘K e tratat de isPaletteShortcut din lib/fuzzy.ts) */
  dispatch?: false;
  /** în ajutor, textul tastelor vine din dicționar (ex. „Ctrl/Cmd + Z”) în loc de scurtătura formatată */
  helpKeys?: string;
  tool?: Tool;
}
export interface KeyLike { key: string; shiftKey: boolean; ctrlKey: boolean; metaKey: boolean; altKey: boolean }

// Literele urmează numele românești; aceleași taste în engleză. R rămâne rotire. W/A/S/D sunt rezervate mersului prin
// vederea 3D (viewer3d-engine.js le citește pe window), de aceea scara e E, nu S. Record<Tool, …>: o unealtă nouă fără tastă nu compilează.
// Lotul 4: literele s-au terminat, deci tavanul fals pe zonă (Shift+Z, lângă zona de pardoseală Z) și balustrada (Shift+Y) au Shift.
const TOOL_KEYS: Record<Tool, string | Shortcut> = { select: 'v', wall: 'p', room: 'c', door: 'u', window: 'o', passage: 'j', shaft: 'h', niche: 'n', clad: 'k', wall_item: 'x', dim: 'q', text: 'b', column: 't', beam: 'g', glass_wall: 'l', demolish: 'y', floor_zone: 'z', stair: 'e', measure: 'm', tech: 'i',
  ceiling_zone: { key: 'z', shift: true }, railing: { key: 'y', shift: true } };
const toolKey = (tk: Tool): Shortcut => { const k = TOOL_KEYS[tk]; return typeof k === 'string' ? { key: k } : { ...k }; };
export const TOOLS = Object.keys(TOOL_KEYS) as Tool[];

const ANY = { ctrlOrMeta: 'any', shift: 'any', alt: 'any' } as const;
const ARROWS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'];
export const COMMANDS: CommandDef[] = [
  { id: 'undo', label: 'help.undo', group: 'edit', domain: 'editor', helpKeys: 'help.undoKeys', shortcuts: [{ key: 'z', ctrlOrMeta: true, alt: 'any' }] },
  { id: 'redo', label: 'help.redo', group: 'edit', domain: 'editor', helpKeys: 'help.redoKeys', shortcuts: [{ key: 'y', ctrlOrMeta: true, shift: 'any', alt: 'any' }, { key: 'z', ctrlOrMeta: true, shift: true, alt: 'any' }] },
  { id: 'delete', label: 'help.delete', group: 'edit', domain: 'editor', helpKeys: 'help.deleteKeys', shortcuts: [{ key: 'Delete', ...ANY }, { key: 'Backspace', ...ANY }] },
  { id: 'rotate', label: 'help.rotate', group: 'edit', domain: 'editor', shortcuts: [{ key: 'r', shift: 'any' }] },
  { id: 'duplicate', label: 'help.duplicate', group: 'edit', domain: 'editor', helpKeys: 'help.duplicateKeys', shortcuts: [{ key: 'd', ctrlOrMeta: true, shift: 'any', alt: 'any' }] },
  { id: 'nudge', label: 'help.nudge', group: 'edit', domain: 'editor', helpKeys: 'help.arrowKeys', shortcuts: ARROWS.map(key => ({ key })) },
  { id: 'nudge.big', label: 'help.nudgeBig', group: 'edit', domain: 'editor', helpKeys: 'help.shiftArrowKeys', shortcuts: ARROWS.map(key => ({ key, shift: true })) },
  { id: 'escape', label: 'help.escape', group: 'nav', domain: 'editor', shortcuts: [{ key: 'Escape', ...ANY }] },
  { id: 'help', label: 'help.toggle', group: 'nav', domain: 'editor', shortcuts: [{ key: '?', ...ANY }] },
  { id: 'palette', label: 'cmdk.open', group: 'nav', domain: 'global', dispatch: false, shortcuts: [{ key: 'k', ctrlOrMeta: true }] },
  ...TOOLS.map((tk): CommandDef => ({ id: `tool.${tk}`, label: `tool.${tk}`, group: 'tools', domain: 'editor', tool: tk, shortcuts: [toolKey(tk)] }))
];

const isLetter = (k: string) => k.length === 1 && k.toLowerCase() !== k.toUpperCase();
const modOk = (want: Mod | undefined, has: boolean) => want === 'any' || !!want === has;
/** Se potrivește evenimentul cu scurtătura? Literele se compară fără majuscule (Caps Lock, Shift+R). */
export function shortcutMatches(s: Shortcut, e: KeyLike): boolean {
  const keyOk = isLetter(s.key) ? e.key.toLowerCase() === s.key.toLowerCase() : e.key === s.key;
  return keyOk && modOk(s.ctrlOrMeta, e.ctrlKey || e.metaKey) && modOk(s.shift, e.shiftKey) && modOk(s.alt, e.altKey);
}
/** Dispecerul editorului: id-ul comenzii pentru o tastă, sau null. Comenzile doar de afișare (Ctrl/⌘K) nu se întorc. */
export function matchCommand(e: KeyLike): string | null {
  return COMMANDS.find(c => c.dispatch !== false && c.shortcuts.some(s => shortcutMatches(s, e)))?.id ?? null;
}

const KEY_LABEL: Record<string, string> = { Escape: 'Esc', ArrowLeft: '←', ArrowRight: '→', ArrowUp: '↑', ArrowDown: '↓' };
/** Textul afișat: pe mac `⇧⌘Z`, altfel `Ctrl+Shift+Z`; modificatorii „indiferent” nu se afișează; literele apar mari. */
export function formatShortcut(s: Shortcut, o: { mac: boolean }): string {
  const k = KEY_LABEL[s.key] ?? (isLetter(s.key) ? s.key.toUpperCase() : s.key);
  if (o.mac) return `${s.alt === true ? '⌥' : ''}${s.shift === true ? '⇧' : ''}${s.ctrlOrMeta === true ? '⌘' : ''}${k}`;
  return [s.ctrlOrMeta === true && 'Ctrl', s.alt === true && 'Alt', s.shift === true && 'Shift', k].filter(Boolean).join('+');
}
/** Valoarea pentru `aria-keyshortcuts` (sintaxa WAI-ARIA: Control/Meta, Shift, Alt, tasta). */
export function ariaShortcut(s: Shortcut, o: { mac: boolean }): string {
  return [s.ctrlOrMeta === true && (o.mac ? 'Meta' : 'Control'), s.alt === true && 'Alt', s.shift === true && 'Shift', isLetter(s.key) ? s.key.toUpperCase() : s.key].filter(Boolean).join('+');
}
export const commandById = (id: string) => COMMANDS.find(c => c.id === id);
export const toolShortcut = (tk: Tool): Shortcut => toolKey(tk);
/** Hint-ul din paletă: prima scurtătură a comenzii, formatată. */
export const commandHint = (id: string, o: { mac: boolean }) => { const s = commandById(id)?.shortcuts[0]; return s ? formatShortcut(s, o) : undefined; };

/** Un rând din ajutor: `keysKey` (cheie i18n) are prioritate față de `keys` (text literal); `label` primește {small}/{big}. */
export interface HelpRow { keys: string; keysKey?: string; label: string; section: 'edit' | 'draw' | 'tools' }
/** Rândurile dialogului `?` pe secțiuni: editare (ordinea veche, rotița, Alt+trage, paleta), desenare precisă (lungimea tastată, Shift), unelte. */
export function helpRows(o: { mac: boolean }): HelpRow[] {
  const row = (c: CommandDef): HelpRow => ({ keys: c.shortcuts.map(s => formatShortcut(s, o)).join(' / '), keysKey: c.helpKeys, label: c.label, section: c.group === 'tools' ? 'tools' : 'edit' });
  const edit = COMMANDS.filter(c => c.group === 'edit').map(row), by = (id: string) => row(commandById(id)!);
  return [...edit, by('escape'), { keys: '', keysKey: 'help.wheelKeys', label: 'help.zoom', section: 'edit' }, { keys: '', keysKey: 'help.altDragKeys', label: 'help.pan', section: 'edit' }, { keys: 'F', label: 'help.frame', section: 'edit' }, by('help'), by('palette'),
    { keys: '', keysKey: 'help.typeLengthKeys', label: 'help.typeLength', section: 'draw' }, { keys: '', keysKey: 'help.shiftDrawKeys', label: 'help.shiftDraw', section: 'draw' },
    ...COMMANDS.filter(c => c.group === 'tools').map(row)];
}
