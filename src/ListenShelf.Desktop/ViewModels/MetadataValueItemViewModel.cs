using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class MetadataValueItemViewModel(
    string value,
    Action<MetadataValueItemViewModel> remove) : ViewModelBase
{
    public string Value { get; } = value;

    [RelayCommand]
    private void Remove() => remove(this);
}
