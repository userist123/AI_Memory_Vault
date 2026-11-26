"""Build designer-ui modules from data/rules.jsonl.

Usage:
  python build_modules.py merge <src_dir_with_jsonl>   # merge + dedupe raw batches into data/rules.jsonl
  python build_modules.py build                        # generate references/*.md + catalog.json
"""
import collections, difflib, glob, json, os, re, sys

SKILL = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(SKILL, 'data')
REFS = os.path.join(SKILL, 'references')
RULES = os.path.join(DATA, 'rules.jsonl')

# module id -> (tier_min, title, selector)
PAGE_TYPES = ['landing', 'marketing-site', 'dashboard', 'saas-app', 'mobile-app', 'ecommerce',
              'portfolio', 'blog', 'form', 'onboarding', 'pricing']


def module_of(r):
    """Return list of module ids a rule belongs to."""
    cat, cx = r.get('category', ''), r.get('complexity', 'basic')
    alias = {'app': 'saas-app', 'saas': 'saas-app', 'web-app': 'saas-app', 'mobile': 'mobile-app',
             'site': 'marketing-site', 'shop': 'ecommerce'}
    pts = sorted({alias.get(p, p) for p in (r.get('page_types') or []) if p != 'any'})
    mods = []
    if cx == 'advanced' or cat == 'visual-effects' and cx != 'basic':
        mods.append('advanced-visual' if cat != 'motion' else 'advanced-motion')
        return mods
    base = {
        'color': 'core-color', 'typography': 'core-typography', 'spacing-layout': 'core-layout',
        'components': 'components', 'accessibility': 'core-a11y' if cx == 'basic' else 'a11y-full',
        'motion': 'core-motion' if cx == 'basic' else 'motion', 'visual-effects': 'core-visual', 'ux-copy': 'ux-copy',
        'conversion': 'ux-conversion', 'process-tools': 'process', 'resource': 'resources',
    }.get(cat)
    if cat == 'page-pattern':
        mods += [f'page-{p}' for p in pts] or ['page-general']
    elif base:
        # keep T1/T2 core lean: intermediate rules of core/components go to a '-more' module (T3+)
        if cx == 'intermediate' and base in ('core-color', 'core-typography', 'core-layout', 'components', 'core-visual'):
            base = base + '-more'
        mods.append(base)
        # page-specific rules are also copied into the page module
        mods += [f'page-{p}' for p in pts if cat in ('components', 'conversion', 'ux-copy', 'spacing-layout')]
    return mods or ['misc']


META = {
    'core-color': (1, 'Colour', 'Colour roles, palettes, contrast'),
    'core-typography': (1, 'Typography', 'Fonts, scale, line-height, pairing'),
    'core-layout': (1, 'Spacing & layout', 'Grid, spacing scale, alignment, hierarchy'),
    'core-a11y': (1, 'Accessibility basics', 'Minimum accessibility every page needs'),
    'core-visual': (1, 'Visual basics', 'Shadows, radius, imagery, icons at basic level'),
    'components': (1, 'Components', 'Buttons, forms, cards, navigation, tables, modals'),
    'a11y-full': (3, 'Accessibility (full)', 'Detailed accessibility for products'),
    'core-motion': (1, 'Motion basics (fluid kit)', 'CSS-only motion every page may use: tokens, reveal, hover, view transitions, reduced motion'),
    'core-color-more': (3, 'Colour (intermediate)', 'Deeper colour techniques for products'),
    'core-typography-more': (3, 'Typography (intermediate)', 'Type scales per breakpoint, fine tuning'),
    'core-layout-more': (3, 'Spacing & layout (intermediate)', 'Detailed grids, breakpoints, density'),
    'components-more': (3, 'Components (intermediate)', 'Advanced component states and patterns'),
    'core-visual-more': (3, 'Visual (intermediate)', 'Richer visual treatment, still not T4'),
    'motion': (3, 'Motion & micro-interactions', 'Durations, easing, feedback'),
    'ux-copy': (3, 'UX writing', 'Labels, errors, empty states, CTAs'),
    'ux-conversion': (2, 'Conversion & UX psychology', 'Persuasion, trust, friction'),
    'process': (3, 'Process & tools', 'Workflow, Figma, handoff'),
    'resources': (2, 'Resources', 'Named sites/tools recommended by educators'),
    'advanced-visual': (4, 'Advanced visual effects', 'Textures, glass, gradients, 3D, rich effects'),
    'advanced-motion': (4, 'Advanced motion', 'Scroll storytelling, complex animation'),
    'page-general': (2, 'Page patterns (general)', 'Section patterns that fit most pages'),
    'misc': (3, 'Misc', 'Uncategorised'),
}
for p in PAGE_TYPES:
    META[f'page-{p}'] = (3 if p in ('dashboard', 'saas-app', 'mobile-app', 'ecommerce', 'onboarding') else 2,
                         f'Page: {p}', f'Patterns and checklist for {p}')

PAGE_TYPES_OF = {f'page-{p}': [p] for p in PAGE_TYPES}


def norm(s):
    return re.sub(r'[^a-z0-9 ]', '', (s or '').lower()).strip()


