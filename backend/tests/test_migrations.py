from alembic import command
from alembic.autogenerate import compare_metadata
from alembic.config import Config
from alembic.runtime.migration import MigrationContext
from sqlalchemy import create_engine, inspect
from sqlmodel import SQLModel

import database
import models
from database import ALEMBIC_INI, BASELINE_REVISION, needs_baseline_stamp

_ = models  # Access module to register table schemas and satisfy static analysis


def upgrade_to(connection, revision):
    config = Config(ALEMBIC_INI)
    config.attributes["connection"] = connection
    command.upgrade(config, revision)


def build_legacy_database(engine):
    """
    Replicates a database created before Alembic was adopted: the baseline schema
    with data in it and no version table.
    """
    with engine.begin() as connection:
        upgrade_to(connection, BASELINE_REVISION)
    with engine.begin() as connection:
        connection.exec_driver_sql("DROP TABLE alembic_version")
        connection.exec_driver_sql(
            "INSERT INTO movie (id, tmdb_id, title, release_year, manually_added, created_at)"
            " VALUES (1, 635302, 'Mugen Train', 2020, 0, '2026-07-29 00:00:00')"
        )
        connection.exec_driver_sql(
            "INSERT INTO collection (name, quality, movie_id)"
            " VALUES ('Mugen Train BDRip', '1080p', 1)"
        )


def build_database_with_a_file(engine, revision):
    """
    Replicates a deployment sitting on an older revision with files already stored:
    the state every schema change has to reach without losing rows.
    """
    with engine.begin() as connection:
        upgrade_to(connection, revision)
    with engine.begin() as connection:
        connection.exec_driver_sql(
            "INSERT INTO collection (id, name, quality) VALUES (1, 'Mugen Train BDRip', '1080p')"
        )
        connection.exec_driver_sql(
            "INSERT INTO file (id, message_id, filename, filesize, created_at, collection_id, storage_peer)"
            " VALUES (1, 4242, 'Mugen Train.mkv', 1048576, '2026-07-29 00:00:00', 1, 'bot')"
        )


def test_fresh_database_upgrades_to_head(tmp_path):
    engine = create_engine(f"sqlite:///{tmp_path / 'fresh.db'}")
    with engine.begin() as connection:
        upgrade_to(connection, "head")

    with engine.connect() as connection:
        tables = inspect(connection).get_table_names()
    assert "alembic_version" in tables
    assert {"movie", "series", "season", "episode", "collection", "file", "watchedfile"} <= set(tables)


def test_migrated_schema_matches_the_models(tmp_path):
    engine = create_engine(f"sqlite:///{tmp_path / 'fresh.db'}")
    with engine.begin() as connection:
        upgrade_to(connection, "head")

    with engine.connect() as connection:
        context = MigrationContext.configure(connection)
        assert compare_metadata(context, SQLModel.metadata) == []


def test_fresh_database_has_an_empty_user_message_id(tmp_path):
    engine = create_engine(f"sqlite:///{tmp_path / 'fresh.db'}")
    with engine.begin() as connection:
        upgrade_to(connection, "head")

    with engine.begin() as connection:
        connection.exec_driver_sql(
            "INSERT INTO collection (id, name, quality) VALUES (1, 'Mugen Train BDRip', '1080p')"
        )
        connection.exec_driver_sql(
            "INSERT INTO file (message_id, filename, filesize, created_at, collection_id, storage_peer)"
            " VALUES (7, 'Mugen Train.mkv', 1048576, '2026-07-29 00:00:00', 1, 'bot')"
        )

    with engine.connect() as connection:
        assert connection.exec_driver_sql("select user_message_id from file").scalar() is None


def test_user_message_id_adopts_existing_files_without_losing_data(tmp_path):
    engine = create_engine(f"sqlite:///{tmp_path / 'stored.db'}")
    build_database_with_a_file(engine, "0003_storage_peer")

    with engine.begin() as connection:
        upgrade_to(connection, "head")

    with engine.connect() as connection:
        message_id, user_message_id, filename = connection.exec_driver_sql(
            "select message_id, user_message_id, filename from file"
        ).one()
        # The bot's own id is the one that must survive; the user account's starts out unknown.
        assert message_id == 4242
        assert user_message_id is None
        assert filename == "Mugen Train.mkv"


def test_watched_file_table_lands_on_a_database_with_existing_data(tmp_path):
    engine = create_engine(f"sqlite:///{tmp_path / 'stored.db'}")
    build_database_with_a_file(engine, "0004_user_message_id")

    with engine.begin() as connection:
        upgrade_to(connection, "head")

    with engine.connect() as connection:
        tables = inspect(connection).get_table_names()
        assert "watchedfile" in tables
        # Pre-existing data from before the migration must survive untouched.
        filename = connection.exec_driver_sql("select filename from file").scalar()
        assert filename == "Mugen Train.mkv"


def test_watched_file_row_can_be_inserted_and_defaults_apply(tmp_path):
    engine = create_engine(f"sqlite:///{tmp_path / 'fresh.db'}")
    with engine.begin() as connection:
        upgrade_to(connection, "head")

    with engine.begin() as connection:
        connection.exec_driver_sql(
            "INSERT INTO watchedfile (path, filename, filesize, first_seen_at, confidence, status)"
            " VALUES ('Show.Name.S01E02.mkv', 'Show.Name.S01E02.mkv', 1048576, '2026-08-22 00:00:00', 0.0, 'pending')"
        )

    with engine.connect() as connection:
        status, confidence = connection.exec_driver_sql(
            "select status, confidence from watchedfile"
        ).one()
        assert status == "pending"
        assert confidence == 0.0


def test_legacy_database_needs_a_baseline_stamp(tmp_path):
    engine = create_engine(f"sqlite:///{tmp_path / 'legacy.db'}")
    build_legacy_database(engine)

    with engine.connect() as connection:
        assert needs_baseline_stamp(connection) is True


def test_empty_database_is_not_stamped(tmp_path):
    engine = create_engine(f"sqlite:///{tmp_path / 'empty.db'}")

    with engine.connect() as connection:
        assert needs_baseline_stamp(connection) is False


def test_init_db_adopts_a_legacy_database_without_losing_data(tmp_path, monkeypatch):
    engine = create_engine(f"sqlite:///{tmp_path / 'legacy.db'}")
    build_legacy_database(engine)
    monkeypatch.setattr(database, "engine", engine)

    database.init_db()

    with engine.connect() as connection:
        assert needs_baseline_stamp(connection) is False
        assert connection.exec_driver_sql("select count(*) from movie").scalar() == 1
        assert connection.exec_driver_sql("select count(*) from collection").scalar() == 1
        context = MigrationContext.configure(connection)
        assert compare_metadata(context, SQLModel.metadata) == []


def test_init_db_is_idempotent(tmp_path, monkeypatch):
    engine = create_engine(f"sqlite:///{tmp_path / 'fresh.db'}")
    monkeypatch.setattr(database, "engine", engine)

    database.init_db()
    database.init_db()

    with engine.connect() as connection:
        assert connection.exec_driver_sql("select count(*) from alembic_version").scalar() == 1
