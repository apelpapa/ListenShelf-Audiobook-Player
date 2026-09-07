using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia;
using LibVLCSharp.Shared;
using ListenShelf.Desktop.Services;

namespace ListenShelf.Desktop.Diagnostics;

// An allowlist of build/runtime facts, not a serialization of application state or logs.
public sealed partial record TroubleshootingInfo(
    string AppVersion,
    TroubleshootingPlatform Platform,
    Version OsVersion,
    Architecture OsArchitecture,
    Architecture ProcessArchitecture,
    Version DotNetVersion,
    string AvaloniaVersion,
    string LibVlcSharpVersion,
    string? LibVlcVersion)
{
    public static TroubleshootingInfo Capture(Func<string?>? readLoadedLibVlcVersion = null)
    {
        string? nativeVersion = null;
        try
        {
            // The caller reads the existing engine. Never initialize another player for diagnostics.
            nativeVersion = readLoadedLibVlcVersion?.Invoke();
        }
        catch (Exception)
        {
            // Version lookup must not block startup or expose exception messages/paths.
        }

        return new TroubleshootingInfo(
            ApplicationAboutInfo.Current.Version,
            OperatingSystem.IsWindows() ? TroubleshootingPlatform.Windows
                : OperatingSystem.IsMacOS() ? TroubleshootingPlatform.MacOS
                : OperatingSystem.IsLinux() ? TroubleshootingPlatform.Linux
                : TroubleshootingPlatform.Other,
            Environment.OSVersion.Version,
            RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture,
            Environment.Version,
            AssemblyVersion(typeof(AvaloniaObject).Assembly),
            AssemblyVersion(typeof(LibVLC).Assembly),
            nativeVersion);
    }

    public string ToReport() => string.Join(Environment.NewLine,
        "ListenShelf troubleshooting details",
        $"App version: {SafeVersion(AppVersion)}",
        $"Operating system: {PlatformName} (build {OsVersion})",
        $"OS architecture: {SafeArchitecture(OsArchitecture)}",
        $"App architecture: {SafeArchitecture(ProcessArchitecture)}",
        $".NET runtime: {DotNetVersion}",
        $"Avalonia: {SafeVersion(AvaloniaVersion)}",
        $"LibVLCSharp: {SafeVersion(LibVlcSharpVersion)}",
        $"LibVLC runtime: {SafeNativeVersion(LibVlcVersion)}");

    private string PlatformName => Platform switch
    {
        TroubleshootingPlatform.Windows => "Windows",
        TroubleshootingPlatform.MacOS => "macOS",
        TroubleshootingPlatform.Linux => "Linux",
        _ => "Other",
    };

    private static string AssemblyVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString() ?? "Unavailable";

    private static string SafeArchitecture(Architecture architecture) =>
        Enum.IsDefined(architecture) ? architecture.ToString() : "Unavailable";

    private static string SafeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256) return "Unavailable";
        // Commit/build metadata is unnecessary here and can contain custom build-machine details.
        var version = value.Split('+', 2)[0].Trim();
        return VersionPattern().IsMatch(version) ? version : "Unavailable";
    }

    private static string SafeNativeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256) return "Unavailable";
        // libvlc_get_version includes a human-readable suffix. Copy only its numeric version.
        var match = NativeVersionPattern().Match(value.Trim());
        return match.Success ? match.Groups[1].Value : "Unavailable";
    }

    [GeneratedRegex(@"\A[0-9]+(?:\.[0-9]+){1,3}(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?\z")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"\A([0-9]+(?:\.[0-9]+){1,3})(?=\s|[-+]|\z)")]
    private static partial Regex NativeVersionPattern();
}

public enum TroubleshootingPlatform
{
    Other,
    Windows,
    MacOS,
    Linux,
}
