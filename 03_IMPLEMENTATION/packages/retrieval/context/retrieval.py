from typing import List, Dict, Any, Optional

from .candidate_generation import generate_candidates, DEFAULT_CANDIDATE_LIMIT

# r025 WP-9 Phase B — how QueryClassifier's INFERRED lifecycle/type filters
# are applied. Never touches an EXPLICIT caller-supplied filter (see
# lifecycle_source/types_source below) and never touches RAW exclusion,
# which storage.query() enforces unconditionally regardless of these
# arguments -- only the classifier's own keyword-triggered inference softens.
CLASSIFIER_FILTER_ARM_HARD = "hard"                # C1: production default, unchanged
CLASSIFIER_FILTER_ARM_BOOST = "boost"              # C2: rank preference, not exclusion
CLASSIFIER_FILTER_ARM_CONDITIONAL = "conditional"  # C3: applied only if pool needs narrowing
CLASSIFIER_FILTER_ARMS = frozenset({
    CLASSIFIER_FILTER_ARM_HARD, CLASSIFIER_FILTER_ARM_BOOST, CLASSIFIER_FILTER_ARM_CONDITIONAL,
})


def _matches_inferred(note: Dict[str, Any], inferred_lifecycle, inferred_types) -> bool:
    if inferred_lifecycle and note.get("lifecycle") not in inferred_lifecycle:
        return False
    if inferred_types and note.get("type") not in inferred_types:
        return False
    return True


def _apply_classifier_filter_arm(arm, candidates, unfiltered_pool_size, inferred_lifecycle,
                                  inferred_types, candidate_limit):
    """Post-ranking softening of the classifier's inferred filter, applied to
    `candidates` (generate_candidates()'s OUTPUT), never to the pre-ranking
    gated variable. If neither inferred_lifecycle nor inferred_types is
    non-empty, there is nothing to soften and this is a no-op for every arm.
    """
    if not inferred_lifecycle and not inferred_types:
        return candidates
    if arm == CLASSIFIER_FILTER_ARM_BOOST:
        # C2: preference, not exclusion. Matching candidates move first;
        # non-matching candidates are kept, not dropped. Stable partition
        # preserves each side's existing (already-deterministic) relative
        # order, so this adds no new tie-break surface.
        matching = [c for c in candidates if _matches_inferred(c, inferred_lifecycle, inferred_types)]
        rest = [c for c in candidates if not _matches_inferred(c, inferred_lifecycle, inferred_types)]
        return matching + rest
    if arm == CLASSIFIER_FILTER_ARM_CONDITIONAL:
        # C3: only narrow when the UNFILTERED pool actually needed it -- a
        # small pool is not helped by narrowing further and risks the
        # collapse-to-zero failure mode (r025 WP-9 Phase A) instead.
        if unfiltered_pool_size <= candidate_limit:
            return candidates
        matching = [c for c in candidates if _matches_inferred(c, inferred_lifecycle, inferred_types)]
        return matching if matching else candidates
    return candidates


