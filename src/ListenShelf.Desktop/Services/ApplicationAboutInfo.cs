using System.Reflection;

namespace ListenShelf.Desktop.Services;

public sealed record ApplicationAboutInfo(
    string ProductName, string Description, string Version, string FullVersion, string Copyright)
{
    public static ApplicationAboutInfo Current { get; } = FromAssembly(typeof(ApplicationAboutInfo).Assembly);

    public static ApplicationAboutInfo FromAssembly(Assembly assembly)
    {
        var fullVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Trim();
        if (string.IsNullOrWhiteSpace(fullVersion)) fullVersion = assembly.GetName().Version?.ToString() ?? "Unknown";
        // Keep prerelease labels, but leave long build/commit metadata in the tooltip.
        var version = fullVersion.Split('+', 2)[0];
        if (string.IsNullOrWhiteSpace(version)) version = assembly.GetName().Version?.ToString() ?? "Unknown";
        return new ApplicationAboutInfo(
            assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "ListenShelf — Audiobook Player",
            assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
                ?? "A free and open-source, privacy-first audiobook library and player.",
            version, fullVersion,
            assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty);
    }
}
