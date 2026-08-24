import os
from contextlib import asynccontextmanager
from fastapi import FastAPI, Depends
from fastapi.middleware.cors import CORSMiddleware
from sqlmodel import Session

from config import settings
from database import init_db, get_session
from crud import create_file, identify_collection
from tmdb import TMDB
from schemas import UploadIn, ItemOut

from routers.movies import router as movies_router
from routers.series import router as series_router
from routers.collections import router as collections_router
from routers.files import router as files_router
from routers.maintenance import router as maintenance_router
from routers.tasks import router as tasks_router
from routers.watch import router as watch_router

@asynccontextmanager
async def lifespan(app: FastAPI):
    os.makedirs(settings.DATA_DIR, exist_ok=True)
    init_db()
    yield

app = FastAPI(title=settings.PROJECT_NAME, version=settings.APP_VERSION, lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

tmdb = TMDB()

@app.get("/")
def root():
    return {"status": "ok", "message": "Backend is running"}

@app.get("/version")
def version():
    return {"name": settings.PROJECT_NAME, "version": settings.APP_VERSION}

@app.post("/upload", response_model=ItemOut)
def upload_endpoint(payload: UploadIn, session: Session = Depends(get_session)):
    file, collection = create_file(
        session,
        message_id=payload.message_id,
        user_message_id=payload.user_message_id,
        filename=payload.filename,
        filesize=payload.filesize,
        mime_type=payload.mime_type,
        created_at=payload.created_at,
        tmdb_id=payload.tmdb_id,
        technical_metadata=payload.technical_metadata,
        storage_peer=payload.storage_peer
    )

    if collection.movie_id is None and collection.episode_id is None and collection.season_id is None:
        identify_collection(
            session, collection.id, tmdb,
            forced_tmdb_id=payload.tmdb_id
        )
        session.refresh(collection)

    return {
        "id": file.id,
        "message_id": file.message_id,
        "filename": file.filename,
        "collection_id": collection.id,
        "movie_id": collection.movie_id,
        "season_id": collection.season_id,
        "episode_id": collection.episode_id
    }

from routers.search import router as search_router

# Register APIRouters
app.include_router(movies_router)
app.include_router(series_router)
app.include_router(collections_router)
app.include_router(files_router)
app.include_router(maintenance_router)
app.include_router(tasks_router)
app.include_router(search_router)
app.include_router(watch_router)
