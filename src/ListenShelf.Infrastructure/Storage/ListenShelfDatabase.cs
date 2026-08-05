using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ListenShelf.Infrastructure.Storage;

public sealed class ListenShelfDatabase
{
    public const int CurrentSchemaVersion = 2;

    private static readonly DatabaseMigration[] Migrations =
    [
        new(1, "Create current ListenShelf schema", ApplyCurrentSchema),
        new(2, "Remove retired library mode setting", RemoveRetiredLibraryModeSetting),
    ];

    private readonly string _connectionString;

    public ListenShelfDatabase(
        string? databasePath = null,
        bool createMigrationSafetyCopy = true)
    {
        DatabasePath = Path.GetFullPath(databasePath ?? GetDefaultDatabasePath());

        DataRootPath = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("The ListenShelf database needs a parent directory.");

        try
        {
            Directory.CreateDirectory(DataRootPath);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException)
        {
            throw new ListenShelfDatabaseException(
                ListenShelfDatabaseFailureKind.Unavailable,
                "ListenShelf could not access its library data directory.",
                exception);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();

        Initialize(createMigrationSafetyCopy);
    }

    public string DatabasePath { get; }

    public string DataRootPath { get; }

    public string? MigrationSafetyCopyPath { get; private set; }

    public int SchemaVersion { get; private set; }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();

        return connection;
    }

    public static string GetDefaultDatabasePath()
        => ListenShelfPaths.CreateDefault().DatabasePath;

    private void Initialize(bool createMigrationSafetyCopy)
    {
        var databaseExisted = File.Exists(DatabasePath);

        try
        {
            using var connection = OpenConnection();
            if (databaseExisted)
            {
                EnsureIntegrity(connection);
            }

            var appliedMigrations = ReadAppliedMigrations(connection);
            ValidateMigrationHistory(appliedMigrations);
            var currentVersion = appliedMigrations.Count == 0
                ? 0
                : appliedMigrations[^1].Version;
            var pragmaVersion = ReadUserVersion(connection);

            if (currentVersion > CurrentSchemaVersion
                || pragmaVersion > CurrentSchemaVersion)
            {
                throw new ListenShelfDatabaseException(
                    ListenShelfDatabaseFailureKind.NewerVersion,
                    $"This library uses database version {Math.Max(currentVersion, pragmaVersion)}, but this ListenShelf build supports up to version {CurrentSchemaVersion}.");
            }

            if ((appliedMigrations.Count == 0 && pragmaVersion != 0)
                || (appliedMigrations.Count > 0 && pragmaVersion != currentVersion))
            {
                throw new ListenShelfDatabaseException(
                    ListenShelfDatabaseFailureKind.Damaged,
                    "The database schema version markers do not agree.");
            }

            var pendingMigrations = Migrations
                .Where(migration => migration.Version > currentVersion)
                .ToArray();
            if (pendingMigrations.Length > 0
                && databaseExisted
                && createMigrationSafetyCopy)
            {
                MigrationSafetyCopyPath = CreateMigrationSafetyCopy(
                    connection,
                    pendingMigrations[^1].Version);
            }

            ConfigureJournal(connection);
            EnsureMigrationTable(connection);

            foreach (var migration in pendingMigrations)
            {
                ApplyMigration(connection, migration);
            }

            EnsureIntegrity(connection);
            ValidateCurrentSchema(connection);
            SchemaVersion = CurrentSchemaVersion;
        }
        catch (ListenShelfDatabaseException)
        {
            SqliteConnection.ClearAllPools();
            throw;
        }
        catch (SqliteException exception)
        {
            SqliteConnection.ClearAllPools();
            throw Classify(exception);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException)
        {
            SqliteConnection.ClearAllPools();
            throw new ListenShelfDatabaseException(
                ListenShelfDatabaseFailureKind.Unavailable,
                "ListenShelf could not access its library database or data directory.",
                exception);
        }
        catch (Exception exception)
        {
            SqliteConnection.ClearAllPools();
            throw new ListenShelfDatabaseException(
                ListenShelfDatabaseFailureKind.MigrationFailed,
                "ListenShelf could not safely prepare the library database. No incomplete migration was kept.",
                exception);
        }
    }

