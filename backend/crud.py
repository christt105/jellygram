from sqlmodel import select
from sqlalchemy.exc import NoResultFound
from models import File, Collection, Movie, Series, Season, Episode
from database import engine
from sqlmodel import Session
from datetime import datetime, timezone
from typing import Optional, Dict, Any
from logger import logger
from tmdb import TMDB

import re

QUALITY_MAP = {
    "4K": ["2160p", "4k", "uhd"],
    "2K": ["1440p", "2k"],
    "1080p": ["1080p", "fullhd", "fhd"],
    "720p": ["720p"],
    "480p": ["480p"],
    "360p": ["360p"],
    "240p": ["240p"]
}

HDR_MAP = {
    "HDR10+": ["hdr10+"],
    "HDR10": ["hdr10"],
    "DolbyVision": ["dolbyvision", "dv"],
    "HLG": ["hlg"],
    "HDR": ["hdr"],
    "SDR": []  # default si no se detecta nada
}

def try_subtract_quality(filename: str):
    fname = filename.lower()
    
    # Normalizar resolución
    resolution = None
    for key, values in QUALITY_MAP.items():
        if any(v in fname for v in values):
            resolution = key
            break
    
    # Normalizar HDR
    hdr = "SDR"
    for key, values in HDR_MAP.items():
        if any(v in fname for v in values):
            hdr = key
            break
    
    return resolution, hdr


def extract_quality_from_metadata(technical_metadata: str) -> Optional[str]:
    import json
    try:
        data = json.loads(technical_metadata)
        streams = data.get("streams", [])
        video_stream = next((s for s in streams if s.get("codec_type") == "video"), None)
        if not video_stream:
            return None
        
        width = video_stream.get("width")
        height = video_stream.get("height")
        if not width or not height:
            return None
            
        resolution = None
        if width >= 3840 or height >= 2160:
            resolution = "4K"
        elif width >= 2560 or height >= 1440:
            resolution = "2K"
        elif width >= 1920 or height >= 1080:
            resolution = "1080p"
        elif width >= 1280 or height >= 720:
            resolution = "720p"
        elif width >= 848 or height >= 480:
            resolution = "480p"
            
        if not resolution:
            return None
            
        # Detect HDR
        color_transfer = video_stream.get("color_transfer", "")
        # smpte2084 (PQ), arib-std-b67 (HLG)
        is_hdr = color_transfer in ["smpte2084", "arib-std-b67"]
        
        if is_hdr:
            return f"{resolution} HDR"
        return resolution
    except Exception as e:
        logger.error(f"Failed to parse technical metadata for quality: {e}")
        return None


def get_or_create_collection(session: Session, filename: str, mime_type: str, technical_metadata: Optional[str] = None):

    collection_name = filename.split('.')[0] # TODO: Improve collection name extraction logic (?)

    quality = None
    if technical_metadata:
        quality = extract_quality_from_metadata(technical_metadata)

    if not quality:
        resolution, hdr = try_subtract_quality(filename)
        quality = f"{resolution}" if resolution else None
        if quality and hdr and hdr != "SDR":
            quality += f" {hdr}"

    if mime_type.startswith("video/"):
        # if it is a video, we can assume that it is a file on its own
        
        # TODO: Check if there is a collection with the same name and use the same movie_id or episode_id if it exists
        
        collection = Collection(name=collection_name, quality=quality, technical_metadata=technical_metadata)
        session.add(collection)
        session.commit()
        session.refresh(collection)
        return collection
    # otherwise, it is a collection of files, so we need to check if it already exists and create it if it does not exist
    # WARN: if the filename is the same as other files, it will be added to the same collection
    
    collection = session.exec(
        select(Collection).where(Collection.name == collection_name)
    ).first()

    if collection:
        updated = False
        if technical_metadata and not collection.technical_metadata:
            collection.technical_metadata = technical_metadata
            updated = True
        
        # Prioritize technical metadata quality
        if collection.technical_metadata:
            extracted_quality = extract_quality_from_metadata(collection.technical_metadata)
            if extracted_quality and collection.quality != extracted_quality:
                collection.quality = extracted_quality
                updated = True
        elif not collection.quality:
            # Fallback to filename
            resolution, hdr = try_subtract_quality(filename)
            quality_fn = f"{resolution}" if resolution else None
            if quality_fn and hdr and hdr != "SDR":
                quality_fn += f" {hdr}"
            if quality_fn:
                collection.quality = quality_fn
                updated = True
                
        if updated:
            session.add(collection)
            session.commit()
            session.refresh(collection)
        return collection

    collection = Collection(name=collection_name, quality=quality, technical_metadata=technical_metadata)
    session.add(collection)
    session.commit()
    session.refresh(collection)
    return collection

