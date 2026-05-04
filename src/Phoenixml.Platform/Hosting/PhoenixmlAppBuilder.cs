using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Phoenixml.Platform.Hosting;

/// <summary>
/// Bootstraps a Phoenixml application.  Every app's <c>Main</c> is roughly ten lines:
/// <code>
/// var builder = PhoenixmlAppBuilder.Create("MyApp", args);
/// builder.Services.AddSingleton&lt;IMyService, MyService&gt;();
/// builder.Commands.Register(new RegisteredCommand("file.open", …));
/// var host = builder.Build();
/// await host.RunAsync();
/// </code>
/// </summary>
public sealed class PhoenixmlAppBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;

    /// <summary>Gets the name of the application being built.</summary>
    public string AppName { get; }

    /// <summary>Gets the command registry for this application.</summary>
    public CommandRegistry Commands { get; } = new();

    private PhoenixmlAppBuilder(string appName, string[] args)
    {
        AppName = appName;
        _hostBuilder = Host.CreateApplicationBuilder(args);
        _hostBuilder.Services.AddSingleton(Commands);
        _hostBuilder.Services.AddSingleton(new SettingsService(appName));
    }

    /// <summary>Creates a new builder for the application named <paramref name="appName"/>.</summary>
    /// <param name="appName">The application name; used for settings paths and single-instance coordination.</param>
    /// <param name="args">The command-line arguments passed to <c>Main</c>.</param>
    public static PhoenixmlAppBuilder Create(string appName, string[] args)
    {
        ArgumentNullException.ThrowIfNull(appName);
        ArgumentNullException.ThrowIfNull(args);
        return new PhoenixmlAppBuilder(appName, args);
    }

    /// <summary>Gets the <see cref="IServiceCollection"/> for registering application services.</summary>
    public IServiceCollection Services => _hostBuilder.Services;

    /// <summary>Builds and returns the configured <see cref="IHost"/>.</summary>
    public IHost Build() => _hostBuilder.Build();
}
