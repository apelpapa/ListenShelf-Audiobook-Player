using ListenShelf.Application.Settings;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Tests;

public sealed partial class LibraryPlaybackIndicatorTests
{
    [Fact]
    public void Sidebar_WithoutLoadedBookIsHiddenAndCannotControlPlayback()
    {
        using var session = new PlayerSession();
        session.Model.ErrorMessage = "No audiobook loaded";

        Assert.False(session.Model.HasSidebarPlayer);
        Assert.False(session.Model.HasSidebarPlayerError);
        Assert.False(session.Model.CanTogglePlayback);
        Assert.False(session.Model.TogglePlaybackCommand.CanExecute(null));
        session.Model.TogglePlaybackCommand.Execute(null);
        Assert.Equal(0, session.Engine.PlaybackChanges);
        Assert.Equal(0, session.Engine.LoadCalls);
    }

    [Theory]
    [InlineData(AppSection.Library, false)]
    [InlineData(AppSection.Library, true)]
    [InlineData(AppSection.Player, false)]
    [InlineData(AppSection.Player, true)]
    [InlineData(AppSection.Settings, false)]
    [InlineData(AppSection.Settings, true)]
    [InlineData(AppSection.StorageCare, false)]
    [InlineData(AppSection.StorageCare, true)]
    public async Task Sidebar_ToggleUsesSharedPlaybackWithoutReloadingOrChangingPage(AppSection section, bool playing)
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.SelectedSection = section;
        session.Model.IsPlaying = playing;
        session.Model.LibrarySearchText = "Beta";
        session.Model.SelectedLibraryView = LibraryViewMode.Tiles;
        session.Model.SelectedLibraryGroupOption = session.Model.LibraryGroupOptions.Single(option => option.Mode == LibraryGroupMode.Series);
        var group = session.Model.SelectedLibraryGroupOption;
        var progress = session.ProgressStore.Get(session.First.FilePath);
        var loads = session.Engine.LoadCalls;

        Assert.True(session.Model.HasSidebarPlayer);
        Assert.Equal("Alpha", session.Model.BookTitle);
        Assert.Equal(playing ? "Pause" : "Play", session.Model.PlayPauseLabel);
        Assert.True(session.Model.TogglePlaybackCommand.CanExecute(null));
        Assert.Equal(0, session.Engine.PlaybackChanges);

        session.Model.TogglePlaybackCommand.Execute(null);

        Assert.Equal(playing ? 0 : 1, session.Engine.PlayCalls);
        Assert.Equal(playing ? 1 : 0, session.Engine.PauseCalls);
        Assert.Equal(1, session.Engine.PlaybackChanges);
        Assert.Equal(loads, session.Engine.LoadCalls);
        Assert.Equal(section, session.Model.SelectedSection);
        Assert.Equal("Beta", session.Model.LibrarySearchText);
        Assert.Equal(LibraryViewMode.Tiles, session.Model.SelectedLibraryView);
        Assert.Same(group, session.Model.SelectedLibraryGroupOption);
        Assert.Equal(progress, session.ProgressStore.Get(session.First.FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sidebar_TitleNavigationDoesNotStartOrPauseAudio(bool playing)
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.ShowSettingsCommand.Execute(null);
        session.Model.IsPlaying = playing;
        var loads = session.Engine.LoadCalls;

        session.Model.ShowPlayerCommand.Execute(null);

        Assert.Equal(AppSection.Player, session.Model.SelectedSection);
        Assert.True(session.Model.HasSidebarPlayer);
        Assert.Equal(playing, session.Model.IsPlaying);
        Assert.Equal(0, session.Engine.PlaybackChanges);
        Assert.Equal(loads, session.Engine.LoadCalls);
    }

    [Fact]
    public async Task Sidebar_BusyStateDisablesPlaybackAndNotifiesBindings()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        var notifications = new HashSet<string?>();
        var commandChanges = 0;
        session.Model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        session.Model.TogglePlaybackCommand.CanExecuteChanged += (_, _) => commandChanges++;

        session.Model.IsBusy = true;

        Assert.True(session.Model.HasSidebarPlayer);
        Assert.False(session.Model.CanTogglePlayback);
        Assert.False(session.Model.TogglePlaybackCommand.CanExecute(null));
        session.Model.TogglePlaybackCommand.Execute(null);
        Assert.Equal(0, session.Engine.PlaybackChanges);
        Assert.Contains(nameof(MainWindowViewModel.CanTogglePlayback), notifications);
        Assert.Equal(1, commandChanges);

        session.Model.IsBusy = false;
        Assert.True(session.Model.TogglePlaybackCommand.CanExecute(null));
        Assert.Equal(2, commandChanges);
    }

    [Fact]
    public async Task Sidebar_UsesLiveTitleStatusAndPlaybackLabelWithoutControllingAudio()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        var notifications = new HashSet<string?>();
        session.Model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        session.Model.BookTitle = "Updated title";
        session.Model.StatusText = "Playing";
        session.Model.IsPlaying = true;

        Assert.Equal("Updated title", session.Model.BookTitle);
        Assert.Equal("Playing", session.Model.StatusText);
        Assert.Equal("Pause", session.Model.PlayPauseLabel);
        Assert.Contains(nameof(MainWindowViewModel.BookTitle), notifications);
        Assert.Contains(nameof(MainWindowViewModel.StatusText), notifications);
        Assert.Contains(nameof(MainWindowViewModel.PlayPauseLabel), notifications);

