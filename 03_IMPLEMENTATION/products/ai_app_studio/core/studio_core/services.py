"""Application core: the ONLY place that mutates project state.

REST, MCP, GUI (via REST) and computer-use all call Studio methods. Every call goes through
Studio.operation(): validate -> policy -> approval -> apply -> verify -> audit, and writes a
ToolExecution row (operation id, project, revisions, actor, tool, params, result, verification).
"""

from __future__ import annotations

import hashlib
import json
import re
import shutil
import threading
import zipfile
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, TypeVar

from sqlalchemy import select, update
from sqlalchemy.orm import Session

from . import audit, project_kind
from .config import Settings
from .db import (
    Approval,
    AuditEvent,
    ChangeSet,
    Component,
    Project,
    ProjectRevision,
    ToolExecution,
    make_engine,
    make_session_factory,
)
from .errors import (
    ApprovalRequired,
    ConflictError,
    NotFound,
    PolicyDenied,
    StudioError,
    ValidationFailed,
)
from .ids import iso, new_id, utcnow
from .policy import Actor, PolicyEngine
from .project_kind import ProjectKind
from .workspace import Workspace

T = TypeVar("T")
_AUDIT_THREAD_LOCK = threading.RLock()
_NAME_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9 _.\-]{0,63}$")


@dataclass
class OpContext:
    operation_id: str
    actor: Actor
    session: Session
    base_revision: int | None = None
    result_revision: int | None = None
    verification: str = "unverified"
    notes: list[str] = field(default_factory=list)
    git: dict[str, Any] | None = None


class EventBus:
    """In-process pub/sub. The backend also tails audit_events to see changes made by the MCP process."""

    def __init__(self) -> None:
        self._subs: list[Callable[[dict[str, Any]], None]] = []
        self._lock = threading.Lock()

    def subscribe(self, fn: Callable[[dict[str, Any]], None]) -> Callable[[], None]:
        with self._lock:
            self._subs.append(fn)

        def _unsub() -> None:
            with self._lock:
                if fn in self._subs:
                    self._subs.remove(fn)
        return _unsub

    def publish(self, event: dict[str, Any]) -> None:
        with self._lock:
            subs = list(self._subs)
        for fn in subs:
            try:
                fn(event)
            except Exception:  # noqa: BLE001,S110 - one bad subscriber must not break the core
                pass


def component_to_dict(c: Component) -> dict[str, Any]:
    return {
        "id": c.id, "type": c.type, "version": c.schema_version, "name": c.name,
        "parentId": c.parent_id, "position": c.position,
        "properties": dict(c.properties or {}), "styles": dict(c.styles or {}),
        "responsive": dict(c.responsive or {}), "metadata": dict(c.meta or {}),
    }


def project_to_dict(p: Project) -> dict[str, Any]:
    return {
        "id": p.id, "name": p.name, "description": p.description, "template": p.template,
        "revision": p.revision, "status": p.status, "settings": dict(p.settings or {}),
        "createdAt": iso(p.created_at), "updatedAt": iso(p.updated_at),
    }


def request_hash(operation: str, params: dict[str, Any]) -> str:
    raw = json.dumps({"op": operation, "params": params}, sort_keys=True, default=str)
    return hashlib.sha256(raw.encode()).hexdigest()


