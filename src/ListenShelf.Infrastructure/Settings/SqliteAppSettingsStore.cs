using ListenShelf.Application.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Infrastructure.Settings;

public sealed class SqliteAppSettingsStore(ListenShelfDatabase database) : IAppSettingsStore
{
    private const string ThemeKey = "appearance.theme";
    private const string LibraryViewModeKey = "library.view_mode";

    public AppTheme GetTheme()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT setting_value
            FROM app_settings
            WHERE setting_key = $setting_key;
            """;
        command.Parameters.AddWithValue("$setting_key", ThemeKey);

        var storedValue = command.ExecuteScalar() as string;
        return Enum.TryParse<AppTheme>(storedValue, ignoreCase: true, out var theme)
            && Enum.IsDefined(theme)
                ? theme
                : AppTheme.Dark;
    }

    public void SaveTheme(AppTheme theme)
    {
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
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
        command.Parameters.AddWithValue("$setting_key", ThemeKey);
        command.Parameters.AddWithValue("$setting_value", theme.ToString());
        command.ExecuteNonQuery();
    }

    public LibraryViewMode GetLibraryViewMode()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT setting_value
            FROM app_settings
            WHERE setting_key = $setting_key;
            """;
        command.Parameters.AddWithValue("$setting_key", LibraryViewModeKey);

        var storedValue = command.ExecuteScalar() as string;
        return Enum.TryParse<LibraryViewMode>(storedValue, ignoreCase: true, out var viewMode)
            && Enum.IsDefined(viewMode)
                ? viewMode
                : LibraryViewMode.List;
    }

    public void SaveLibraryViewMode(LibraryViewMode viewMode)
    {
        if (!Enum.IsDefined(viewMode))
        {
            throw new ArgumentOutOfRangeException(nameof(viewMode));
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
        command.Parameters.AddWithValue("$setting_key", LibraryViewModeKey);
        command.Parameters.AddWithValue("$setting_value", viewMode.ToString());
        command.ExecuteNonQuery();
    }
}
