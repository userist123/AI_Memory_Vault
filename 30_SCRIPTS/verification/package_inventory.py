#!/usr/bin/env python3
"""AST inventory of the Python packages of the repository (WP-01, F0).

Read-only: walks the tree, parses every ``.py`` file with ``ast`` and writes a
JSON and a Markdown report. Standard library only. Anything the AST cannot
resolve (dynamic imports, ``sys.path`` / ``__path__`` manipulation) is listed
as UNKNOWN and never guessed.

    python 30_SCRIPTS/verification/package_inventory.py --root . \
        --out-json <path> --out-md <path>

Units discovered (nothing hardcoded by name):
  * every directory under ``03_IMPLEMENTATION/packages/``
  * every directory under ``03_IMPLEMENTATION/products/``
  * root-level directories whose name is an identifier and that contain .py files
Imports are resolved with the ``pythonpath`` order of ``pytest.ini`` (first wins).
"""
from __future__ import annotations

import argparse
import ast
import json
import re
import sys
import warnings
from collections import defaultdict
from pathlib import Path

PRUNE_DIRS = {
    ".git", "node_modules", "80_ARCHIVE", "__pycache__", ".venv", "venv",
    ".pytest_cache", ".mypy_cache", "site-packages", ".pnpm-store",
}
DEFAULT_PYTHONPATH = [".", "03_IMPLEMENTATION/packages", "03_IMPLEMENTATION"]
UNIT_PARENTS = (("packages", "03_IMPLEMENTATION/packages"), ("products", "03_IMPLEMENTATION/products"))
TEXT_EXT = {".py", ".yml", ".yaml", ".md", ".toml", ".json", ".txt", ".sh", ".ini", ".cfg", ".ps1", ".bat", ".cmd"}
TEXT_NAMES = {"Makefile", "justfile"}
TESTDIR_TOP = ["20_TESTS", "02_PRODUCT", "07_EVALUATION", "03_IMPLEMENTATION", ".agents"]
REQ_FILES = ["requirements.txt", "requirements-memory-v6.txt"]
SELF_EXCLUDED_REF = ("07_EVALUATION/reports/modernization/", "30_SCRIPTS/verification/package_inventory.py",
                     "04_CONFIG/architecture_layers.json",
                     "20_TESTS/test_package_inventory.py", "00_GOVERNANCE/coordination/tasks/todo-claude-modernization")


# ----------------------------------------------------------------------------- walking
def walk_files(root: Path) -> list[str]:
    out: list[str] = []
    stack = [root]
    while stack:
        d = stack.pop()
        try:
            entries = list(d.iterdir())
        except OSError:
            continue
        for e in entries:
            if e.is_dir():
                if e.name in PRUNE_DIRS or e.is_symlink():
                    continue
                stack.append(e)
            elif e.is_file():
                out.append(e.relative_to(root).as_posix())
    return sorted(out)


def is_test_path(rel: str) -> bool:
    p = "/" + rel
    base = p.rsplit("/", 1)[-1]
    return "/20_TESTS/" in p or "/tests/" in p or base.startswith("test_") or base == "conftest.py"


def is_bench_path(rel: str) -> bool:
    return "/benchmarks/" in "/" + rel


def loc_of(text: str) -> int:
    return sum(1 for ln in text.splitlines() if ln.strip() and not ln.strip().startswith("#"))


# ----------------------------------------------------------------------------- units
def discover_units(root: Path, pyfiles: list[str]) -> dict[str, dict]:
    units: dict[str, dict] = {}
    for kind, parent in UNIT_PARENTS:
        pdir = root / parent
        if pdir.is_dir():
            for d in sorted(pdir.iterdir()):
                if d.is_dir() and d.name not in PRUNE_DIRS and not d.name.startswith("."):
                    units[f"{kind}/{d.name}"] = {"id": f"{kind}/{d.name}", "kind": kind, "name": d.name,
                                                 "dir": f"{parent}/{d.name}"}
    top_with_py = {f.split("/", 1)[0] for f in pyfiles if "/" in f}
    for d in sorted(root.iterdir()):
        if (d.is_dir() and d.name.isidentifier() and d.name in top_with_py
                and d.name not in PRUNE_DIRS and not d.name.startswith(".")):
            units[f"root/{d.name}"] = {"id": f"root/{d.name}", "kind": "root", "name": d.name, "dir": d.name}
    return units


def unit_of_file(rel: str, units_by_dir_len: list[dict]) -> str | None:
    for u in units_by_dir_len:
        if rel.startswith(u["dir"] + "/"):
            return u["id"]
    return None


def read_pythonpath(root: Path) -> list[str]:
    ini = root / "pytest.ini"
    if ini.is_file():
        m = re.search(r"^pythonpath\s*=\s*(.+)$", ini.read_text("utf-8", "replace"), re.M)
        if m:
            return [p.strip("/") if p.strip() != "." else "." for p in m.group(1).split()]
    return list(DEFAULT_PYTHONPATH)


def read_testpaths(root: Path) -> list[str]:
    ini = root / "pytest.ini"
    if ini.is_file():
        m = re.search(r"^testpaths\s*=\s*(.+)$", ini.read_text("utf-8", "replace"), re.M)
        if m:
            return m.group(1).split()
    return []


