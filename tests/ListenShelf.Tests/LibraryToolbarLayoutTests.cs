using System.Xml.Linq;

namespace ListenShelf.Tests;

public sealed class LibraryToolbarLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void StatusMessage_WrapsWithoutTruncatingFeedback()
    {
        var status = FindStatus(LoadView());

        Assert.Equal("Wrap", (string?)status.Attribute("TextWrapping"));
        Assert.Null(status.Attribute("TextTrimming"));
        Assert.Null(status.Attribute("MaxLines"));
        Assert.Null(status.Attribute("Height"));
        Assert.Null(status.Attribute("MaxHeight"));
    }

    [Fact]
    public void StatusMessage_DoesNotCompeteWithFixedWidthToolbarControls()
    {
        // Guard the XAML layout contract. Compact-window rendering is also
        // checked in the running Windows app, not simulated by this test.
        var document = LoadView();
        var statusRow = FindStatus(document).Parent!;
        var toolbar = statusRow.Parent!;
        var tileButton = Assert.Single(document.Descendants(Avalonia + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding ShowLibraryAsTilesCommand}");
        var controlsRow = tileButton.Ancestors().Single(element => element.Parent == toolbar);

        Assert.NotSame(statusRow, controlsRow);
        Assert.NotEqual((string?)statusRow.Attribute("Grid.Row"), (string?)controlsRow.Attribute("Grid.Row"));
        Assert.Equal("Auto,*", (string?)statusRow.Attribute("ColumnDefinitions"));
    }

    private static XElement FindStatus(XDocument document) =>
        Assert.Single(document.Descendants(Avalonia + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding LibraryStatusMessage}");

    private static XDocument LoadView()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "ListenShelf.Desktop", "Views", "Sections", "LibraryView.axaml");
            if (File.Exists(path)) return XDocument.Load(path);
        }

        throw new FileNotFoundException("Could not find repository view LibraryView.axaml.");
    }
}
