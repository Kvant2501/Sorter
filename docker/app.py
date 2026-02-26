# -*- coding: utf-8 -*-
"""
CLIP-based image classification microservice.
Accepts an image and a list of candidate labels, returns the best-matching label with confidence.
"""

import io
import logging
from contextlib import asynccontextmanager

import open_clip
import torch
from fastapi import FastAPI, File, Form, UploadFile
from PIL import Image

logger = logging.getLogger("clip-service")
logging.basicConfig(level=logging.INFO)

# Global model state
model = None
preprocess = None
tokenizer = None
device = "cpu"

# Default categories with CLIP-optimized prompt templates
DEFAULT_CATEGORIES = [
    "люди и портреты",
    "природа и пейзажи",
    "животные",
    "еда и напитки",
    "архитектура и здания",
    "транспорт",
    "документы и текст",
    "спорт и активный отдых",
    "интерьер и предметы",
    "ночное небо и астрономия",
]

# Prompt template that improves CLIP accuracy
PROMPT_TEMPLATE = "a photo of {}"


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Load CLIP model once at startup."""
    global model, preprocess, tokenizer, device

    model_name = "ViT-L-14"
    pretrained = "openai"

    logger.info(f"Loading CLIP model ({model_name} / {pretrained}) ...")
    model, _, preprocess = open_clip.create_model_and_transforms(
        model_name, pretrained=pretrained, device=device
    )
    tokenizer = open_clip.get_tokenizer(model_name)
    model.eval()
    logger.info(f"CLIP model {model_name} loaded successfully.")
    yield


app = FastAPI(title="CLIP Image Classifier", version="1.1.0", lifespan=lifespan)


@app.get("/health")
async def health():
    return {"status": "ok", "model_loaded": model is not None}


@app.post("/classify")
async def classify(
    file: UploadFile = File(...),
    categories: str = Form(default=""),
    top_k: int = Form(default=3),
):
    """
    Classify an uploaded image against a set of text categories.

    - **file**: image file (JPEG/PNG/etc.)
    - **categories**: comma-separated list of category labels. If empty, uses default set.
    - **top_k**: number of top results to return.
    """
    # Parse categories
    if categories.strip():
        labels = [c.strip() for c in categories.split(",") if c.strip()]
    else:
        labels = DEFAULT_CATEGORIES

    # Build prompted labels for better CLIP accuracy
    prompted_labels = [PROMPT_TEMPLATE.format(label) for label in labels]

    # Read and preprocess image
    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
    image_tensor = preprocess(image).unsqueeze(0).to(device)

    # Tokenize prompted labels
    text_tokens = tokenizer(prompted_labels).to(device)

    # Compute similarities
    with torch.no_grad():
        image_features = model.encode_image(image_tensor)
        text_features = model.encode_text(text_tokens)

        image_features /= image_features.norm(dim=-1, keepdim=True)
        text_features /= text_features.norm(dim=-1, keepdim=True)

        similarity = (100.0 * image_features @ text_features.T).squeeze(0)
        probs = similarity.softmax(dim=-1)

    # Build results sorted by confidence (return original labels, not prompted)
    results = []
    for idx in probs.argsort(descending=True)[:top_k]:
        results.append(
            {"category": labels[idx], "confidence": round(probs[idx].item(), 4)}
        )

    return {
        "best": results[0]["category"],
        "confidence": results[0]["confidence"],
        "results": results,
    }
