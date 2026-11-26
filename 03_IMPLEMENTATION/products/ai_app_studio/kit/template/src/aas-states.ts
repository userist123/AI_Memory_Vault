// AI App Studio kit: preview state switch. The studio's preview toolbar (Populated / Loading / Empty / Error)
// reloads the preview with ?aas-state=<state>; a flow test can do the same with page.goto('/?aas-state=empty').
// Read-only: the hook only reads the page's own URL (query or #aas-state=...), no messages, no storage.
//
//   const forced = useAasState();               // null in normal use
//   const view = forced ?? (loading ? "loading" : error ? "error" : items.length ? "populated" : "empty");
import { useEffect, useState } from "react";

export type AasState = "populated" | "loading" | "empty" | "error";
export const AAS_STATES: readonly AasState[] = ["populated", "loading", "empty", "error"];

export function readAasState(loc: { search: string; hash: string } = window.location): AasState | null {
  const fromQuery = new URLSearchParams(loc.search).get("aas-state");
  const fromHash = new URLSearchParams(loc.hash.replace(/^#/, "")).get("aas-state");
  const v = fromQuery ?? fromHash;
  return v !== null && (AAS_STATES as readonly string[]).includes(v) ? (v as AasState) : null;
}

export function useAasState(): AasState | null {
  const [state, setState] = useState<AasState | null>(() => readAasState());
  useEffect(() => {
    const update = () => setState(readAasState());
    window.addEventListener("hashchange", update);
    window.addEventListener("popstate", update);
    return () => {
      window.removeEventListener("hashchange", update);
      window.removeEventListener("popstate", update);
    };
  }, []);
  return state;
}
