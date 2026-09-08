using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CodeEdit.Lsp;

/// <summary>Identifies a protocol implementation.</summary>
/// <param name="Name">The implementation name.</param>
/// <param name="Version">The optional version.</param>
public sealed record LspImplementationInfo(string Name, string? Version = null);

/// <summary>An LSP workspace folder.</summary>
/// <param name="Uri">The absolute folder URI.</param>
/// <param name="Name">The display name.</param>
public sealed record WorkspaceFolder(string Uri, string Name);

/// <summary>The LSP initialize request.</summary>
/// <param name="Capabilities">Client capabilities, including any application extensions.</param>
/// <param name="ProcessId">The optional client process identifier.</param>
/// <param name="ClientInfo">Client implementation information.</param>
/// <param name="RootUri">The optional workspace root URI.</param>
/// <param name="InitializationOptions">Server-specific initialization data.</param>
/// <param name="WorkspaceFolders">The optional workspace folders.</param>
public sealed record InitializeParams(JsonObject Capabilities, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? ProcessId = null,
    LspImplementationInfo? ClientInfo = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? RootUri = null,
    JsonElement? InitializationOptions = null, WorkspaceFolder[]? WorkspaceFolders = null);

/// <summary>The LSP initialize result.</summary>
/// <param name="Capabilities">The complete server capabilities.</param>
/// <param name="ServerInfo">Optional server information.</param>
public sealed record InitializeResult(JsonObject Capabilities, LspImplementationInfo? ServerInfo = null);

/// <summary>A newly opened LSP document.</summary>
/// <param name="Uri">The document URI.</param>
/// <param name="LanguageId">The standard LSP language identifier.</param>
/// <param name="Version">The LSP revision.</param>
/// <param name="Text">The complete source.</param>
public sealed record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);

/// <summary>Parameters for textDocument/didOpen.</summary>
/// <param name="TextDocument">The opened document.</param>
public sealed record DidOpenTextDocumentParams(TextDocumentItem TextDocument);

/// <summary>A full or incremental source change.</summary>
/// <param name="Text">The new source or replacement text.</param>
/// <param name="Range">The replaced range, or null for full synchronization.</param>
public sealed record TextDocumentContentChangeEvent(string Text, LspRange? Range = null);

/// <summary>Parameters for textDocument/didChange.</summary>
/// <param name="TextDocument">The resulting document revision.</param>
/// <param name="ContentChanges">Changes applied in order.</param>
public sealed record DidChangeTextDocumentParams(VersionedTextDocumentIdentifier TextDocument, TextDocumentContentChangeEvent[] ContentChanges);

/// <summary>Parameters for textDocument/didClose.</summary>
/// <param name="TextDocument">The closed document.</param>
public sealed record DidCloseTextDocumentParams(TextDocumentIdentifier TextDocument);

/// <summary>Parameters for textDocument/didSave.</summary>
/// <param name="TextDocument">The saved document.</param>
/// <param name="Text">The saved text when requested by the server.</param>
public sealed record DidSaveTextDocumentParams(TextDocumentIdentifier TextDocument, string? Text = null);

/// <summary>Parameters for a document request.</summary>
/// <param name="TextDocument">The document identifier.</param>
public sealed record TextDocumentParams(TextDocumentIdentifier TextDocument);

/// <summary>Parameters for a document-range request.</summary>
/// <param name="TextDocument">The document identifier.</param>
/// <param name="Range">The source range.</param>
public sealed record TextDocumentRangeParams(TextDocumentIdentifier TextDocument, LspRange Range);

/// <summary>Parameters for textDocument/completion.</summary>
/// <param name="TextDocument">The document identifier.</param>
/// <param name="Position">The completion position.</param>
/// <param name="Context">The optional standard LSP completion context.</param>
public sealed record CompletionParams(TextDocumentIdentifier TextDocument, LspPosition Position, JsonElement? Context = null);

/// <summary>Parameters for textDocument/references.</summary>
/// <param name="TextDocument">The document identifier.</param>
/// <param name="Position">The symbol position.</param>
/// <param name="Context">Whether to include the declaration.</param>
public sealed record ReferenceParams(TextDocumentIdentifier TextDocument, LspPosition Position, ReferenceContext Context);

/// <summary>The reference-query context.</summary>
/// <param name="IncludeDeclaration">Whether to include the declaration.</param>
public sealed record ReferenceContext(bool IncludeDeclaration);

/// <summary>An LSP source location.</summary>
/// <param name="Uri">The document URI.</param>
/// <param name="Range">The source range.</param>
public sealed record LspLocation(string Uri, LspRange Range);

/// <summary>Parameters for textDocument/rename.</summary>
/// <param name="TextDocument">The document identifier.</param>
/// <param name="Position">The symbol position.</param>
/// <param name="NewName">The replacement symbol name.</param>
public sealed record RenameParams(TextDocumentIdentifier TextDocument, LspPosition Position, string NewName);

/// <summary>Parameters for textDocument/formatting.</summary>
/// <param name="TextDocument">The document identifier.</param>
/// <param name="Options">Standard formatting options and server-specific additions.</param>
public sealed record DocumentFormattingParams(TextDocumentIdentifier TextDocument, JsonObject Options);
