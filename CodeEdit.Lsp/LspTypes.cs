using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CodeEdit.Lsp;

/// <summary>A zero-based LSP line and UTF-16 character offset.</summary>
/// <param name="Line">The zero-based line.</param>
/// <param name="Character">The zero-based UTF-16 character offset.</param>
public readonly record struct LspPosition(int Line, int Character);

/// <summary>An LSP range with an exclusive end.</summary>
/// <param name="Start">The inclusive start.</param>
/// <param name="End">The exclusive end.</param>
public readonly record struct LspRange(LspPosition Start, LspPosition End);

/// <summary>An LSP text replacement.</summary>
/// <param name="Range">The replaced range in the original source.</param>
/// <param name="NewText">The replacement text.</param>
public sealed record LspTextEdit(LspRange Range, string NewText);

/// <summary>Identifies a text document.</summary>
/// <param name="Uri">The absolute document URI.</param>
public sealed record TextDocumentIdentifier(string Uri);

/// <summary>Identifies a version of a text document.</summary>
/// <param name="Uri">The absolute document URI.</param>
/// <param name="Version">The LSP revision.</param>
public sealed record VersionedTextDocumentIdentifier(string Uri, int Version);

/// <summary>Parameters for a request at a source position.</summary>
/// <param name="TextDocument">The document identifier.</param>
/// <param name="Position">The source position.</param>
public sealed record TextDocumentPositionParams(TextDocumentIdentifier TextDocument, LspPosition Position);

