using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class PlaybackSkipSettingsViewModelTests
{
    [Fact]
    public void ChangingIntervals_UpdatesLabelsAndPersistsAcrossLaunches()
    {
        using var workspace = new TestWorkspace();
        var store = new SqliteAppSettingsStore(new ListenShelfDatabase(workspace.DatabasePath));
        var viewModel = new PlaybackSkipSettingsViewModel(store);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        Assert.Equal("−15 sec", viewModel.RewindButtonText);
        Assert.Equal("+30 sec", viewModel.ForwardButtonText);
        viewModel.RewindSeconds = 12;
        viewModel.ForwardSeconds = 90;

        Assert.Equal("−12 sec", viewModel.RewindButtonText);
        Assert.Equal("+90 sec", viewModel.ForwardButtonText);
        Assert.Contains("Rewind 12 seconds", viewModel.RewindToolTip);
        Assert.Contains("Forward 90 seconds", viewModel.ForwardToolTip);
        Assert.Contains(nameof(viewModel.RewindButtonText), notifications);
        Assert.Contains(nameof(viewModel.ForwardButtonText), notifications);
        Assert.Contains(nameof(viewModel.RewindToolTip), notifications);
        Assert.Contains(nameof(viewModel.ForwardToolTip), notifications);

        var reopened = new PlaybackSkipSettingsViewModel(
            new SqliteAppSettingsStore(new ListenShelfDatabase(workspace.DatabasePath)));
        Assert.Equal(12m, reopened.RewindSeconds);
        Assert.Equal(90m, reopened.ForwardSeconds);
        Assert.Equal(12, reopened.EffectiveRewindSeconds);
        Assert.Equal(90, reopened.EffectiveForwardSeconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("601")]
    [InlineData("1.5")]
    public void InvalidInput_DoesNotChangeActiveOrSavedIntervals(string? input)
    {
        using var workspace = new TestWorkspace();
        var store = new SqliteAppSettingsStore(new ListenShelfDatabase(workspace.DatabasePath));
        var viewModel = new PlaybackSkipSettingsViewModel(store)
        {
            RewindSeconds = 20,
            ForwardSeconds = 40,
        };
        var invalidValue = input is null
            ? (decimal?)null
            : decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);

        viewModel.RewindSeconds = invalidValue;
        viewModel.ForwardSeconds = invalidValue;

        Assert.Equal(20, viewModel.EffectiveRewindSeconds);
        Assert.Equal(40, viewModel.EffectiveForwardSeconds);
        Assert.Equal(20, store.GetRewindSeconds());
        Assert.Equal(40, store.GetForwardSeconds());
        Assert.Contains("previous interval is still active", viewModel.RewindMessage);
        Assert.Contains("previous interval is still active", viewModel.ForwardMessage);

        viewModel.RewindSeconds = 1;
        viewModel.ForwardSeconds = 600;
        Assert.Equal(1, viewModel.EffectiveRewindSeconds);
        Assert.Equal(600, viewModel.EffectiveForwardSeconds);
        Assert.Equal("Saved for all audiobooks.", viewModel.RewindMessage);
        Assert.Equal("Saved for all audiobooks.", viewModel.ForwardMessage);
    }

    [Fact]
    public void Reload_AppliesRestoredPreferencesWithoutWritingBackToTheDatabase()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var store = new SqliteAppSettingsStore(database);
        var viewModel = new PlaybackSkipSettingsViewModel(store)
        {
            RewindSeconds = 5,
            ForwardSeconds = 10,
        };

        store.SaveRewindSeconds(25);
        store.SaveForwardSeconds(60);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TRIGGER reject_settings_update BEFORE UPDATE ON app_settings
            BEGIN SELECT RAISE(ABORT, 'Settings are read-only for this test'); END;
            """;
        command.ExecuteNonQuery();

        viewModel.Reload();

        Assert.Equal(25m, viewModel.RewindSeconds);
        Assert.Equal(60m, viewModel.ForwardSeconds);
        Assert.Equal("−25 sec", viewModel.RewindButtonText);
        Assert.Equal("+60 sec", viewModel.ForwardButtonText);
        Assert.Empty(viewModel.RewindMessage);
        Assert.Empty(viewModel.ForwardMessage);
    }

    [Fact]
    public void SaveFailure_KeepsSessionControlsConsistentAndShowsAnInlineWarning()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var store = new SqliteAppSettingsStore(database);
        var viewModel = new PlaybackSkipSettingsViewModel(store);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TRIGGER reject_settings_insert BEFORE INSERT ON app_settings
            BEGIN SELECT RAISE(ABORT, 'Settings are read-only for this test'); END;
            """;
        command.ExecuteNonQuery();

        viewModel.RewindSeconds = 7;
        viewModel.ForwardSeconds = 50;

        Assert.Equal(7, viewModel.EffectiveRewindSeconds);
        Assert.Equal(50, viewModel.EffectiveForwardSeconds);
        Assert.Equal("−7 sec", viewModel.RewindButtonText);
        Assert.Equal("+50 sec", viewModel.ForwardButtonText);
        Assert.Contains("could not be saved", viewModel.RewindMessage);
        Assert.Contains("could not be saved", viewModel.ForwardMessage);
        Assert.Equal(15, store.GetRewindSeconds());
        Assert.Equal(30, store.GetForwardSeconds());
    }
}
