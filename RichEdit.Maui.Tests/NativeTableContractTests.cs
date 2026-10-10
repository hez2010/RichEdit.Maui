using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public class NativeTableContractTests
{
    private const string TableRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}\fs24
\trowd\trgaph108\trleft0\cellx1440\cellx2880
\intbl alpha\cell beta\cell\row
\trowd\trgaph108\trleft0\cellx1440\cellx2880
\intbl gamma\cell delta\cell\row
\pard\par}";

    [Fact]
    public Task NativeRichEditRetainsTableStructureAcrossCellTypingAndUndo()
    {
        var output = TestContext.Current.TestOutputHelper!;
        return WindowsTestHost.RunAsync(() =>
        {
            var native = new RichEditBox();
            native.Document.SetText(TextSetOptions.FormatRtf, TableRtf);
            native.Document.ClearUndoRedoHistory();
            native.Document.GetText(TextGetOptions.None, out var before);
            output.WriteLine($"Native table text: {Escape(before)}");
            output.WriteLine($"Native text units: {string.Join(", ", Enum.GetNames<TextRangeUnit>())}");
            Assert.Contains("alpha", before);
            Assert.Contains("beta", before);
            Assert.Contains("gamma", before);
            Assert.Contains("delta", before);
            var position = before.IndexOf("beta", StringComparison.Ordinal);
            native.Document.Selection.SetRange(position, position + 4);
            native.Document.Selection.SetText(TextSetOptions.None, "BETA");
            native.Document.GetText(TextGetOptions.None, out var after);
            Assert.Equal(before.Replace("beta", "BETA", StringComparison.Ordinal), after);
            native.Document.GetText(TextGetOptions.FormatRtf, out var saved);
            Assert.Contains(@"\trowd", saved);
            Assert.Contains(@"\cellx", saved);
            Assert.Contains(@"\cell", saved);
            Assert.True(native.Document.CanUndo());
            native.Document.Undo();
            native.Document.GetText(TextGetOptions.None, out var restored);
            Assert.Equal(before, restored);
        });
    }

    [Fact]
    public Task NativeTableEndIncludesAnAddressableFinalParagraph()
    {
        var output = TestContext.Current.TestOutputHelper!;
        return WindowsTestHost.RunAsync(() =>
        {
            var native = new RichEditBox();
            native.Document.SetText(TextSetOptions.FormatRtf, TableRtf.Replace(@"\pard\par", "", StringComparison.Ordinal));
            native.Document.GetText(TextGetOptions.None, out var before);
            output.WriteLine($"Table ending without explicit final paragraph: {Escape(before)}");
            native.Document.Selection.EndKey(TextRangeUnit.Story, false);
            output.WriteLine($"Native end caret: {native.Document.Selection.StartPosition}");
            native.Document.Selection.TypeText("tail");
            native.Document.GetText(TextGetOptions.None, out var after);
            native.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
            output.WriteLine($"Typed at native end: {Escape(after)}");
            Assert.Equal(before[..^1] + "tail\r", after);
            Assert.Contains(@"\trowd", rtf);
        });
    }

    private static string Escape(string value) => string.Concat(value.Select(character => char.IsControl(character)
        ? $"\\u{(int)character:x4}" : character.ToString()));
}
