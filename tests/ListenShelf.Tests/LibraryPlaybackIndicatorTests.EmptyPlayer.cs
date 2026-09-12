using System.Xml.Linq;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Tests;

public sealed partial class LibraryPlaybackIndicatorTests
{
    [Fact]
    public void EmptyPlayer_LibraryGuidanceAndNavigationDoNotStartPlayback()
    {
        using var session = new PlayerSession();
        session.Model.ShowPlayerCommand.Execute(null);

        Assert.False(session.Model.IsFileLoaded);
        Assert.Equal("Choose an audiobook from your library to begin listening.", session.Model.FileName);
        Assert.True(session.Model.ShowLibraryCommand.CanExecute(null));

        session.Model.ShowLibraryCommand.Execute(null);

        Assert.Equal(AppSection.Library, session.Model.SelectedSection);
        Assert.False(session.Model.IsFileLoaded);
        Assert.Equal(0, session.Engine.LoadCalls);
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Fact]
    public void EmptyPlayer_ViewOffersLibraryNavigationOnlyWithoutALoadedBook()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        var document = LoadEmptyPlayerView();
        var button = Assert.Single(document.Descendants(avalonia + "Button"), element =>
            (string?)element.Attribute("Content") == "Go to library");

        Assert.Equal("{Binding ShowLibraryCommand}", (string?)button.Attribute("Command"));
        Assert.Equal("{Binding !IsBusy}", (string?)button.Attribute("IsEnabled"));
        var panel = button.Parent!;
        Assert.Equal("{Binding !IsFileLoaded}", (string?)panel.Attribute("IsVisible"));
        Assert.Contains(panel.Ancestors(), element => element.Name == avalonia + "ScrollViewer");
        var guidance = Assert.Single(panel.Elements(avalonia + "TextBlock"));
        Assert.Equal("Wrap", (string?)guidance.Attribute("TextWrapping"));
        Assert.Null(guidance.Attribute("TextTrimming"));
    }

    private static XDocument LoadEmptyPlayerView()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "ListenShelf.Desktop", "Views", "Sections", "PlayerView.axaml");
            if (File.Exists(path)) return XDocument.Load(path);
        }

        throw new FileNotFoundException("Could not find repository view PlayerView.axaml.");
    }
}
