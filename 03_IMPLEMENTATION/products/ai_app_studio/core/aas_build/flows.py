"""Flow tests (W3): agent-written Playwright smoke tests in ``tests/flows/<name>.spec.ts``, run by the studio.

Trust model: the spec is written by the agent and is therefore DATA, never code. It is never handed to Node, to the
Playwright test runner or to any interpreter. ``parse_spec`` reads a closed subset of the Playwright test API:

  * an optional ``import { test, expect } from '@playwright/test'``; then ``test(...)``, ``test.describe(...)`` and
    ``test.beforeEach(...)`` blocks whose callback is exactly ``async ({ page }) => { ... }``;
  * statements are ``await <call>;`` or ``const <name> = <locator>;`` (a locator alias, nothing else);
  * calls come from closed tables (PAGE_METHODS, LOCATORS, ACTIONS, MATCHERS) with typed, length-capped arguments and
    closed option keys. ``goto`` takes a same-origin relative path only. There is no ``evaluate``, ``route``,
    ``request``, ``addInitScript``, ``setExtraHTTPHeaders``, ``screenshot({path})``, ``context()``, config or timeout.
  * regular expressions are capped, flag-checked and refused when they nest quantifiers (catastrophic backtracking).

``run`` executes the parsed steps with the studio's own (Python) Playwright: Chromium with the preview browser args
(dead proxy for every non-loopback request, WebRTC limited), a route filter that aborts every request that is not
same-origin with ``http://p-<id16>.localhost:<preview port>``, data: or blob:, service workers blocked, downloads off,
a fresh context per test, fixed step/navigation timeouts and a hard wall-clock limit that kills the driver. Results:
pass/fail per test, the failing step, an error excerpt (sanitised, returned to the agent as untrusted data) and a PNG
screenshot on failure stored in the content-addressed capture store (``preview.captured``) plus one ``qa.result``
event (suite ``flows``) for the owner's UI.
"""
from __future__ import annotations

import hashlib
import os
import re
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass, field
from typing import Any

from studio_core import activity
from studio_core.errors import Unavailable, ValidationFailed

from . import capture, runner, service, sight

FLOW_DIR = "tests/flows"
FLOW_PATH = re.compile(r"^tests/flows/([A-Za-z0-9][A-Za-z0-9_-]{0,40})\.(?i:spec\.ts)$")  # = appfiles._FLOW_SPEC
NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]{0,40}$")
_PID = re.compile(r"^prj_[0-9a-f]{16}$")
MAX_SPEC_BYTES = 20_000
MAX_TESTS_PER_FILE = 20
MAX_TESTS_PER_RUN = 40
MAX_STEPS = 60
MAX_STRING = 500
MAX_REGEX = 200
MAX_WAIT_MS = 3000
STEP_TIMEOUT_MS = 5000
NAV_TIMEOUT_MS = 15_000
RUN_TIMEOUT_S = 240.0
KILL_JOIN_S = 15.0
MAX_ERROR_CHARS = 600
MAX_CONSOLE_RAW = 200
DEFAULT_FRAME = "phone"
_RUN_LOCK = threading.Lock()
_STATE = threading.Lock()
_ACTIVE: dict[str, Any] = {}


# ======================================================================== AST
@dataclass(frozen=True)
class Regex:
    source: str
    flags: str


@dataclass(frozen=True)
class Call:
    name: str
    args: tuple[Any, ...]


@dataclass(frozen=True)
class Step:
    kind: str  # page | keyboard | action | expect
    name: str
    args: tuple[Any, ...]
    line: int
    source: str
    locator: tuple[Call, ...] | None = None  # expect on page: None
    negated: bool = False


@dataclass
class FlowTest:
    file: str
    title: str
    steps: tuple[Step, ...] = field(default_factory=tuple)


# ---------------------------------------------------------------- argument kinds
S, SR, I, B = "s", "sr", "i", "b"  # string, string|regex, int, bool


def _enum(*vals: str) -> tuple[str, ...]:
    return ("enum", *vals)


ARIA_ROLES = frozenset(
    "alert alertdialog application article banner blockquote button caption cell checkbox code columnheader combobox "
    "complementary contentinfo definition deletion dialog directory document emphasis feed figure form generic grid "
    "gridcell group heading img insertion link list listbox listitem log main marquee math meter menu menubar menuitem "
    "menuitemcheckbox menuitemradio navigation none note option paragraph presentation progressbar radio radiogroup "
    "region row rowgroup rowheader scrollbar search searchbox separator slider spinbutton status strong subscript "
    "superscript switch tab table tablist tabpanel term textbox time timer toolbar tooltip tree treegrid treeitem".split())

