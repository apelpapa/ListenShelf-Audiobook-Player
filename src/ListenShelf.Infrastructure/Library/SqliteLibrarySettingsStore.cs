using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Infrastructure.Library;

public sealed class SqliteLibrarySettingsStore(ListenShelfDatabase database) : ILibrarySettingsStore
{
    private const string DefaultStorageModeKey = "library.default_storage_mode";

    public LibraryStorageMode? GetDefaultStorageMode()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT setting_value
            FROM app_settings
            WHERE setting_key = $setting_key;
            """;
        command.Parameters.AddWithValue("$setting_key", DefaultStorageModeKey);

        var storedValue = command.ExecuteScalar() as string;
        return Enum.TryParse<LibraryStorageMode>(storedValue, ignoreCase: true, out var storageMode)
            ? storageMode
            : null;
    }

    public void SaveDefaultStorageMode(LibraryStorageMode storageMode)
    {
        if (!Enum.IsDefined(storageMode))
        {
            throw new ArgumentOutOfRangeException(nameof(storageMode));
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES ($setting_key, $setting_value)
            ON CONFLICT(setting_key) DO UPDATE SET
                setting_value = excluded.setting_value;
            """;
        command.Parameters.AddWithValue("$setting_key", DefaultStorageModeKey);
        command.Parameters.AddWithValue("$setting_value", storageMode.ToString());
        command.ExecuteNonQuery();
    }
}
