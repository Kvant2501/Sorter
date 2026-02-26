# -*- coding: utf-8 -*-
"""
PhotoSorter Gallery — local photo gallery service.
Indexes photos without moving them, generates thumbnails,
provides a web UI with timeline and auto-albums.
"""

import asyncio
import hashlib
import io
import logging
import os
import sqlite3
import time
from contextlib import asynccontextmanager
from datetime import datetime
from pathlib import Path
from typing import Optional

import httpx
from fastapi import BackgroundTasks, FastAPI, Query, Request
from fastapi.responses import FileResponse, HTMLResponse, JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from PIL import Image, ExifTags

# Face recognition
import numpy as np
import cv2
from insightface.app import FaceAnalysis

logger = logging.getLogger("gallery")
logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(name)s] %(levelname)s: %(message)s")

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
DATA_DIR = Path("/data")
DB_PATH = DATA_DIR / "gallery.db"
THUMB_DIR = DATA_DIR / "thumbnails"
THUMB_SIZE = (320, 320)

PHOTO_EXTENSIONS = {
    ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif",
    ".cr2", ".cr3", ".nef", ".arw", ".dng", ".webp",
}

CLIP_SERVICE_URL = os.environ.get("CLIP_SERVICE_URL", "http://clip-service:8000")

# Face recognition configuration
FACE_MODEL_NAME = os.environ.get("FACE_MODEL_NAME", "buffalo_l")
FACE_SIM_THRESHOLD = float(os.environ.get("FACE_SIM_THRESHOLD", "0.38"))

_face_app: FaceAnalysis | None = None


def get_face_app() -> FaceAnalysis:
    global _face_app
    if _face_app is None:
        logger.info(f"Loading face model: {FACE_MODEL_NAME} (CPU)")
        app = FaceAnalysis(name=FACE_MODEL_NAME, providers=["CPUExecutionProvider"])
        # det_size affects speed/quality; 640 is a common default
        app.prepare(ctx_id=-1, det_size=(640, 640))
        _face_app = app
        logger.info("Face model loaded.")
    return _face_app


def cosine_similarity(a: np.ndarray, b: np.ndarray) -> float:
    denom = (np.linalg.norm(a) * np.linalg.norm(b))
    if denom == 0:
        return 0.0
    return float(np.dot(a, b) / denom)


def image_to_bgr(file_path: str) -> np.ndarray:
    img = cv2.imdecode(np.fromfile(file_path, dtype=np.uint8), cv2.IMREAD_COLOR)
    if img is None:
        # fallback for some paths
        img = cv2.imread(file_path)
    return img


def detect_faces(file_path: str):
    bgr = image_to_bgr(file_path)
    if bgr is None:
        return []
    face_app = get_face_app()
    faces = face_app.get(bgr)
    # Filter invalid
    valid = []
    for f in faces:
        if getattr(f, "bbox", None) is None or getattr(f, "embedding", None) is None:
            continue
        x1, y1, x2, y2 = [int(v) for v in f.bbox]
        w, h = max(0, x2 - x1), max(0, y2 - y1)
        if w < 20 or h < 20:
            continue
        valid.append({
            "bbox": (x1, y1, w, h),
            "embedding": np.asarray(f.embedding, dtype=np.float32),
            "det_score": float(getattr(f, "det_score", 0.0)),
        })
    return valid


def upsert_person_by_face(conn: sqlite3.Connection, embedding: np.ndarray) -> int | None:
    """Assign a face to an existing person if similar enough; otherwise return None."""
    rows = conn.execute(
        "SELECT f.person_id, f.embedding FROM faces f WHERE f.person_id IS NOT NULL"
    ).fetchall()
    best_pid = None
    best_sim = 0.0
    for r in rows:
        pid = r["person_id"]
        emb = np.frombuffer(r["embedding"], dtype=np.float32)
        sim = cosine_similarity(embedding, emb)
        if sim > best_sim:
            best_sim = sim
            best_pid = pid

    if best_pid is not None and best_sim >= FACE_SIM_THRESHOLD:
        return int(best_pid)
    return None


# ---------------------------------------------------------------------------
# Database helpers
# ---------------------------------------------------------------------------

def get_db() -> sqlite3.Connection:
    conn = sqlite3.connect(str(DB_PATH))
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA foreign_keys=ON")
    return conn


