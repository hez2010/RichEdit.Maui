#if ANDROID || IOS || MACCATALYST
using System.Diagnostics;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace RichEdit.Maui.TestApp;

internal static class CharacterFormattingTests
{
    internal static IEnumerable<EditorContractTests.Case> Cases
    {
        get
        {
            yield return new("formatting projection matches a full render across character formats", async editor =>
            {
                editor.TextColor = Colors.Black;
                editor.Document = RichTextDocument.FromPlainText("before styled after");
                foreach (var (name, format) in EditorContractTests.CharacterFormats())
                {
                    editor.Document.Edit(edit => edit.SetCharacterFormat(new(7, 6), format));
                    AssertMatchesFullRender(editor, [], name);
                    editor.Document.Edit(edit => edit.SetCharacterFormat(new(7, 6), RichTextCharacterFormat.Default));
                    AssertMatchesFullRender(editor, [], name + " cleared");
                }
                await Task.Delay(100);
            });

            yield return new("formatting projection preserves overlapping layers and authored content", async editor =>
            {
                editor.TextColor = Colors.Black;
                editor.Document = RichTextDocument.FromPlainText("before styled after");
                editor.Document.Edit(edit => edit.SetCharacterFormat(new(7, 6), new()
                {
                    FontSize = 20, FontWeight = 700, Italic = true, CharacterSpacing = 2,
                }));
                var source = editor.Document.CurrentSnapshot;
                var rtf = editor.Document.RtfText;
                editor.SelectionState = new(12, 4);
                using var first = editor.Decorations.CreateLayer();
                using var second = editor.Decorations.CreateLayer();
                RichTextDecoration[] lower = [new(new(1, 10), new() { BackgroundColor = Colors.Yellow, Underline = RichTextUnderlineStyle.Double })];
                RichTextDecoration[] upper = [new(new(4, 10), new() { ForegroundColor = Colors.Green, BackgroundColor = Colors.Pink })];
                first.TrySet(editor.Document.Revision, lower);
                second.TrySet(editor.Document.Revision, upper);
                AssertMatchesFullRender(editor, [lower, upper], "overlapping layers");
                lower = [new(new(2, 8), new() { BackgroundColor = Colors.Blue, Underline = RichTextUnderlineStyle.Wave })];
                first.TrySet(editor.Document.Revision, lower);
                AssertMatchesFullRender(editor, [lower, upper], "changed layer boundaries");
                second.Clear();
                AssertMatchesFullRender(editor, [lower], "upper layer cleared");
                first.Clear();
                AssertMatchesFullRender(editor, [], "all layers cleared");
                await Task.Delay(100);
                Check(ReferenceEquals(source, editor.Document.CurrentSnapshot), "Decorations changed the source snapshot.");
                Check(rtf == editor.Document.RtfText, "Decorations changed authored RTF.");
                Check(editor.SelectionState == new RichTextSelectionState(12, 4), "Decorations changed the selection.");
            });

            yield return new("formatting projection handles 2000 lines", async editor =>
            {
                const string line = "return 42;\n";
                editor.Document = RichTextDocument.FromPlainText(string.Concat(Enumerable.Repeat(line, 2000)));
                await Task.Delay(100);
                var source = editor.Document.CurrentSnapshot;
                using var layer = editor.Decorations.CreateLayer();
                var items = Enumerable.Range(0, 2000).SelectMany(index => new RichTextDecoration[]
                {
                    new(new(index * line.Length, 6), new() { ForegroundColor = Colors.Green }),
                    new(new(index * line.Length + 7, 2), new() { ForegroundColor = Colors.Blue }),
                }).ToArray();
                var timer = Stopwatch.StartNew();
                layer.TrySet(editor.Document.Revision, items);
                var initial = timer.Elapsed.TotalMilliseconds;
                var recolored = items.Select(item => item with { Style = new() { ForegroundColor = Colors.Purple } }).ToArray();
                timer.Restart();
                layer.TrySet(editor.Document.Revision, recolored);
                var recolor = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                layer.Clear();
                var clear = timer.Elapsed.TotalMilliseconds;
                File.WriteAllText(Path.Combine(FileSystem.CacheDirectory, "formatting-projection-performance.txt"),
                    $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {DeviceInfo.Platform}\n" +
                    $"2000 lines, 4000 decorations: initial {initial:F1} ms; recolor {recolor:F1} ms; clear {clear:F1} ms\n");
                await Task.Delay(100);
                Check(ReferenceEquals(source, editor.Document.CurrentSnapshot), "Highlighting changed the source.");
            });
        }
    }