# name -> (positional kinds, {option: kind}); a tuple kind starting with "enum" is a closed string set
PAGE_METHODS: dict[str, tuple[list[Any], dict[str, Any]]] = {
    "goto": ([S], {}), "reload": ([], {}), "goBack": ([], {}), "goForward": ([], {}),
    "waitForTimeout": ([I], {}),
    "waitForLoadState": ([_enum("load", "domcontentloaded", "networkidle")], {}),
    "emulateMedia": ([], {"colorScheme": _enum("light", "dark", "no-preference"),
                          "reducedMotion": _enum("reduce", "no-preference")}),
}
KEYBOARD = {"press": ([S], {}), "type": ([S], {})}
LOCATORS: dict[str, tuple[list[Any], dict[str, Any]]] = {
    "getByRole": ([S], {"name": SR, "exact": B, "checked": B, "pressed": B, "selected": B, "expanded": B,
                        "disabled": B, "level": I}),
    "getByText": ([SR], {"exact": B}), "getByLabel": ([SR], {"exact": B}), "getByPlaceholder": ([SR], {"exact": B}),
    "getByAltText": ([SR], {"exact": B}), "getByTitle": ([SR], {"exact": B}), "getByTestId": ([SR], {}),
    "locator": ([S], {"hasText": SR, "hasNotText": SR}),
    "first": ([], {}), "last": ([], {}), "nth": ([I], {}),
    "filter": ([], {"hasText": SR, "hasNotText": SR}),
}
ACTIONS: dict[str, tuple[list[Any], dict[str, Any]]] = {
    "click": ([], {}), "dblclick": ([], {}), "tap": ([], {}), "hover": ([], {}), "focus": ([], {}),
    "fill": ([S], {}), "clear": ([], {}), "press": ([S], {}), "check": ([], {}), "uncheck": ([], {}),
    "selectOption": ([S], {}), "scrollIntoViewIfNeeded": ([], {}),
}
MATCHERS: dict[str, tuple[list[Any], dict[str, Any]]] = {
    "toBeVisible": ([], {}), "toBeHidden": ([], {}), "toBeEnabled": ([], {}), "toBeDisabled": ([], {}),
    "toBeChecked": ([], {}), "toBeFocused": ([], {}), "toBeEmpty": ([], {}), "toBeAttached": ([], {}),
    "toBeEditable": ([], {}), "toBeInViewport": ([], {}),
    "toHaveText": ([SR], {}), "toContainText": ([SR], {}), "toHaveValue": ([SR], {}), "toHaveCount": ([I], {}),
    "toHaveAttribute": ([S, SR], {}), "toHaveClass": ([SR], {}), "toHaveCSS": ([S, SR], {}),
    "toHaveId": ([SR], {}), "toHaveAccessibleName": ([SR], {}),
}
PAGE_MATCHERS: dict[str, tuple[list[Any], dict[str, Any]]] = {"toHaveURL": ([SR], {}), "toHaveTitle": ([SR], {})}


# ======================================================================== tokenizer
_PUNCT = ("=>", "(", ")", "{", "}", ",", ";", ".", ":", "=")  # no [ ]: no computed member access
_IDENT = re.compile(r"[A-Za-z_$][A-Za-z0-9_$]*")
_NUM = re.compile(r"[0-9]+(?:\.[0-9]+)?")
_ESC = {"n": "\n", "t": "\t", "r": "\r", "\\": "\\", "'": "'", '"': '"', "`": "`", "0": "\0"}


@dataclass(frozen=True)
class Tok:
    kind: str  # ident | str | num | regex | punct | eof
    value: Any
    line: int


def _err(msg: str, line: int | None = None) -> ValidationFailed:
    return ValidationFailed(f"flow spec: {msg}" + (f" (line {line})" if line else ""))


def _tokens(src: str) -> list[Tok]:
    out: list[Tok] = []
    i, line, n = 0, 1, len(src)
    while i < n:
        c = src[i]
        if c == "\n":
            line += 1
            i += 1
        elif c in " \t\r﻿":
            i += 1
        elif src.startswith("//", i):
            j = src.find("\n", i)
            i = n if j < 0 else j
        elif src.startswith("/*", i):
            j = src.find("*/", i + 2)
            if j < 0:
                raise _err("unterminated comment", line)
            line += src.count("\n", i, j)
            i = j + 2
        elif c in "'\"`":
            j, buf = i + 1, []
            while True:
                if j >= n or src[j] == "\n":
                    raise _err("unterminated string", line)
                ch = src[j]
                if ch == c:
                    break
                if c == "`" and src.startswith("${", j):
                    raise _err("template literals with ${...} are not allowed", line)
                if ch == "\\":
                    nx = src[j + 1] if j + 1 < n else ""
                    if nx == "u" and re.fullmatch(r"[0-9a-fA-F]{4}", src[j + 2:j + 6]):
                        buf.append(chr(int(src[j + 2:j + 6], 16)))
                        j += 6
                        continue
                    if nx not in _ESC:
                        raise _err("unsupported escape in string", line)
                    buf.append(_ESC[nx])
                    j += 2
                    continue
                buf.append(ch)
                j += 1
            s = "".join(buf)
            if len(s) > MAX_STRING:
                raise _err(f"strings are limited to {MAX_STRING} characters", line)
            out.append(Tok("str", s, line))
            i = j + 1
        elif c == "/" and out and out[-1].kind == "punct" and out[-1].value in ("(", ","):
            j, cls = i + 1, False
            while True:
                if j >= n or src[j] == "\n":
                    raise _err("unterminated regular expression", line)
                ch = src[j]
                if ch == "\\":
                    j += 2
                    continue
                if ch == "[":
                    cls = True
                elif ch == "]":
                    cls = False
                elif ch == "/" and not cls:
                    break
                j += 1
            body = src[i + 1:j]
            m = re.match(r"[a-z]*", src[j + 1:])
            flags = m.group(0) if m else ""
            out.append(Tok("regex", _regex(body, flags, line), line))
            i = j + 1 + len(flags)
        elif m := _IDENT.match(src, i):
            out.append(Tok("ident", m.group(0), line))
            i = m.end()
        elif m := _NUM.match(src, i):
            v = m.group(0)
            out.append(Tok("num", float(v) if "." in v else int(v), line))
            i = m.end()
        else:
            p = next((p for p in _PUNCT if src.startswith(p, i)), None)
            if p is None:
                raise _err(f"unexpected character {c!r}", line)
            out.append(Tok("punct", p, line))
            i += len(p)
    out.append(Tok("eof", None, line))
    return out


