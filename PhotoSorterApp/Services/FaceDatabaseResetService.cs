#nullable enable

using PhotoSorterApp.Models;
using System;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

/// <summary>
/// Полная очистка БД для диагностики и рестарта
/// </summary>
public class FaceDatabaseResetService
{
    private readonly IFaceCatalogService _catalogService;

    public FaceDatabaseResetService(IFaceCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<ResetResult> ResetAllAsync()
    {
        var result = new ResetResult();

        try
        {
            // Получить статистику до очистки
            var photosBefore = await _catalogService.GetAllPhotosCountAsync();
            var facesBefore = await _catalogService.GetAllFacesCountAsync();
            var personsBefore = await _catalogService.GetPersonsCountAsync();
            var tagsBefore = await _catalogService.GetAllTagsCountAsync();

            result.PhotosBefore = photosBefore;
            result.FacesBefore = facesBefore;
            result.PersonsBefore = personsBefore;
            result.TagsBefore = tagsBefore;

            // === ОЧИСТКА В ПРАВИЛЬНОМ ПОРЯДКЕ ===
            // 1. Удаляем теги фото (зависят от PhotoAssets)
            await _catalogService.DeleteAllPhotoTagsAsync();

            // 2. Удаляем обнаруженные лица (зависят от PhotoAssets)
            await _catalogService.DeleteAllDetectedFacesAsync();

            // 3. Удаляем сами фото
            await _catalogService.DeleteAllPhotosAsync();

            result.Success = true;
            result.Message = "? База данных полностью очищена. Готова к новой индексации.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"? Ошибка при очистке: {ex.Message}";
        }

        return result;
    }

    public class ResetResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int PhotosBefore { get; set; }
        public int FacesBefore { get; set; }
        public int PersonsBefore { get; set; }
        public int TagsBefore { get; set; }
    }
}
