import pytest
from fastapi.testclient import TestClient
from sqlalchemy.pool import StaticPool
from sqlmodel import SQLModel, Session, create_engine

from database import get_session
from main import app


@pytest.fixture(name="client")
def client_fixture():
    import models
    _ = models
    engine = create_engine(
        "sqlite:///:memory:",
        connect_args={"check_same_thread": False},
        poolclass=StaticPool
    )
    SQLModel.metadata.create_all(engine)

    def get_session_override():
        with Session(engine) as session:
            yield session

    app.dependency_overrides[get_session] = get_session_override
    with TestClient(app) as client:
        client.engine = engine
        yield client
    app.dependency_overrides.clear()


def upload(client, message_id, filename, **forward_fields):
    payload = {
        "message_id": message_id,
        "filename": filename,
        "filesize": 1048576,
        "mime_type": "video/x-matroska",
        **forward_fields
    }
    response = client.post("/upload", json=payload)
    assert response.status_code == 200, response.text
    return response.json()["collection_id"]


def raw_file_row(client, message_id):
    with client.engine.connect() as connection:
        return connection.exec_driver_sql(
            "select document_id, fwd_from_type, fwd_from_id, fwd_from_name, fwd_from_hidden"
            " from file where message_id = ?",
            (message_id,)
        ).one()


def test_upload_stores_document_id_and_forward_origin(client):
    upload(
        client, 7001, "Frankenstein.1080p.mkv",
        document_id=5794420050676948545,
        fwd_from_type="chat",
        fwd_from_id="-1001234567890",
        fwd_from_name="Las Cositas 3: La venganza",
        fwd_from_hidden=False
    )

    document_id, fwd_type, fwd_id, fwd_name, fwd_hidden = raw_file_row(client, 7001)
    assert document_id == 5794420050676948545
    assert fwd_type == "chat"
    assert fwd_id == "-1001234567890"
    assert fwd_name == "Las Cositas 3: La venganza"
    assert fwd_hidden == 0


def test_upload_without_forward_fields_leaves_them_empty(client):
    upload(client, 7002, "Frankenstein.4K.mkv")

    document_id, fwd_type, fwd_id, fwd_name, fwd_hidden = raw_file_row(client, 7002)
    assert document_id is None
    assert fwd_type is None
    assert fwd_hidden == 0


def test_hidden_forward_has_no_id_but_keeps_the_name(client):
    upload(
        client, 7003, "Frankenstein.4K.HDR.mkv",
        document_id=5798571127978597543,
        fwd_from_type="hidden_user",
        fwd_from_name="Someone",
        fwd_from_hidden=True
    )

    document_id, fwd_type, fwd_id, fwd_name, fwd_hidden = raw_file_row(client, 7003)
    assert document_id == 5798571127978597543
    assert fwd_type == "hidden_user"
    assert fwd_id is None
    assert fwd_name == "Someone"
    assert fwd_hidden == 1


def test_reuploading_the_same_message_fills_in_missing_forward_origin(client):
    upload(client, 7004, "Movie.mkv")
    upload(
        client, 7004, "Movie.mkv",
        document_id=111,
        fwd_from_type="channel",
        fwd_from_id="-100999",
        fwd_from_name="Doraemon [Castellano]"
    )

    document_id, fwd_type, fwd_id, fwd_name, fwd_hidden = raw_file_row(client, 7004)
    assert document_id == 111
    assert fwd_type == "channel"


def test_the_first_forward_origin_is_never_overwritten_by_a_second_upload(client):
    upload(
        client, 7005, "Movie.mkv",
        document_id=222,
        fwd_from_type="channel",
        fwd_from_id="-100999",
        fwd_from_name="Doraemon [Castellano]"
    )
    upload(
        client, 7005, "Movie.mkv",
        document_id=333,
        fwd_from_type="chat",
        fwd_from_id="-100111",
        fwd_from_name="Las Cositas 3"
    )

    document_id, fwd_type, fwd_id, fwd_name, fwd_hidden = raw_file_row(client, 7005)
    assert document_id == 222
    assert fwd_type == "channel"
