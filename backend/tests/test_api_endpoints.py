import pytest
from fastapi.testclient import TestClient
from sqlmodel import SQLModel, create_engine, Session
from database import get_session
import main
from main import app
from models import Movie, Series, Season, Episode, Collection, File

from sqlalchemy.pool import StaticPool

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


def test_root_endpoint(client):
    response = client.get("/")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_upload_endpoint_without_created_at(client):
    payload = {
        "message_id": 12345,
        "filename": "Test.Movie.2024.1080p.mkv",
        "filesize": 1048576,
        "mime_type": "video/mp4",
        "created_at": None
    }
    response = client.post("/upload", json=payload)
    assert response.status_code == 200, response.text
    data = response.json()
    assert data["message_id"] == 12345
    assert data["filename"] == "Test.Movie.2024.1080p.mkv"


def test_delete_collection_prunes_orphans(client):
    upload_res = client.post("/upload", json={
        "message_id": 999,
        "filename": "Orphan.Test.Movie.2024.mkv",
        "filesize": 1000,
        "mime_type": "video/mp4"
    })
    assert upload_res.status_code == 200
    data = upload_res.json()
    collection_id = data["collection_id"]

    # Delete collection
    del_res = client.delete(f"/collections/{collection_id}")
    assert del_res.status_code == 200
    res_json = del_res.json()

    # Should prune orphan movies - checking response format and orphan list
    assert res_json.get("status") == "ok"

    # Verify movie list is empty (orphaned movie was pruned)
    movies_res = client.get("/movies")
    assert movies_res.status_code == 200
    movies = movies_res.json()
    assert len(movies) == 0


def test_movies_and_series_crud(client):
    m_res = client.get("/movies")
    assert m_res.status_code == 200
    assert m_res.json() == []

    s_res = client.get("/series")
    assert s_res.status_code == 200
    assert s_res.json() == []


def test_task_endpoints(client):
    dl_res = client.get("/tasks/downloads")
    assert dl_res.status_code == 200
    assert dl_res.json() == []

    ul_res = client.get("/tasks/uploads")
    assert ul_res.status_code == 200
    assert ul_res.json() == []

    clear_res = client.delete("/tasks/completed")
    assert clear_res.status_code == 200
    assert clear_res.json()["cleared_downloads"] == 0


CLIENT_API_CONTRACT = [
    ("GET", "/tmdb/search"),
    ("POST", "/uploads/enqueue"),
    ("GET", "/uploads/queue"),
    ("GET", "/uploads/pending"),
    ("DELETE", "/uploads/{task_id}"),
    ("POST", "/uploads/{task_id}/status"),
    ("POST", "/uploads/{task_id}/retry"),
    ("GET", "/downloads/queue"),
    ("GET", "/downloads/pending"),
    ("DELETE", "/downloads/{task_id}"),
    ("POST", "/downloads/{task_id}/status"),
    ("POST", "/downloads/{task_id}/retry"),
    ("POST", "/downloads/enqueue/collection/{collection_id}"),
    ("POST", "/downloads/enqueue/series/{series_id}"),
    ("POST", "/downloads/enqueue/series/{series_id}/season/{season_number}"),
    ("POST", "/downloads/enqueue/series/{series_id}/season/{season_number}/episode/{episode_number}"),
    ("POST", "/watch/files"),
    ("POST", "/watch/files/rename"),
    ("POST", "/watch/files/missing"),
    ("GET", "/watch"),
    ("GET", "/watch/pending-notify"),
    ("POST", "/watch/{watched_file_id}/confirm"),
    ("POST", "/watch/{watched_file_id}/correct"),
    ("PATCH", "/watch/{watched_file_id}"),
]


def test_client_api_contract_is_registered():
    """Guard against silently removing or renaming routes the web and bot depend on"""
    registered = {
        (method, route.path)
        for route in app.routes
        for method in getattr(route, "methods", set()) or set()
    }
    missing = [
        (method, path)
        for method, path in CLIENT_API_CONTRACT
        if (method, path) not in registered
    ]
    assert not missing, f"Missing client-facing routes: {missing}"

