# Phoenixml.Platform

Foundation library for the [Phoenixml](https://phoenixml.dev) family of XML-aware tools — Editor, Workbench, Studio. Apache-2.0.

## What it is

The shared, vocabulary-agnostic substrate every Phoenixml app builds on:

- **Document model** — `IDocument`, dirty tracking, command-based undo/redo with merge support, multi-document services.
- **Editor primitives** (interfaces; UI controls land later) — tree/text view contracts, find/replace, problems pane.
- **Account** (interfaces) — license, registration, update channel. Implementations consume the licensing API documented separately.
- **Localization** — `IStringLocalizer` seam from day one; English-only ships in v1, expansion is demand-driven.

## Why one package

We deliberately ship the foundation as a single rolled-up NuGet rather than 8+ thin packages. Reasoning lives in the design spec §2.5: every foundation namespace is a transitive dependency of every other anyway, and we already manage plenty of NuGet artifacts in the family. Internal modularity is preserved at the namespace level and enforced via architecture tests.

## Quick start

```csharp
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Documents;

// Define a concrete document by deriving from Document.
public sealed class MyDocument : Document
{
    public MyDocument(Uri source)
        : base(DocumentId.New(), source, displayName: "MyDoc", DocumentKind.Xml) { }

    public override ValueTask SaveAsync(CancellationToken ct)
    {
        // …persist…
        MarkClean();
        return default;
    }
}

// Edits happen via commands on the undo stack.
var doc = new MyDocument(new Uri("file:///tmp/foo.xml"));
doc.Undo.Execute(new MyEditCommand(...));
doc.Undo.Undo();
```

## Used by

- **Phoenixml.Editor** (forthcoming) — XML/XSD authoring app.
- **Phoenixml.Workbench** (forthcoming) — XSLT/XQuery authoring app.

## Contributing

Pull requests welcome. By submitting a contribution you agree it is licensed under Apache-2.0 and that you have the right to do so (DCO: include a `Signed-off-by:` line in commit messages, or use `git commit --signoff`).

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
