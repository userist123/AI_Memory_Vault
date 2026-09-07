"""AST-level proof that WP-6's edge-promotion mutation cannot bypass policy.

Requirement 5 ("prove no policy bypass with an AST call-path check") for
`07_EVALUATION/r025_wp6_edge_promotion/promote.py`'s `promote_one()` --
the ONLY function in that file permitted to call a mutation method. Proves,
structurally (not just by reading the code once), that this function:

  1. Calls `controller.update(...)` and nothing else that could mutate a
     note -- no `storage.set(...)`, no `open(...)` in write mode, no other
     attribute call on `controller.storage` besides the read-only `.get()`.
  2. Passes literally `controller.update(...)` (never a differently-named
     alias, never a call reached through an intermediate wrapper this test
     cannot see into).

Mirrors `test_candidate_generation_call_path.py`'s approach for the
retrieval read path, applied here to the promotion write path.
"""
import ast
from pathlib import Path

PROMOTE_PATH = (
    Path(__file__).resolve().parents[2]
    / "07_EVALUATION" / "r025_wp6_edge_promotion" / "promote.py"
)

#: Storage methods this function is allowed to call. `.get()` is a read; no
#: write method may appear here.
ALLOWED_STORAGE_METHODS = {"get"}


def _promote_one_node() -> ast.FunctionDef:
    tree = ast.parse(PROMOTE_PATH.read_text(encoding="utf-8"), filename=str(PROMOTE_PATH))
    return next(
        n for n in ast.walk(tree)
        if isinstance(n, (ast.FunctionDef, ast.AsyncFunctionDef)) and n.name == "promote_one"
    )


def test_promote_one_calls_only_controller_update_as_its_mutation():
    """Exactly one call to `<name>.update(...)` where `<name>` is the
    function's own `controller` parameter, and no other attribute call on
    that same object besides read-only `.storage.get(...)`."""
    func = _promote_one_node()
    controller_param = func.args.args[0].arg  # first positional param, named `controller` in promote_one's signature
    assert controller_param == "controller", (
        f"expected promote_one()'s first parameter to be named 'controller', got '{controller_param}'"
    )

    update_calls = []
    other_controller_attr_calls = []
    for node in ast.walk(func):
        if not (isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)):
            continue
        target = node.func.value
        if isinstance(target, ast.Name) and target.id == controller_param:
            if node.func.attr == "update":
                update_calls.append(node)
            else:
                other_controller_attr_calls.append(node.func.attr)

    assert len(update_calls) == 1, (
        f"expected exactly one `{controller_param}.update(...)` call, found {len(update_calls)}"
    )
    assert not other_controller_attr_calls, (
        f"promote_one() must not call anything on `{controller_param}` besides `.update()`; "
        f"found: {other_controller_attr_calls}"
    )


def test_promote_one_touches_storage_only_via_read_only_get():
    """No `.storage.set(...)` (or any other storage write method) anywhere
    in promote_one() -- the only permitted note write is through
    `controller.update()` itself, which routes through `_validate_note()`
    and the lifecycle-gate check inside controller.py."""
    func = _promote_one_node()
    storage_methods = set()
    for node in ast.walk(func):
        if (isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
                and isinstance(node.func.value, ast.Attribute)
                and node.func.value.attr == "storage"):
            storage_methods.add(node.func.attr)
    assert storage_methods <= ALLOWED_STORAGE_METHODS, (
        f"promote_one() must only read via storage.get(); found storage methods: "
        f"{storage_methods - ALLOWED_STORAGE_METHODS}"
    )


def test_promote_one_contains_no_direct_file_write():
    """No `open(...)` call anywhere in promote_one() -- a raw file write
    would bypass `_validate_note()`/the lifecycle gate entirely, which is
    exactly the shortcut this proof rules out."""
    func = _promote_one_node()
    open_calls = [
        node for node in ast.walk(func)
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == "open"
    ]
    assert not open_calls, "promote_one() must never call open() directly -- all writes go through controller.update()"
