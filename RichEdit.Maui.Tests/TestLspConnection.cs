using System.Text.Json;
using CodeEdit.Lsp;

namespace RichEdit.Maui.Tests;

// In-process connection shared by the tests and TestApp.
internal sealed class TestLspConnection : LspConnection
{
    private readonly CancellationTokenSource _lifetime;
    private readonly bool _serialize;
    private TestLspConnection _peer = null!;

    private TestLspConnection(CancellationTokenSource lifetime, bool serialize) { _lifetime = lifetime; _serialize = serialize; }

    internal static (TestLspConnection Client, TestLspConnection Server) CreatePair(bool serialize = false)
    {
        var lifetime = new CancellationTokenSource();
        var client = new TestLspConnection(lifetime, serialize);
        var server = new TestLspConnection(lifetime, serialize);
        client._peer = server;
        server._peer = client;
        return (client, server);
    }

    public override async Task<TResult> RequestAsync<TParams, TResult>(LspRequest<TParams, TResult> request, TParams parameters,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        return await Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (!_serialize) return await _peer.DispatchRequestAsync(request, parameters, token).ConfigureAwait(false);
            using var payload = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(parameters, request.Parameters));
            var result = await _peer.DispatchRequestAsync(request.Method, payload.RootElement, token).ConfigureAwait(false);
            return Deserialize(result, request.Result);
        }, token).WaitAsync(token).ConfigureAwait(false);
    }

    public override async Task NotifyAsync<TParams>(LspNotification<TParams> notification, TParams parameters,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        await Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (!_serialize) await _peer.DispatchNotificationAsync(notification, parameters, token).ConfigureAwait(false);
            else
            {
                using var payload = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(parameters, notification.Parameters));
                await _peer.DispatchNotificationAsync(notification.Method, payload.RootElement, token).ConfigureAwait(false);
            }
        }, token).WaitAsync(token).ConfigureAwait(false);
    }

    public override ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        return ValueTask.CompletedTask;
    }
}
