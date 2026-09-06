"""Wikilink ingestion into the runtime synapse graph.

The vault kept its connectivity in two representations that had drifted apart:
`relations:` frontmatter, which `SynapseStore.from_index()` read, and Obsidian
`[[wikilinks]]` in note bodies, which it did not. The Obsidian graph therefore
looked dense while the runtime graph was effectively empty.

These tests pin the ingestion contract, the navigation-hub cut that keeps it
useful, and the filename resolution it depends on.
"""
import pytest

from graph.synapse_store import (
    HUB_IN_DEGREE_THRESHOLD,
    WIKILINK_WEIGHT,
    SynapseStore,
)


class _Note:
    def __init__(self, nid, title, body="", stem=None, rels=None):
        self.id = nid
        self.title = title
        self.body = body
        self._rels = rels or []
        self._stem = stem or title

    def relations(self):
        return self._rels

    def wikilinks(self):
        import re
        return [w.strip() for w in re.findall(r"\[\[([^\]\|#]+)", self.body)]


class _Index:
    """Minimal stand-in exposing only what from_index() consumes."""

    def __init__(self, notes):
        self.notes = notes
        self.by_id = {n.id: n for n in notes}
        self._by_name = {}
        for n in notes:
            self._by_name.setdefault(n.title.lower(), n)
            self._by_name.setdefault(n._stem.lower(), n)

    def resolve(self, ref):
        if not ref:
            return None
        return self.by_id.get(ref) or self._by_name.get(ref.strip().lower())


def _pair(store):
    return {(s.source_id, s.target_id) for s in store.all()}


@pytest.fixture
def simple():
    return _Index([
        _Note("a", "Alpha", "text [[Beta]] more"),
        _Note("b", "Beta", "back to [[Alpha]]"),
        _Note("c", "Gamma", "no links here"),
    ])


def test_disabled_by_flag_reproduces_declared_only_behaviour(simple):
    """REGRESSION: the old contract must remain reachable unchanged."""
    assert _pair(SynapseStore.from_index(simple, include_wikilinks=False)) == set()


def test_wikilinks_become_edges(simple):
    assert _pair(SynapseStore.from_index(simple, include_wikilinks=True)) == {
        ("a", "b"), ("b", "a"),
    }


def test_wikilink_edges_carry_wikilink_provenance(simple):
    store = SynapseStore.from_index(simple, include_wikilinks=True)
    assert store.all()
    assert all(s.origin == "wikilink" for s in store.all())


def test_wikilink_weight_is_below_a_declared_relation(simple):
    from graph.synapse_store import DEFAULT_WEIGHT
    store = SynapseStore.from_index(simple, include_wikilinks=True)
    assert all(s.weight == WIKILINK_WEIGHT for s in store.all())
    assert WIKILINK_WEIGHT < DEFAULT_WEIGHT


def test_wikilinks_are_not_mirrored():
    """A one-way link must not assert a reciprocity the author never wrote."""
    idx = _Index([_Note("a", "Alpha", "[[Beta]]"), _Note("b", "Beta", "nothing")])
    assert _pair(SynapseStore.from_index(idx, include_wikilinks=True)) == {("a", "b")}


def test_unresolvable_wikilink_is_ignored():
    idx = _Index([_Note("a", "Alpha", "[[Nowhere]] [[Beta]]"), _Note("b", "Beta")])
    assert _pair(SynapseStore.from_index(idx, include_wikilinks=True)) == {("a", "b")}


def test_self_link_is_dropped():
    idx = _Index([_Note("a", "Alpha", "[[Alpha]] and [[Beta]]"), _Note("b", "Beta")])
    edges = _pair(SynapseStore.from_index(idx, include_wikilinks=True))
    assert ("a", "a") not in edges
    assert edges == {("a", "b")}


def test_repeated_link_produces_one_edge():
    idx = _Index([_Note("a", "Alpha", "[[Beta]] [[Beta]] [[Beta]]"), _Note("b", "Beta")])
    assert len(SynapseStore.from_index(idx, include_wikilinks=True).all()) == 1


# --- navigation-hub cut ----------------------------------------------------


