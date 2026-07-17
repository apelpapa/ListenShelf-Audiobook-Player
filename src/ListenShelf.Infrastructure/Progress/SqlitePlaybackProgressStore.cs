using System.Globalization;
using ListenShelf.Application.Progress;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Infrastructure.Progress;

public sealed class SqlitePlaybackProgressStore : IPlaybackProgressStore
{
    private readonly ListenShelfDatabase _database;

    public SqlitePlaybackProgressStore(string? databasePath = null)
        : this(new ListenShelfDatabase(databasePath))
    {
    }

    public SqlitePlaybackProgressStore(ListenShelfDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public string DatabasePath => _database.DatabasePath;

    public PlaybackProgress? Get(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT file_path, position_ms, duration_ms, updated_utc
            FROM playback_progress
            WHERE file_key = $file_key;
            """;
        command.Parameters.AddWithValue("$file_key", CreateFileKey(normalizedPath));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var position = TimeSpan.FromMilliseconds(Math.Max(0L, reader.GetInt64(1)));
        var duration = TimeSpan.FromMilliseconds(Math.Max(0L, reader.GetInt64(2)));
        var updatedAtUtc = DateTimeOffset.Parse(
            reader.GetString(3),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        return new PlaybackProgress(reader.GetString(0), position, duration, updatedAtUtc);
    }

    public void Save(PlaybackProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var normalizedPath = NormalizePath(progress.FilePath);
        var positionMilliseconds = Math.Max(0L, (long)progress.Position.TotalMilliseconds);
        var durationMilliseconds = Math.Max(0L, (long)progress.Duration.TotalMilliseconds);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO playback_progress (
                file_key,
                file_path,
                position_ms,
                duration_ms,
                updated_utc)
            VALUES (
                $file_key,
                $file_path,
                $position_ms,
                $duration_ms,
                $updated_utc)
            ON CONFLICT(file_key) DO UPDATE SET
                file_path = excluded.file_path,
                position_ms = excluded.position_ms,
                duration_ms = excluded.duration_ms,
                updated_utc = excluded.updated_utc;
            """;

        command.Parameters.AddWithValue("$file_key", CreateFileKey(normalizedPath));
        command.Parameters.AddWithValue("$file_path", normalizedPath);
        command.Parameters.AddWithValue("$position_ms", positionMilliseconds);
        command.Parameters.AddWithValue("$duration_ms", durationMilliseconds);
        command.Parameters.AddWithValue(
            "$updated_utc",
            progress.UpdatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
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
}
