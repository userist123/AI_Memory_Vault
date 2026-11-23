"""W4 click-to-edit inspector (owner only; behind the same BoundaryMiddleware: bearer token, host and origin checks).

The preview iframe sends the studio UI an element descriptor ``{v:1, kind:"select", path, tag, text, rect, classes}``
(kit/template/src/aas-inspector.ts; the UI accepts it only from the exact preview origin of the current project). The
descriptor is untrusted page data: it is schema-validated again here and only ever used to LOCATE source text, never
executed or rendered as markup.

GET  /api/v1/projects/{pid}/inspector          -> {revision, families: {color: [...], spacing: [...]}}
POST /api/v1/projects/{pid}/inspector/edit     -> {baseRevision, element, op: "text", to} or
                                                  {baseRevision, element, op: "class", from, to}
     The change is located in src/** (unique visible-text match, or a className token, tie-broken by the element's
     tag and classes; ambiguous -> 409 inspector_ambiguous, missing -> 404) and written through the normal revisioned
     Two steps: without ``confirm`` a dry run answers {dryRun, path, line, planToken} and writes nothing; the UI
     shows "Will edit <file>:<line>" and only the owner's confirmation sends the same body with ``confirm: true`` and
     that ``planToken`` (sha256 of path|line|baseRevision|op|old|new); a missing or different token -> 409
     inspector_plan_mismatch, nothing written.
     ``Studio.write_file`` operation as the owner (policy + tool_executions + audit row), then a web rebuild starts.
POST /api/v1/projects/{pid}/inspector/capture  -> {device, element}: a preview capture of that frame (the unchanged
     capture.capture path), cropped to the element's rect (+ padding), stored content-addressed like every capture.
"""
from __future__ import annotations

import bisect
import hashlib
import hmac
import io
import json
import os
import re
from typing import Any, Literal

from fastapi import FastAPI
from pydantic import BaseModel, ConfigDict, Field, StrictInt, field_validator, model_validator

from aas_build import capture
from aas_runner.claude import RunInProgress
from studio_core import app_templates
from studio_core.errors import ConflictError, NotFound, StudioError, Unavailable, ValidationFailed
from studio_core.policy import Actor

API = "/api/v1"
_PID = re.compile(r"^prj_[0-9a-f]{16}$")
MAX_TEXT = 200
MAX_CLASSES = 20
MAX_PATH = 300
COORD = 100_000.0
CROP_PAD = 16
KIT_OWNED = frozenset({"src/aas-inspector.ts"})  # materialised from the kit at build time: never an edit target
SOURCE_EXT = (".tsx", ".jsx", ".ts", ".js")
# control characters, line/paragraph separators and bidi overrides are never part of a descriptor or an edit
_BAD_CHARS = re.compile(r"[\x00-\x1f\x7f\u2028\u2029\u202a-\u202e\u2066-\u2069]")
_CLASS = re.compile(r"^[A-Za-z0-9_:/.\[\]%#-]{1,64}$")

# The allow-list is the W1 design system: one utility class per semantic token (studio_core.app_templates, parsed
# from kit/template/src/ui/globals.css; the classes live in src/ui/utilities.css). Never a second copy here.
COLOR_TOKENS = app_templates.SEMANTIC_COLOR_TOKENS
SPACING_SCALE = app_templates.SPACING_STEPS
COLOR_PREFIXES = tuple(app_templates.COLOR_UTILITIES)
SPACING_PREFIXES = tuple(app_templates.SPACING_UTILITIES)
FAMILIES: dict[str, list[str]] = app_templates.utility_classes()
# class -> utility prefix (prefixes have no dash; token names may: surface-2, on-primary-soft)
_FAMILY_OF: dict[str, str] = {c: c.split("-", 1)[0] for fam in FAMILIES.values() for c in fam}


class AmbiguousEdit(ConflictError):
    """The selected element matches more than one place in the source: the owner should use the chat instead."""

    code = "inspector_ambiguous"


class PlanMismatch(ConflictError):
    """The confirmed edit is not the one the dry run showed the owner (missing or different ``planToken``)."""

    code = "inspector_plan_mismatch"


class Strict(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)


class Rect(Strict):
    x: float = Field(ge=-COORD, le=COORD, strict=True)
    y: float = Field(ge=-COORD, le=COORD, strict=True)
    w: float = Field(ge=0, le=COORD, strict=True)
    h: float = Field(ge=0, le=COORD, strict=True)


