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
}