_NESTED_QUANT = re.compile(r"\((?:[^()\\]|\\.)*[+*}](?:[^()\\]|\\.)*\)\s*[+*{]")
_BACKREF = re.compile(r"\\[1-9]")


def _regex(body: str, flags: str, line: int) -> Regex:
    if not body or len(body) > MAX_REGEX:
        raise _err(f"regular expression must be 1..{MAX_REGEX} characters", line)
    if set(flags) - set("ims") or len(set(flags)) != len(flags):
        raise _err("regular expression flags: only i, m, s", line)
    if _NESTED_QUANT.search(body) or _BACKREF.search(body) or "(?<" in body or "(?=" in body or "(?!" in body:
        raise _err("regular expression too complex (nested quantifiers, look-around or back-references)", line)
    try:
        re.compile(body)
    except re.error as e:
        raise _err(f"invalid regular expression ({e.msg})", line) from None
    return Regex(body, flags)


# ======================================================================== parser
class _P:
    def __init__(self, src: str) -> None:
        self.t = _tokens(src)
        self.i = 0
        self.lines = src.splitlines()

    @property
    def cur(self) -> Tok:
        return self.t[self.i]

    def next(self) -> Tok:
        tok = self.t[self.i]
        self.i += 1
        return tok

    def at(self, kind: str, value: Any = None) -> bool:
        return self.cur.kind == kind and (value is None or self.cur.value == value)

    def expect(self, kind: str, value: Any = None, what: str | None = None) -> Tok:
        if not self.at(kind, value):
            got = "end of file" if self.cur.kind == "eof" else repr(self.cur.value)
            raise _err(f"expected {what or repr(value) if value is not None else what or kind}, got {got}",
                       self.cur.line)
        return self.next()

    def ident(self, value: str) -> None:
        self.expect("ident", value)

    def punct(self, value: str) -> None:
        self.expect("punct", value)

    def opt(self, value: str) -> None:
        if self.at("punct", value):
            self.next()

    # ---------------------------------------------------------------- file level
    def file(self, file: str) -> list[FlowTest]:
        if self.at("ident", "import"):
            self.imports()
        tests: list[FlowTest] = []
        self.block(file, "", [], tests, top=True)
        if not tests:
            raise _err("no test(...) found")
        return tests

    def imports(self) -> None:
        line = self.cur.line
        self.ident("import")
        if not self.at("punct", "{"):
            raise _err("only `import { test, expect } from '@playwright/test'` is allowed", line)
        self.punct("{")
        while True:
            name = self.expect("ident", what="test or expect").value
            if name not in ("test", "expect"):
                raise _err("only test and expect can be imported", line)
            if self.at("punct", ","):
                self.next()
                if self.at("punct", "}"):
                    break
                continue
            break
        self.punct("}")
        self.ident("from")
        if self.expect("str", what="'@playwright/test'").value != "@playwright/test":
            raise _err("imports must come from '@playwright/test'", line)
        self.opt(";")
        if self.at("ident", "import"):
            raise _err("only one import from '@playwright/test' is allowed", self.cur.line)

    def block(self, file: str, prefix: str, before: list[Step], tests: list[FlowTest], *, top: bool) -> None:
        before = list(before)
        while not (self.at("eof") if top else self.at("punct", "}")):
            line = self.cur.line
            if not self.at("ident", "test"):
                raise _err("only test(...), test.describe(...) and test.beforeEach(...) are allowed at the top level",
                           line)
            self.next()
            if self.at("punct", "."):
                self.next()
                which = self.expect("ident", what="describe or beforeEach").value
                if which == "describe":
                    self.punct("(")
                    title = self.expect("str", what="a describe title").value
                    self.punct(",")
                    self.punct("(")
                    self.punct(")")
                    self.punct("=>")
                    self.punct("{")
                    self.block(file, f"{prefix}{title} › ", before, tests, top=False)
                    self.punct("}")
                    self.punct(")")
                elif which == "beforeEach":
                    self.punct("(")
                    before += self.callback()
                    self.punct(")")
                else:
                    raise _err(f"test.{which} is not allowed at the top level (only describe and beforeEach)", line)
            else:
                self.punct("(")
                title = self.expect("str", what="a test title").value.strip()
                if not title:
                    raise _err("a test needs a title", line)
                self.punct(",")
                steps = before + self.callback()
                self.punct(")")
                if len(steps) > MAX_STEPS:
                    raise _err(f"a test may have at most {MAX_STEPS} steps (beforeEach included)", line)
                tests.append(FlowTest(file, prefix + title, tuple(steps)))
                if len(tests) > MAX_TESTS_PER_FILE:
                    raise _err(f"at most {MAX_TESTS_PER_FILE} tests per file", line)
            self.opt(";")

    def callback(self) -> list[Step]:
        """``async ({ page }) => { statements }``"""
        line = self.cur.line
        self.ident("async")
        self.punct("(")
        self.punct("{")
        if self.expect("ident", what="page").value != "page" or not self.at("punct", "}"):
            raise _err("the test callback must be exactly async ({ page }) => { ... } (only the page fixture)", line)
        self.punct("}")
        self.punct(")")
        self.punct("=>")
        self.punct("{")
        steps: list[Step] = []
        aliases: dict[str, tuple[Call, ...]] = {}
        while not self.at("punct", "}"):
            st = self.statement(aliases)
            if st is not None:
                steps.append(st)
            if len(steps) > MAX_STEPS:
                raise _err(f"a test may have at most {MAX_STEPS} steps", self.cur.line)
        self.punct("}")
        return steps

    # ---------------------------------------------------------------- statements
    def statement(self, aliases: dict[str, tuple[Call, ...]]) -> Step | None:
        line = self.cur.line
        src = self.lines[line - 1].strip()[:200] if 0 < line <= len(self.lines) else ""
        if self.at("ident", "const"):
            self.next()
            name = self.expect("ident", what="a name").value
            if name in ("page", "test", "expect") or name in aliases:
                raise _err(f"{name} cannot be redefined", line)
            self.punct("=")
            if not self.at("ident", "page") and not (self.at("ident") and self.cur.value in aliases):
                raise _err("const may only name a locator (page.getBy...(...))", line)
            loc = self.locator(aliases, line)
            if not loc:
                raise _err("const may only name a locator (page.getBy...(...))", line)
            aliases[name] = loc
            self.opt(";")
            return None
        if not self.at("ident", "await"):
            raise _err("each statement must be `await ...;` or `const name = page.getBy...(...);`", line)
        self.next()
        if self.at("ident", "expect"):
            st = self.expectation(aliases, line, src)
        else:
            st = self.call_statement(aliases, line, src)
        self.opt(";")
        return st

    def expectation(self, aliases: dict[str, tuple[Call, ...]], line: int, src: str) -> Step:
        self.ident("expect")
        self.punct("(")
        loc: tuple[Call, ...] | None
        if self.at("ident", "page") and self.t[self.i + 1].kind == "punct" and self.t[self.i + 1].value == ")":
            self.next()
            loc = None
        else:
            loc = self.locator(aliases, line)
            if not loc:
                raise _err("expect(...) takes page or a locator", line)
        self.punct(")")
        self.punct(".")
        negated = False
        if self.at("ident", "not"):
            self.next()
            negated = True
            self.punct(".")
        name = self.expect("ident", what="a matcher").value
        table = PAGE_MATCHERS if loc is None else MATCHERS
        if name not in table:
            raise _err(f"matcher {name} is not allowed here (allowed: {', '.join(sorted(table))})", line)
        args = self.args(name, table[name], line)
        return Step("expect", name, args, line, src, loc, negated)

    def call_statement(self, aliases: dict[str, tuple[Call, ...]], line: int, src: str) -> Step:
        if self.at("ident", "page"):
            t1, t2 = self.t[self.i + 1], self.t[self.i + 2]
            if t1.kind == "punct" and t1.value == "." and t2.kind == "ident":
                if t2.value == "keyboard":
                    self.i += 3
                    self.punct(".")
                    name = self.expect("ident", what="press or type").value
                    if name not in KEYBOARD:
                        raise _err(f"page.keyboard.{name} is not allowed (press, type)", line)
                    return Step("keyboard", name, self.args(name, KEYBOARD[name], line), line, src)
                if t2.value in PAGE_METHODS:
                    self.i += 3
                    args = self.args(t2.value, PAGE_METHODS[t2.value], line)
                    if t2.value == "goto":
                        _check_goto(args[0], line)
                    if t2.value == "waitForTimeout" and not 0 <= args[0] <= MAX_WAIT_MS:
                        raise _err(f"waitForTimeout: at most {MAX_WAIT_MS} ms", line)
                    if t2.value == "emulateMedia" and not args[0]:
                        raise _err("emulateMedia needs colorScheme or reducedMotion", line)
                    return Step("page", t2.value, args, line, src)
                if t2.value not in LOCATORS:
                    raise _err(f"page.{t2.value} is not allowed (allowed: {', '.join(sorted(PAGE_METHODS))}, "
                               "keyboard.press/type and getBy... locators)", line)
        elif not (self.at("ident") and self.cur.value in aliases):
            raise _err(f"statement not allowed: {self.cur.value!r} (use page, expect or a locator alias)", line)
        loc = self.locator(aliases, line, stop_at_action=True)
        if not loc:
            raise _err("an action needs a locator", line)
        self.punct(".")
        name = self.expect("ident", what="an action").value
        if name not in ACTIONS:
            raise _err(f"action {name} is not allowed (allowed: {', '.join(sorted(ACTIONS))})", line)
        return Step("action", name, self.args(name, ACTIONS[name], line), line, src, loc)

    def locator(self, aliases: dict[str, tuple[Call, ...]], line: int, stop_at_action: bool = False) -> tuple[Call, ...]:
        first = self.expect("ident", what="page or a locator").value
        if first == "page":
            chain: list[Call] = []
        elif first in aliases:
            chain = list(aliases[first])
        else:
            raise _err(f"unknown name {first!r}", line)
        while self.at("punct", "."):
            nxt = self.t[self.i + 1]
            if nxt.kind != "ident":
                raise _err("expected a method name", line)
            if nxt.value not in LOCATORS:
                if stop_at_action and chain and nxt.value in ACTIONS:
                    break
                raise _err(f"{'page' if not chain else 'locator'}.{nxt.value} is not allowed", line)
            self.i += 2
            chain.append(Call(nxt.value, self.args(nxt.value, LOCATORS[nxt.value], line)))
        if chain and chain[0].name in ("first", "last", "nth", "filter"):
            raise _err(f"page.{chain[0].name} is not allowed (start with a getBy... or locator)", line)
        if len(chain) > 8:
            raise _err("locator chains are limited to 8 calls", line)
        if chain and chain[0].name == "getByRole" and chain[0].args[0] not in ARIA_ROLES:
            raise _err(f"unknown ARIA role {chain[0].args[0]!r}", line)
        return tuple(chain)

    # ---------------------------------------------------------------- arguments
    def value(self, line: int) -> Any:
        tok = self.next()
        if tok.kind in ("str", "num", "regex"):
            return tok.value
        if tok.kind == "ident" and tok.value in ("true", "false"):
            return tok.value == "true"
        if tok.kind == "punct" and tok.value == "{":
            obj: dict[str, Any] = {}
            while not self.at("punct", "}"):
                key = self.next()
                if key.kind not in ("ident", "str"):
                    raise _err("expected an option name", line)
                self.punct(":")
                obj[key.value] = self.value(line)
                if not self.at("punct", "}"):
                    self.punct(",")
            self.punct("}")
            return obj
        raise _err(f"unexpected {tok.value!r} (only string, number, regex, true/false and {{...}} literals)", line)

    def args(self, name: str, sig: tuple[list[Any], dict[str, Any]], line: int) -> tuple[Any, ...]:
        self.punct("(")
        vals: list[Any] = []
        while not self.at("punct", ")"):
            vals.append(self.value(line))
            if not self.at("punct", ")"):
                self.punct(",")
            if len(vals) > 3:
                raise _err(f"{name}: too many arguments", line)
        self.punct(")")
        pos, opts = sig
        if vals and isinstance(vals[-1], dict):
            if not opts:
                raise _err(f"{name} takes no options (timeouts and flags are fixed by the studio)", line)
            given = vals.pop()
            for k, v in given.items():
                if k not in opts:
                    raise _err(f"{name}: option {k!r} is not allowed (options: {', '.join(sorted(opts))})", line)
                _check(v, opts[k], f"{name}.{k}", line)
            vals.append(given)
        elif opts:
            vals.append({})
        n = len(vals) - (1 if opts else 0)
        if n != len(pos):
            raise _err(f"{name} takes {len(pos)} argument(s)", line)
        for v, kind in zip(vals, pos, strict=False):
            _check(v, kind, name, line)
        return tuple(vals)