def init_db():
    """Create tables if they don't exist."""
    conn = get_db()
    conn.executescript("""
        CREATE TABLE IF NOT EXISTS photos (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            file_path TEXT UNIQUE NOT NULL,
            file_name TEXT NOT NULL,
            file_size INTEGER DEFAULT 0,
            file_hash TEXT,
            date_taken TEXT,
            year INTEGER,
            month INTEGER,
            day INTEGER,
            width INTEGER,
            height INTEGER,
            camera_make TEXT,
            camera_model TEXT,
            ai_category TEXT,
            ai_confidence REAL,
            thumb_path TEXT,
            indexed_at TEXT NOT NULL,
            source_folder TEXT
        );

        CREATE TABLE IF NOT EXISTS albums (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            album_type TEXT NOT NULL DEFAULT 'manual',
            description TEXT,
            cover_photo_id INTEGER,
            created_at TEXT NOT NULL,
            FOREIGN KEY (cover_photo_id) REFERENCES photos(id)
        );

        CREATE TABLE IF NOT EXISTS album_photos (
            album_id INTEGER NOT NULL,
            photo_id INTEGER NOT NULL,
            sort_order INTEGER DEFAULT 0,
            PRIMARY KEY (album_id, photo_id),
            FOREIGN KEY (album_id) REFERENCES albums(id) ON DELETE CASCADE,
            FOREIGN KEY (photo_id) REFERENCES photos(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS indexing_tasks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            folder_path TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'pending',
            total_files INTEGER DEFAULT 0,
            processed_files INTEGER DEFAULT 0,
            started_at TEXT,
            finished_at TEXT,
            error TEXT
        );

        -- Face recognition
        CREATE TABLE IF NOT EXISTS persons (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS faces (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            photo_id INTEGER NOT NULL,
            bbox_x INTEGER NOT NULL,
            bbox_y INTEGER NOT NULL,
            bbox_w INTEGER NOT NULL,
            bbox_h INTEGER NOT NULL,
            embedding BLOB NOT NULL,
            det_score REAL,
            person_id INTEGER,
            created_at TEXT NOT NULL,
            FOREIGN KEY (photo_id) REFERENCES photos(id) ON DELETE CASCADE,
            FOREIGN KEY (person_id) REFERENCES persons(id) ON DELETE SET NULL
        );

        CREATE INDEX IF NOT EXISTS idx_photos_year_month ON photos(year, month);
        CREATE INDEX IF NOT EXISTS idx_photos_ai_category ON photos(ai_category);
        CREATE INDEX IF NOT EXISTS idx_photos_date_taken ON photos(date_taken);
        CREATE INDEX IF NOT EXISTS idx_photos_source ON photos(source_folder);

        CREATE INDEX IF NOT EXISTS idx_faces_photo ON faces(photo_id);
        CREATE INDEX IF NOT EXISTS idx_faces_person ON faces(person_id);
    """)
    conn.commit()
    conn.close()


# ---------------------------------------------------------------------------
# EXIF helpers
# ---------------------------------------------------------------------------

EXIF_DATE_TAG = None
for tag_id, tag_name in ExifTags.TAGS.items():
    if tag_name == "DateTimeOriginal":
        EXIF_DATE_TAG = tag_id
        break


def extract_exif(file_path: str) -> dict:
    """Extract EXIF metadata from an image file."""
    result = {
        "date_taken": None,
        "width": None,
        "height": None,
        "camera_make": None,
        "camera_model": None,
    }
    try:
        with Image.open(file_path) as img:
            result["width"] = img.width
            result["height"] = img.height
            exif_data = img.getexif()
            if exif_data:
                # DateTimeOriginal
                if EXIF_DATE_TAG and EXIF_DATE_TAG in exif_data:
                    raw = exif_data[EXIF_DATE_TAG]
                    try:
                        dt = datetime.strptime(str(raw), "%Y:%m:%d %H:%M:%S")
                        result["date_taken"] = dt.isoformat()
                    except (ValueError, TypeError):
                        pass

                # DateTime fallback
                if not result["date_taken"] and 306 in exif_data:
                    raw = exif_data[306]
                    try:
                        dt = datetime.strptime(str(raw), "%Y:%m:%d %H:%M:%S")
                        result["date_taken"] = dt.isoformat()
                    except (ValueError, TypeError):
                        pass

                # Camera
                if 271 in exif_data:
                    result["camera_make"] = str(exif_data[271]).strip()
                if 272 in exif_data:
                    result["camera_model"] = str(exif_data[272]).strip()
    except Exception as e:
        logger.debug(f"EXIF error for {file_path}: {e}")

    return result


# ---------------------------------------------------------------------------
# Thumbnail generation
# ---------------------------------------------------------------------------

def generate_thumbnail(file_path: str, photo_id: int) -> Optional[str]:
    """Generate a thumbnail and return its path relative to THUMB_DIR."""
    try:
        THUMB_DIR.mkdir(parents=True, exist_ok=True)
        thumb_name = f"{photo_id}.jpg"
        thumb_path = THUMB_DIR / thumb_name

        with Image.open(file_path) as img:
            img = img.convert("RGB")
            img.thumbnail(THUMB_SIZE, Image.LANCZOS)
            img.save(str(thumb_path), "JPEG", quality=82, optimize=True)

        return thumb_name
    except Exception as e:
        logger.warning(f"Thumbnail error for {file_path}: {e}")
        return None