# ----------------------------------------------------------------------------- resolver
class Resolver:
    def __init__(self, root: Path, units: dict[str, dict], pythonpath: list[str]):
        self.root = root
        self.units = units
        self.pythonpath = pythonpath
        self.by_dir = {u["dir"]: u["id"] for u in units.values()}
        self.shims: dict[str, dict] = {}
        self._exists: dict[str, bool] = {}
        self.collisions = self._find_collisions()
        self.stats: dict[str, dict[str, int]] = defaultdict(lambda: defaultdict(int))
        self.count_stats = True

    def exists(self, rel: str) -> bool:
        if rel not in self._exists:
            self._exists[rel] = (self.root / rel).exists()
        return self._exists[rel]

    def _join(self, base: str, *parts: str) -> str:
        return "/".join(p for p in (base if base != "." else "", *parts) if p)

    def candidates(self, name: str) -> list[str]:
        out = []
        for p in self.pythonpath:
            uid = self.by_dir.get(self._join(p, name))
            if uid:
                out.append(uid)
        return out

    def _find_collisions(self) -> dict[str, list[str]]:
        names = {u["name"] for u in self.units.values()}
        res = {}
        for n in sorted(names):
            c = self.candidates(n)
            if len(c) > 1:
                res[n] = c
        return res

    def _elem_targets(self, uid: str, ups: int, comps: tuple[str, ...]) -> tuple[list[str], bool, str] | None:
        """Units named by one evaluated ``__path__`` element (``(ups, comps)`` from ``analyze_file``).

        Returns ``(targets, expanded, directory)``: ``"self"`` = the shim's own directory, ``expanded`` = the
        element is a directory that holds several unit directories (``path_expression``), ``directory`` = the
        directory the element names (may be below the unit directory). ``None`` = not interpretable.
        """
        if ups < 1:
            return None
        own = self.units[uid]["dir"]
        base = own
        for _ in range(ups - 1):
            base = base.rsplit("/", 1)[0] if "/" in base else ""
        d = "/".join(x for x in (base, *comps) if x)
        if d == own:
            return ["self"], False, d
        if d in self.by_dir:
            return [self.by_dir[d]], False, d
        anc = [u for u in self.units.values() if d.startswith(u["dir"] + "/")]
        if anc:
            best = max(anc, key=lambda u: len(u["dir"]))["id"]
            return (["self"] if best == uid else [best]), False, d
        kids = sorted(u["id"] for u in self.units.values()
                      if u["id"] != uid and (u["dir"].rsplit("/", 1)[0] if "/" in u["dir"] else "") == d)
        return (kids, True, d) if kids else None

    def set_shim(self, uid: str, consts: set[str], has_path: bool, path_ups: list[int] | None = None,
                 path_elems: list | None = None, resolves: bool = True) -> None:
        """Record a ``__path__`` shim and which units it points to, IN THE ORDER OF ``__path__``.

        ``path_elems`` = the evaluated ``__path__`` elements (``(ups, comps)`` of ``Path(__file__)``
        expressions, see ``analyze_file``); targets are then exact and ordered, ``"self"`` marks the shim's
        own directory. Without it (not interpretable) the legacy heuristics apply: ``path_ups`` (bare
        parent-directory expressions serve every sibling directly under that directory, ``path_expression``)
        or string constants (sorted; the order is NOT the ``__path__`` order).
        ``shim_target_source``: ``path_eval`` / ``path_expression`` = the symbolic evaluator (exact, ordered);
        ``legacy_fallback`` = the string-constant heuristic (sorted, NOT the ``__path__`` order; never trusted by
        the layer check).
        ``resolves=False`` (only the ``also_unit`` aliases of the layer config) records the targets (``shim_targets``) but the shim does not change how imports of
        its name are resolved (a unit whose own name is shadowed by another package earlier on the pythonpath).
        """
        if not has_path:
            return
        if path_elems:
            targets: list[str] = []
            dirs: dict[str, str] = {}
            expanded = False
            for ups, comps in path_elems:
                r = self._elem_targets(uid, ups, comps)
                if r is None:
                    targets = []
                    break
                expanded = expanded or r[1]
                for t in r[0]:
                    if t not in targets:
                        targets.append(t)
                        if not r[1]:
                            dirs[t] = r[2]
            if targets:
                self.shims[uid] = {"targets": targets, "resolves": resolves, "dirs": dirs,
                                   "source": "path_expression" if expanded else "path_eval"}
                return
        if path_ups:
            targets = []
            for ups in path_ups:
                base = self.units[uid]["dir"]
                for _ in range(ups - 1):
                    base = base.rsplit("/", 1)[0] if "/" in base else ""
                for u in sorted(self.units.values(), key=lambda x: x["id"]):
                    parent = u["dir"].rsplit("/", 1)[0] if "/" in u["dir"] else ""
                    if parent == base and u["id"] != uid and u["id"] not in targets:
                        targets.append(u["id"])
            self.shims[uid] = {"targets": targets, "source": "path_expression", "resolves": resolves}
            return
        own_parent = self.units[uid]["dir"].rsplit("/", 1)[0].rsplit("/", 1)[-1] if "/" in self.units[uid]["dir"] else ""
        parent_names = {u["dir"].rsplit("/", 1)[0].rsplit("/", 1)[-1] for u in self.units.values() if "/" in u["dir"]}
        hints = [p for p in sorted(parent_names) if p in consts] or [own_parent]
        targets = []
        for c in sorted(consts):
            for h in hints:
                for u in self.units.values():
                    if u["name"] == c and u["id"] != uid and u["dir"].rsplit("/", 1)[0].endswith(h) and u["id"] not in targets:
                        targets.append(u["id"])
        self.shims[uid] = {"targets": targets, "source": "legacy_fallback", "resolves": resolves}

    def _served_by(self, unit_id: str, sub: list[str]) -> bool:
        d = self.units[unit_id]["dir"]
        return self.exists(f"{d}/{sub[0]}.py") or self.exists(f"{d}/{sub[0]}")

    def _reexport_unit(self, uid: str, mod: str) -> str | None:
        """``<shim dir>/<mod>.py`` re-exports a sibling package (``from [packages.]X.<m> import *``)."""
        f = self.root / self.units[uid]["dir"] / f"{mod}.py"
        if not f.is_file():
            return None
        try:
            text = f.read_text("utf-8", "replace")
        except OSError:
            return None
        m = re.search(r"^from (?:packages\.)?(\w+)\.", text, re.M)
        cand = f"packages/{m.group(1)}" if m else None
        return cand if cand in self.units and cand != uid else None

    def resolve(self, parts: list[str]) -> set[str]:
        """Units reached by ``import a.b.c`` (``parts`` = ['a','b','c']); empty = not a repo unit.

        Consumer view: a facade import credits the facade AND the unit that serves the submodule.
        For dependency edges use ``resolve_full(...)["edges"]``.
        """
        return self.resolve_full(parts)["credits"]

    def resolve_full(self, parts: list[str]) -> dict:
        """``credits`` (consumers), ``edges`` (dependency targets), ``facade`` and ``state``.

        ``state``: ``plain`` (no facade involved), ``bare`` (facade without submodule), ``served`` (submodule
        resolved to another unit; the edge goes ONLY to that unit) or ``unresolved`` (facade submodule that
        no ``__path__`` entry serves: the edge stays on the facade and is reported).
        """
        for p in self.pythonpath:
            base = self._join(p, parts[0])
            uid = self.by_dir.get(base)
            sub = parts[1:]
            if uid is None and len(parts) > 1:
                uid = self.by_dir.get(self._join(p, parts[0], parts[1]))
                sub = parts[2:]
                if uid is not None and parts[0] in self.units[uid]["name"]:
                    uid = None
            if uid is None:
                continue
            name = self.units[uid]["name"]
            credits = {uid}
            served = uid
            state = "plain"
            shim = self.shims.get(uid)
            if shim and not shim.get("resolves", True):
                shim = None
            if shim:
                state = "bare"
            if shim and sub:
                targets = shim["targets"]
                served = None
                if shim.get("source") == "path_expression":
                    # __path__ is the parent directory: ``shim.X`` is the sibling package X
                    for t in targets:
                        if t != "self" and self.units[t]["name"] == sub[0]:
                            served = t
                            break
                dirs = shim.get("dirs")
                for t in ([] if served else targets):
                    tid = uid if t == "self" else t
                    if dirs is not None and t in dirs:  # the exact directory of the __path__ element
                        hit = self.exists(f"{dirs[t]}/{sub[0]}.py") or self.exists(f"{dirs[t]}/{sub[0]}")
                    else:
                        hit = self._served_by(tid, sub)
                    if hit:
                        served = tid
                        break
                if served is None and dirs is None and len(targets) == 1 and targets[0] != "self":
                    served = targets[0]  # legacy (uninterpreted __path__): a single target serves everything
                if served == uid:  # an own module of the facade: follow its re-export to the sibling package
                    served = self._reexport_unit(uid, sub[0])
                state = "served" if served else "unresolved"
                if served:
                    credits.add(served)
            edges = {served} if state == "served" else {uid}
            if name in self.collisions and sub and self.count_stats:
                key = served or "UNSERVED"
                if served is None:
                    for loser in self.collisions[name][1:]:
                        if self._served_by(loser, sub) or loser in self.shims:
                            key = f"SHADOWED:{loser}"
                            break
                self.stats[name][key] += 1
            return {"credits": credits, "edges": edges, "facade": uid if shim else None, "state": state}
        return {"credits": set(), "edges": set(), "facade": None, "state": "plain"}