        session.Model.IsPlaying = false;
        Assert.Equal("Play", session.Model.PlayPauseLabel);
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Fact]
    public async Task Sidebar_SwitchingBooksHidesOldPanelUntilNewBookIsLoaded()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.ErrorMessage = "Previous error";
        session.Engine.DuringLoad = () =>
        {
            Assert.False(session.Model.HasSidebarPlayer);
            Assert.False(session.Model.HasSidebarPlayerError);
            Assert.False(session.Model.TogglePlaybackCommand.CanExecute(null));
        };

        await session.LoadAsync(session.Second);

        Assert.True(session.Model.HasSidebarPlayer);
        Assert.False(session.Model.HasSidebarPlayerError);
        Assert.Equal("Beta", session.Model.BookTitle);
        Assert.True(session.Model.TogglePlaybackCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sidebar_FailedLoadDoesNotLeaveAnActionableOldPanel()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Engine.FailLoad = true;

        await session.Item(session.Second).PlayCommand.ExecuteAsync(null);

        Assert.False(session.Model.HasSidebarPlayer);
        Assert.False(session.Model.HasSidebarPlayerError);
        Assert.False(session.Model.TogglePlaybackCommand.CanExecute(null));
        Assert.Contains("Test load failure", session.Model.ErrorMessage);
    }

    [Fact]
    public async Task Sidebar_RemovingCurrentBookHidesPanelAndDisablesPlayback()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);

        await session.Item(session.First).RemoveBookCommand.ExecuteAsync(null);

        Assert.False(session.Model.HasSidebarPlayer);
        Assert.False(session.Model.TogglePlaybackCommand.CanExecute(null));
        var changes = session.Engine.PlaybackChanges;
        session.Model.TogglePlaybackCommand.Execute(null);
        Assert.Equal(changes, session.Engine.PlaybackChanges);
    }

    [Fact]
    public async Task Sidebar_RejectedPlaybackDisplaysErrorAndAllowsRetry()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.SelectedSection = AppSection.Library;
        session.Engine.RejectPlay = true;
        var notifications = new HashSet<string?>();
        session.Model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        session.Model.TogglePlaybackCommand.Execute(null);

        Assert.True(session.Model.HasSidebarPlayerError);
        Assert.Equal("Playback could not be started.", session.Model.ErrorMessage);
        Assert.False(session.Model.IsPlaying);
        Assert.Contains(nameof(MainWindowViewModel.HasSidebarPlayerError), notifications);

        session.Engine.RejectPlay = false;
        session.Model.TogglePlaybackCommand.Execute(null);

        Assert.False(session.Model.HasSidebarPlayerError);
        Assert.Empty(session.Model.ErrorMessage);
        Assert.Equal(AppSection.Library, session.Model.SelectedSection);
        Assert.Equal(2, session.Engine.PlayCalls);
    }

    [Theory]
    [InlineData(false, "Test play failure")]
    [InlineData(true, "Test pause failure")]
    public async Task Sidebar_PlaybackExceptionsAreShownInlineAndCanBeRetried(bool playing, string error)
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.IsPlaying = playing;
        session.Model.SelectedSection = AppSection.Settings;
        session.Engine.ThrowOnPlay = !playing;
        session.Engine.ThrowOnPause = playing;

        session.Model.TogglePlaybackCommand.Execute(null);

        Assert.True(session.Model.HasSidebarPlayerError);
        Assert.Contains(error, session.Model.ErrorMessage);
        Assert.Equal(playing, session.Model.IsPlaying);
        Assert.True(session.Model.TogglePlaybackCommand.CanExecute(null));

        session.Engine.ThrowOnPlay = false;
        session.Engine.ThrowOnPause = false;
        session.Model.TogglePlaybackCommand.Execute(null);

        Assert.False(session.Model.HasSidebarPlayerError);
        Assert.Empty(session.Model.ErrorMessage);
        Assert.Equal(AppSection.Settings, session.Model.SelectedSection);
        Assert.Equal(2, session.Engine.PlaybackChanges);
    }

    [Fact]
    public async Task Sidebar_DisposalHidesPanelAndDisablesSharedCommand()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.ErrorMessage = "Previous error";
        var notifications = new HashSet<string?>();
        var commandChanges = 0;
        session.Model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        session.Model.TogglePlaybackCommand.CanExecuteChanged += (_, _) => commandChanges++;

        session.Model.Dispose();

        Assert.False(session.Model.HasSidebarPlayer);
        Assert.False(session.Model.HasSidebarPlayerError);
        Assert.False(session.Model.CanTogglePlayback);
        Assert.False(session.Model.TogglePlaybackCommand.CanExecute(null));
        session.Model.TogglePlaybackCommand.Execute(null);
        Assert.Equal(0, session.Engine.PlaybackChanges);
        Assert.Contains(nameof(MainWindowViewModel.HasSidebarPlayer), notifications);
        Assert.Contains(nameof(MainWindowViewModel.HasSidebarPlayerError), notifications);
        Assert.Contains(nameof(MainWindowViewModel.CanTogglePlayback), notifications);
        Assert.True(commandChanges > 0);
    }
}
