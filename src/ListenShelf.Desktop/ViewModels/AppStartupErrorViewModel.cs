using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Desktop.Diagnostics;
using ListenShelf.Infrastructure.Storage;
using ListenShelf.Playback.LibVlc;

namespace ListenShelf.Desktop.ViewModels;

public partial class AppStartupErrorViewModel : ViewModelBase
{
    private readonly AppDiagnosticLog _diagnosticLog;
    private readonly string _dataDirectoryPath;

    [ObservableProperty]
    private string _heading = string.Empty;

    [ObservableProperty]
    private string _explanation = string.Empty;

    [ObservableProperty]
    private string _helpText = string.Empty;

    [ObservableProperty]
    private string _technicalDetails = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public AppStartupErrorViewModel(
        Exception failure,
        AppDiagnosticLog diagnosticLog)
    {
        _diagnosticLog = diagnosticLog
            ?? throw new ArgumentNullException(nameof(diagnosticLog));
        _dataDirectoryPath = GetSafeDataDirectoryPath();
        ApplyFailure(failure);
    }

    public event EventHandler? RetryRequested;

    public string DataDirectoryPath => _dataDirectoryPath;

    public string DiagnosticLocationText => _diagnosticLog.LogFilePath
        ?? "Diagnostic logging is unavailable because the data directory could not be opened.";

    public void ApplyFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (failure is LibVlcInitializationException playbackFailure)
        {
            Heading = "The playback engine could not start";
            Explanation =
                "ListenShelf stopped safely because a compatible native LibVLC runtime was not available. No managed audiobook files were deleted or replaced.";
            HelpText = playbackFailure.PlatformHelp;
            TechnicalDetails =
                $"{playbackFailure.Message}{Environment.NewLine}{playbackFailure.RuntimeDescription}";
        }
        else
        {
            Heading = "ListenShelf could not finish starting";
            Explanation =
                "An unexpected startup problem occurred. Your library data was not deliberately changed.";
            HelpText =
                "Retry once. If the problem continues, include the diagnostic log with a GitHub issue.";
            TechnicalDetails = failure.Message;
        }

        StatusText = string.IsNullOrWhiteSpace(_diagnosticLog.LogFilePath)
            ? "ListenShelf could not create a diagnostic log."
            : $"Technical details were written to {_diagnosticLog.LogFilePath}.";
        OnPropertyChanged(nameof(DiagnosticLocationText));
    }

    [RelayCommand]
    private void Retry() => RetryRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenDiagnosticFolder()
    {
        var path = _diagnosticLog.LogFilePath is null
            ? DataDirectoryPath
            : Path.GetDirectoryName(_diagnosticLog.LogFilePath)!;

        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            StatusText = $"The diagnostic folder could not be opened: {exception.Message}";
        }
    }

    private static string GetSafeDataDirectoryPath()
    {
        try
        {
            return ListenShelfPaths.CreateDefault().DataRootPath;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }
}
