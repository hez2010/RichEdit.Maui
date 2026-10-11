#if IOS || MACCATALYST
using System.Diagnostics;
using System.Runtime.Versioning;
using CoreGraphics;
using Foundation;
using UIKit;

namespace RichEdit.Maui.Tests;

internal static partial class SourcePerformanceWorkload
{
    // A small native-only comparison isolates layout-engine costs. These raw
    // samples are not the 100-sample editor acceptance workload.
    [SupportedOSPlatform("ios16.0")]
    [SupportedOSPlatform("maccatalyst16.0")]
    internal static async Task RunAppleNative(NativeEditorView editor)
    {
        var original = (UITextView)editor.Handler!.PlatformView!;
        var parent = original.Superview!;
        var hidden = original.Hidden;
        var focused = original.IsFirstResponder;
        var output = Path.Combine(EditorTestOutput.Directory, "source-native-layout-comparison.txt");
        File.WriteAllText(output, $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {BuildConfiguration}; {DeviceInfo.Model}\n");
        original.ResignFirstResponder();
        original.Hidden = true;
        try
        {
            foreach (var textKit2 in new[] { false, true })
            {
                using var native = UITextView.GetTextView(textKit2);
                native.Frame = original.Frame;
                native.Font = original.Font;
                native.BackgroundColor = UIColor.SystemBackground;
                native.TextColor = UIColor.Label;
                native.TextContainerInset = original.TextContainerInset;
                native.TextContainer.LineFragmentPadding = original.TextContainer.LineFragmentPadding;
                native.TextContainer.WidthTracksTextView = false;
                native.TextContainer.Size = new CGSize(nfloat.MaxValue, nfloat.MaxValue);
                native.AutocapitalizationType = UITextAutocapitalizationType.None;
                native.AutocorrectionType = UITextAutocorrectionType.No;
                native.SpellCheckingType = UITextSpellCheckingType.No;
                native.SmartQuotesType = UITextSmartQuotesType.No;
                native.SmartDashesType = UITextSmartDashesType.No;
                native.SmartInsertDeleteType = UITextSmartInsertDeleteType.No;
                parent.AddSubview(native);
                try
                {
                    native.Text = new string('x', 1024 * 1024) + "a\u0301 \U0001f600 אבג العربية";
                    native.BecomeFirstResponder();
                    await FrameOpportunity(TimeSpan.FromSeconds(60));
                    Check((native.TextLayoutManager is not null) == textKit2, "native layout engine changed unexpectedly");
                    File.AppendAllText(output, $"TextKit{(textKit2 ? 2 : 1)}: sample,position,navigationMs,inputMs,secondFrameMs\n");
                    for (var index = -2; index < 12; index++)
                    {
                        var length = (int)native.TextStorage.Length;
                        var position = index < 0 ? 0 : (index % 3) switch { 0 => 0, 1 => length / 2, _ => length };
                        var start = Stopwatch.GetTimestamp();
                        native.SelectedRange = new NSRange(position, 0);
                        native.ScrollRangeToVisible(new NSRange(position, 0));
                        var navigation = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        start = Stopwatch.GetTimestamp();
                        native.InsertText("x");
                        var input = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        await FrameOpportunity(TimeSpan.FromSeconds(60));
                        var frame = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        Check(native.TextStorage.Length == length + 1, "native baseline input failed");
                        if (index >= 0) File.AppendAllText(output, FormattableString.Invariant($"{index},{position},{navigation:F3},{input:F3},{frame:F3}\n"));
                    }
                }
                finally { native.ResignFirstResponder(); native.RemoveFromSuperview(); }
            }
        }
        finally
        {
            original.Hidden = hidden;
            if (focused) original.BecomeFirstResponder();
        }
    }
}
#endif
