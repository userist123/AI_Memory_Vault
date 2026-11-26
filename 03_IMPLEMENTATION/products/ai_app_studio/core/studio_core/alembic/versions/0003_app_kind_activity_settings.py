"""AAS: projects.kind, activity_events (live activity stream), app_settings (owner-only flags)

Revision ID: 0003
Revises: 0002
"""
from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0003"
down_revision = "0002"
branch_labels = None
depends_on = None

KINDS = ("run.started", "run.finished", "plan.proposed", "plan.decided", "step.started", "tool.call", "tool.result",
         "file.diff", "build.started", "build.log", "build.error", "build.finished", "preview.revision",
         "preview.interaction", "device.state", "device.input", "device.log", "qa.result", "approval.requested",
         "approval.decided", "agent.text", "agent.typing", "usage", "stream.dropped")


def upgrade() -> None:
    with op.batch_alter_table("projects") as b:
        b.add_column(sa.Column("kind", sa.String(24), nullable=False, server_default="app"))
    kinds = ", ".join(f"'{k}'" for k in KINDS)
    op.create_table(
        "activity_events",
        sa.Column("seq", sa.Integer(), primary_key=True, autoincrement=True),
        sa.Column("prev_seq", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("ts", sa.DateTime(timezone=True), nullable=False),
        sa.Column("run_id", sa.String(40), nullable=True),
        sa.Column("project_id", sa.String(40), nullable=True),
        sa.Column("kind", sa.String(32), nullable=False),
        sa.Column("source", sa.String(16), nullable=False),
        sa.Column("audit_id", sa.Integer(), nullable=True),
        sa.Column("payload", sa.JSON(), nullable=False),
        sa.Column("artifact_sha256", sa.String(64), nullable=True),
        sa.Column("redactions", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("dropped", sa.Integer(), nullable=False, server_default="0"),
        sa.CheckConstraint(f"kind IN ({kinds})", name="ck_activity_kind"),
        sa.CheckConstraint("source IN ('server','runner','build','device')", name="ck_activity_source"),
        sa.CheckConstraint("length(payload) <= 8192", name="ck_activity_payload_size"),
        sqlite_autoincrement=True,
    )
    op.create_index("ix_activity_events_project_id", "activity_events", ["project_id"])
    op.create_table(
        "app_settings",
        sa.Column("key", sa.String(64), primary_key=True),
        sa.Column("value", sa.Text(), nullable=False, server_default=""),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
    )


def downgrade() -> None:
    op.drop_table("app_settings")
    op.drop_index("ix_activity_events_project_id", table_name="activity_events")
    op.drop_table("activity_events")
    with op.batch_alter_table("projects") as b:
        b.drop_column("kind")
