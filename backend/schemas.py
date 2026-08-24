from datetime import datetime
from typing import List, Optional
from pydantic import BaseModel, ConfigDict

# ========================
# Upload / Input & Output
# ========================

class UploadIn(BaseModel):
    message_id: int
    user_message_id: int | None = None  # Same message in the user account's own numbering
    filename: str
    filesize: int | None = None
    mime_type: str | None = None
    created_at: str | None = None  # ISO format date string
    tmdb_id: int | None = None     # Pre-identified TMDB ID
    technical_metadata: str | None = None
    storage_peer: str = "bot"      # "bot" = bot-owner chat, "saved" = user's Saved Messages

class ItemOut(BaseModel):
    id: int
    message_id: int | None
    filename: str | None
    collection_id: int | None
    movie_id: int | None
    season_id: int | None
    episode_id: int | None

# ========================
# DTOs / Out Models
# ========================

class FileOut(BaseModel):
    id: int
    message_id: int
    filename: str
    filesize: int
    mime_type: Optional[str]
    created_at: datetime
    collection_id: int

    model_config = ConfigDict(from_attributes=True)

class CollectionOut(BaseModel):
    id: int
    name: Optional[str]
    movie_id: Optional[int]
    episode_id: Optional[int]
    season_id: Optional[int]
    files: List[FileOut] = []
    quality: Optional[str]
    audio_languages: Optional[str]
    subtitle_languages: Optional[str]
    tags: Optional[str]
    notes: Optional[str]
    technical_metadata: Optional[str] = None
    local_path: Optional[str] = None

    model_config = ConfigDict(from_attributes=True)

class EpisodeOut(BaseModel):
    id: int
    episode_number: int
    title: Optional[str]
    collections: List[CollectionOut] = []

    model_config = ConfigDict(from_attributes=True)

class SeasonOut(BaseModel):
    id: int
    season_number: int
    local_metadata: bool
    episodes: List[EpisodeOut] = []
    collections: List[CollectionOut] = []

    model_config = ConfigDict(from_attributes=True)

class SeriesOut(BaseModel):
    id: int
    tmdb_id: Optional[int]
    manual_title: Optional[str]
    poster_path: Optional[str]
    overview: Optional[str]
    release_year: Optional[int]
    seasons: List[SeasonOut] = []
    created_at: Optional[datetime] = None

    model_config = ConfigDict(from_attributes=True)

class MovieOut(BaseModel):
    id: int
    tmdb_id: Optional[int]
    title: Optional[str]
    poster_path: Optional[str]
    collections: List[CollectionOut] = []
    release_year: Optional[int]
    overview: Optional[str]
    tags: Optional[str]
    notes: Optional[str]
    created_at: Optional[datetime] = None

    model_config = ConfigDict(from_attributes=True)

# ========================
# Update & Action Schemas
# ========================

class MovieUpdate(BaseModel):
    title: Optional[str] = None
    poster_path: Optional[str] = None
    overview: Optional[str] = None
    release_year: Optional[int] = None

class SeriesUpdate(BaseModel):
    manual_title: Optional[str] = None
    poster_path: Optional[str] = None
    overview: Optional[str] = None
    release_year: Optional[int] = None

class SeasonUpdate(BaseModel):
    local_metadata: Optional[bool] = None

class EpisodeUpdate(BaseModel):
    title: Optional[str] = None

class CollectionUpdate(BaseModel):
    name: Optional[str] = None
    quality: Optional[str] = None
    audio_languages: Optional[str] = None
    subtitle_languages: Optional[str] = None
    tags: Optional[str] = None
    notes: Optional[str] = None
    technical_metadata: Optional[str] = None
    local_path: Optional[str] = None

    season_number: Optional[int] = None
    episode_number: Optional[int] = None
    clear_episode: Optional[bool] = None

class CollectionCreate(BaseModel):
    name: str
    movie_id: Optional[int] = None
    season_id: Optional[int] = None
    episode_id: Optional[int] = None
    quality: Optional[str] = None
    audio_languages: Optional[str] = None
    subtitle_languages: Optional[str] = None
    tags: Optional[str] = None
    notes: Optional[str] = None

class IdentifyRequest(BaseModel):
    tmdb_id: int

class ReidentifyCollectionRequest(BaseModel):
    tmdb_id: int | None = None
    media_type: str | None = None
    season_number: int | None = None
    episode_number: int | None = None

class BatchIdentifyRequest(BaseModel):
    collection_ids: List[int]
    tmdb_id: int

class BatchDeleteRequest(BaseModel):
    collection_ids: List[int]

class FileUpdate(BaseModel):
    collection_id: Optional[int] = None

class BatchFileDeleteRequest(BaseModel):
    file_ids: List[int]

class BatchFileMoveRequest(BaseModel):
    file_ids: List[int]
    collection_id: int
