"""JavaScript evaluated in the preview page to collect semantic layout evidence.

Prefers semantic DOM information (data-component-id, role, text, bounds, computed styles)
over pixel guessing. Returned data is measured by the browser, not inferred.
"""

DOM_PROBE_JS = r"""
() => {
  const vw = window.innerWidth, vh = window.innerHeight;
  const parseColor = (s) => { const m = s && s.match(/rgba?\(([^)]+)\)/); if (!m) return null;
    const p = m[1].split(',').map(x => parseFloat(x)); return {r:p[0], g:p[1], b:p[2], a: p.length > 3 ? p[3] : 1}; };
  const effectiveBg = (el) => { let e = el; while (e && e.nodeType === 1) { const cs = getComputedStyle(e);
      if (cs.backgroundImage && cs.backgroundImage !== 'none') return {image: true};
      const c = parseColor(cs.backgroundColor); if (c && c.a > 0.5) return c; e = e.parentElement; }
    return {r:255, g:255, b:255, a:1, assumed: true}; };
  const roleOf = (el) => el.getAttribute('role') || ({H1:'heading',H2:'heading',H3:'heading',H4:'heading',H5:'heading',H6:'heading',
      A:'link',BUTTON:'button',IMG:'img',NAV:'navigation',FOOTER:'contentinfo',UL:'list',P:'paragraph',SECTION:'region'})[el.tagName] || 'generic';
  const ownText = (el) => Array.from(el.childNodes).filter(n => n.nodeType === 3).map(n => n.textContent).join(' ').trim();
  const elements = [];
  for (const el of document.querySelectorAll('[data-component-id]')) {
    const r = el.getBoundingClientRect(); const cs = getComputedStyle(el);
    const text = (el.innerText || '').trim().slice(0, 300);
    const hasOwnText = ownText(el).length > 0 || ['H1','H2','H3','H4','H5','H6','P','A','LI','BUTTON'].includes(el.tagName);
    const fg = parseColor(cs.color);
    elements.push({
      elementId: el.dataset.componentId, componentType: el.dataset.componentType, tag: el.tagName.toLowerCase(),
      role: roleOf(el), text, hasOwnText,
      bounds: {x: Math.round(r.x + scrollX), y: Math.round(r.y + scrollY), width: Math.round(r.width), height: Math.round(r.height)},
      viewportBounds: {x: Math.round(r.x), y: Math.round(r.y), width: Math.round(r.width), height: Math.round(r.height)},
      parentId: el.parentElement ? (el.parentElement.closest('[data-component-id]') || {}).dataset?.componentId || null : null,
      style: {color: cs.color, backgroundColor: cs.backgroundColor, fontSize: parseFloat(cs.fontSize), fontWeight: cs.fontWeight,
              display: cs.display, visibility: cs.visibility, opacity: parseFloat(cs.opacity), overflow: cs.overflow,
              position: cs.position, zIndex: cs.zIndex},
      fg, bg: effectiveBg(el),
      scrollWidth: el.scrollWidth, clientWidth: el.clientWidth, scrollHeight: el.scrollHeight, clientHeight: el.clientHeight,
      img: el.tagName === 'IMG' ? {complete: el.complete, naturalWidth: el.naturalWidth, src: el.getAttribute('src')} : null,
      scene: (() => { const cv = el.querySelector('canvas[data-scene]'); if (!cv) return null; const d = cv.dataset;
        return {status: d.sceneStatus || null, fps: d.fps ? +d.fps : null, triangles: d.triangles ? +d.triangles : null,
                drawCalls: d.drawCalls ? +d.drawCalls : null, geometries: d.geometries ? +d.geometries : null,
                textures: d.textures ? +d.textures : null, loadMs: d.loadMs ? +d.loadMs : null, picked: d.picked || null}; })(),
    });
  }
  // text nodes outside the components that overflow the viewport
  return {
    viewport: {width: vw, height: vh, devicePixelRatio: window.devicePixelRatio},
    document: {scrollWidth: document.documentElement.scrollWidth, scrollHeight: document.documentElement.scrollHeight,
               title: document.title, revision: document.body?.dataset?.revision || null, projectId: document.body?.dataset?.projectId || null},
    elements,
  };
}
"""