# ---------------------------------------------------------------------------
# File hash
# ---------------------------------------------------------------------------

def file_hash_sha256(file_path: str, chunk_size: int = 65536) -> str:
    h = hashlib.sha256()
    with open(file_path, "rb") as f:
        while True:
            chunk = f.read(chunk_size)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


# ---------------------------------------------------------------------------
# CLIP integration
# ---------------------------------------------------------------------------

async def classify_image(file_path: str, categories: Optional[list[str]] = None) -> Optional[dict]:
    """Call the CLIP service to classify an image."""
    try:
        async with httpx.AsyncClient(timeout=60.0) as client:
            with open(file_path, "rb") as f:
                files = {"file": (os.path.basename(file_path), f, "application/octet-stream")}
                data = {}
                if categories:
                    data["categories"] = ",".join(categories)

                resp = await client.post(f"{CLIP_SERVICE_URL}/classify", files=files, data=data)
                if resp.status_code == 200:
                    return resp.json()
    except Exception as e:
        logger.debug(f"CLIP classify error for {file_path}: {e}")
    return None


# ---------------------------------------------------------------------------
# Indexing logic (runs in background)
# ---------------------------------------------------------------------------

_indexing_lock = asyncio.Lock()


async def run_indexing(task_id: int, folder_path: str, recursive: bool, use_ai: bool):
    """Index photos in a folder. Called as a background task."""
    async with _indexing_lock:
        conn = get_db()
        try:
            conn.execute("UPDATE indexing_tasks SET status='running', started_at=? WHERE id=?",
                         (datetime.now().isoformat(), task_id))
            conn.commit()

            # Collect files
            folder = Path(folder_path)
            logger.info(f"Indexing task {task_id}: folder_path={folder_path}, exists={folder.exists()}, is_dir={folder.is_dir() if folder.exists() else 'N/A'}, recursive={recursive}")

            if not folder.exists():
                error_msg = f"Folder not found: {folder_path}"
                logger.error(f"Indexing task {task_id}: {error_msg}")
                # Try to list parent to help diagnose
                parent = folder.parent
                if parent.exists():
                    try:
                        children = [e.name for e in parent.iterdir()]
                        logger.info(f"Indexing task {task_id}: parent '{parent}' contents: {children[:20]}")
                    except Exception as pe:
                        logger.info(f"Indexing task {task_id}: cannot list parent: {pe}")
                conn.execute("UPDATE indexing_tasks SET status='error', error=?, finished_at=? WHERE id=?",
                             (error_msg, datetime.now().isoformat(), task_id))
                conn.commit()
                return

            # List top-level entries for debugging
            try:
                top_entries = list(folder.iterdir())
                top_files = [e.name for e in top_entries if e.is_file()][:10]
                top_dirs = [e.name for e in top_entries if e.is_dir()][:10]
                logger.info(f"Indexing task {task_id}: top-level files ({len([e for e in top_entries if e.is_file()])}): {top_files}")
                logger.info(f"Indexing task {task_id}: top-level dirs ({len([e for e in top_entries if e.is_dir()])}): {top_dirs}")
            except Exception as le:
                logger.warning(f"Indexing task {task_id}: cannot list folder: {le}")

            files = []
            if recursive:
                for root, _, filenames in os.walk(folder):
                    for fn in filenames:
                        if Path(fn).suffix.lower() in PHOTO_EXTENSIONS:
                            files.append(os.path.join(root, fn))
            else:
                for fn in os.listdir(folder):
                    fp = os.path.join(folder, fn)
                    if os.path.isfile(fp) and Path(fn).suffix.lower() in PHOTO_EXTENSIONS:
                        files.append(fp)

            total = len(files)
            conn.execute("UPDATE indexing_tasks SET total_files=? WHERE id=?", (total, task_id))
            conn.commit()

            logger.info(f"Indexing task {task_id}: found {total} photo files in {folder_path} (extensions: {PHOTO_EXTENSIONS})")
            if total == 0:
                # Log all file extensions found for debugging
                try:
                    all_exts = set()
                    if recursive:
                        for root, _, filenames in os.walk(folder):
                            for fn in filenames:
                                all_exts.add(Path(fn).suffix.lower())
                    else:
                        for fn in os.listdir(folder):
                            if os.path.isfile(os.path.join(str(folder), fn)):
                                all_exts.add(Path(fn).suffix.lower())
                    logger.info(f"Indexing task {task_id}: all file extensions found: {all_exts}")
                except Exception:
                    pass

            processed = 0
            for fp in files:
                try:
                    # Skip already indexed
                    existing = conn.execute("SELECT id FROM photos WHERE file_path=?", (fp,)).fetchone()
                    if existing:
                        processed += 1
                        if processed % 50 == 0:
                            conn.execute("UPDATE indexing_tasks SET processed_files=? WHERE id=?", (processed, task_id))
                            conn.commit()
                        continue

                    stat = os.stat(fp)
                    exif = extract_exif(fp)

                    date_taken = exif["date_taken"]
                    year, month, day = None, None, None
                    if date_taken:
                        try:
                            dt = datetime.fromisoformat(date_taken)
                            year, month, day = dt.year, dt.month, dt.day
                        except (ValueError, TypeError):
                            pass

                    # Fallback to file modification time
                    if not date_taken:
                        try:
                            mtime = datetime.fromtimestamp(stat.st_mtime)
                            date_taken = mtime.isoformat()
                            year, month, day = mtime.year, mtime.month, mtime.day
                        except Exception:
                            pass

                    fhash = file_hash_sha256(fp)

                    cursor = conn.execute("""
                        INSERT INTO photos (file_path, file_name, file_size, file_hash,
                            date_taken, year, month, day, width, height,
                            camera_make, camera_model, thumb_path, indexed_at, source_folder)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, ?, ?)
                    """, (
                        fp, os.path.basename(fp), stat.st_size, fhash,
                        date_taken, year, month, day,
                        exif["width"], exif["height"],
                        exif["camera_make"], exif["camera_model"],
                        datetime.now().isoformat(), folder_path
                    ))
                    photo_id = cursor.lastrowid
                    conn.commit()

                    # Generate thumbnail
                    thumb = generate_thumbnail(fp, photo_id)
                    if thumb:
                        conn.execute("UPDATE photos SET thumb_path=? WHERE id=?", (thumb, photo_id))
                        conn.commit()

                    # AI classification
                    if use_ai:
                        try:
                            result = await classify_image(fp)
                            if result and "best" in result:
                                conn.execute("UPDATE photos SET ai_category=?, ai_confidence=? WHERE id=?",
                                             (result["best"], result.get("confidence", 0), photo_id))
                                conn.commit()
                        except Exception as e:
                            logger.debug(f"AI classification failed for {fp}: {e}")

                    # Face detection + embedding
                    try:
                        faces = detect_faces(fp)
                        if faces:
                            for face in faces:
                                (x, y, w, h) = face["bbox"]
                                emb = face["embedding"]
                                det_score = face.get("det_score", 0.0)

                                # Try to auto-assign to an existing person
                                person_id = upsert_person_by_face(conn, emb)

                                conn.execute(
                                    """
                                    INSERT INTO faces (photo_id, bbox_x, bbox_y, bbox_w, bbox_h, embedding, det_score, person_id, created_at)
                                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                                    """,
                                    (
                                        photo_id,
                                        x, y, w, h,
                                        emb.tobytes(),
                                        det_score,
                                        person_id,
                                        datetime.now().isoformat(),
                                    ),
                                )
                            conn.commit()
                    except Exception as fe:
                        logger.debug(f"Face detection failed for {fp}: {fe}")

            # Auto-generate albums
            try:
                generate_auto_albums(conn)
            except Exception as e:
                logger.error(f"Auto-album generation error: {e}")

            conn.execute("UPDATE indexing_tasks SET status='completed', finished_at=? WHERE id=?",
                         (datetime.now().isoformat(), task_id))
        except Exception as e:
            logger.exception(f"Indexing task {task_id} error: {e}")
            conn.execute("UPDATE indexing_tasks SET status='error', error=?, finished_at=? WHERE id=?",
                         (str(e), datetime.now().isoformat(), task_id))
        finally:
            conn.commit()
            conn.close()


