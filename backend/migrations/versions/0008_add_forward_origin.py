"""add document_id and fwd_from_* to file

Revision ID: 0008_forward_origin
Revises: 0007_season_local_metadata
Create Date: 2026-09-03

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = '0008_forward_origin'
down_revision: Union[str, Sequence[str], None] = '0007_season_local_metadata'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    with op.batch_alter_table('file', schema=None) as batch_op:
        batch_op.add_column(sa.Column('document_id', sa.Integer(), nullable=True))
        batch_op.add_column(sa.Column('fwd_from_type', sa.String(), nullable=True))
        batch_op.add_column(sa.Column('fwd_from_id', sa.String(), nullable=True))
        batch_op.add_column(sa.Column('fwd_from_name', sa.String(), nullable=True))
        batch_op.add_column(sa.Column('fwd_from_hidden', sa.Boolean(), nullable=False, server_default=sa.false()))
        batch_op.create_index(batch_op.f('ix_file_document_id'), ['document_id'])


def downgrade() -> None:
    with op.batch_alter_table('file', schema=None) as batch_op:
        batch_op.drop_index(batch_op.f('ix_file_document_id'))
        batch_op.drop_column('fwd_from_hidden')
        batch_op.drop_column('fwd_from_name')
        batch_op.drop_column('fwd_from_id')
        batch_op.drop_column('fwd_from_type')
        batch_op.drop_column('document_id')
