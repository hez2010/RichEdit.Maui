using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeEdit.Lsp;

namespace RichEdit.Maui.Tests;

// In-process sample server shared by the tests and TestApp.
internal sealed class TestLanguageServer : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.Ordinal);
    private readonly TestLspConnection _server;
    private readonly TestCSharpClassifier _classifier = new();
    private int _semanticTokenRequests;
    internal LspClient Client { get; }
    internal int SemanticTokenRequests => Volatile.Read(ref _semanticTokenRequests);
    internal Func<string, CancellationToken, Task<SemanticTokens?>>? SemanticTokensHandler { get; set; }
    internal TestLanguageServer(SemanticTokensLegend? legend = null)
    {
        var (client, server) = TestLspConnection.CreatePair();
        _server = server;
        var capabilities = JsonNode.Parse("""
            { "textDocumentSync": 1, "semanticTokensProvider": {
              "legend": { "tokenTypes": ["keyword", "string", "comment", "number", "macro"], "tokenModifiers": [] }, "full": true
            } }
            """)!.AsObject();
        if (legend is not null) capabilities["semanticTokensProvider"]!["legend"] = JsonSerializer.SerializeToNode(legend, LspJsonContext.Default.SemanticTokensLegend);
        server.OnRequest(LspMethods.Initialize, (_, _) => Task.FromResult(new InitializeResult(capabilities)));
        server.OnNotification(LspMethods.DidOpen, (parameters, _) =>
        {
            _documents[parameters.TextDocument.Uri] = parameters.TextDocument.Text;
            return Task.CompletedTask;
        });
        server.OnNotification(LspMethods.DidChange, (parameters, _) =>
        {
            _documents[parameters.TextDocument.Uri] = parameters.ContentChanges.Single().Text;
            return Task.CompletedTask;
        });
        server.OnNotification(LspMethods.DidClose, (parameters, cancellationToken) =>
        {
            _documents.TryRemove(parameters.TextDocument.Uri, out _);
            return Task.CompletedTask;
        });
        server.OnRequest(LspMethods.SemanticTokensFull, (parameters, token) =>
        {
            Interlocked.Increment(ref _semanticTokenRequests);
            var text = _documents[parameters.TextDocument.Uri];
            return SemanticTokensHandler is { } handler ? handler(text, token) : Task.FromResult<SemanticTokens?>(Classify(text, token));
        });
        server.OnRequest(LspMethods.Shutdown, (_, _) => Task.FromResult<System.Text.Json.JsonElement?>(null));
        Client = Task.Run(() => LspClient.ConnectAsync(client)).GetAwaiter().GetResult();
    }

    internal Task PublishDiagnosticsAsync(PublishDiagnosticsParams parameters) => _server.NotifyAsync(LspMethods.PublishDiagnostics, parameters);

    private SemanticTokens Classify(string text, CancellationToken token)
    {
        var tokens = _classifier.Highlight(text, token);
        var data = new List<int>();
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
        var lastLine = 0;
        var lastCharacter = 0;
        foreach (var item in tokens)
        {
            var offset = item.Range.Start;
            while (offset < item.Range.End)
            {
                if (text[offset] is '\n' or '\r') { offset++; continue; }
                var line = lastLine;
                while (line + 1 < starts.Count && starts[line + 1] <= offset) line++;
                var position = new LspPosition(line, offset - starts[line]);
                var end = text.IndexOfAny(['\r', '\n'], offset);
                if (end < 0 || end > item.Range.End) end = item.Range.End;
                data.AddRange([position.Line - lastLine, position.Line == lastLine ? position.Character - lastCharacter : position.Character,
                    end - offset, (int)item.Kind, 0]);
                lastLine = position.Line;
                lastCharacter = position.Character;
                offset = end;
            }
        }
        return new([.. data]);
    }

    public void Dispose() => Task.Run(async () => await Client.DisposeAsync()).GetAwaiter().GetResult();
}