# ---------------------------------------------------------------------------
# Automatic album generation
# ---------------------------------------------------------------------------

def generate_auto_albums(conn: sqlite3.Connection):
    """Generate automatic albums by year, month, AI category, and people."""
    now = datetime.now().isoformat()

    # --- Albums by year ---
    years = conn.execute("SELECT DISTINCT year FROM photos WHERE year IS NOT NULL ORDER BY year DESC").fetchall()
    for row in years:
        year = row["year"]
        album_name = f"{year}"
        existing = conn.execute("SELECT id FROM albums WHERE name=? AND album_type='auto_year'", (album_name,)).fetchone()
        if not existing:
            cursor = conn.execute("INSERT INTO albums (name, album_type, description, created_at) VALUES (?, 'auto_year', ?, ?)",
                                  (album_name, f"Photos from {year}", now))
            album_id = cursor.lastrowid
            conn.commit()

            # Add photos to album
            conn.execute(
                "INSERT INTO album_photos (album_id, photo_id) SELECT ?, id FROM photos WHERE year=?",
                (album_id, year)
            )
            conn.commit()
            logger.info(f"Auto-generated album for year {year}: {album_name}")

    # --- Albums by month ---
    if conn.execute("SELECT COUNT(*) FROM albums WHERE album_type='auto_month'").fetchone()[0] == 0:
        logger.info("Creating monthly albums for all available photos...")
        conn.execute("""
            INSERT INTO albums (name, album_type, description, created_at)
            SELECT strftime('%Y-%m', date_taken) as name,
                   'auto_month' as album_type,
                   'Photos from ' || strftime('%Y-%m', date_taken) as description,
                   ?
            FROM photos
            WHERE date_taken IS NOT NULL
            GROUP BY year, month
            ORDER BY year DESC, month DESC
        """, (now,))
        conn.commit()

    # --- Person albums (face recognition) ---
    persons = conn.execute("SELECT id, name FROM persons").fetchall()
    for person in persons:
        person_id = person["id"]
        person_name = person["name"]

        # Create album for the person if it doesn't exist
        existing = conn.execute("SELECT id FROM albums WHERE name=? AND album_type='auto_person'", (person_name,)).fetchone()
        if not existing:
            cursor = conn.execute(
                "INSERT INTO albums (name, album_type, description, created_at) VALUES (?, 'auto_person', ?, ?)",
                (person_name, f"Auto-generated album for {person_name}", now)
            )
            album_id = cursor.lastrowid
            conn.commit()
            logger.info(f"Auto-generated album for person '{person_name}' (ID: {person_id})")

        # Add or update photos in the person's album
        conn.execute("""
            INSERT INTO album_photos (album_id, photo_id)
            SELECT a.id, f.photo_id
            FROM albums a
            JOIN faces f ON f.photo_id = a.cover_photo_id
            WHERE a.name = ?
            ON CONFLICT(album_id, photo_id) DO NOTHING
        """, (person_name,))
        conn.commit()


