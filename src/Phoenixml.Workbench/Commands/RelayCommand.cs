namespace Phoenixml.Workbench.Commands;

/// <summary>
/// Minimal <see cref="System.Windows.Input.ICommand"/> implementation that wraps a
/// synchronous <see cref="Action"/> or a fire-and-forget <see cref="Func{Task}"/>.
/// <see cref="CanExecute"/> always returns <see langword="true"/>; this command does not
/// raise <see cref="CanExecuteChanged"/>.
/// </summary>
public sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;

    /// <summary>Initializes a new <see cref="RelayCommand"/> that calls <paramref name="execute"/> synchronously.</summary>
    public RelayCommand(Action execute) => _execute = execute;

    /// <summary>Initializes a new <see cref="RelayCommand"/> that fires <paramref name="executeAsync"/> as a discarded task.</summary>
    public RelayCommand(Func<Task> executeAsync) => _execute = () => _ = executeAsync();

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute();

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
