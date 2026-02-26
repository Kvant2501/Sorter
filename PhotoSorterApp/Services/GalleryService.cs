#nullable enable

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Services;

/// <summary>
/// HTTP client for the Gallery service running in Docker (http://localhost:8080).
/// </summary>
public class GalleryService : IDisposable
{
    private readonly HttpClient _httpClient;

    public GalleryService(string baseUrl = "http://localhost:8080")
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Check if the gallery service is available.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Start indexing a folder. Returns the task ID.
    /// </summary>
    public async Task<IndexingStartResult> StartIndexingAsync(string folderPath, bool recursive = true, bool useAi = false, CancellationToken ct = default)
    {
        var body = new { folder = folderPath, recursive, use_ai = useAi };
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/api/index", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<IndexingStartResult>(responseJson, JsonOpts)
               ?? throw new InvalidOperationException("Empty response from gallery service.");
    }

    /// <summary>
    /// Get the status of an indexing task.
    /// </summary>
    public async Task<IndexingStatusResult> GetIndexingStatusAsync(int taskId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/index/status/{taskId}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<IndexingStatusResult>(json, JsonOpts)
               ?? throw new InvalidOperationException("Empty response from gallery service.");
    }

    /// <summary>
    /// Get gallery statistics.
    /// </summary>
    public async Task<GalleryStatsResult> GetStatsAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("/api/stats", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GalleryStatsResult>(json, JsonOpts)
               ?? throw new InvalidOperationException("Empty response from gallery service.");
    }

    /// <summary>
    /// Debug: list files visible inside the container at the given folder.
    /// </summary>
    public async Task<DebugFilesResult> DebugListFilesAsync(string folder = "/photos", CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/debug/files?folder={Uri.EscapeDataString(folder)}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<DebugFilesResult>(json, JsonOpts)
               ?? new DebugFilesResult();
    }

    public void Dispose() => _httpClient.Dispose();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public class IndexingStartResult
{
    [JsonPropertyName("task_id")]
    public int TaskId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

public class IndexingStatusResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("folder_path")]
    public string FolderPath { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("total_files")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("processed_files")]
    public int ProcessedFiles { get; set; }

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("finished_at")]
    public string? FinishedAt { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class GalleryStatsResult
{
    [JsonPropertyName("total_photos")]
    public int TotalPhotos { get; set; }

    [JsonPropertyName("total_albums")]
    public int TotalAlbums { get; set; }

    [JsonPropertyName("years")]
    public int[] Years { get; set; } = [];

    [JsonPropertyName("categories")]
    public GalleryCategoryInfo[] Categories { get; set; } = [];
}

public class GalleryCategoryInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class DebugFilesResult
{
    [JsonPropertyName("folder")]
    public string Folder { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("is_dir")]
    public bool IsDir { get; set; }

    [JsonPropertyName("files")]
    public DebugFileEntry[] Files { get; set; } = [];

    [JsonPropertyName("dirs")]
    public string[] Dirs { get; set; } = [];

    [JsonPropertyName("total_entries")]
    public int TotalEntries { get; set; }

    [JsonPropertyName("errors")]
    public string[] Errors { get; set; } = [];
}

public class DebugFileEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("ext")]
    public string Ext { get; set; } = "";
}