# ---------------------------------------------------------------------------
# API
# ---------------------------------------------------------------------------

app = FastAPI()
app.mount("/static", StaticFiles(directory="static"), name="static")
templates = Jinja2Templates(directory="templates")

# Background tasks (for long-running operations)
@app.post("/api/indexing")
async def api_indexing(task: dict, background_tasks: BackgroundTasks):
    """Start a new indexing task in the background."""
    folder_path = task.get("folder_path")
    recursive = task.get("recursive", True)
    use_ai = task.get("use_ai", True)

    if not folder_path:
        return JSONResponse({"error": "folder_path is required"}, status_code=400)

    folder_path = str(Path(folder_path).resolve())
    task_id = int(time.time())  # Simple sequential task ID based on timestamp

    background_tasks.add_task(run_indexing, task_id, folder_path, recursive, use_ai)

    # Create a new task record in the database
    conn = get_db()
    conn.execute(
        "INSERT INTO indexing_tasks (id, folder_path, status, started_at) VALUES (?, ?, 'pending', ?)",
        (task_id, folder_path, datetime.now().isoformat())
    )
    conn.commit()
    conn.close()

    logger.info(f"Started indexing task {task_id} for folder: {folder_path}")
    return {"task_id": task_id, "folder_path": folder_path}


@app.get("/api/indexing/{task_id}")
async def api_indexing_status(task_id: int):
    """Get the status of an indexing task."""
    conn = get_db()
    task = conn.execute("SELECT * FROM indexing_tasks WHERE id=?", (task_id,)).fetchone()
    conn.close()
    if task:
        return dict(task)
    else:
        return JSONResponse({"error": "Task not found"}, status_code=404)


@app.get("/api/albums")
async def list_albums():
    """List all albums."""
    conn = get_db()
    rows = conn.execute("SELECT * FROM albums ORDER BY created_at DESC").fetchall()
    conn.close()
    return [dict(r) for r in rows]


@app.get("/api/albums/{album_id}")
async def get_album(album_id: int):
    """Get details of a specific album, including photos."""
    conn = get_db()

    # Album info
    album = conn.execute("SELECT * FROM albums WHERE id=?", (album_id,)).fetchone()
    if not album:
        conn.close()
        return JSONResponse({"error": "Album not found"}, status_code=404)

    # Photos in the album
    photos = conn.execute("""
        SELECT p.*, ap.sort_order
        FROM photos p
        JOIN album_photos ap ON ap.photo_id = p.id
        WHERE ap.album_id = ?
        ORDER BY ap.sort_order
    """, (album_id,)).fetchall()

    conn.close()
    album_dict = dict(album)
    album_dict["photos"] = [dict(photo) for photo in photos]
    return album_dict


