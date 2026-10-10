namespace RichEdit.Maui;

/// <summary>Allows an application to replace or cancel source-text paste before committing it.</summary>
public sealed class TextPastingEventArgs(string text) : EventArgs
{
    /// <summary>Gets or sets the exact source text to insert.</summary>
    public string Text { get; set; } = text;
    /// <summary>Gets or sets whether paste is canceled.</summary>
    public bool Cancel { get; set; }
}

internal static class PlainTextClipboard
{
    private static string? _text;
    private static string[]? _parts;
#if WINDOWS
    private static uint _sequence;
    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetClipboardSequenceNumber();
#elif IOS || MACCATALYST
    private static nint _sequence = -1;
#elif ANDROID
    private const string TokenFormat = "com.textedit.source.selection-token";
    private static string? _token;
#endif

    internal static async Task SetAsync(string[] parts)
    {
        var text = string.Join("\n", parts);
#if WINDOWS
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(text);
        await WindowsClipboardAccess.SetAsync(data);
        _sequence = GetClipboardSequenceNumber();
#elif IOS || MACCATALYST
        UIKit.UIPasteboard.General.String = text;
        _sequence = UIKit.UIPasteboard.General.ChangeCount;
#elif ANDROID
        var clipboard = (Android.Content.ClipboardManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.ClipboardService)!;
        _token = Guid.NewGuid().ToString("N");
        using var intent = new Android.Content.Intent();
        intent.PutExtra(TokenFormat, _token);
        using var value = new Java.Lang.String(text);
        using var item = new Android.Content.ClipData.Item(value, null, intent, null);
        using var label = new Java.Lang.String("Source text");
        using var description = new Android.Content.ClipDescription(label, [Android.Content.ClipDescription.MimetypeTextPlain]);
        using var data = new Android.Content.ClipData(description, item);
        clipboard.PrimaryClip = data;
#else
        await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(text);
#endif
        _text = text;
        _parts = parts;
        await Task.CompletedTask;
    }

    internal static async Task<(string? Text, string[]? Parts)> GetAsync()
    {
        bool cached;
        string? text;
#if WINDOWS
        var sequence = GetClipboardSequenceNumber();
        var data = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        text = data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) ? await data.GetTextAsync() : null;
        cached = sequence != 0 && sequence == _sequence && sequence == GetClipboardSequenceNumber();
#elif IOS || MACCATALYST
        var pasteboard = UIKit.UIPasteboard.General;
        var sequence = pasteboard.ChangeCount;
        text = pasteboard.String;
        cached = sequence == _sequence && sequence == pasteboard.ChangeCount;
#elif ANDROID
        var context = Android.App.Application.Context;
        var clipboard = (Android.Content.ClipboardManager?)context.GetSystemService(Android.Content.Context.ClipboardService);
        var data = clipboard?.PrimaryClip;
        var item = data is { ItemCount: > 0 } ? data.GetItemAt(0) : null;
        text = item?.CoerceToText(context)?.ToString();
        cached = _token is not null && item?.Intent?.GetStringExtra(TokenFormat) == _token;
#else
        text = await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.GetTextAsync();
        cached = false;
#endif
        await Task.CompletedTask;
        return (text, cached && text == _text ? _parts : null);
    }
}
