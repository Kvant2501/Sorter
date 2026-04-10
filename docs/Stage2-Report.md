# Этап 2 — Пайплайн детекции лиц и embedding

## Что сделано
- Добавлена модель результата анализа `FaceAnalysisResult` / `FaceDetectionResult`.
- Добавлен контракт клиента распознавания `IFaceRecognitionClient`.
- Добавлена реализация `LocalFaceRecognitionClient` для вызова локального API:
  - `POST /analyze`
  - чтение `model`, `faces[]`, `embedding`, `tags`.
  - URL по умолчанию: `http://localhost:5272` (можно переопределить через `PHOTOSORTER_FACE_API_URL`).
- Добавлен контракт каталога `IFaceCatalogService`.
- `FaceCatalogService` переведен на `IFaceCatalogService`.
- Реализован `FaceIndexingPipelineService`:
  - проход по фото в папке (учитываются поддерживаемые расширения фото)
  - чтение EXIF-даты
  - вычисление SHA256
  - upsert фото в каталоге
  - вызов распознавания
  - фильтрация лиц по порогу `MinConfidence`
  - сохранение embeddings и системных тегов в БД
  - сбор статистики (`ProcessedFiles`, `IndexedPhotos`, `SavedFaces`, `Errors`)
- В `ServiceLocator` добавлены фабрики:
  - `CreateFaceRecognitionClient`
  - `CreateFaceIndexingPipelineService`

## Продолжение этапа 2
- Добавлен batch-контракт в `IFaceRecognitionClient`: `AnalyzeBatchAsync(...)`.
- `LocalFaceRecognitionClient` теперь поддерживает:
  - `POST /analyze-batch`
  - fallback на `POST /analyze`, если batch endpoint недоступен.
- `FaceIndexingPipelineService` обновлен для пакетной обработки:
  - новый параметр `FaceIndexingOptions.BatchSize` (по умолчанию `8`)
  - обработка идет батчами для ускорения интеграции с API.

## Docker MVP для локального AI-сервиса
Добавлены/обновлены файлы:
- `ai-face-service/app.py`
- `ai-face-service/requirements.txt`
- `ai-face-service/Dockerfile`
- `docker-compose.face.yml`

Текущая версия сервиса теперь не заглушка, а локальный детектор на OpenCV (Haar Cascade):
- endpoint `GET /health`
- endpoint `POST /analyze`
- endpoint `POST /analyze-batch`
- для каждого лица формируется детекция + embedding (128 float) + теги (`face`, `portrait/group`).

## Модульные тесты
Добавлены:
1. `LocalFaceRecognitionClientTests`
   - проверка сериализации запроса `imagePath`
   - проверка парсинга ответа API (model, faces, embedding, tags)
   - проверка fallback batch -> single calls
2. `FaceIndexingPipelineServiceTests`
   - проверка полного прохода пайплайна на тестовом фото
   - проверка сохранения детекции и тегов через fake-каталог

## Результаты тестов
- Команда: `dotnet test PhotoSorterApp.Tests/PhotoSorterApp.Tests.csproj --nologo`
- Результат: **ошибок 0**, тесты проходят успешно.
- Есть предупреждения анализатора NUnit (стилевые), без влияния на корректность.

## Следующий шаг
- Добавить UI-поток подтверждения личности пользователем (этап 3).
- Ввести очередь "Неизвестные лица" и массовое присвоение имени.
- Подключить улучшенную модель распознавания (InsightFace) как альтернативный backend.
