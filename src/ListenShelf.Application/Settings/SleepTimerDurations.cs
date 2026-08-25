namespace ListenShelf.Application.Settings;

public static class SleepTimerDurations
{
    public const int MinimumMinutes = 1;
    public const int MaximumMinutes = 1440;
    public const int DefaultMinutes = 30;

    public static bool IsValid(int minutes) => minutes is >= MinimumMinutes and <= MaximumMinutes;
}
