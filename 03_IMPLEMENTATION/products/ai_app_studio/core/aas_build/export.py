"""Export (S9b): web zip (the built dist only) or the debug APK -> the owner's export folder, owner approval required.

Flow (MCP ``app_export`` and REST ``POST /api/v1/projects/<id>/export``):
  1. ``prepare``: the artifact bytes are built in memory once - web: a deterministic zip of the current preview site
     (sorted names, fixed 1980-01-01 timestamps, fixed permissions, DEFLATE level 9; any link / reparse point /
     hard link / non-regular file fails closed; <= 1000 files / 200 MiB); android: the project's APK, which must match
     the sha256 recorded by its ``build.finished`` event.
  2. ``scan``: final secret scan of exactly those bytes (every file of the zip; every entry of the APK decompressed,
     bounded; raw and UTF-16 views). A key pattern or a registered owner secret blocks the export (``export_blocked``,
     audited, finding = pattern + file, never the value). Fail closed: an unreadable entry also blocks.
  3. Approval: policy ``app.export`` is APPROVAL for the owner and for agents. The approval request is bound to
     {projectId, target, sha256}; an identical pending/approved request is reused (no flood). The approved grant is
     consumed atomically inside ``Studio.operation`` (audit row); a changed artifact has another sha256, so the grant
     does not match ("approval was granted for different parameters").
  4. Write: the same in-memory bytes are written to ``<root>/<name>-<id16>/<name>-rev<N>-<sha12>.zip|.apk`` (temp file
     + ``os.replace``; the folder and the target must not be links). ``root`` = owner setting ``export_folder`` or
     ``%USERPROFILE%\\Downloads\\AIAppStudio``; it may not lie inside the studio data root.
"""
from __future__ import annotations

import hashlib
import io
import logging
import os
import re
import stat
import zipfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from sqlalchemy import select

from studio_core import activity, audit
from studio_core.aas_models import ActivityEvent, AppSetting
from studio_core.db import Approval
from studio_core.errors import ApprovalRequired, ConflictError, PolicyDenied, StudioError, Unavailable, ValidationFailed
from studio_core.ids import utcnow
from studio_core.policy import Actor
from studio_core.services import request_hash

from . import service

log = logging.getLogger(__name__)
OPERATION = "app.export"
TARGETS = ("web", "android", "desktop")
FOLDER_KEY = "export_folder"
MAX_FILES = 1000
MAX_BYTES = 200 * 1024 * 1024
MAX_ENTRY_BYTES = 100 * 1024 * 1024
MAX_UNZIPPED = 512 * 1024 * 1024
ZIP_DATE = (1980, 1, 1, 0, 0, 0)
_PID = re.compile(r"^prj_[0-9a-f]{16}$")
_REPARSE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)

# key patterns (bytes); a boundary in front keeps CSS words like "mask-border-..." out of the sk- rule
PATTERNS: tuple[tuple[str, re.Pattern[bytes]], ...] = (
    ("private-key", re.compile(rb"-----BEGIN [A-Z ]{0,40}PRIVATE KEY-----")),
    ("anthropic-key", re.compile(rb"sk-ant-[A-Za-z0-9_-]{20,}")),
    ("openai-key", re.compile(rb"(?<![A-Za-z0-9_-])sk-(?:proj-|svcacct-|admin-)?[A-Za-z0-9_-]{32,}")),
    ("github-token", re.compile(rb"(?<![A-Za-z0-9_])gh[pousr]_[A-Za-z0-9]{36,}")),
    ("github-token", re.compile(rb"github_pat_[A-Za-z0-9_]{30,}")),
    ("aws-key", re.compile(rb"(?<![A-Z0-9])(?:AKIA|ASIA)[0-9A-Z]{16}(?![A-Z0-9])")),
    ("google-key", re.compile(rb"AIza[0-9A-Za-z_-]{35}")),
    ("slack-token", re.compile(rb"xox[abprs]-[0-9A-Za-z-]{10,}")),
    ("stripe-key", re.compile(rb"(?:sk|rk)_live_[0-9A-Za-z]{20,}")),
)


