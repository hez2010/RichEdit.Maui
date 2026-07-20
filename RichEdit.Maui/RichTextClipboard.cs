namespace RichEdit.Maui;

internal static class RichTextClipboard
{
#if ANDROID
    private const string RichFragmentTokenExtra = "RichEdit.Maui.FragmentToken";
    private static string? _processFragmentToken;
    private static RichTextDocumentFragment? _processFragment;
#endif

    public static bool HasContent =>
        Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.HasText;

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
        pasteboard.String = fragment.Text;
        using var data = Foundation.NSData.FromString(
            fragment.RtfText,
            Foundation.NSStringEncoding.UTF8);
        pasteboard.SetData(data, "public.rtf");
        await Task.CompletedTask;
#elif ANDROID
        var context = Android.App.Application.Context;
        var clipboard = (Android.Content.ClipboardManager?)context.GetSystemService(
            Android.Content.Context.ClipboardService) ??
            throw new InvalidOperationException("The Android clipboard service is unavailable.");
        var token = Guid.NewGuid().ToString("N");
        using var intent = new Android.Content.Intent();
        intent.PutExtra(RichFragmentTokenExtra, token);
        using var text = new Java.Lang.String(fragment.Text);
        using var item = new Android.Content.ClipData.Item(text, null, intent, null);
        using var label = new Java.Lang.String("RichEdit.Maui");
        using var description = new Android.Content.ClipDescription(
            label,
            [Android.Content.ClipDescription.MimetypeTextPlain]);
        using var clip = new Android.Content.ClipData(description, item);
        clipboard.PrimaryClip = clip;
        _processFragmentToken = token;
        _processFragment = fragment;

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
#elif ANDROID
        var context = Android.App.Application.Context;
        var clipboard = (Android.Content.ClipboardManager?)context.GetSystemService(
            Android.Content.Context.ClipboardService);
        var clip = clipboard?.PrimaryClip;
        var item = clip is { ItemCount: > 0 } ? clip.GetItemAt(0) : null;
        var text = item?.CoerceToText(context)?.ToString();
        var token = item?.Intent?.GetStringExtra(RichFragmentTokenExtra);
        if (token is not null &&
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
