using Avalonia;
using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.Services;

public sealed record WindowPlacementScreen(PixelRect WorkingArea, double Scaling, bool IsPrimary);
public sealed record WindowPlacementResult(WindowPlacement Placement, double MinimumWidth, double MinimumHeight);

public static class WindowPlacementGeometry
{
    public const double DefaultWidth = 1180;
    public const double DefaultHeight = 720;
    public const double MinimumWidth = 980;
    public const double MinimumHeight = 620;

    public static WindowPlacementResult? Resolve(
        WindowPlacement? saved, IEnumerable<WindowPlacementScreen> screens,
        double frameWidth = 16, double frameHeight = 40)
    {
        frameWidth = double.IsFinite(frameWidth) ? Math.Clamp(frameWidth, 0, 200) : 16;
        frameHeight = double.IsFinite(frameHeight) ? Math.Clamp(frameHeight, 0, 200) : 40;
        var usable = screens.Where(screen => double.IsFinite(screen.Scaling) && screen.Scaling is >= 0.1 and <= 16
            && screen.WorkingArea.Width / screen.Scaling > frameWidth
            && screen.WorkingArea.Height / screen.Scaling > frameHeight).ToArray();
        if (usable.Length == 0) return null;
        if (saved?.IsValid() != true) saved = null;

        var target = usable.FirstOrDefault(screen => screen.IsPrimary) ?? usable[0];
        var bestOverlap = 0d;
        if (saved is not null)
        {
            // Prefer the monitor containing the saved origin. Comparing hypothetical
            // sizes at every monitor's DPI could otherwise pull a fully visible
            // window onto a neighboring high-DPI display.
            var originScreen = usable.FirstOrDefault(screen =>
                saved.X >= screen.WorkingArea.X && saved.X < (double)screen.WorkingArea.X + screen.WorkingArea.Width
                && saved.Y >= screen.WorkingArea.Y && saved.Y < (double)screen.WorkingArea.Y + screen.WorkingArea.Height);
            if (originScreen is not null)
            {
                target = originScreen;
                bestOverlap = 1;
            }
            else foreach (var screen in usable)
            {
                var area = screen.WorkingArea;
                // Use doubles to avoid overflow from corrupt/extreme stored coordinates.
                var overlapWidth = Math.Max(0, Math.Min((double)area.X + area.Width,
                    saved.X + (saved.Width + frameWidth) * screen.Scaling) - Math.Max(area.X, saved.X));
                var overlapHeight = Math.Max(0, Math.Min((double)area.Y + area.Height,
                    saved.Y + (saved.Height + frameHeight) * screen.Scaling) - Math.Max(area.Y, saved.Y));
                var overlap = overlapWidth * overlapHeight;
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    target = screen;
                }
            }
        }

        var work = target.WorkingArea;
        var maxWidth = work.Width / target.Scaling - frameWidth;
        var maxHeight = work.Height / target.Scaling - frameHeight;
        var minWidth = Math.Min(MinimumWidth, maxWidth);
        var minHeight = Math.Min(MinimumHeight, maxHeight);
        var width = Math.Clamp(saved?.Width ?? DefaultWidth, minWidth, maxWidth);
        var height = Math.Clamp(saved?.Height ?? DefaultHeight, minHeight, maxHeight);
        var outerWidth = (width + frameWidth) * target.Scaling;
        var outerHeight = (height + frameHeight) * target.Scaling;
        var rightmost = Math.Max(work.X, (double)work.X + work.Width - outerWidth);
        var bottommost = Math.Max(work.Y, (double)work.Y + work.Height - outerHeight);
        var x = bestOverlap > 0 ? Math.Clamp(saved!.X, work.X, rightmost) : work.X + (work.Width - outerWidth) / 2;
        var y = bestOverlap > 0 ? Math.Clamp(saved!.Y, work.Y, bottommost) : work.Y + (work.Height - outerHeight) / 2;
        return new WindowPlacementResult(new WindowPlacement(
            (int)Math.Clamp(Math.Floor(x), int.MinValue, int.MaxValue),
            (int)Math.Clamp(Math.Floor(y), int.MinValue, int.MaxValue),
            width, height, saved?.IsMaximized == true), minWidth, minHeight);
    }
}
