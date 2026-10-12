using System.Collections.Immutable;
using System.Text;
using RichEdit.Core;
using RichEdit.Core.Rtf;

namespace RichEdit.Maui;

internal static class RichTextClipboard
{
    private sealed record Payload(string? Rtf, string? Text)
    {
        internal RichTextDocumentFragment? Legacy { get; init; }
        internal RichTextDocumentFragment[]? LegacyParts { get; init; }
        internal RichDocumentFragment? Structured { get; init; }
        internal RichDocumentFragment[]? StructuredParts { get; init; }
    }
    internal sealed record StructuredContent(RichDocumentFragment Fragment, bool IsPlainText,
        IReadOnlyList<RichDocumentFragment>? Parts, ImmutableArray<RtfDiagnostic> Diagnostics);
    private static Payload? _cache;
    private static RichTextDocumentFragment? _plainProcessFragment;
#if WINDOWS
    private static uint _processClipboardSequence;
    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetClipboardSequenceNumber();
#elif IOS || MACCATALYST
    private static nint _processClipboardChangeCount = -1;
#elif ANDROID
    private const string RichFragmentTokenFormat = "com.richedit.maui.fragment-token";
    private static string? _processFragmentToken;
#endif

    internal static IReadOnlyList<RichTextDocumentFragment>? GetSelectionFragments(RichTextDocumentFragment fragment) =>
        ReferenceEquals(fragment, _cache?.Legacy) ? _cache?.LegacyParts : ReferenceEquals(fragment, _plainProcessFragment)
            ? _cache?.LegacyParts?.Select(item => RichTextDocumentFragment.FromPlainText(item.Text)).ToArray() : null;