class ExportBlocked(StudioError):
    code = "export_blocked"
    http_status = 422


class BuildRequired(Unavailable):
    """Owner report 2026-11-23 ("Nu merge sa o export APK"): the APK is missing, or was built from an older revision
    than the app the owner sees, or no longer matches its recorded build. The UI builds it (owner build route) and asks
    again; the export never ships a stale APK."""
    code = "build_required"
    http_status = 409


@dataclass
class Artifact:
    project_id: str
    target: str
    data: bytes
    sha256: str
    revision: int
    name: str
    files: list[str] = field(default_factory=list)
    embedded: bytes = b""  # desktop: deterministic zip of the dist embedded in the exe (scan input only)
    store_shots: dict[str, Any] = field(default_factory=dict)  # android: sha256 of each store screenshot + caption
    store_shots_note: str = ""  # android: why stored screenshots were left out (shown to the owner, bound into the hash)

    @property
    def params(self) -> dict[str, Any]:
        p: dict[str, Any] = {"projectId": self.project_id, "target": self.target, "sha256": self.sha256}
        if self.store_shots:  # bound into the approval hash: regenerated/swapped screenshots need a new approval
            p["storeShots"] = dict(self.store_shots)
        if self.store_shots_note:
            p["storeShotsNote"] = self.store_shots_note
        return p


# ---------------------------------------------------------------- folder setting
def default_root() -> Path:
    return Path(os.environ.get("USERPROFILE") or Path.home()) / "Downloads" / "AIAppStudio"


def export_root(studio: Any) -> Path:
    with studio.session() as s:
        row = s.get(AppSetting, FOLDER_KEY)
        return Path(row.value) if row is not None and row.value else default_root()


def _check_root(studio: Any, raw: str) -> Path:
    if not isinstance(raw, str) or not raw or len(raw) > 260 or "\0" in raw:
        raise ValidationFailed("export folder must be an absolute path")
    p = Path(raw)
    if not p.is_absolute() or raw.startswith(("\\\\", "//")):
        raise ValidationFailed("export folder must be an absolute local path (no UNC share)")
    resolved = Path(os.path.abspath(p))
    data = Path(os.path.abspath(studio.settings.data_dir))
    if resolved == data or data in resolved.parents or resolved in data.parents:
        raise ValidationFailed("export folder may not be inside (or contain) the studio data root")
    return resolved


def set_export_root(studio: Any, actor: Actor, raw: str) -> dict[str, Any]:
    """Owner only (audited)."""
    if not getattr(actor, "is_owner", False):
        raise PolicyDenied("only the authenticated human owner can change the export folder")
    p = _check_root(studio, raw)
    with studio.audit_session() as s:
        row = s.get(AppSetting, FOLDER_KEY)
        if row is None:
            row = AppSetting(key=FOLDER_KEY, value="")
            s.add(row)
        row.value = str(p)
        row.updated_at = utcnow()
        audit.record(s, actor, "settings.export_folder", "ok", data={"path": str(p)})
    return {"path": str(p)}


# ---------------------------------------------------------------- prepare
def _slug(name: str) -> str:
    s = re.sub(r"[^A-Za-z0-9_-]+", "-", name or "").strip("-")[:40]
    return s or "app"


