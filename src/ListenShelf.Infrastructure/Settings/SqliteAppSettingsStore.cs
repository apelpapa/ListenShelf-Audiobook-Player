using ListenShelf.Application.Settings;
using ListenShelf.Infrastructure.Storage;
using System.Globalization;

namespace ListenShelf.Infrastructure.Settings;

public sealed class SqliteAppSettingsStore(ListenShelfDatabase database) : IAppSettingsStore
{
    private const string ThemeKey = "appearance.theme";
    private const string LibraryViewModeKey = "library.view_mode";
    private const string LibraryGroupModeKey = "library.group_mode";
    private const string LibrarySortModeKey = "library.sort_mode";
    private const string LibrarySortReversedKey = "library.sort_reversed";
    private const string LibraryStatusFilterKey = "library.status_filter";
    private const string LibraryTileWidthKey = "library.tile_width";
    private const string PlaybackVolumeKey = "player.volume";
    private const string PlaybackRateKey = "player.playback_rate";
    private const string RewindSecondsKey = "player.rewind_seconds";
    private const string ForwardSecondsKey = "player.forward_seconds";
    private const string LastSleepTimerMinutesKey = "player.last_sleep_timer_minutes";
    private const double DefaultLibraryTileWidth = 220d;
    private const double MinimumLibraryTileWidth = 180d;
    private const double MaximumLibraryTileWidth = 320d;
    private const double DefaultPlaybackVolume = 80d;
    private const double MinimumPlaybackVolume = 0d;
    private const double MaximumPlaybackVolume = 100d;
    private const double DefaultPlaybackRate = 1d;
    private const double MinimumPlaybackRate = 0.5d;
    private const double MaximumPlaybackRate = 3d;

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

