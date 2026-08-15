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
        yield client
    app.dependency_overrides.clear()


def upload(client, message_id, filename, user_message_id=None):
    payload = {
        "message_id": message_id,
        "filename": filename,
        "filesize": 1048576,
        "mime_type": "video/x-matroska"
    }
    if user_message_id is not None:
        payload["user_message_id"] = user_message_id

    response = client.post("/upload", json=payload)
    assert response.status_code == 200, response.text
    return response.json()["collection_id"]


def pending_file(client, collection_id):
    client.post(f"/downloads/enqueue/collection/{collection_id}")
    pending = client.get("/downloads/pending").json()
    return pending[0]["files"][0]


def test_upload_stores_both_ids(client):
    collection_id = upload(client, 6001, "Mugen.Train.2020.1080p.mkv", user_message_id=91001)

    file = pending_file(client, collection_id)
    assert file["message_id"] == 6001
    assert file["user_message_id"] == 91001


def test_upload_without_the_user_id_leaves_it_empty(client):
    collection_id = upload(client, 6002, "Mugen.Train.2020.720p.mkv")

    file = pending_file(client, collection_id)
    assert file["message_id"] == 6002
    assert file["user_message_id"] is None


def test_reuploading_the_same_message_fills_in_a_missing_user_id(client):
    collection_id = upload(client, 6003, "Mugen.Train.2020.mkv")
    upload(client, 6003, "Mugen.Train.2020.mkv", user_message_id=91003)

    file = pending_file(client, collection_id)
    assert file["message_id"] == 6003
    assert file["user_message_id"] == 91003


def test_the_bot_id_is_never_overwritten_by_a_second_upload(client):
    collection_id = upload(client, 6004, "Mugen.Train.2020.mkv", user_message_id=91004)
    upload(client, 6004, "Mugen.Train.2020.mkv", user_message_id=99999)

    file = pending_file(client, collection_id)
    assert file["message_id"] == 6004
    assert file["user_message_id"] == 91004