def _site_files(root: Path) -> list[tuple[str, bytes]]:
    out: list[tuple[str, bytes]] = []
    total = 0
    stack = [(root, "")]
    while stack:
        d, rel = stack.pop()
        with os.scandir(d) as it:
            entries = sorted(it, key=lambda e: e.name)
        for e in entries:
            st = os.stat(e.path, follow_symlinks=False)  # S9b review L1: DirEntry.stat() has st_nlink 0 on Windows
            r = f"{rel}{e.name}"
            if e.is_symlink() or getattr(st, "st_file_attributes", 0) & _REPARSE:
                raise ValidationFailed("the build output contains a link: export refused", file=r)
            if stat.S_ISDIR(st.st_mode):
                stack.append((Path(e.path), r + "/"))
                continue
            if not stat.S_ISREG(st.st_mode) or st.st_nlink > 1:
                raise ValidationFailed("the build output contains a non-regular file: export refused", file=r)
            total += st.st_size
            if len(out) >= MAX_FILES or total > MAX_BYTES:
                raise ValidationFailed("the build output is over the export limits")
            out.append((r, Path(e.path).read_bytes()))
    return sorted(out)


def _zip(files: list[tuple[str, bytes]]) -> bytes:
    b = io.BytesIO()
    with zipfile.ZipFile(b, "w") as z:
        for name, data in files:
            zi = zipfile.ZipInfo(name, ZIP_DATE)
            zi.compress_type = zipfile.ZIP_DEFLATED
            zi.create_system = 3
            zi.external_attr = (0o100644 & 0xFFFF) << 16
            z.writestr(zi, data, compresslevel=9)
    return b.getvalue()


def recorded_apk_sha(studio: Any, project_id: str, target: str = "android") -> str | None:
    with studio.session() as db:
        q = (select(ActivityEvent).where(ActivityEvent.project_id == project_id, ActivityEvent.kind == "build.finished",
                                         ActivityEvent.artifact_sha256.is_not(None))
             .order_by(ActivityEvent.seq.desc()).limit(50))
        for e in db.execute(q).scalars():
            pl = e.payload or {}
            if pl.get("target") == target and pl.get("status") == "ok":
                return e.artifact_sha256
    return None


def recorded_build(studio: Any, project_id: str, target: str) -> tuple[str, dict[str, Any]] | None:
    """(artifact sha256, payload) of the latest successful ``build.finished`` of ``target``."""
    with studio.session() as db:
        q = (select(ActivityEvent).where(ActivityEvent.project_id == project_id, ActivityEvent.kind == "build.finished",
                                         ActivityEvent.artifact_sha256.is_not(None))
             .order_by(ActivityEvent.seq.desc()).limit(50))
        for e in db.execute(q).scalars():
            pl = e.payload or {}
            if pl.get("target") == target and pl.get("status") == "ok":
                return str(e.artifact_sha256), dict(pl)
    return None


STORE_SHOOT: Any = None  # tests inject a browser stand-in; None = the real capture
STORE_SHOTS_OMITTED = "Capturi pentru magazin omise: nu s-au putut reface"  # owner-facing (Romanian)