@app.post("/api/albums")
async def create_album(album: dict, request: Request):
    """Create a new album."""
    body = await request.json()
    name = (body.get("name") or "").strip()

    if not name:
        return JSONResponse({"error": "name is required"}, status_code=400)

    conn = get_db()
    try:
        cursor = conn.execute(
            "INSERT INTO albums (name, created_at) VALUES (?, ?)",
            (name, datetime.now().isoformat())
        )
        album_id = cursor.lastrowid
        conn.commit()
        logger.info(f"Album created: {name} (ID: {album_id})")
        return {"id": album_id, "name": name}
    except Exception as e:
        logger.exception(f"Error creating album: {e}")
        return JSONResponse({"error": str(e)}, status_code=500)
    finally:
        conn.close()


# Serve photos and thumbnails
@app.get("/photo/{photo_id}")
async def serve_photo(photo_id: int):
    conn = get_db()
    photo = conn.execute("SELECT * FROM photos WHERE id=?", (photo_id,)).fetchone()
    conn.close()
    if not photo:
        return JSONResponse({"error": "Photo not found"}, status_code=404)

    file_path = photo["file_path"]
    return FileResponse(file_path)


@app.get("/thumb/{photo_id}")
async def serve_thumb(photo_id: int):
    conn = get_db()
    photo = conn.execute("SELECT thumb_path FROM photos WHERE id=?", (photo_id,)).fetchone()
    conn.close()
    if not photo or not photo["thumb_path"]:
        return JSONResponse({"error": "Thumbnail not found"}, status_code=404)

    thumb_path = THUMB_DIR / photo["thumb_path"]
    return FileResponse(str(thumb_path))


@app.get("/api/faces/unknown")
async def list_unknown_faces(limit: int = Query(48, ge=1, le=200)):
    """Return unknown faces (no person_id) with bbox and photo_id."""
    conn = get_db()
    rows = conn.execute(
        """
        SELECT f.id as face_id, f.photo_id, f.bbox_x, f.bbox_y, f.bbox_w, f.bbox_h, f.det_score,
               p.thumb_path, p.file_name
        FROM faces f
        JOIN photos p ON p.id=f.photo_id
        WHERE f.person_id IS NULL
        ORDER BY f.det_score DESC, f.id DESC
        LIMIT ?
        """,
        (limit,),
    ).fetchall()
    conn.close()
    return [dict(r) for r in rows]


@app.post("/api/faces/{face_id}/label")
async def label_face(face_id: int, request: Request):
    """Assign a name to a face. Creates/gets a person and links this face to it."""
    body = await request.json()
    name = (body.get("name") or "").strip()
    if not name:
        return JSONResponse({"error": "name is required"}, status_code=400)

    conn = get_db()
    try:
        person = conn.execute("SELECT id FROM persons WHERE name=?", (name,)).fetchone()
        if person:
            person_id = int(person["id"])
        else:
            cur = conn.execute(
                "INSERT INTO persons (name, created_at) VALUES (?, ?)",
                (name, datetime.now().isoformat()),
            )
            person_id = int(cur.lastrowid)

        updated = conn.execute(
            "UPDATE faces SET person_id=? WHERE id=?",
            (person_id, face_id),
        ).rowcount
        conn.commit()

        # Refresh albums so the person album appears immediately
        generate_auto_albums(conn)
        return {"ok": True, "updated": updated, "person_id": person_id, "name": name}
    finally:
        conn.close()


@app.get("/face/{face_id}.jpg")
async def face_crop(face_id: int, size: int = Query(160, ge=64, le=512)):
    from fastapi.responses import StreamingResponse

    conn = get_db()
    row = conn.execute(
        """
        SELECT f.bbox_x, f.bbox_y, f.bbox_w, f.bbox_h, p.file_path
        FROM faces f JOIN photos p ON p.id=f.photo_id
        WHERE f.id=?
        """,
        (face_id,),
    ).fetchone()
    conn.close()

    if not row:
        return JSONResponse({"error": "Face not found"}, status_code=404)

    fp = row["file_path"]
    if not fp or not os.path.exists(fp):
        return JSONResponse({"error": "Photo not found"}, status_code=404)

    try:
        with Image.open(fp) as img:
            img = img.convert("RGB")
            x, y, w, h = int(row["bbox_x"]), int(row["bbox_y"]), int(row["bbox_w"]), int(row["bbox_h"])

            # clamp bbox
            x = max(0, x)
            y = max(0, y)
            x2 = min(img.width, x + max(1, w))
            y2 = min(img.height, y + max(1, h))

            crop = img.crop((x, y, x2, y2))
            crop.thumbnail((size, size), Image.LANCZOS)

            buf = io.BytesIO()
            crop.save(buf, format="JPEG", quality=85)
            buf.seek(0)
            return StreamingResponse(buf, media_type="image/jpeg")
    except Exception as e:
        return JSONResponse({"error": str(e)}, status_code=500)


