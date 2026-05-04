namespace Phoenixml.Platform.Hosting;

/// <summary>
/// A command that has been registered with a <see cref="CommandRegistry"/>.
/// </summary>
/// <param name="Id">Stable, unique identifier for the command (e.g. <c>"file.save"</c>).</param>
/// <param name="DisplayName">Human-readable name shown in menus, command palette, etc.</param>
/// <param name="DefaultKeybinding">Default key binding (e.g. <c>"Ctrl+S"</c>), or <see langword="null"/>.</param>
/// <param name="MenuPath">Slash-delimited menu path (e.g. <c>"File/Save"</c>), or <see langword="null"/>.</param>
/// <param name="ToolbarGroup">Toolbar group identifier, or <see langword="null"/>.</param>
/// <param name="Execute">Factory that receives the DI container and returns a <see cref="ValueTask"/> to execute the command.</param>
/// <param name="CanExecute">Optional guard; if <see langword="null"/> the command is always enabled.</param>
public sealed record RegisteredCommand(
    string Id,
    string DisplayName,
    string? DefaultKeybinding,
    string? MenuPath,
    string? ToolbarGroup,
    Func<IServiceProvider, ValueTask> Execute,
    Func<IServiceProvider, bool>? CanExecute);

/// <summary>
/// Explicit-register command registry.  Commands are registered by calling
/// <see cref="Register"/> with a <see cref="RegisteredCommand"/> instance;
/// a source-generator upgrade is planned for v2 but is not required at v1.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, RegisteredCommand> _byId = new(StringComparer.Ordinal);

    /// <summary>Gets a read-only view of all registered commands keyed by id.</summary>
    public IReadOnlyDictionary<string, RegisteredCommand> All => _byId;

    /// <summary>
    /// Registers a command.  If a command with the same <see cref="RegisteredCommand.Id"/>
    /// already exists it is silently replaced.
    /// </summary>
    /// <param name="command">The command to register.</param>
    public void Register(RegisteredCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _byId[command.Id] = command;
    }

    /// <summary>
    /// Returns the command registered under <paramref name="id"/>,
    /// or <see langword="null"/> if no such command exists.
    /// </summary>
    /// <param name="id">The command identifier to look up.</param>
    public RegisteredCommand? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.TryGetValue(id, out var cmd) ? cmd : null;
    }
}