# ----------------------------------------------------------------------------- per-file analysis
def _is_name(node: ast.AST, name: str) -> bool:
    return isinstance(node, ast.Name) and node.id == name


def _is_sys_path(node: ast.AST) -> bool:
    return isinstance(node, ast.Attribute) and node.attr == "path" and _is_name(node.value, "sys")


def _is_dunder_path(node: ast.AST) -> bool:
    return _is_name(node, "__path__") or (isinstance(node, ast.Attribute) and node.attr == "__path__")


def _file_ups(node: ast.AST, varmap: dict[str, int]) -> int | None:
    """Levels above ``__file__`` for a bare ``Path(__file__)[.resolve()].parent/.parents[N]`` expression."""
    if isinstance(node, ast.Name):
        return varmap.get(node.id)
    if isinstance(node, ast.Call):
        f = node.func
        if isinstance(f, ast.Name) and f.id == "str" and len(node.args) == 1 and not node.keywords:
            return _file_ups(node.args[0], varmap)
        if isinstance(f, ast.Attribute) and f.attr in ("resolve", "absolute") and not node.args:
            return _file_ups(f.value, varmap)
        name = f.id if isinstance(f, ast.Name) else (f.attr if isinstance(f, ast.Attribute) else "")
        if name == "Path" and len(node.args) == 1 and _is_name(node.args[0], "__file__"):
            return 0
        return None
    if isinstance(node, ast.Attribute) and node.attr == "parent":
        base = _file_ups(node.value, varmap)
        return None if base is None else base + 1
    if (isinstance(node, ast.Subscript) and isinstance(node.value, ast.Attribute) and node.value.attr == "parents"
            and isinstance(node.slice, ast.Constant) and isinstance(node.slice.value, int) and node.slice.value >= 0):
        base = _file_ups(node.value.value, varmap)
        return None if base is None else base + node.slice.value + 1
    return None


def _pexpr(node: ast.AST, env: dict, loop: dict) -> tuple[int, tuple[str, ...]] | None:
    """Symbolic ``Path(__file__)`` expression -> ``(ups, comps)``: ``ups`` levels above ``__file__`` (1 = the
    directory of the file), then the path components appended below it. ``None`` = not interpretable."""
    if isinstance(node, ast.Name):
        return env.get(node.id)
    if isinstance(node, ast.Call):
        f = node.func
        if isinstance(f, ast.Attribute) and f.attr in ("resolve", "absolute") and not node.args:
            return _pexpr(f.value, env, loop)
        name = f.id if isinstance(f, ast.Name) else ""
        if name in ("str", "Path", "PurePath") and len(node.args) == 1 and not node.keywords:
            a = node.args[0]
            if name != "str" and _is_name(a, "__file__"):
                return (0, ())
            return _pexpr(a, env, loop)
        return None
    if isinstance(node, ast.Attribute) and node.attr == "parent":
        base = _pexpr(node.value, env, loop)
        if base is None:
            return None
        return (base[0], base[1][:-1]) if base[1] else (base[0] + 1, ())
    if (isinstance(node, ast.Subscript) and isinstance(node.value, ast.Attribute) and node.value.attr == "parents"
            and isinstance(node.slice, ast.Constant) and isinstance(node.slice.value, int) and node.slice.value >= 0):
        base = _pexpr(node.value.value, env, loop)
        return None if base is None or base[1] else (base[0] + node.slice.value + 1, ())
    if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Div):
        base = _pexpr(node.left, env, loop)
        if base is None:
            return None
        r = node.right
        if isinstance(r, ast.Constant) and isinstance(r.value, str):
            return (base[0], base[1] + tuple(x for x in r.value.split("/") if x))
        if isinstance(r, ast.Name) and r.id in loop:
            return (base[0], base[1] + (loop[r.id],))
    return None


def _flatten_path_value(v: ast.AST, env: dict) -> list:
    """Elements of a ``__path__`` value: list/tuple, ``a + b`` and a one-generator comprehension over constants."""
    if isinstance(v, (ast.List, ast.Tuple)):
        return [_pexpr(e, env, {}) for e in v.elts]
    if isinstance(v, ast.BinOp) and isinstance(v.op, ast.Add):
        return _flatten_path_value(v.left, env) + _flatten_path_value(v.right, env)
    if (isinstance(v, ast.ListComp) and len(v.generators) == 1 and not v.generators[0].ifs
            and isinstance(v.generators[0].target, ast.Name)
            and isinstance(v.generators[0].iter, (ast.List, ast.Tuple))
            and all(isinstance(c, ast.Constant) and isinstance(c.value, str) for c in v.generators[0].iter.elts)):
        g = v.generators[0]
        return [_pexpr(v.elt, env, {g.target.id: c.value}) for c in g.iter.elts]
    return [_pexpr(v, env, {})]


def collect_path_elems(tree: ast.Module) -> list | None:
    """The ordered ``__path__`` of a package ``__init__`` as ``(ups, comps)`` elements, or ``None`` if it does not
    touch ``__path__`` or any element is not interpretable. ``__path__.append/extend`` without a prior assignment
    extends the default ``__path__`` (the package's own directory, ``(1, ())``)."""
    env: dict = {}
    state = {"elems": None, "ok": True}

    def need_init() -> None:
        if state["elems"] is None:
            state["elems"] = [(1, ())]

    def add(items: list) -> None:
        for it in items:
            if it is None:
                state["ok"] = False
            else:
                state["elems"].append(it)

    handled: set[int] = set()

    def walk(stmts: list) -> None:
        for st in stmts:
            if isinstance(st, ast.Assign):
                if any(_is_name(t, "__path__") for t in st.targets):
                    handled.add(id(st))
                    state["elems"] = []
                    add(_flatten_path_value(st.value, env))
                elif len(st.targets) == 1 and isinstance(st.targets[0], ast.Name):
                    v = _pexpr(st.value, env, {})
                    if v is not None:
                        env[st.targets[0].id] = v
            elif (isinstance(st, ast.Expr) and isinstance(st.value, ast.Call)
                  and isinstance(st.value.func, ast.Attribute) and _is_name(st.value.func.value, "__path__")
                  and st.value.func.attr in ("append", "extend") and len(st.value.args) == 1):
                handled.add(id(st.value))
                need_init()
                if st.value.func.attr == "append":
                    add([_pexpr(st.value.args[0], env, {})])
                else:
                    add(_flatten_path_value(st.value.args[0], env))
            elif isinstance(st, ast.If):
                walk(st.body)
                walk(st.orelse)
            elif isinstance(st, ast.Try):
                walk(st.body)

    walk(tree.body)
    # fail closed: any other way of moving ``__path__`` (insert, +=, subscript, del, attribute target, a function
    # body, a handler, ...) makes the whole list uninterpretable instead of being ignored
    for n in ast.walk(tree):
        if isinstance(n, (ast.Assign, ast.AugAssign, ast.AnnAssign)) and id(n) not in handled:
            tgs = n.targets if isinstance(n, ast.Assign) else [n.target]
            if any(_is_dunder_path(t) or (isinstance(t, ast.Subscript) and _is_dunder_path(t.value)) for t in tgs):
                state["ok"] = False
        elif isinstance(n, ast.Delete):
            if any(_is_dunder_path(t) or (isinstance(t, ast.Subscript) and _is_dunder_path(t.value))
                   for t in n.targets):
                state["ok"] = False
        elif (isinstance(n, ast.Call) and id(n) not in handled and isinstance(n.func, ast.Attribute)
              and _is_dunder_path(n.func.value) and n.func.attr not in ("index", "count", "copy")):
            state["ok"] = False
    return state["elems"] if state["elems"] is not None and state["ok"] else None


