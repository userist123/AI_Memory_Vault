/** Unique React keys for sibling panels inside one container: two panels keyed by the same project id collide
 *  and React duplicates them (wow check B1: thousands of RunControls). The prefix makes each kind its own. */
export type PanelKind = "rc" | "changes";
export function panelKey(kind: PanelKind, projectId: string): string {
  return `${kind}:${projectId || "none"}`;
}