def _hub_index(spokes=60):
    """`spokes` notes all linking to one map-of-content note, plus one real
    semantic edge between two spokes."""
    notes = [_Note("hub", "Knowledge Graph Home")]
    for i in range(spokes):
        notes.append(_Note(f"n{i}", f"Note {i}", "[[Knowledge Graph Home]]"))
    notes[1].body += " [[Note 2]]"
    return _Index(notes)


def test_hub_is_excluded_above_threshold():
    idx = _hub_index(spokes=HUB_IN_DEGREE_THRESHOLD + 10)
    edges = _pair(SynapseStore.from_index(idx, include_wikilinks=True))
    assert not any("hub" in e for e in edges), "hub must not appear on any edge"
    assert ("n0", "n2") in edges, "semantic edges between spokes must survive"


def test_hub_cut_can_be_disabled():
    idx = _hub_index(spokes=HUB_IN_DEGREE_THRESHOLD + 10)
    edges = _pair(SynapseStore.from_index(idx, include_wikilinks=True, hub_in_degree=0))
    assert any(t == "hub" for _, t in edges)


def test_below_threshold_target_is_kept():
    idx = _hub_index(spokes=3)
    assert any(t == "hub" for _, t in _pair(
        SynapseStore.from_index(idx, include_wikilinks=True)))


def test_ingestion_is_deterministic(simple):
    runs = [
        [s.key for s in SynapseStore.from_index(simple, include_wikilinks=True).all()]
        for _ in range(3)
    ]
    assert runs[0] == runs[1] == runs[2]


def test_declared_and_wikilink_edges_coexist():
    idx = _Index([
        _Note("a", "Alpha", "[[Gamma]]",
              rels=[{"type": "depends_on", "target_id": "b"}]),
        _Note("b", "Beta"),
        _Note("c", "Gamma"),
    ])
    store = SynapseStore.from_index(idx, include_wikilinks=True)
    origins = {(s.source_id, s.target_id): s.origin for s in store.all()}
    assert origins[("a", "b")] == "declared"
    assert origins[("a", "c")] == "wikilink"


# --- VaultIndex filename resolution ---------------------------------------


def _write(d, name, text):
    p = d / "01_ARCHITECTURE"
    p.mkdir(parents=True, exist_ok=True)
    (p / name).write_text(text, encoding="utf-8")


def test_index_resolves_a_wikilink_by_file_name(tmp_path):
    """Obsidian links point at file names, which often differ from titles.

    Before the by_slug index, `[[08 Memory Subsystems Map]]` failed to resolve
    whenever the note's heading differed from its file name — which is the
    normal case for map-of-content notes.
    """
    from retrieval.vault_index import VaultIndex

    _write(tmp_path, "08 Memory Subsystems Map.md",
           "---\nid: mem-map\n---\n# A Completely Different Heading\n")
    index = VaultIndex.load(tmp_path)

    assert index.resolve("08 Memory Subsystems Map").id == "mem-map"
    assert index.resolve("a completely different heading").id == "mem-map"
    assert index.resolve("mem-map").id == "mem-map"
    assert index.resolve("no such note") is None


def test_underscore_and_hyphen_file_names_resolve(tmp_path):
    from retrieval.vault_index import VaultIndex

    _write(tmp_path, "Agents_Skill_Matrix.md", "---\nid: asm\n---\n# Agents Skill Matrix\n")
    index = VaultIndex.load(tmp_path)

    assert index.resolve("Agents_Skill_Matrix").id == "asm"
    assert index.resolve("Agents Skill Matrix").id == "asm"


def test_title_lookup_is_not_shadowed_by_a_file_stem(tmp_path):
    """Title matches must keep precedence over filename matches.

    Two distinct files are used deliberately: on a case-insensitive filesystem
    "alpha.md" and "Alpha.md" are the same file, so the collision has to be
    built from one note *titled* Alpha and a different note whose *file name*
    is Alpha.
    """
    from retrieval.vault_index import VaultIndex

    _write(tmp_path, "beta.md", "---\nid: titled-alpha\n---\n# Alpha\n")
    _write(tmp_path, "Alpha.md", "---\nid: named-alpha\n---\n# Gamma\n")
    index = VaultIndex.load(tmp_path)

    assert index.resolve("Alpha").id == "titled-alpha"
    assert index.resolve("Gamma").id == "named-alpha"
