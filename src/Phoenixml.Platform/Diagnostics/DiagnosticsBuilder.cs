using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Phoenixml.Platform.Diagnostics;

/// <summary>
/// Extension methods for wiring Phoenixml diagnostics into an <see cref="IServiceCollection"/>.
/// </summary>
public static class DiagnosticsBuilder
{
    /// <summary>
    /// Adds Phoenixml diagnostics to the service collection:
    /// <list type="bullet">
    ///   <item>A rolling log-file sink via <see cref="LogFileSinkProvider"/>.</item>
    ///   <item>Crash capture via <see cref="CrashCapture.Install"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="appName">
    /// The application name; used for log file and crash record paths.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddPhoenixmlDiagnostics(
        this IServiceCollection services,
        string appName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(appName);

        CrashCapture.Install(appName);

        services.AddLogging(builder =>
        {
            builder.AddProvider(new LogFileSinkProvider(appName));
        });

        return services;
    }
}
