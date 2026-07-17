using Microsoft.Data.Sqlite;

namespace ListenShelf.Infrastructure.Storage;

public sealed class ListenShelfDatabase
{
    private readonly string _connectionString;

    public ListenShelfDatabase(string? databasePath = null)
    {
        DatabasePath = Path.GetFullPath(databasePath ?? GetDefaultDatabasePath());

        var parentDirectory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("The ListenShelf database needs a parent directory.");

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

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();

        return connection;
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

            CREATE TABLE IF NOT EXISTS app_settings (
                setting_key TEXT NOT NULL PRIMARY KEY,
                setting_value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS library_books (
                book_id TEXT NOT NULL PRIMARY KEY,
                title TEXT NOT NULL,
                file_path TEXT NOT NULL,
                file_key TEXT NOT NULL UNIQUE,
                storage_mode TEXT NOT NULL CHECK (storage_mode IN ('Linked', 'Managed')),
                source_path TEXT NULL,
                source_key TEXT NULL,
                file_size_bytes INTEGER NOT NULL CHECK (file_size_bytes >= 0),
                added_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_library_books_managed_source
            ON library_books(source_key)
            WHERE source_key IS NOT NULL;
            """;
        command.ExecuteNonQuery();
    }
}
