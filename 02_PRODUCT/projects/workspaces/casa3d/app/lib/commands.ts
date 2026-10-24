// Registrul de comenzi: un singur tabel de date pentru scurtături. Tastatura editorului, paleta Ctrl/⌘K, tooltip-urile
// uneltelor și dialogul de ajutor `?` citesc de aici, deci nu mai pot diverge. Pur: fără React, fără DOM (tests/commands.test.ts).
import type { Tool } from '@/components/PlanView';

/** Un modificator: `true` cerut, `false`/lipsă interzis, `'any'` indiferent (ca în vechiul handler: Delete, Esc, `?`). */
export type Mod = boolean | 'any';
/** `key` = `KeyboardEvent.key`; o literă se compară fără majuscule. `ctrlOrMeta` = Ctrl pe Windows/Linux, ⌘ pe mac. */
export interface Shortcut { key: string; ctrlOrMeta?: Mod; shift?: Mod; alt?: Mod }
export type CommandDomain = 'global' | 'editor';
/** `context` = comandă doar de meniu contextual (fără tastă, nu apare în ajutor) */
export type CommandGroup = 'edit' | 'tools' | 'nav' | 'context';
export interface CommandDef {
  id: string;
  /** cheia i18n a etichetei */
  label: string;
  /** eticheta scurtă din meniul contextual (lipsă = `label`) */
  menuLabel?: string;
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
const TOOL_KEYS: Record<Tool, string> = { select: 'v', wall: 'p', room: 'c', door: 'u', window: 'o', passage: 'j', shaft: 'h', niche: 'n', clad: 'k', wall_item: 'x', dim: 'q', text: 'b', column: 't', beam: 'g', glass_wall: 'l', demolish: 'y', floor_zone: 'z', stair: 'e', measure: 'm', tech: 'i' };
export const TOOLS = Object.keys(TOOL_KEYS) as Tool[];

const ANY = { ctrlOrMeta: 'any', shift: 'any', alt: 'any' } as const;
const ARROWS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'];
export const COMMANDS: CommandDef[] = [
  { id: 'undo', label: 'help.undo', group: 'edit', domain: 'editor', helpKeys: 'help.undoKeys', shortcuts: [{ key: 'z', ctrlOrMeta: true, alt: 'any' }] },
  { id: 'redo', label: 'help.redo', group: 'edit', domain: 'editor', helpKeys: 'help.redoKeys', shortcuts: [{ key: 'y', ctrlOrMeta: true, shift: 'any', alt: 'any' }, { key: 'z', ctrlOrMeta: true, shift: true, alt: 'any' }] },
  { id: 'delete', label: 'help.delete', menuLabel: 'ctx.delete', group: 'edit', domain: 'editor', helpKeys: 'help.deleteKeys', shortcuts: [{ key: 'Delete', ...ANY }, { key: 'Backspace', ...ANY }] },
  { id: 'rotate', label: 'help.rotate', menuLabel: 'ctx.rotate', group: 'edit', domain: 'editor', shortcuts: [{ key: 'r', shift: 'any' }] },
  { id: 'duplicate', label: 'help.duplicate', menuLabel: 'ctx.duplicate', group: 'edit', domain: 'editor', helpKeys: 'help.duplicateKeys', shortcuts: [{ key: 'd', ctrlOrMeta: true, shift: 'any', alt: 'any' }] },
  { id: 'nudge', label: 'help.nudge', group: 'edit', domain: 'editor', helpKeys: 'help.arrowKeys', shortcuts: ARROWS.map(key => ({ key })) },
  { id: 'nudge.big', label: 'help.nudgeBig', group: 'edit', domain: 'editor', helpKeys: 'help.shiftArrowKeys', shortcuts: ARROWS.map(key => ({ key, shift: true })) },
  { id: 'escape', label: 'help.escape', group: 'nav', domain: 'editor', shortcuts: [{ key: 'Escape', ...ANY }] },
  { id: 'help', label: 'help.toggle', group: 'nav', domain: 'editor', shortcuts: [{ key: '?', ...ANY }] },
  { id: 'palette', label: 'cmdk.open', group: 'nav', domain: 'global', dispatch: false, shortcuts: [{ key: 'k', ctrlOrMeta: true }] },
  // F e prinsă de plan (PlanView keyDown), nu de dispecerul editorului
  { id: 'frame', label: 'help.frame', menuLabel: 'ctx.frame', group: 'nav', domain: 'editor', dispatch: false, shortcuts: [{ key: 'f' }] },
  // spațiul de lucru (PLAN_A1_3): Focus ascunde panourile; structura și layoutul clasic sunt doar în ⋯ și Ctrl/⌘K.
  // Alt oricum: pe tastatura românească „\” cere AltGr, pe care Windows îl raportează ca Ctrl+Alt
  { id: 'ws.focus', label: 'ws.focus', group: 'nav', domain: 'editor', shortcuts: [{ key: '\\', ctrlOrMeta: true, alt: 'any' }] },
  { id: 'ws.outliner', label: 'ws.outliner', group: 'nav', domain: 'editor', dispatch: false, shortcuts: [] },
  { id: 'ws.classic', label: 'ws.classic', group: 'nav', domain: 'editor', dispatch: false, shortcuts: [] },
  { id: 'wall.length', label: 'ctx.editLength', group: 'context', domain: 'editor', dispatch: false, shortcuts: [] },
  { id: 'view3d', label: 'hud.frame3d', group: 'context', domain: 'editor', dispatch: false, shortcuts: [] },
  { id: 'room.furnish', label: 'hud.autoFurnish', group: 'context', domain: 'editor', dispatch: false, shortcuts: [] },
  { id: 'room.split', label: 'hud.splitRoom', group: 'context', domain: 'editor', dispatch: false, shortcuts: [] },
  { id: 'column.shape', label: 'ctx.columnShape', group: 'context', domain: 'editor', dispatch: false, shortcuts: [] },
  ...TOOLS.map((tk): CommandDef => ({ id: `tool.${tk}`, label: `tool.${tk}`, group: 'tools', domain: 'editor', tool: tk, shortcuts: [{ key: TOOL_KEYS[tk] }] }))
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
export const toolShortcut = (tk: Tool): Shortcut => ({ key: TOOL_KEYS[tk] });
/** Hint-ul din paletă: prima scurtătură a comenzii, formatată. */
export const commandHint = (id: string, o: { mac: boolean }) => { const s = commandById(id)?.shortcuts[0]; return s ? formatShortcut(s, o) : undefined; };

/** Ce comenzi arată meniul contextual (clic dreapta, Shift+F10) pentru selecția de tipul `kind`; „șterge” e mereu ultima. */
export type ContextKind = 'wall' | 'room' | 'placement' | 'stair' | 'column' | 'beam' | 'glass_wall' | 'opening' | 'niche' | 'wall_item' | 'annotation' | 'floor_zone';
const CONTEXT: Record<ContextKind, string[]> = {
  wall: ['wall.length', 'frame', 'delete'], placement: ['rotate', 'duplicate', 'view3d', 'frame', 'delete'],
  room: ['room.furnish', 'room.split', 'frame', 'delete'], column: ['rotate', 'column.shape', 'frame', 'delete'],
  stair: ['rotate', 'frame', 'delete'], beam: ['frame', 'delete'], glass_wall: ['frame', 'delete'], opening: ['frame', 'delete'],
  niche: ['frame', 'delete'], wall_item: ['frame', 'delete'], annotation: ['frame', 'delete'], floor_zone: ['frame', 'delete']
};
export const contextCommands = (kind: ContextKind): string[] => CONTEXT[kind];

/** Un rând din ajutor: `keysKey` (cheie i18n) are prioritate față de `keys` (text literal); `label` primește {small}/{big}. */
export interface HelpRow { keys: string; keysKey?: string; label: string; section: 'edit' | 'draw' | 'tools' }
/** Rândurile dialogului `?` pe secțiuni: editare (ordinea veche, rotița, Alt+trage, paleta), desenare precisă (lungimea tastată, Shift), unelte. */
export function helpRows(o: { mac: boolean }): HelpRow[] {
  const row = (c: CommandDef): HelpRow => ({ keys: c.shortcuts.map(s => formatShortcut(s, o)).join(' / '), keysKey: c.helpKeys, label: c.label, section: c.group === 'tools' ? 'tools' : 'edit' });
  const edit = COMMANDS.filter(c => c.group === 'edit').map(row), by = (id: string) => row(commandById(id)!);
  return [...edit, by('escape'), { keys: '', keysKey: 'help.wheelKeys', label: 'help.zoom', section: 'edit' }, { keys: '', keysKey: 'help.altDragKeys', label: 'help.pan', section: 'edit' }, by('frame'), by('help'), by('palette'), by('ws.focus'),
    { keys: '', keysKey: 'help.typeLengthKeys', label: 'help.typeLength', section: 'draw' }, { keys: '', keysKey: 'help.shiftDrawKeys', label: 'help.shiftDraw', section: 'draw' },
    ...COMMANDS.filter(c => c.group === 'tools').map(row)];
}
