using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class CustomSleepTimerViewModel : ViewModelBase
{
    public const int MinimumMinutes = 1;
    public const int MaximumMinutes = 1440;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string _minutesText = "30";

    public string RangeText => $"Whole minutes ({MinimumMinutes}–{MaximumMinutes})";
    public bool CanStart => TryGetMinutes(out _);
    public string ValidationMessage => CanStart
        ? string.Empty
        : $"Enter a whole number from {MinimumMinutes} to {MaximumMinutes} minutes.";

    public bool TryGetMinutes(out int minutes) =>
        int.TryParse(MinutesText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
        && IsValidDuration(minutes);

    public static bool IsValidDuration(int minutes) => minutes is >= MinimumMinutes and <= MaximumMinutes;
}
