#nullable enable

using System;

namespace PhotoSorterApp.Services;

public static class ServiceLocator
{
    // Factory delegates Ч можно переопределить в тестах
    public static Func<DuplicateDetectionService> CreateDuplicateDetectionService { get; set; } = () => new DuplicateDetectionService();
    public static Func<PhotoSortingService> CreatePhotoSortingService { get; set; } = () => new PhotoSortingService();
}