def merge(src):
    raw = []
    for f in sorted(glob.glob(os.path.join(src, '*.jsonl'))):
        for line in open(f, encoding='utf-8'):
            line = line.strip()
            if not line:
                continue
            try:
                raw.append(json.loads(line))
            except json.JSONDecodeError:
                pass
    merged = []
    by_topic = collections.defaultdict(list)
    for r in raw:
        key = r.get('category')
        dup = None
        for m in by_topic[key]:
            if difflib.SequenceMatcher(None, norm(m['rule']), norm(r.get('rule'))).ratio() > 0.72:
                dup = m
                break
        if dup:
            dup['sources'] = sorted(set(dup['sources']) | {r.get('author', '')})
            for k in ('values', 'do', 'dont'):
                if not dup.get(k) and r.get(k):
                    dup[k] = r[k]
            dup['page_types'] = sorted(set(dup.get('page_types') or []) | set(r.get('page_types') or []))
            continue
        r = dict(r)
        r['sources'] = [r.get('author', '')]
        for k in ('sc', 'author', 'likes', 'slide'):
            r.pop(k, None)
        by_topic[key].append(r)
        merged.append(r)
    for i, r in enumerate(merged):
        r['id'] = f'R{i:04d}'
        r['support'] = len(r['sources'])
    os.makedirs(DATA, exist_ok=True)
    with open(RULES, 'w', encoding='utf-8') as f:
        for r in merged:
            f.write(json.dumps(r, ensure_ascii=False) + '\n')
    print(f'raw {len(raw)} -> merged {len(merged)}')


def build():
    rules = [json.loads(l) for l in open(RULES, encoding='utf-8') if l.strip()]
    groups = collections.defaultdict(list)
    for r in rules:
        if r.get('confidence') == 'low' and r.get('support', 1) < 2:
            continue
        for m in module_of(r):
            groups[m].append(r)
    os.makedirs(REFS, exist_ok=True)
    for f in glob.glob(os.path.join(REFS, '*.md')):
        if not os.path.basename(f).startswith('manual-'):
            os.remove(f)
    catalog = []
    for mid, rs in sorted(groups.items()):
        tier, title, summary = META.get(mid, (3, mid, mid))
        rs.sort(key=lambda r: (-r.get('support', 1), r.get('topic', '')))
        topics = collections.Counter(r.get('topic', '') for r in rs)
        lines = ['---', f'id: {mid}', f'title: {title}', f'tier_min: T{tier}',
                 f'page_types: {json.dumps(PAGE_TYPES_OF.get(mid, ["any"]))}',
                 f'topics: {json.dumps([t for t, _ in topics.most_common(15)])}',
                 f'rules: {len(rs)}', '---', '', f'# {title}', '', summary + '.',
                 'Support = number of independent educators who teach the rule.', '']
        by_t = collections.defaultdict(list)
        for r in rs:
            by_t[r.get('topic', 'other')].append(r)
        for t, trs in sorted(by_t.items(), key=lambda kv: -sum(x.get('support', 1) for x in kv[1])):
            lines.append(f'## {t}')
            for r in trs:
                extra = []
                if r.get('values'):
                    extra.append(f"values: {r['values']}")
                if r.get('do') and r.get('do') != r.get('rule'):
                    extra.append(f"do: {r['do']}")
                if r.get('dont'):
                    extra.append(f"don't: {r['dont']}")
                for k in ('caveat', 'disagrees', 'scope', 'stack', 'group'):
                    if r.get(k):
                        extra.append(f"{k}: {r[k]}")
                lines.append(f"- {r['rule']} _(support {r.get('support', 1)}, {r.get('complexity', 'basic')}, {r['id']})_")
                for e in extra:
                    lines.append(f'  - {e}')
            lines.append('')
        top = [r for r in rs if r.get('support', 1) >= 2][:10] or rs[:8]
        lines += ['## Checklist', ''] + [f"- [ ] {r['rule']}" for r in top] + ['']
        text = '\n'.join(lines)
        fn = f'{mid}.md'
        open(os.path.join(REFS, fn), 'w', encoding='utf-8').write(text)
        catalog.append(dict(id=mid, file=f'references/{fn}', tier_min=f'T{tier}', title=title,
                            page_types=PAGE_TYPES_OF.get(mid, ['any']),
                            topics=[t for t, _ in topics.most_common(10)], rules=len(rs),
                            est_tokens=len(text) // 4))
    for f in glob.glob(os.path.join(REFS, 'manual-*.md')):
        head = open(f, encoding='utf-8').read(800)
        mid = os.path.basename(f)[:-3]
        tm = re.search(r'tier_min:\s*(T\d)', head)
        catalog.append(dict(id=mid, file=f'references/{os.path.basename(f)}', tier_min=tm.group(1) if tm else 'T1',
                            title=mid, page_types=['any'], topics=[], rules=0,
                            est_tokens=os.path.getsize(f) // 4))
    catalog.sort(key=lambda c: (c['tier_min'], c['id']))
    json.dump(catalog, open(os.path.join(SKILL, 'catalog.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    for c in catalog:
        print(c['tier_min'], c['id'], c['rules'], c['est_tokens'])


if __name__ == '__main__':
    if sys.argv[1] == 'merge':
        merge(sys.argv[2])
    else:
        build()
