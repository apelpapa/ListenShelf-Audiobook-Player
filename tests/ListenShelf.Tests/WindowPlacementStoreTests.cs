using ListenShelf.Application.Settings;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace ListenShelf.Tests;

public sealed class WindowPlacementStoreTests
{
    [Fact]
    public void MissingPreference_IsReadOnlyAndDoesNotCreateDefaultRows()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        Assert.Null(new SqliteAppSettingsStore(database).GetWindowPlacement());
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM app_settings WHERE setting_key = 'window.placement';";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletePlacement_RoundTripsAsOneSetting(bool maximized)
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var saved = new WindowPlacement(-1720, 80, 1280.5, 760.25, maximized);
        new SqliteAppSettingsStore(database).SaveWindowPlacement(saved);
        var reopened = new SqliteAppSettingsStore(new ListenShelfDatabase(workspace.DatabasePath));
        Assert.Equal(saved, reopened.GetWindowPlacement());
        var updated = saved with { X = 100, IsMaximized = !maximized };
        reopened.SaveWindowPlacement(updated);
        Assert.Equal(updated, reopened.GetWindowPlacement());
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM app_settings WHERE setting_key = 'window.placement';";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Version\":1}")]
    [InlineData("{\"Version\":2,\"Placement\":{\"X\":100,\"Y\":100,\"Width\":1200,\"Height\":760,\"IsMaximized\":true}}")]
    [InlineData("{\"Version\":1,\"Placement\":{\"Width\":0,\"Height\":760}}")]
    [InlineData("{\"Version\":1,\"Placement\":{\"Width\":1200,\"Height\":-760}}")]
    [InlineData("{\"Version\":1,\"Placement\":{\"X\":99999999999,\"Width\":1200,\"Height\":760}}")]
    public void InvalidOrNewerPreference_IsIgnoredWithoutRewritingIt(string json)
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO app_settings VALUES ('window.placement', $value);";
        command.Parameters.AddWithValue("$value", json);
        command.ExecuteNonQuery();
        Assert.Null(new SqliteAppSettingsStore(database).GetWindowPlacement());
        command.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key = 'window.placement';";
        Assert.Equal(json, command.ExecuteScalar());
    }

    [Theory]
    [InlineData(double.NaN, 720)]
    [InlineData(1180, double.PositiveInfinity)]
    [InlineData(-1, 720)]
    [InlineData(1180, 0)]
    [InlineData(100001, 720)]
    public void InvalidSave_DoesNotOverwriteLastGoodPlacement(double width, double height)
    {
        using var workspace = new TestWorkspace();
        var store = new SqliteAppSettingsStore(new ListenShelfDatabase(workspace.DatabasePath));
        var saved = new WindowPlacement(100, 100, 1180, 720, true);
        store.SaveWindowPlacement(saved);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.SaveWindowPlacement(saved with { Width = width, Height = height }));
        Assert.Equal(saved, store.GetWindowPlacement());
    }

    [Fact]
    public void FailedWrite_PreservesEntirePreviousSnapshot()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var store = new SqliteAppSettingsStore(database);
        var saved = new WindowPlacement(100, 100, 1180, 720, true);
        store.SaveWindowPlacement(saved);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER reject_placement BEFORE UPDATE ON app_settings BEGIN SELECT RAISE(ABORT, 'Read-only test'); END;";
        command.ExecuteNonQuery();
        Assert.Throws<SqliteException>(() => store.SaveWindowPlacement(new WindowPlacement(400, 300, 1400, 900, false)));
        Assert.Equal(saved, store.GetWindowPlacement());
    }
}
