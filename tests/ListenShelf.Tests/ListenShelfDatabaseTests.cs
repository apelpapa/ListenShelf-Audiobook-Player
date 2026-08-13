using Microsoft.Data.Sqlite;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ListenShelfDatabaseTests
{
    [Fact]
    public void CreatingAFreshDatabase_CreatesTheCurrentTables()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sqlite_schema
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        using var reader = command.ExecuteReader();
        var tableNames = new List<string>();
        while (reader.Read())
        {
            tableNames.Add(reader.GetString(0));
        }

        Assert.Equal(
            [
                "app_settings",
                "library_books",
                "pending_library_removals",
                "playback_bookmarks",
                "playback_progress",
                "schema_migrations",
            ],
            tableNames);

        Assert.Equal(ListenShelfDatabase.CurrentSchemaVersion, database.SchemaVersion);
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        Assert.Equal(
            ListenShelfDatabase.CurrentSchemaVersion,
            Convert.ToInt32(versionCommand.ExecuteScalar()));

        using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(
            ListenShelfDatabase.CurrentSchemaVersion,
            Convert.ToInt32(migrationCommand.ExecuteScalar()));
    }

    [Fact]
    public void OpeningTheLegacySchema_RemovesTheObsoleteModeSettingAndHidesLinkedBooks()
    {
        using var workspace = new TestWorkspace();
        CreateLegacyDatabase(workspace.DatabasePath);

        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var visibleBook = Assert.Single(library.GetBooks());

        Assert.Equal("Managed legacy book", visibleBook.Title);

        using var connection = database.OpenConnection();
        using var settingCommand = connection.CreateCommand();
        settingCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM app_settings
            WHERE setting_key = 'library.default_storage_mode';
            """;
        Assert.Equal(0L, (long)settingCommand.ExecuteScalar()!);

        using var bookCommand = connection.CreateCommand();
        bookCommand.CommandText = "SELECT COUNT(*) FROM library_books;";
        Assert.Equal(2L, (long)bookCommand.ExecuteScalar()!);

        Assert.NotNull(database.MigrationSafetyCopyPath);
        Assert.True(File.Exists(database.MigrationSafetyCopyPath));
    }

    [Fact]
    public void ReopeningCurrentDatabase_DoesNotCreateAnotherMigrationSafetyCopy()
    {
        using var workspace = new TestWorkspace();
        _ = new ListenShelfDatabase(workspace.DatabasePath);
        var recoveryRoot = Path.Combine(
            Path.GetDirectoryName(workspace.DatabasePath)!,
            "Database Recovery");
        var safetyCopyCount = Directory.Exists(recoveryRoot)
            ? Directory.EnumerateFiles(recoveryRoot, "listenshelf.db", SearchOption.AllDirectories).Count()
            : 0;

        var reopened = new ListenShelfDatabase(workspace.DatabasePath);

        Assert.Null(reopened.MigrationSafetyCopyPath);
        Assert.Equal(
            safetyCopyCount,
            Directory.Exists(recoveryRoot)
                ? Directory.EnumerateFiles(recoveryRoot, "listenshelf.db", SearchOption.AllDirectories).Count()
                : 0);
    }

    [Fact]
    public void OpeningDatabaseFromNewerVersion_IsRefusedWithoutChangingVersion()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"PRAGMA user_version = {ListenShelfDatabase.CurrentSchemaVersion + 1};";
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<ListenShelfDatabaseException>(() =>
            new ListenShelfDatabase(workspace.DatabasePath));

        Assert.Equal(ListenShelfDatabaseFailureKind.NewerVersion, exception.Kind);
        using var verifyConnection = database.OpenConnection();
        using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "PRAGMA user_version;";
        Assert.Equal(
            ListenShelfDatabase.CurrentSchemaVersion + 1,
            Convert.ToInt32(verifyCommand.ExecuteScalar()));
    }

    [Fact]
    public void OpeningCorruptDatabase_ReportsDamageAndLeavesFileUntouched()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.DatabasePath)!);
        byte[] corruptContents = [0x4C, 0x69, 0x73, 0x74, 0x65, 0x6E, 0x53, 0x68, 0x65, 0x6C, 0x66];
        File.WriteAllBytes(workspace.DatabasePath, corruptContents);

        var exception = Assert.Throws<ListenShelfDatabaseException>(() =>
            new ListenShelfDatabase(workspace.DatabasePath));

        Assert.Equal(ListenShelfDatabaseFailureKind.Damaged, exception.Kind);
        Assert.Equal(corruptContents, File.ReadAllBytes(workspace.DatabasePath));
    }

    [Fact]
    public void OpeningDatabaseWithCurrentVersionButMissingSchema_IsReportedAsDamaged()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DROP TABLE playback_bookmarks;";
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<ListenShelfDatabaseException>(() =>
            new ListenShelfDatabase(workspace.DatabasePath));

        Assert.Equal(ListenShelfDatabaseFailureKind.Damaged, exception.Kind);
        Assert.Contains("playback_bookmarks", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedMigration_IsRolledBackAndSafetyCopyIsPreserved()
    {
        using var workspace = new TestWorkspace();
        CreateMigrationFailureDatabase(workspace.DatabasePath);

        var exception = Assert.Throws<ListenShelfDatabaseException>(() =>
            new ListenShelfDatabase(workspace.DatabasePath));

        Assert.Equal(ListenShelfDatabaseFailureKind.MigrationFailed, exception.Kind);
        using var connection = OpenRawDatabase(workspace.DatabasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
        Assert.True(Directory.EnumerateFiles(
            Path.Combine(Path.GetDirectoryName(workspace.DatabasePath)!, "Database Recovery"),
            "listenshelf.db",
            SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void VersionTwoMigration_AddsNullableFingerprintsWithoutScanningOrChangingBooks()
    {
        using var workspace = new TestWorkspace();
        var previous = CreateVersionTwoFixture(workspace);
        var originalBook = Assert.Single(new SqliteAudiobookLibrary(previous, workspace.ManagedLibraryPath).GetBooks());

        var upgraded = new ListenShelfDatabase(workspace.DatabasePath);

        Assert.Equal(3, upgraded.SchemaVersion);
        Assert.NotNull(upgraded.MigrationSafetyCopyPath);
        using var connection = upgraded.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content_sha256 FROM library_books;";
        Assert.Equal(DBNull.Value, command.ExecuteScalar());
        var book = Assert.Single(new SqliteAudiobookLibrary(upgraded, workspace.ManagedLibraryPath).GetBooks());
        Assert.Equal(originalBook.Id, book.Id);
        Assert.Equal(originalBook.Title, book.Title);
        Assert.Equal(originalBook.FilePath, book.FilePath);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(book.FilePath));
        using var preserved = OpenRawDatabase(upgraded.MigrationSafetyCopyPath!);
        using var preservedCommand = preserved.CreateCommand();
        preservedCommand.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, preservedCommand.ExecuteScalar());
        preservedCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('library_books') WHERE name = 'content_sha256';";
        Assert.Equal(0L, preservedCommand.ExecuteScalar());
        Assert.Null(new ListenShelfDatabase(workspace.DatabasePath).MigrationSafetyCopyPath);
    }

    [Fact]
    public void FingerprintMigrationFailure_RollsBackColumnIndexAndVersionChanges()
    {
        using var workspace = new TestWorkspace();
        var previous = CreateVersionTwoFixture(workspace);
        using (var connection = previous.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TRIGGER reject_fingerprint_migration BEFORE INSERT ON schema_migrations
                WHEN NEW.version = 3
                BEGIN SELECT RAISE(ABORT, 'Test migration failure'); END;
                """;
            command.ExecuteNonQuery();
        }

        var error = Assert.Throws<ListenShelfDatabaseException>(() => new ListenShelfDatabase(workspace.DatabasePath));

        Assert.Equal(ListenShelfDatabaseFailureKind.MigrationFailed, error.Kind);
        using var verify = previous.OpenConnection();
        using var check = verify.CreateCommand();
        check.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, check.ExecuteScalar());
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('library_books') WHERE name = 'content_sha256';";
        Assert.Equal(0L, check.ExecuteScalar());
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'ux_library_books_managed_source';";
        Assert.Equal(1L, check.ExecuteScalar());
        check.CommandText = "SELECT COUNT(*) FROM library_books;";
        Assert.Equal(1L, check.ExecuteScalar());
    }

    private static ListenShelfDatabase CreateVersionTwoFixture(TestWorkspace workspace)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        library.Import(workspace.CreateSourceFile("Existing v2 book.m4b", [1, 2, 3]));
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        // Reconstruct the released v2 shape in this isolated test database only.
        command.CommandText =
            """
            DROP INDEX ix_library_books_content;
            ALTER TABLE library_books DROP COLUMN content_sha256;
            CREATE UNIQUE INDEX ux_library_books_managed_source ON library_books(source_key)
                WHERE source_key IS NOT NULL;
            DELETE FROM schema_migrations WHERE version = 3;
            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
        return database;
    }

    private static void CreateLegacyDatabase(string databasePath)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("A database directory is required."));

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE app_settings (
                setting_key TEXT NOT NULL PRIMARY KEY,
                setting_value TEXT NOT NULL
            );

            INSERT INTO app_settings (setting_key, setting_value)
            VALUES ('library.default_storage_mode', 'Linked');

            CREATE TABLE library_books (
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

            INSERT INTO library_books (
                book_id,
                title,
                file_path,
                file_key,
                storage_mode,
                source_path,
                source_key,
                file_size_bytes,
                added_utc)
            VALUES (
                '11111111-1111-1111-1111-111111111111',
                'Linked legacy book',
                'C:\legacy\linked.m4b',
                'C:\LEGACY\LINKED.M4B',
                'Linked',
                'C:\legacy\linked.m4b',
                NULL,
                100,
                '2025-01-01T00:00:00.0000000+00:00'),
            (
                '22222222-2222-2222-2222-222222222222',
                'Managed legacy book',
                'C:\legacy\managed.m4b',
                'C:\LEGACY\MANAGED.M4B',
                'Managed',
                'C:\source\managed.m4b',
                'C:\SOURCE\MANAGED.M4B',
                200,
                '2025-01-02T00:00:00.0000000+00:00');
            """;
        command.ExecuteNonQuery();
    }

    private static void CreateMigrationFailureDatabase(string databasePath)
    {
        using var connection = OpenRawDatabase(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE VIEW library_books AS SELECT 1 AS invalid_column;
            """;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenRawDatabase(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }
}