def create_file(session: Session, message_id, filename, filesize, mime_type, created_at, tmdb_id=None, technical_metadata=None, storage_peer="bot", user_message_id=None):
    existing = session.exec(select(File).where(File.message_id == message_id)).first()
    if existing:
        if user_message_id is not None and existing.user_message_id is None:
            existing.user_message_id = user_message_id
            session.add(existing)
            session.commit()
            session.refresh(existing)
        return existing, session.get(Collection, existing.collection_id)

    collection = get_or_create_collection(session, filename, mime_type, technical_metadata)

    file_created_at = datetime.fromisoformat(created_at) if created_at else datetime.now(timezone.utc)
    file = File(
        message_id=message_id,
        user_message_id=user_message_id,
        filename=filename,
        filesize=filesize,
        mime_type=mime_type,
        created_at=file_created_at,
        collection_id=collection.id,
        storage_peer=storage_peer
    )
    session.add(file)
    session.commit()
    session.refresh(file)
    return file, collection

def get_file(session: Session, item_id: int) -> Optional[File]:
    return session.get(File, item_id)

def get_collection(session: Session, item_id: int) -> Optional[File]:
    return session.get(Collection, item_id)

def identify_collection(
    session: Session,
    collection_id: int,
    tmdb: TMDB,
    forced_tmdb_id: int | None = None,
    forced_media_type: str | None = None,
    forced_season: int | None = None,
    forced_episode: int | None = None,
) -> bool:
    logger.info(f"Identifying collection {collection_id}" + (f" (forced tmdb_id={forced_tmdb_id})" if forced_tmdb_id else ""))

    collection = get_collection(session, collection_id)

    if not collection:
        logger.error(f"Collection {collection_id} not found")
        return False

    if forced_tmdb_id is None and (collection.movie_id is not None or collection.episode_id is not None):
        logger.info(f"Collection {collection_id} already identified")
        return True

    parsed = TMDB.clean_filename(collection.name)
    clean_name = parsed["clean_name"]
    content_type = forced_media_type or parsed["type"]

    tmdb_result = None

    try:
        if forced_tmdb_id is not None:
            tmdb_result = tmdb.identify_by_tmdbid(forced_tmdb_id, content_type)
        else:
            # Try local DB match first to save API calls and ensure consistency
            if content_type == "tv":
                existing_series = session.exec(
                    select(Series).where(Series.manual_title == clean_name)
                ).first()
                if existing_series:
                    logger.info(f"Local Series match found for '{clean_name}': Series {existing_series.id}")
                    tmdb_result = {
                        "id": existing_series.tmdb_id,
                        "name": existing_series.manual_title,
                        "media_type": "tv"
                    }
            else:
                existing_movie = session.exec(
                    select(Movie).where(Movie.title == clean_name)
                ).first()
                if existing_movie:
                    logger.info(f"Local Movie match found for '{clean_name}': Movie {existing_movie.id}")
                    tmdb_result = {
                        "id": existing_movie.tmdb_id,
                        "title": existing_movie.title,
                        "media_type": "movie"
                    }

            # Fallback to TMDB API if not matched locally
            if not tmdb_result:
                tmdb_result = tmdb.identify_by_filename(collection.name)
    except Exception as e:
        logger.error(f"Failed to identify collection {collection_id}: {e}")
        tmdb_result = None

    if not tmdb_result:
        logger.warning(f"No TMDB identification result for collection {collection_id}")
        return False

    media_type = tmdb_result.get("media_type")

    if not media_type:
        logger.warning(f"No media type found for collection {collection_id}")
        return False

    if media_type == "movie":
        movie = get_or_create_movie(session, tmdb_result)
        collection.movie_id = movie.id
        collection.episode_id = None
        collection.season_id = None
        session.add(collection)
        session.commit()
        logger.info(f"Linked collection {collection_id} to movie {movie.id} ({movie.title})")
        propagate_identification(session, clean_name, tmdb)
        prune_orphaned_media(session)
        return True

    elif media_type == "tv":
        series = get_or_create_series(session, tmdb_result)

        if forced_season is not None:
            season_num = forced_season
            episode_num = forced_episode
        else:
            parsed = TMDB.clean_filename(collection.name)
            season_num = parsed.get("season")
            episode_num = parsed.get("episode")
        
        collection.movie_id = None # Clear movie reference if it's TV
        
        if season_num is not None:
            season = get_or_create_season(session, series.id, season_num)
            
            if episode_num is not None:
                episode = get_or_create_episode(session, season.id, episode_num)
                collection.episode_id = episode.id
                collection.season_id = None
                session.add(collection)
                session.commit()
                logger.info(f"Linked collection {collection_id} to TV episode: {series.manual_title} S{season_num:02d}E{episode_num:02d}")
                propagate_identification(session, clean_name, tmdb)
                prune_orphaned_media(session)
                return True
            else:
                collection.season_id = season.id
                collection.episode_id = None
                session.add(collection)
                session.commit()
                logger.info(f"Linked collection {collection_id} to TV season pack: {series.manual_title} Season {season_num}")
                propagate_identification(session, clean_name, tmdb)
                prune_orphaned_media(session)
                return True
        else:
            # Fallback if no season parsed: Link to a default Season 1
            season = get_or_create_season(session, series.id, 1)
            collection.season_id = season.id
            collection.episode_id = None
            session.add(collection)
            session.commit()
            logger.info(f"Linked collection {collection_id} to TV series (fallback Season 1): {series.manual_title}")
            propagate_identification(session, clean_name, tmdb)
            prune_orphaned_media(session)
            return True

    logger.info(f"TMDB identification result: {tmdb_result}")
    return False


