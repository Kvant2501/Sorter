#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

/// <summary>
/// HTTP-клиент дл€ CLIP-сервиса в Docker-контейнере.
/// </summary>
public class ImageClassificationService : IDisposable
{
    private readonly HttpClient _httpClient;

    public ImageClassificationService(string baseUrl = "http://localhost:8000")
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    /// <summary>
    /// ѕровер€ет, доступен ли CLIP-сервис.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///  лассифицирует изображение по списку категорий.
    /// </summary>
    public async Task<ClassificationResult> ClassifyAsync(
        string filePath,
        IEnumerable<string>? categories = null,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();

        var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        if (categories != null)
        {
            var categoriesStr = string.Join(",", categories);
            form.Add(new StringContent(categoriesStr), "categories");
        }

        var response = await _httpClient.PostAsync("/classify", form, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<ClassificationResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result ?? throw new InvalidOperationException("Empty response from CLIP service.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public class ClassificationResult
{
    [JsonPropertyName("best")]
    public string Best { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("results")]
    public List<CategoryScore> Results { get; set; } = new();
}

public class CategoryScore
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}
