namespace ListenShelf.Desktop.Services;

public sealed record KeyboardShortcutReferenceEntry(string Keys, string Description, string Context);

public static class KeyboardShortcutReference
{
    public static IReadOnlyList<KeyboardShortcutReferenceEntry> MainWindow { get; } = Array.AsReadOnly<KeyboardShortcutReferenceEntry>(
    [
        new("Space / K", "Play or pause", "Requires a loaded audiobook that is ready to play."),
        new("Left Arrow / J", "Rewind", "Uses the rewind interval in Playback settings above."),
        new("Right Arrow / L", "Skip forward", "Uses the forward interval in Playback settings above."),
        new("M", "Mute or unmute", "Restores the selected volume without pausing playback."),
        new("Ctrl+F", "Search the library", "Opens Library and selects any existing search text."),
        new("Escape", "Clear library search", "Only while the search box is focused; other filters stay unchanged."),
    ]);

    public static IReadOnlyList<KeyboardShortcutReferenceEntry> JumpToTimeDialog { get; } = Array.AsReadOnly<KeyboardShortcutReferenceEntry>(
    [
        new("Enter", "Jump to the entered time", "Available only when the time is valid and within the audiobook."),
        new("Escape", "Cancel", "Closes Jump to time without seeking."),
    ]);

    public static IReadOnlyList<KeyboardShortcutReferenceEntry> CustomSleepTimerDialog { get; } = Array.AsReadOnly<KeyboardShortcutReferenceEntry>(
    [
        new("Enter", "Start the custom timer", "Available only for a valid whole-minute duration; replaces the active timer."),
        new("Escape", "Cancel", "Closes the custom timer dialog without changing an existing timer."),
    ]);

    public static string FocusGuidance =>
        "Main-window playback shortcuts do not run while typing in text fields or choosing combo-box values. "
        + "Focused buttons keep Space; focused sliders keep the arrow keys. "
        + "Play/pause and skipping require a loaded, ready audiobook. Tab / Shift+Tab move between controls.";

    public static string MediaKeyGuidance =>
        "On Windows, keyboard and headset media buttons can also control a loaded book while ListenShelf is minimized. "
        + "Play/Pause toggles playback, Previous/Next use your configured skip intervals, and Stop pauses. "
        + "The letter-key shortcuts above require the main window to be focused.";
}
