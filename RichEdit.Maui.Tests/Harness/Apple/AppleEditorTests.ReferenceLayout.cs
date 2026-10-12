#if (DEBUG || EDITOR_TESTS) && (IOS || MACCATALYST)
using Foundation;
using RichEdit.Core;
using RichEdit.Maui.Platforms.Apple;
using UIKit;

namespace RichEdit.Maui.Tests;

internal static partial class AppleEditorTests
{
    private static async Task VerifyTableListMarkers(NativeEditorView editor)
    {
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            var table = edit.InsertTable(edit.Body, 1, 1, new RichLength[] { new(3000), new(3000) });
            var grid = edit.Snapshot.GetTableGrid(table);
            for (var column = 0; column < 2; column++)
            {
                var cell = grid[0, column];
                var paragraph = edit.Snapshot.Get<RichTableCell>(cell).Blocks[0];
                edit.ReplaceText(new(edit.Body, paragraph, 0), 0, column == 0 ? "left" : "right");
                edit.WrapInList(cell, 0, 1, column != 0, column == 0 ? 1 : 3,
                    column == 0 ? RichListNumberingStyle.Decimal : RichListNumberingStyle.UpperRoman);
            }
        }));
        var previous = editor.Document;
        try
        {
            editor.BindStructuredDocument(document);
            await Task.Delay(100);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            var labels = native.Subviews.OfType<NativeListMarkerLabel>().ToArray();
            foreach (var (word, marker) in new[] { ("left", "•"), ("right", "III.") })
            {
                using var position = native.GetDisplayPosition(native.BeginningOfDocument, native.Text!.IndexOf(word, StringComparison.Ordinal))!;
                var caret = native.GetDisplayCaret(position);
                var label = labels.Single(label => label.Text == marker);
                EditorContractTests.Equal(true, label.Frame.Right < caret.X && label.Frame.Left > caret.X - 60, marker + " stays beside its own cell text");
                EditorContractTests.Equal(true, Math.Abs(label.Frame.Top - caret.Top) < 5, marker + " aligns vertically");
            }
        }
        finally { editor.Document = previous; }
    }

    private static async Task VerifyReferenceLayout(NativeEditorView editor)
    {
        using var input = typeof(AppleEditorTests).Assembly.GetManifestResourceStream("ReferenceDocument")!;
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        var document = new RichDocument(imported.Snapshot);
        var authored = document.CurrentSnapshot;
        var previous = editor.Document;
        var width = editor.WidthRequest;
        var alignment = editor.HorizontalOptions;
        try
        {
            editor.BindStructuredDocument(document);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            await Task.Delay(100);
            native.LayoutManager.EnsureLayoutForTextContainer(native.TextContainer);
            var text = native.Text!;
            foreach (var name in new[] { "left", "right", "center" })
            {
                var position = text.IndexOf($"The text you are reading is aligned {name}.", StringComparison.Ordinal);
                EditorContractTests.Equal(true, position >= 0, name);
                var attributes = new UIStringAttributes(native.TextStorage.GetAttributes(position, out _));
                EditorContractTests.Equal(name == "right" ? 10d : 12d, (double)attributes.Font!.PointSize, name + " authored font size");
                EditorContractTests.Equal("Times New Roman", attributes.Font.FamilyName, name + " native font family");
                EditorContractTests.Equal(0d, (double)attributes.ParagraphStyle!.TailIndent, name + " uses the text viewport's right edge");
                var end = text.IndexOf('\n', position);
                using var sentence = new NSString(text[position..end]);
                var advance = sentence.GetSizeUsingAttributes(attributes).Width;
                if (advance < native.TextContainer.Size.Width - 2 * native.TextContainer.LineFragmentPadding)
                {
                    var glyph = native.LayoutManager.GetGlyphIndex((nuint)position);
                    native.LayoutManager.GetLineFragmentRect(glyph, out var glyphs);
                    var characters = native.LayoutManager.GetCharacterRange(glyphs);
                    EditorContractTests.Equal(true, characters.Location + characters.Length >= end, name + " sentence fits on one native line");
                }
            }

            var left = NativeCaret(text.IndexOf("1st column", StringComparison.Ordinal));
            var right = NativeCaret(text.IndexOf("2nd column", StringComparison.Ordinal));
            EditorContractTests.Equal(true, right.X > left.X + 20 && Math.Abs(right.Y - left.Y) < 2, "native table cell layout is retained");
            await Reveal("It is the 1");
            EditorContractTests.Equal(true, native.Subviews.OfType<NativeListMarkerLabel>().Any(label => label.Text == "•"), "native bullet labels are visible");
            await Reveal("Align left");
            foreach (var marker in new[] { "I.", "II.", "III.", "IV." })
                EditorContractTests.Equal(true, native.Subviews.OfType<NativeListMarkerLabel>().Any(label => label.Text == marker), marker + " native label");

            var body = text.IndexOf("The text you are reading is aligned left.", StringComparison.Ordinal);
            editor.HorizontalOptions = LayoutOptions.Start;
            editor.WidthRequest = 300;
            await Task.Delay(100);
            native.LayoutManager.EnsureLayoutForTextContainer(native.TextContainer);
            var startGlyph = native.LayoutManager.GetGlyphIndex((nuint)body);
            native.LayoutManager.GetLineFragmentRect(startGlyph, out var narrowGlyphs);
            var narrow = native.LayoutManager.GetCharacterRange(narrowGlyphs);
            EditorContractTests.Equal(true, narrow.Location + narrow.Length < text.IndexOf('\n', body), "narrow viewport wraps naturally");
            editor.WidthRequest = width;
            editor.HorizontalOptions = alignment;
            await Task.Delay(100);
            EditorContractTests.Equal(true, ReferenceEquals(authored, document.CurrentSnapshot), "layout does not alter the authored document");
            EditorContractTests.Equal(false, document.CanUndo);

            CoreGraphics.CGRect NativeCaret(int offset)
            {
                using var position = native.GetDisplayPosition(native.BeginningOfDocument, offset)!;
                return native.GetDisplayCaret(position);
            }
            async Task Reveal(string words)
            {
                var caret = NativeCaret(text.IndexOf(words, StringComparison.Ordinal));
                native.SetContentOffset(new(0, Math.Max(0, caret.Y - 30)), false);
                await Task.Delay(100);
            }
        }
        finally
        {
            editor.WidthRequest = width;
            editor.HorizontalOptions = alignment;
            editor.Document = previous;
        }
    }
}
#endif