    private static void AssertMatchesFullRender(RichEditor editor, RichTextDecoration[][] layers, string context)
    {
        var document = RichTextDocument.FromPlainText(editor.Document.Text);
        document.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(editor.Document.DefaultCharacterFormat);
            edit.SetCharacterFormats(editor.Document.CurrentSnapshot.Runs);
        });
        var reference = new RichEditor
        {
            Document = document, FontFamily = editor.FontFamily, FontSize = editor.FontSize, TextColor = editor.TextColor,
        };
        foreach (var items in layers) reference.Decorations.CreateLayer().TrySet(reference.Document.Revision, items);
        var handler = new RichEditorHandler();
        handler.SetMauiContext(editor.Handler!.MauiContext!);
        reference.Handler = handler;
        try
        {
            for (var position = 0; position < document.Length; position++)
            {
                var actual = Capture(editor, position);
                var expected = Capture(reference, position);
                Check(Equals(actual, expected), $"{context}, position {position}: actual {actual}; expected {expected}");
            }
        }
        finally
        {
            reference.Handler = null;
            ((IElementHandler)handler).DisconnectHandler();
        }
    }

    internal static object Capture(RichEditor editor, int position)
    {
#if ANDROID
        var native = (Android.Widget.EditText)editor.Handler!.PlatformView!;
        using var paint = new Android.Text.TextPaint(native.Paint!);
        // TextView refreshes this color at draw time; the reference view has no parent/layout pass.
        paint.Color = new Android.Graphics.Color(native.CurrentTextColor);
        foreach (var span in native.EditableText!.GetSpans(position, position + 1,
            Java.Lang.Class.FromType(typeof(Android.Text.Style.CharacterStyle)))!.OfType<Android.Text.Style.CharacterStyle>())
            span.UpdateDrawState(paint);
        return new
        {
            paint.Color, paint.BgColor, paint.TextSize, paint.TextScaleX, paint.LetterSpacing,
            paint.BaselineShift, paint.UnderlineText, paint.StrikeThruText, paint.FakeBoldText,
            paint.TextSkewX, paint.FontFeatureSettings, TypefaceStyle = paint.Typeface?.Style,
            Locale = paint.TextLocale?.ToLanguageTag(), Style = paint.GetStyle(), paint.StrokeWidth,
        };
#else
        var native = (UIKit.UITextView)editor.Handler!.PlatformView!;
        var attributes = new UIKit.UIStringAttributes(native.TextStorage.GetAttributes(position, out _));
        return new
        {
            attributes.Font?.FamilyName, attributes.Font?.PointSize, attributes.Font?.FontDescriptor.SymbolicTraits,
            attributes.ForegroundColor, attributes.BackgroundColor, attributes.UnderlineStyle, attributes.UnderlineColor,
            attributes.StrikethroughStyle, attributes.StrikethroughColor, attributes.BaselineOffset,
            attributes.KerningAdjustment, attributes.Expansion, attributes.Ligature,
            attributes.StrokeColor, attributes.StrokeWidth, attributes.Shadow?.ShadowBlurRadius,
            attributes.Shadow?.ShadowOffset, attributes.Shadow?.ShadowColor,
            Direction = string.Join(",", attributes.WritingDirectionInt?.Select(value => value.Int32Value) ?? []),
        };
#endif
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
