using NetArchTest.Rules;
using Xunit;

namespace Phoenixml.Platform.ArchTests;

// Note: at v0.2 the library brought in Avalonia. The arch test that previously forbade
// Avalonia references whole-library-wide is gone — the invariant it described was a
// v0.1-only thing. Other arch tests stay: namespace layering still matters.
//
// AbstractionsHasNoUiDependencies (added in v0.2) keeps the contracts namespace UI-free
// — that invariant IS permanent, because contracts are the natural seam if a future
// pure-logic-vs-Avalonia split happens.
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
    public void AbstractionsHasNoUiDependencies()
    {
        var result = Types.InAssembly(PlatformAssembly)
            .That()
            .ResideInNamespace("Phoenixml.Platform.Abstractions")
            .ShouldNot()
            .HaveDependencyOnAny("Avalonia", "System.Reactive")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Abstractions namespace must remain UI-free. Failures: " +
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
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
