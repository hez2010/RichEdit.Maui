namespace RichEdit.Maui;

internal static class RichTextClipboard
{
    // Keep model values which RTF cannot encode, but only for the last copy while
    // the system clipboard still identifies that exact content.
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
    private static RichTextDocumentFragment? _processFragment;

    public static bool HasContent
    {
        get
        {
#if WINDOWS
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            return content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Rtf) ||
                content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text);
#elif IOS || MACCATALYST
            return UIKit.UIPasteboard.General.Contains(["public.rtf"]) ||
                UIKit.UIPasteboard.General.HasStrings;
#else
            return Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.HasText;
#endif
        }
    }

    public static async Task SetAsync(RichTextDocumentFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
#if WINDOWS
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(fragment.Text);
        package.SetRtf(fragment.RtfText);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        await Task.CompletedTask;
#elif IOS || MACCATALYST
        var pasteboard = UIKit.UIPasteboard.General;
        using var data = Foundation.NSData.FromString(
            fragment.RtfText,
            Foundation.NSStringEncoding.UTF8);
        using var text = new Foundation.NSString(fragment.Text);
        using var item = Foundation.NSDictionary.FromObjectsAndKeys(
            [text, data],
            [new Foundation.NSString("public.utf8-plain-text"), new Foundation.NSString("public.rtf")]);
        pasteboard.Items = [item];
        await Task.CompletedTask;
#elif ANDROID
        var token = Guid.NewGuid().ToString("N");
        var context = Android.App.Application.Context;
        var clipboard = (Android.Content.ClipboardManager?)context.GetSystemService(
            Android.Content.Context.ClipboardService) ??
            throw new InvalidOperationException("The Android clipboard service is unavailable.");
        using var intent = new Android.Content.Intent();
        intent.PutExtra(RichFragmentTokenFormat, token);
        using var text = new Java.Lang.String(fragment.Text);
        using var item = new Android.Content.ClipData.Item(text, null, intent, null);
        using var label = new Java.Lang.String("RichEdit.Maui");
        using var description = new Android.Content.ClipDescription(
            label,
            [Android.Content.ClipDescription.MimetypeTextPlain]);
        using var clip = new Android.Content.ClipData(description, item);
        clipboard.PrimaryClip = clip;
        await Task.CompletedTask;
#else
        await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(
            fragment.Text);
#endif
#if WINDOWS
        _processClipboardSequence = GetClipboardSequenceNumber();
#elif IOS || MACCATALYST
        _processClipboardChangeCount = UIKit.UIPasteboard.General.ChangeCount;
#elif ANDROID
        _processFragmentToken = token;
#endif
        _processFragment = fragment;
    }

    public static async Task<RichTextDocumentFragment?> GetAsync(bool asPlainText = false)
    {
#if WINDOWS
        var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        var sequence = _processClipboardSequence;
        if (!asPlainText && sequence != 0 && sequence == GetClipboardSequenceNumber() &&
            _processFragment is { } cached &&
            content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) &&
            string.Equals(cached.Text, await content.GetTextAsync(), StringComparison.Ordinal) &&
            sequence == GetClipboardSequenceNumber())
        {
            return cached;
        }

        if (!asPlainText && content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Rtf))
        {
            try
            {
                var rtf = await content.GetRtfAsync();
                return RichTextDocumentFragment.FromRtf(rtf);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                // Fall back to the portable plain-text representation.
            }
        }

        var text = content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)
            ? await content.GetTextAsync()
            : null;
#elif IOS || MACCATALYST
        var pasteboard = UIKit.UIPasteboard.General;
        if (!asPlainText && pasteboard.ChangeCount == _processClipboardChangeCount &&
            _processFragment is { } cached && string.Equals(cached.Text, pasteboard.String, StringComparison.Ordinal) &&
            pasteboard.ChangeCount == _processClipboardChangeCount)
        {
            return cached;
        }

        var data = asPlainText ? null : pasteboard.DataForPasteboardType("public.rtf");
        if (data is not null)
        {
            using (data)
            {
                var rtf = Foundation.NSString.FromData(
                    data,
                    Foundation.NSStringEncoding.UTF8)?.ToString() ??
                    System.Text.Encoding.Latin1.GetString(data.ToArray());
                if (!string.IsNullOrEmpty(rtf))
                {
                    try
                    {
                        return RichTextDocumentFragment.FromRtf(rtf);
                    }
                    catch (Exception exception) when (exception is FormatException or ArgumentException)
                    {
                        // Fall back to plain text.
                    }
                }
            }
        }

        var text = pasteboard.String;
#elif ANDROID
        var context = Android.App.Application.Context;
        var clipboard = (Android.Content.ClipboardManager?)context.GetSystemService(
            Android.Content.Context.ClipboardService);
        var clip = clipboard?.PrimaryClip;
        var item = clip is { ItemCount: > 0 } ? clip.GetItemAt(0) : null;
        var text = item?.CoerceToText(context)?.ToString();
        var token = item?.Intent?.GetStringExtra(RichFragmentTokenFormat);
        if (!asPlainText && token is not null &&
            string.Equals(token, _processFragmentToken, StringComparison.Ordinal) &&
            _processFragment is { } cached &&
            string.Equals(cached.Text, text, StringComparison.Ordinal))
        {
            return cached;
        }
#else
        var clipboard = Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default;
        var text = clipboard.HasText ? await clipboard.GetTextAsync() : null;
#endif
        if (text is null)
        {
            return null;
        }

        return RichTextDocumentFragment.FromPlainText(text);
    }
}
