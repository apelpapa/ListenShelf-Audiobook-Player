using System.Globalization;
using ListenShelf.Application.Progress;
using Microsoft.Data.Sqlite;

namespace ListenShelf.Infrastructure.Progress;

public sealed class SqlitePlaybackProgressStore : IPlaybackProgressStore
{
    private readonly string _connectionString;

    public SqlitePlaybackProgressStore(string? databasePath = null)
    {
        DatabasePath = Path.GetFullPath(databasePath ?? GetDefaultDatabasePath());

        var parentDirectory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("The progress database needs a parent directory.");

        Directory.CreateDirectory(parentDirectory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();

        EnsureSchema();
    }

    public string DatabasePath { get; }

    public PlaybackProgress? Get(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);

        using var connection = OpenConnection();
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

        using var connection = OpenConnection();
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

    private static string GetDefaultDatabasePath()
    {
        var localDataDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        if (string.IsNullOrWhiteSpace(localDataDirectory))
        {
            throw new InvalidOperationException("The local application-data directory is unavailable.");
        }

        return Path.Combine(localDataDirectory, "ListenShelf", "listenshelf.db");
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();

        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS playback_progress (
                file_key TEXT NOT NULL PRIMARY KEY,
                file_path TEXT NOT NULL,
                position_ms INTEGER NOT NULL CHECK (position_ms >= 0),
                duration_ms INTEGER NOT NULL CHECK (duration_ms >= 0),
                updated_utc TEXT NOT NULL
            );
            """;
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