def prepare(studio: Any, project_id: str, target: str) -> Artifact:
    if not isinstance(project_id, str) or not _PID.fullmatch(project_id):
        raise ValidationFailed("invalid project id")
    if target not in TARGETS:
        raise ValidationFailed("target must be web, android or desktop")
    project = studio.get_project(Actor("system", "export", "system"), project_id, with_components=False)
    revision = studio.get_revision(project_id)
    slug = _slug(str(project.get("name", "")))
    if target == "web":
        site = service.site_root_for(studio.settings)(project_id[4:])
        if site is None:
            raise Unavailable("nothing to export: build the web target first (app_build target web)")
        files = _site_files(site)
        if not files:
            raise Unavailable("the web build is empty")
        data = _zip(files)
        names = [n for n, _ in files]
    elif target == "desktop":
        # S9c review M1: the exe and the zip of its embedded dist must be the pair of ONE successful build record
        rec = recorded_build(studio, project_id, "desktop")
        bid = str((rec[1] if rec else {}).get("buildId", ""))
        zsha = str((rec[1] if rec else {}).get("distSha256", ""))
        try:
            exe = service.desktop_path(studio.settings, project_id, bid)
            zpath = service.desktop_dist_path(studio.settings, project_id, bid)
        except Unavailable:
            exe = zpath = None  # type: ignore[assignment]
        if rec is None or exe is None:
            raise Unavailable("no desktop exe has been built for this project (app_build target desktop)")
        for f in (exe, zpath):
            if not f.is_file() or f.is_symlink() or os.lstat(f).st_nlink > 1:
                raise Unavailable("no desktop exe has been built for this project (app_build target desktop)")
        if exe.stat().st_size + zpath.stat().st_size > MAX_BYTES:
            raise ValidationFailed("the desktop exe is over the export limit")
        data = exe.read_bytes()
        embedded = zpath.read_bytes()
        if hashlib.sha256(data).hexdigest() != rec[0] or hashlib.sha256(embedded).hexdigest() != zsha:
            raise Unavailable("the exe and its embedded-dist zip do not match one recorded build of this project: "
                              "rebuild (app_build desktop)")
        art = Artifact(project_id, target, data, rec[0], revision, slug, ["app.exe"])
        art.embedded = embedded  # the dist the exe embeds (brotli inside the exe): scanned file by file
        return art
    else:
        apk = service.apk_path(studio.settings, project_id)
        if not apk.is_file() or apk.is_symlink():
            raise BuildRequired("no APK has been built for this project (app_build target android)",
                                target="android", revision=revision)
        rec_a = recorded_build(studio, project_id, "android")
        built_rev = (rec_a[1] if rec_a else {}).get("revision")
        if isinstance(built_rev, int) and built_rev != revision:
            raise BuildRequired(f"the APK was built from revision {built_rev} but the app is at revision {revision}: "
                                "build the APK again (app_build target android)", target="android",
                                revision=revision, builtRevision=built_rev)
        if apk.stat().st_size > MAX_BYTES:
            raise ValidationFailed("the APK is over the export limit")
        data = apk.read_bytes()
        recorded = rec_a[0] if rec_a else None
        if recorded is None or hashlib.sha256(data).hexdigest() != recorded:
            raise BuildRequired("the APK does not match a recorded build of this project: rebuild (app_build android)",
                                target="android", revision=revision)
        names = ["app-debug.apk"]
    sha = hashlib.sha256(data).hexdigest()
    shots_fp: dict[str, Any] = {}
    shots_note = ""
    if target == "android":
        try:
            from . import store_shots
            failed = False
            try:  # wow2 #8: brand kit says store screenshots: shoot the preview now, before approval (never blocks)
                store_shots.ensure_current(studio, project_id, shoot=STORE_SHOOT)
            except Exception as e:  # noqa: BLE001 - screenshots are optional; the APK export goes on without them
                failed = True
                log.warning("store screenshots were not generated for %s: %s", project_id, type(e).__name__)
            shots_fp = store_shots.fingerprint(studio.settings, project_id)
            if failed and shots_fp and store_shots.generated_revision(studio.settings, project_id) != revision:
                shots_fp = {}  # never ship screenshots of an older revision: leave them out and say so
                shots_note = STORE_SHOTS_OMITTED
        except (OSError, ValueError):
            shots_fp = {}
    return Artifact(project_id, target, data, sha, revision, slug, names, store_shots=shots_fp,
                    store_shots_note=shots_note)


# ---------------------------------------------------------------- scan
def scan_bytes(data: bytes) -> list[str]:
    """Names of the key patterns found in ``data`` (raw bytes and, when NULs are present, the UTF-16 view)."""
    views = [data]
    if b"\x00" in data:
        views.append(data.replace(b"\x00", b""))
    found: list[str] = []
    with activity._SECRETS_LOCK:  # noqa: SLF001 - registered owner secrets (owner token)
        owner = [s.encode() for s in activity._SECRETS if s]  # noqa: SLF001
    for v in views:
        for name, pat in PATTERNS:
            if name not in found and pat.search(v):
                found.append(name)
        if "owner-secret" not in found and any(s in v for s in owner):
            found.append("owner-secret")
    return found


