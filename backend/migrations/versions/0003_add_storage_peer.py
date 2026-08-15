"""add storage_peer to file

Revision ID: 0003_storage_peer
Revises: 0002_versions
Create Date: 2026-07-31

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
import sqlmodel


revision: str = '0003_storage_peer'
down_revision: Union[str, Sequence[str], None] = '0002_versions'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    with op.batch_alter_table('file', schema=None) as batch_op:
        batch_op.add_column(
            sa.Column('storage_peer', sqlmodel.sql.sqltypes.AutoString(), nullable=False, server_default='bot')
        )


def downgrade() -> None:
    with op.batch_alter_table('file', schema=None) as batch_op:
        batch_op.drop_column('storage_peer')