def _check(v: Any, kind: Any, what: str, line: int) -> None:
    ok = {S: isinstance(v, str), SR: isinstance(v, str | Regex), B: isinstance(v, bool),
          I: isinstance(v, int) and not isinstance(v, bool) and 0 <= v <= 10_000_000}.get(kind) if isinstance(kind, str) \
        else isinstance(v, str) and v in kind[1:]
    if not ok:
        want = {S: "a string", SR: "a string or regex", B: "true/false", I: "a whole number"}.get(kind) \
            if isinstance(kind, str) else " | ".join(kind[1:])
        raise _err(f"{what}: expected {want}", line)


def _check_goto(path: str, line: int) -> None:
    """Same-origin relative only: '/x', '/?q', '/#h', '?q', '#h'. Never a scheme, '//' host or backslash."""
    if not re.fullmatch(r"(?:/(?![/\\])|[?#])[^\\\s]*|/", path) or ":" in path.split("?")[0].split("#")[0]:
        raise _err("page.goto takes a relative path on the preview origin ('/', '/about', '/?aas-state=empty')", line)


def parse_spec(source: str, file: str = "spec") -> list[FlowTest]:
    """Parse one spec into tests; raises ValidationFailed (with the line) on anything outside the subset."""
    if not isinstance(source, str):
        raise _err("the spec must be text")
    if len(source.encode("utf-8")) > MAX_SPEC_BYTES:
        raise _err(f"a spec is limited to {MAX_SPEC_BYTES} bytes")
    return _P(source).file(file)