def analyze_file(text: str) -> dict:
    """Return imports (list of part-lists + from-aliases), unknowns, main-guard, __all__, constants."""
    res = {"imports": [], "unknown": [], "has_main": False, "has_all": False, "consts": set(), "parse_error": None,
           "path_ups": [], "path_elems": None, "path_assigned": False}
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            tree = ast.parse(text)
    except (SyntaxError, ValueError, RecursionError) as exc:
        res["parse_error"] = type(exc).__name__
        return res
    res["path_elems"] = collect_path_elems(tree)
    res["path_assigned"] = any(isinstance(n, ast.Assign) and any(_is_name(t, "__path__") for t in n.targets)
                               for n in ast.walk(tree))
    varmap: dict[str, int] = {}
    for stmt in tree.body:
        if isinstance(stmt, ast.Assign) and any(_is_dunder_path(t) for t in stmt.targets):
            elts = stmt.value.elts if isinstance(stmt.value, (ast.List, ast.Tuple)) else [stmt.value]
            for e in elts:
                ups = _file_ups(e, varmap)
                if ups is not None and ups >= 1:
                    res["path_ups"].append(ups)
        elif isinstance(stmt, ast.Assign) and len(stmt.targets) == 1 and isinstance(stmt.targets[0], ast.Name):
            ups = _file_ups(stmt.value, varmap)
            if ups is not None and ups >= 1:
                varmap[stmt.targets[0].id] = ups
    fn_aliases: set[str] = set()   # names bound to importlib.import_module
    mod_aliases: set[str] = {"importlib"}
    for node in ast.walk(tree):
        if isinstance(node, ast.ImportFrom) and node.module == "importlib" and not node.level:
            for a in node.names:
                if a.name in ("import_module", "__import__"):
                    fn_aliases.add(a.asname or a.name)
        elif isinstance(node, ast.Import):
            for a in node.names:
                if a.name == "importlib" and a.asname:
                    mod_aliases.add(a.asname)
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for a in node.names:
                res["imports"].append(([a.name.split(".")], node.lineno, False))
        elif isinstance(node, ast.ImportFrom):
            if node.level:
                continue
            mod = (node.module or "").split(".") if node.module else []
            if not mod or mod == [""]:
                continue
            alts = [mod] + [mod + [a.name] for a in node.names if a.name != "*"]
            res["imports"].append((alts, node.lineno, True))
        elif isinstance(node, ast.If):
            t = node.test
            if (isinstance(t, ast.Compare) and _is_name(t.left, "__name__") and len(t.comparators) == 1
                    and isinstance(t.comparators[0], ast.Constant) and t.comparators[0].value == "__main__"):
                res["has_main"] = True
        elif isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign)):
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            for tg in targets:
                if _is_name(tg, "__all__"):
                    res["has_all"] = True
                if _is_sys_path(tg) or _is_dunder_path(tg):
                    res["unknown"].append({"line": node.lineno, "kind": "path_assign",
                                           "detail": "sys.path" if _is_sys_path(tg) else "__path__"})
                if isinstance(tg, ast.Subscript) and (_is_sys_path(tg.value) or _is_dunder_path(tg.value)):
                    res["unknown"].append({"line": node.lineno, "kind": "path_assign",
                                           "detail": "sys.path[...]" if _is_sys_path(tg.value) else "__path__[...]"})
        elif isinstance(node, ast.Delete):
            for tg in node.targets:
                if isinstance(tg, ast.Subscript) and _is_dunder_path(tg.value):
                    res["unknown"].append({"line": node.lineno, "kind": "path_assign", "detail": "__path__[...]"})
        elif isinstance(node, ast.Constant) and isinstance(node.value, str):
            res["consts"].add(node.value)
        if isinstance(node, ast.Call):
            f = node.func
            fname = f.id if isinstance(f, ast.Name) else (f.attr if isinstance(f, ast.Attribute) else "")
            if isinstance(f, ast.Attribute) and fname in {"insert", "append", "extend", "remove"} and \
                    (_is_sys_path(f.value) or _is_dunder_path(f.value)):
                res["unknown"].append({"line": node.lineno, "kind": "path_mutation",
                                       "detail": ("sys.path." if _is_sys_path(f.value) else "__path__.") + fname})
            elif (isinstance(f, ast.Name) and (fname in {"import_module", "__import__"} or fname in fn_aliases)) or (
                    isinstance(f, ast.Attribute) and fname in {"import_module", "__import__"}
                    and isinstance(f.value, ast.Name) and f.value.id in mod_aliases):
                arg = node.args[0] if node.args else None
                if isinstance(arg, ast.Constant) and isinstance(arg.value, str) and not arg.value.startswith("."):
                    res["imports"].append(([arg.value.split(".")], node.lineno, False))
                else:
                    res["unknown"].append({"line": node.lineno, "kind": "dynamic_import",
                                           "detail": "import_module" if fname in fn_aliases else fname})
            elif fname in {"spec_from_file_location", "run_module", "run_path", "extend_path"}:
                res["unknown"].append({"line": node.lineno, "kind": "file_or_module_loader", "detail": fname})
    return res


# ----------------------------------------------------------------------------- text references
def unit_patterns(u: dict, winner_names: set[str]) -> list[re.Pattern]:
    n = re.escape(u["name"])
    pats = []
    if u["kind"] in ("packages", "products"):
        pats.append(re.compile(rf"(?<!\w){u['kind']}[/\\]{n}(?!\w)"))
    else:
        pats.append(re.compile(rf"(?<![\w/\\.\-]){n}[/\\]"))
    if u["id"] in winner_names:
        pats.append(re.compile(rf"-m\s+{n}\."))
    return pats


def ci_config_files(root: Path, allfiles: list[str]) -> list[str]:
    out = []
    for f in allfiles:
        if f.startswith(SELF_EXCLUDED_REF):
            continue
        if (f.startswith(".github/workflows/") and f.count("/") == 2 and f.endswith((".yml", ".yaml"))) \
                or f in ("pytest.ini", "pyproject.toml") \
                or (f.startswith("04_CONFIG/") and f.count("/") == 1 and f.endswith(".json")):
            out.append(f)
    return out


