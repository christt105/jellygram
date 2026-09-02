"""add watchedfile.guess_tvdb_id

Revision ID: 0008_watched_file_guess_tvdb_id
Revises: 0007_season_local_metadata
Create Date: 2026-09-01

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = '0008_watched_file_guess_tvdb_id'
down_revision: Union[str, Sequence[str], None] = '0007_season_local_metadata'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    with op.batch_alter_table('watchedfile', schema=None) as batch_op:
        batch_op.add_column(sa.Column('guess_tvdb_id', sa.Integer(), nullable=True))


def downgrade() -> None:
    with op.batch_alter_table('watchedfile', schema=None) as batch_op:
        batch_op.drop_column('guess_tvdb_id')
