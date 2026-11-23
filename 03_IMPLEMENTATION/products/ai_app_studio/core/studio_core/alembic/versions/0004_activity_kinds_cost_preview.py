"""AAS S6: activity kinds cost.update and preview.captured (the names apps/desktop reads)

Revision ID: 0004
Revises: 0003
"""
from __future__ import annotations

from alembic import op

revision = "0004"
down_revision = "0003"
branch_labels = None
depends_on = None

OLD = ("run.started", "run.finished", "plan.proposed", "plan.decided", "step.started", "tool.call", "tool.result",
       "file.diff", "build.started", "build.log", "build.error", "build.finished", "preview.revision",
       "preview.interaction", "device.state", "device.input", "device.log", "qa.result", "approval.requested",
       "approval.decided", "agent.text", "agent.typing", "usage", "stream.dropped")
NEW = (*OLD, "cost.update", "preview.captured")


def _swap(kinds: tuple[str, ...]) -> None:
    listing = ", ".join(f"'{k}'" for k in kinds)
    with op.batch_alter_table("activity_events", recreate="always") as b:
        b.drop_constraint("ck_activity_kind", type_="check")
        b.create_check_constraint("ck_activity_kind", f"kind IN ({listing})")


def upgrade() -> None:
    _swap(NEW)


def downgrade() -> None:
    op.execute("DELETE FROM activity_events WHERE kind IN ('cost.update', 'preview.captured')")
    _swap(OLD)