def _entries(art: Artifact) -> list[tuple[str, bytes]]:
    if art.target == "desktop":
        out_d: list[tuple[str, bytes]] = [("app.exe", art.data)]
        try:
            with zipfile.ZipFile(io.BytesIO(art.embedded)) as z:
                for i in z.infolist():
                    if i.file_size > MAX_ENTRY_BYTES:
                        raise ExportBlocked("an embedded file is over the scan limit: export refused", file=i.filename[:200])
                    out_d.append(("embedded/" + i.filename, z.read(i)))
        except zipfile.BadZipFile as e:
            raise ExportBlocked("the embedded dist cannot be scanned: export refused") from e
        return out_d
    if art.target == "web":
        with zipfile.ZipFile(io.BytesIO(art.data)) as z:
            return [(i.filename, z.read(i)) for i in z.infolist()]
    out: list[tuple[str, bytes]] = [("app-debug.apk", art.data)]
    total = 0
    try:
        with zipfile.ZipFile(io.BytesIO(art.data)) as z:
            for i in z.infolist():
                if i.file_size > MAX_ENTRY_BYTES or total + i.file_size > MAX_UNZIPPED:
                    raise ExportBlocked("an APK entry is over the scan limit: export refused", file=i.filename[:200])
                total += i.file_size
                out.append((i.filename, z.read(i)))
    except zipfile.BadZipFile as e:
        raise ExportBlocked("the APK cannot be scanned: export refused") from e
    return out


def scan(art: Artifact) -> list[dict[str, str]]:
    findings: list[dict[str, str]] = []
    for name, data in _entries(art):
        for pattern in scan_bytes(data):
            findings.append({"pattern": pattern, "file": name[:200]})
            if len(findings) >= 50:
                return findings
    return findings


# ---------------------------------------------------------------- approval + write
def find_request(studio: Any, art: Artifact) -> dict[str, Any] | None:
    """An open (pending or approved, unused) request for exactly this artifact."""
    h = request_hash(OPERATION, art.params)
    with studio.session() as s:
        a = s.execute(select(Approval).where(Approval.operation == OPERATION, Approval.request_hash == h,
                                             Approval.status.in_(("pending", "approved")))
                      .order_by(Approval.created_at.desc()).limit(1)).scalars().first()
        return None if a is None else {"approvalId": a.id, "status": a.status}


def request(studio: Any, actor: Actor, art: Artifact) -> dict[str, Any]:
    got = find_request(studio, art)
    if got is not None:
        return got
    res = studio.request_approval(actor, OPERATION, art.params, project_id=art.project_id)
    try:
        activity.record(studio, "approval.requested", project_id=art.project_id,
                        payload={"approvalId": res["approvalId"], "operation": OPERATION})
    except Exception:  # noqa: BLE001 - the timeline never breaks the request
        activity._note_dropped(studio)  # noqa: SLF001
    return {"approvalId": res["approvalId"], "status": "pending"}


def project_folder(studio: Any, project_id: str, dest_root: Path | None = None) -> Path:
    """``<export root>/<name>-<id16>``: where ``write`` puts this project's exports (computed, never from a request)."""
    if not isinstance(project_id, str) or not _PID.fullmatch(project_id):
        raise ValidationFailed("invalid project id")
    project = studio.get_project(Actor("system", "export", "system"), project_id, with_components=False)
    root = Path(dest_root) if dest_root is not None else export_root(studio)
    return root / f"{_slug(str(project.get('name', '')))}-{project_id[4:]}"


def status_of(studio: Any, approval_id: str) -> str:
    with studio.session() as s:
        a = s.get(Approval, approval_id)
        return a.status if a is not None and a.operation == OPERATION else "missing"


