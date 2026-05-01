using NetArchTest.Rules;
using Xunit;

namespace Phoenixml.Platform.ArchTests;

public class NamespaceLayeringTests
{
    private static readonly System.Reflection.Assembly PlatformAssembly =
        typeof(Phoenixml.Platform.Abstractions.Documents.IDocument).Assembly;

    [Fact]
    public void AbstractionsHasNoOutwardDependencies()
    {
        // Abstractions can depend on Abstractions (any sub-namespace) and BCL only —
        // never on Documents, Shell, Hosting, Editing, etc.
        var result = Types.InAssembly(PlatformAssembly)
            .That()
            .ResideInNamespace("Phoenixml.Platform.Abstractions")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Phoenixml.Platform.Documents",
                "Phoenixml.Platform.Shell",
                "Phoenixml.Platform.Hosting",
                "Phoenixml.Platform.Editing",
                "Phoenixml.Platform.Account",     // implementation namespace, distinct from Abstractions.Account
                "Phoenixml.Platform.Connections",
                "Phoenixml.Platform.Diagnostics",
                "Phoenixml.Platform.Theming"
            )
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Abstractions namespace must not depend on any implementation namespace. " +
            "Failures: " + string.Join(", ",
                result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void DocumentsDependsOnlyOnAbstractions()
    {
        var result = Types.InAssembly(PlatformAssembly)
            .That()
            .ResideInNamespace("Phoenixml.Platform.Documents")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Phoenixml.Platform.Shell",
                "Phoenixml.Platform.Hosting",
                "Phoenixml.Platform.Editing",
                "Phoenixml.Platform.Connections"
            )
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Documents may only depend on Abstractions (and BCL). Failures: " +
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void NoAvaloniaReferenceInTheLibrary()
    {
        // The whole pure-logic-slice premise: Phoenixml.Platform must not reference Avalonia
        // at any point (until/unless Shell + Editing land in this assembly later).
        var result = Types.InAssembly(PlatformAssembly)
            .ShouldNot()
            .HaveDependencyOn("Avalonia")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Phoenixml.Platform must not depend on Avalonia in this build. " +
            "Failures: " + string.Join(", ",
                result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void NoSystemReactiveReferenceInTheLibrary()
    {
        // Per §3.1: events use Action<T>, not IObservable<T>. Catches accidental imports.
        var result = Types.InAssembly(PlatformAssembly)
            .ShouldNot()
            .HaveDependencyOn("System.Reactive")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Phoenixml.Platform must not depend on System.Reactive. " +
            "Failures: " + string.Join(", ",
                result.FailingTypeNames ?? Array.Empty<string>()));
    }
}
