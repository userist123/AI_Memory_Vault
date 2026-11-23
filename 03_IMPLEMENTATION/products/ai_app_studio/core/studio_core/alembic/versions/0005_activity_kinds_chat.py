"""AAS owner chat (2026-11-23): activity kinds chat.prompt and chat.fallback (aas_runner/chat.py)

Revision ID: 0005
Revises: 0004
"""
from __future__ import annotations

from alembic import op

revision = "0005"
down_revision = "0004"
branch_labels = None
depends_on = None

OLD = ("run.started", "run.finished", "plan.proposed", "plan.decided", "step.started", "tool.call", "tool.result",
       "file.diff", "build.started", "build.log", "build.error", "build.finished", "preview.revision",
       "preview.interaction", "device.state", "device.input", "device.log", "qa.result", "approval.requested",
       "approval.decided", "agent.text", "agent.typing", "usage", "stream.dropped", "cost.update", "preview.captured")
NEW = (*OLD, "chat.prompt", "chat.fallback")


def _swap(kinds: tuple[str, ...]) -> None:
    listing = ", ".join(f"'{k}'" for k in kinds)
    with op.batch_alter_table("activity_events", recreate="always") as b:
        b.drop_constraint("ck_activity_kind", type_="check")
        b.create_check_constraint("ck_activity_kind", f"kind IN ({listing})")


def upgrade() -> None:
    _swap(NEW)


def downgrade() -> None:
    op.execute("DELETE FROM activity_events WHERE kind IN ('chat.prompt', 'chat.fallback')")
    _swap(OLD)
