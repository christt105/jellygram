import pytest
from fastapi.testclient import TestClient
from sqlalchemy.pool import StaticPool
from sqlmodel import SQLModel, Session, create_engine

from database import get_session
from main import app
import routers.watch as watch_module


class StubTMDB:
    def identify_by_filename(self, filename):
        if "S01E02" in filename or "S01E03" in filename:
            return {
                "id": 42, "name": "Stub Show", "media_type": "tv", "_match_score": 0.9,
                "first_air_date": "2020-05-01",
            }
        if "[tmdbid-777]" in filename:
            return {"id": 777, "title": "Stub Movie", "media_type": "movie"}
        return {}

    def identify_by_tmdbid(self, tmdb_id, content_type):
        if tmdb_id == 42:
            return {"id": 42, "name": "Stub Show", "media_type": "tv", "first_air_date": "2020-05-01"}
        if tmdb_id == 100:
            return {
                "id": 100, "title": "Corrected Movie", "media_type": "movie", "release_date": "2019-03-01",
            }
        return {}


@pytest.fixture(name="client")
def client_fixture(monkeypatch):
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

    monkeypatch.setattr(watch_module, "tmdb", StubTMDB())
    app.dependency_overrides[get_session] = get_session_override
    with TestClient(app) as client:
        yield client
    app.dependency_overrides.clear()


def report(client, path, filename="Stub.Show.S01E02.mkv", filesize=123):
    res = client.post("/watch/files", json={"path": path, "filename": filename, "filesize": filesize})
    assert res.status_code == 200, res.text
    return res.json()


def test_report_new_file_runs_guess(client):
    data = report(client, "downloads/Stub.Show.S01E02.mkv")
    assert data["status"] == "pending"
    assert data["guess_tmdb_id"] == 42
    assert data["guess_title"] == "Stub Show"
    assert data["guess_media_type"] == "tv"
    assert data["guess_year"] == 2020
    assert data["guess_season"] == 1
    assert data["guess_episode"] == 2
    assert data["confidence"] == 0.9
    assert data["guess_source"] == "tmdb"


def test_report_existing_path_is_idempotent(client):
    first = report(client, "downloads/dupe.mkv", filename="Dupe.mkv")
    second = report(client, "downloads/dupe.mkv", filename="Dupe.mkv")
    assert first["id"] == second["id"]


def test_rename_reguesses_a_pending_row(client):
    report(client, "downloads/old.mkv", filename="Old.Name.mkv")
    res = client.post("/watch/files/rename", json={
        "old_path": "downloads/old.mkv",
        "new_path": "downloads/Stub.Show.S01E03.mkv"
    })
    assert res.status_code == 200, res.text
    data = res.json()
    assert data["path"] == "downloads/Stub.Show.S01E03.mkv"
    assert data["filename"] == "Stub.Show.S01E03.mkv"
    assert data["guess_tmdb_id"] == 42
    assert data["guess_episode"] == 3


def test_rename_does_not_clobber_a_confirmed_row(client):
    created = report(client, "downloads/confirm-me.mkv", filename="Confirm.Me.mkv")
    client.post(f"/watch/{created['id']}/confirm", json={"tmdb_id": 42})

    res = client.post("/watch/files/rename", json={
        "old_path": "downloads/confirm-me.mkv",
        "new_path": "downloads/renamed-after-confirm.mkv"
    })
    assert res.status_code == 200, res.text
    data = res.json()
    assert data["status"] == "confirmed"
    assert data["guess_tmdb_id"] == 42  # unchanged, no re-guess triggered


def test_missing_marks_row_removed_without_deleting(client):
    created = report(client, "downloads/gone.mkv", filename="Gone.mkv")
    res = client.post("/watch/files/missing", json={"path": "downloads/gone.mkv"})
    assert res.status_code == 200, res.text
    assert res.json()["status"] == "removed"

    listed = client.get("/watch", params={"status": "removed"}).json()
    assert any(f["id"] == created["id"] for f in listed)


def test_missing_is_a_noop_for_an_already_moved_row(client):
    created = report(client, "downloads/moved.mkv", filename="Moved.mkv")
    client.patch(f"/watch/{created['id']}", json={"status": "moved", "moved_path": "/library/Moved.mkv"})

    res = client.post("/watch/files/missing", json={"path": "downloads/moved.mkv"})
    assert res.status_code == 200
    assert res.json()["status"] == "moved"


def test_list_filters_by_status(client):
    report(client, "downloads/a.mkv", filename="A.mkv")
    b = report(client, "downloads/b.mkv", filename="B.mkv")
    client.post(f"/watch/{b['id']}/confirm", json={"tmdb_id": 42})

    pending = client.get("/watch", params={"status": "pending"}).json()
    confirmed = client.get("/watch", params={"status": "confirmed"}).json()
    assert all(f["status"] == "pending" for f in pending)
    assert all(f["status"] == "confirmed" for f in confirmed)
    assert any(f["id"] == b["id"] for f in confirmed)


