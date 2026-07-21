using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.ViewModels;

public sealed record LibraryGroupOptionViewModel(
    LibraryGroupMode Mode,
    string DisplayName);