    public LibraryGroupMode GetLibraryGroupMode()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT setting_value
            FROM app_settings
            WHERE setting_key = $setting_key;
            """;
        command.Parameters.AddWithValue("$setting_key", LibraryGroupModeKey);

        var storedValue = command.ExecuteScalar() as string;
        return Enum.TryParse<LibraryGroupMode>(storedValue, ignoreCase: true, out var groupMode)
            && Enum.IsDefined(groupMode)
                ? groupMode
                : LibraryGroupMode.None;
    }

    public void SaveLibraryGroupMode(LibraryGroupMode groupMode)
    {
        if (!Enum.IsDefined(groupMode))
        {
            throw new ArgumentOutOfRangeException(nameof(groupMode));
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
        command.Parameters.AddWithValue("$setting_key", LibraryGroupModeKey);
        command.Parameters.AddWithValue("$setting_value", groupMode.ToString());
        command.ExecuteNonQuery();
    }

    public LibrarySortMode GetLibrarySortMode() =>
        GetEnumSetting(LibrarySortModeKey, LibrarySortMode.Title);

    public void SaveLibrarySortMode(LibrarySortMode sortMode) =>
        SaveEnumSetting(LibrarySortModeKey, sortMode);

    public bool GetLibrarySortReversed()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", LibrarySortReversedKey);
        return bool.TryParse(command.ExecuteScalar() as string, out var reversed) && reversed;
    }

    public void SaveLibrarySortReversed(bool reversed)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO app_settings (setting_key, setting_value) VALUES ($key, $value)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value;
            """;
        command.Parameters.AddWithValue("$key", LibrarySortReversedKey);
        command.Parameters.AddWithValue("$value", reversed.ToString());
        command.ExecuteNonQuery();
    }

    public LibraryStatusFilter GetLibraryStatusFilter() =>
        GetEnumSetting(LibraryStatusFilterKey, LibraryStatusFilter.All);

    public void SaveLibraryStatusFilter(LibraryStatusFilter statusFilter) =>
        SaveEnumSetting(LibraryStatusFilterKey, statusFilter);

    private T GetEnumSetting<T>(string key, T defaultValue) where T : struct, Enum
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return Enum.TryParse<T>(command.ExecuteScalar() as string, ignoreCase: true, out var value)
            && Enum.IsDefined(value) ? value : defaultValue;
    }

    private void SaveEnumSetting<T>(string key, T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO app_settings (setting_key, setting_value) VALUES ($key, $value)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value.ToString());
        command.ExecuteNonQuery();
    }

    public double GetLibraryTileWidth() =>
        GetDoubleSetting(
            LibraryTileWidthKey,
            DefaultLibraryTileWidth,
            MinimumLibraryTileWidth,
            MaximumLibraryTileWidth);

    public void SaveLibraryTileWidth(double tileWidth) =>
        SaveDoubleSetting(
            LibraryTileWidthKey,
            tileWidth,
            MinimumLibraryTileWidth,
            MaximumLibraryTileWidth,
            nameof(tileWidth));

    public double GetPlaybackVolume() =>
        GetDoubleSetting(
            PlaybackVolumeKey,
            DefaultPlaybackVolume,
            MinimumPlaybackVolume,
            MaximumPlaybackVolume);

    public void SavePlaybackVolume(double volume) =>
        SaveDoubleSetting(
            PlaybackVolumeKey,
            volume,
            MinimumPlaybackVolume,
            MaximumPlaybackVolume,
            nameof(volume));

    public double GetPlaybackRate() =>
        GetDoubleSetting(
            PlaybackRateKey,
            DefaultPlaybackRate,
            MinimumPlaybackRate,
            MaximumPlaybackRate);

    public void SavePlaybackRate(double rate) =>
        SaveDoubleSetting(
            PlaybackRateKey,
            rate,
            MinimumPlaybackRate,
            MaximumPlaybackRate,
            nameof(rate));

    public int GetRewindSeconds() =>
        GetSkipInterval(RewindSecondsKey, PlaybackSkipIntervals.DefaultRewindSeconds);

    public void SaveRewindSeconds(int seconds) => SaveSkipInterval(RewindSecondsKey, seconds);

    public int GetForwardSeconds() =>
        GetSkipInterval(ForwardSecondsKey, PlaybackSkipIntervals.DefaultForwardSeconds);

    public void SaveForwardSeconds(int seconds) => SaveSkipInterval(ForwardSecondsKey, seconds);

    public int? GetLastSleepTimerMinutes()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", LastSleepTimerMinutesKey);
        return int.TryParse(command.ExecuteScalar() as string, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var minutes) && SleepTimerDurations.IsValid(minutes)
            ? minutes : null;
    }

    public void SaveLastSleepTimerMinutes(int minutes) =>
        SaveDoubleSetting(LastSleepTimerMinutesKey, minutes,
            SleepTimerDurations.MinimumMinutes, SleepTimerDurations.MaximumMinutes, nameof(minutes));

    private int GetSkipInterval(string key, int defaultValue)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT setting_value
            FROM app_settings
            WHERE setting_key = $setting_key;
            """;
        command.Parameters.AddWithValue("$setting_key", key);

        return int.TryParse(
            command.ExecuteScalar() as string,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var seconds) && PlaybackSkipIntervals.IsValid(seconds)
                ? seconds
                : defaultValue;
    }

    private void SaveSkipInterval(string key, int seconds) =>
        SaveDoubleSetting(
            key,
            seconds,
            PlaybackSkipIntervals.MinimumSeconds,
            PlaybackSkipIntervals.MaximumSeconds,
            nameof(seconds));

    private double GetDoubleSetting(
        string key,
        double defaultValue,
        double minimumValue,
        double maximumValue)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT setting_value
            FROM app_settings
            WHERE setting_key = $setting_key;
            """;
        command.Parameters.AddWithValue("$setting_key", key);

        var storedValue = command.ExecuteScalar() as string;
        return double.TryParse(
            storedValue,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value)
            && double.IsFinite(value)
                ? Math.Clamp(value, minimumValue, maximumValue)
                : defaultValue;
    }

    private void SaveDoubleSetting(
        string key,
        double value,
        double minimumValue,
        double maximumValue,
        string parameterName)
    {
        if (!double.IsFinite(value) || value < minimumValue || value > maximumValue)
        {
            throw new ArgumentOutOfRangeException(parameterName);
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
        command.Parameters.AddWithValue("$setting_key", key);
        command.Parameters.AddWithValue(
            "$setting_value",
            value.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