@app.get("/faces", response_class=HTMLResponse)
async def page_faces(request: Request, limit: int = 48):
    conn = get_db()
    faces = conn.execute(
        """
        SELECT f.id as face_id, f.photo_id, f.det_score, p.file_name
        FROM faces f
        JOIN photos p ON p.id=f.photo_id
        WHERE f.person_id IS NULL
        ORDER BY f.det_score DESC, f.id DESC
        LIMIT ?
        """,
        (limit,),
    ).fetchall()
    conn.close()

    return templates.TemplateResponse("faces_unknown.html", {
        "request": request,
        "faces": [dict(r) for r in faces],
        "active_page": "faces",
    })


@app.get("/", response_class=HTMLResponse)
async def index(request: Request):
    conn = get_db()

    # Total photos and albums count
    total, albums_count = 0, 0
    try:
        total = conn.execute("SELECT COUNT(*) FROM photos").fetchone()[0]
        albums_count = conn.execute("SELECT COUNT(*) FROM albums").fetchone()[0]
    except Exception as e:
        logger.error(f"Error fetching total photos or albums count: {e}")

    # Recent photos
    recent = conn.execute("""
        SELECT id, file_name, date_taken, thumb_path
        FROM photos
        ORDER BY date_taken DESC
        LIMIT 12
    """).fetchall()

    # Years with photos
    years = conn.execute("SELECT DISTINCT year FROM photos WHERE year IS NOT NULL ORDER BY year DESC").fetchall()

    conn.close()
    return templates.TemplateResponse("index.html", {
        "request": request,
        "total": total,
        "albums_count": albums_count,
        "recent_photos": [dict(r) for r in recent],
        "years": [r["year"] for r in years],
        "active_page": "home",
    })


@app.get("/timeline", response_class=HTMLResponse)
async def timeline_page(request: Request, year: int = None, month: int = None, page: int = 1):
    """Timeline page: photos organized by time (year/month)."""
    conn = get_db()

    # Year and month filters
    year_filter = "year IS NOT NULL"
    if year:
        year = int(year)
        year_filter = f"year = {year}"
    month_filter = "month IS NOT NULL"
    if month:
        month = int(month)
        month_filter = f"month = {month}"

    # Paginated photos for the selected year/month
    photos = conn.execute(f"""
        SELECT id, file_name, date_taken, thumb_path, year, month
        FROM photos
        WHERE {year_filter} AND {month_filter}
        ORDER BY year DESC, month DESC, date_taken DESC
        LIMIT 48 OFFSET ?
    """, ((page - 1) * 48,)).fetchall()

    # Count photos for pagination
    total_photos = conn.execute(f"""
        SELECT COUNT(*) FROM photos WHERE {year_filter} AND {month_filter}
    """).fetchone()[0]
    total_pages = (total_photos + 47) // 48

    # Available years and months for the filter sidebar
    years = conn.execute("SELECT DISTINCT year FROM photos WHERE year IS NOT NULL ORDER BY year DESC").fetchall()
    months_data = conn.execute("SELECT DISTINCT month FROM photos WHERE month IS NOT NULL ORDER BY month DESC").fetchall()

    conn.close()

    # Month names in Russian
    month_names_ru = {
        1: "Январь", 2: "Февраль", 3: "Март",
        4: "Апрель", 5: "Май", 6: "Июнь",
        7: "Июль", 8: "Август", 9: "Сентябрь",
        10: "Октябрь", 11: "Ноябрь", 12: "Декабрь"
    }

    return templates.TemplateResponse("timeline.html", {
        "request": request,
        "photos": [dict(p) for p in photos],
        "total": total_photos,
        "page": page,
        "pages": total_pages,
        "year": year,
        "month": month,
        "years": [r["year"] for r in years],
        "months": [{"num": r["month"], "name": month_names_ru.get(r["month"], str(r["month"]))} for r in months_data],
        "month_names": month_names_ru,
        "active_page": "timeline",
    })


@app.get("/albums", response_class=HTMLResponse)
async def albums_page(request: Request):
    """Albums page."""
    conn = get_db()
    albums = conn.execute("SELECT * FROM albums ORDER BY created_at DESC").fetchall()
    conn.close()
    return templates.TemplateResponse("albums.html", {
        "request": request,
        "albums": [dict(a) for a in albums],
        "active_page": "albums",
    })


