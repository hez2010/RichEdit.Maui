#if DEBUG && ANDROID
namespace RichEdit.Maui.Tests;

internal static class RtfClipboardExchangeTests
{
    internal static async Task RunAsync(string action)
    {
        var output = Path.Combine(FileSystem.CacheDirectory, "rtf-clipboard-exchange.txt");
        try
        {
            if (action == "publish")
            {
                await RichTextClipboard.SetAsync(RichTextDocumentFragment.FromRtf(@"{\rtf1\ansi From {\b library} \u26085?\u26412?}"));
                File.WriteAllText(output, "PASS published library RTF");
            }
            else
            {
                var fragment = await RichTextClipboard.GetAsync();
                if (fragment?.Text != "External peer 日本" || !fragment.Snapshot.Runs.Any(run => run.Format.Bold))
                    throw new InvalidOperationException("The external RTF text or bold formatting was lost.");
                File.WriteAllText(output, "PASS received external RTF with bold and Unicode");
            }
        }
        catch (Exception error) { File.WriteAllText(output, "FAIL " + error); }
    }
}
#endif
