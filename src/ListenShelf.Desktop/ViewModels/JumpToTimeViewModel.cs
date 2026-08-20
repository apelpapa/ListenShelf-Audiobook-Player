using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class JumpToTimeViewModel : ViewModelBase
{
    private readonly TimeSpan _duration;

    public JumpToTimeViewModel(TimeSpan position, TimeSpan duration)
    {
        _duration = duration;
        var clamped = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Math.Max(0, duration.Ticks)));
        _timeText = FormatTimestamp(clamped);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanJump))]
    private string _timeText;

    public string RangeText => $"Book time: 0:00 – {FormatTimestamp(_duration)}";
    public bool CanJump => TryGetPosition(out _);
    public string ValidationMessage => Validate(out _);

    public bool TryGetPosition(out TimeSpan position) => Validate(out position).Length == 0;

    private string Validate(out TimeSpan position)
    {
        position = TimeSpan.Zero;
        if (_duration <= TimeSpan.Zero) return "The audiobook length is not available yet.";

        var parts = (TimeText ?? string.Empty).Trim().Split(':');
        if (parts.Length is not (2 or 3)
            || parts.Any(part => part.Length == 0 || part.Any(character => character is < '0' or > '9'))
            || parts[^1].Length != 2
            || !ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first)
            || !int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds >= 60)
        {
            return "Use mm:ss or hh:mm:ss, for example 14:30 or 2:14:30.";
        }

        var minutes = 0;
        if (parts.Length == 3 && (parts[1].Length != 2
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
            || minutes >= 60))
        {
            return "In hh:mm:ss, minutes and seconds must be 00–59.";
        }

        // Decimal arithmetic keeps even excessively large input safe. A two-part
        // timestamp may contain total minutes (e.g. 90:00); hours never wrap at 24.
        var totalSeconds = (decimal)first * (parts.Length == 3 ? 3600 : 60) + minutes * 60 + seconds;
        if (totalSeconds > _duration.Ticks / (decimal)TimeSpan.TicksPerSecond)
        {
            return $"Enter a time from 0:00 to {FormatTimestamp(_duration)}.";
        }

        position = TimeSpan.FromTicks((long)(totalSeconds * TimeSpan.TicksPerSecond));
        return string.Empty;
    }

    private static string FormatTimestamp(TimeSpan position) =>
        position.TotalHours >= 1
            ? FormattableString.Invariant($"{position.Ticks / TimeSpan.TicksPerHour}:{position.Minutes:00}:{position.Seconds:00}")
            : FormattableString.Invariant($"{Math.Max(0, position.Minutes)}:{Math.Max(0, position.Seconds):00}");
}
