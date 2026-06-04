using System.Text.Json.Serialization;

namespace Phoenixml.Workbench.Lsp;

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

public sealed record LspTextDocumentIdentifier(
    [property: JsonPropertyName("uri")] string Uri);

public sealed record LspTextDocumentPositionParams(
    [property: JsonPropertyName("textDocument")] LspTextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] LspPosition Position);

public sealed record LspCompletionItem(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("kind")] int Kind);

public sealed record LspCompletionList(
    [property: JsonPropertyName("isIncomplete")] bool IsIncomplete,
    [property: JsonPropertyName("items")] LspCompletionItem[] Items);

public sealed record LspHover(
    [property: JsonPropertyName("contents")] string? Contents);

// Plan 28 additions
public sealed record LspDocumentSymbolParams(
    [property: JsonPropertyName("textDocument")] LspTextDocumentIdentifier TextDocument);

public sealed record LspDocumentSymbol(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] int Kind,
    [property: JsonPropertyName("range")] LspRange Range,
    [property: JsonPropertyName("selectionRange")] LspRange SelectionRange);

public sealed record LspLocation(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("range")] LspRange Range);

public sealed record LspSignatureInformation(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("documentation")] string? Documentation);

public sealed record LspSignatureHelp(
    [property: JsonPropertyName("signatures")] LspSignatureInformation[] Signatures,
    [property: JsonPropertyName("activeSignature")] int ActiveSignature,
    [property: JsonPropertyName("activeParameter")] int ActiveParameter);

public sealed record LspReferenceContext(
    [property: JsonPropertyName("includeDeclaration")] bool IncludeDeclaration);

public sealed record LspReferenceParams(
    [property: JsonPropertyName("textDocument")] LspTextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] LspPosition Position,
    [property: JsonPropertyName("context")] LspReferenceContext Context);
