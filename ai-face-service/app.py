from __future__ import annotations

import base64
from pathlib import Path
from typing import List, Optional

import cv2
import numpy as np
from fastapi import FastAPI
from pydantic import BaseModel

app = FastAPI(title="PhotoSorter Face API", version="0.8.0")

# insightface primary detector
_insight_app = None
try:
    import insightface
    _insight_app = insightface.app.FaceAnalysis(
        name="buffalo_sc",
        providers=["CPUExecutionProvider"],
    )
    _insight_app.prepare(ctx_id=-1, det_size=(640, 640), det_thresh=0.35)
except Exception as _e:
    _insight_app = None

# SSD fallback
_MODEL_DIR = Path("/app/models")
_face_net = None
_PROTO = _MODEL_DIR / "deploy.prototxt"
_SSD_WEIGHTS = _MODEL_DIR / "res10_300x300_ssd_iter_140000.caffemodel"
if _insight_app is None and _PROTO.exists() and _SSD_WEIGHTS.exists():
    _face_net = cv2.dnn.readNetFromCaffe(str(_PROTO), str(_SSD_WEIGHTS))

_cascade_frontal = cv2.CascadeClassifier(cv2.data.haarcascades + "haarcascade_frontalface_default.xml")


class AnalyzeRequest(BaseModel):
    imagePath: Optional[str] = None
    imageBase64: Optional[str] = None


class AnalyzeBatchRequest(BaseModel):
    imagePaths: Optional[List[str]] = None
    items: Optional[List[AnalyzeRequest]] = None


@app.get("/health")
def health() -> dict:
    if _insight_app is not None:
        model = "insightface-buffalo_sc"
    elif _face_net is not None:
        model = "opencv-dnn-res10"
    else:
        model = "opencv-haar-fallback"
    return {"status": "ok", "model": model}


@app.post("/analyze")
def analyze(req: AnalyzeRequest) -> dict:
    return _analyze_image(_resolve_image(req))


@app.post("/analyze-batch")
def analyze_batch(req: AnalyzeBatchRequest) -> dict:
    items = []
    if req.items:
        for item in req.items:
            items.append({"imagePath": item.imagePath or "", "analysis": _analyze_image(_resolve_image(item))})
        return {"items": items}
    for image_path in req.imagePaths or []:
        image = _resolve_image(AnalyzeRequest(imagePath=image_path))
        items.append({"imagePath": image_path, "analysis": _analyze_image(image)})
    return {"items": items}


def _resolve_image(req: AnalyzeRequest):
    if req.imageBase64:
        return _load_image_from_base64(req.imageBase64)
    if req.imagePath:
        return _load_image_from_path(req.imagePath)
    return None


def _load_image_from_path(image_path: str):
    path = Path(image_path)
    if not path.exists():
        return None
    return cv2.imread(str(path))


def _load_image_from_base64(payload: str):
    try:
        raw = base64.b64decode(payload)
        data = np.frombuffer(raw, dtype=np.uint8)
        return cv2.imdecode(data, cv2.IMREAD_COLOR)
    except Exception:
        return None


def _analyze_image(image) -> dict:
    if image is None:
        return {"model": "none", "faces": []}
    if _insight_app is not None:
        return _analyze_insightface(image)
    if _face_net is not None:
        return _analyze_ssd(image)
    return _analyze_haar(image)


def _analyze_insightface(image: np.ndarray) -> dict:
    h, w = image.shape[:2]
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)

    try:
        detected = _insight_app.get(rgb)
    except Exception:
        detected = []

    faces = []
    total = len(detected)
    for face in detected:
        bbox = face.bbox.astype(int)
        x1, y1, x2, y2 = bbox[0], bbox[1], bbox[2], bbox[3]
        bw = x2 - x1
        bh = y2 - y1
        conf = float(face.det_score) if hasattr(face, "det_score") else 0.75

        x1, y1, bw, bh = _fit_bbox(x1, y1, bw, bh, w, h)
        if bw < 16 or bh < 16:
            continue

        # Use real 512-dim InsightFace embedding for accurate clustering
        if face.embedding is not None and len(face.embedding) > 0:
            emb_vec = face.embedding.astype(np.float32)
            norm = np.linalg.norm(emb_vec)
            if norm > 0:
                emb_vec = emb_vec / norm
            embedding = emb_vec.tolist()
        else:
            crop = gray[y1 : y1 + bh, x1 : x1 + bw]
            embedding = _make_embedding(crop)

        faces.append({
            "x": float(x1),
            "y": float(y1),
            "width": float(bw),
            "height": float(bh),
            "confidence": min(conf, 0.97),
            "embedding": embedding,
        })

    return {"model": "insightface-buffalo_sc", "faces": faces}


def _analyze_ssd(image: np.ndarray) -> dict:
    h, w = image.shape[:2]
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    blob = cv2.dnn.blobFromImage(cv2.resize(image, (300, 300)), 1.0, (300, 300), (104.0, 177.0, 123.0))
    _face_net.setInput(blob)
    out = _face_net.forward()

    faces = []
    total = int((out[0, 0, :, 2] >= 0.5).sum())
    for i in range(out.shape[2]):
        conf = float(out[0, 0, i, 2])
        if conf < 0.5:
            continue
        box = out[0, 0, i, 3:7] * np.array([w, h, w, h])
        x1, y1, x2, y2 = box.astype("int")
        bw, bh = x2 - x1, y2 - y1
        if bw < 18 or bh < 18:
            continue
        x1, y1, bw, bh = _fit_bbox(x1, y1, bw, bh, w, h)
        crop = gray[y1 : y1 + bh, x1 : x1 + bw]
        faces.append({"x": float(x1), "y": float(y1), "width": float(bw), "height": float(bh),
                      "confidence": float(conf), "embedding": _make_embedding(crop)})
    return {"model": "opencv-dnn-res10", "faces": faces}


def _analyze_haar(image: np.ndarray) -> dict:
    h, w = image.shape[:2]
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    rects = _cascade_frontal.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=5, minSize=(30, 30))
    faces = []
    total = len(rects)
    for (x, y, bw, bh) in rects:
        x, y, bw, bh = _fit_bbox(int(x), int(y), int(bw), int(bh), w, h)
        crop = gray[y : y + bh, x : x + bw]
        faces.append({"x": float(x), "y": float(y), "width": float(bw), "height": float(bh),
                      "confidence": 0.65, "embedding": _make_embedding(crop)})
    return {"model": "opencv-haar-fallback", "faces": faces}


def _fit_bbox(x: int, y: int, w: int, h: int, width: int, height: int):
    x = max(0, min(x, width - 1))
    y = max(0, min(y, height - 1))
    w = max(1, min(w, width - x))
    h = max(1, min(h, height - y))
    return x, y, w, h


def _make_embedding(face_gray: np.ndarray) -> list[float]:
    if face_gray.size == 0:
        return []
    resized = cv2.resize(face_gray, (16, 8), interpolation=cv2.INTER_AREA)
    return (resized.astype(np.float32).reshape(-1) / 255.0).tolist()
