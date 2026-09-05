namespace ListenShelf.Application.Settings;

// Position is in desktop pixels; normal client size is in device-independent units.
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool IsMaximized)
{
    public bool IsValid() => double.IsFinite(Width) && double.IsFinite(Height)
        && Width is > 0 and <= 100_000 && Height is > 0 and <= 100_000;
}
