# Этап 3 — Подтверждение личности пользователем

## Что сделано
- Добавлена вкладка `Лица` в `MainWindow`:
  - выбор папки с фото,
  - запуск индексации лиц,
  - запуск подтверждения неизвестных лиц.
- В `MainViewModel` добавлены параметры:
  - `FaceIndexFolder`
  - `FaceIndexRecursive`
  - `FaceMinConfidence`
  - `FaceBatchSize`
- В `MainWindow.xaml.cs` реализованы обработчики:
  - `SelectFaceIndexFolder_Click`
  - `StartFaceIndexing_Click`
  - `LabelUnknownFaces_Click`
- Добавлен сервис `FaceLabelingService`:
  - `EnsurePersonAsync` — находит существующую персону (без учета регистра) или создает новую,
  - `AssignFacesToPersonAsync` — подтверждает лицо и добавляет person-tag к фото.
- Расширен контракт `IFaceCatalogService` методом `GetPersonsAsync`.
- В `FaceCatalogService` добавлена реализация `GetPersonsAsync`.
- В `ServiceLocator` добавлена фабрика `CreateFaceLabelingService`.

## Продолжение этапа 3 (автокластеры)
- Добавлен `FaceClusteringService` с кластеризацией unknown лиц по embedding (cosine distance + union-find).
- `GetUnknownFacesAsync` теперь загружает `FaceEmbedding` для кластеризации.
- `UnknownFacesWindow` теперь:
  - показывает `Cluster` для каждого лица,
  - сортирует список по кластеру,
  - при назначении имени автоматически применяет имя ко всем лицам выбранных кластеров.
- В `ServiceLocator` добавлена фабрика `CreateFaceClusteringService`.

## Продолжение этапа 3 (управление персонами)
- Добавлены операции управления персонами на уровне каталога:
  - `RenamePersonAsync`
  - `MergePersonsAsync`
- Добавлен `FacePersonManagementService` (доменная логика rename/merge по имени).
- Добавлено окно `PersonsWindow`:
  - список персон,
  - переименование выбранной персоны,
  - объединение выбранной персоны в другую (по имени, с созданием при необходимости).
- На вкладке `Лица` добавлена кнопка `Персоны` для открытия окна управления.

## UX-поток подтверждения лица
- Пользователь нажимает `Подтвердить неизвестные лица`.
- Приложение загружает очередь `unknown` из БД.
- Открывается галерея/список неизвестных лиц.
- Пользователь выбирает нужные лица и вводит имя один раз для выбранных.
- Имя сохраняется как персона, выбранные лица связываются с персоной, фото получает person-tag.

## Модульные тесты
Добавлены тесты `FaceLabelingServiceTests`:
1. `EnsurePersonAsync_ReusesExistingPerson_CaseInsensitive`
   - проверяет повторное использование существующей персоны.
2. `AssignFacesToPersonAsync_AssignsAndAddsPersonTags`
   - проверяет назначение лиц и добавление тегов персон.

Также обновлены test doubles в `FaceIndexingPipelineServiceTests` под новый метод интерфейса.

Добавлены `FaceClusteringServiceTests`:
1. `BuildClusters_GroupsNearbyEmbeddings`
2. `BuildClusters_AssignsStandaloneCluster_WhenEmbeddingMissing`

Добавлены `FacePersonManagementServiceTests`:
1. `MergePersonIntoNameAsync_CreatesTarget_WhenMissing`
2. `RenamePersonAsync_DelegatesToCatalog`

## Результаты тестов
- Команда: `dotnet test PhotoSorterApp.Tests/PhotoSorterApp.Tests.csproj --nologo`
- Итог: **ошибок 0**, все тесты проходят.
- Остались только стилевые предупреждения NUnit анализатора.

## Ограничения текущей итерации
- Нет автокластеризации по эмбеддингам (группы лиц формируются вручную выбором пользователя).
- Нет ручного объединения/разделения уже существующих персон.

## Следующий шаг
- Добавить автокластеры неизвестных лиц (DBSCAN/HDBSCAN) и массовое присвоение по кластеру.
- Добавить редактирование персон: merge/split/rename.
- Добавить веб-представление альбомов и фильтрацию по персонам/тегам.
