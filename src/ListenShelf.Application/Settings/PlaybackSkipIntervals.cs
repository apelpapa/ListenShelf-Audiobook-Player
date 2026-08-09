namespace ListenShelf.Application.Settings;

public static class PlaybackSkipIntervals
{
    public const int DefaultRewindSeconds = 15;
    public const int DefaultForwardSeconds = 30;
    public const int MinimumSeconds = 1;
    public const int MaximumSeconds = 600;

    public static bool IsValid(int seconds) =>
        seconds is >= MinimumSeconds and <= MaximumSeconds;
}