def path_for(name: str) -> str:
    if not isinstance(name, str) or not NAME.fullmatch(name):
        raise ValidationFailed("invalid flow test name (letters, digits, _ and -, max 41 characters)")
    return f"{FLOW_DIR}/{name}.spec.ts"


# ======================================================================== executor (studio Playwright)
def _py_regex(r: Regex) -> re.Pattern[str]:
    fl = (re.I if "i" in r.flags else 0) | (re.M if "m" in r.flags else 0) | (re.S if "s" in r.flags else 0)
    return re.compile(r.source, fl)


def _arg(v: Any) -> Any:
    return _py_regex(v) if isinstance(v, Regex) else v


_SNAKE = {"hasText": "has_text", "hasNotText": "has_not_text", "colorScheme": "color_scheme",
          "reducedMotion": "reduced_motion"}


def _kw(opts: dict[str, Any]) -> dict[str, Any]:
    return {_SNAKE.get(k, k): _arg(v) for k, v in opts.items()}


def _snake(name: str) -> str:
    return re.sub(r"(?<!^)([A-Z])", lambda m: "_" + m.group(1).lower(), name).replace("_u_r_l", "_url") \
        .replace("_c_s_s", "_css")


def _build_locator(page: Any, chain: tuple[Call, ...]) -> Any:
    loc = page
    for c in chain:
        if c.name == "first":
            loc = loc.first
        elif c.name == "last":
            loc = loc.last
        elif c.name == "nth":
            loc = loc.nth(c.args[0])
        elif c.name == "filter":
            loc = loc.filter(**_kw(c.args[0]))
        elif c.name == "getByRole":
            loc = loc.get_by_role(c.args[0], **_kw(c.args[1]))
        elif c.name == "getByTestId":
            loc = loc.get_by_test_id(_arg(c.args[0]))
        elif c.name == "locator":
            loc = loc.locator(c.args[0], **_kw(c.args[1]))
        else:
            loc = getattr(loc, _snake(c.name))(_arg(c.args[0]), **_kw(c.args[1]))
    return loc


