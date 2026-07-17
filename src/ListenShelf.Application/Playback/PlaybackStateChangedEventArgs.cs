namespace ListenShelf.Application.Playback;

public sealed class PlaybackStateChangedEventArgs(
    PlaybackState state,
    string? message = null) : EventArgs
{
    public PlaybackState State { get; } = state;

    public string? Message { get; } = message;
}
