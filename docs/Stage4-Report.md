# Этап 4 — Локальный веб-сервер и фотоальбомы (финал)

## Что реализовано
- Добавлен отдельный проект локального сервера `PhotoSorterApp.Web` (.NET 8, ASP.NET Core Minimal API).
- Реализованы API-эндпоинты:
  - `GET /api/health`
  - `GET /api/persons`
  - `GET /api/tags`
  - `GET /api/albums`
  - `GET /api/photos?person=&tag=&skip=&take=`
  - `GET /api/random-selection?person=&tag=&count=`
  - `GET /api/image?path=`
- Сервер читает текущую SQLite-базу `%AppData%/PhotoSorter/face-catalog.db`.
- Добавлена современная веб-страница `wwwroot/index.html`:
  - карточки фотографий,
  - фильтры по персонам и тегам,
  - lazy-loading изображений,
  - кнопка случайной подборки.
- В WPF (`MainWindow`, вкладка `Лица`) добавлена кнопка `Веб-альбомы`:
  - запускает локальный сервер,
  - открывает страницу `http://localhost:5288` в браузере.

## Архитектура
- `PhotoSorterApp` продолжает быть desktop-ядром (сортировка, распознавание, разметка).
- `PhotoSorterApp.Web` — локальный read-only web слой для галереи и API.
- Доступ к данным выполнен через `GalleryRepository` (SQLite запросы).

## Модульные тесты
Добавлены тесты:
- `GalleryRepositoryTests.GetPhotos_AppliesPersonFilter`
  - проверка фильтрации фото по персоне.

Ранее добавленные тесты этапов 1-3 остаются актуальны.

## Результаты проверки
- `dotnet build` (workspace) — успешно.
- `dotnet build PhotoSorterApp.Web/PhotoSorterApp.Web.csproj` —Successfully.
- `dotnet test PhotoSorterApp.Tests/PhotoSorterApp.Tests.csproj --nologo` — успешно (0 падений).

## Как запустить веб-альбомы
1. Из приложения: вкладка `Лица` ? `Веб-альбомы`.
2. Или вручную:
   - `dotnet run --project PhotoSorterApp.Web/PhotoSorterApp.Web.csproj --urls http://localhost:5288`
   - открыть `http://localhost:5288`.

## Ограничения текущей версии
- Веб-страница пока без редактирования данных (read-only).
- Альбомы в API отдаются из БД, но расширенный UI управления альбомами будет на следующем шаге.
- У `api/image` пока прямой доступ к пути файла (локальный сценарий), для production нужен дополнительный слой безопасности.

## Face Detection Engine — итоговые результаты

### Эволюция детектора (папка F:\2017\Шарголь, 141 фото)

| Модель | Найдено лиц | Мусор |
|---|---|---|
| OpenCV Haar | ~300 | ~100% |
| OpenCV SSD Res10 | 69 | ~9% |
| YOLOv8n (person crop) | 227 | ~39% |
| InsightFace buffalo_sc (thresh=0.5) | 157 | ~2.5% |
| **InsightFace buffalo_sc (thresh=0.35)** | **173** | **~8%** |

### Финальный стек (ai-face-service)

- **Основной детектор:** `insightface` `buffalo_sc`, `det_size=(640,640)`, `det_thresh=0.35`, CPU.
- **Fallback:** OpenCV SSD Res10 ? Haar.
- **Зависимости:** `insightface==0.7.3`, `onnxruntime==1.20.1`, `opencv-python-headless`.

### Настройки индексации (.NET)

- `FaceMinConfidence = 0.45`
- `FaceBatchSize = 1`
