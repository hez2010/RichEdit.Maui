using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeEdit.Lsp;

namespace RichEdit.Maui.Tests;

public sealed class LspTests
{
    private static void Check(bool value, string message) => Assert.True(value, message);
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    private static (TestLspConnection Client, TestLspConnection Server, ConcurrentQueue<string> Calls) Server(bool serialized, JsonObject capabilities)
    {
        var pair = TestLspConnection.CreatePair(serialized);
        var calls = new ConcurrentQueue<string>();
        pair.Server.OnRequest(LspMethods.Initialize, (parameters, _) =>
        {
            calls.Enqueue("initialize");
            Check(parameters.Capabilities["general"]!["positionEncodings"]![0]!.GetValue<string>() == "utf-16", "UTF-16 was not negotiated.");
            Check(parameters.Capabilities["textDocument"]!["semanticTokens"]!["augmentsSyntaxTokens"]!.GetValue<bool>() == false, "Semantic tokens replace the lexical API.");
            return Task.FromResult(new InitializeResult(capabilities));
        });
        pair.Server.OnNotification(LspMethods.Initialized, (_, _) => { calls.Enqueue("initialized"); return Task.CompletedTask; });
        pair.Server.OnRequest(LspMethods.Shutdown, (_, _) => { calls.Enqueue("shutdown"); return Task.FromResult<JsonElement?>(null); });
        pair.Server.OnNotification(LspMethods.Exit, (_, _) => { calls.Enqueue("exit"); return Task.CompletedTask; });
        return (pair.Client, pair.Server, calls);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task SynchronizationAsync(bool serialized, int kind)
    {
        var (endpoint, server, calls) = Server(serialized, JsonNode.Parse($$"""
            { "textDocumentSync": { "openClose": true, "change": {{kind}}, "save": { "includeText": true } } }
            """)!.AsObject());
        var texts = new ConcurrentDictionary<string, string>();
        var versions = new ConcurrentDictionary<string, int>();
        server.OnNotification(LspMethods.DidOpen, (parameters, _) =>
        {
            var item = parameters.TextDocument;
            Check(calls.Last() == "initialized" || calls.Last().StartsWith("open:"), "didOpen arrived before initialized.");
            texts[item.Uri] = item.Text;
            versions[item.Uri] = item.Version;
            calls.Enqueue($"open:{item.Uri}");
            return Task.CompletedTask;
        });
        server.OnNotification(LspMethods.DidChange, (parameters, _) =>
        {
            var uri = parameters.TextDocument.Uri;
            Check(parameters.TextDocument.Version > versions[uri], "Document versions must increase.");
            foreach (var change in parameters.ContentChanges)
            {
                var previous = texts[uri];
                Check((kind == 2) == (change.Range is not null), "The negotiated change kind was ignored.");
                if (change.Range is { } range)
                {
                    var start = LspText.GetOffset(previous, range.Start);
                    var end = LspText.GetOffset(previous, range.End);
                    texts[uri] = previous[..start] + change.Text + previous[end..];
                }
                else texts[uri] = change.Text;
            }
            versions[uri] = parameters.TextDocument.Version;
            calls.Enqueue($"change:{uri}");
            return Task.CompletedTask;
        });
        server.OnNotification(LspMethods.DidSave, (parameters, _) =>
        {
            Check(parameters.Text == texts[parameters.TextDocument.Uri], "Saved text differs from synchronized source.");
            calls.Enqueue("save");
            return Task.CompletedTask;
        });
        server.OnNotification(LspMethods.DidClose, (parameters, cancellationToken) => { texts.TryRemove(parameters.TextDocument.Uri, out _); calls.Enqueue("close"); return Task.CompletedTask; });
        var client = await LspClient.ConnectAsync(endpoint, cancellationToken: TestContext.Current.CancellationToken);
        var first = client.OpenDocument(new("untitled:first"), "csharp", "a😀\r\nb");
        var second = client.OpenDocument(new("untitled:second"), "python", "other");
        await Task.WhenAll(first.SynchronizeAsync(cancellationToken: TestContext.Current.CancellationToken), second.SynchronizeAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.ThrowsAny<InvalidOperationException>(() => client.OpenDocument(first.Uri, "csharp", "duplicate"));
        var firstUpdate = first.UpdateTextAsync("a😃\r\nb");
        var nextUpdate = first.UpdateTextAsync("prefix\na😃\r\nb\n");
        await Task.WhenAll(firstUpdate, nextUpdate, second.UpdateTextAsync("other!"));
        Check(texts[first.Uri.AbsoluteUri] == first.CurrentSnapshot.Text && texts[second.Uri.AbsoluteUri] == "other!", "A queued update was lost or applied to the wrong document.");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => first.NotifySavedAsync(1));
        await first.NotifySavedAsync(first.CurrentSnapshot.Version);
        await first.DisposeAsync();
        Check(texts.Count == 1, "Closing one document closed another document.");
        await client.DisposeAsync();
        Check(texts.IsEmpty, "Client disposal failed to close documents.");
        Check(calls.TakeLast(2).SequenceEqual((string[])["shutdown", "exit"]), "Shutdown must precede exit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAsync(bool serialized)
    {
        var (endpoint, server, _) = Server(serialized, new JsonObject { ["textDocumentSync"] = 1, ["hoverProvider"] = true });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnRequest(LspMethods.Hover, async (_, token) =>
        {
            using var registration = token.Register(() => cancellationObserved.TrySetResult());
            started.TrySetResult();
            await release.Task; // Simulates a server returning an obsolete response despite cancellation.
            return (Hover?)new Hover(Json("\"old\""));
        });
        await using var client = await LspClient.ConnectAsync(endpoint, cancellationToken: TestContext.Current.CancellationToken);
        await using var document = client.OpenDocument(new("untitled:cancellation"), "csharp", "old");
        var pending = document.GetHoverAsync(new(0, 0), cancellationToken: TestContext.Current.CancellationToken);
        await started.Task;
        await document.UpdateTextAsync("new");
        await cancellationObserved.Task;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        release.TrySetResult();
        await document.SynchronizeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Check(document.CurrentSnapshot.Text == "new", "Canceled response overwrote current source.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeaturesAsync(bool serialized)
    {
        var (endpoint, server, _) = Server(serialized, JsonNode.Parse("""
            { "textDocumentSync": 1, "completionProvider": { "resolveProvider": true }, "hoverProvider": true,
              "definitionProvider": true, "referencesProvider": true, "documentFormattingProvider": true, "renameProvider": true }
            """)!.AsObject());
        server.OnRequest(LspMethods.Completion, (parameters, _) =>
        {
            Check(parameters.Position == new LspPosition(0, 2), "Completion coordinates changed.");
            return Task.FromResult<CompletionList?>(new(false, [new() { Label = "Write", Data = Json("{\"id\":42}") }]));
        });
        server.OnRequest(LspMethods.ResolveCompletion, (item, _) =>
        {
            Check(item.Data!.Value.GetProperty("id").GetInt32() == 42, "Completion data was lost.");
            return Task.FromResult(item with { Detail = "resolved" });
        });
        server.OnRequest(LspMethods.Hover, (_, _) => Task.FromResult<Hover?>(new(Json("{\"kind\":\"markdown\",\"value\":\"**type**\"}"))));
        server.OnRequest(LspMethods.Definition, (_, _) => Task.FromResult<JsonElement?>(Json("[{\"targetUri\":\"file:///target.cs\",\"targetRange\":{\"start\":{\"line\":0,\"character\":0},\"end\":{\"line\":0,\"character\":1}}}]")));
        server.OnRequest(LspMethods.References, (_, _) => Task.FromResult<LspLocation[]?>([new("file:///ref.cs", new(new(0, 0), new(0, 1)))]));
        server.OnRequest(LspMethods.Formatting, (parameters, _) =>
        {
            Check(parameters.Options["tabSize"]!.GetValue<int>() == 4, "Formatting options were lost.");
            return Task.FromResult<LspTextEdit[]?>([new(new(new(0, 0), new(0, 2)), "new")]);
        });
        server.OnRequest(LspMethods.Rename, (parameters, _) =>
        {
            Check(parameters.NewName == "renamed", "Rename parameters changed.");
            return Task.FromResult<JsonElement?>(Json("{\"changes\":{}}"));
        });
        await using var client = await LspClient.ConnectAsync(endpoint, cancellationToken: TestContext.Current.CancellationToken);
        await using var document = client.OpenDocument(new("untitled:features"), "csharp", "😀");
        var completions = await document.GetCompletionsAsync(new(0, 2), cancellationToken: TestContext.Current.CancellationToken);
        var resolved = await document.ResolveCompletionAsync(completions!.Items.Single(), cancellationToken: TestContext.Current.CancellationToken);
        Check(resolved.Detail == "resolved", "Completion resolve failed.");
        Check((await document.GetHoverAsync(new(0, 0), cancellationToken: TestContext.Current.CancellationToken))!.Contents.GetProperty("kind").GetString() == "markdown", "Hover markup was lost.");
        Check((await document.GetDefinitionAsync(new(0, 0), cancellationToken: TestContext.Current.CancellationToken))!.Value[0].GetProperty("targetUri").GetString() == "file:///target.cs", "Definition links were lost.");
        Check((await document.GetReferencesAsync(new(0, 0), cancellationToken: TestContext.Current.CancellationToken))!.Single().Uri == "file:///ref.cs", "References failed.");
        Check((await document.FormatAsync(cancellationToken: TestContext.Current.CancellationToken))!.Single().NewText == "new", "Formatting edits were lost.");
        Check((await document.RenameAsync(new(0, 0), "renamed", cancellationToken: TestContext.Current.CancellationToken))!.Value.TryGetProperty("changes", out _), "WorkspaceEdit was lost.");
        var diagnostics = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DiagnosticsPublished += (_, parameters) => diagnostics.TrySetResult(parameters);
        await server.NotifyAsync(LspMethods.PublishDiagnostics, new(document.Uri.AbsoluteUri, [new() { Range = new(new(0, 0), new(0, 2)), Message = "test" }], 1), cancellationToken: TestContext.Current.CancellationToken);
        Check((await diagnostics.Task).Diagnostics.Single().Message == "test", "Diagnostics were not delivered.");
        var refreshed = false;
        client.SemanticTokensRefreshRequested += (_, _) => refreshed = true;
        await server.RequestAsync(LspMethods.SemanticTokensRefresh, null, cancellationToken: TestContext.Current.CancellationToken);
        Check(refreshed, "Semantic token refresh was not delivered.");
        var configuration = await server.RequestAsync("workspace/configuration", Json("{\"items\":[{\"section\":\"first\"},{\"section\":\"second\"}]}"), cancellationToken: TestContext.Current.CancellationToken);
        Check(configuration!.Value.GetArrayLength() == 2 && configuration.Value[0].ValueKind == JsonValueKind.Null, "Default configuration must return a null per item.");
        await Assert.ThrowsAnyAsync<LanguageServerException>(() => server.RequestAsync("unknown/request", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RangeTokensAsync(bool serialized)
    {
        var (endpoint, server, _) = Server(serialized, JsonNode.Parse("""
            { "textDocumentSync": 1, "semanticTokensProvider": { "range": true,
              "legend": { "tokenTypes": ["keyword"], "tokenModifiers": [] } } }
            """)!.AsObject());
        server.OnRequest(LspMethods.SemanticTokensRange, (parameters, _) =>
        {
            Check(parameters.Range == new LspRange(new(0, 0), new(1, 0)), "Range fallback must cover the complete document, including the trailing line.");
            return Task.FromResult<SemanticTokens?>(new([0, 0, 5, 0, 0]));
        });
        await using var client = await LspClient.ConnectAsync(endpoint, cancellationToken: TestContext.Current.CancellationToken);
        await using var document = client.OpenDocument(new("untitled:range"), "csharp", "class\n");
        var token = (await document.GetSemanticTokensAsync(cancellationToken: TestContext.Current.CancellationToken)).Single();
        Check(token.Start == 0 && token.Length == 5 && token.Type == "keyword", "Range semantic tokens were not decoded.");
    }

    [Fact]
    public async Task DirectIdentityAsync()
    {
        var (client, server) = TestLspConnection.CreatePair();
        var item = new CompletionItem { Label = "same object" };
        using var registration = server.OnRequest(LspMethods.ResolveCompletion, (parameters, _) =>
        {
            Check(ReferenceEquals(item, parameters), "The in-process path serialized the parameters.");
            return Task.FromResult(parameters);
        });
        Check(ReferenceEquals(item, await client.RequestAsync(LspMethods.ResolveCompletion, item, cancellationToken: TestContext.Current.CancellationToken)), "The in-process path serialized the result.");
        registration.Dispose();
        await Assert.ThrowsAnyAsync<LanguageServerException>(() => client.RequestAsync(LspMethods.ResolveCompletion, item, cancellationToken: TestContext.Current.CancellationToken));
        await client.DisposeAsync();
    }

    [Fact]
    public void TokenValidation()
    {
        const string text = "😀 class\r\nname\u2028value\n";
        var legend = new SemanticTokensLegend(["keyword", "customType"], ["readonly", "static"]);
        var tokens = LspText.DecodeSemanticTokens(text, new([0, 3, 5, 0, 0, 1, 0, 4, 1, 3, 0, 5, 5, 1, 0]), legend, cancellationToken: TestContext.Current.CancellationToken);
        Check(tokens.Count == 3 && tokens[0].Start == 3 && tokens[1].Start == 10 && tokens[2].Start == 15, "UTF-16, CRLF, or U+2028 positions were decoded incorrectly.");
        Check(tokens[1].Modifiers.SequenceEqual((string[])["readonly", "static"]), "Modifier bits were decoded incorrectly.");
        Check(LspText.GetPosition(text, 15) == new LspPosition(1, 5), "U+2028 is not an LSP line terminator.");
        Check(LspText.DecodeSemanticTokens("abc\nnext", new([0, 1, 100, 0, 0]), legend, cancellationToken: TestContext.Current.CancellationToken).Single().Length == 2, "Single-line tokens must be clipped at line end.");
        Assert.ThrowsAny<InvalidDataException>(() => LspText.DecodeSemanticTokens("abc", new([0, 0]), legend, cancellationToken: TestContext.Current.CancellationToken));
        Assert.ThrowsAny<InvalidDataException>(() => LspText.DecodeSemanticTokens("abc", new([0, 0, 2, 0, 0, 0, 1, 1, 0, 0]), legend, cancellationToken: TestContext.Current.CancellationToken));
        Assert.ThrowsAny<InvalidDataException>(() => LspText.DecodeSemanticTokens("abc", new([0, 0, 1, 5, 0]), legend, cancellationToken: TestContext.Current.CancellationToken));
        Assert.ThrowsAny<InvalidDataException>(() => LspText.DecodeSemanticTokens("abc", new([int.MaxValue, 0, 1, 0, 0]), legend, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void IncrementalEdits()
    {
        string[] texts = ["", "a😀b", "a😃b", "a\r\nb", "a\nb", "a\rb", "\n", "😀", "😃\r\nlast\n", "a\u2028b"];
        foreach (var before in texts)
        foreach (var after in texts)
        {
            var change = LspText.CreateChange(before, after);
            var range = change.Range!.Value;
            var start = LspText.GetOffset(before, range.Start);
            var end = LspText.GetOffset(before, range.End);
            Check(before[..start] + change.Text + before[end..] == after, "Incremental synchronization corrupted source.");
            Check(!(start > 0 && start < before.Length && char.IsLowSurrogate(before[start]) && char.IsHighSurrogate(before[start - 1])), "The change split a surrogate pair.");
        }
    }

    [Fact]
    public void CompletionUnions()
    {
        var array = JsonSerializer.Deserialize("[{\"label\":\"one\",\"data\":{\"custom\":true},\"futureProperty\":17}]", LspJsonContext.Default.CompletionList)!;
        Check(!array.IsIncomplete && array.Items[0].Extensions!["futureProperty"].GetInt32() == 17, "Completion array or extensions were lost.");
        var list = JsonSerializer.Deserialize("{\"isIncomplete\":true,\"items\":[{\"label\":\"two\"}],\"itemDefaults\":{\"commitCharacters\":[\".\"]}}", LspJsonContext.Default.CompletionList)!;
        Check(list.IsIncomplete && list.ItemDefaults!.Value.GetProperty("commitCharacters")[0].GetString() == ".", "CompletionList defaults were lost.");
    }

    [Fact]
    public async Task CapabilityFailuresAsync()
    {
        var (endpoint, _, _) = Server(false, new JsonObject { ["textDocumentSync"] = 1 });
        await using (var client = await LspClient.ConnectAsync(endpoint, cancellationToken: TestContext.Current.CancellationToken))
        {
            await using var document = client.OpenDocument(new("untitled:unsupported"), "plaintext", "source");
            Check((await document.GetSemanticTokensAsync(cancellationToken: TestContext.Current.CancellationToken)).Count == 0 && await document.GetHoverAsync(new(0, 0), cancellationToken: TestContext.Current.CancellationToken) is null, "Unsupported features must not be requested.");
            var completion = new CompletionItem { Label = "unresolved" };
            Check(ReferenceEquals(completion, await document.ResolveCompletionAsync(completion, cancellationToken: TestContext.Current.CancellationToken)), "Unsupported completion resolve must preserve the item.");
        }
        var incompatible = Server(true, new JsonObject { ["positionEncoding"] = "utf-8" });
        await Assert.ThrowsAnyAsync<NotSupportedException>(() => LspClient.ConnectAsync(incompatible.Client, cancellationToken: TestContext.Current.CancellationToken));
    }
}
