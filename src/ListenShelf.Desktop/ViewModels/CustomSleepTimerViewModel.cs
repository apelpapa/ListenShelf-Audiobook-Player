using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class CustomSleepTimerViewModel : ViewModelBase
{
    public const int MinimumMinutes = SleepTimerDurations.MinimumMinutes;
    public const int MaximumMinutes = SleepTimerDurations.MaximumMinutes;

    public CustomSleepTimerViewModel(int? initialMinutes = null)
    {
        _minutesText = (initialMinutes is { } minutes && IsValidDuration(minutes)
            ? minutes : SleepTimerDurations.DefaultMinutes).ToString(CultureInfo.InvariantCulture);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string _minutesText;

    public string RangeText => $"Whole minutes ({MinimumMinutes}–{MaximumMinutes})";
    public bool CanStart => TryGetMinutes(out _);
    public string ValidationMessage => CanStart
        ? string.Empty
        : $"Enter a whole number from {MinimumMinutes} to {MaximumMinutes} minutes.";

    public bool TryGetMinutes(out int minutes) =>
        int.TryParse(MinutesText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
        && IsValidDuration(minutes);

    public static bool IsValidDuration(int minutes) => SleepTimerDurations.IsValid(minutes);
}
