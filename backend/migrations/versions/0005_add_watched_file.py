"""add watched_file table

Revision ID: 0005_watched_file
Revises: 0004_user_message_id
Create Date: 2026-08-22

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
import sqlmodel


revision: str = '0005_watched_file'
down_revision: Union[str, Sequence[str], None] = '0004_user_message_id'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('watchedfile',
    sa.Column('id', sa.Integer(), nullable=False),
    sa.Column('path', sqlmodel.sql.sqltypes.AutoString(), nullable=False),
    sa.Column('filename', sqlmodel.sql.sqltypes.AutoString(), nullable=False),
    sa.Column('filesize', sa.Integer(), nullable=False),
    sa.Column('first_seen_at', sa.DateTime(), nullable=False),
    sa.Column('guess_media_type', sqlmodel.sql.sqltypes.AutoString(), nullable=True),
    sa.Column('guess_tmdb_id', sa.Integer(), nullable=True),
    sa.Column('guess_title', sqlmodel.sql.sqltypes.AutoString(), nullable=True),
    sa.Column('guess_season', sa.Integer(), nullable=True),
    sa.Column('guess_episode', sa.Integer(), nullable=True),
    sa.Column('confidence', sa.Float(), nullable=False),
    sa.Column('guess_source', sqlmodel.sql.sqltypes.AutoString(), nullable=True),
    sa.Column('status', sqlmodel.sql.sqltypes.AutoString(), nullable=False),
    sa.Column('notified_at', sa.DateTime(), nullable=True),
    sa.Column('moved_path', sqlmodel.sql.sqltypes.AutoString(), nullable=True),
    sa.Column('error_message', sqlmodel.sql.sqltypes.AutoString(), nullable=True),
    sa.PrimaryKeyConstraint('id')
    )
    with op.batch_alter_table('watchedfile', schema=None) as batch_op:
        batch_op.create_index(batch_op.f('ix_watchedfile_path'), ['path'], unique=True)


def downgrade() -> None:
    with op.batch_alter_table('watchedfile', schema=None) as batch_op:
        batch_op.drop_index(batch_op.f('ix_watchedfile_path'))
    op.drop_table('watchedfile')