def _do_step(page: Any, origin: str, st: Step) -> None:
    from playwright.sync_api import expect

    if st.kind == "page":
        if st.name == "goto":
            page.goto(origin + (st.args[0] if st.args[0].startswith("/") else "/" + st.args[0]),
                      wait_until="load", timeout=NAV_TIMEOUT_MS)
        elif st.name in ("reload", "goBack", "goForward"):
            getattr(page, _snake(st.name))(wait_until="load", timeout=NAV_TIMEOUT_MS)
        elif st.name == "waitForTimeout":
            page.wait_for_timeout(min(int(st.args[0]), MAX_WAIT_MS))
        elif st.name == "waitForLoadState":
            page.wait_for_load_state(st.args[0], timeout=NAV_TIMEOUT_MS)
        elif st.name == "emulateMedia":
            page.emulate_media(**_kw(st.args[0]))
        return
    if st.kind == "keyboard":
        getattr(page.keyboard, st.name)(st.args[0])
        return
    if st.kind == "action":
        loc = _build_locator(page, st.locator or ())
        fn = getattr(loc, _snake(st.name))
        fn(*[_arg(a) for a in st.args], timeout=STEP_TIMEOUT_MS)
        return
    target = page if st.locator is None else _build_locator(page, st.locator)
    e = expect(target)
    args = [_arg(a) for a in st.args]
    if st.name == "toHaveURL" and isinstance(args[0], str):
        args[0] = origin + (args[0] if args[0].startswith("/") else "/" + args[0])
    getattr(e, ("not_" if st.negated else "") + _snake(st.name))(*args, timeout=STEP_TIMEOUT_MS)


def _excerpt(e: BaseException) -> str:
    text = str(e) or type(e).__name__
    head, _, log = text.partition("Call log:")
    calls = [ln.strip(" -") for ln in log.splitlines() if ln.strip(" -")][:2]  # what was waited for (the locator)
    return " | ".join([f"{type(e).__name__}: {' '.join(head.split())}", *calls])[:MAX_ERROR_CHARS]


