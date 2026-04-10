#nullable enable

using System;

namespace PhotoSorterApp.Services;

/// <summary>
/// Локатор служб — предоставляет фабрики для создания сервисов приложения.
/// Позволяет подменять реализации (например, в тестах) через установку делегатов.
/// </summary>
public static class ServiceLocator
{
    /// <summary>
    /// Фабрика для создания сервиса поиска дубликатов.
    /// По умолчанию создаёт экземпляр <see cref="DuplicateDetectionService"/>.
    /// Можно заменить на мок в тестах.
    /// </summary>
    public static Func<DuplicateDetectionService> CreateDuplicateDetectionService { get; set; } = () => new DuplicateDetectionService();

    /// <summary>
    /// Фабрика для создания сервиса сортировки фото.
    /// По умолчанию создаёт экземпляр <see cref="PhotoSortingService"/>.
    /// </summary>
    public static Func<Action<string>?, PhotoSortingService> CreatePhotoSortingService { get; set; } = (logger) => new PhotoSortingService(logger);

    /// <summary>
    /// Фабрика для создания сервиса сортировки документов.
    /// </summary>
    public static Func<Action<string>?, DocumentSortingService> CreateDocumentSortingService { get; set; } = (logger) => new DocumentSortingService(logger);

    /// <summary>
    /// Фабрика для создания сервиса каталогизации внешности для лиц, тегов и альбомов.
    /// </summary>
    public static Func<FaceCatalogService> CreateFaceCatalogService { get; set; } = () => new FaceCatalogService();
    public static IFaceCatalogService GetFaceCatalogService() => CreateFaceCatalogService();

    /// <summary>
    /// Фабрика для создания клиента распознавания лиц.
    /// Используется для взаимодействия с API распознавания лиц (локальный или Docker).
    /// </summary>
    public static Func<IFaceRecognitionClient> CreateFaceRecognitionClient { get; set; } = () => LocalFaceRecognitionClient.CreateDefault();

    /// <summary>
    /// Фабрика для создания сервиса разметки лиц.
    /// </summary>
    public static Func<FaceLabelingService> CreateFaceLabelingService { get; set; } =
        () => new FaceLabelingService(CreateFaceCatalogService());

    /// <summary>
    /// Фабрика для создания сервиса кластеризации лиц.
    /// </summary>
    public static Func<FaceClusteringService> CreateFaceClusteringService { get; set; } = () => new FaceClusteringService();

    /// <summary>
    /// Фабрика для создания сервиса управления персонами лиц.
    /// Используется для переименования и слияния персон.
    /// </summary>
    public static Func<FacePersonManagementService> CreateFacePersonManagementService { get; set; } =
        () => new FacePersonManagementService(CreateFaceCatalogService());

    /// <summary>
    /// Фабрика для создания пайплайна индексации лиц.
    /// </summary>
    public static Func<Action<string>?, FaceIndexingPipelineService> CreateFaceIndexingPipelineService { get; set; } =
        (logger) => new FaceIndexingPipelineService(CreateFaceRecognitionClient(), CreateFaceCatalogService(), logger);
}
