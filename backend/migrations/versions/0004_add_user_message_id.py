"""add user_message_id to file

Revision ID: 0004_user_message_id
Revises: 0003_storage_peer
Create Date: 2026-08-15

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
import sqlmodel


revision: str = '0004_user_message_id'
down_revision: Union[str, Sequence[str], None] = '0003_storage_peer'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    with op.batch_alter_table('file', schema=None) as batch_op:
        batch_op.add_column(sa.Column('user_message_id', sa.Integer(), nullable=True))


def downgrade() -> None:
    with op.batch_alter_table('file', schema=None) as batch_op:
        batch_op.drop_column('user_message_id')