class Descriptor(Strict):
    v: Literal[1]
    kind: Literal["select"]
    path: str = Field(max_length=MAX_PATH, pattern=r"^[\x20-\x7e]*$")
    tag: str = Field(pattern=r"^[a-z][a-z0-9-]{0,31}$")
    text: str = Field(max_length=MAX_TEXT)
    rect: Rect
    classes: list[str] = Field(max_length=MAX_CLASSES)

    @field_validator("v", mode="before")
    @classmethod
    def _int_only(cls, v: Any) -> Any:
        if type(v) is not int:  # "1", 1.0, true are not the protocol version
            raise ValueError("v must be the integer 1")
        return v

    @field_validator("text")
    @classmethod
    def _text(cls, v: str) -> str:
        if _BAD_CHARS.search(v):
            raise ValueError("text contains control characters")
        return v

    @field_validator("classes")
    @classmethod
    def _classes(cls, v: list[str]) -> list[str]:
        if any(not isinstance(c, str) or not _CLASS.fullmatch(c) for c in v):
            raise ValueError("invalid class name")
        return v


class EditReq(Strict):
    baseRevision: StrictInt = Field(ge=0)  # noqa: N815
    element: Descriptor
    op: Literal["text", "class"]
    from_: str | None = Field(default=None, alias="from", max_length=64)
    to: str = Field(max_length=MAX_TEXT)
    confirm: bool = False  # False: dry run (names file:line, writes nothing); True: the owner confirmed that target
    planToken: str | None = Field(default=None, max_length=64)  # noqa: N815 - the dry run's token; confirm needs it

    @model_validator(mode="after")
    def _shape(self) -> EditReq:
        if self.op == "class" and not self.from_:
            raise ValueError("a class swap needs 'from'")
        if self.op == "text" and self.from_ is not None:
            raise ValueError("a text edit takes no 'from'")
        return self


class CaptureReq(Strict):
    device: Literal["phone", "tablet", "desktop"]
    element: Descriptor


# ---------------------------------------------------------------- source location (pure)
def _sources(files: dict[str, str]) -> list[tuple[str, str]]:
    return [(p, c) for p, c in sorted(files.items())
            if p.startswith("src/") and p.endswith(SOURCE_EXT) and p not in KIT_OWNED and isinstance(c, str)]


def _opening_tag_before(content: str, pos: int) -> tuple[str, str] | None:
    """(tag, attributes) of the JSX opening tag that ends right before ``pos`` (whitespace only in between)."""
    m = re.search(r"<([A-Za-z][\w.-]*)((?:\s[^<>]*)?)>\s*$", content[max(0, pos - 600):pos])
    return (m.group(1), m.group(2)) if m else None


# the ``>`` that ends a JSX tag: an opening tag (attributes may hold one level of {braces}, e.g. an arrow function),
# a closing tag, a self-closing tag or a fragment. A ``>`` of a comparison or an arrow is never one.
_TAG_END = re.compile(r"(?:<[A-Za-z][\w.:-]*(?:\s(?:[^<>{}]|\{[^{}]*\})*)?|</[A-Za-z][\w.:-]*\s*|<)>$")


def _closes_jsx_tag(content: str, gt: int) -> bool:
    return gt >= 0 and _TAG_END.search(content[max(0, gt - 600):gt + 1]) is not None


# a string literal used as an import source, a class name, a key or another non-visible attribute is not text
_NOT_TEXT_BEFORE = re.compile(r"(?:\bimport\s*|\bfrom\s*|\brequire\(\s*|\b(?:className|class|style|key|id|type|href|src|"
                              r"role|name|htmlFor|data-[\w-]+|aria-[\w-]+)\s*=\s*\{?\s*)$")


# a ``(`` after these words groups an expression; after any other identifier, ``)`` or ``]`` it opens a call
_NOT_CALLEE = frozenset({"if", "for", "while", "switch", "catch", "return", "typeof", "void", "delete", "await",
                         "yield", "case", "in", "of", "new", "throw", "else", "do", "instanceof"})


