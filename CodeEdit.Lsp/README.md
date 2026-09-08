# CodeEdit.Lsp

A [Language Server Protocol](https://microsoft.github.io/language-server-protocol/) client for .NET 10. It has no MAUI or third-party dependencies. `CodeEdit.Maui` uses it for document synchronization, semantic coloring, and language-feature requests.

Applications implement `LspConnection`. The package ships no concrete connection, process launcher, socket client, or language server. The same typed contracts support direct in-process delivery and connections to existing remote or out-of-process servers.

## Connect a server

```csharp
using CodeEdit.Lsp;

// applicationConnection is your implementation of LspConnection.
await using var client = await LspClient.ConnectAsync(applicationConnection, new()
{
    RootUri = new Uri("file:///workspace/"),
});

await using var document = client.OpenDocument(
    new Uri("file:///workspace/Example.cs"), "csharp", source);

await document.UpdateTextAsync(updatedSource);
var tokens = await document.GetSemanticTokensAsync();
var completions = await document.GetCompletionsAsync(new LspPosition(3, 8));
var hover = await document.GetHoverAsync(new LspPosition(3, 8));
```

The client sends `initialize` and `initialized`, negotiates UTF-16 positions and server capabilities, and owns the lifetime of its document sessions. `RootUri`, `InitializationOptions`, and additional client `Capabilities` configure the server. Only advertise additional capabilities that the application handles. The built-in synchronization and semantic-token capabilities use static registration.

A client may be shared by multiple editors. There can be only one open session for a given URI on a client. Disposing a document sends `didClose`; disposing the client closes remaining documents, requests `shutdown`, sends `exit`, and disposes the connection. `LeaveConnectionOpen` lets the application retain ownership of connection cleanup. Client shutdown is bounded to five seconds; failures propagate from `DisposeAsync`.

## Implement a connection

Derive from `LspConnection` and implement these operations:

| Operation | Application responsibility |
|---|---|
| `RequestAsync<TParams, TResult>` | Deliver the specified LSP method and parameters, correlate its response, and honor cancellation |
| `NotifyAsync<TParams>` | Deliver a notification in order; complete after delivery and do not truncate a message after transmission begins |
| `DisposeAsync` | Stop delivery and unblock pending operations |

Incoming messages are delivered through the protected `DispatchRequestAsync` and `DispatchNotificationAsync` overloads. Invoke them off the UI thread. An adapter must preserve notification order, dispatch later document requests after preceding notifications, and continue receiving responses while a handler awaits another request. A JSON-RPC adapter also owns framing, request identifiers, protocol errors, `$/cancelRequest`, and connection failures.

For a serialized connection, `LspRequest.Parameters`, `LspRequest.Result`, and `LspNotification.Parameters` supply source-generated `JsonTypeInfo<T>`. Pass those to the corresponding `JsonSerializer` overloads. Incoming JSON dispatch uses the same metadata. Do not use serializer overloads that infer metadata from runtime types.

For an in-process connection, pass typed contracts and objects directly to the peer's typed dispatch methods. Register server handlers with `OnRequest` and `OnNotification`, for example:

```csharp
using var completionHandler = serverEndpoint.OnRequest(
    LspMethods.Completion,
    (CompletionParams request, CancellationToken cancellation) =>
        languageService.GetCompletionsAsync(request, cancellation));
```

Matching typed handlers receive the original parameter objects and return their results directly. They do not serialize or deserialize those objects. Treat these shared request and result objects as immutable. Raw JSON observers and fallback handlers opt into JSON conversion.

## Documents and requests

`LspDocument` queues `didOpen`, `didChange`, and `didClose` in order. It respects full or incremental synchronization. Incremental changes cover the changed source without splitting a surrogate pair or a CRLF pair. A source edit cancels requests for the preceding revision; it never cancels a queued source notification. Requests wait for preceding synchronization. Each document has its own queue, so one document does not block requests in another.

`CurrentSnapshot` contains the source and the LSP revision. After a successful application save, call `NotifySavedAsync(savedVersion)`. It sends `didSave` only when requested by the server, includes source only when requested, and rejects a save notification for a superseded revision. An editor's own source revision and its LSP revision are independent.

The named APIs cover semantic tokens, completion and resolve, hover, definitions, references, formatting, and rename. `LspMethods` also exposes typed contracts for signature help, declarations, type definitions, implementations, document symbols, and folding ranges. Optional features return empty or null results when the corresponding server capability is absent. Raw and generic request APIs provide access to other LSP methods and server extensions:

```csharp
var symbols = await document.RequestAsync(
    LspMethods.DocumentSymbols,
    new TextDocumentParams(new TextDocumentIdentifier(document.Uri.AbsoluteUri)));

// Adds this document's textDocument identifier and cancels on source changes.
var hints = await document.RequestAsync("textDocument/inlayHint", new()
{
    ["range"] = new System.Text.Json.Nodes.JsonObject
    {
        ["start"] = new System.Text.Json.Nodes.JsonObject { ["line"] = 0, ["character"] = 0 },
        ["end"] = new System.Text.Json.Nodes.JsonObject { ["line"] = 5, ["character"] = 0 },
    },
});
```

Use `client.Connection.RequestAsync` for workspace requests and item-resolution methods that do not take a document identifier. Applications can define additional typed `LspRequest<TParams, TResult>` and `LspNotification<TParams>` contracts with their own source-generated metadata. Union-valued results and extension data retain their LSP JSON representation.

`DiagnosticsPublished` and `SemanticTokensRefreshRequested` deliver server events. `LspClientOptions.RequestHandler` handles application requests such as `workspace/configuration`, `window/showMessageRequest`, or `workspace/applyEdit`; `NotificationHandler` receives notifications from the beginning of initialization. These callbacks run on the adapter's dispatch thread. Without a request handler, configuration returns one null per requested item, message choices return null, and workspace edits return `applied: false`. Unknown requests report JSON-RPC method-not-found.

Applications own completion UI, hover presentation, diagnostic decorations, navigation, and workspace edit application. Formatting and rename APIs return proposed changes. They do not write files or apply edits automatically.

## Semantic tokens and coordinates

The client requests full semantic tokens, falling back to a whole-document range request when only range tokens are available. It advertises the standard token types and modifiers and preserves names from the server's legend. The decoder checks relative positions, indices, integer bounds, and overlapping ranges. With multiline tokens disabled, lengths are clipped at line ends as specified by LSP. Token deltas and dynamic registration are not advertised.

`LspText` uses zero-based lines and UTF-16 characters. It recognizes CRLF, CR, and LF line endings. U+2028 remains an ordinary character for LSP coordinates. `CodeEditor` also has one-based visual/editor coordinates; use `GetLspPosition` for server requests. Text-edit conversion rejects out-of-bounds positions before editing.
