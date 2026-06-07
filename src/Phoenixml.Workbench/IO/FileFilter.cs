using System.Collections.Generic;

namespace Phoenixml.Workbench.IO;

/// <summary>
/// Shell-neutral description of a file-picker filter group (e.g. "XML files" → *.xml, *.xsd).
/// Mapped to whatever native filter type the shell uses (Avalonia FilePickerFileType, Godot pattern, etc.).
/// </summary>
public sealed record FileFilter(string Label, IReadOnlyList<string> Patterns);