def guess_watched_file(tmdb: TMDB, filename: str) -> Dict[str, Any]:
    """Guess a watched file's media identity, reusing the same parsing/matching
    pipeline as the Telegram upload path (TMDB.clean_filename/identify_by_filename).

    Season/episode always come straight from the filename regex (clean_filename),
    never from TMDB, per the regex-first design: TMDB only resolves the movie/
    series identity, it never renumbers episodes.

    guess_source is "filename" when the identity was resolved from an explicit
    [tmdbid-NNN] tag embedded in the filename itself (deterministic), otherwise
    "tmdb", meaning the cleaned title was fuzzy-matched against TMDB search
    results. confidence is 1.0 for the "filename" case, and the difflib
    similarity ratio of the fuzzy match otherwise (0.0 if nothing matched).
    """
    parsed = TMDB.clean_filename(filename)

    try:
        tmdb_result = tmdb.identify_by_filename(filename)
    except Exception as e:
        logger.error(f"Failed to guess watched file '{filename}': {e}")
        tmdb_result = None

    tmdb_id = (tmdb_result or {}).get("id")
    from_filename_tag = bool(
        parsed["tmdbid"] is not None and tmdb_id is not None and tmdb_id == parsed["tmdbid"]
    )

    if from_filename_tag:
        source = "filename"
        confidence = 1.0
    else:
        source = "tmdb"
        confidence = float((tmdb_result or {}).get("_match_score") or 0.0)

    media_type = (tmdb_result or {}).get("media_type") or parsed["type"]
    title = None
    if tmdb_result:
        title = tmdb_result.get("title") if media_type == "movie" else tmdb_result.get("name")
        title = title or tmdb_result.get("title") or tmdb_result.get("name")

    year = None
    if tmdb_result:
        release_date = tmdb_result.get("release_date") if media_type == "movie" else tmdb_result.get("first_air_date")
        if release_date:
            year = int(release_date[:4])

    return {
        "media_type": media_type,
        "tmdb_id": tmdb_id,
        "title": title,
        "year": year,
        "season": parsed.get("season"),
        "episode": parsed.get("episode"),
        "confidence": confidence,
        "source": source,
    }


def prune_orphaned_media(session: Session):
    """Delete movies and series left without any linked collection.

    Manually added entries are kept even while empty so their files can be
    attached later; everything else with no collections is pruned.
    """
    pruned = False

    for movie in session.exec(select(Movie)).all():
        if movie.manually_added:
            continue
        if not movie.collections:
            session.delete(movie)
            pruned = True

    for series in session.exec(select(Series)).all():
        if series.manually_added:
            continue
        has_collections = any(
            season.collections or any(ep.collections for ep in season.episodes)
            for season in series.seasons
        )
        if not has_collections:
            for season in series.seasons:
                for episode in season.episodes:
                    session.delete(episode)
                session.delete(season)
            session.delete(series)
            pruned = True

    if pruned:
        session.commit()


