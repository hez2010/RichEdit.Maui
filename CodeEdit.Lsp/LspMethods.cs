using System.Text.Json;

namespace CodeEdit.Lsp;

/// <summary>Standard typed LSP contracts shared by stream and in-process connections.</summary>
/// <remarks>Use LspRequest and LspNotification with source-generated metadata for additional protocol methods or extensions.</remarks>
public static class LspMethods
{
    /// <summary>The initialize request.</summary>
    public static LspRequest<InitializeParams, InitializeResult> Initialize { get; } = new("initialize", LspJsonContext.Default.InitializeParams, LspJsonContext.Default.InitializeResult);
    /// <summary>The initialized notification.</summary>
    public static LspNotification<JsonElement?> Initialized { get; } = new("initialized", LspJsonContext.Default.NullableJsonElement);
    /// <summary>The shutdown request.</summary>
    public static LspRequest<JsonElement?, JsonElement?> Shutdown { get; } = new("shutdown", LspJsonContext.Default.NullableJsonElement, LspJsonContext.Default.NullableJsonElement);
    /// <summary>The exit notification.</summary>
    public static LspNotification<JsonElement?> Exit { get; } = new("exit", LspJsonContext.Default.NullableJsonElement);
    /// <summary>The textDocument/didOpen notification.</summary>
    public static LspNotification<DidOpenTextDocumentParams> DidOpen { get; } = new("textDocument/didOpen", LspJsonContext.Default.DidOpenTextDocumentParams);
    /// <summary>The textDocument/didChange notification.</summary>
    public static LspNotification<DidChangeTextDocumentParams> DidChange { get; } = new("textDocument/didChange", LspJsonContext.Default.DidChangeTextDocumentParams);
    /// <summary>The textDocument/didClose notification.</summary>
    public static LspNotification<DidCloseTextDocumentParams> DidClose { get; } = new("textDocument/didClose", LspJsonContext.Default.DidCloseTextDocumentParams);
    /// <summary>The textDocument/didSave notification.</summary>
    public static LspNotification<DidSaveTextDocumentParams> DidSave { get; } = new("textDocument/didSave", LspJsonContext.Default.DidSaveTextDocumentParams);
    /// <summary>The textDocument/publishDiagnostics notification.</summary>
    public static LspNotification<PublishDiagnosticsParams> PublishDiagnostics { get; } = new("textDocument/publishDiagnostics", LspJsonContext.Default.PublishDiagnosticsParams);
    /// <summary>The textDocument/semanticTokens/full request.</summary>
    public static LspRequest<TextDocumentParams, SemanticTokens?> SemanticTokensFull { get; } = new("textDocument/semanticTokens/full", LspJsonContext.Default.TextDocumentParams, LspJsonContext.Default.SemanticTokens);
    /// <summary>The textDocument/semanticTokens/range request.</summary>
    public static LspRequest<TextDocumentRangeParams, SemanticTokens?> SemanticTokensRange { get; } = new("textDocument/semanticTokens/range", LspJsonContext.Default.TextDocumentRangeParams, LspJsonContext.Default.SemanticTokens);
    /// <summary>The workspace/semanticTokens/refresh request.</summary>
    public static LspRequest<JsonElement?, JsonElement?> SemanticTokensRefresh { get; } = new("workspace/semanticTokens/refresh", LspJsonContext.Default.NullableJsonElement, LspJsonContext.Default.NullableJsonElement);
    /// <summary>The textDocument/completion request.</summary>
    public static LspRequest<CompletionParams, CompletionList?> Completion { get; } = new("textDocument/completion", LspJsonContext.Default.CompletionParams, LspJsonContext.Default.CompletionList);
    /// <summary>The completionItem/resolve request.</summary>
    public static LspRequest<CompletionItem, CompletionItem> ResolveCompletion { get; } = new("completionItem/resolve", LspJsonContext.Default.CompletionItem, LspJsonContext.Default.CompletionItem);
    /// <summary>The textDocument/hover request.</summary>
    public static LspRequest<TextDocumentPositionParams, Hover?> Hover { get; } = new("textDocument/hover", LspJsonContext.Default.TextDocumentPositionParams, LspJsonContext.Default.Hover);
    /// <summary>The textDocument/signatureHelp request.</summary>
    public static LspRequest<TextDocumentPositionParams, JsonElement?> SignatureHelp { get; } = PositionRequest("textDocument/signatureHelp");
    /// <summary>The textDocument/definition request. Results retain Location or LocationLink forms.</summary>
    public static LspRequest<TextDocumentPositionParams, JsonElement?> Definition { get; } = PositionRequest("textDocument/definition");
    /// <summary>The textDocument/declaration request.</summary>
    public static LspRequest<TextDocumentPositionParams, JsonElement?> Declaration { get; } = PositionRequest("textDocument/declaration");
    /// <summary>The textDocument/typeDefinition request.</summary>
    public static LspRequest<TextDocumentPositionParams, JsonElement?> TypeDefinition { get; } = PositionRequest("textDocument/typeDefinition");
    /// <summary>The textDocument/implementation request.</summary>
    public static LspRequest<TextDocumentPositionParams, JsonElement?> Implementation { get; } = PositionRequest("textDocument/implementation");
    /// <summary>The textDocument/references request.</summary>
    public static LspRequest<ReferenceParams, LspLocation[]?> References { get; } = new("textDocument/references", LspJsonContext.Default.ReferenceParams, LspJsonContext.Default.LspLocationArray);
    /// <summary>The textDocument/documentSymbol request. Results retain hierarchical or flat symbol forms.</summary>
    public static LspRequest<TextDocumentParams, JsonElement?> DocumentSymbols { get; } = DocumentRequest("textDocument/documentSymbol");
    /// <summary>The textDocument/foldingRange request.</summary>
    public static LspRequest<TextDocumentParams, JsonElement?> FoldingRanges { get; } = DocumentRequest("textDocument/foldingRange");
    /// <summary>The textDocument/formatting request.</summary>
    public static LspRequest<DocumentFormattingParams, LspTextEdit[]?> Formatting { get; } = new("textDocument/formatting", LspJsonContext.Default.DocumentFormattingParams, LspJsonContext.Default.LspTextEditArray);
    /// <summary>The textDocument/rename request. The complete WorkspaceEdit is returned to the application.</summary>
    public static LspRequest<RenameParams, JsonElement?> Rename { get; } = new("textDocument/rename", LspJsonContext.Default.RenameParams, LspJsonContext.Default.NullableJsonElement);

    private static LspRequest<TextDocumentPositionParams, JsonElement?> PositionRequest(string method) =>
        new(method, LspJsonContext.Default.TextDocumentPositionParams, LspJsonContext.Default.NullableJsonElement);
    private static LspRequest<TextDocumentParams, JsonElement?> DocumentRequest(string method) =>
        new(method, LspJsonContext.Default.TextDocumentParams, LspJsonContext.Default.NullableJsonElement);
}
