# Этап 1 — Доменная модель и хранилище

## Что сделано
- Добавлен базовый каталог метаданных для лиц, тегов и альбомов на SQLite.
- Добавлены доменные сущности:
  - `FacePerson`
  - `PhotoAsset`
  - `DetectedFace`
  - `FaceEmbedding`
  - `Tag` / `TagKind`
  - `PhotoTag`
  - `PhotoAlbum`
  - `AlbumPhoto`
- Добавлен `FaceCatalogDbContext` с индексами и связями.
- Добавлен `FaceCatalogDatabase` для инициализации файла БД в `%AppData%/PhotoSorter/face-catalog.db`.
- Добавлен `FaceCatalogService` для базовых операций:
  - upsert фото
  - создание персоны
  - сохранение детекции лица и embedding
  - подтверждение лица пользователем
  - добавление тега к фото
  - выборка неизвестных лиц
- Инициализация БД подключена в `App.OnStartup()`.
- Добавлена фабрика `CreateFaceCatalogService` в `ServiceLocator`.

## Технические изменения
- В `PhotoSorterApp` добавлены пакеты:
  - `Microsoft.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore.Sqlite`

## Модульные тесты
Добавлены тесты `FaceCatalogDbContextTests`:
1. `PhotoAsset_FilePath_MustBeUnique`
   - проверка уникального индекса на `PhotoAsset.FilePath`.
2. `PhotoTag_ManyToMany_Link_IsPersisted`
   - проверка сохранения связи many-to-many между фото и тегом.

## Результаты тестов
- Команда: `dotnet test PhotoSorterApp.Tests/PhotoSorterApp.Tests.csproj --nologo`
- Результат: **ошибок 0**, все тесты прошли успешно (в т.ч. новые тесты).
- В проекте присутствуют предупреждения анализатора NUnit, без падения тестов.

## Ограничения текущего этапа
- Пока без миграций EF (`EnsureCreated`), для MVP-старта.
- Механизм кластеризации/распознавания лиц и UI подтверждения пользователя — в следующем этапе.