def test_pending_notify_only_returns_pending_rows(client):
    a = report(client, "downloads/pn-a.mkv", filename="PN-A.mkv")
    b = report(client, "downloads/pn-b.mkv", filename="PN-B.mkv")
    client.post(f"/watch/{b['id']}/confirm", json={"tmdb_id": 42})

    pending_notify = client.get("/watch/pending-notify").json()
    ids = {f["id"] for f in pending_notify}
    assert a["id"] in ids
    assert b["id"] not in ids


def test_confirm_returns_final_identity_and_updates_status(client):
    created = report(client, "downloads/confirm2.mkv", filename="Confirm2.mkv")
    res = client.post(f"/watch/{created['id']}/confirm", json={"tmdb_id": 42})
    assert res.status_code == 200, res.text
    data = res.json()
    assert data["status"] == "confirmed"
    assert data["tmdb_id"] == 42
    assert data["media_type"] == "tv"
    assert data["title"] == "Stub Show"
    assert data["year"] == 2020


def test_correct_overrides_tmdb_id_and_season_episode(client):
    created = report(client, "downloads/correct-me.mkv", filename="Correct.Me.mkv")
    res = client.post(f"/watch/{created['id']}/correct", json={"tmdb_id": 100, "season": 2, "episode": 5})
    assert res.status_code == 200, res.text
    data = res.json()
    assert data["status"] == "corrected"
    assert data["tmdb_id"] == 100
    assert data["title"] == "Corrected Movie"
    assert data["year"] == 2019
    assert data["season"] == 2
    assert data["episode"] == 5


def test_confirm_with_unknown_tmdb_id_is_a_404(client):
    created = report(client, "downloads/unknown-id.mkv", filename="Unknown.Id.mkv")
    res = client.post(f"/watch/{created['id']}/confirm", json={"tmdb_id": 999999})
    assert res.status_code == 404


def test_patch_requires_moved_path_for_moved_status(client):
    created = report(client, "downloads/needs-path.mkv", filename="Needs.Path.mkv")
    res = client.patch(f"/watch/{created['id']}", json={"status": "moved"})
    assert res.status_code == 400


def test_patch_moved_records_the_destination(client):
    created = report(client, "downloads/patch-moved.mkv", filename="Patch.Moved.mkv")
    res = client.patch(f"/watch/{created['id']}", json={"status": "moved", "moved_path": "/library/Show/S01E02.mkv"})
    assert res.status_code == 200, res.text
    data = res.json()
    assert data["status"] == "moved"
    assert data["moved_path"] == "/library/Show/S01E02.mkv"


def test_patch_error_records_the_message(client):
    created = report(client, "downloads/patch-error.mkv", filename="Patch.Error.mkv")
    res = client.patch(f"/watch/{created['id']}", json={"status": "error", "error_message": "disk full"})
    assert res.status_code == 200, res.text
    data = res.json()
    assert data["status"] == "error"
    assert data["error_message"] == "disk full"


def test_patch_notified_sets_notified_at(client):
    created = report(client, "downloads/patch-notified.mkv", filename="Patch.Notified.mkv")
    assert created["notified_at"] is None
    res = client.patch(f"/watch/{created['id']}", json={"status": "notified"})
    assert res.status_code == 200, res.text
    assert res.json()["notified_at"] is not None


def test_reidentify_reguesses_only_unresolved_rows(client):
    unresolved = report(client, "downloads/unmatched.mkv", filename="Unmatched.File.mkv")
    assert unresolved["guess_tmdb_id"] is None

    confirmed = report(client, "downloads/already-confirmed.mkv", filename="Confirm.Me.mkv")
    client.post(f"/watch/{confirmed['id']}/confirm", json={"tmdb_id": 42})

    watch_module.tmdb.identify_by_filename = lambda filename: (
        {"id": 42, "name": "Stub Show", "media_type": "tv", "_match_score": 0.9}
        if "Unmatched" in filename else {}
    )

    res = client.post("/watch/reidentify")
    assert res.status_code == 200, res.text
    data = res.json()

    updated = next(f for f in data if f["id"] == unresolved["id"])
    assert updated["guess_tmdb_id"] == 42
    assert all(f["id"] != confirmed["id"] for f in data)

    still_confirmed = client.get("/watch", params={"status": "confirmed"}).json()
    assert any(f["id"] == confirmed["id"] for f in still_confirmed)


def test_patch_rejects_confirmed_status(client):
    created = report(client, "downloads/patch-bad.mkv", filename="Patch.Bad.mkv")
    res = client.patch(f"/watch/{created['id']}", json={"status": "confirmed"})
    assert res.status_code == 400
