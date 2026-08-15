from typing import List, Optional
from datetime import datetime, timezone
from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel
from sqlmodel import Session, select
from database import get_session
from models import Series, Season, Episode, Movie, Collection, DownloadTask, UploadTask

router = APIRouter(tags=["tasks"])

def enqueue_collection_id(session: Session, collection_id: int, name_suffix: Optional[str] = None):
    coll = session.get(Collection, collection_id)
    if not coll or len(coll.files) == 0:
        return False
    existing = session.exec(
        select(DownloadTask)
        .where(DownloadTask.collection_id == collection_id)
        .where(DownloadTask.status.in_(["pending", "downloading"]))
    ).first()
    if not existing:
        task = DownloadTask(
            collection_id=collection_id,
            name_suffix=name_suffix or None,
            status="pending",
            progress=0
        )
        session.add(task)
        session.commit()
        return True
    return False

@router.get("/tasks/downloads", response_model=List[DownloadTask])
def list_download_tasks(session: Session = Depends(get_session)):
    """List all download tasks"""
    return session.exec(select(DownloadTask)).all()

@router.get("/tasks/uploads", response_model=List[UploadTask])
def list_upload_tasks(session: Session = Depends(get_session)):
    """List all upload tasks"""
    return session.exec(select(UploadTask)).all()

@router.delete("/tasks/completed")
def clear_completed_tasks(session: Session = Depends(get_session)):
    """Clear completed download and upload tasks from the queue"""
    completed_downloads = session.exec(
        select(DownloadTask).where(DownloadTask.status == "completed")
    ).all()
    completed_uploads = session.exec(
        select(UploadTask).where(UploadTask.status == "completed")
    ).all()

    for dt in completed_downloads:
        session.delete(dt)
    for ut in completed_uploads:
        session.delete(ut)

    session.commit()
    return {
        "status": "ok",
        "cleared_downloads": len(completed_downloads),
        "cleared_uploads": len(completed_uploads)
    }

class DownloadEnqueueIn(BaseModel):
    name_suffix: Optional[str] = None

@router.post("/downloads/enqueue/collection/{collection_id}")
def enqueue_collection_endpoint(
    collection_id: int,
    payload: Optional[DownloadEnqueueIn] = None,
    session: Session = Depends(get_session)
):
    success = enqueue_collection_id(session, collection_id, payload.name_suffix if payload else None)
    if not success:
        return {"status": "ignored", "message": "Collection already in queue or empty"}
    return {"status": "ok", "message": "Enqueued collection"}

@router.post("/downloads/enqueue/series/{series_id}")
def enqueue_series_endpoint(series_id: int, session: Session = Depends(get_session)):
    series = session.get(Series, series_id)
    if not series:
        raise HTTPException(status_code=404, detail="Series not found")
    count = 0
    for season in series.seasons:
        for ep in season.episodes:
            for coll in ep.collections:
                if enqueue_collection_id(session, coll.id):
                    count += 1
        for coll in season.collections:
            if enqueue_collection_id(session, coll.id):
                count += 1
    return {"status": "ok", "message": f"Enqueued {count} collections for series"}

@router.post("/downloads/enqueue/series/{series_id}/season/{season_number}")
def enqueue_season_endpoint(series_id: int, season_number: int, session: Session = Depends(get_session)):
    series = session.get(Series, series_id)
    if not series:
        raise HTTPException(status_code=404, detail="Series not found")
    season = session.exec(
        select(Season)
        .where(Season.series_id == series_id)
        .where(Season.season_number == season_number)
    ).first()
    if not season:
        raise HTTPException(status_code=404, detail="Season not found")
    count = 0
    for ep in season.episodes:
        for coll in ep.collections:
            if enqueue_collection_id(session, coll.id):
                count += 1
    for coll in season.collections:
        if enqueue_collection_id(session, coll.id):
            count += 1
    return {"status": "ok", "message": f"Enqueued {count} collections for season {season_number}"}