    private static void ConfigureJournal(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<AppliedMigration> ReadAppliedMigrations(
        SqliteConnection connection)
    {
        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_migrations';";
        if (Convert.ToInt64(tableCommand.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT version, name FROM schema_migrations ORDER BY version;";
        using var reader = command.ExecuteReader();
        var migrations = new List<AppliedMigration>();
        while (reader.Read())
        {
            migrations.Add(new AppliedMigration(reader.GetInt32(0), reader.GetString(1)));
        }

        return migrations;
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void ValidateMigrationHistory(IReadOnlyList<AppliedMigration> applied)
    {
        for (var index = 0; index < applied.Count; index++)
        {
            var expectedVersion = index + 1;
            var actual = applied[index];
            if (actual.Version > CurrentSchemaVersion)
            {
                throw new ListenShelfDatabaseException(
                    ListenShelfDatabaseFailureKind.NewerVersion,
                    $"This library uses database version {actual.Version}, but this ListenShelf build supports up to version {CurrentSchemaVersion}.");
            }

            var expected = Migrations[index];
            if (actual.Version != expectedVersion
                || !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal))
            {
                throw new ListenShelfDatabaseException(
                    ListenShelfDatabaseFailureKind.Damaged,
                    "The database migration history is incomplete or does not match this ListenShelf build.");
            }
        }
    }

    private static void EnsureMigrationTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY CHECK (version > 0),
                name TEXT NOT NULL,
                applied_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void ApplyMigration(
        SqliteConnection connection,
        DatabaseMigration migration)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            migration.Apply(connection, transaction);

            using var recordCommand = connection.CreateCommand();
            recordCommand.Transaction = transaction;
            recordCommand.CommandText =
                """
                INSERT INTO schema_migrations (version, name, applied_utc)
                VALUES ($version, $name, $applied_utc);
                """;
            recordCommand.Parameters.AddWithValue("$version", migration.Version);
            recordCommand.Parameters.AddWithValue("$name", migration.Name);
            recordCommand.Parameters.AddWithValue(
                "$applied_utc",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            recordCommand.ExecuteNonQuery();

            using var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = $"PRAGMA user_version = {migration.Version};";
            versionCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch (Exception exception)
        {
            transaction.Rollback();
            throw new ListenShelfDatabaseException(
                ListenShelfDatabaseFailureKind.MigrationFailed,
                $"Database migration {migration.Version} ({migration.Name}) failed and was rolled back.",
                exception);
        }
    }

    private static void ApplyCurrentSchema(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
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

            CREATE TABLE IF NOT EXISTS playback_bookmarks (
                bookmark_id TEXT NOT NULL PRIMARY KEY,
                file_key TEXT NOT NULL,
                file_path TEXT NOT NULL,
                position_ms INTEGER NOT NULL CHECK (position_ms >= 0),
                name TEXT NULL,
                note TEXT NULL,
                chapter_index INTEGER NULL CHECK (chapter_index >= 0),
                chapter_title TEXT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_playback_bookmarks_file_position
            ON playback_bookmarks(file_key, position_ms, created_utc);

            CREATE TABLE IF NOT EXISTS library_books (
                book_id TEXT NOT NULL PRIMARY KEY,
                title TEXT NOT NULL,
                file_path TEXT NOT NULL,
                file_key TEXT NOT NULL UNIQUE,
                storage_mode TEXT NOT NULL CHECK (storage_mode = 'Managed'),
                source_path TEXT NULL,
                source_key TEXT NULL,
                file_size_bytes INTEGER NOT NULL CHECK (file_size_bytes >= 0),
                added_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_library_books_managed_source
            ON library_books(source_key)
            WHERE source_key IS NOT NULL;

            CREATE TABLE IF NOT EXISTS pending_library_removals (
                book_id TEXT NOT NULL PRIMARY KEY,
                title TEXT NOT NULL,
                file_path TEXT NOT NULL,
                cover_path TEXT NULL,
                requested_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        EnsureColumn(connection, transaction, "library_books", "cover_path", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "subtitle", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "authors_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureColumn(connection, transaction, "library_books", "series_name", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "series_position", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "original_publication_year", "INTEGER NULL");
        EnsureColumn(connection, transaction, "library_books", "original_publisher", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "description", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "genres_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureColumn(connection, transaction, "library_books", "narrators_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureColumn(connection, transaction, "library_books", "audio_publisher", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "audiobook_release_date", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "language", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "isbn_10", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "isbn_13", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "asin", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "edition_name", "TEXT NULL");
        EnsureColumn(connection, transaction, "library_books", "abridgement", "TEXT NOT NULL DEFAULT 'Unknown'");
        EnsureColumn(connection, transaction, "library_books", "edition_notes", "TEXT NULL");
    }

    private static void RemoveRetiredLibraryModeSetting(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM app_settings
            WHERE setting_key = 'library.default_storage_mode';
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        using var schemaCommand = connection.CreateCommand();
        schemaCommand.Transaction = transaction;
        schemaCommand.CommandText = $"PRAGMA table_info({tableName});";

        using (var reader = schemaCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.Transaction = transaction;
        alterCommand.CommandText =
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alterCommand.ExecuteNonQuery();
    }

    private string CreateMigrationSafetyCopy(
        SqliteConnection source,
        int targetVersion)
    {
        var timestamp = DateTimeOffset.Now.ToString(
            "yyyy-MM-dd HH-mm-ss",
            CultureInfo.InvariantCulture);
        var recoveryDirectory = CreateUniqueDirectory(Path.Combine(
            DataRootPath,
            "Database Recovery",
            $"Before migration to v{targetVersion} {timestamp}"));
        var safetyPath = Path.Combine(recoveryDirectory, "listenshelf.db");

        using var destination = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = safetyPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
        EnsureIntegrity(destination);
        return safetyPath;
    }

    private static string CreateUniqueDirectory(string preferredPath)
    {
        var path = preferredPath;
        while (Directory.Exists(path) || File.Exists(path))
        {
            path = $"{preferredPath} {Guid.NewGuid():N}";
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static void EnsureIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        using var reader = command.ExecuteReader();
        var result = reader.Read() ? reader.GetString(0) : null;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new ListenShelfDatabaseException(
                ListenShelfDatabaseFailureKind.Damaged,
                $"The library database failed SQLite's integrity check: {result ?? "unknown error"}.");
        }
    }

    private static void ValidateCurrentSchema(SqliteConnection connection)
    {
        var requiredTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "app_settings",
            "library_books",
            "pending_library_removals",
            "playback_bookmarks",
            "playback_progress",
            "schema_migrations",
        };

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                requiredTables.Remove(reader.GetString(0));
            }
        }

        if (requiredTables.Count > 0)
        {
            throw new ListenShelfDatabaseException(
                ListenShelfDatabaseFailureKind.Damaged,
                $"The library database is missing required tables: {string.Join(", ", requiredTables.Order())}.");
        }

        ValidateRequiredColumns(connection, "app_settings", "setting_key", "setting_value");
        ValidateRequiredColumns(
            connection,
            "library_books",
            "book_id",
            "title",
            "file_path",
            "file_key",
            "storage_mode",
            "source_path",
            "source_key",
            "file_size_bytes",
            "added_utc",
            "cover_path",
            "subtitle",
            "authors_json",
            "series_name",
            "series_position",
            "original_publication_year",
            "original_publisher",
            "description",
            "genres_json",
            "narrators_json",
            "audio_publisher",
            "audiobook_release_date",
            "language",
            "isbn_10",
            "isbn_13",
            "asin",
            "edition_name",
            "abridgement",
            "edition_notes");
        ValidateRequiredColumns(
            connection,
            "pending_library_removals",
            "book_id",
            "title",
            "file_path",
            "cover_path",
            "requested_utc");
        ValidateRequiredColumns(
            connection,
            "playback_bookmarks",
            "bookmark_id",
            "file_key",
            "file_path",
            "position_ms",
            "name",
            "note",
            "chapter_index",
            "chapter_title",
            "created_utc",
            "updated_utc");
        ValidateRequiredColumns(
            connection,
            "playback_progress",
            "file_key",
            "file_path",
            "position_ms",
            "duration_ms",
            "updated_utc");
        ValidateRequiredColumns(
            connection,
            "schema_migrations",
            "version",
            "name",
            "applied_utc");
    }

    private static void ValidateRequiredColumns(
        SqliteConnection connection,
        string tableName,
        params string[] requiredColumnNames)
    {
        var missingColumns = requiredColumnNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            missingColumns.Remove(reader.GetString(1));
        }

        if (missingColumns.Count > 0)
        {
            throw new ListenShelfDatabaseException(
                ListenShelfDatabaseFailureKind.Damaged,
                $"The library database table {tableName} is missing required columns: {string.Join(", ", missingColumns.Order())}.");
        }
    }

    private static ListenShelfDatabaseException Classify(SqliteException exception)
    {
        var primaryCode = exception.SqliteErrorCode & 0xFF;
        var kind = primaryCode is 1 or 11 or 26
            ? ListenShelfDatabaseFailureKind.Damaged
            : ListenShelfDatabaseFailureKind.Unavailable;
        var message = kind == ListenShelfDatabaseFailureKind.Damaged
            ? "SQLite reported that the ListenShelf library database is damaged or is not a database."
            : "ListenShelf could not open or read its library database.";
        return new ListenShelfDatabaseException(kind, message, exception);
    }

    private sealed record DatabaseMigration(
        int Version,
        string Name,
        Action<SqliteConnection, SqliteTransaction> Apply);

    private sealed record AppliedMigration(int Version, string Name);
}