class Studio:
    def __init__(self, settings: Settings, *, migrate: bool = True, kind: str | None = None) -> None:
        self.settings = settings
        # AAS seam: the project kind is resolved from the registry on use (no website modules imported here)
        self.kind_name = kind or project_kind.DEFAULT_KIND
        settings.ensure_dirs()
        self.engine = make_engine(settings.db_url)
        if migrate:
            from .migrations import upgrade_to_head
            upgrade_to_head(settings.db_url)
        self.Session = make_session_factory(self.engine)
        self.policy = PolicyEngine()
        self.events = EventBus()

    @property
    def kind(self) -> ProjectKind:
        return project_kind.resolve(self.kind_name)

    # ------------------------------------------------------------------ infrastructure
    @contextmanager
    def session(self) -> Iterator[Session]:
        s: Session = self.Session()
        try:
            yield s
            s.commit()
        except Exception:
            s.rollback()
            raise
        finally:
            s.close()

    @contextmanager
    def audit_session(self) -> Iterator[Session]:
        """Session for writes that append audit events: serialised across threads AND processes
        (the MCP server and the backend share the database), then the external anchor is updated."""
        from .migrations import interprocess_lock
        with _AUDIT_THREAD_LOCK, interprocess_lock(self.settings.data_dir / "audit.lock"):
            with self.session() as s:
                yield s
            with self.session() as s2:
                audit.write_anchor(s2, self.settings.data_dir / "audit" / "anchor.json")

    def workspace(self, project_id: str, *, create: bool = True) -> Workspace:
        if not re.match(r"^prj_[0-9a-f]{16}$", project_id):
            raise ValidationFailed("invalid project id", projectId=project_id)
        return Workspace(self.settings.projects_dir / project_id, create=create)

    def operation(self, actor: Actor, tool: str, operation: str, params: dict[str, Any],
                  fn: Callable[[OpContext], T], *, project_id: str | None = None,
                  base_revision: int | None = None, approval_id: str | None = None) -> T:
        op_id = new_id("op")
        safe_params = audit.redact(params)
        status: str = "error"
        result_payload: Any = {}
        ctx: OpContext | None = None
        try:
            approved = False
            try:
                self.policy.enforce(actor, operation)
            except ApprovalRequired:
                if not approval_id:
                    raise
                # consume in its own committed transaction: the operation body may run nested operations
                # (each needs its own write connection) and SQLite allows one writer at a time
                with self.session() as s0:
                    self._consume_approval(s0, actor, approval_id, operation, params)
                approved = True
            try:
                with self.session() as s:
                    ctx = OpContext(op_id, actor, s, base_revision=base_revision)
                    result = fn(ctx)
                    if approved:
                        ctx.notes.append(f"approval {approval_id} consumed")
            except Exception:
                if approved:  # compensate: the approved action did not happen, so the grant is still usable
                    with self.session() as s1:
                        s1.execute(update(Approval).where(Approval.id == approval_id, Approval.status == "used")
                                   .values(status="approved", updated_at=utcnow()))
                raise
            status = "ok"
            result_payload = result if isinstance(result, dict) else {"value": str(result)[:500]}
            return result
        except PolicyDenied as e:
            status, result_payload = "denied", e.to_dict()
            raise
        except ApprovalRequired as e:
            status = "approval_required"
            result_payload = e.to_dict()
            raise
        except StudioError as e:
            result_payload = e.to_dict()
            raise
        except Exception as e:
            result_payload = {"error": "internal", "message": type(e).__name__}
            raise
        finally:
            with self.audit_session() as s:
                s.add(ToolExecution(
                    id=op_id, project_id=project_id, base_revision=base_revision,
                    result_revision=ctx.result_revision if ctx else None, actor=actor.label,
                    channel=actor.channel, tool=tool, params=safe_params,
                    result=audit.redact(_jsonable(result_payload)), status=status,
                    verification=ctx.verification if ctx else "not_run", finished_at=utcnow()))
                audit.record(s, actor, tool, status, project_id=project_id, operation_id=op_id,
                             data={"params": safe_params, "verification": ctx.verification if ctx else None,
                                   "resultRevision": ctx.result_revision if ctx else None})
            self.events.publish({"type": "operation", "operationId": op_id, "tool": tool, "status": status,
                                 "projectId": project_id, "actor": actor.label,
                                 "revision": ctx.result_revision if ctx else None})

    # ------------------------------------------------------------------ approvals
    def request_approval(self, actor: Actor, operation: str, params: dict[str, Any],
                         project_id: str | None = None) -> dict[str, Any]:
        with self.audit_session() as s:
            a = Approval(id=new_id("apr"), operation=operation, project_id=project_id,
                         request_hash=request_hash(operation, params), requested_by=actor.label,
                         params=audit.redact(params))
            s.add(a)
            audit.record(s, actor, "approval.request", "pending", project_id=project_id,
                         data={"approvalId": a.id, "operation": operation})
        self.events.publish({"type": "approval_requested", "approvalId": a.id, "operation": operation})
        return {"approvalId": a.id, "operation": operation, "status": "pending", "requestHash": a.request_hash}

    def decide_approval(self, actor: Actor, approval_id: str, approve: bool) -> dict[str, Any]:
        # Agents are denied here by policy; MCP has no tool that reaches this method.
        self.policy.enforce(actor, "approval.decide")
        if not actor.is_owner:
            raise PolicyDenied("only the authenticated human owner can decide approvals")
        with self.audit_session() as s:
            a = s.get(Approval, approval_id)
            if a is None:
                raise NotFound("approval not found", approvalId=approval_id)
            if a.status != "pending":
                raise ValidationFailed(f"approval is already {a.status}")
            a.status = "approved" if approve else "rejected"
            a.decided_by = actor.label
            audit.record(s, actor, "approval.decide", a.status, project_id=a.project_id,
                         data={"approvalId": a.id})
        return {"approvalId": approval_id, "status": "approved" if approve else "rejected"}

    def revoke_approval(self, actor: Actor, approval_id: str, operation: str | None = None) -> dict[str, Any]:
        """Owner-only: withdraw a still-valid (approved, unused) grant."""
        self.policy.enforce(actor, "approval.decide")
        if not actor.is_owner:
            raise PolicyDenied("only the authenticated human owner can revoke approvals")
        with self.audit_session() as s:
            q = update(Approval).where(Approval.id == approval_id, Approval.status == "approved")
            if operation:
                q = q.where(Approval.operation == operation)
            res = s.execute(q.values(status="revoked", decided_by=actor.label, updated_at=utcnow()))
            if res.rowcount != 1:  # type: ignore[attr-defined]
                raise NotFound("no approved grant with this id", approvalId=approval_id)
            audit.record(s, actor, "approval.revoke", "revoked", data={"approvalId": approval_id})
        return {"approvalId": approval_id, "status": "revoked"}

    def list_approvals(self, status: str | None = "pending") -> list[dict[str, Any]]:
        with self.session() as s:
            q = select(Approval).order_by(Approval.created_at.desc())
            if status:
                q = q.where(Approval.status == status)
            return [{"approvalId": a.id, "operation": a.operation, "projectId": a.project_id,
                     "status": a.status, "requestedBy": a.requested_by, "params": a.params,
                     "createdAt": iso(a.created_at)} for a in s.execute(q).scalars()]

    def _consume_approval(self, s: Session, actor: Actor, approval_id: str, operation: str,
                          params: dict[str, Any]) -> None:
        # single atomic conditional UPDATE: of two concurrent consumers (threads or processes)
        # exactly one gets rowcount == 1
        res = s.execute(update(Approval)
                        .where(Approval.id == approval_id, Approval.operation == operation,
                               Approval.status == "approved",
                               Approval.request_hash == request_hash(operation, params))
                        .values(status="used", updated_at=utcnow()))
        if res.rowcount != 1:  # type: ignore[attr-defined]
            a = s.get(Approval, approval_id)
            if a is None or a.operation != operation:
                raise ApprovalRequired("approval does not exist for this operation")
            if a.request_hash != request_hash(operation, params):
                raise ApprovalRequired("approval was granted for different parameters")
            raise ApprovalRequired(f"approval is {a.status}, not approved")

    # ------------------------------------------------------------------ revision engine
    def _lock_revision(self, ctx: OpContext, project_id: str) -> Project:
        s = ctx.session
        p = s.get(Project, project_id)
        if p is None:
            raise NotFound("project not found", projectId=project_id)
        if ctx.base_revision is None:
            raise ValidationFailed("baseRevision is required for mutations", currentRevision=p.revision)
        res = s.execute(update(Project).where(Project.id == project_id, Project.revision == ctx.base_revision)
                        .values(revision=Project.revision + 1, updated_at=utcnow()))
        if res.rowcount != 1:  # type: ignore[attr-defined]
            s.refresh(p)
            raise ConflictError("project changed since baseRevision; fetch latest state and rebase",
                                baseRevision=ctx.base_revision, currentRevision=p.revision)
        s.refresh(p)
        ctx.result_revision = p.revision
        return p

    def _snapshot(self, s: Session, p: Project) -> dict[str, Any]:
        comps = s.execute(select(Component).where(Component.project_id == p.id)).scalars().all()
        return {"project": {"name": p.name, "settings": dict(p.settings or {})},
                "components": sorted((component_to_dict(c) for c in comps), key=lambda c: c["id"])}

    def _commit_revision(self, ctx: OpContext, p: Project, summary: str, before: dict[str, Any],
                         nav: dict[str, int] | None = None) -> dict[str, Any]:
        s = ctx.session
        s.flush()
        snap = self._snapshot(s, p)
        if nav:
            snap["_nav"] = nav
        s.add(ProjectRevision(id=new_id("rev"), project_id=p.id, revision=p.revision,
                              operation_id=ctx.operation_id, actor=ctx.actor.label, summary=summary, snapshot=snap))
        s.add(ChangeSet(id=new_id("chg"), project_id=p.id, operation_id=ctx.operation_id,
                        from_revision=p.revision - 1, to_revision=p.revision, diff=_diff(before, snap)))
        self._render(p, snap)
        if self.settings.git_enabled:
            from . import gitrepo
            ctx.git = gitrepo.commit_revision(self.workspace(p.id).root, self._git_model(p, snap), p.revision, summary,
                                              ctx.actor.label, ctx.operation_id)
        # verification: re-read revision row inside the same transaction
        current = s.execute(select(Project.revision).where(Project.id == p.id)).scalar_one()
        ctx.verification = "verified" if current == ctx.base_revision + 1 else "failed"  # type: ignore[operator]
        if ctx.verification != "verified":
            raise ConflictError("revision verification failed", expected=ctx.base_revision + 1, actual=current)  # type: ignore[operator]
        return snap

    def _render(self, p: Project, snap: dict[str, Any]) -> None:
        # AAS seam: materialising the model into the workspace belongs to the project kind
        self.kind.build(project_to_dict(p), snap, self.workspace(p.id))

    # ------------------------------------------------------------------ projects
    def create_project(self, actor: Actor, name: str, template: str = "blank", description: str = "") -> dict[str, Any]:
        params = {"name": name, "template": template, "description": description}

        def _do(ctx: OpContext) -> dict[str, Any]:
            if not _NAME_RE.match(name or ""):
                raise ValidationFailed("project name must be 1-64 chars: letters, digits, space, _ . -")
            initial = self.kind.initial_settings(template)  # AAS seam: raises ValidationFailed on unknown template
            s = ctx.session
            if s.execute(select(Project).where(Project.name == name)).scalar_one_or_none():
                raise ValidationFailed("a project with this name already exists", name=name)
            p = Project(id=new_id("prj"), name=name, description=description[:2000], template=template,
                        revision=0, settings=initial)
            s.add(p)
            s.flush()
            ctx.base_revision = 0
            p = self._lock_revision(ctx, p.id)
            before: dict[str, Any] = {"components": []}
            self._commit_revision(ctx, p, f"create project from template {template}", before)
            return project_to_dict(p)

        return self.operation(actor, "project.create", "project.create", params, _do)

    def list_projects(self, actor: Actor, include_archived: bool = False) -> list[dict[str, Any]]:
        def _do(ctx: OpContext) -> list[dict[str, Any]]:
            q = select(Project).order_by(Project.updated_at.desc())
            if not include_archived:
                q = q.where(Project.status == "active")
            ctx.verification = "read"
            return [project_to_dict(p) for p in ctx.session.execute(q).scalars()]
        return self.operation(actor, "project.list", "project.read", {"includeArchived": include_archived}, _do)

    def get_project(self, actor: Actor, project_id: str, *, with_components: bool = True) -> dict[str, Any]:
        def _do(ctx: OpContext) -> dict[str, Any]:
            p = ctx.session.get(Project, project_id)
            if p is None:
                raise NotFound("project not found", projectId=project_id)
            d = project_to_dict(p)
            if with_components:
                d["components"] = self._snapshot(ctx.session, p)["components"]
            ctx.verification = "read"
            return d
        return self.operation(actor, "project.inspect", "project.read", {"projectId": project_id}, _do,
                              project_id=project_id)

    def get_revision(self, project_id: str) -> int:
        with self.session() as s:
            rev = s.execute(select(Project.revision).where(Project.id == project_id)).scalar_one_or_none()
            if rev is None:
                raise NotFound("project not found", projectId=project_id)
            return int(rev)

    def update_project(self, actor: Actor, project_id: str, base_revision: int, *, name: str | None = None,
                       description: str | None = None, status: str | None = None) -> dict[str, Any]:
        params = {"projectId": project_id, "name": name, "description": description, "status": status}
        op = "project.archive" if status == "archived" else "project.write"

        def _do(ctx: OpContext) -> dict[str, Any]:
            p = self._lock_revision(ctx, project_id)
            before = self._snapshot(ctx.session, p)
            if name is not None:
                if not _NAME_RE.match(name):
                    raise ValidationFailed("invalid project name")
                p.name = name
            if description is not None:
                p.description = description[:2000]
            if status is not None:
                if status not in ("active", "archived"):
                    raise ValidationFailed("status must be active|archived")
                p.status = status
            self._commit_revision(ctx, p, "update project", before)
            return project_to_dict(p)
        return self.operation(actor, "project.update", op, params, _do, project_id=project_id,
                              base_revision=base_revision)

    def delete_project(self, actor: Actor, project_id: str, approval_id: str | None = None) -> dict[str, Any]:
        """Requires an approved Approval bound to exactly these parameters. Files go to data/trash."""
        params = {"projectId": project_id}

        def _do(ctx: OpContext) -> dict[str, Any]:
            p = ctx.session.get(Project, project_id)
            if p is None:
                raise NotFound("project not found", projectId=project_id)
            src = self.settings.projects_dir / project_id
            trash = self.settings.data_dir / "trash" / f"{project_id}-{utcnow().strftime('%Y%m%dT%H%M%S')}"
            if src.exists():
                trash.parent.mkdir(parents=True, exist_ok=True)
                shutil.move(str(src), str(trash))
            ctx.session.query(ProjectRevision).filter_by(project_id=project_id).delete()
            ctx.session.delete(p)
            ctx.verification = "verified"
            return {"deleted": project_id, "filesMovedTo": str(trash) if src.exists() or trash.exists() else None}
        return self.operation(actor, "project.delete", "project.delete", params, _do, project_id=project_id,
                              approval_id=approval_id)

    def export_project(self, actor: Actor, project_id: str) -> dict[str, Any]:
        def _do(ctx: OpContext) -> dict[str, Any]:
            p = ctx.session.get(Project, project_id)
            if p is None:
                raise NotFound("project not found", projectId=project_id)
            self._render(p, self._snapshot(ctx.session, p))
            ws = self.workspace(project_id)
            pub = self.kind.publish(project_to_dict(p), ws)  # AAS seam: the kind names what is exported
            out = ws.resolve(f"exports/{p.id}-rev{p.revision}.zip")
            out.parent.mkdir(parents=True, exist_ok=True)
            skipped: list[str] = []
            with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
                for rel in ws.iter_files(pub.root):  # never follows symlinks/junctions; each path re-resolved
                    if rel in pub.exclude:
                        continue
                    try:
                        z.writestr(rel.removeprefix(f"{pub.root}/"), ws.read_bytes(rel))
                    except StudioError:
                        skipped.append(rel)
            data = out.read_bytes()
            with zipfile.ZipFile(out) as z:
                names = z.namelist()
            ctx.verification = "verified" if all(r in names for r in pub.required) else "failed"
            return {"projectId": p.id, "revision": p.revision, "path": str(out), "files": names, "skipped": skipped,
                    "sha256": hashlib.sha256(data).hexdigest(), "size": len(data)}
        return self.operation(actor, "project.export", "project.export", {"projectId": project_id}, _do,
                              project_id=project_id)

    def history(self, actor: Actor, project_id: str, limit: int = 50) -> list[dict[str, Any]]:
        def _do(ctx: OpContext) -> list[dict[str, Any]]:
            q = (select(ProjectRevision).where(ProjectRevision.project_id == project_id)
                 .order_by(ProjectRevision.revision.desc()).limit(min(limit, 500)))
            ctx.verification = "read"
            return [{"revision": r.revision, "operationId": r.operation_id, "actor": r.actor,
                     "summary": r.summary, "createdAt": iso(r.created_at)} for r in ctx.session.execute(q).scalars()]
        return self.operation(actor, "project.history", "project.read", {"projectId": project_id}, _do,
                              project_id=project_id)

    # ------------------------------------------------------------------ mutations
    # AAS seam: website component/theme methods (add/update/remove/move_component, set_theme) are not vendored
    def _mutate(self, actor: Actor, tool: str, project_id: str, base_revision: int, params: dict[str, Any],
                summary: str, change: Callable[[Session, Project], dict[str, Any]],
                operation: str = "project.write") -> dict[str, Any]:
        def _do(ctx: OpContext) -> dict[str, Any]:
            p = self._lock_revision(ctx, project_id)
            before = self._snapshot(ctx.session, p)
            out = change(ctx.session, p)
            self._commit_revision(ctx, p, summary, before)
            return {"projectId": project_id, "revision": p.revision, "operationId": ctx.operation_id,
                    "verification": ctx.verification, "git": ctx.git, **out}
        return self.operation(actor, tool, operation, params, _do, project_id=project_id, base_revision=base_revision)

    # ------------------------------------------------------------------ code files (spec §6.6)
    def list_files(self, actor: Actor, project_id: str) -> list[dict[str, Any]]:
        files = self.get_project(actor, project_id, with_components=False)["settings"].get("files", {})
        return [{"path": k, "language": v["language"], "bytes": len(v["content"].encode("utf-8"))} for k, v in sorted(files.items())]

    def read_file(self, actor: Actor, project_id: str, path: str) -> dict[str, Any]:
        files = self.get_project(actor, project_id, with_components=False)["settings"].get("files", {})
        f = files.get(path)
        if f is None:
            raise NotFound("file not found", path=path)
        return {"path": path, "language": f["language"], "content": f["content"], "removed": f.get("removed", [])}

    def write_file(self, actor: Actor, project_id: str, base_revision: int, path: str, content: str) -> dict[str, Any]:
        kind = self.kind
        info = kind.validate(path, content)  # AAS seam: validated by the kind before any lock/transaction

        def change(s: Session, p: Project) -> dict[str, Any]:
            files = dict((p.settings or {}).get("files", {}))
            if path not in files and len(files) >= kind.max_files:
                raise ValidationFailed(f"at most {kind.max_files} code files per project")
            files[path] = {"content": content, **info}
            total = sum(len(f["content"].encode("utf-8")) for f in files.values())
            if total > kind.max_total_bytes:
                raise ValidationFailed(f"total size of code files exceeds {kind.max_total_bytes} bytes")
            p.settings = {**(p.settings or {}), "files": files}
            return {"path": path, "language": info["language"], "removed": info.get("removed", [])}
        return self._mutate(actor, "code.write_file", project_id, base_revision, {"path": path, "bytes": len(content)},
                            f"write {path}", change)

    def delete_file(self, actor: Actor, project_id: str, base_revision: int, path: str) -> dict[str, Any]:
        kind = self.kind

        def change(s: Session, p: Project) -> dict[str, Any]:
            files = dict((p.settings or {}).get("files", {}))
            kind.validate(path)  # AAS seam: path-only check
            if path not in files:
                raise NotFound("file not found", path=path)
            del files[path]
            p.settings = {**(p.settings or {}), "files": files}
            return {"deleted": path}
        return self._mutate(actor, "code.delete_file", project_id, base_revision, {"path": path}, f"delete {path}", change)

    def restore_revision(self, actor: Actor, project_id: str, base_revision: int, target: int,
                         _nav: dict[str, int] | None = None, tool: str = "project.restore_revision") -> dict[str, Any]:
        def _do(ctx: OpContext) -> dict[str, Any]:
            s = ctx.session
            snap_row = s.execute(select(ProjectRevision).where(ProjectRevision.project_id == project_id,
                                                               ProjectRevision.revision == target)).scalar_one_or_none()
            if snap_row is None:
                raise NotFound("revision not found", revision=target)
            p = self._lock_revision(ctx, project_id)
            before = self._snapshot(s, p)
            self._apply_components(s, p, snap_row.snapshot["components"], snap_row.snapshot["project"]["settings"])
            self._commit_revision(ctx, p, f"restore revision {target}", before, nav=_nav)
            return {"projectId": project_id, "revision": p.revision, "restoredFrom": target,
                    "operationId": ctx.operation_id, "verification": ctx.verification, "git": ctx.git}
        return self.operation(actor, tool, "project.write", {"projectId": project_id, "target": target}, _do,
                              project_id=project_id, base_revision=base_revision)

    def _apply_components(self, s: Session, p: Project, components: list[dict[str, Any]], settings: dict[str, Any]) -> None:
        """Replace the whole component tree (restore from a revision snapshot or a git commit).
        AAS seam: project kinds have no website components, so a model that carries any is rejected."""
        if components:
            raise ValidationFailed("this project kind has no components; refusing to restore a model with components",
                                   components=len(components))
        for c in s.execute(select(Component).where(Component.project_id == p.id)).scalars().all():
            s.delete(c)
        s.flush()
        p.settings = settings

    def _git_model(self, p: Project, snap: dict[str, Any]) -> dict[str, Any]:
        return {"schema": "aws.project/1", "revision": p.revision,
                "project": {"id": p.id, "name": p.name, "description": p.description, "template": p.template,
                            "status": p.status, "settings": dict(p.settings or {})},
                "components": snap["components"]}

    # ------------------------------------------------------------------ git (spec §11)
    def git_log(self, actor: Actor, project_id: str, limit: int = 100) -> list[dict[str, Any]]:
        from . import gitrepo

        def _do(ctx: OpContext) -> list[dict[str, Any]]:
            ctx.verification = "read"
            return gitrepo.log(self.workspace(project_id).root, limit)
        return self.operation(actor, "project.git_log", "project.read", {"projectId": project_id}, _do, project_id=project_id)

    def git_diff(self, actor: Actor, project_id: str, a: str, b: str) -> dict[str, Any]:
        from . import gitrepo

        def _do(ctx: OpContext) -> dict[str, Any]:
            ctx.verification = "read"
            return gitrepo.diff_stat(self.workspace(project_id).root, a, b)
        return self.operation(actor, "project.git_diff", "project.read", {"projectId": project_id, "a": a, "b": b}, _do,
                              project_id=project_id)

    def restore_from_git(self, actor: Actor, project_id: str, base_revision: int, commit: str) -> dict[str, Any]:
        """New revision whose content equals the model stored in a git commit (history is never rewritten)."""
        from . import gitrepo

        def _do(ctx: OpContext) -> dict[str, Any]:
            model = gitrepo.model_at(self.workspace(project_id).root, commit)
            if model.get("project", {}).get("id") != project_id:
                raise ValidationFailed("commit belongs to another project")
            p = self._lock_revision(ctx, project_id)
            before = self._snapshot(ctx.session, p)
            self._apply_components(ctx.session, p, model["components"], model["project"]["settings"])
            self._commit_revision(ctx, p, f"restore from git {commit[:10]} (rev {model.get('revision')})", before)
            return {"projectId": project_id, "revision": p.revision, "restoredFromCommit": commit,
                    "restoredFromRevision": model.get("revision"), "operationId": ctx.operation_id, "git": ctx.git}
        return self.operation(actor, "project.git_restore", "project.write", {"projectId": project_id, "commit": commit}, _do,
                              project_id=project_id, base_revision=base_revision)

    def recover_from_git(self, actor: Actor, project_id: str) -> dict[str, Any]:
        """Rebuild a project that exists on disk (with git history) but not in the database (disaster recovery)."""
        from . import gitrepo

        def _do(ctx: OpContext) -> dict[str, Any]:
            s = ctx.session
            if s.get(Project, project_id) is not None:
                raise ValidationFailed("project already exists in the database")
            root = self.settings.projects_dir / project_id
            if not (root / ".git").is_dir():
                raise NotFound("no git repository for this project id", projectId=project_id)
            head = gitrepo.log(self.workspace(project_id).root, 1)[0]["commit"]
            model = gitrepo.model_at(self.workspace(project_id).root, head)
            meta = model["project"]
            if meta.get("id") != project_id or not _NAME_RE.match(meta.get("name", "")):
                raise ValidationFailed("git model does not match the project id")
            name = meta["name"]
            if s.execute(select(Project).where(Project.name == name)).scalar_one_or_none():
                name = f"{name[:50]} (recovered)"
            p = Project(id=project_id, name=name, description=meta.get("description", ""), template=meta.get("template", "blank"),
                        revision=int(model.get("revision", 0)), status=meta.get("status", "active"), settings={})
            s.add(p)
            s.flush()
            ctx.base_revision = p.revision
            p = self._lock_revision(ctx, project_id)
            self._apply_components(s, p, model["components"], meta.get("settings", {}))
            self._commit_revision(ctx, p, f"recovered from git {head[:10]}", {"components": []})
            return {"projectId": project_id, "name": name, "revision": p.revision, "recoveredFromCommit": head,
                    "components": len(model["components"]), "git": ctx.git}
        return self.operation(actor, "project.recover_from_git", "project.create", {"projectId": project_id}, _do,
                              project_id=project_id)

    def _latest_nav(self, project_id: str) -> tuple[int, dict[str, int] | None]:
        with self.session() as s:
            r = s.execute(select(ProjectRevision).where(ProjectRevision.project_id == project_id)
                          .order_by(ProjectRevision.revision.desc()).limit(1)).scalar_one_or_none()
            if r is None:
                raise NotFound("project not found", projectId=project_id)
            return r.revision, r.snapshot.get("_nav")

    def undo(self, actor: Actor, project_id: str, base_revision: int) -> dict[str, Any]:
        cur, nav = self._latest_nav(project_id)
        target, head = (nav["target"] - 1, nav["head"]) if nav else (cur - 1, cur)
        if target < 1:
            raise ValidationFailed("nothing to undo")
        return self.restore_revision(actor, project_id, base_revision, target, {"target": target, "head": head}, "project.undo")

    def redo(self, actor: Actor, project_id: str, base_revision: int) -> dict[str, Any]:
        _cur, nav = self._latest_nav(project_id)
        if not nav or nav["target"] >= nav["head"]:
            raise ValidationFailed("nothing to redo")
        target = nav["target"] + 1
        return self.restore_revision(actor, project_id, base_revision, target, {"target": target, "head": nav["head"]}, "project.redo")

    # ------------------------------------------------------------------ audit
    def audit_events(self, limit: int = 100, project_id: str | None = None, after_id: int = 0) -> list[dict[str, Any]]:
        with self.session() as s:
            q = select(AuditEvent).where(AuditEvent.id > after_id).order_by(AuditEvent.id.desc()).limit(min(limit, 1000))
            if project_id:
                q = q.where(AuditEvent.project_id == project_id)
            return [{"seq": e.id, "eventId": e.event_id, "ts": iso(e.ts), "actor": e.actor, "action": e.action,
                     "outcome": e.outcome, "projectId": e.project_id, "operationId": e.operation_id, "data": e.data}
                    for e in s.execute(q).scalars()]

    def acknowledge_restore(self, actor: Actor, reason: str = "database restored from backup") -> dict[str, Any]:
        """Owner-only: after a deliberate backup restore the old anchor points past the restored tail.
        Records the previous anchor in a new audit event and re-anchors on it."""
        if not actor.is_owner:
            raise PolicyDenied("only the owner can acknowledge a restore")
        anchor_path = self.settings.data_dir / "audit" / "anchor.json"
        previous = json.loads(anchor_path.read_text(encoding="utf-8")) if anchor_path.exists() else None
        flag = anchor_path.with_name("tamper.json")
        tamper = json.loads(flag.read_text(encoding="utf-8")) if flag.exists() else None
        if tamper is not None:  # keep the evidence next to the log before clearing the flag
            flag.replace(flag.with_name(f"tamper-acknowledged-{int(__import__('time').time())}.json"))
        anchor_path.unlink(missing_ok=True)  # re-anchor on the acknowledged state
        anchor_path.with_name("anchor.reset").write_text("owner acknowledged", encoding="utf-8")
        with self.audit_session() as s:
            audit.record(s, actor, "audit.restore_acknowledged", "ok",
                         data={"previousAnchor": previous, "tamper": tamper, "reason": reason[:200]})
        return {"acknowledged": True, "previousAnchor": previous}

    def verify_audit_chain(self) -> dict[str, Any]:
        with self.session() as s:
            ok, n = audit.verify_chain(s)
            anchor = audit.check_anchor(s, self.settings.data_dir / "audit" / "anchor.json")
            return {"valid": ok and anchor["ok"], "eventsChecked": n, "chainValid": ok, "anchor": anchor}

    def site_dir(self, project_id: str) -> Path:
        return self.workspace(project_id).resolve("site")


def _diff(before: dict[str, Any], after: dict[str, Any]) -> dict[str, Any]:
    b = {c["id"]: c for c in before.get("components", [])}
    a = {c["id"]: c for c in after.get("components", [])}
    return {
        "added": sorted(set(a) - set(b)),
        "removed": sorted(set(b) - set(a)),
        "changed": {k: {"before": b[k], "after": a[k]} for k in sorted(set(a) & set(b)) if a[k] != b[k]},
        "projectChanged": before.get("project") != after.get("project"),
    }


def _jsonable(v: Any) -> Any:
    try:
        json.dumps(v, default=str)
        return json.loads(json.dumps(v, default=str))
    except (TypeError, ValueError):
        return {"value": str(v)[:500]}