@router.post("/downloads/enqueue/series/{series_id}/season/{season_number}/episode/{episode_number}")
def enqueue_episode_endpoint(series_id: int, season_number: int, episode_number: int, session: Session = Depends(get_session)):
    series = session.get(Series, series_id)
    if not series:
        raise HTTPException(status_code=404, detail="Series not found")
    season = session.exec(
        select(Season)
        .where(Season.series_id == series_id)
        .where(Season.season_number == season_number)
    ).first()
    if not season:
        raise HTTPException(status_code=404, detail="Season not found")
    episode = session.exec(
        select(Episode)
        .where(Episode.season_id == season.id)
        .where(Episode.episode_number == episode_number)
    ).first()
    if not episode:
        raise HTTPException(status_code=404, detail="Episode not found")
    count = 0
    for coll in episode.collections:
        if enqueue_collection_id(session, coll.id):
            count += 1
    if count == 0:
        return {"status": "ignored", "message": "Episode collection already enqueued or not found"}
    return {"status": "ok", "message": "Enqueued episode"}

@router.get("/downloads/pending")
def list_pending_downloads(session: Session = Depends(get_session)):
    """Downloads awaiting processing, enriched with media metadata for the worker"""
    tasks = session.exec(select(DownloadTask).where(DownloadTask.status == "pending")).all()
    result = []
    for t in tasks:
        coll = session.get(Collection, t.collection_id)
        if not coll:
            continue
        media_type = "movie"
        title = "Unknown"
        year = None
        season_num = None
        episode_num = None
        tmdb_id = None
        tvdb_id = None
        if coll.movie_id:
            movie = session.get(Movie, coll.movie_id)
            if movie:
                title = movie.title
                year = movie.release_year
                tmdb_id = movie.tmdb_id
        elif coll.episode_id:
            episode = session.get(Episode, coll.episode_id)
            if episode:
                media_type = "tv"
                episode_num = episode.episode_number
                season = session.get(Season, episode.season_id)
                if season:
                    season_num = season.season_number
                    series = session.get(Series, season.series_id)
                    if series:
                        title = series.manual_title
                        year = series.release_year
                        tmdb_id = series.tmdb_id
                        tvdb_id = series.tvdb_id
        elif coll.season_id:
            season = session.get(Season, coll.season_id)
            if season:
                media_type = "tv"
                season_num = season.season_number
                series = session.get(Series, season.series_id)
                if series:
                    title = series.manual_title
                    year = series.release_year
                    tmdb_id = series.tmdb_id
                    tvdb_id = series.tvdb_id
        result.append({
            "task_id": t.id,
            "collection_id": t.collection_id,
            "media_type": media_type,
            "title": title,
            "year": year,
            "season_number": season_num,
            "episode_number": episode_num,
            "quality": coll.quality or "1080p",
            "name_suffix": t.name_suffix,
            "tmdb_id": tmdb_id,
            "tvdb_id": tvdb_id,
            "files": [
                {
                    "id": f.id,
                    "message_id": f.message_id,
                    "filename": f.filename,
                    "filesize": f.filesize,
                    "storage_peer": f.storage_peer
                }
                for f in coll.files
            ]
        })
    return result

@router.get("/downloads/queue")
def list_queue_downloads(session: Session = Depends(get_session)):
    """Active download tasks (pending, downloading, failed) for the transfers view"""
    tasks = session.exec(select(DownloadTask).where(DownloadTask.status.in_(["pending", "downloading", "failed"]))).all()
    result = []
    for t in tasks:
        coll = session.get(Collection, t.collection_id)
        if not coll:
            continue
        title = coll.name or "Unknown"
        if coll.movie_id:
            movie = session.get(Movie, coll.movie_id)
            if movie:
                title = movie.title or title
        elif coll.episode_id:
            episode = session.get(Episode, coll.episode_id)
            if episode and episode.season_id:
                season = session.get(Season, episode.season_id)
                if season and season.series_id:
                    series = session.get(Series, season.series_id)
                    if series:
                        title = f"{series.manual_title or 'Unknown Series'} - S{season.season_number}E{episode.episode_number}"
        elif coll.season_id:
            season = session.get(Season, coll.season_id)
            if season and season.series_id:
                series = session.get(Series, season.series_id)
                if series:
                    title = f"{series.manual_title or 'Unknown Series'} - S{season.season_number}"

        result.append({
            "id": t.id,
            "collection_id": t.collection_id,
            "title": title,
            "status": t.status,
            "progress": t.progress,
            "error_message": t.error_message
        })
    return result

class DownloadStatusIn(BaseModel):
    status: str
    progress: int
    error_message: Optional[str] = None
    local_path: Optional[str] = None

