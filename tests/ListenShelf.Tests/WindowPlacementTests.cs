using Avalonia;
using Avalonia.Controls;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;

namespace ListenShelf.Tests;

public sealed class WindowPlacementTests
{
    private static readonly WindowPlacementScreen Primary = new(new PixelRect(0, 0, 1920, 1040), 1, true);
    private static readonly WindowPlacement Normal = new(100, 120, 1200, 760, false);

    [Fact]
    public void FirstLaunch_CentersDefaultSizeOnPrimaryWorkingArea()
    {
        var result = Assert.IsType<WindowPlacementResult>(WindowPlacementGeometry.Resolve(null, [Primary]));
        Assert.Equal(new WindowPlacement(362, 140, 1180, 720, false), result.Placement);
        Assert.Equal(980, result.MinimumWidth);
        Assert.Equal(620, result.MinimumHeight);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisiblePlacement_RetainsNormalBoundsAndMaximizedChoice(bool maximized)
    {
        var saved = Normal with { IsMaximized = maximized };
        Assert.Equal(saved, WindowPlacementGeometry.Resolve(saved, [Primary])!.Placement);
    }

    [Fact]
    public void SecondaryMonitor_CanHaveNegativeCoordinates()
    {
        var secondary = new WindowPlacementScreen(new PixelRect(-1920, 0, 1920, 1040), 1, false);
        var saved = new WindowPlacement(-1800, 80, 1100, 700, false);
        Assert.Equal(saved, WindowPlacementGeometry.Resolve(saved, [Primary, secondary])!.Placement);
    }

    [Fact]
    public void MixedDpi_UsesTargetMonitorScaleWithoutScalingTheStoredDipSizeTwice()
    {
        var secondary = new WindowPlacementScreen(new PixelRect(1920, 0, 2560, 1440), 1.5, false);
        var saved = new WindowPlacement(2100, 50, 1200, 700, false);
        var result = WindowPlacementGeometry.Resolve(saved, [Primary, secondary])!.Placement;
        Assert.Equal(saved, result);
        AssertFits(result, secondary);
    }

    [Fact]
    public void MovingMaximizedWindowToAnotherMonitor_RehomesNormalBoundsWithoutSavingMaximizedDimensions()
    {
        var secondary = new WindowPlacementScreen(new PixelRect(1920, 0, 2560, 1440), 1.5, false);
        var tracker = new WindowPlacementState(Normal with { IsMaximized = true });
        var safe = WindowPlacementGeometry.Resolve(tracker.Current, [secondary])!;
        tracker.SetSafeBounds(safe.Placement);
        Assert.True(tracker.Current.IsMaximized);
        Assert.Equal(Normal.Width, tracker.Current.Width);
        Assert.Equal(Normal.Height, tracker.Current.Height);
        AssertFits(tracker.Current, secondary);
    }

    [Fact]
    public void NeighboringHighDpiMonitor_DoesNotStealAWindowThatFitsItsOriginalMonitor()
    {
        var highDpi = new WindowPlacementScreen(new PixelRect(1920, 0, 3840, 2160), 2, false);
        var saved = Normal with { X = 600 };
        Assert.Equal(saved, WindowPlacementGeometry.Resolve(saved, [Primary, highDpi])!.Placement);
    }

    [Theory]
    [InlineData(4000, 4000)]
    [InlineData(-10000, -10000)]
    [InlineData(int.MinValue, int.MinValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void DisconnectedOrImpossibleMonitor_CentersOnPrimary(int x, int y)
    {
        var saved = Normal with { X = x, Y = y, IsMaximized = true };
        var result = WindowPlacementGeometry.Resolve(saved, [Primary])!.Placement;
        Assert.Equal(new WindowPlacement(352, 120, 1200, 760, true), result);
        AssertFits(result, Primary);
    }

    [Fact]
    public void PartialOffScreenPlacement_IsClampedWithTitleBarAndFrameVisible()
    {
        var saved = new WindowPlacement(-300, 900, 1400, 900, false);
        var result = WindowPlacementGeometry.Resolve(saved, [Primary])!.Placement;
        Assert.Equal(new WindowPlacement(0, 100, 1400, 900, false), result);
        AssertFits(result, Primary);
    }

    [Fact]
    public void OversizedPlacement_IsReducedToWorkingAreaIncludingFrame()
    {
        var result = WindowPlacementGeometry.Resolve(new WindowPlacement(0, 0, 6000, 4000, false), [Primary])!;
        Assert.Equal(new WindowPlacement(0, 0, 1904, 1000, false), result.Placement);
        AssertFits(result.Placement, Primary);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(3)]
    public void SmallOrHighDpiScreen_RelaxesMinimumOnlyEnoughToFit(double scale)
    {
        var screen = new WindowPlacementScreen(new PixelRect(0, 0, 1280, 720), scale, true);
        var result = WindowPlacementGeometry.Resolve(Normal, [screen])!;
        AssertFits(result.Placement, screen);
        Assert.Equal(Math.Min(980, 1280 / scale - 16), result.MinimumWidth);
        Assert.Equal(Math.Min(620, 720 / scale - 40), result.MinimumHeight);
        Assert.InRange(result.Placement.Width, result.MinimumWidth, 1280 / scale - 16);
        Assert.InRange(result.Placement.Height, result.MinimumHeight, 720 / scale - 40);
    }

    [Fact]
    public void TaskbarsAtTopOrLeft_KeepTheWindowInsideTheActualWorkingArea()
    {
        var screen = new WindowPlacementScreen(new PixelRect(60, 40, 1800, 960), 1, true);
        var result = WindowPlacementGeometry.Resolve(Normal with { X = 0, Y = 0 }, [screen])!.Placement;
        Assert.Equal(60, result.X);
        Assert.Equal(40, result.Y);
        AssertFits(result, screen);
    }

    [Fact]
    public void NoUsableScreens_LeavesPlacementToTheWindowManager()
    {
        Assert.Null(WindowPlacementGeometry.Resolve(Normal, []));
        Assert.Null(WindowPlacementGeometry.Resolve(Normal,
        [
            new WindowPlacementScreen(default, 1, true),
            new WindowPlacementScreen(Primary.WorkingArea, 0, false),
            new WindowPlacementScreen(Primary.WorkingArea, double.NaN, false),
        ]));
    }

    [Fact]
    public void MissingPrimaryFlag_UsesFirstUsableScreen()
    {
        var secondary = Primary with { IsPrimary = false };
        Assert.Equal(new WindowPlacement(362, 140, 1180, 720, false), WindowPlacementGeometry.Resolve(null,
            [new WindowPlacementScreen(default, 1, true), secondary])!.Placement);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(100001)]
    public void InvalidSavedSize_FallsBackToDefaultInsteadOfPreventingStartup(double width)
    {
        var invalid = Normal with { Width = width, IsMaximized = true };
        Assert.Equal(new WindowPlacement(362, 140, 1180, 720, false), WindowPlacementGeometry.Resolve(invalid, [Primary])!.Placement);
    }

    [Fact]
    public void Revalidation_IsIdempotentAfterMonitorOrDpiChange()
    {
        var screen = new WindowPlacementScreen(new PixelRect(-1600, 40, 1600, 960), 1.25, true);
        var safe = WindowPlacementGeometry.Resolve(Normal with { Width = 1700, Height = 1100 }, [screen])!;
        Assert.Equal(safe, WindowPlacementGeometry.Resolve(safe.Placement, [screen]));
        AssertFits(safe.Placement, screen);
    }

    [Fact]
    public void MaximizedResize_DoesNotReplaceNormalRestoreBounds()
    {
        var tracker = new WindowPlacementState(Normal);
        tracker.Observe(WindowState.Maximized, new WindowPlacement(0, 0, 1920, 1040, false));
        Assert.Equal(Normal with { IsMaximized = true }, tracker.Current);
        tracker.Observe(WindowState.Minimized, new WindowPlacement(-32000, -32000, 0, 0, false));
        Assert.Equal(Normal with { IsMaximized = true }, tracker.Current);
    }

    [Theory]
    [InlineData(WindowState.Minimized)]
    [InlineData(WindowState.FullScreen)]
    public void MinimizedAndFullscreen_AreNeverPersistedAsLaunchStates(WindowState state)
    {
        var tracker = new WindowPlacementState(Normal);
        tracker.Observe(state, new WindowPlacement(-32000, -32000, 1920, 1080, true));
        Assert.Equal(Normal, tracker.Current);
    }

    [Fact]
    public void RestoreDownThenResize_UpdatesNormalBoundsAndClearsMaximizedFlag()
    {
        var tracker = new WindowPlacementState(Normal with { IsMaximized = true });
        var resized = new WindowPlacement(200, 150, 1300, 780, false);
        tracker.Observe(WindowState.Normal, resized);
        Assert.Equal(resized, tracker.Current);
        tracker.Observe(WindowState.Minimized, Normal);
        Assert.Equal(resized, tracker.Current);
    }

    [Fact]
    public void StateChangeBeforeDeferredCapture_PreservesMaximizedFlagWhenImmediatelyMinimized()
    {
        var tracker = new WindowPlacementState(Normal);
        tracker.ObserveState(WindowState.Maximized);
        tracker.ObserveState(WindowState.Minimized);
        tracker.Observe(WindowState.Minimized, Normal with { X = -32000, Width = 0 });
        Assert.Equal(Normal with { IsMaximized = true }, tracker.Current);
    }

    [Fact]
    public void InvalidOrTransientNormalSize_DoesNotLoseValidBounds()
    {
        var tracker = new WindowPlacementState(Normal);
        tracker.Observe(WindowState.Normal, Normal with { Width = double.NaN });
        tracker.Observe(WindowState.Normal, Normal with { Height = 0 });
        Assert.Equal(Normal, tracker.Current);
    }

    private static void AssertFits(WindowPlacement placement, WindowPlacementScreen screen)
    {
        Assert.True(placement.X >= screen.WorkingArea.X);
        Assert.True(placement.Y >= screen.WorkingArea.Y);
        Assert.True(placement.X + (placement.Width + 16) * screen.Scaling <= (double)screen.WorkingArea.X + screen.WorkingArea.Width + 0.01);
        Assert.True(placement.Y + (placement.Height + 40) * screen.Scaling <= (double)screen.WorkingArea.Y + screen.WorkingArea.Height + 0.01);
    }
}
