using System.Reflection;
using System.Reflection.Emit;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Tests;

public sealed class AboutViewModelTests
{
    [Fact]
    public void CreatingAbout_ReadsLocalBuildInfoWithoutOpeningLinks()
    {
        var launcher = new TestLinkService();
        var model = new AboutViewModel(launcher);
        var assembly = typeof(MainWindowViewModel).Assembly;
        var builtVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        Assert.Empty(launcher.Opened);
        Assert.Equal("ListenShelf — Audiobook Player", model.Info.ProductName);
        Assert.Equal(builtVersion, model.Info.FullVersion);
        Assert.Equal($"Version {builtVersion.Split('+', 2)[0]}", model.VersionText);
        Assert.Equal(assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright, model.Info.Copyright);
        Assert.Contains("GPL-3.0-only", model.LicenseText);
        Assert.True(model.CanOpenLinks);
        Assert.False(model.IsOpeningLink);
        Assert.False(model.HasLinkError);
    }

    [Theory]
    [InlineData("2.3.4-beta.2+abcdef", "2.3.4-beta.2")]
    [InlineData("2.3.4-alpha.1", "2.3.4-alpha.1")]
    [InlineData("2.3.4", "2.3.4")]
    [InlineData(null, "1.2.3.4")]
    [InlineData("   ", "1.2.3.4")]
    [InlineData("+build-only", "1.2.3.4")]
    public void VersionDisplay_PreservesPrereleaseAndUsesAssemblyFallback(string? informationalVersion, string expected)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"AboutTests{Guid.NewGuid():N}")
        {
            Version = new Version(1, 2, 3, 4),
        }, AssemblyBuilderAccess.RunAndCollect);
        if (informationalVersion is not null)
            assembly.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informationalVersion]));

        var info = ApplicationAboutInfo.FromAssembly(assembly);

        Assert.Equal(expected, info.Version);
        Assert.Equal(string.IsNullOrWhiteSpace(informationalVersion) ? "1.2.3.4" : informationalVersion.Trim(), info.FullVersion);
        Assert.Equal("ListenShelf — Audiobook Player", info.ProductName);
        Assert.Contains("audiobook", info.Description);
    }

    [Theory]
    [InlineData("repository", "https://github.com/apelpapa/ListenShelf-Audiobook-Player")]
    [InlineData("issues", "https://github.com/apelpapa/ListenShelf-Audiobook-Player/issues")]
    [InlineData("license", "https://github.com/apelpapa/ListenShelf-Audiobook-Player/blob/main/LICENSE")]
    public async Task ExplicitLinkClick_OpensOnlyItsFixedHttpsDestination(string link, string expected)
    {
        var launcher = new TestLinkService();
        var model = new AboutViewModel(launcher);

        await Command(model, link).ExecuteAsync(null);

        var uri = Assert.Single(launcher.Opened);
        Assert.Equal(expected, uri.AbsoluteUri);
        Assert.Equal("https", uri.Scheme);
        Assert.Empty(uri.Query);
        Assert.Empty(uri.Fragment);
        Assert.Empty(uri.UserInfo);
        Assert.False(model.IsOpeningLink);
        Assert.False(model.HasLinkError);
        Assert.True(model.CanOpenLinks);
    }

    [Theory]
    [InlineData("repository", false)]
    [InlineData("repository", true)]
    [InlineData("issues", false)]
    [InlineData("issues", true)]
    [InlineData("license", false)]
    [InlineData("license", true)]
    public async Task BrowserFailure_ShowsCopyableAddressWithoutLeakingExceptionDetails(string link, bool throws)
    {
        var launcher = new TestLinkService { Result = false, Throw = throws };
        var model = new AboutViewModel(launcher);

        await Command(model, link).ExecuteAsync(null);

        Assert.True(model.HasLinkError);
        Assert.Contains("Copy this address", model.LinkError);
        Assert.Equal(Assert.Single(launcher.Opened).AbsoluteUri, model.FailedLinkUrl);
        Assert.DoesNotContain("private-book.m4b", model.LinkError);
        Assert.False(model.IsOpeningLink);
        Assert.True(model.CanOpenLinks);
    }

    [Fact]
    public async Task MissingLauncher_LeavesAboutUsableAndOffersManualAddress()
    {
        var model = new AboutViewModel(null);
        await model.OpenRepositoryCommand.ExecuteAsync(null);
        Assert.True(model.HasLinkError);
        Assert.Equal(model.RepositoryUrl, model.FailedLinkUrl);
        Assert.NotEmpty(model.Info.Version);
        Assert.True(model.CanOpenLinks);
    }

    [Fact]
    public async Task SuccessfulRetry_ClearsOldErrorAndNotifiesThePanel()
    {
        var launcher = new TestLinkService { Result = false };
        var model = new AboutViewModel(launcher);
        await model.OpenIssuesCommand.ExecuteAsync(null);
        var notifications = new HashSet<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        launcher.Result = true;

        await model.OpenIssuesCommand.ExecuteAsync(null);

        Assert.False(model.HasLinkError);
        Assert.Empty(model.LinkError);
        Assert.Empty(model.FailedLinkUrl);
        Assert.Contains(nameof(AboutViewModel.HasLinkError), notifications);
        Assert.Contains(nameof(AboutViewModel.CanOpenLinks), notifications);
        Assert.Equal(2, launcher.Opened.Count);
    }

    [Fact]
    public async Task PendingLaunch_DisablesEveryLinkAndRejectsExtraLaunches()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new TestLinkService { Pending = completion.Task };
        var model = new AboutViewModel(launcher);
        var pending = model.OpenRepositoryCommand.ExecuteAsync(null);

        Assert.True(model.IsOpeningLink);
        Assert.False(model.OpenRepositoryCommand.CanExecute(null));
        Assert.False(model.OpenIssuesCommand.CanExecute(null));
        Assert.False(model.OpenLicenseCommand.CanExecute(null));
        await model.OpenIssuesCommand.ExecuteAsync(null);
        await model.OpenLicenseCommand.ExecuteAsync(null);
        Assert.Single(launcher.Opened);

        completion.SetResult(true);
        await pending;

        Assert.False(model.IsOpeningLink);
        Assert.True(model.OpenRepositoryCommand.CanExecute(null));
        Assert.True(model.OpenIssuesCommand.CanExecute(null));
        Assert.True(model.OpenLicenseCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("file:///C:/private-book.m4b")]
    [InlineData("javascript:alert(1)")]
    [InlineData("relative/path")]
    public async Task ExternalLinkAdapter_RejectsNonHttpsBeforeCallingThePlatform(string address)
    {
        var service = new AvaloniaExternalLinkService(null!);
        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync(new Uri(address, UriKind.RelativeOrAbsolute)));
    }

    private static IAsyncRelayCommand Command(AboutViewModel model, string link) => link switch
    {
        "repository" => model.OpenRepositoryCommand,
        "issues" => model.OpenIssuesCommand,
        "license" => model.OpenLicenseCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(link)),
    };

    private sealed class TestLinkService : IExternalLinkService
    {
        public List<Uri> Opened { get; } = [];
        public bool Result { get; set; } = true;
        public bool Throw { get; init; }
        public Task<bool>? Pending { get; init; }

        public Task<bool> OpenAsync(Uri uri)
        {
            Opened.Add(uri);
            if (Throw) throw new InvalidOperationException("private-book.m4b");
            return Pending ?? Task.FromResult(Result);
        }
    }
}
