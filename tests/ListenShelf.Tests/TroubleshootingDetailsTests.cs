using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Avalonia;
using LibVLCSharp.Shared;
using ListenShelf.Desktop.Diagnostics;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Tests;

public sealed class TroubleshootingDetailsTests
{
    private static TroubleshootingInfo Example => new(
        "0.2.0-alpha.2+private-build-machine",
        TroubleshootingPlatform.Windows, new Version(10, 0, 26200, 1),
        Architecture.Arm64, Architecture.X64, new Version(10, 0, 10),
        "12.0.5+build", "3.10.0+build", "3.0.23 Vetinari");

    [Fact]
    public void Report_ContainsOnlyTheFixedVersionAndArchitectureFields()
    {
        Assert.Equal(string.Join(Environment.NewLine,
            "ListenShelf troubleshooting details",
            "App version: 0.2.0-alpha.2",
            "Operating system: Windows (build 10.0.26200.1)",
            "OS architecture: Arm64",
            "App architecture: X64",
            ".NET runtime: 10.0.10",
            "Avalonia: 12.0.5",
            "LibVLCSharp: 3.10.0",
            "LibVLC runtime: 3.0.23"), Example.ToReport());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Users\\Reader\\Private Book.m4b")]
    [InlineData("/home/reader/Private Book.mp3")]
    [InlineData("\\\\server\\share\\book.m4a")]
    [InlineData("https://example.com/private")]
    [InlineData("User: reader\nBook: Private Book")]
    [InlineData("3.10.0\nPrivate Book")]
    [InlineData("3.10.0\u0000Private Book")]
    [InlineData("3.10.0/Private Book")]
    public void ManagedVersionFields_RejectPathsAndUnexpectedText(string? input)
    {
        var report = (Example with { AppVersion = input!, AvaloniaVersion = input!, LibVlcSharpVersion = input! }).ToReport();
        Assert.Contains("App version: Unavailable", report);
        Assert.Contains("Avalonia: Unavailable", report);
        Assert.Contains("LibVLCSharp: Unavailable", report);
        Assert.DoesNotContain("Private Book", report);
        Assert.DoesNotContain("reader", report);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.4", "1.2.3-beta.4")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData(" 1.2.3-rc.1+build-path-C:\\Users\\Reader ", "1.2.3-rc.1")]
    public void ManagedVersionFields_KeepReleaseLabelsButDropBuildMetadata(string input, string expected)
    {
        var report = (Example with { AppVersion = input }).ToReport();
        Assert.Contains($"App version: {expected}{Environment.NewLine}", report);
        Assert.DoesNotContain("Reader", report);
    }

    [Theory]
    [InlineData("3.0.23 Vetinari", "3.0.23")]
    [InlineData("3.0.23", "3.0.23")]
    [InlineData("3.0.23-custom C:\\Users\\Reader\\Private Book.m4b", "3.0.23")]
    [InlineData("3.0.23\nPrivate Book", "3.0.23")]
    [InlineData("3.0.23/private/path", "Unavailable")]
    [InlineData("C:\\Users\\Reader\\Private Book.m4b", "Unavailable")]
    [InlineData(null, "Unavailable")]
    public void NativeVersion_CopiesOnlyNumericPrefix(string? input, string expected)
    {
        var report = (Example with { LibVlcVersion = input }).ToReport();
        Assert.EndsWith($"LibVLC runtime: {expected}", report);
        Assert.DoesNotContain("Reader", report);
        Assert.DoesNotContain("Private Book", report);
    }

    [Fact]
    public void OversizedVersionsAndUnknownEnums_UseBoundedFallbacks()
    {
        var huge = "3.0.23-" + new string('x', 300);
        var report = (Example with
        {
            AppVersion = huge,
            AvaloniaVersion = huge,
            LibVlcSharpVersion = huge,
            LibVlcVersion = huge,
            Platform = (TroubleshootingPlatform)999,
            OsArchitecture = (Architecture)999,
            ProcessArchitecture = (Architecture)999,
        }).ToReport();
        Assert.DoesNotContain(huge, report);
        Assert.Contains("Operating system: Other", report);
        Assert.Contains("OS architecture: Unavailable", report);
        Assert.Contains("App architecture: Unavailable", report);
        Assert.EndsWith("LibVLC runtime: Unavailable", report);
    }

    [Fact]
    public void Capture_ReadsRunningAssembliesAndRuntimeAndCallsProvidedVersionReaderOnce()
    {
        var calls = 0;
        var info = TroubleshootingInfo.Capture(() => { calls++; return "3.0.23 Vetinari"; });
        Assert.Equal(1, calls);
        Assert.Equal(ApplicationAboutInfo.Current.Version, info.AppVersion);
        Assert.Equal(Environment.OSVersion.Version, info.OsVersion);
        Assert.Equal(Environment.Version, info.DotNetVersion);
        Assert.Equal(RuntimeInformation.OSArchitecture, info.OsArchitecture);
        Assert.Equal(RuntimeInformation.ProcessArchitecture, info.ProcessArchitecture);
        Assert.Equal(typeof(AvaloniaObject).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, info.AvaloniaVersion);
        Assert.Equal(typeof(LibVLC).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, info.LibVlcSharpVersion);
        Assert.EndsWith("LibVLC runtime: 3.0.23", info.ToReport());
        if (OperatingSystem.IsWindows()) Assert.Equal(TroubleshootingPlatform.Windows, info.Platform);
    }

    [Fact]
    public void CaptureWithoutEngine_DoesNotInitializeNativePlaybackToObtainVersion()
    {
        var info = TroubleshootingInfo.Capture();
        Assert.Null(info.LibVlcVersion);
        Assert.EndsWith("LibVLC runtime: Unavailable", info.ToReport());
    }

    [Fact]
    public void FailedNativeVersionRead_KeepsOtherDetailsWithoutExceptionData()
    {
        var info = TroubleshootingInfo.Capture(() => throw new IOException("C:\\Users\\Reader\\Private Book.m4b"));
        Assert.Null(info.LibVlcVersion);
        Assert.DoesNotContain("Reader", info.ToReport());
        Assert.DoesNotContain("Private Book", info.ToReport());
        Assert.Contains($".NET runtime: {Environment.Version}", info.ToReport());
    }

    [Fact]
    public async Task AboutPreviewAndCopy_UseIdenticalTextAndNeverOpenBrowser()
    {
        var clipboard = new TestClipboardService();
        var model = new AboutViewModel(new UnexpectedBrowser(), clipboard, Example);
        Assert.Empty(clipboard.Writes);
        Assert.False(model.HasCopyStatus);
        Assert.True(model.CopyTroubleshootingDetailsCommand.CanExecute(null));
        model.IsTroubleshootingPreviewExpanded = true;
        Assert.Equal(Example.ToReport(), model.TroubleshootingDetails);
        Assert.Empty(clipboard.Writes);

        await model.CopyTroubleshootingDetailsCommand.ExecuteAsync(null);

        Assert.Equal(model.TroubleshootingDetails, Assert.Single(clipboard.Writes));
        Assert.Contains("details copied", model.CopyStatus);
        Assert.True(model.HasCopyStatus);
        Assert.False(model.IsCopyingDetails);
        Assert.True(model.CanCopyTroubleshootingDetails);
        Assert.False(model.HasLinkError);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("false")]
    [InlineData("throw")]
    public async Task FailedCopy_ExpandsSelectablePreviewAndDoesNotExposeException(string failure)
    {
        var clipboard = failure == "missing" ? null : new TestClipboardService { Result = false, Throw = failure == "throw" };
        var model = new AboutViewModel(new UnexpectedBrowser(), clipboard, Example);

        await model.CopyTroubleshootingDetailsCommand.ExecuteAsync(null);

        Assert.True(model.IsTroubleshootingPreviewExpanded);
        Assert.Contains("Select and copy the details below", model.CopyStatus);
        Assert.DoesNotContain("Private Book", model.CopyStatus);
        Assert.DoesNotContain("Reader", model.CopyStatus);
        Assert.Equal(Example.ToReport(), model.TroubleshootingDetails);
        Assert.True(model.CopyTroubleshootingDetailsCommand.CanExecute(null));
        Assert.False(model.IsCopyingDetails);
        Assert.False(model.HasLinkError);
    }

    [Fact]
    public async Task Retry_ClearsFailureStatusAndNotifiesBoundControls()
    {
        var clipboard = new TestClipboardService { Result = false };
        var model = new AboutViewModel(null, clipboard, Example);
        await model.CopyTroubleshootingDetailsCommand.ExecuteAsync(null);
        var notifications = new HashSet<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        clipboard.Result = true;

        await model.CopyTroubleshootingDetailsCommand.ExecuteAsync(null);

        Assert.Equal(2, clipboard.Writes.Count);
        Assert.Contains("details copied", model.CopyStatus);
        Assert.DoesNotContain("Could not", model.CopyStatus);
        Assert.Contains(nameof(AboutViewModel.HasCopyStatus), notifications);
        Assert.Contains(nameof(AboutViewModel.CanCopyTroubleshootingDetails), notifications);
        Assert.Contains(nameof(AboutViewModel.IsCopyingDetails), notifications);
    }

    [Fact]
    public async Task PendingCopy_DisablesButtonAndRejectsDuplicateDirectExecution()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new TestClipboardService { Pending = pending.Task };
        var model = new AboutViewModel(null, clipboard, Example);

        var first = model.CopyTroubleshootingDetailsCommand.ExecuteAsync(null);
        Assert.True(model.IsCopyingDetails);
        Assert.False(model.CopyTroubleshootingDetailsCommand.CanExecute(null));
        Assert.False(model.HasCopyStatus);
        await model.CopyTroubleshootingDetailsCommand.ExecuteAsync(null);
        Assert.Single(clipboard.Writes);
        Assert.True(model.CanOpenLinks);
        pending.SetResult(true);
        await first;

        Assert.False(model.IsCopyingDetails);
        Assert.True(model.CopyTroubleshootingDetailsCommand.CanExecute(null));
        Assert.Contains("details copied", model.CopyStatus);
    }

    [Fact]
    public async Task ClipboardAdapter_MissingPlatformClipboardReturnsFalse()
    {
        var service = new AvaloniaClipboardTextService(() => null);
        Assert.False(await service.SetTextAsync(Example.ToReport()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ClipboardAdapter_RejectsEmptyTextWithoutClearingClipboard(string? value)
    {
        var service = new AvaloniaClipboardTextService(() => throw new InvalidOperationException("Must not access clipboard"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.SetTextAsync(value!));
    }

    [Fact]
    public void AboutView_WiresExplicitCopyAndSelectableWrappingPreview()
    {
        // Declarative wiring check, not a native clipboard or rendered UI test.
        var document = LoadAboutView();
        XNamespace ns = "https://github.com/avaloniaui";
        Assert.Single(document.Descendants(ns + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding CopyTroubleshootingDetailsCommand}");
        var preview = Assert.Single(document.Descendants(ns + "Expander"), element =>
            (string?)element.Attribute("IsExpanded") == "{Binding IsTroubleshootingPreviewExpanded, Mode=TwoWay}");
        var text = Assert.Single(preview.Descendants(ns + "SelectableTextBlock"));
        Assert.Equal("{Binding TroubleshootingDetails}", (string?)text.Attribute("Text"));
        Assert.Equal("Wrap", (string?)text.Attribute("TextWrapping"));
        Assert.Null(text.Attribute("TextTrimming"));
    }

    private static XDocument LoadAboutView()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "ListenShelf.Desktop", "Views", "Components", "AboutPanel.axaml");
            if (File.Exists(path)) return XDocument.Load(path);
        }
        throw new FileNotFoundException("Could not find repository About view.");
    }

    private sealed class UnexpectedBrowser : IExternalLinkService
    {
        public Task<bool> OpenAsync(Uri uri) => throw new Xunit.Sdk.XunitException("Troubleshooting must never open a browser.");
    }

    private sealed class TestClipboardService : IClipboardTextService
    {
        public List<string> Writes { get; } = [];
        public bool Result { get; set; } = true;
        public bool Throw { get; init; }
        public Task<bool>? Pending { get; init; }

        public Task<bool> SetTextAsync(string text)
        {
            Writes.Add(text);
            if (Throw) throw new IOException("C:\\Users\\Reader\\Private Book.m4b");
            return Pending ?? Task.FromResult(Result);
        }
    }
}
