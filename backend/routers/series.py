from typing import List, Optional
from fastapi import APIRouter, Depends, HTTPException
from sqlmodel import Session, select
from database import get_session
from models import Series, Season, Episode, Collection
from schemas import SeriesOut, SeriesUpdate, SeasonUpdate, EpisodeUpdate
from crud import prune_orphaned_media, get_or_create_season, get_or_create_episode, propagate_identification
from tmdb import TMDB
import tmdbsimple as tmdb_simple
from logger import logger

router = APIRouter(prefix="/series", tags=["series"])
tmdb = TMDB()

@router.get("", response_model=List[SeriesOut])
def list_series(session: Session = Depends(get_session)):
    """Return all series stored in DB"""
    return session.exec(select(Series)).all()

@router.get("/{series_id}", response_model=SeriesOut)
def get_series(series_id: int, session: Session = Depends(get_session)):
    series = session.get(Series, series_id)
    if not series:
        raise HTTPException(status_code=404, detail="Series not found")
    return series

@router.delete("/{series_id}")
def delete_series(series_id: int, session: Session = Depends(get_session)):
    """Delete a series and its seasons/episodes structure (unlinks collections)"""
    series = session.get(Series, series_id)
    if not series:
        raise HTTPException(status_code=404, detail="Series not found")
    for season in series.seasons:
        for episode in season.episodes:
            for collection in episode.collections:
                collection.episode_id = None
                session.add(collection)
            session.delete(episode)
        for collection in season.collections:
            collection.season_id = None
            session.add(collection)
        session.delete(season)
    session.delete(series)
    session.commit()
    prune_orphaned_media(session)
    return {"status": "ok", "message": f"Series {series_id} deleted"}

@router.patch("/{series_id}")
def update_series(series_id: int, request: SeriesUpdate, session: Session = Depends(get_session)):
    series = session.get(Series, series_id)
    if not series:
        raise HTTPException(status_code=404, detail="Series not found")
        
    update_data = request.dict(exclude_unset=True)
    for key, value in update_data.items():
        setattr(series, key, value)
        
    session.add(series)
    session.commit()
    session.refresh(series)
    return series

@router.patch("/{series_id}/seasons/{season_number}")
def update_season(series_id: int, season_number: int, request: SeasonUpdate, session: Session = Depends(get_session)):
    season = session.exec(
        select(Season).where(Season.series_id == series_id).where(Season.season_number == season_number)
    ).first()
    if not season:
        raise HTTPException(status_code=404, detail="Season not found")

    update_data = request.dict(exclude_unset=True)
    for key, value in update_data.items():
        setattr(season, key, value)

    session.add(season)
    session.commit()
    session.refresh(season)
    return season

@router.patch("/{series_id}/seasons/{season_number}/episodes/{episode_number}")
def update_episode(series_id: int, season_number: int, episode_number: int, request: EpisodeUpdate, session: Session = Depends(get_session)):
    season = session.exec(
        select(Season).where(Season.series_id == series_id).where(Season.season_number == season_number)
    ).first()
    if not season:
        raise HTTPException(status_code=404, detail="Season not found")

    episode = session.exec(
        select(Episode).where(Episode.season_id == season.id).where(Episode.episode_number == episode_number)
    ).first()
    if not episode:
        raise HTTPException(status_code=404, detail="Episode not found")

    update_data = request.dict(exclude_unset=True)
    for key, value in update_data.items():
        setattr(episode, key, value)

    session.add(episode)
    session.commit()
    session.refresh(episode)
    return episode

@router.get("/{series_id}/posters")
def get_series_posters(series_id: int, session: Session = Depends(get_session)):
    series = session.get(Series, series_id)
    if not series or not series.tmdb_id:
        return []
    try:
        images = tmdb_simple.TV(series.tmdb_id).images()
        posters = [p["file_path"] for p in images.get("posters", []) if "file_path" in p]
        return posters
    except Exception as e:
        logger.error(f"Error fetching series posters: {e}")
        return []

@router.get("/tmdb/{tmdb_id}", response_model=Optional[SeriesOut])
def get_series_by_tmdb(tmdb_id: int, session: Session = Depends(get_session)):
    """Return a single series by TMDB ID with its seasons and episodes"""
    series = session.exec(select(Series).where(Series.tmdb_id == tmdb_id)).first()
    if not series:
        return None
    return series

@router.post("/{series_id}/reidentify")
def reidentify_series(series_id: int, new_tmdb_id: int, session: Session = Depends(get_session)):
    series = session.get(Series, series_id)
    if not series:
        raise HTTPException(status_code=404, detail="Series not found")
        
    tmdb_result = tmdb.identify_by_tmdbid(new_tmdb_id, "tv")
    if not tmdb_result:
        raise HTTPException(status_code=404, detail="New TMDB ID not found on TMDB")
        
    existing_series = session.exec(select(Series).where(Series.tmdb_id == new_tmdb_id)).first()
    
    collections_to_remap = []
    for season in series.seasons:
        for collection in season.collections:
            collections_to_remap.append({
                "collection_id": collection.id,
                "season_num": season.season_number,
                "episode_num": None
            })
        for episode in season.episodes:
            for collection in episode.collections:
                collections_to_remap.append({
                    "collection_id": collection.id,
                    "season_num": season.season_number,
                    "episode_num": episode.episode_number
                })
                
    if existing_series:
        target_series_id = existing_series.id
    else:
        first_air = tmdb_result.get("first_air_date")
        release_year = int(first_air.split("-")[0]) if first_air else None
        
        tvdb_id = None
        try:
            external_ids = tmdb_simple.TV(new_tmdb_id).external_ids()
            tvdb_id = external_ids.get("tvdb_id")
        except Exception as e:
            logger.error(f"Error fetching TVDB ID: {e}")
            
        series.tmdb_id = new_tmdb_id
        series.manual_title = tmdb_result.get("name") or tmdb_result.get("original_name")
        series.poster_path = tmdb_result.get("poster_path")
        series.overview = tmdb_result.get("overview")
        series.release_year = release_year
        if tvdb_id:
            series.tvdb_id = tvdb_id
            
        session.add(series)
        session.commit()
        session.refresh(series)
        
        target_series_id = series.id

    if existing_series:
        for season in series.seasons:
            for episode in season.episodes:
                session.delete(episode)
            session.delete(season)
        session.delete(series)
    else:
        for season in series.seasons:
            for episode in season.episodes:
                session.delete(episode)
            session.delete(season)
            
    session.commit()
    
    for item in collections_to_remap:
        collection = session.get(Collection, item["collection_id"])
        if not collection:
            continue
        season_num = item["season_num"]
        episode_num = item["episode_num"]
        
        season_obj = get_or_create_season(session, target_series_id, season_num)
        if episode_num is not None:
            episode_obj = get_or_create_episode(session, season_obj.id, episode_num)
            collection.episode_id = episode_obj.id
            collection.season_id = None
        else:
            collection.season_id = season_obj.id
            collection.episode_id = None
        session.add(collection)
        
    session.commit()
    
    for item in collections_to_remap:
        collection = session.get(Collection, item["collection_id"])
        if collection:
            parsed = TMDB.clean_filename(collection.name)
            propagate_identification(session, parsed["clean_name"], tmdb)
            
    prune_orphaned_media(session)
    series_name = existing_series.manual_title if existing_series else series.manual_title
    return {"status": "ok", "message": f"Series re-identified to {series_name}"}
