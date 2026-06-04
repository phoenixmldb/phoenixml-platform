using System.IO;
using Phoenixml.Platform.Abstractions.Documents;

namespace Phoenixml.Workbench.Lsp;

/// <summary>
/// Maps a <see cref="DocumentKind"/> to the on-disk path of its LSP server binary.
/// Convention: <c>&lt;rootDir&gt;/tools/phoenixmldb-{kind}-lsp</c>. The binaries are
/// produced by <c>dotnet publish</c> of the LanguageServer projects into the editor's
/// <c>AppContext.BaseDirectory/tools</c>.
/// </summary>
public sealed class LspServerRegistry
{
    private readonly string _rootDir;

    /// <summary>Initializes a new registry rooted at <paramref name="rootDir"/>.</summary>
    public LspServerRegistry(string rootDir)
    {
        ArgumentNullException.ThrowIfNull(rootDir);
        _rootDir = rootDir;
    }

    /// <summary>Returns the absolute path for <paramref name="kind"/>'s server binary, or null if unsupported.</summary>
    public string? PathFor(DocumentKind kind) => kind switch
    {
        DocumentKind.XQuery => Path.Combine(_rootDir, "tools", "phoenixmldb-xquery-lsp"),
        DocumentKind.Xslt => Path.Combine(_rootDir, "tools", "phoenixmldb-xslt-lsp"),
        _ => null,
    };
}
