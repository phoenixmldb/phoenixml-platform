# Phoenixml.Platform.Editor.Xml

XML/XSD vocabulary binding for Phoenixml.Platform. Concrete IDocument and ITreeNodeProvider implementations against PhoenixmlDb.Core's XDM, plus an XSD schema validation pipeline that surfaces errors as IDiagnostic for ProblemsView.

```csharp
using Phoenixml.Platform.Editor.Xml;

var doc = await XmlDocument.LoadAsync("orders.xml", CancellationToken.None);
documentService.Add(doc);

// Wire it into the foundation's tree view:
var treeView = new TreeEditorView();
treeView.SetProvider(doc.TreeNodes);
```

Apache-2.0. See https://github.com/phoenixmldb/phoenixml-platform.
