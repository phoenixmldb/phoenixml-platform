using System.Text.Json.Serialization;

namespace Phoenixml.Editor.Lsp;

// Minimal LSP subset duplicated client-side. Same shape as the server-side DTOs.
// Plan 26 candidate: factor into a shared PhoenixmlDb.Lsp.Common library.

public sealed record LspPosition(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character);

public sealed record LspRange(
    [property: JsonPropertyName("start")] LspPosition Start,
    [property: JsonPropertyName("end")] LspPosition End);

public sealed record LspDiagnostic(
    [property: JsonPropertyName("range")] LspRange Range,
    [property: JsonPropertyName("severity")] int Severity,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("message")] string Message);

public sealed record LspTextDocumentItem(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("languageId")] string LanguageId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("text")] string Text);

public sealed record LspVersionedTextDocumentIdentifier(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("version")] int Version);

public sealed record LspTextDocumentContentChangeEvent(
    [property: JsonPropertyName("text")] string Text);

public sealed record LspDidOpenTextDocumentParams(
    [property: JsonPropertyName("textDocument")] LspTextDocumentItem TextDocument);

public sealed record LspDidChangeTextDocumentParams(
    [property: JsonPropertyName("textDocument")] LspVersionedTextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("contentChanges")] LspTextDocumentContentChangeEvent[] ContentChanges);

public sealed record LspPublishDiagnosticsParams(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("diagnostics")] LspDiagnostic[] Diagnostics);