def read_text(root: Path, rel: str, limit: int = 2_000_000) -> str:
    p = root / rel
    try:
        if p.stat().st_size > limit:
            return ""
        return p.read_text("utf-8", "replace")
    except OSError:
        return ""


# ----------------------------------------------------------------------------- requirements
def parse_requirements(text: str) -> dict[str, str]:
    out = {}
    for ln in text.splitlines():
        ln = ln.split("#", 1)[0].strip()
        if not ln or ln.startswith("-"):
            continue
        m = re.match(r"^([A-Za-z0-9_.\-]+)(\[[^\]]*\])?\s*(.*)$", ln)
        if m:
            out[m.group(1).lower().replace("_", "-")] = m.group(3).strip() or "(fără versiune)"
    return out


# ----------------------------------------------------------------------------- string imports
STR_TEXT_EXT = (".yml", ".yaml", ".toml", ".sh", ".ps1", ".bat", ".cmd", ".ini", ".cfg")
_MOD = r"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*"
_INTERP = (r"(?:(?<![\w.\-])python[\w.\-]*|(?<![\w.\-/])py(?=\s)|\$\{?PYTHON\}?|%PYTHON%)[\"']?"
           r"(?:\s+(?:-[XW]\s+\S+|-[A-Za-z0-9]+(?:\.\d+)?))*")
_PY_M = re.compile(rf"{_INTERP}\s+-m\s+({_MOD})")
_PY_C = re.compile(rf"{_INTERP}\s+-c\s+(.*)$")
_IMP = re.compile(rf"(?<![\w.])import\s+({_MOD}(?:\s*,\s*{_MOD})*)")
_FROM = re.compile(rf"(?<![\w.])from\s+({_MOD})\s+import\b")
_TOML_EP = re.compile(rf'"({_MOD}):[A-Za-z_]\w*"')
_TOML_PKG_START = re.compile(r"^\s*(include|packages)\s*=")
_TOML_PKG_ITEM = re.compile(r'"([A-Za-z_][\w.]*)\*?"')
_MODNAME = re.compile(rf"^{_MOD}$")


def _unit_for_module(res: Resolver, module: str) -> str | None:
    """The unit a string import depends on: the unit that serves the submodule of a facade (not the facade)."""
    edges = res.resolve_full(module.split("."))["edges"]
    return sorted(edges)[0] if edges else None


def find_string_imports(root: Path, allfiles: list[str], res: Resolver) -> list[dict]:
    """Imports that live in strings: ``python -m``, ``python -c``, pyproject entry points, argv lists."""
    out: list[dict] = []
    res.count_stats = False

    def add(f: str, line: int, kind: str, module: str) -> None:
        out.append({"file": f, "line": line, "kind": kind, "module": module,
                    "unit": _unit_for_module(res, module)})

    for f in allfiles:
        if f.startswith(SELF_EXCLUDED_REF):
            continue
        base = f.rsplit("/", 1)[-1]
        if f.endswith(".py"):
            text = read_text(root, f, limit=5_000_000)
            if '"-m"' not in text and "'-m'" not in text:
                continue
            try:
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore")
                    tree = ast.parse(text)
            except (SyntaxError, ValueError, RecursionError):
                continue
            for node in ast.walk(tree):
                seqs = []
                if isinstance(node, (ast.List, ast.Tuple)):
                    seqs.append(node.elts)
                elif isinstance(node, ast.Call):
                    seqs.append(node.args)
                for elts in seqs:
                    for i, e in enumerate(elts[:-1]):
                        nxt = elts[i + 1]
                        if (isinstance(e, ast.Constant) and e.value == "-m" and isinstance(nxt, ast.Constant)
                                and isinstance(nxt.value, str) and _MODNAME.match(nxt.value)):
                            add(f, nxt.lineno, "py_argv_m", nxt.value)
            continue
        if not (f.endswith(STR_TEXT_EXT) or base in TEXT_NAMES):
            continue
        text = read_text(root, f)
        if not text:
            continue
        in_pkg = False
        for no, ln in enumerate(text.splitlines(), 1):
            stripped = ln.strip()
            if (re.match(r"(?i)(rem(\s|$)|::)", stripped) if f.endswith((".bat", ".cmd"))
                    else stripped.startswith("#")):
                continue
            for m in _PY_M.finditer(ln):
                add(f, no, "python_m", m.group(1))
            mc = _PY_C.search(ln)
            if mc:
                code = mc.group(1)
                for m in _FROM.finditer(code):
                    add(f, no, "python_c", m.group(1))
                code = _FROM.sub(" ", code)  # the names after ``from x import`` are not modules
                for m in _IMP.finditer(code):
                    for mod in re.split(r"\s*,\s*", m.group(1)):
                        add(f, no, "python_c", mod)
            if base == "pyproject.toml":
                for m in _TOML_EP.finditer(ln):
                    add(f, no, "toml_entry", m.group(1))
                starts = bool(_TOML_PKG_START.match(ln))
                if starts:
                    in_pkg = True
                if in_pkg:
                    for m in _TOML_PKG_ITEM.finditer(ln.split("=", 1)[-1] if starts else ln):
                        add(f, no, "toml_entry", m.group(1))
                    if "]" in ln:
                        in_pkg = False
    res.count_stats = True
    return sorted(out, key=lambda r: (r["file"], r["line"], r["kind"], r["module"]))


def promoted_test_files(info: dict[str, dict], pyfiles: list[str], pythonpath: list[str]) -> set[str]:
    """Test-path files that a non-test file (transitively) imports: they are production code in effect."""
    test_set = {f for f in pyfiles if is_test_path(f)}
    if not test_set:
        return set()

    def targets(f: str) -> set[str]:
        out: set[str] = set()
        for alts, _line, _is_from in info[f]["imports"]:
            for parts in alts:
                for p in pythonpath:
                    for k in range(1, len(parts) + 1):
                        stem = "/".join(x for x in ("" if p == "." else p, *parts[:k]) if x)
                        for cand in (stem + ".py", stem + "/__init__.py"):
                            if cand in test_set:
                                out.add(cand)
        return out

    promoted: set[str] = set()
    frontier = [f for f in pyfiles if f not in test_set]
    seen = set(frontier)
    while frontier:
        for t in targets(frontier.pop()):
            promoted.add(t)
            if t not in seen:
                seen.add(t)
                frontier.append(t)
    return promoted


# ----------------------------------------------------------------------------- main build
def default_also_units(root: Path) -> set[str]:
    """``also_unit`` aliases of ``04_CONFIG/architecture_layers.json`` under ``root``.

    A missing or invalid config is reported on stderr (never silent): every alias then resolves
    through its ``__path__``, including ``also_unit`` ones such as packages/security.
    """
    cfg = Path(root) / "04_CONFIG" / "architecture_layers.json"
    try:
        data = json.loads(cfg.read_text("utf-8"))
        return {a for a, d in data.get("aliases", {}).items() if d.get("also_unit")}
    except (OSError, ValueError, AttributeError) as exc:
        print(f"package_inventory: WARNING no also_unit aliases ({cfg.name}: {type(exc).__name__}); "
              "also_unit facades resolve through __path__", file=sys.stderr)
        return set()


