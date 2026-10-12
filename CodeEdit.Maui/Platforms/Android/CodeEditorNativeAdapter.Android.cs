using Microsoft.Maui.Platform;
using RichEdit.Maui.Platforms.Android;

namespace CodeEdit.Maui;

internal sealed partial class CodeEditorNativeAdapter
{
    private RichEditText PlatformView = null!;

    internal partial Color? GetTextColor() => new global::Android.Graphics.Color(PlatformView.CurrentTextColor).ToColor();

    internal partial bool Post(Action action) => PlatformView.Post(action);

    internal partial async Task<bool> PrepareViewportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!PlatformView.IsAttachedToWindow) return false;
        if (!PlatformView.IsLayoutRequested) return true;
        var observer = PlatformView.ViewTreeObserver!;
        var completion = new TaskCompletionSource<bool>();
        void Draw(object? sender, EventArgs args) => completion.TrySetResult(true);
        void Detached(object? sender, EventArgs args) => completion.TrySetResult(false);
        observer.PreDraw += Draw;
        PlatformView.ViewDetachedFromWindow += Detached;
        try { return await completion.Task.WaitAsync(cancellationToken); }
        finally
        {
            if (observer.IsAlive) observer.PreDraw -= Draw;
            PlatformView.ViewDetachedFromWindow -= Detached;
        }
    }

    private partial void Connect() => PlatformView = _handler.PlatformView;

    private partial void Disconnect() { }

    private partial void UpdateConfigurationCore() => PlatformView.SetHorizontallyScrolling(!Owner.WordWrap);
}
