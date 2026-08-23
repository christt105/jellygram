import os
from datetime import datetime, timezone
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel
from sqlmodel import Session, select

from database import get_session
from models import WatchedFile
from tmdb import TMDB
from crud import guess_watched_file

router = APIRouter(prefix="/watch", tags=["watch"])
tmdb = TMDB()

RESOLVABLE_STATUSES = ("pending", "notified")
PATCHABLE_STATUSES = ("notified", "moved", "error")


class WatchedFileIn(BaseModel):
    path: str
    filename: str
    filesize: int


class WatchedFileRenameIn(BaseModel):
    old_path: str
    new_path: str


class WatchedFileMissingIn(BaseModel):
    path: str


class WatchedFileResolveIn(BaseModel):
    tmdb_id: int
    season: Optional[int] = None
    episode: Optional[int] = None


class WatchedFilePatchIn(BaseModel):
    status: str
    moved_path: Optional[str] = None
    error_message: Optional[str] = None


def _apply_guess(row: WatchedFile) -> None:
    guess = guess_watched_file(tmdb, row.filename)
    row.guess_media_type = guess["media_type"]
    row.guess_tmdb_id = guess["tmdb_id"]
    row.guess_title = guess["title"]
    row.guess_season = guess["season"]
    row.guess_episode = guess["episode"]
    row.confidence = guess["confidence"]
    row.guess_source = guess["source"]


@router.post("/files", response_model=WatchedFile)
def report_file(payload: WatchedFileIn, session: Session = Depends(get_session)):
    """Bot-net reports a new, size-stable file found in the downloads folder."""
    existing = session.exec(select(WatchedFile).where(WatchedFile.path == payload.path)).first()
    if existing:
        return existing

    row = WatchedFile(path=payload.path, filename=payload.filename, filesize=payload.filesize)
    _apply_guess(row)
    session.add(row)
    session.commit()
    session.refresh(row)
    return row


@router.post("/files/rename", response_model=WatchedFile)
def rename_file(payload: WatchedFileRenameIn, session: Session = Depends(get_session)):
    """Bot-net calls this on a filesystem Renamed event."""
    row = session.exec(select(WatchedFile).where(WatchedFile.path == payload.old_path)).first()
    if not row:
        raise HTTPException(status_code=404, detail="Watched file not found")

    row.path = payload.new_path
    row.filename = os.path.basename(payload.new_path)

    if row.status in RESOLVABLE_STATUSES:
        _apply_guess(row)

    session.add(row)
    session.commit()
    session.refresh(row)
    return row


@router.post("/files/missing", response_model=WatchedFile)
def mark_missing(payload: WatchedFileMissingIn, session: Session = Depends(get_session)):
    """Bot-net calls this on a Deleted event, or a startup reconciliation sweep.

    Marks the row removed without deleting it, so later phases can still show
    what happened. Idempotent no-op for rows already removed or moved (the
    latter is an expected side effect of the app's own move, not a real loss).
    """
    row = session.exec(select(WatchedFile).where(WatchedFile.path == payload.path)).first()
    if not row:
        raise HTTPException(status_code=404, detail="Watched file not found")

    if row.status in ("removed", "moved"):
        return row

    row.status = "removed"
    session.add(row)
    session.commit()
    session.refresh(row)
    return row


@router.post("/reidentify", response_model=List[WatchedFile])
def reidentify_all(session: Session = Depends(get_session)):
    """Re-run the TMDB guess for every row still unresolved (pending/notified) — e.g. after
    fixing the filename parser, or if TMDB was unreachable when a file was first detected.
    Rows already confirmed/corrected/moved are left alone, since re-guessing them would be
    reopening something that (for moved rows) is already sitting on disk."""
    rows = session.exec(select(WatchedFile).where(WatchedFile.status.in_(RESOLVABLE_STATUSES))).all()

    for row in rows:
        _apply_guess(row)
        session.add(row)
    session.commit()

    for row in rows:
        session.refresh(row)
    return rows


