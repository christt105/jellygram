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


def upload(client, message_id, filename, mime_type="application/zip", document_id=None):
    payload = {
        "message_id": message_id,
        "filename": filename,
        "filesize": 1048576,
        "mime_type": mime_type
    }
    if document_id is not None:
        payload["document_id"] = document_id

    response = client.post("/upload", json=payload)
    assert response.status_code == 200, response.text
    return response.json()


def test_first_upload_of_a_document_id_is_not_a_duplicate(client):
    result = upload(client, 8001, "Postres.zip.001", document_id=111)
    assert result["duplicate"] is False


def test_reforwarding_the_same_document_id_is_flagged_as_duplicate(client):
    first = upload(client, 8002, "Postres.zip.001", document_id=222)

    # Re-forwarded message: new message_id, same underlying blob.
    result = upload(client, 8099, "Postres.zip.001", document_id=222)
    assert result["duplicate"] is True
    assert result["collection_id"] == first["collection_id"]


def test_a_duplicate_forward_does_not_add_a_second_file_to_the_collection(client):
    first = upload(client, 8003, "Postres.zip.001", document_id=333)
    upload(client, 8004, "Postres.zip.002", document_id=334)
    upload(client, 8005, "Postres.zip.003", document_id=335)

    # Someone re-forwards the same 3 parts a second time.
    upload(client, 8103, "Postres.zip.001", document_id=333)
    upload(client, 8104, "Postres.zip.002", document_id=334)
    upload(client, 8105, "Postres.zip.003", document_id=335)

    collection = client.get(f"/collections/{first['collection_id']}").json()
    assert len(collection["files"]) == 3


def test_uploads_without_a_document_id_are_never_treated_as_duplicates(client):
    upload(client, 8006, "Postres.zip.001")
    result = upload(client, 8007, "Postres.zip.001")
    assert result["duplicate"] is False
