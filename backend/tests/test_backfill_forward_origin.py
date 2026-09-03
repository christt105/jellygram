from datetime import datetime, timezone

import pytest
from sqlalchemy.pool import StaticPool
from sqlmodel import Session, SQLModel, create_engine, select

import models
from models import Collection, File
from scripts import backfill_forward_origin as backfill


class FakeResponse:
    def __init__(self, payload):
        self._payload = payload

    def raise_for_status(self):
        pass

    def json(self):
        return self._payload


@pytest.fixture(name="db_engine")
def db_engine_fixture(monkeypatch):
    engine = create_engine(
        "sqlite:///:memory:",
        connect_args={"check_same_thread": False},
        poolclass=StaticPool
    )
    SQLModel.metadata.create_all(engine)
    monkeypatch.setattr(backfill, "engine", engine)
    return engine


def seed_file(engine, message_id, document_id=None, storage_peer="bot"):
    with Session(engine) as session:
        collection = session.exec(select(Collection)).first()
        if collection is None:
            collection = Collection(name="Test", quality="1080p")
            session.add(collection)
            session.commit()
            session.refresh(collection)
        session.add(File(
            message_id=message_id,
            filename=f"{message_id}.mkv",
            filesize=1024,
            created_at=datetime.now(timezone.utc),
            collection_id=collection.id,
            storage_peer=storage_peer,
            document_id=document_id
        ))
        session.commit()


def test_pending_files_skips_already_backfilled_and_non_bot_rows(db_engine):
    seed_file(db_engine, 1)
    seed_file(db_engine, 2, document_id=999)
    seed_file(db_engine, 3, storage_peer="saved")

    with Session(db_engine) as session:
        pending = backfill.pending_files(session)

    assert [f.message_id for f in pending] == [1]


def test_run_updates_matching_rows(db_engine, monkeypatch):
    seed_file(db_engine, 10)
    seed_file(db_engine, 11)

    def fake_post(url, json, timeout):
        assert json == [10, 11]
        return FakeResponse({
            "10": {
                "document_id": 5794420050676948545,
                "fwd_from_type": "channel",
                "fwd_from_id": "-100999",
                "fwd_from_name": "Doraemon [Castellano]",
                "fwd_from_hidden": False
            }
            # 11 is intentionally absent: message no longer reachable
        })

    monkeypatch.setattr(backfill.httpx, "post", fake_post)

    stats = backfill.run("http://bot-net:8080")

    assert stats == {"updated": 1, "not_found": 1, "batches": 1}

    with Session(db_engine) as session:
        file10 = session.exec(select(File).where(File.message_id == 10)).one()
        file11 = session.exec(select(File).where(File.message_id == 11)).one()

    assert file10.document_id == 5794420050676948545
    assert file10.fwd_from_type == "channel"
    assert file10.fwd_from_name == "Doraemon [Castellano]"
    assert file11.document_id is None


def test_dry_run_does_not_write_to_the_database(db_engine, monkeypatch):
    seed_file(db_engine, 20)

    monkeypatch.setattr(
        backfill.httpx, "post",
        lambda url, json, timeout: FakeResponse({"20": {"document_id": 1, "fwd_from_type": None,
                                                          "fwd_from_id": None, "fwd_from_name": None,
                                                          "fwd_from_hidden": False}})
    )

    stats = backfill.run("http://bot-net:8080", dry_run=True)

    assert stats["updated"] == 1
    with Session(db_engine) as session:
        file20 = session.exec(select(File).where(File.message_id == 20)).one()
    assert file20.document_id is None


def test_batches_splits_evenly_and_leaves_a_remainder():
    result = list(backfill.batches(list(range(5)), 2))
    assert result == [[0, 1], [2, 3], [4]]