def _comment_spans(content: str) -> list[tuple[int, int]]:
    """[start, end) of every ``//`` and ``/* */`` comment, skipping string and template literals. A plain lexer: a
    regex literal or a JSX text holding a quote may confuse it, which only ever hides (never adds) an edit target."""
    spans: list[tuple[int, int]] = []
    i, n = 0, len(content)
    while i < n:
        c = content[i]
        if c == "/" and content.startswith("//", i):
            j = content.find("\n", i)
            j = n if j < 0 else j
            spans.append((i, j))
            i = j
        elif c == "/" and content.startswith("/*", i):
            j = content.find("*/", i + 2)
            j = n if j < 0 else j + 2
            spans.append((i, j))
            i = j
        elif c in "\"'`":
            i += 1
            while i < n and content[i] != c and not (c != "`" and content[i] == "\n"):
                i += 2 if content[i] == "\\" else 1
            i += 1
        else:
            i += 1
    return spans


def _in_comment(spans: list[tuple[int, int]], pos: int) -> bool:
    k = bisect.bisect_right(spans, (pos, 1 << 62)) - 1
    return k >= 0 and spans[k][0] <= pos < spans[k][1]


def _opens_call(content: str, paren: int) -> bool:
    """The ``(`` at ``paren`` opens a call's argument list (``f(``, ``a.b(``, ``f()(``, ``x[0](``)."""
    j = paren - 1
    while j >= 0 and content[j].isspace():
        j -= 1
    if j < 0:
        return False
    if content[j] in ")]":
        return True
    m = re.search(r"[\w$]+$", content[max(0, j - 40):j + 1])
    return m is not None and m.group(0) not in _NOT_CALLEE and not m.group(0)[0].isdigit()


def _is_call_argument(content: str, start: int) -> bool:
    """The literal at ``start`` is directly preceded by ``(`` or ``,`` of a call's argument list."""
    j = start - 1
    while j >= 0 and content[j].isspace():
        j -= 1
    if j < 0 or content[j] not in "(,":
        return False
    if content[j] == "(":
        return _opens_call(content, j)
    depth = 0  # a comma: find the bracket that encloses it
    for k in range(j - 1, max(-1, j - 2000), -1):
        ch = content[k]
        if ch in ")]}":
            depth += 1
        elif ch in "([{":
            if depth == 0:
                return ch == "(" and _opens_call(content, k)
            depth -= 1
    return False


def _text_matches(files: dict[str, str], text: str) -> list[tuple[str, int, int, str]]:
    """Every (path, start, end, kind) where ``text`` is written in the source: as JSX text (any whitespace between
    the words) or as a whole string literal. kind is "jsx" or the quote character."""
    words = text.split()
    if not words:
        return []
    flex = r"\s+".join(re.escape(w) for w in words)
    jsx = re.compile(r"(?<=>)(\s*)(" + flex + r")(\s*)(?=<(?=[A-Za-z/>])|\{)")
    lit = re.compile(r"([\"'`])(" + re.escape(text) + r")\1")
    out: list[tuple[str, int, int, str]] = []
    for path, content in _sources(files):
        if path.endswith((".tsx", ".jsx")):  # .ts/.js have no JSX text, only literals
            out += [(path, m.start(2), m.end(2), "jsx") for m in jsx.finditer(content)
                    if _closes_jsx_tag(content, m.start(1) - 1)]
        spans: list[tuple[int, int]] | None = None
        for m in lit.finditer(content):
            if _NOT_TEXT_BEFORE.search(content[max(0, m.start() - 40):m.start()]):
                continue
            if _is_call_argument(content, m.start()):  # navigate("X"), db.exec("X"): code, not visible text
                continue
            spans = _comment_spans(content) if spans is None else spans
            if _in_comment(spans, m.start()):
                continue
            if m.group(1) == "`" and "${" in m.group(2):
                continue
            out.append((path, m.start(2), m.end(2), m.group(1)))
    return out


def _tie_break(files: dict[str, str], hits: list[tuple[str, int, int, str]], d: Descriptor) -> list[tuple[str, int, int, str]]:
    if len(hits) <= 1:
        return hits
    by_tag = []
    for h in hits:
        tag = _opening_tag_before(files[h[0]], h[1])
        if tag and tag[0] == d.tag:
            by_tag.append(h)
    if len(by_tag) == 1:
        return by_tag
    pool = by_tag or hits
    if d.classes:
        by_cls = []
        for h in pool:
            tag = _opening_tag_before(files[h[0]], h[1])
            if tag and all(re.search(r"(?<![\w-])" + re.escape(c) + r"(?![\w-])", tag[1]) for c in d.classes):
                by_cls.append(h)
        if len(by_cls) == 1:
            return by_cls
    return pool


