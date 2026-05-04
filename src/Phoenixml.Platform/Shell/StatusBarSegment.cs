namespace Phoenixml.Platform.Shell;

/// <summary>
/// A composable segment in <see cref="StatusBar"/>. Apps register segments via
/// DI; the bar lays them out left-to-right by <see cref="Order"/> with separators.
/// </summary>
public sealed record StatusBarSegment(
    string Id,
    string Label,
    int Order,
    Func<IServiceProvider, string>? DynamicTextFactory = null);
