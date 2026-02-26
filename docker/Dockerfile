FROM python:3.11-slim

WORKDIR /app

# Install system dependencies for Pillow and curl for healthcheck
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libjpeg62-turbo-dev libpng-dev libwebp-dev curl && \
    rm -rf /var/lib/apt/lists/*

COPY requirements.txt .

# Install PyTorch CPU from the official index, then the rest
RUN pip install --no-cache-dir \
        torch==2.5.1+cpu torchvision==0.20.1+cpu \
        --index-url https://download.pytorch.org/whl/cpu && \
    pip install --no-cache-dir -r requirements.txt

COPY app.py .

EXPOSE 8000

CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000"]