def build_inventory(root: Path, also_units: set[str] | None = None) -> dict:
    """``also_units``: units whose ``__path__`` extension must not change how their own name resolves
    (``None`` = read ``also_unit`` from the layer config under ``root``)."""
    also = set(also_units) if also_units is not None else default_also_units(root)
    root = root.resolve()
    allfiles = walk_files(root)
    pyfiles = [f for f in allfiles if f.endswith(".py")]
    units = discover_units(root, pyfiles)
    pythonpath = read_pythonpath(root)
    res = Resolver(root, units, pythonpath)
    by_len = sorted(units.values(), key=lambda u: -len(u["dir"]))

    # pass 1: parse every file, remember per-file info
    info: dict[str, dict] = {}
    texts_loc: dict[str, int] = {}
    for f in pyfiles:
        text = read_text(root, f, limit=5_000_000)
        info[f] = analyze_file(text)
        texts_loc[f] = loc_of(text)

    local_names = set()
    for f in pyfiles:
        parts = f.split("/")
        local_names.add(parts[-1][:-3])
        local_names.update(parts[:-1])

    # shims (a unit __init__.py that assigns __path__)
    for uid, u in units.items():
        init = f"{u['dir']}/__init__.py"
        if init not in info:
            continue
        fi = info[init]
        touches = any(x["detail"].startswith("__path__") for x in fi["unknown"])
        if touches or fi["path_elems"]:
            # any ``__path__`` move makes the unit a shim. Only an ``also_unit`` alias of the layer config keeps
            # resolving its own name as before (``packages/security``); every other shim resolves through it
            res.set_shim(uid, fi["consts"], True, fi["path_ups"], fi["path_elems"], resolves=uid not in also)

    promoted = promoted_test_files(info, pyfiles, pythonpath)
    string_refs = find_string_imports(root, allfiles, res)

    def effective_test(f: str) -> bool:
        return is_test_path(f) and f not in promoted

    # console_scripts
    console: dict[str, list[str]] = defaultdict(list)
    pp = root / "pyproject.toml"
    if pp.is_file():
        try:
            import tomllib
            data = tomllib.loads(pp.read_text("utf-8"))
            scripts = dict(data.get("project", {}).get("scripts", {}))
            scripts.update(data.get("tool", {}).get("poetry", {}).get("scripts", {}))
            for sname, target in sorted(scripts.items()):
                mod = str(target).split(":")[0].split(".")
                for uid in sorted(res.resolve(mod)):
                    console[uid].append(f"{sname} = {target}")
        except Exception:  # noqa: BLE001 - malformed pyproject is reported as no scripts
            pass

    stdlib = set(sys.stdlib_module_names)
    prod_cons: dict[str, set] = defaultdict(set)
    test_cons: dict[str, set] = defaultdict(set)
    bench_cons: dict[str, set] = defaultdict(set)
    out_edges: dict[str, dict[str, set]] = defaultdict(lambda: defaultdict(set))
    ext: dict[str, dict[str, int]] = defaultdict(lambda: defaultdict(int))
    local_unres: dict[str, set] = defaultdict(set)
    unknown: dict[str, list] = defaultdict(list)
    unknown_outside: list[dict] = []
    violations: list[dict] = []
    parse_errors: list[str] = []
    facade_unres: list[dict] = []
    per_unit_files: dict[str, list[str]] = defaultdict(list)

    for f in pyfiles:
        fi = info[f]
        src = unit_of_file(f, by_len)
        if src:
            per_unit_files[src].append(f)
        if fi["parse_error"]:
            parse_errors.append(f)
        for uk in fi["unknown"]:
            rec = {"file": f, **uk}
            if rec["detail"] == "__path__" and src in res.shims and f.endswith("__init__.py"):
                rec["detail"] = "__path__ shim -> " + ", ".join(res.shims[src]["targets"] or ["(neidentificat)"])
            (unknown[src] if src else unknown_outside).append(rec)
        seen_targets: set[str] = set()
        seen_edges: set[str] = set()
        for alts, line, is_from in fi["imports"]:
            credits: set[str] = set()
            edge_t: set[str] = set()
            results = [res.resolve_full(parts) for parts in alts]
            for r in results:
                credits |= r["credits"]
                edge_t |= r["edges"]
            # a facade import whose submodule resolved elsewhere drops the facade edge (bare / unresolved alts of
            # the same statement); a facade whose submodule resolves nowhere keeps the edge and is reported
            served_f = {r["facade"] for r in results if r["state"] == "served"}
            for r in results:
                if r["facade"] in served_f and r["state"] in ("bare", "unresolved"):
                    edge_t.discard(r["facade"])
            for parts, r in zip(alts, results):
                if r["state"] == "unresolved" and r["facade"] not in served_f and src:
                    facade_unres.append({"file": f, "line": line, "from_unit": src, "module": ".".join(parts),
                                         "facade": r["facade"], "test": effective_test(f)})
            if not credits:
                top = alts[0][0]
                if top in stdlib:
                    continue
                if top in local_names:
                    if src:
                        local_unres[src].add(top)
                    continue
                if src:
                    ext[src][top] += 1
                continue
            for t in sorted(credits):
                if t == src or t in seen_targets:
                    continue
                seen_targets.add(t)
                bucket = test_cons if is_test_path(f) else (bench_cons if is_bench_path(f) else prod_cons)
                bucket[t].add(f)
            for t in sorted(edge_t):
                if t == src or t in seen_edges:
                    continue
                seen_edges.add(t)
                if src:
                    out_edges[src][t].add(f)
                    if src.startswith("packages/") and t.startswith("products/"):
                        violations.append({"from_unit": src, "file": f, "line": line, "to_unit": t,
                                           "test_file": is_test_path(f)})

    # imports that live in strings, from non-test files inside a unit, are edges for the ratchet
    str_edges: dict[str, dict[str, set]] = defaultdict(lambda: defaultdict(set))
    for r in string_refs:
        if r["kind"] in ("py_argv_m", "python_c") and r["unit"] and not effective_test(r["file"]):
            s_unit = unit_of_file(r["file"], by_len)
            if s_unit and s_unit != r["unit"]:
                str_edges[s_unit][r["unit"]].add(r["file"])

    # external deps: restrict to units own files only when counting top-level 3rd-party names
    ci_files = ci_config_files(root, allfiles)
    ci_texts = {f: read_text(root, f) for f in ci_files}
    winner_names = {c[0] for c in (res.candidates(u["name"]) for u in units.values()) if c}

    unit_rows = []
    for uid in sorted(units):
        u = units[uid]
        files = sorted(per_unit_files.get(uid, []))
        init = f"{u['dir']}/__init__.py"
        mains = [f for f in files if info[f]["has_main"] and not f.endswith("/__main__.py")]
        dunder = [f for f in files if f.endswith("/__main__.py")]
        ci_refs: dict[str, int] = {}
        pats = unit_patterns(u, winner_names)
        for cf, ct in ci_texts.items():
            n = sum(len(p.findall(ct)) for p in pats)
            if n:
                ci_refs[cf] = n
        unit_rows.append({
            "id": uid, "kind": u["kind"], "name": u["name"], "dir": u["dir"],
            "py_files": len(files),
            "loc": sum(texts_loc[f] for f in files),
            "init_exists": init in info,
            "has_all": bool(init in info and info[init]["has_all"]),
            "shim_targets": res.shims.get(uid, {}).get("targets") if uid in res.shims else None,
            "shim_target_source": res.shims[uid]["source"] if uid in res.shims else None,
            "entry_points": {"main_guard": mains, "dunder_main": dunder, "console_scripts": console.get(uid, [])},
            "imports_out": {t: len(fs) for t, fs in sorted(out_edges[uid].items())},
            "imports_out_nontest": {t: n for t, n in sorted(
                (t, len({f for f in fs if not effective_test(f)} | str_edges[uid].get(t, set())))
                for t in set(out_edges[uid]) | set(str_edges[uid]) for fs in [out_edges[uid].get(t, set())]) if n},
            "dynamic_imports_nontest": sum(1 for x in unknown[uid]
                                           if x["kind"] == "dynamic_import" and not effective_test(x["file"])),
            "consumers_prod": sorted(prod_cons[uid]),
            "consumers_test": sorted(test_cons[uid]),
            "consumers_bench_excluded": sorted(bench_cons[uid]),
            "ci_config_refs": dict(sorted(ci_refs.items())),
            "external_deps": dict(sorted(ext[uid].items())),
            "local_unresolved_names": sorted(local_unres[uid]),
            "unknown": sorted(unknown[uid], key=lambda r: (r["file"], r["line"])),
            "import_name_winner": uid in winner_names,
        })

    # test counts per top-level dir
    tests_by_top: dict[str, int] = defaultdict(int)
    for f in allfiles:
        base = f.rsplit("/", 1)[-1]
        if base.startswith("test_") and base.endswith(".py"):
            tests_by_top[f.split("/", 1)[0] if "/" in f else "(rădăcină)"] += 1
    testpaths = read_testpaths(root)

    # requirements
    reqs = {n: parse_requirements(read_text(root, n)) for n in REQ_FILES if (root / n).is_file()}
    names = sorted({k for v in reqs.values() for k in v})
    req_rows = [{"name": n, **{fn: reqs[fn].get(n) for fn in reqs}} for n in names]
    refs: dict[str, list[str]] = {n: [] for n in REQ_FILES}
    ref_res = {
        "requirements.txt": re.compile(r"(?<![\w\-])requirements\.txt"),
        "requirements-memory-v6.txt": re.compile(r"requirements-memory-v6\.txt"),
    }
    for f in allfiles:
        base = f.rsplit("/", 1)[-1]
        ext_ = "." + base.rsplit(".", 1)[-1] if "." in base else ""
        if (ext_ not in TEXT_EXT and base not in TEXT_NAMES) or f.startswith(SELF_EXCLUDED_REF) or f in REQ_FILES:
            continue
        t = read_text(root, f)
        for rn, rx in ref_res.items():
            if rx.search(t):
                refs[rn].append(f)

    collisions = []
    for name, cands in res.collisions.items():
        mods = {}
        for c in cands:
            d = root / units[c]["dir"]
            mods[c] = sorted(p.stem if p.is_file() else p.name for p in d.iterdir()
                             if (p.is_file() and p.suffix == ".py" and p.stem != "__init__")
                             or (p.is_dir() and p.name not in PRUNE_DIRS)) if d.is_dir() else []
        collisions.append({"name": name, "candidates_in_pythonpath_order": cands, "winner": cands[0],
                           "shim_targets": {c: res.shims[c]["targets"] for c in cands if c in res.shims},
                           "shim_target_source": {c: res.shims[c]["source"] for c in cands if c in res.shims},
                           "modules": mods, "imports_by_served_unit": dict(sorted(res.stats[name].items()))})

    return {
        "meta": {
            "root": ".", "pythonpath_order": pythonpath, "testpaths": testpaths,
            "py_files_scanned": len(pyfiles), "pruned_dirs": sorted(PRUNE_DIRS),
            "test_paths_promoted_to_production": sorted(promoted), "parse_errors": sorted(parse_errors),
            "facade_unresolved": sorted(facade_unres, key=lambda r: (r["file"], r["line"], r["module"])),
            "rules": {
                "loc": "linii non-goale care nu încep cu #; docstring-urile se numără",
                "consumers_prod": "fișiere .py în afara oricărui director de test/benchmark și în afara modulului",
                "consumers_test": "fișiere sub 20_TESTS, /tests/, test_*.py sau conftest.py",
                "shim_credit": "consumatori: un import prin shim __path__ creditează fațada ȘI modulul servit",
                "shim_target_source": ("path_eval/path_expression = evaluatorul simbolic al __path__ (exact, în ordine); "
                                       "legacy_fallback = euristica pe constante-șir (sortat, nu ordinea __path__); "
                                       "un alias declarat care cade pe legacy_fallback pică în architecture_boundaries"),
                "facade_edges": ("muchii (imports_out*): un import prin fațadă creditează DOAR unitatea care servește "
                                 "submodulul (ordinea din __path__); dacă nu se rezolvă, muchia rămâne pe fațadă și "
                                 "apare în meta.facade_unresolved"),
            },
        },
        "units": unit_rows,
        "collisions": collisions,
        "string_import_refs": string_refs,
        "layer_violations_package_to_product": sorted(violations, key=lambda v: (v["file"], v["line"])),
        "unknown_outside_units": sorted(unknown_outside, key=lambda r: (r["file"], r["line"])),
        "tests_by_top_dir": dict(sorted(tests_by_top.items())),
        "tests_focus_dirs": {d: tests_by_top.get(d, 0) for d in TESTDIR_TOP},
        "requirements": {"files": list(reqs), "rows": req_rows, "referenced_by": {k: sorted(v) for k, v in refs.items()}},
    }


