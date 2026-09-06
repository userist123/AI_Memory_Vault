"""AST-level proof that candidate generation cannot bypass the hard gate.

Behavioral tests (test_candidate_generation.py) prove that excluded notes
don't show up in results *for the cases we thought to construct*. This file
instead parses RetrievalEngine.retrieve()'s actual source and proves,
structurally, that there is no code path by which it could:

  1. Source notes from anywhere other than ``self.storage.query()`` -- the
     single call point where RAW exclusion, lifecycle filtering, and type
     filtering are enforced (see StorageEngine.query / FileStorageEngine.query).
  2. Call ``generate_candidates()`` with anything other than exactly the
     variable that call was assigned to, unmodified, and only after that
     call has run.

This is deliberately AST-based rather than grep-based: grep can be fooled by
comments, docstrings, or coincidental substring matches, and can't verify
*data flow* (which variable a value came from) or *ordering* (that the gate
runs before ranking). Parsing the function body gives both.
"""
import ast
from pathlib import Path

import pytest

RETRIEVAL_PATH = (
    Path(__file__).resolve().parents[2]
    / "03_IMPLEMENTATION" / "packages" / "retrieval" / "context" / "retrieval.py"
)


def _retrieve_function_node() -> ast.FunctionDef:
    tree = ast.parse(RETRIEVAL_PATH.read_text(encoding="utf-8"), filename=str(RETRIEVAL_PATH))
    class_node = next(
        n for n in ast.walk(tree)
        if isinstance(n, ast.ClassDef) and n.name == "RetrievalEngine"
    )
    return next(
        n for n in class_node.body
        if isinstance(n, (ast.FunctionDef, ast.AsyncFunctionDef)) and n.name == "retrieve"
    )


def _is_self_storage_call(node: ast.AST, attr: str = None) -> bool:
    if not (isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)):
        return False
    outer = node.func
    inner = outer.value
    is_self_storage = (
        isinstance(inner, ast.Attribute)
        and inner.attr == "storage"
        and isinstance(inner.value, ast.Name)
        and inner.value.id == "self"
    )
    if not is_self_storage:
        return False
    return attr is None or outer.attr == attr


def test_retrieve_sources_notes_exclusively_via_storage_query():
    """No call to self.storage.<anything but query> exists in retrieve().

    This rules out any path that re-fetches notes via a broader accessor
    (e.g. a hypothetical self.storage.all_notes() or self.storage.store)
    that would not have RAW/lifecycle/type filtering applied.
    """
    func = _retrieve_function_node()
    storage_methods = set()
    for node in ast.walk(func):
        if (isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
                and isinstance(node.func.value, ast.Attribute)
                and node.func.value.attr == "storage"
                and isinstance(node.func.value.value, ast.Name)
                and node.func.value.value.id == "self"):
            storage_methods.add(node.func.attr)
    assert storage_methods == {"query"}, (
        f"retrieve() must source notes exclusively via self.storage.query(); "
        f"found additional/unexpected storage access: {storage_methods - {'query'}}"
    )


def test_generate_candidates_called_with_unmodified_gated_variable_after_the_gate():
    """The variable assigned from self.storage.query(...) is exactly the
    variable passed to generate_candidates(...), it is never reassigned in
    between, and the call happens strictly after the gate."""
    func = _retrieve_function_node()

    gate_assignments = [
        node for node in ast.walk(func)
        if isinstance(node, ast.Assign)
        and len(node.targets) == 1
        and isinstance(node.targets[0], ast.Name)
        and _is_self_storage_call(node.value, attr="query")
    ]
    assert len(gate_assignments) == 1, (
        f"expected exactly one `<name> = self.storage.query(...)` assignment, "
        f"found {len(gate_assignments)}"
    )
    gate_node = gate_assignments[0]
    gate_target = gate_node.targets[0].id
    gate_line = gate_node.lineno

    candidate_calls = [
        node for node in ast.walk(func)
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
        and node.func.id == "generate_candidates"
    ]
    assert len(candidate_calls) == 1, (
        f"expected exactly one generate_candidates(...) call, found {len(candidate_calls)}"
    )
    call_node = candidate_calls[0]
    assert call_node.lineno > gate_line, (
        "generate_candidates() must run strictly after the storage.query() hard gate"
    )

    positional_names = [a.id for a in call_node.args if isinstance(a, ast.Name)]
    keyword_names = [kw.value.id for kw in call_node.keywords if isinstance(kw.value, ast.Name)]
    assert gate_target in positional_names or gate_target in keyword_names, (
        f"generate_candidates() must be called with the exact gated variable "
        f"'{gate_target}' returned by storage.query(); got args={positional_names}, "
        f"kwargs={keyword_names}"
    )

    # No reassignment of the gate variable between the gate and the call --
    # this rules out e.g. `results = results + extra_notes` or
    # `results = self.storage.all_notes()` sneaking in between.
    for node in ast.walk(func):
        if (isinstance(node, ast.Assign) and len(node.targets) == 1
                and isinstance(node.targets[0], ast.Name)
                and node.targets[0].id == gate_target
                and gate_line < node.lineno < call_node.lineno):
            pytest.fail(
                f"line {node.lineno}: '{gate_target}' is reassigned between the "
                "storage.query() hard gate and the generate_candidates() call -- "
                "this could bypass the filter by substituting a different note set"
            )


def test_max_notes_legacy_branch_still_slices_the_gated_variable_directly():
    """The pre-existing `max_notes` early-return branch (unreachable from
    MemoryController.search(), used only by direct engine callers) must
    still slice the SAME gated variable -- it must not have been changed to
    source notes from anywhere else either."""
    func = _retrieve_function_node()

    gate_assignments = [
        node for node in ast.walk(func)
        if isinstance(node, ast.Assign)
        and len(node.targets) == 1
        and isinstance(node.targets[0], ast.Name)
        and _is_self_storage_call(node.value, attr="query")
    ]
    gate_target = gate_assignments[0].targets[0].id

    if_nodes = [
        node for node in ast.walk(func)
        if isinstance(node, ast.If)
        and isinstance(node.test, ast.Compare)
        and any(isinstance(c, ast.Constant) and c.value == "max_notes" for c in [node.test.left])
    ]
    assert if_nodes, "expected an `if \"max_notes\" in classified_query:` branch"
    branch = if_nodes[0]

    subscripts = [
        node for node in ast.walk(branch)
        if isinstance(node, ast.Subscript) and isinstance(node.value, ast.Name)
    ]
    assert any(s.value.id == gate_target for s in subscripts), (
        f"the max_notes branch must slice '{gate_target}' (the gated variable), "
        "not source notes from anywhere else"
    )