def propagate_identification(session: Session, clean_name: str, tmdb: TMDB):
    """Automatically propagate identification to other unidentified collections with the same clean name."""
    try:
        unidentified = session.exec(
            select(Collection)
            .where(Collection.movie_id == None)
            .where(Collection.episode_id == None)
            .where(Collection.season_id == None)
        ).all()
        for coll in unidentified:
            coll_parsed = TMDB.clean_filename(coll.name)
            if coll_parsed["clean_name"] == clean_name:
                logger.info(f"Propagating identification for '{clean_name}' to collection {coll.id} ({coll.name})")
                # Recursive call will resolve it instantly using the local DB match
                identify_collection(session, coll.id, tmdb)
    except Exception as e:
        logger.error(f"Error during propagation: {e}")


def get_or_create_series(session: Session, tmdb_series: dict) -> Series:
    tmdb_id = tmdb_series["id"]
    series = session.exec(select(Series).where(Series.tmdb_id == tmdb_id)).first()
    
    release_year = None
    first_air = tmdb_series.get("first_air_date")
    if first_air and len(first_air) >= 4:
        try:
            release_year = int(first_air[:4])
        except ValueError:
            pass

    # Fetch tvdb_id from TMDB if not available
    tvdb_id = None
    if not series or not getattr(series, "tvdb_id", None):
        try:
            import tmdbsimple as tmdb_lib
            tv_details = tmdb_lib.TV(tmdb_id)
            exts = tv_details.external_ids()
            if exts and "tvdb_id" in exts:
                tvdb_id = exts["tvdb_id"]
        except Exception as e:
            logger.error(f"Error fetching TVDB ID for TMDB series {tmdb_id}: {e}")

    if not series:
        series = Series(
            tmdb_id=tmdb_id,
            tvdb_id=tvdb_id,
            manual_title=tmdb_series.get("name") or tmdb_series.get("original_name"),
            poster_path=tmdb_series.get("poster_path"),
            overview=tmdb_series.get("overview"),
            release_year=release_year
        )
        session.add(series)
        session.commit()
        session.refresh(series)
    else:
        # Update missing fields
        updated = False
        if not getattr(series, "tvdb_id", None) and tvdb_id:
            series.tvdb_id = tvdb_id
            updated = True
        if not series.poster_path and tmdb_series.get("poster_path"):
            series.poster_path = tmdb_series.get("poster_path")
            updated = True
        if not series.overview and tmdb_series.get("overview"):
            series.overview = tmdb_series.get("overview")
            updated = True
        if not series.release_year and release_year:
            series.release_year = release_year
            updated = True
        if updated:
            session.add(series)
            session.commit()
            session.refresh(series)
    return series

def get_or_create_season(session: Session, series_id: int, season_number: int) -> Season:
    season = session.exec(
        select(Season)
        .where(Season.series_id == series_id)
        .where(Season.season_number == season_number)
    ).first()
    if not season:
        season = Season(
            series_id=series_id,
            season_number=season_number
        )
        session.add(season)
        session.commit()
        session.refresh(season)
    return season

def get_or_create_episode(session: Session, season_id: int, episode_number: int) -> Episode:
    episode = session.exec(
        select(Episode)
        .where(Episode.season_id == season_id)
        .where(Episode.episode_number == episode_number)
    ).first()
    if not episode:
        episode = Episode(
            season_id=season_id,
            episode_number=episode_number,
            title=f"Episode {episode_number}"
        )
        session.add(episode)
        session.commit()
        session.refresh(episode)
    return episode
        
    
def get_or_create_movie(session: Session, tmdb_movie: dict) -> Movie:
    """Get or create a movie by TMDB ID."""
    tmdb_id = tmdb_movie["id"]
    try:
        movie = session.exec(
            select(Movie).where(Movie.tmdb_id == tmdb_id)
        ).one()
        return movie
    except NoResultFound:
        movie = Movie(
            tmdb_id=tmdb_id, 
            title=tmdb_movie["title"],
            release_year=int(tmdb_movie["release_date"].split("-")[0]) if tmdb_movie.get("release_date") else None,
            poster_path=tmdb_movie.get("poster_path")
        )
        session.add(movie)
        session.commit()
        session.refresh(movie)
        return movie