class RetrievalEngine:
    """Retrieve a bounded candidate set without loading whole-memory context.

    Candidate selection is query-driven: the hard gate (RAW exclusion,
    lifecycle filter, type filter) is applied first via
    ``self.storage.query()``, and only the notes that survive that gate are
    ever handed to ``generate_candidates()`` for lexical + entity ranking.
    See ``candidate_generation.py`` for the ranking contract and
    ``20_TESTS/regression/test_candidate_generation_call_path.py`` for an
    AST-level proof that the gate always runs first.
    """

    def __init__(self, storage_engine, cache=None):
        self.storage = storage_engine
        self.cache = cache

    def retrieve(
        self,
        classified_query: Dict[str, Any],
        principal=None,
        query_fp: str = None,
        disclosure_level: str = None,
        budget=None,
        offset: int = 0,
        query: Optional[str] = None,
        trace_sink: Optional[Dict[str, Any]] = None,
        classifier_filter_arm: Optional[str] = None,
    ) -> List[Dict[str, Any]]:
        intent = classified_query.get("intent")
        lifecycle = classified_query.get("lifecycle_filters", [])
        target_types = classified_query.get("target_types", [])
        lifecycle_source = classified_query.get("lifecycle_filters_source") or ("inferred" if lifecycle else "none")
        types_source = classified_query.get("target_types_source") or ("inferred" if target_types else "none")

        arm = classifier_filter_arm or CLASSIFIER_FILTER_ARM_HARD
        # What is actually passed to storage.query() as the hard exclusion.
        # C1 (default): unchanged -- the full classified filter, inferred or
        # explicit, is hard. C2/C3: only an EXPLICIT caller-supplied filter
        # stays hard; an INFERRED one is never excluded at the gate -- it is
        # softened afterward, on generate_candidates()'s output, never by
        # reintroducing anything the gate would have excluded.
        if arm == CLASSIFIER_FILTER_ARM_HARD:
            hard_lifecycle, hard_types = lifecycle, target_types
        else:
            hard_lifecycle = lifecycle if lifecycle_source == "explicit" else []
            hard_types = target_types if types_source == "explicit" else []
        inferred_lifecycle = lifecycle if (arm != CLASSIFIER_FILTER_ARM_HARD and lifecycle_source == "inferred") else []
        inferred_types = target_types if (arm != CLASSIFIER_FILTER_ARM_HARD and types_source == "inferred") else []

        if (
            self.cache
            and principal is not None
            and query_fp is not None
            and disclosure_level is not None
            and budget is not None
            and offset == 0
        ):
            # Keyed on the EFFECTIVE hard filter and the arm, not the raw
            # classified values -- two different arms softening the same
            # inferred filter differently must never collide in cache, and
            # neither may an arm change silently reuse another arm's entry.
            cached = self.cache.get(principal, query_fp, hard_lifecycle, hard_types, disclosure_level,
                                     classifier_filter_arm=arm)
            if cached is not None:
                # Never let stale/oversized cache entries bypass the current budget.
                if budget.serialized_size(cached) <= budget.soft_limit_bytes:
                    if trace_sink is not None:
                        trace_sink.update({
                            "source": "cache",
                            "query": query or "",
                            "candidate_limit": None,
                            "candidates_considered": None,
                            "per_generator": {},
                            "fused_ranking": [],
                        })
                    return list(cached)[:budget.max_notes]

        # HARD GATE: RAW exclusion is unconditional inside storage.query()
        # itself (see StorageEngine.query / FileStorageEngine.query),
        # regardless of hard_lifecycle/hard_types. Everything below this
        # line only reorders and truncates `results` -- it never re-queries
        # storage and never unions in notes from any other source, so
        # nothing this gate would have excluded can re-enter through
        # candidate ranking or through the classifier-filter-arm softening
        # below (which only ever narrows or reorders `candidates`, never
        # reintroduces a note absent from this exact `results`).
        results = self.storage.query(intent=intent, lifecycle=hard_lifecycle, types=hard_types)

        if "max_notes" in classified_query:
            # Legacy raw-truncation contract for direct engine callers that
            # bypass ranking entirely (see test_retrieval_engine_respects_max_notes).
            # Deliberately left untouched: MemoryController.search() never
            # sets `max_notes`, so this branch is unreachable from the
            # production query path this task changes.
            sliced = results[: int(classified_query["max_notes"])]
            if trace_sink is not None:
                trace_sink.update({
                    "source": "max_notes",
                    "query": query or "",
                    "candidate_limit": int(classified_query["max_notes"]),
                    "candidates_considered": len(results),
                    "per_generator": {},
                    "fused_ranking": [],
                })
            return sliced

        requested_limit = int(classified_query.get("candidate_limit", DEFAULT_CANDIDATE_LIMIT))
        ceiling = max(budget.max_notes * 4, budget.max_notes) if budget else DEFAULT_CANDIDATE_LIMIT
        candidate_limit = max(1, min(requested_limit, ceiling))

        # Fail closed, no silent fallback: generate_candidates() is a pure
        # function over the already-gated `results` list. If it (or the
        # BM25/tokenize/entities primitives it reuses) raises, that exception
        # propagates unchanged -- there is no except-and-degrade-to-head-N
        # here. See CandidateGenerationError and
        # test_candidate_generation_fails_closed.
        candidates, trace = generate_candidates(query or "", results, candidate_limit)
        if trace_sink is not None:
            trace_sink.update({"source": "generated", **trace.to_dict()})
            trace_sink["classifier_filter_arm"] = arm

        # r025 WP-9 Phase B: soften the classifier's inferred filter, on
        # candidates (generate_candidates()'s output) -- never on `results`
        # itself, so the gate above stays exactly what the AST proof in
        # test_candidate_generation_call_path.py verifies. `len(results)` is
        # the already-gated (RAW-excluded, explicit-filters-applied) pool
        # size, known for free -- no second storage.query() call.
        candidates = _apply_classifier_filter_arm(
            arm, candidates, len(results), inferred_lifecycle, inferred_types, candidate_limit,
        )
        results = candidates

        if self.cache and principal is not None and query_fp is not None and disclosure_level is not None and offset == 0:
            cache_limit = budget.max_notes if budget is not None else 5
            self.cache.set(results[:cache_limit], principal, query_fp, hard_lifecycle, hard_types, disclosure_level,
                           classifier_filter_arm=arm, events=["memory_updated"])  # type: ignore

        return results