def _execute(origin: str, frame: str, tests: list[FlowTest]) -> tuple[list[dict[str, Any]], list[str]]:
    """Run each test in a fresh, isolated browser context. Never raises for a test failure."""
    from playwright.sync_api import sync_playwright

    w, h = capture.FRAMES[frame]
    results: list[dict[str, Any]] = []
    console: list[str] = []

    def note(text: str) -> None:
        if len(console) < MAX_CONSOLE_RAW:
            console.append(text[:2000])
    with sync_playwright() as pw:
        _ACTIVE["proc"] = getattr(getattr(getattr(getattr(pw, "_impl_obj", None), "_connection", None), "_transport",
                                          None), "_proc", None)
        browser = pw.chromium.launch(headless=True, args=list(capture.BROWSER_ARGS))
        try:
            for t in tests:
                ctx = browser.new_context(viewport={"width": w, "height": h}, device_scale_factor=1,
                                          service_workers="block", accept_downloads=False, color_scheme="light",
                                          is_mobile=frame == "phone", has_touch=frame != "desktop")
                start = time.monotonic()
                res: dict[str, Any] = {"status": "passed"}
                try:
                    ctx.set_default_timeout(STEP_TIMEOUT_MS)
                    ctx.set_default_navigation_timeout(NAV_TIMEOUT_MS)
                    ctx.route("**/*", lambda route: route.continue_()
                              if capture._same_origin(route.request.url, origin)  # noqa: SLF001
                              or route.request.url.startswith(("data:", "blob:"))
                              else route.abort("blockedbyclient"))
                    page = ctx.new_page()
                    page.on("console", lambda m, tt=t.title: note(f"[{tt}] console.{m.type}: {m.text}")
                            if m.type in ("error", "assert") else None)
                    page.on("pageerror", lambda exc, tt=t.title: note(f"[{tt}] uncaught: {exc}"))
                    page.goto(origin + "/", wait_until="load", timeout=NAV_TIMEOUT_MS)
                    for idx, st in enumerate(t.steps):
                        try:
                            _do_step(page, origin, st)
                        except Exception as e:  # noqa: BLE001 - a failing step fails the test, not the run
                            res = {"status": "failed", "step": idx, "error": _excerpt(e)}
                            try:
                                res["screenshot"] = page.screenshot(full_page=False, type="png", timeout=STEP_TIMEOUT_MS)
                            except Exception:  # noqa: BLE001
                                pass
                            break
                except Exception as e:  # noqa: BLE001 - context/page setup or the first load failed
                    res = {"status": "failed", "step": -1, "error": _excerpt(e)}
                finally:
                    res["durationMs"] = int((time.monotonic() - start) * 1000)
                    try:
                        ctx.close()
                    except Exception:  # noqa: BLE001
                        pass
                results.append(res)
        finally:
            browser.close()
            _ACTIVE.pop("proc", None)
    return results, console


def _kill_active() -> None:
    proc = _ACTIVE.get("proc")
    pid = getattr(proc, "pid", None)
    if pid:
        runner.kill_tree(int(pid))
    if proc is not None:
        try:
            proc.kill()
        except Exception:  # noqa: BLE001
            pass


# ======================================================================== run
def _specs(studio: Any, actor: Any, project_id: str, file: str | None) -> dict[str, str]:
    files = studio.get_project(actor, project_id, with_components=False)["settings"].get("files", {})
    specs = {p: f["content"] for p, f in sorted(files.items()) if FLOW_PATH.fullmatch(p)}
    if file is not None:
        want = path_for(file)
        if want not in specs:
            raise ValidationFailed("flow test not found", path=want)
        specs = {want: specs[want]}
    if not specs:
        raise ValidationFailed("no flow tests yet: write one with flow_test_write (tests/flows/<name>.spec.ts)")
    return specs


def _store_png(studio: Any, project_id: str, png: Any, revision: int, run_id: str | None) -> str | None:
    if not isinstance(png, bytes | bytearray) or not bytes(png).startswith(sight.PNG_MAGIC) \
            or len(png) > capture.MAX_PNG_BYTES:
        return None
    png = bytes(png)
    sha = hashlib.sha256(png).hexdigest()
    d = capture.captures_dir(studio.settings)
    d.mkdir(parents=True, exist_ok=True)
    target = d / f"{sha}.png"
    if not target.is_file():
        tmp = d / f".{sha}.{os.getpid()}.partial"
        tmp.write_bytes(png)
        os.replace(tmp, target)
    capture._register_and_prune(studio.settings, project_id, [(sha, len(png))])  # noqa: SLF001
    try:
        activity.record(studio, "preview.captured", project_id=project_id, run_id=run_id, source="build",
                        artifact_sha256=sha, payload={"revision": revision, "frame": DEFAULT_FRAME, "sha256": sha})
    except Exception:  # noqa: BLE001
        activity._note_dropped(studio)  # noqa: SLF001
    return sha


def _summary(revision: int, passed: int, total: int, tests: list[dict[str, Any]], errors: dict[int, str]) -> str:
    """Owner UI text (qa.result summary, <= 500 chars): one header line, then one line per non-passing test."""
    lines = [f"flows rev {revision}: {passed}/{total} passed"]
    for i, t in enumerate(tests):
        if t["status"] == "passed":
            continue
        shot = f" [shot {t['screenshotSha256'][:12]}]" if t.get("screenshotSha256") else ""
        err = " ".join(errors.get(i, "").split())[:120]
        lines.append(f"x {t['file'].rsplit('/', 1)[-1]} › {t['title'][:80]}{shot}: {err}")
    out = "\n".join(lines)
    return out if len(out) <= 480 else out[:477] + "..."


