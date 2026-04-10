using Microsoft.Data.Sqlite;
using System.Text;

namespace PhotoSorterApp.Web;

public sealed class GalleryRepository
{
    private readonly string _connectionString;

    public GalleryRepository(string? databasePath = null)
    {
        var dbPath = databasePath;
        if (string.IsNullOrWhiteSpace(dbPath))
            dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoSorter", "face-catalog.db");

        _connectionString = $"Data Source={dbPath};Pooling=False";
    }

    public IReadOnlyList<PersonDto> GetPersons()
    {
        const string sql = """
            SELECT fp.Id, fp.DisplayName, COUNT(df.Id) AS FacesCount
            FROM FacePersons fp
            LEFT JOIN DetectedFaces df ON df.ConfirmedPersonId = fp.Id
            GROUP BY fp.Id, fp.DisplayName
            ORDER BY fp.DisplayName;
            """;

        return Query(sql, r => new PersonDto(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));
    }

    public IReadOnlyList<TagDto> GetTags()
    {
        const string sql = """
            SELECT t.Id, t.Name, t.Kind, COUNT(pt.PhotoAssetId) AS PhotosCount
            FROM Tags t
            LEFT JOIN PhotoTags pt ON pt.TagId = t.Id
            GROUP BY t.Id, t.Name, t.Kind
            ORDER BY PhotosCount DESC, t.Name;
            """;

        return Query(sql, r => new TagDto(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3)));
    }

    public IReadOnlyList<ArchiveMonthDto> GetArchiveMonths()
    {
        const string sql = """
            SELECT CAST(strftime('%Y', p.CapturedAtUtc) AS INTEGER) AS Y,
                   CAST(strftime('%m', p.CapturedAtUtc) AS INTEGER) AS M,
                   COUNT(*) AS PhotosCount
            FROM PhotoAssets p
            WHERE p.CapturedAtUtc IS NOT NULL
            GROUP BY Y, M
            ORDER BY Y DESC, M DESC;
            """;

        return Query(sql, r => new ArchiveMonthDto(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
    }

    public IReadOnlyList<AlbumDto> GetAlbums()
    {
        const string sql = """
            SELECT a.Id, a.Name, a.IsSmartAlbum, COUNT(ap.PhotoAssetId) AS PhotosCount
            FROM PhotoAlbums a
            LEFT JOIN AlbumPhotos ap ON ap.PhotoAlbumId = a.Id
            GROUP BY a.Id, a.Name, a.IsSmartAlbum
            ORDER BY a.Name;
            """;

        return Query(sql, r => new AlbumDto(r.GetInt32(0), r.GetString(1), r.GetBoolean(2), r.GetInt32(3)));
    }

    public IReadOnlyList<FolderDto> GetFolders()
    {
        const string sql = """
            SELECT p.FilePath
            FROM PhotoAssets p
            WHERE p.FilePath IS NOT NULL AND p.FilePath <> ''
            ORDER BY p.FilePath;
            """;

        var filePaths = Query(sql, r => r.GetString(0));

        return filePaths
            .Select(p => {
                var dir = Path.GetDirectoryName(p);
                return string.IsNullOrWhiteSpace(dir) ? "Корневая папка" : dir;
            })
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FolderDto(g.Key, g.Count()))
            .OrderBy(g => g.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<PhotoDto> GetPhotos(string? person, string? tag, string? folder, int? year, int? month, int skip, int take)
    {
        var rows = QueryPhotos(person, tag, folder, year, month, randomize: false, take, skip);
        return rows;
    }

    public IReadOnlyList<PhotoDto> GetRandomSelection(int count, string? person, string? tag, string? folder, int? year, int? month)
    {
        return QueryPhotos(person, tag, folder, year, month, randomize: true, count, 0);
    }

    private IReadOnlyList<PhotoDto> QueryPhotos(string? person, string? tag, string? folder, int? year, int? month, bool randomize, int take, int skip)
    {
        var where = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrWhiteSpace(person))
        {
            where.Add("EXISTS (SELECT 1 FROM PhotoTags pt JOIN Tags t ON t.Id=pt.TagId WHERE pt.PhotoAssetId = p.Id AND t.Kind=0 AND t.Name = $person)");
            parameters.Add(new SqliteParameter("$person", person.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            where.Add("EXISTS (SELECT 1 FROM PhotoTags pt JOIN Tags t ON t.Id=pt.TagId WHERE pt.PhotoAssetId = p.Id AND t.Name = $tag)");
            parameters.Add(new SqliteParameter("$tag", tag.Trim()));
        }

        if (year.HasValue)
        {
            where.Add("CAST(strftime('%Y', p.CapturedAtUtc) AS INTEGER) = $year");
            parameters.Add(new SqliteParameter("$year", year.Value));
        }

        if (month.HasValue)
        {
            where.Add("CAST(strftime('%m', p.CapturedAtUtc) AS INTEGER) = $month");
            parameters.Add(new SqliteParameter("$month", month.Value));
        }

        if (!string.IsNullOrWhiteSpace(folder))
        {
            var f = folder.Trim();
            if (f == "Корневая папка")
            {
                where.Add("(p.FilePath NOT LIKE '%\\%' AND p.FilePath NOT LIKE '%/%')");
            }
            else
            {
                var winPrefix = f.EndsWith("\\", StringComparison.Ordinal) ? f : f + "\\";
                var unixPrefix = f.EndsWith("/", StringComparison.Ordinal) ? f : f + "/";

                where.Add("(p.FilePath = $folderExact OR p.FilePath LIKE $folderWin OR p.FilePath LIKE $folderUnix)");
                parameters.Add(new SqliteParameter("$folderExact", f));
                parameters.Add(new SqliteParameter("$folderWin", winPrefix + "%"));
                parameters.Add(new SqliteParameter("$folderUnix", unixPrefix + "%"));
            }
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        var order = randomize ? "ORDER BY RANDOM()" : "ORDER BY p.CapturedAtUtc DESC, p.Id DESC";

        var sql = $"""
            SELECT p.Id, p.FilePath, p.CapturedAtUtc,
                   IFNULL((SELECT GROUP_CONCAT(DISTINCT t.Name) FROM PhotoTags pt JOIN Tags t ON t.Id=pt.TagId WHERE pt.PhotoAssetId = p.Id), '') AS Tags,
                   IFNULL((SELECT GROUP_CONCAT(DISTINCT fp.DisplayName)
                           FROM DetectedFaces df
                           JOIN FacePersons fp ON fp.Id = df.ConfirmedPersonId
                           WHERE df.PhotoAssetId = p.Id), '') AS Persons
            FROM PhotoAssets p
            {whereClause}
            {order}
            LIMIT $take OFFSET $skip;
            """;

        parameters.Add(new SqliteParameter("$take", take));
        parameters.Add(new SqliteParameter("$skip", skip));

        return Query(sql, r =>
        {
            var id = r.GetInt32(0);
            var filePath = r.GetString(1);
            var captured = r.IsDBNull(2) ? (DateTime?)null : r.GetDateTime(2);
            var tags = r.GetString(3);
            var persons = r.GetString(4);

            return new PhotoDto(
                id,
                filePath,
                captured,
                tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                persons.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }, parameters.ToArray());
    }

    /// <summary>
    /// Export detected faces grouped by confirmed person (clusters) to CSV.
    /// Columns: DetectedFaceId, PhotoPath, PersonId, PersonName, Confidence
    /// </summary>
    public string ExportClustersCsv(bool includeUnknowns = true, double minConfidence = 0.0)
    {
        var sql = @"SELECT df.Id, p.FilePath, df.ConfirmedPersonId, fp.DisplayName, df.Confidence
                      FROM DetectedFaces df
                      JOIN PhotoAssets p ON p.Id = df.PhotoAssetId
                      LEFT JOIN FacePersons fp ON fp.Id = df.ConfirmedPersonId
                      WHERE df.Confidence >= $min
                      ORDER BY CASE WHEN df.ConfirmedPersonId IS NULL THEN 1 ELSE 0 END, fp.DisplayName, p.FilePath;";

        var parameters = new[] { new SqliteParameter("$min", minConfidence) };

        var rows = Query(sql, r => new
        {
            Id = r.GetInt32(0),
            FilePath = r.GetString(1),
            PersonId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
            PersonName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
            Confidence = r.GetDouble(4)
        }, parameters);

        var sb = new StringBuilder();
        sb.AppendLine("DetectedFaceId,PhotoPath,PersonId,PersonName,Confidence");

        foreach (var row in rows)
        {
            if (!includeUnknowns && row.PersonId is null)
                continue;

            var safePath = row.FilePath.Replace("\"", "\"\"");
            var personIdStr = row.PersonId?.ToString() ?? "";
            var personNameEsc = string.IsNullOrEmpty(row.PersonName) ? "" : row.PersonName.Replace("\"", "\"\"");
            var confidenceStr = row.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture);

            sb.AppendLine($"\"{row.Id}\",\"{safePath}\",\"{personIdStr}\",\"{personNameEsc}\",{confidenceStr}");
        }

        return sb.ToString();
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params SqliteParameter[] parameters)
    {
        var result = new List<T>();

        if (!DatabaseExists())
            return result;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(map(reader));

        return result;
    }

    private bool DatabaseExists()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        return File.Exists(builder.DataSource);
    }
}

public record PersonDto(int Id, string DisplayName, int FacesCount);
public record TagDto(int Id, string Name, int Kind, int PhotosCount);
public record FolderDto(string Path, int PhotosCount);
public record ArchiveMonthDto(int Year, int Month, int PhotosCount);
public record AlbumDto(int Id, string Name, bool IsSmartAlbum, int PhotosCount);
public record PhotoDto(int Id, string FilePath, DateTime? CapturedAtUtc, IReadOnlyList<string> Tags, IReadOnlyList<string> Persons);
