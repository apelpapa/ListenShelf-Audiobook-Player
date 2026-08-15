using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private Guid? _reloadAfterRepair;
    public ManagedFileRepairViewModel Repair { get; }

    private void PrepareManagedFileRepair(VerificationBookOption book)
    {
        _reloadAfterRepair = null;
        if (_currentFilePath is not null && string.Equals(Path.GetFullPath(_currentFilePath), Path.GetFullPath(book.FilePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            _reloadAfterRepair = book.Id;
            UnloadCurrentBook();
        }
    }

    private async Task FinishManagedFileRepairAsync(ManagedFileRepairResult? result)
    {
        if (_disposed) return;
        if (result is not null) Verification.RecordRepair(result);
        RefreshLibrary();
        var reloadId = _reloadAfterRepair;
        _reloadAfterRepair = null;
        if (reloadId is not null)
        {
            var book = _audiobookLibrary.GetBooks().FirstOrDefault(book => book.Id == reloadId);
            if (book is not null && File.Exists(book.FilePath)) await LoadFileAsync(book, autoPlay: false);
        }

        await RefreshManagedStorageAsync();
    }
}
