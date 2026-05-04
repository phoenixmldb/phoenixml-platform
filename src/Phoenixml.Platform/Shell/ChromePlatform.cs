namespace Phoenixml.Platform.Shell;

/// <summary>
/// Platform-specific chrome configuration. Detected at runtime from the host OS
/// and applied to <see cref="MainWindow"/>. The detection logic is informed by
/// the spike findings in <c>docs/chrome-constraints.md</c>; if a platform's
/// chrome turns out broken in the field, override via <see cref="Custom"/>.
/// </summary>
public sealed class ChromePlatform
{
    /// <summary>True when the explicit min/max/close buttons should be drawn (Win/Linux).</summary>
    public bool ShowWindowsChromeButtons { get; init; }

    /// <summary>True when the OS supplies native window decorations (macOS traffic lights).</summary>
    public bool UseTrafficLightsNative { get; init; }

    /// <summary>True when the window's client area extends into the chrome region (custom title bar).</summary>
    public bool ExtendsClientArea { get; init; } = true;

    /// <summary>If chrome falls back to OS decorations, the reason (logged at startup, surfaced in diagnostics).</summary>
    public string? FallbackReason { get; init; }

    /// <summary>Detects the right chrome configuration for the current OS.</summary>
    public static ChromePlatform Detect()
    {
        if (OperatingSystem.IsWindows())
            return new ChromePlatform { ShowWindowsChromeButtons = true, ExtendsClientArea = true };
        if (OperatingSystem.IsMacOS())
            return new ChromePlatform { UseTrafficLightsNative = true, ExtendsClientArea = true };
        // Linux: defaults pending chrome-constraints.md outcomes.
        return new ChromePlatform { ShowWindowsChromeButtons = true, ExtendsClientArea = true };
    }

    /// <summary>Builds a custom configuration. Useful for tests or platform-specific overrides.</summary>
    public static ChromePlatform Custom(
        bool showWindowsChromeButtons = false,
        bool useTrafficLightsNative = false,
        bool extendsClientArea = true,
        string? fallbackReason = null) =>
        new()
        {
            ShowWindowsChromeButtons = showWindowsChromeButtons,
            UseTrafficLightsNative = useTrafficLightsNative,
            ExtendsClientArea = extendsClientArea,
            FallbackReason = fallbackReason,
        };
}