@app.get("/search", response_class=HTMLResponse)
async def search_page(request: Request, query: str = "", page: int = 1):
    """Search page: find photos by filename or EXIF data."""
    conn = get_db()

    # Simple search by query string
    photos = conn.execute(
        """
        SELECT id, file_name, date_taken, thumb_path
        FROM photos
        WHERE file_name LIKE ?
        ORDER BY date_taken DESC
        LIMIT 48 OFFSET ?
        """,
        (f"%{query}%", (page - 1) * 48),
    ).fetchall()

    # Count total results for pagination
    total_results = conn.execute(
        """
        SELECT COUNT(*) FROM photos
        WHERE file_name LIKE ?
        """,
        (f"%{query}%",),
    ).fetchone()[0]
    total_pages = (total_results + 47) // 48

    conn.close()
    return templates.TemplateResponse("search.html", {
        "request": request,
        "query": query,
        "photos": [dict(p) for p in photos],
        "total": total_results,
        "page": page,
        "pages": total_pages,
        "active_page": "search",
    })

# Compatibility API endpoints for .NET client
@app.get("/api/health")
async def api_health():
    return {"status": "ok"}


@app.post("/api/index")
async def api_index(task: dict, background_tasks: BackgroundTasks):
    """Compatibility wrapper for starting indexing. Accepts JSON { "folder": "...", "recursive": true, "use_ai": false }"""
    folder = task.get("folder") or task.get("folder_path")
    recursive = task.get("recursive", True)
    use_ai = task.get("use_ai", False)

    if not folder:
        return JSONResponse({"error": "folder is required"}, status_code=400)

    folder_path = str(Path(folder).resolve())
    task_id = int(time.time())

    background_tasks.add_task(run_indexing, task_id, folder_path, recursive, use_ai)

    conn = get_db()
    try:
        conn.execute(
            "INSERT INTO indexing_tasks (id, folder_path, status, started_at) VALUES (?, ?, 'pending', ?)",
            (task_id, folder_path, datetime.now().isoformat()),
        )
        conn.commit()
    finally:
        conn.close()

    logger.info(f"Started indexing task {task_id} (compat) for folder: {folder_path}")
    return {"task_id": task_id, "status": "pending"}


@app.get("/api/index/status/{task_id}")
async def api_index_status_compat(task_id: int):
    conn = get_db()
    task = conn.execute("SELECT * FROM indexing_tasks WHERE id=?", (task_id,)).fetchone()
    conn.close()
    if task:
        return dict(task)
    else:
        return JSONResponse({"error": "Task not found"}, status_code=404)


@app.get("/api/stats")
async def api_stats():
    conn = get_db()
    try:
        total_photos = 0
        total_albums = 0
        years = []
        categories = []
        try:
            total_photos = conn.execute("SELECT COUNT(*) FROM photos").fetchone()[0]
            total_albums = conn.execute("SELECT COUNT(*) FROM albums").fetchone()[0]
            years_rows = conn.execute("SELECT DISTINCT year FROM photos WHERE year IS NOT NULL ORDER BY year DESC").fetchall()
            years = [int(r[0]) for r in years_rows]
            # categories: ai_category counts
            cat_rows = conn.execute("SELECT ai_category as name, COUNT(*) as count FROM photos WHERE ai_category IS NOT NULL GROUP BY ai_category").fetchall()
            categories = [{"name": r[0], "count": r[1]} for r in cat_rows]
        except Exception as e:
            logger.debug(f"Error computing stats: {e}")
        return {"total_photos": total_photos, "total_albums": total_albums, "years": years, "categories": categories}
    finally:
        conn.close()


@app.get("/api/debug/files")
async def api_debug_files(folder: str = Query("/photos")):
    """Return debug info about a folder: existence, dirs, files (name/size/ext), total_entries, errors."""
    errors = []
    try:
        p = Path(folder)
        exists = p.exists()
        is_dir = p.is_dir() if exists else False
        files = []
        dirs = []
        total = 0

        if exists and is_dir:
            try:
                for entry in p.iterdir():
                    total += 1
                    if entry.is_dir():
                        dirs.append(entry.name)
                    elif entry.is_file():
                        try:
                            files.append({
                                "name": entry.name,
                                "size": entry.stat().st_size,
                                "ext": entry.suffix
                            })
                        except Exception as fe:
                            errors.append(f"Stat error for {entry}: {fe}")
            except Exception as le:
                errors.append(f"Cannot list folder: {le}")
        else:
            # folder may not exist; try to report parent details
            if not exists:
                errors.append(f"Folder not found: {folder}")

        return {
            "folder": folder,
            "exists": exists,
            "is_dir": is_dir,
            "files": files,
            "dirs": dirs,
            "total_entries": total,
            "errors": errors
        }
    except Exception as e:
        return JSONResponse({"error": str(e)}, status_code=500)