@router.get("", response_model=List[WatchedFile])
def list_watched_files(status: Optional[str] = None, session: Session = Depends(get_session)):
    """For the web: all watched files, optionally filtered by status."""
    query = select(WatchedFile)
    if status:
        query = query.where(WatchedFile.status == status)
    return session.exec(query.order_by(WatchedFile.first_seen_at.desc())).all()


@router.get("/pending-notify", response_model=List[WatchedFile])
def list_pending_notify(session: Session = Depends(get_session)):
    """For bot-net: rows that still need a Telegram message sent."""
    return session.exec(select(WatchedFile).where(WatchedFile.status == "pending")).all()


def _resolve(row: WatchedFile, payload: WatchedFileResolveIn) -> dict:
    try:
        tmdb_result = tmdb.identify_by_tmdbid(payload.tmdb_id, row.guess_media_type)
    except Exception as e:
        raise HTTPException(status_code=502, detail=f"TMDB lookup failed: {e}")

    if not tmdb_result:
        raise HTTPException(status_code=404, detail="TMDB id not found")

    media_type = tmdb_result.get("media_type")
    title = tmdb_result.get("title") if media_type == "movie" else tmdb_result.get("name")
    title = title or tmdb_result.get("title") or tmdb_result.get("name")

    row.guess_tmdb_id = payload.tmdb_id
    row.guess_media_type = media_type
    row.guess_title = title
    if payload.season is not None:
        row.guess_season = payload.season
    if payload.episode is not None:
        row.guess_episode = payload.episode
    row.confidence = 1.0
    row.guess_source = "filename"

    return {
        "id": row.id,
        "path": row.path,
        "filename": row.filename,
        "tmdb_id": row.guess_tmdb_id,
        "media_type": row.guess_media_type,
        "title": row.guess_title,
        "season": row.guess_season,
        "episode": row.guess_episode,
        "status": row.status,
    }


@router.post("/{watched_file_id}/confirm")
def confirm_file(watched_file_id: int, payload: WatchedFileResolveIn, session: Session = Depends(get_session)):
    """Confirm the guess (or the given tmdb_id) as correct. Returns the final
    identity so bot-net can build the destination path and perform the move.
    Performs no file I/O: only bot-net has access to the mounted disk."""
    row = session.get(WatchedFile, watched_file_id)
    if not row:
        raise HTTPException(status_code=404, detail="Watched file not found")

    result = _resolve(row, payload)
    row.status = "confirmed"
    result["status"] = row.status

    session.add(row)
    session.commit()
    return result


@router.post("/{watched_file_id}/correct")
def correct_file(watched_file_id: int, payload: WatchedFileResolveIn, session: Session = Depends(get_session)):
    """Same as /confirm, but for a manually corrected identity (wrong guess).
    Performs no file I/O: only bot-net has access to the mounted disk."""
    row = session.get(WatchedFile, watched_file_id)
    if not row:
        raise HTTPException(status_code=404, detail="Watched file not found")

    result = _resolve(row, payload)
    row.status = "corrected"
    result["status"] = row.status

    session.add(row)
    session.commit()
    return result


@router.patch("/{watched_file_id}", response_model=WatchedFile)
def update_status(watched_file_id: int, payload: WatchedFilePatchIn, session: Session = Depends(get_session)):
    """Status callbacks from bot-net: status=notified once a Telegram message
    was sent, or status=moved/error once the actual move was attempted."""
    row = session.get(WatchedFile, watched_file_id)
    if not row:
        raise HTTPException(status_code=404, detail="Watched file not found")

    if payload.status not in PATCHABLE_STATUSES:
        raise HTTPException(status_code=400, detail=f"status must be one of {PATCHABLE_STATUSES}")

    if payload.status == "moved":
        if not payload.moved_path:
            raise HTTPException(status_code=400, detail="moved_path is required for status=moved")
        row.moved_path = payload.moved_path
    elif payload.status == "error":
        if not payload.error_message:
            raise HTTPException(status_code=400, detail="error_message is required for status=error")
        row.error_message = payload.error_message
    elif payload.status == "notified":
        row.notified_at = datetime.now(timezone.utc)

    row.status = payload.status
    session.add(row)
    session.commit()
    session.refresh(row)
    return row