def _safe_dir(p: Path) -> None:
    cur = p
    while True:
        if cur.is_symlink() or (cur.exists() and getattr(cur.lstat(), "st_file_attributes", 0) & _REPARSE
                                and cur != Path(cur.anchor)):
            raise ValidationFailed("the export folder (or a parent) is a link: export refused")
        if cur.parent == cur:
            return
        cur = cur.parent


def write(studio: Any, actor: Actor, art: Artifact, approval_id: str, dest_root: Path | None = None) -> dict[str, Any]:
    root = Path(dest_root) if dest_root is not None else export_root(studio)
    _safe_dir(root / f"{art.name}-{art.project_id[4:]}")  # S9b review L2: no link in the chain BEFORE any mkdir

    def _do(ctx: Any) -> dict[str, Any]:
        if art.target == "android":  # nothing is written if the approved screenshots are no longer the stored ones
            from . import store_shots
            if not art.store_shots_note and store_shots.fingerprint(studio.settings, art.project_id) != art.store_shots:
                raise ConflictError("the store screenshots changed after the owner approved the export: approve again")
        folder = root / f"{art.name}-{art.project_id[4:]}"
        _safe_dir(folder)
        folder.mkdir(parents=True, exist_ok=True)
        _safe_dir(folder)
        ext = {"web": ".zip", "android": ".apk", "desktop": ".exe"}[art.target]
        target = folder / f"{art.name}-rev{art.revision}-{art.sha256[:12]}{ext}"
        if target.is_symlink():
            raise ValidationFailed("the export target is a link: export refused")
        tmp = folder / f".{target.name}.{os.getpid()}.partial"
        fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_BINARY", 0), 0o600)
        with os.fdopen(fd, "wb") as f:
            f.write(art.data)
        os.replace(tmp, target)
        shots: list[str] = []
        if art.target == "android" and not art.store_shots_note:  # W5: framed store screenshots travel with the APK
            try:
                from . import store_shots
                shots = store_shots.copy_for_export(studio.settings, art.project_id, folder, art.store_shots)
            except (OSError, ValueError):
                shots = []
        ctx.verification = "verified"
        res = {"status": "exported", "target": art.target, "path": str(target), "sha256": art.sha256,
               "bytes": len(art.data), "revision": art.revision, "approvalId": approval_id}
        if shots:
            res["storeScreenshots"] = shots
        if art.store_shots_note:
            res["storeScreenshotsNote"] = art.store_shots_note
        return res
    return studio.operation(actor, OPERATION, OPERATION, art.params, _do, project_id=art.project_id,
                            approval_id=approval_id)


def checked(studio: Any, actor: Actor, project_id: str, target: str) -> Artifact:
    """prepare + final secret scan; a finding blocks (audited) before any approval is requested."""
    art = prepare(studio, project_id, target)
    findings = scan(art)
    if findings:
        with studio.audit_session() as s:
            audit.record(s, actor, "app.export", "blocked", project_id=project_id,
                         data={"target": target, "sha256": art.sha256, "findings": findings})
        raise ExportBlocked("export refused: the artifact contains secret-looking values (remove them and rebuild)",
                            findings=findings, sha256=art.sha256)
    return art


def export(studio: Any, actor: Actor, project_id: str, target: str, *, approval_id: str | None = None,
           dest_root: Path | None = None) -> dict[str, Any]:
    """Two steps: without ``approval_id`` -> ApprovalRequired {approvalId, sha256}; with an approved id -> written."""
    art = checked(studio, actor, project_id, target)
    if not approval_id:
        req = request(studio, actor, art)
        raise ApprovalRequired("exporting needs the owner's approval of this exact artifact",
                               approvalId=req["approvalId"], sha256=art.sha256, target=target, status=req["status"],
                               **({"storeShotsNote": art.store_shots_note} if art.store_shots_note else {}))
    return write(studio, actor, art, approval_id, dest_root)
