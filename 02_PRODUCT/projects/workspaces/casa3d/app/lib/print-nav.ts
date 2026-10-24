// Page navigator of the print package: which page is "current" while scrolling, from the set of pages that an
// IntersectionObserver reports inside the reading band. Kept pure so it can be tested without a browser.

/** The first page (in document order) inside the band; when none is (between two tall pages), the last known one stays. */
export function activePage(order: string[], visible: ReadonlySet<string>, last: string | null): string | null {
  for (const id of order) if (visible.has(id)) return id;
  return last != null && order.includes(last) ? last : order[0] ?? null;
}

/** Neighbour page for the previous / next buttons, or null at the ends. */
export function stepPage(order: string[], current: string | null, dir: -1 | 1): string | null {
  const i = current == null ? -1 : order.indexOf(current), j = i < 0 ? (dir > 0 ? 0 : -1) : i + dir;
  return j >= 0 && j < order.length ? order[j]! : null;
}

/** A section's label: its explicit data-title, else the first text of its heading, trimmed; never empty. */
export function pageTitle(dataTitle: string | undefined, heading: string | null | undefined, fallback: string): string {
  const t = (dataTitle ?? heading ?? '').replace(/\s+/g, ' ').trim();
  return t || fallback;
}