# ----------------------------------------------------------------------------- markdown
def _short(items: list[str], n: int = 6) -> str:
    items = list(items)
    return ", ".join(items[:n]) + (f" +{len(items) - n}" if len(items) > n else "") if items else "-"


def render_md(inv: dict) -> str:
    L: list[str] = []
    m = inv["meta"]
    L += ["# F0 — Inventar pachete (generat automat)", "",
          "Generat de `30_SCRIPTS/verification/package_inventory.py`; toate numerele provin din rularea scriptului.",
          f"Fișiere .py analizate: {m['py_files_scanned']}; ordinea `pythonpath`: `{' '.join(m['pythonpath_order'])}` "
          f"(primul câștigă); erori de parsare AST: {len(m['parse_errors'])}.", "",
          "Reguli: LOC = linii non-goale fără comentariu-linie. Consumator prod = fișier .py în afara "
          "20_TESTS, /tests/, /benchmarks/, test_*.py și în afara modulului. Consumator test = 20_TESTS, "
          "/tests/, test_*.py, conftest.py. Un import prin shim `__path__` creditează și modulul servit.", "",
          "## Tabel module", "",
          "| Modul | Fișiere | LOC | Entry points | Consumatori prod | Consumatori test | CI/config | Ext deps | UNKNOWN |",
          "|---|---|---|---|---|---|---|---|---|"]
    for u in inv["units"]:
        ep = u["entry_points"]
        eps = f"main:{len(ep['main_guard'])} __main__:{len(ep['dunder_main'])} scripts:{len(ep['console_scripts'])}"
        L.append(f"| `{u['id']}` | {u['py_files']} | {u['loc']} | {eps} | {len(u['consumers_prod'])} | "
                 f"{len(u['consumers_test'])} | {sum(u['ci_config_refs'].values())} ({len(u['ci_config_refs'])} f.) | "
                 f"{_short(list(u['external_deps']), 5)} | {len(u['unknown'])} |")
    L += ["", "## `__all__`, importuri ieșite și shim-uri", "",
          "| Modul | `__init__` | `__all__` | Importă (nr. fișiere) | Shim `__path__` către (sursa) |", "|---|---|---|---|---|"]
    for u in inv["units"]:
        outs = ", ".join(f"{k}({v})" for k, v in u["imports_out"].items())
        L.append(f"| `{u['id']}` | {'da' if u['init_exists'] else 'NU'} | {'da' if u['has_all'] else 'nu'} | "
                 f"{outs or '-'} | {(_short(u['shim_targets'] or [], 9) + ' (' + u['shim_target_source'] + ')') if u['shim_targets'] is not None else '-'} |")
    L += ["", "Note: fișierele și LOC includ testele aflate în directorul modulului (ex. `security/tests/test_*.py`). Nume locale nerezolvate (ex. module importate după manipulare `sys.path`) nu sunt numărate ca "
          "dependențe externe; ele sunt în JSON (`local_unresolved_names`).", ""]

    L += ["## Coliziunea `cognitive_core` (și alte nume duplicate)", ""]
    for c in inv["collisions"]:
        L.append(f"- `{c['name']}`: candidați în ordinea pythonpath: {' > '.join('`'+x+'`' for x in c['candidates_in_pythonpath_order'])}; "
                 f"câștigă `{c['winner']}`.")
        for k, v in c["shim_targets"].items():
            L.append(f"  - `{k}` este shim `__path__` către: {_short(v, 12)}.")
        for k, v in c["modules"].items():
            L.append(f"  - module în `{k}`: {_short(v, 12)}.")
        stats = ", ".join(f"{k}={v}" for k, v in c["imports_by_served_unit"].items()) or "niciun import `nume.submodul`"
        L.append(f"  - importuri `{c['name']}.X` după unitatea care servește X: {stats}.")
    L.append("")

    L += ["## Încălcări pachet → produs", ""]
    v = inv["layer_violations_package_to_product"]
    if not v:
        L.append("Niciuna găsită.")
    else:
        pairs = defaultdict(lambda: [0, 0])
        for x in v:
            pairs[(x["from_unit"], x["file"], x["to_unit"])][1 if x["test_file"] else 0] += 1
        for (a, f, b), (np_, nt) in sorted(pairs.items()):
            L.append(f"- `{f}` ({a}) → `{b}`: {np_ + nt} importuri (rânduri: "
                     f"{_short([str(x['line']) for x in v if x['file'] == f and x['to_unit'] == b], 8)}).")
    L += ["", "Notă: `packages/xau_kinetic` este un shim către `products/xau_kinetic/engine`; importul `xau_kinetic.*` "
          "este creditat ambelor unități.", ""]

    L += ["## Teste în afara testpaths", "",
          f"`pytest.ini testpaths = {' '.join(m['testpaths']) or '(lipsește)'}`: pytest colectează doar acest director.", "",
          "| Director de top | Fișiere test_*.py | Colectat |", "|---|---|---|"]
    for d, n in inv["tests_focus_dirs"].items():
        L.append(f"| `{d}` | {n} | {'da' if d in m['testpaths'] else 'nu'} |")
    other = {d: n for d, n in inv["tests_by_top_dir"].items() if d not in inv["tests_focus_dirs"]}
    if other:
        L.append(f"| alte directoare | {sum(other.values())} ({_short([f'{d}:{n}' for d, n in other.items()], 8)}) | nu |")
    L += ["", "## Requirements diff", ""]
    r = inv["requirements"]
    if len(r["files"]) == 2:
        a, b = r["files"]
        L += [f"| Pachet | `{a}` | `{b}` | Identic |", "|---|---|---|---|"]
        for row in r["rows"]:
            L.append(f"| {row['name']} | {row[a] or '-'} | {row[b] or '-'} | {'da' if row[a] == row[b] else 'NU'} |")
    else:
        L.append(f"Fișiere găsite: {', '.join(r['files']) or 'niciunul'}.")
    L.append("")
    for fn, fs in r["referenced_by"].items():
        L.append(f"- Referit `{fn}` ({len(fs)} fișiere): {_short(fs, 14)}.")

    unk_out = inv["unknown_outside_units"]
    kinds: dict[str, int] = defaultdict(int)
    for x in unk_out:
        kinds[x["kind"]] += 1
    tot = sum(len(u["unknown"]) for u in inv["units"])
    L += ["", "## UNKNOWN (neinterpretabil de AST)", "",
          f"În module: {tot} intrări (detaliu în JSON). În afara modulelor (teste, scripturi): {len(unk_out)} "
          f"({', '.join(f'{k}={n}' for k, n in sorted(kinds.items())) or '-'}). Aceste fișiere pot importa module "
          "prin căi runtime pe care inventarul nu le vede; numărul de consumatori este deci un minim.", ""]
    srefs = inv.get("string_import_refs", [])
    by_kind: dict[str, int] = defaultdict(int)
    for x in srefs:
        by_kind[x["kind"]] += 1
    L += ["## Importuri din șiruri de caractere", "",
          "AST-ul nu vede importurile din șiruri: `python -m X`, `python -c \"import X\"`, intrări pyproject, liste argv "
          f"`[\"-m\", \"X\"]`. Total: {len(srefs)} ({', '.join(f'{k}={n}' for k, n in sorted(by_kind.items())) or '-'}); "
          f"rezolvate la o unitate: {sum(1 for x in srefs if x['unit'])}.", "",
          "| Fișier:rând | Fel | Modul | Unitate |", "|---|---|---|---|"]
    for x in [x for x in srefs if x["unit"]][:40]:
        L.append(f"| `{x['file']}:{x['line']}` | {x['kind']} | `{x['module']}` | `{x['unit']}` |")
    L.append("")
    return "\n".join(L)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--root", default=".")
    ap.add_argument("--out-json", required=True)
    ap.add_argument("--out-md", required=True)
    args = ap.parse_args(argv)
    root = Path(args.root)
    if not root.is_dir():
        print("root is not a directory", file=sys.stderr)
        return 2
    inv = build_inventory(root)
    for p, content in ((args.out_json, json.dumps(inv, ensure_ascii=False, indent=2, sort_keys=True) + "\n"),
                       (args.out_md, render_md(inv) + "\n")):
        out = Path(p)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(content, "utf-8", newline="\n")
    print(f"units={len(inv['units'])} py_files={inv['meta']['py_files_scanned']} "
          f"violations={len(inv['layer_violations_package_to_product'])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
