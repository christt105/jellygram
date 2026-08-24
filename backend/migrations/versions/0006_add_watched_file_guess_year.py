"""add watchedfile.guess_year

Revision ID: 0006_watched_file_guess_year
Revises: 0005_watched_file
Create Date: 2026-08-23

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = '0006_watched_file_guess_year'
down_revision: Union[str, Sequence[str], None] = '0005_watched_file'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    with op.batch_alter_table('watchedfile', schema=None) as batch_op:
        batch_op.add_column(sa.Column('guess_year', sa.Integer(), nullable=True))


def downgrade() -> None:
    with op.batch_alter_table('watchedfile', schema=None) as batch_op:
        batch_op.drop_column('guess_year')
