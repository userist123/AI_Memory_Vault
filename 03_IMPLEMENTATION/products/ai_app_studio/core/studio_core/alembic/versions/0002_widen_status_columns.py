"""widen status/verification/channel columns to 32 (PostgreSQL enforces lengths; 'approval_required' is 17)

Revision ID: 0002
Revises: 0001
"""
from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0002"
down_revision = "0001"
branch_labels = None
depends_on = None

COLUMNS = {
    "projects": ["status"], "tasks": ["status"], "agent_runs": ["status"],
    "tool_executions": ["channel", "status", "verification"], "approvals": ["status"],
    "audit_events": ["channel"], "test_runs": ["status"], "deployments": ["status"],
}


def upgrade() -> None:
    for table, cols in COLUMNS.items():
        with op.batch_alter_table(table) as b:
            for c in cols:
                b.alter_column(c, type_=sa.String(32), existing_type=sa.String(16))
    with op.batch_alter_table("deployments") as b:  # full filesystem paths, not 40 chars
        b.alter_column("target", type_=sa.Text(), existing_type=sa.String(40))


def downgrade() -> None:
    with op.batch_alter_table("deployments") as b:
        b.alter_column("target", type_=sa.String(40), existing_type=sa.Text())
    for table, cols in COLUMNS.items():
        with op.batch_alter_table(table) as b:
            for c in cols:
                b.alter_column(c, type_=sa.String(16), existing_type=sa.String(32))