@router.post("/downloads/{task_id}/status")
def update_download_status(task_id: int, payload: DownloadStatusIn, session: Session = Depends(get_session)):
    """Update a download task's progress and status, reported by the worker"""
    task = session.get(DownloadTask, task_id)
    if not task:
        raise HTTPException(status_code=404, detail="Task not found")
    task.status = payload.status
    task.progress = payload.progress
    if payload.error_message:
        task.error_message = payload.error_message
    if payload.status in ["completed", "failed"]:
        task.completed_at = datetime.now(timezone.utc)
    if payload.local_path:
        collection = session.get(Collection, task.collection_id)
        if collection:
            collection.local_path = payload.local_path
            session.add(collection)
    session.add(task)
    session.commit()
    return {"status": "ok"}

@router.delete("/downloads/{task_id}")
def delete_download_task(task_id: int, session: Session = Depends(get_session)):
    task = session.get(DownloadTask, task_id)
    if not task:
        raise HTTPException(404, "Task not found")
    session.delete(task)
    session.commit()
    return {"status": "ok"}

@router.post("/downloads/{task_id}/retry")
def retry_download_task(task_id: int, session: Session = Depends(get_session)):
    task = session.get(DownloadTask, task_id)
    if not task:
        raise HTTPException(status_code=404, detail="Download task not found")
    task.status = "pending"
    task.progress = 0
    task.error_message = None
    session.add(task)
    session.commit()
    return {"status": "ok"}

class UploadEnqueueIn(BaseModel):
    jellyfin_id: str
    tmdb_id: Optional[int] = None
    media_type: str
    path: str
    title: str
    year: Optional[int] = None

@router.post("/uploads/enqueue")
def enqueue_upload(payload: UploadEnqueueIn, session: Session = Depends(get_session)):
    existing = session.exec(
        select(UploadTask)
        .where(UploadTask.jellyfin_id == payload.jellyfin_id)
        .where(UploadTask.status.in_(["pending", "uploading"]))
    ).first()
    if existing:
        return {"status": "ignored", "message": "Already in upload queue"}
    task = UploadTask(
        jellyfin_id=payload.jellyfin_id,
        tmdb_id=payload.tmdb_id,
        media_type=payload.media_type,
        path=payload.path,
        title=payload.title,
        year=payload.year,
        status="pending"
    )
    session.add(task)
    session.commit()
    return {"status": "ok"}

@router.get("/uploads/pending", response_model=List[UploadTask])
def list_pending_uploads(session: Session = Depends(get_session)):
    return session.exec(select(UploadTask).where(UploadTask.status == "pending")).all()

@router.get("/uploads/queue", response_model=List[UploadTask])
def list_queue_uploads(session: Session = Depends(get_session)):
    return session.exec(select(UploadTask).where(UploadTask.status.in_(["pending", "uploading", "failed"]))).all()

class UploadStatusIn(BaseModel):
    status: str
    progress: int
    error_message: Optional[str] = None

@router.post("/uploads/{task_id}/status")
def update_upload_status(task_id: int, payload: UploadStatusIn, session: Session = Depends(get_session)):
    """Update an upload task's progress and status, reported by the worker"""
    task = session.get(UploadTask, task_id)
    if not task:
        raise HTTPException(status_code=404, detail="Upload task not found")
    task.status = payload.status
    task.progress = payload.progress
    if payload.error_message:
        task.error_message = payload.error_message
    if payload.status in ["completed", "failed"]:
        task.completed_at = datetime.now(timezone.utc)
    session.add(task)
    session.commit()
    return {"status": "ok"}

@router.delete("/uploads/{task_id}")
def delete_upload_task(task_id: int, session: Session = Depends(get_session)):
    task = session.get(UploadTask, task_id)
    if not task:
        raise HTTPException(404, "Task not found")
    session.delete(task)
    session.commit()
    return {"status": "ok"}

@router.post("/uploads/{task_id}/retry")
def retry_upload_task(task_id: int, session: Session = Depends(get_session)):
    task = session.get(UploadTask, task_id)
    if not task:
        raise HTTPException(status_code=404, detail="Upload task not found")
    task.status = "pending"
    task.progress = 0
    task.error_message = None
    session.add(task)
    session.commit()
    return {"status": "ok"}
