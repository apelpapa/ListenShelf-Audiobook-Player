using System.Xml.Linq;
using ListenShelf.Application.Library;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Tests;

public sealed class FullTextToolTipTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Theory]
    [InlineData("Components/LibraryBookListCard.axaml", "Title")]
    [InlineData("Components/LibraryBookListCard.axaml", "AuthorText")]
    [InlineData("Components/LibraryBookListCard.axaml", "SeriesText")]
    [InlineData("Components/LibraryBookTileCard.axaml", "Title")]
    [InlineData("Components/LibraryBookTileCard.axaml", "AuthorText")]
    [InlineData("Components/LibraryBookTileCard.axaml", "SeriesText")]
    [InlineData("Components/LibraryGroupStackHeader.axaml", "Name")]
    [InlineData("Components/LibraryGroupStackHeader.axaml", "PreviewTitle")]
    [InlineData("Components/LibraryGroupStackTile.axaml", "Name")]
    [InlineData("Components/LibraryGroupDetailHeader.axaml", "ActiveLibraryGroupName")]
    [InlineData("Sections/PlayerView.axaml", "BookTitle")]
    [InlineData("Sections/PlayerView.axaml", "FileName")]
    public void TruncatedLabels_BindWrappedTooltipsToTheSameFullText(string relativePath, string property)
    {
        // Verify the declarative view wiring; the normal app build also compiles
        // these typed bindings. This is not a simulated pointer/hover test.
        var document = LoadView(relativePath);
        var binding = $"{{Binding {property}}}";
        var label = Assert.Single(document.Descendants(Avalonia + "TextBlock"), element =>
            (string?)element.Attribute("Text") == binding && element.Attribute("TextTrimming") is not null);
        var tooltip = Assert.Single(label.Elements(Avalonia + "ToolTip.Tip").Elements(Avalonia + "TextBlock"));

        Assert.Equal(binding, (string?)tooltip.Attribute("Text"));
        AssertWrappedAndUntrimmed(tooltip);
        if (property == "SeriesText") Assert.Equal("{Binding HasSeries}", (string?)label.Attribute("IsVisible"));
    }

    [Fact]
    public void SidebarTitle_KeepsNavigationHintWithWrappedFullTitle()
    {
        var document = LoadView("Components/SidebarPlayerPanel.axaml");
        var button = Assert.Single(document.Descendants(Avalonia + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding ShowPlayerCommand}");
        var tooltip = Assert.Single(button.Elements(Avalonia + "ToolTip.Tip").Elements(Avalonia + "TextBlock"));

        Assert.Equal("{Binding BookTitle, StringFormat='Open Player: {0}'}", (string?)tooltip.Attribute("Text"));
        AssertWrappedAndUntrimmed(tooltip);
    }

    [Fact]
    public void TooltipSourceValues_PreserveLongUnicodeMetadataAndAllAuthors()
    {
        var title = string.Concat(Enumerable.Repeat("A long audiobook title — 日本語 & <text> ", 10));
        var authors = new[] { "Élodie " + new string('A', 100), "Владимир " + new string('B', 100) };
        var series = "An extended series name " + new string('S', 200);
        var book = new LibraryBook(Guid.NewGuid(), new AudiobookMetadata
        {
            Title = title,
            Authors = authors,
            SeriesName = series,
            SeriesPosition = "2.5",
        }, "unused.m4b", 0, DateTimeOffset.UtcNow);
        using var item = new LibraryBookItemViewModel(book, null, 180,
            UnexpectedAction, UnexpectedAction, UnexpectedAction, UnexpectedAction);
        var group = new LibraryGroupViewModel(series, [item], showHeader: true);

        Assert.Equal(title, item.Title);
        Assert.Equal(string.Join(", ", authors), item.AuthorText);
        Assert.Equal($"{series} · Book 2.5", item.SeriesText);
        Assert.Equal(series, group.Name);
        Assert.Equal(title, group.PreviewTitle);
        Assert.Equal(180, item.TileWidth);

        static Task UnexpectedAction(LibraryBook _) => throw new InvalidOperationException("Reading tooltip text must not invoke an action.");
    }

    private static void AssertWrappedAndUntrimmed(XElement tooltip)
    {
        Assert.Equal("420", (string?)tooltip.Attribute("MaxWidth"));
        Assert.Equal("Wrap", (string?)tooltip.Attribute("TextWrapping"));
        Assert.Null(tooltip.Attribute("TextTrimming"));
        Assert.Null(tooltip.Attribute("Height"));
        Assert.Null(tooltip.Attribute("MaxHeight"));
        Assert.Null(tooltip.Attribute("MaxLines"));
    }

    private static XDocument LoadView(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "ListenShelf.Desktop", "Views", relativePath);
            if (File.Exists(path)) return XDocument.Load(path);
        }

        throw new FileNotFoundException($"Could not find repository view {relativePath}.");
    }
}