def _check_new_text(new: str) -> str:
    if not new.strip():
        raise ValidationFailed("the new text is empty")
    if len(new) > MAX_TEXT or _BAD_CHARS.search(new):
        raise ValidationFailed(f"the new text must be one line of at most {MAX_TEXT} characters")
    return new


def plan_text_edit(files: dict[str, str], d: Descriptor, new: str) -> tuple[str, str]:
    """(path, new content) for replacing the element's visible text ``d.text`` with ``new``."""
    _check_new_text(new)
    text = " ".join(d.text.split())
    if not text:
        raise ValidationFailed("the selected element has no text of its own")
    if len(d.text) >= MAX_TEXT:
        raise ValidationFailed("the selected text is too long to edit inline (it was truncated): use the chat")
    hits = _tie_break(files, _text_matches(files, text), d)
    if not hits:
        raise NotFound("this text is not written literally in src/ (it is probably computed): use the chat")
    if len(hits) > 1:
        raise AmbiguousEdit(f"this text appears {len(hits)} times in src/: use the chat to say which one",
                            matches=len(hits))
    path, start, end, kind = hits[0]
    if kind == "jsx":
        if re.search(r"[{}<>]", new):
            raise ValidationFailed("the new text may not contain { } < or >")
        repl = new
    else:
        repl = new.replace("\\", "\\\\").replace(kind, "\\" + kind)
        if kind == "`":
            repl = repl.replace("${", "\\${")
    content = files[path]
    return path, content[:start] + repl + content[end:]


_LITERAL = re.compile(r"\"([^\"\n]*)\"|'([^'\n]*)'|`([^`]*)`")


def plan_class_swap(files: dict[str, str], d: Descriptor, old: str, new: str) -> tuple[str, str]:
    """(path, new content) for replacing the class token ``old`` of the element with ``new`` (same allowed family)."""
    if old not in d.classes:
        raise ValidationFailed("the class is not on the selected element")
    fam_old, fam_new = _FAMILY_OF.get(old), _FAMILY_OF.get(new)
    if fam_old is None or fam_new is None or fam_old != fam_new or old == new:
        raise ValidationFailed("only a colour token or spacing class from the allowed list can be swapped, "
                               "within the same family")
    tok = re.compile(r"(?<![\w-])" + re.escape(old) + r"(?![\w-])")
    hits: list[tuple[str, int, int, set[str]]] = []
    for path, content in _sources(files):
        for m in _LITERAL.finditer(content):
            body = next(g for g in m.groups() if g is not None)
            if tok.search(body):
                hits.append((path, m.start(), m.end(), set(body.split())))
    if len(hits) > 1:  # prefer the static className of exactly this element, then any subset of its classes
        cls = set(d.classes)
        hits = [h for h in hits if h[3] == cls] or [h for h in hits if h[3] <= cls] or hits
    if len(hits) > 1:  # then the literal sitting inside an opening tag of the element's tag
        tag = re.compile(r"<" + re.escape(d.tag) + r"\b[^<>]*$")
        hits = [h for h in hits if tag.search(files[h[0]][max(0, h[1] - 400):h[1]])] or hits
    if not hits:
        raise NotFound("the class is not written literally in src/: use the chat")
    if len(hits) > 1:
        raise AmbiguousEdit(f"this class list appears {len(hits)} times in src/: use the chat", matches=len(hits))
    path, start, end, _ = hits[0]
    content = files[path]
    return path, content[:start] + tok.sub(new, content[start:end]) + content[end:]


def crop_box(r: Rect, width: int, height: int) -> tuple[int, int, int, int]:
    left = max(0, int(r.x) - CROP_PAD)
    top = max(0, int(r.y) - CROP_PAD)
    right = min(width, int(r.x + r.w + 0.999) + CROP_PAD)
    bottom = min(height, int(r.y + r.h + 0.999) + CROP_PAD)
    if right <= left or bottom <= top:
        raise ValidationFailed("the selected element is outside the visible frame")
    return left, top, right, bottom