/// <summary>A language-server diagnostic. Extension fields retain their LSP JSON representation.</summary>
public sealed record LspDiagnostic
{
    /// <summary>Gets the source range.</summary>
    public required LspRange Range { get; init; }
    /// <summary>Gets the diagnostic message.</summary>
    public required string Message { get; init; }
    /// <summary>Gets the severity: 1 error, 2 warning, 3 information, or 4 hint.</summary>
    public int? Severity { get; init; }
    /// <summary>Gets the numeric or string diagnostic code.</summary>
    public JsonElement? Code { get; init; }
    /// <summary>Gets the diagnostic source.</summary>
    public string? Source { get; init; }
    /// <summary>Gets diagnostic tags.</summary>
    public int[]? Tags { get; init; }
    /// <summary>Gets server data for subsequent requests.</summary>
    public JsonElement? Data { get; init; }
    /// <summary>Gets additional LSP properties, including related information.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>A publishDiagnostics notification.</summary>
/// <param name="Uri">The document URI.</param>
/// <param name="Diagnostics">The replacement diagnostic collection.</param>
/// <param name="Version">The optional LSP revision to which diagnostics belong.</param>
public sealed record PublishDiagnosticsParams(string Uri, LspDiagnostic[] Diagnostics, int? Version = null);

/// <summary>An LSP completion item. Union-valued and extension properties retain their wire representation.</summary>
public sealed record CompletionItem
{
    /// <summary>Gets the visible label.</summary>
    public required string Label { get; init; }
    /// <summary>Gets the LSP completion-item kind.</summary>
    public int? Kind { get; init; }
    /// <summary>Gets additional detail.</summary>
    public string? Detail { get; init; }
    /// <summary>Gets documentation as a string or MarkupContent.</summary>
    public JsonElement? Documentation { get; init; }
    /// <summary>Gets the sorting key.</summary>
    public string? SortText { get; init; }
    /// <summary>Gets the filtering key.</summary>
    public string? FilterText { get; init; }
    /// <summary>Gets insertion text when no text edit is present.</summary>
    public string? InsertText { get; init; }
    /// <summary>Gets the insertion format: 1 plain text or 2 snippet.</summary>
    public int? InsertTextFormat { get; init; }
    /// <summary>Gets a TextEdit or InsertReplaceEdit.</summary>
    public JsonElement? TextEdit { get; init; }
    /// <summary>Gets additional edits in original-document coordinates.</summary>
    public LspTextEdit[]? AdditionalTextEdits { get; init; }
    /// <summary>Gets the optional command to execute after insertion.</summary>
    public JsonElement? Command { get; init; }
    /// <summary>Gets opaque data for completionItem/resolve.</summary>
    public JsonElement? Data { get; init; }
    /// <summary>Gets all remaining LSP properties.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>A completion response, normalized from an array or CompletionList.</summary>
/// <param name="IsIncomplete">Whether the server must be queried as the user types.</param>
/// <param name="Items">The completion items.</param>
/// <param name="ItemDefaults">Optional LSP defaults shared by the items.</param>
[JsonConverter(typeof(CompletionListConverter))]
public sealed record CompletionList(bool IsIncomplete, CompletionItem[] Items, JsonElement? ItemDefaults = null);

/// <summary>An LSP hover response.</summary>
/// <param name="Contents">MarkupContent, MarkedString, or a MarkedString array.</param>
/// <param name="Range">The optional relevant source range.</param>
public sealed record Hover(JsonElement Contents, LspRange? Range = null);

/// <summary>The server's semantic-token legend.</summary>
/// <param name="TokenTypes">Token type names indexed by the token stream.</param>
/// <param name="TokenModifiers">Modifier names indexed by bit position.</param>
public sealed record SemanticTokensLegend(string[] TokenTypes, string[] TokenModifiers);

/// <summary>A full or range semantic-token response.</summary>
/// <param name="Data">The relative token stream, with five integers per token.</param>
/// <param name="ResultId">An optional identifier for subsequent delta requests.</param>
public sealed record SemanticTokens(int[] Data, string? ResultId = null);

/// <summary>A decoded semantic token using UTF-16 source offsets and the server's names.</summary>
/// <param name="Start">The UTF-16 start offset.</param>
/// <param name="Length">The UTF-16 length.</param>
/// <param name="Type">The semantic-token type.</param>
/// <param name="Modifiers">The semantic-token modifiers.</param>
public sealed record SemanticToken(int Start, int Length, string Type, IReadOnlyList<string> Modifiers);

/// <summary>A language-server notification.</summary>
/// <param name="Method">The LSP method.</param>
/// <param name="Parameters">The parameters, or null when absent.</param>
public sealed record LanguageServerNotification(string Method, JsonElement? Parameters);

/// <summary>A request initiated by the server.</summary>
/// <param name="Method">The LSP method.</param>
/// <param name="Parameters">The parameters, or null when absent.</param>
public sealed record LanguageServerRequest(string Method, JsonElement? Parameters);

/// <summary>A JSON-RPC error returned by a language server or client handler.</summary>
public sealed class LanguageServerException : Exception
{
    /// <summary>Creates a protocol error.</summary>
    /// <param name="code">The JSON-RPC or LSP error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="errorData">Optional protocol error data.</param>
    public LanguageServerException(int code, string message, JsonElement? errorData = null) : base(message)
    { Code = code; ErrorData = errorData; }
    /// <summary>Gets the protocol error code.</summary>
    public int Code { get; }
    /// <summary>Gets the optional protocol error data.</summary>
    public JsonElement? ErrorData { get; }
}

/// <summary>Settings for an application-owned LSP connection.</summary>
public sealed class LspClientOptions
{
    /// <summary>Gets the workspace root, or null for a workspace without a root.</summary>
    public Uri? RootUri { get; init; }
    /// <summary>Gets server-specific initialization options.</summary>
    public JsonElement? InitializationOptions { get; init; }
    /// <summary>Gets additional client capabilities, merged into the built-in capabilities.</summary>
    /// <remarks>Only advertise capabilities the application implements. Position encoding remains UTF-16.</remarks>
    public JsonObject? Capabilities { get; init; }
    /// <summary>Gets an optional handler for server requests such as configuration, messages, or workspace edits.</summary>
    /// <remarks>Runs off the UI thread. Marshal UI work explicitly. Throw LanguageServerException to return a protocol error.</remarks>
    public Func<LanguageServerRequest, CancellationToken, Task<JsonElement?>>? RequestHandler { get; init; }
    /// <summary>Gets an optional notification handler installed before initialization.</summary>
    public Action<LanguageServerNotification>? NotificationHandler { get; init; }
    /// <summary>Gets whether disposing the client leaves its connection open.</summary>
    public bool LeaveConnectionOpen { get; init; }
}

/// <summary>Source-generated metadata for built-in LSP payloads. No reflection fallback is used.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(DidOpenTextDocumentParams))]
[JsonSerializable(typeof(DidChangeTextDocumentParams))]
[JsonSerializable(typeof(DidCloseTextDocumentParams))]
[JsonSerializable(typeof(DidSaveTextDocumentParams))]
[JsonSerializable(typeof(TextDocumentParams))]
[JsonSerializable(typeof(TextDocumentRangeParams))]
[JsonSerializable(typeof(CompletionParams))]
[JsonSerializable(typeof(ReferenceParams))]
[JsonSerializable(typeof(RenameParams))]
[JsonSerializable(typeof(DocumentFormattingParams))]
[JsonSerializable(typeof(LspLocation[]))]
[JsonSerializable(typeof(LspPosition))]
[JsonSerializable(typeof(LspRange))]
[JsonSerializable(typeof(LspTextEdit[]))]
[JsonSerializable(typeof(TextDocumentPositionParams))]
[JsonSerializable(typeof(VersionedTextDocumentIdentifier))]
[JsonSerializable(typeof(PublishDiagnosticsParams))]
[JsonSerializable(typeof(CompletionItem))]
[JsonSerializable(typeof(CompletionItem[]))]
[JsonSerializable(typeof(CompletionList))]
[JsonSerializable(typeof(Hover))]
[JsonSerializable(typeof(SemanticTokensLegend))]
[JsonSerializable(typeof(SemanticTokens))]
public partial class LspJsonContext : JsonSerializerContext;

internal sealed class CompletionListConverter : JsonConverter<CompletionList>
{
    public override CompletionList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var json = JsonDocument.ParseValue(ref reader);
        var value = json.RootElement;
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Array) return new(false, value.Deserialize(LspJsonContext.Default.CompletionItemArray)!);
        return new(value.GetProperty("isIncomplete").GetBoolean(),
            value.GetProperty("items").Deserialize(LspJsonContext.Default.CompletionItemArray)!,
            value.TryGetProperty("itemDefaults", out var defaults) ? defaults.Clone() : null);
    }

    public override void Write(Utf8JsonWriter writer, CompletionList value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("isIncomplete", value.IsIncomplete);
        writer.WritePropertyName("items");
        JsonSerializer.Serialize(writer, value.Items, LspJsonContext.Default.CompletionItemArray);
        if (value.ItemDefaults is { } defaults) { writer.WritePropertyName("itemDefaults"); defaults.WriteTo(writer); }
        writer.WriteEndObject();
    }
}