    public static bool HasContent
    {
        get
        {
#if WINDOWS
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            return content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Rtf) || content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text);
#elif IOS || MACCATALYST
            return UIKit.UIPasteboard.General.Contains(["public.rtf"]) || UIKit.UIPasteboard.General.HasStrings;
#elif ANDROID
            var clipboard = (Android.Content.ClipboardManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.ClipboardService);
            return clipboard?.PrimaryClipDescription is { } description &&
                (description.HasMimeType(Platforms.Android.RtfClipboardProvider.MimeType) || description.HasMimeType("text/rtf") || description.HasMimeType("text/plain"));
#else
            return Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.HasText;
#endif
        }
    }

    public static Task SetAsync(RichTextDocumentFragment fragment, IReadOnlyList<RichTextDocumentFragment>? selections = null)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        return SetPayloadAsync(new(fragment.RtfText, fragment.Text) { Legacy = fragment, LegacyParts = selections?.ToArray() });
    }

    internal static async Task SetStructuredAsync(RichDocumentFragment fragment, IReadOnlyList<RichDocumentFragment>? parts = null)
    {
        using var stream = new MemoryStream();
        await RichEdit.Core.Rtf.RtfCodec.WriteAsync(fragment.Content, stream);
        await SetPayloadAsync(new(Encoding.ASCII.GetString(stream.GetBuffer(), 0, checked((int)stream.Length)), fragment.Content.ProjectText().Text.ToString())
        { Structured = fragment, StructuredParts = parts?.ToArray() });
    }

    private static async Task SetPayloadAsync(Payload payload)
    {
#if WINDOWS
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(payload.Text!);
        package.SetRtf(payload.Rtf!);
        await WindowsClipboardAccess.SetAsync(package);
#elif IOS || MACCATALYST
        using var data = Foundation.NSData.FromArray(Encoding.Latin1.GetBytes(payload.Rtf!));
        using var text = new Foundation.NSString(payload.Text!);
        using var item = Foundation.NSDictionary.FromObjectsAndKeys([text, data],
            [new Foundation.NSString("public.utf8-plain-text"), new Foundation.NSString("public.rtf")]);
        UIKit.UIPasteboard.General.Items = [item];
        await Task.CompletedTask;
#elif ANDROID
        var token = Guid.NewGuid().ToString("N");
        var context = Android.App.Application.Context;
        var clipboard = (Android.Content.ClipboardManager?)context.GetSystemService(Android.Content.Context.ClipboardService) ??
            throw new InvalidOperationException("The Android clipboard service is unavailable.");
        using var intent = new Android.Content.Intent();
        intent.PutExtra(RichFragmentTokenFormat, token);
        using var text = new Java.Lang.String(payload.Text!);
        using var uri = await Platforms.Android.RtfClipboardProvider.StoreAsync(payload.Rtf!);
        using var item = new Android.Content.ClipData.Item(text, null, intent, uri);
        using var label = new Java.Lang.String("RichEdit.Maui");
        using var description = new Android.Content.ClipDescription(label,
            [Platforms.Android.RtfClipboardProvider.MimeType, Android.Content.ClipDescription.MimetypeTextPlain]);
        using var clip = new Android.Content.ClipData(description, item);
        clipboard.PrimaryClip = clip;
#else
        await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(payload.Text!);
#endif
#if WINDOWS
        _processClipboardSequence = GetClipboardSequenceNumber();
#elif IOS || MACCATALYST
        _processClipboardChangeCount = UIKit.UIPasteboard.General.ChangeCount;
#elif ANDROID
        _processFragmentToken = token;
#endif
        _cache = payload;
        _plainProcessFragment = RichTextDocumentFragment.FromPlainText(payload.Text!);
    }

    public static async Task<RichTextDocumentFragment?> GetAsync(bool asPlainText = false)
    {
        var payload = await GetPayloadAsync(asPlainText);
        if (payload is null) return null;
        if (ReferenceEquals(payload, _cache) && payload.Legacy is { } owned) return asPlainText ? _plainProcessFragment : owned;
        if (!asPlainText && payload.Rtf is { } rtf)
            try { return RichTextDocumentFragment.FromRtf(rtf); }
            catch (Exception error) when (error is FormatException or ArgumentException) { }
        return payload.Text is { } text ? RichTextDocumentFragment.FromPlainText(text) : null;
    }

    internal static async Task<StructuredContent?> GetStructuredAsync(bool asPlainText)
    {
        var payload = await GetPayloadAsync(asPlainText);
        if (payload is null) return null;
        if (ReferenceEquals(payload, _cache) && payload.Structured is { } owned)
            return new(asPlainText ? RichDocumentFragment.FromPlainText(payload.Text!) : owned, asPlainText,
                asPlainText ? payload.StructuredParts?.Select(part => RichDocumentFragment.FromPlainText(part.Content.ProjectText().Text.ToString())).ToArray() : payload.StructuredParts, []);
        return await ReadStructuredTransferAsync(asPlainText ? null : payload.Rtf, payload.Text);
    }

    internal static async Task<StructuredContent?> ReadStructuredTransferAsync(string? rtf, string? text)
    {
        var diagnostics = ImmutableArray<RtfDiagnostic>.Empty;
        if (rtf is not null)
        {
            try
            {
                using var stream = new MemoryStream(Encoding.Latin1.GetBytes(rtf), writable: false);
                var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(stream, new() { MaximumBytes = 64 * 1024 * 1024 });
                return new(new(imported.Snapshot), false, null, imported.Diagnostics);
            }
            catch (Exception error) when (error is FormatException or ArgumentException or InvalidDataException)
            { diagnostics = [new("clipboard", "The RTF is invalid; plain text is available instead.")]; }
        }
        return text is not null ? new(RichDocumentFragment.FromPlainText(text), true, null, diagnostics) : null;
    }

    private static async Task<Payload?> GetPayloadAsync(bool asPlainText)
    {
        string? rtf = null;
#if WINDOWS
        var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        var sequence = GetClipboardSequenceNumber();
        var text = content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) ? await content.GetTextAsync() : null;
        if (sequence != 0 && sequence == _processClipboardSequence && sequence == GetClipboardSequenceNumber() && _cache is { } cached && cached.Text == text) return cached;
        if (!asPlainText && content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Rtf)) rtf = await content.GetRtfAsync();
#elif IOS || MACCATALYST
        var pasteboard = UIKit.UIPasteboard.General;
        var text = pasteboard.String;
        if (pasteboard.ChangeCount == _processClipboardChangeCount && _cache is { } cached && cached.Text == text) return cached;
        using var data = asPlainText ? null : pasteboard.DataForPasteboardType("public.rtf");
        if (data is not null) rtf = Encoding.Latin1.GetString(data.ToArray());
#elif ANDROID
        var context = Android.App.Application.Context;
        var clipboard = (Android.Content.ClipboardManager?)context.GetSystemService(Android.Content.Context.ClipboardService);
        var clip = clipboard?.PrimaryClip;
        var item = clip is { ItemCount: > 0 } ? clip.GetItemAt(0) : null;
        var text = item?.CoerceToText(context)?.ToString();
        var token = item?.Intent?.GetStringExtra(RichFragmentTokenFormat);
        if (token is not null && token == _processFragmentToken && _cache is { } cached && cached.Text == text) return cached;
        if (!asPlainText && item?.Uri is { } uri)
            try { rtf = await Platforms.Android.RtfClipboardProvider.ReadAsync(context.ContentResolver!, uri); }
            catch (Exception error) when (error is IOException or Java.Lang.SecurityException or Java.IO.FileNotFoundException) { }
#else
        var clipboard = Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default;
        var text = clipboard.HasText ? await clipboard.GetTextAsync() : null;
#endif
        return text is null && rtf is null ? null : new(rtf, text);
    }
}
