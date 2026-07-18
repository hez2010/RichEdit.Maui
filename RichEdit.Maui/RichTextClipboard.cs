namespace RichEdit.Maui;

internal static class RichTextClipboard
{
    private static readonly object Gate = new();
    private static RichTextDocumentFragment? _processFragment;

    public static bool HasContent =>
        Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.HasText;

    public static async Task SetAsync(RichTextDocumentFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        lock (Gate)
        {
            _processFragment = fragment;
        }

#if WINDOWS
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(fragment.Text);
        package.SetRtf(fragment.RtfText);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        await Task.CompletedTask;
#elif IOS || MACCATALYST
        var pasteboard = UIKit.UIPasteboard.General;
        pasteboard.String = fragment.Text;
        using var data = Foundation.NSData.FromString(
            fragment.RtfText,
            Foundation.NSStringEncoding.UTF8);
        pasteboard.SetData(data, "public.rtf");
        await Task.CompletedTask;
#else
        await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(
            fragment.Text);
#endif
    }

    public static async Task<RichTextDocumentFragment?> GetAsync()
    {
#if WINDOWS
        var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Rtf))
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
        var data = pasteboard.DataForPasteboardType("public.rtf");
        if (data is not null)
        {
            using (data)
            {
                var rtf = Foundation.NSString.FromData(
                    data,
                    Foundation.NSStringEncoding.UTF8)?.ToString();
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
#else
        var clipboard = Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default;
        var text = clipboard.HasText ? await clipboard.GetTextAsync() : null;
#endif
        if (text is null)
        {
            return null;
        }

        lock (Gate)
        {
            if (_processFragment is { } cached &&
                string.Equals(cached.Text, text, StringComparison.Ordinal))
            {
                return cached;
            }
        }

        return RichTextDocumentFragment.FromPlainText(text);
    }
}
