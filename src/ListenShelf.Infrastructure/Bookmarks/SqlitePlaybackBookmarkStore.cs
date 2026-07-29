using System.Globalization;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Infrastructure.Bookmarks;

public sealed class SqlitePlaybackBookmarkStore : IPlaybackBookmarkStore
{
    private const int MaximumNameLength = 120;
    private const int MaximumNoteLength = 2000;
    private const int MaximumChapterTitleLength = 500;
    private readonly ListenShelfDatabase _database;

    public SqlitePlaybackBookmarkStore(string? databasePath = null)
        : this(new ListenShelfDatabase(databasePath))
    {
    }

    public SqlitePlaybackBookmarkStore(ListenShelfDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public IReadOnlyList<PlaybackBookmark> GetForFile(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                bookmark_id,
                file_path,
                position_ms,
                name,
                note,
                chapter_index,
                chapter_title,
                created_utc,
                updated_utc
            FROM playback_bookmarks
            WHERE file_key = $file_key
            ORDER BY position_ms, created_utc;
            """;
        command.Parameters.AddWithValue("$file_key", CreateFileKey(normalizedPath));

        using var reader = command.ExecuteReader();
        var bookmarks = new List<PlaybackBookmark>();
        while (reader.Read())
        {
            bookmarks.Add(ReadBookmark(reader));
        }

        return bookmarks;
    }

    public void Save(PlaybackBookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);

        if (bookmark.Id == Guid.Empty)
        {
            throw new ArgumentException("A bookmark ID is required.", nameof(bookmark));
        }

        if (bookmark.Position < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(bookmark));
        }

        if (bookmark.ChapterIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bookmark));
        }

        var normalizedPath = NormalizePath(bookmark.FilePath);
        var positionMilliseconds = Math.Max(0L, (long)bookmark.Position.TotalMilliseconds);
        var name = NormalizeOptionalText(bookmark.Name, MaximumNameLength, nameof(bookmark.Name));
        var note = NormalizeOptionalText(bookmark.Note, MaximumNoteLength, nameof(bookmark.Note));
        var chapterTitle = NormalizeOptionalText(
            bookmark.ChapterTitle,
            MaximumChapterTitleLength,
            nameof(bookmark.ChapterTitle));
        var createdAtUtc = bookmark.CreatedAtUtc.ToUniversalTime();
        var updatedAtUtc = bookmark.UpdatedAtUtc.ToUniversalTime();

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO playback_bookmarks (
                bookmark_id,
                file_key,
                file_path,
                position_ms,
                name,
                note,
                chapter_index,
                chapter_title,
                created_utc,
                updated_utc)
            VALUES (
                $bookmark_id,
                $file_key,
                $file_path,
                $position_ms,
                $name,
                $note,
                $chapter_index,
                $chapter_title,
                $created_utc,
                $updated_utc)
            ON CONFLICT(bookmark_id) DO UPDATE SET
                file_key = excluded.file_key,
                file_path = excluded.file_path,
                position_ms = excluded.position_ms,
                name = excluded.name,
                note = excluded.note,
                chapter_index = excluded.chapter_index,
                chapter_title = excluded.chapter_title,
                updated_utc = excluded.updated_utc;
            """;

        command.Parameters.AddWithValue("$bookmark_id", bookmark.Id.ToString("D"));
        command.Parameters.AddWithValue("$file_key", CreateFileKey(normalizedPath));
        command.Parameters.AddWithValue("$file_path", normalizedPath);
        command.Parameters.AddWithValue("$position_ms", positionMilliseconds);
        command.Parameters.AddWithValue("$name", (object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$chapter_index",
            bookmark.ChapterIndex is { } chapterIndex
                ? chapterIndex
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$chapter_title",
            (object?)chapterTitle ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$created_utc",
            createdAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$updated_utc",
            updatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public void Delete(Guid bookmarkId)
    {
        if (bookmarkId == Guid.Empty)
        {
            throw new ArgumentException("A bookmark ID is required.", nameof(bookmarkId));
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM playback_bookmarks
            WHERE bookmark_id = $bookmark_id;
            """;
        command.Parameters.AddWithValue("$bookmark_id", bookmarkId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static PlaybackBookmark ReadBookmark(
        Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var id = Guid.Parse(reader.GetString(0));
        var position = TimeSpan.FromMilliseconds(Math.Max(0L, reader.GetInt64(2)));
        var name = reader.IsDBNull(3) ? null : reader.GetString(3);
        var note = reader.IsDBNull(4) ? null : reader.GetString(4);
        var chapterIndex = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
        var chapterTitle = reader.IsDBNull(6) ? null : reader.GetString(6);
        var createdAtUtc = DateTimeOffset.Parse(
            reader.GetString(7),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var updatedAtUtc = DateTimeOffset.Parse(
            reader.GetString(8),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        return new PlaybackBookmark(
            id,
            reader.GetString(1),
            position,
            name,
            note,
            chapterIndex,
            chapterTitle,
            createdAtUtc,
            updatedAtUtc);
    }

    private static string NormalizePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("An audiobook path is required.", nameof(filePath));
        }

        return Path.GetFullPath(filePath);
    }

    private static string CreateFileKey(string normalizedPath) =>
        OperatingSystem.IsWindows()
            ? normalizedPath.ToUpperInvariant()
            : normalizedPath;

    private static string? NormalizeOptionalText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

        if (normalized?.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }
}
