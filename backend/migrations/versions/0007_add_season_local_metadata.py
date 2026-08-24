"""add local_metadata to season

Revision ID: 0007_season_local_metadata
Revises: 0006_watched_file_guess_year
Create Date: 2026-08-24

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
import sqlmodel


revision: str = '0007_season_local_metadata'
down_revision: Union[str, Sequence[str], None] = '0006_watched_file_guess_year'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    with op.batch_alter_table('season', schema=None) as batch_op:
        batch_op.add_column(sa.Column('local_metadata', sa.Boolean(), nullable=False, server_default=sa.false()))


def downgrade() -> None:
    with op.batch_alter_table('season', schema=None) as batch_op:
        batch_op.drop_column('local_metadata')
