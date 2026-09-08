using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CodeEdit.Lsp;

/// <summary>A typed LSP request and its compile-time JSON metadata.</summary>
/// <param name="Method">The standard or extension method name.</param>
/// <param name="Parameters">Source-generated parameter metadata.</param>
/// <param name="Result">Source-generated result metadata.</param>
public sealed record LspRequest<TParams, TResult>(string Method, JsonTypeInfo<TParams> Parameters, JsonTypeInfo<TResult> Result);

/// <summary>A typed LSP notification and its compile-time JSON metadata.</summary>
/// <param name="Method">The standard or extension method name.</param>
/// <param name="Parameters">Source-generated parameter metadata.</param>
public sealed record LspNotification<TParams>(string Method, JsonTypeInfo<TParams> Parameters);

/// <summary>The application-implemented connection contract and client handler registration.</summary>
/// <remarks>Implement request and notification delivery and disposal. Dispatch incoming messages through the protected dispatch methods.
/// The application chooses in-process calls or a transport and must invoke incoming handlers off the UI thread.</remarks>
public abstract class LspConnection : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, IRequestHandler> _requests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, INotificationHandler> _notifications = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the fallback for unregistered server requests.</summary>
    public Func<LanguageServerRequest, CancellationToken, Task<JsonElement?>>? RequestHandler { get; set; }
    /// <summary>Occurs for received notifications, including those with typed handlers.</summary>
    public event EventHandler<LanguageServerNotification>? NotificationReceived;

    /// <summary>Sends a typed LSP request.</summary>
    /// <typeparam name="TParams">The parameter type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="request">The method and generated serialization metadata.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's result.</returns>
    public abstract Task<TResult> RequestAsync<TParams, TResult>(LspRequest<TParams, TResult> request, TParams parameters,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a typed LSP notification.</summary>
    /// <typeparam name="TParams">The parameter type.</typeparam>
    /// <param name="notification">The method and generated serialization metadata.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels before transmission begins.</param>
    /// <returns>The notification operation.</returns>
    public abstract Task NotifyAsync<TParams>(LspNotification<TParams> notification, TParams parameters,
        CancellationToken cancellationToken = default);

    /// <summary>Sends any standard or extension LSP request using its JSON payload.</summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters, or null.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The complete JSON result, including union and extension properties.</returns>
    public Task<JsonElement?> RequestAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default) =>
        RequestAsync(new LspRequest<JsonElement?, JsonElement?>(method, LspJsonContext.Default.NullableJsonElement,
            LspJsonContext.Default.NullableJsonElement), parameters, cancellationToken);

    /// <summary>Sends any standard or extension LSP notification using its JSON payload.</summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters, or null.</param>
    /// <param name="cancellationToken">Cancels before transmission begins.</param>
    /// <returns>The notification operation.</returns>
    public Task NotifyAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default) =>
        NotifyAsync(new LspNotification<JsonElement?>(method, LspJsonContext.Default.NullableJsonElement), parameters, cancellationToken);

    /// <summary>Registers a typed request handler without scanning types or generating proxies.</summary>
    /// <typeparam name="TParams">The parameter type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="request">The method contract.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>A registration to dispose when the handler is no longer needed.</returns>
    public IDisposable OnRequest<TParams, TResult>(LspRequest<TParams, TResult> request, Func<TParams, CancellationToken, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var registration = new RegisteredRequestHandler<TParams, TResult>(request, handler);
        if (!_requests.TryAdd(request.Method, registration)) throw new InvalidOperationException($"A handler for '{request.Method}' is already registered.");
        return new Registration(() => _requests.TryRemove(new(request.Method, registration)));
    }

    /// <summary>Registers a typed notification handler.</summary>
    /// <typeparam name="TParams">The parameter type.</typeparam>
    /// <param name="notification">The method contract.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>A registration to dispose when the handler is no longer needed.</returns>
    public IDisposable OnNotification<TParams>(LspNotification<TParams> notification, Func<TParams, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var registration = new NotificationHandler<TParams>(notification, handler);
        if (!_notifications.TryAdd(notification.Method, registration)) throw new InvalidOperationException($"A handler for '{notification.Method}' is already registered.");
        return new Registration(() => _notifications.TryRemove(new(notification.Method, registration)));
    }

    /// <summary>Dispatches an incoming typed request directly when the handler's types match.</summary>
    /// <typeparam name="TParams">The parameter type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="request">The method contract.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="token">The incoming request cancellation.</param>
    /// <returns>The client handler's result.</returns>
    protected async Task<TResult> DispatchRequestAsync<TParams, TResult>(LspRequest<TParams, TResult> request, TParams parameters, CancellationToken token)
    {
        if (_requests.TryGetValue(request.Method, out var handler) && handler is RegisteredRequestHandler<TParams, TResult> typed)
            return await typed.Callback(parameters, token).ConfigureAwait(false);
        var result = await DispatchRequestAsync(request.Method, JsonSerializer.SerializeToElement(parameters, request.Parameters), token).ConfigureAwait(false);
        return Deserialize(result, request.Result);
    }

    /// <summary>Dispatches an incoming decoded JSON request using registered compile-time metadata.</summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="token">The incoming request cancellation.</param>
    /// <returns>The handler's JSON result.</returns>
    protected Task<JsonElement?> DispatchRequestAsync(string method, JsonElement? parameters, CancellationToken token)
    {
        if (_requests.TryGetValue(method, out var handler)) return handler.InvokeAsync(parameters, token);
        if (RequestHandler is { } fallback) return fallback(new(method, parameters), token);
        throw new LanguageServerException(-32601, $"No handler is registered for '{method}'.");
    }

    /// <summary>Dispatches an incoming typed notification directly when the handler's types match.</summary>
    /// <typeparam name="TParams">The parameter type.</typeparam>
    /// <param name="notification">The method contract.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="token">The connection cancellation.</param>
    /// <returns>The handler operation.</returns>
    protected async Task DispatchNotificationAsync<TParams>(LspNotification<TParams> notification, TParams parameters, CancellationToken token)
    {
        if (_notifications.TryGetValue(notification.Method, out var handler))
        {
            if (handler is NotificationHandler<TParams> typed) await typed.Callback(parameters, token).ConfigureAwait(false);
            else await handler.InvokeAsync(JsonSerializer.SerializeToElement(parameters, notification.Parameters), token).ConfigureAwait(false);
        }
        // A matching typed in-process handler never needs JSON serialization. Only raw observers do.
        if (NotificationReceived is { } observer)
            observer(this, new(notification.Method, JsonSerializer.SerializeToElement(parameters, notification.Parameters)));
    }

    /// <summary>Dispatches an incoming decoded JSON notification.</summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="token">The connection cancellation.</param>
    /// <returns>The handler operation.</returns>
    protected async Task DispatchNotificationAsync(string method, JsonElement? parameters, CancellationToken token)
    {
        if (_notifications.TryGetValue(method, out var handler)) await handler.InvokeAsync(parameters, token).ConfigureAwait(false);
        NotificationReceived?.Invoke(this, new(method, parameters));
    }

    /// <summary>Deserializes a result using supplied compile-time metadata.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="value">The JSON result, or null.</param>
    /// <param name="typeInfo">Source-generated metadata.</param>
    /// <returns>The deserialized result.</returns>
    protected static T Deserialize<T>(JsonElement? value, JsonTypeInfo<T> typeInfo) =>
        (value ?? JsonSerializer.SerializeToElement<JsonElement?>(null, LspJsonContext.Default.NullableJsonElement)).Deserialize(typeInfo)!;

    /// <summary>Closes the connection and cancels outstanding operations.</summary>
    /// <returns>The cleanup operation.</returns>
    public abstract ValueTask DisposeAsync();

    private interface IRequestHandler { Task<JsonElement?> InvokeAsync(JsonElement? parameters, CancellationToken token); }
    private sealed class RegisteredRequestHandler<TParams, TResult>(LspRequest<TParams, TResult> request,
        Func<TParams, CancellationToken, Task<TResult>> callback) : IRequestHandler
    {
        internal Func<TParams, CancellationToken, Task<TResult>> Callback => callback;
        public async Task<JsonElement?> InvokeAsync(JsonElement? parameters, CancellationToken token) =>
            JsonSerializer.SerializeToElement(await callback(Deserialize(parameters, request.Parameters), token).ConfigureAwait(false), request.Result);
    }
    private interface INotificationHandler { Task InvokeAsync(JsonElement? parameters, CancellationToken token); }
    private sealed class NotificationHandler<TParams>(LspNotification<TParams> notification,
        Func<TParams, CancellationToken, Task> callback) : INotificationHandler
    {
        internal Func<TParams, CancellationToken, Task> Callback => callback;
        public Task InvokeAsync(JsonElement? parameters, CancellationToken token) => callback(Deserialize(parameters, notification.Parameters), token);
    }
    private sealed class Registration(Action remove) : IDisposable
    {
        private Action? _remove = remove;
        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
}
