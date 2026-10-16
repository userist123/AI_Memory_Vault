// Keyboard rules shared by the command palette and the editor, kept pure so they are tested without a browser.

/** Where a key event inside the palette came from. */
export type PaletteTarget = 'input' | 'option' | 'button' | 'other';
export type PaletteKeyAction = 'move' | 'run' | 'close' | 'trap' | null;

/** What the palette does with a key. Enter runs the highlighted command only from the search field or an option;
 *  on a button (e.g. "Close the palette") Enter and Space keep their native meaning. Home/End move only with an
 *  empty query (otherwise they move the caret in the search field). */
export function paletteKeyAction(key: string, target: PaletteTarget, queryEmpty: boolean): PaletteKeyAction {
  if (key === 'Escape') return 'close';
  if (key === 'Tab') return 'trap';
  if (key === 'ArrowDown' || key === 'ArrowUp') return 'move';
  if ((key === 'Home' || key === 'End') && queryEmpty && target !== 'button') return 'move';
  if (key === 'Enter' && (target === 'input' || target === 'option')) return 'run';
  return null;
}

/** The editor's global shortcuts (arrows nudge, Delete, R, Ctrl+Z…) never act on keys typed in a field, inside an
 *  open dialog (command palette, confirmations) or while the palette is open. */
export function editorIgnoresKey(o: { inField: boolean; inDialog: boolean; paletteOpen: boolean; preview: boolean }): boolean {
  return o.preview || o.inField || o.inDialog || o.paletteOpen;
}
