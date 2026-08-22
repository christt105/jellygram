from typing import Optional, List
from sqlmodel import SQLModel, Field, Relationship
from datetime import datetime, timezone

# ========================
# Media core
# ========================

class Movie(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    tmdb_id: Optional[int] = Field(default=None, unique=True, index=True)
    title: Optional[str] = None
    poster_path: Optional[str] = None
    release_year: Optional[int] = None
    overview: Optional[str] = None
    tags: Optional[str] = None  # Comma separated tags
    notes: Optional[str] = None
    manually_added: bool = Field(default=False)
    created_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc), nullable=False)

    collections: List["Collection"] = Relationship(
        back_populates="movie",
        sa_relationship_kwargs={"foreign_keys": "[Collection.movie_id]"}
    )

class Series(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    tmdb_id: Optional[int] = Field(default=None, unique=True, index=True)
    tvdb_id: Optional[int] = None
    manual_title: Optional[str] = None
    poster_path: Optional[str] = None
    overview: Optional[str] = None
    release_year: Optional[int] = None
    tags: Optional[str] = None
    notes: Optional[str] = None
    manually_added: bool = Field(default=False)
    created_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc), nullable=False)

    seasons: List["Season"] = Relationship(back_populates="series")

class Season(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    series_id: int = Field(foreign_key="series.id")
    season_number: int
    tags: Optional[str] = None
    notes: Optional[str] = None

    series: "Series" = Relationship(back_populates="seasons")
    episodes: List["Episode"] = Relationship(back_populates="season")
    collections: List["Collection"] = Relationship(
        back_populates="season",
        sa_relationship_kwargs={"foreign_keys": "[Collection.season_id]"}
    )

class Episode(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    season_id: int = Field(foreign_key="season.id")
    episode_number: int
    title: Optional[str] = None
    tags: Optional[str] = None
    notes: Optional[str] = None

    season: "Season" = Relationship(back_populates="episodes")
    collections: List["Collection"] = Relationship(
        back_populates="episode",
        sa_relationship_kwargs={"foreign_keys": "[Collection.episode_id]"}
    )

# ========================
# Files & Collections
# ========================

class Collection(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    name: Optional[str] = None
    quality: Optional[str] = None
    audio_languages: Optional[str] = None   # Comma separated
    subtitle_languages: Optional[str] = None  # Comma separated
    tags: Optional[str] = None
    notes: Optional[str] = None
    technical_metadata: Optional[str] = None
    local_path: Optional[str] = None

    movie_id: Optional[int] = Field(default=None, foreign_key="movie.id")
    season_id: Optional[int] = Field(default=None, foreign_key="season.id")
    episode_id: Optional[int] = Field(default=None, foreign_key="episode.id")

    movie: Optional[Movie] = Relationship(
        back_populates="collections",
        sa_relationship_kwargs={"foreign_keys": "[Collection.movie_id]"}
    )
    season: Optional[Season] = Relationship(
        back_populates="collections",
        sa_relationship_kwargs={"foreign_keys": "[Collection.season_id]"}
    )
    episode: Optional[Episode] = Relationship(
        back_populates="collections",
        sa_relationship_kwargs={"foreign_keys": "[Collection.episode_id]"}
    )

    files: List["File"] = Relationship(back_populates="collection")

class File(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    message_id: int
    # The same message as seen by the user account, which numbers private chats on its own.
    # Nullable and opportunistic: message_id alone always keeps the file readable by the bot.
    user_message_id: Optional[int] = None
    filename: str
    filesize: int
    mime_type: Optional[str] = None
    created_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc), nullable=False)
    storage_peer: str = Field(default="bot")

    collection_id: int = Field(foreign_key="collection.id")
    collection: "Collection" = Relationship(back_populates="files")

class DownloadTask(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    collection_id: int = Field(foreign_key="collection.id")
    name_suffix: Optional[str] = None
    status: str = "pending" # pending, downloading, completed, failed
    progress: int = 0
    error_message: Optional[str] = None
    created_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))
    completed_at: Optional[datetime] = None

class WatchedFile(SQLModel, table=True):
    """A file seen by bot-net's downloads-folder watcher, guessed against TMDB.

    status flow: pending -> notified -> confirmed/corrected -> moved, with
    two terminal states off the happy path: removed (the file vanished from
    the folder without going through this flow) and error (the move failed).

    confidence is a float 0-1: 1.0 when the identity came from an explicit
    [tmdbid-NNN] tag in the filename or a human confirmation/correction,
    otherwise the difflib similarity ratio of the TMDB fuzzy title match
    (0.0 when no match was found at all). See crud.guess_watched_file.
    """
    id: Optional[int] = Field(default=None, primary_key=True)
    path: str = Field(unique=True, index=True)
    filename: str
    filesize: int
    first_seen_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc), nullable=False)

    guess_media_type: Optional[str] = None  # "movie" or "tv"
    guess_tmdb_id: Optional[int] = None
    guess_title: Optional[str] = None
    guess_season: Optional[int] = None
    guess_episode: Optional[int] = None
    confidence: float = Field(default=0.0)
    guess_source: Optional[str] = None  # "filename" or "tmdb"

    status: str = Field(default="pending")  # pending, notified, confirmed, corrected, moved, removed, error
    notified_at: Optional[datetime] = None
    moved_path: Optional[str] = None
    error_message: Optional[str] = None

class UploadTask(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    jellyfin_id: str
    tmdb_id: Optional[int] = None
    media_type: str # movie or series
    path: str
    title: str
    year: Optional[int] = None
    status: str = "pending" # pending, uploading, completed, failed
    progress: int = 0
    error_message: Optional[str] = None
    created_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))
    completed_at: Optional[datetime] = None
