namespace ListenShelf.Application.Playback;

public sealed class PlaybackProgressChangedEventArgs(
    TimeSpan position,
    TimeSpan duration) : EventArgs
{
    public TimeSpan Position { get; } = position;

    public TimeSpan Duration { get; } = duration;
}