def run(studio: Any, actor: Any, project_id: str, *, file: str | None = None, frame: str = DEFAULT_FRAME,
        run_id: str | None = None, url: str | None = None,
        execute: Callable[[str, str, list[FlowTest]], tuple[list[dict[str, Any]], list[str]]] = _execute,
        timeout: float = RUN_TIMEOUT_S, kill: Callable[[], None] | None = None,
        join_timeout: float = KILL_JOIN_S) -> dict[str, Any]:
    """Run the project's flow tests (all, or ``file``) against its preview origin; see the module docstring."""
    if not isinstance(project_id, str) or not _PID.fullmatch(project_id):
        raise ValidationFailed("invalid project id")
    if frame not in capture.FRAMES:
        raise ValidationFailed("frame must be phone, tablet or desktop")
    if service.site_root_for(studio.settings)(project_id[4:]) is None:
        raise Unavailable("nothing to test: build the web target first (app_build target web)")
    specs = _specs(studio, actor, project_id, file)
    revision = studio.get_revision(project_id)
    origin = (url or f"http://p-{project_id[4:]}.localhost:{studio.settings.preview_port}/").rstrip("/")
    tests: list[dict[str, Any]] = []
    errors: dict[int, str] = {}
    runnable: list[tuple[int, FlowTest]] = []
    for path, content in specs.items():
        try:
            parsed = parse_spec(content, path)
        except ValidationFailed as e:
            errors[len(tests)] = str(e)
            tests.append({"file": path, "title": "(spec does not parse)", "status": "invalid", "durationMs": 0,
                          "screenshotSha256": None})
            continue
        for ft in parsed:
            if len(runnable) >= MAX_TESTS_PER_RUN:
                errors[len(tests)] = f"skipped: at most {MAX_TESTS_PER_RUN} tests per run"
                tests.append({"file": path, "title": ft.title, "status": "skipped", "durationMs": 0,
                              "screenshotSha256": None})
                continue
            runnable.append((len(tests), ft))
            tests.append({"file": path, "title": ft.title, "status": "pending", "durationMs": 0,
                          "screenshotSha256": None})
    console: list[str] = []
    if runnable:
        if not _RUN_LOCK.acquire(blocking=False):
            raise Unavailable("a flow test run is already in progress")
        handed_off = False
        try:
            box: dict[str, Any] = {}

            def work() -> None:
                try:
                    box["results"], box["console"] = execute(origin, frame, [ft for _, ft in runnable])
                except BaseException as e:  # noqa: BLE001
                    box["error"] = e
                finally:
                    with _STATE:
                        box["done"] = True
                        if box.get("abandoned"):
                            _RUN_LOCK.release()

            t = threading.Thread(target=work, name="aas-flows", daemon=True)
            t.start()
            t.join(timeout=timeout)
            if t.is_alive():
                try:
                    (kill or _kill_active)()
                except Exception:  # noqa: BLE001
                    pass
                t.join(timeout=join_timeout)
                with _STATE:
                    if not box.get("done"):
                        box["abandoned"] = True
                        handed_off = True
                raise Unavailable(f"the flow test run timed out after {int(timeout)} s")
            if "error" in box:
                raise Unavailable(f"the flow test run failed ({type(box['error']).__name__}: "
                                  f"{str(box['error'])[:200]})")
            results = list(box.get("results") or [])
            console = list(box.get("console") or [])
        finally:
            if not handed_off:
                _RUN_LOCK.release()
        for k, (i, ft) in enumerate(runnable):
            r = results[k] if k < len(results) and isinstance(results[k], dict) else {"status": "failed",
                                                                                         "error": "no result"}
            status = "passed" if r.get("status") == "passed" else "failed"
            tests[i]["status"] = status
            tests[i]["durationMs"] = int(r.get("durationMs") or 0)
            if status == "failed":
                step = r.get("step")
                if isinstance(step, int) and 0 <= step < len(ft.steps):
                    tests[i]["failedStep"] = step
                    tests[i]["stepLine"] = ft.steps[step].line
                    tests[i]["stepSource"] = ft.steps[step].source
                errors[i] = str(r.get("error") or "failed")
                tests[i]["screenshotSha256"] = _store_png(studio, project_id, r.get("screenshot"), revision, run_id)
    passed = sum(1 for x in tests if x["status"] == "passed")
    total = len(tests)
    err_lines = sight.console_lines([f"{tests[i]['file'].rsplit('/', 1)[-1]} › {tests[i]['title']}: {msg}"
                                     for i, msg in sorted(errors.items())])
    try:
        activity.record(studio, "qa.result", project_id=project_id, run_id=run_id, source="build",
                        payload={"suite": "flows", "passed": passed, "failed": total - passed,
                                 "summary": _summary(revision, passed, total, tests, errors)})
    except Exception:  # noqa: BLE001
        activity._note_dropped(studio)  # noqa: SLF001
    return {"revision": revision, "url": origin + "/", "frame": frame, "passed": passed, "failed": total - passed,
            "total": total, "tests": tests, "errors": err_lines, "console": sight.console_lines(console)}