# ---------------------------------------------------------------- routes
def _first_diff_line(old: str, new: str) -> int:
    """1-based line of the first difference between the current and the edited file."""
    for i, (a, b) in enumerate(zip(old.splitlines(), new.splitlines(), strict=False), 1):
        if a != b:
            return i
    return 1


def plan_token(path: str, line: int, base_revision: int, op: str, old: str, new: str) -> str:
    """sha256 over the dry run the owner saw: path | line | baseRevision | op | old | new (JSON-encoded, so no field
    can bleed into the next). ``old`` is the located text (whitespace-normalised) or the class token replaced."""
    raw = json.dumps([path, line, base_revision, op, old, new])
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


def _pid(pid: str) -> str:
    if not _PID.fullmatch(pid):
        raise ValidationFailed("invalid project id")
    return pid


def _project_files(studio: Any, owner: Actor, pid: str) -> dict[str, str]:
    files = studio.get_project(owner, pid, with_components=False)["settings"].get("files", {})
    return {p: f["content"] for p, f in files.items() if isinstance(f, dict) and isinstance(f.get("content"), str)}


def register(app: FastAPI, studio: Any, owner: Actor, runner: Any) -> None:
    @app.get(f"{API}/projects/{{pid}}/inspector")
    def inspector_options(pid: str) -> dict[str, Any]:
        _pid(pid)
        return {"revision": studio.get_revision(pid), "families": FAMILIES}

    @app.post(f"{API}/projects/{{pid}}/inspector/edit")
    def inspector_edit(pid: str, b: EditReq) -> dict[str, Any]:
        _pid(pid)
        if runner.live_project() is not None:
            raise RunInProgress("the AI is working: wait for the run to finish, then edit", projectId=pid)
        files = _project_files(studio, owner, pid)
        if b.op == "text":
            path, content = plan_text_edit(files, b.element, b.to)
        else:
            path, content = plan_class_swap(files, b.element, b.from_ or "", b.to)
        line = _first_diff_line(files[path], content)
        old = " ".join(b.element.text.split()) if b.op == "text" else (b.from_ or "")
        token = plan_token(path, line, b.baseRevision, b.op, old, b.to)
        if not b.confirm:  # step 1: where the edit would land; the UI shows it and asks the owner
            return {"dryRun": True, "path": path, "line": line, "op": b.op, "planToken": token}
        if not isinstance(b.planToken, str) or not hmac.compare_digest(b.planToken.encode("utf-8"), token.encode()):
            # step 2 must confirm exactly the plan of step 1 (same file, line, revision and change)
            raise PlanMismatch("the confirmed edit differs from the one shown: preview it again", projectId=pid)
        res = studio.write_file(owner, pid, b.baseRevision, path, content)  # revision check + policy + audit
        rebuild = "busy"
        try:
            rebuild = app.state.owner_builds.start(pid, "web").get("state", "building")
        except (StudioError, Unavailable):
            rebuild = "busy"  # a build or run started meanwhile: the change is saved, the owner can rebuild
        return {"path": path, "line": line, "revision": res["revision"],
                "rebuild": rebuild}

    @app.post(f"{API}/projects/{{pid}}/inspector/capture")
    def inspector_capture(pid: str, b: CaptureReq) -> dict[str, Any]:
        from PIL import Image

        _pid(pid)
        studio.get_project(owner, pid, with_components=False)  # NotFound
        shot = capture.capture(studio, pid, [b.device])["captures"][0]
        src = capture.capture_file(studio.settings, shot["sha256"])
        if src is None:
            raise Unavailable("the capture is gone")
        with Image.open(src) as im:
            im.load()
            box = crop_box(b.element.rect, im.width, im.height)
            buf = io.BytesIO()
            im.crop(box).save(buf, format="PNG")
        png = buf.getvalue()
        sha = hashlib.sha256(png).hexdigest()
        d = capture.captures_dir(studio.settings)
        target = d / f"{sha}.png"
        if not target.is_file():
            tmp = d / f".{sha}.{os.getpid()}.partial"
            tmp.write_bytes(png)
            os.replace(tmp, target)
        capture._register_and_prune(studio.settings, pid, [(sha, len(png))])  # noqa: SLF001 - same limits as captures
        return {"sha256": sha, "width": box[2] - box[0], "height": box[3] - box[1], "device": b.device}
