using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Text;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public class IncrementalCharacterFormattingTests
{
    private static readonly Dictionary<string, RichTextCharacterFormat> Formats = new()
    {
        ["font family"] = new() { FontFamily = "Arial" },
        ["font size"] = new() { FontSize = 24 },
        ["weight"] = new() { FontWeight = 700 },
        ["italic"] = new() { Italic = true },
        ["underline"] = new() { Underline = RichTextUnderlineStyle.Double },
        ["strikethrough"] = new() { Strikethrough = RichTextStrikethroughStyle.Single },
        ["foreground"] = new() { ForegroundColor = Colors.Red },
        ["background"] = new() { BackgroundColor = Colors.Yellow },
        ["transparent background"] = new() { BackgroundColor = Colors.Transparent },
        ["superscript"] = new() { Script = RichTextScript.Superscript },
        ["subscript"] = new() { Script = RichTextScript.Subscript },
        ["baseline"] = new() { BaselineOffset = 2 },
        ["spacing"] = new() { CharacterSpacing = 2 },
        ["scale"] = new() { HorizontalScale = 1.25 },
        ["small caps"] = new() { SmallCaps = true },
        ["all caps"] = new() { AllCaps = true },
        ["outline"] = new() { Outline = true },
        ["hidden"] = new() { Hidden = true },
        ["language"] = new() { LanguageTag = "ja-JP" },
        ["English language"] = new() { LanguageTag = "en-US" },
        ["kerning enabled"] = new() { Kerning = RichTextFeatureMode.Enabled },
        ["kerning disabled"] = new() { Kerning = RichTextFeatureMode.Disabled },
        ["combined"] = new()
        {
            FontFamily = "Arial", FontSize = 24, FontWeight = 700, Italic = true,
            ForegroundColor = Colors.Green, BackgroundColor = Colors.Yellow,
            Underline = RichTextUnderlineStyle.Double, Strikethrough = RichTextStrikethroughStyle.Single,
            Script = RichTextScript.Superscript, BaselineOffset = 2, CharacterSpacing = 2,
            HorizontalScale = 1.25, SmallCaps = true, AllCaps = true, Outline = true,
            Kerning = RichTextFeatureMode.Enabled,
        },
    };

    public static IEnumerable<object[]> FormatNames => Formats.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(FormatNames))]
    public Task FormatEditsAndResetsMatchFullProjection(string name) => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture(new RichTextDocumentSnapshot("before styled after", defaultCharacterFormat: new()
        {
            FontFamily = "Consolas", FontSize = 14, ForegroundColor = Colors.Navy,
        }));
        var document = fixture.Editor.Document;
        var range = new RichTextRange(7, 6);
        document.Edit(edit => edit.SetCharacterFormat(range, Formats[name]));
        AssertMatchesFullProjection(fixture);
        document.Undo();
        AssertMatchesFullProjection(fixture);
        document.Redo();
        AssertMatchesFullProjection(fixture);
        document.Edit(edit => edit.SetCharacterFormat(range, RichTextCharacterFormat.Default));
        AssertMatchesFullProjection(fixture);
    });

    [Fact]
    public Task ScriptTransitionsAndBaselineChangesMatchFullProjection() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture(new RichTextDocumentSnapshot("styled text"));
        foreach (var script in new[] { RichTextScript.Superscript, RichTextScript.Subscript, RichTextScript.Normal })
        {
            foreach (var offset in new[] { 2d, 0d })
            {
                fixture.Editor.Document.Edit(edit => edit.SetCharacterFormat(new(0, 6), new() { Script = script, BaselineOffset = offset }));
                AssertMatchesFullProjection(fixture);
            }
        }
    });

    [Fact]
    public Task CaseEffectsRetainTheSamePrecedenceAsFullProjection() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture(new RichTextDocumentSnapshot("styled text"));
        RichTextCharacterFormat[] formats =
        [
            new() { AllCaps = true },
            new() { AllCaps = true, SmallCaps = true },
            new() { SmallCaps = true },
            new() { AllCaps = true, SmallCaps = true },
            new(),
        ];
        foreach (var format in formats)
        {
            fixture.Editor.Document.Edit(edit => edit.SetCharacterFormat(new(0, 6), format));
            AssertMatchesFullProjection(fixture);
        }
    });

    [Fact]
    public Task DirtyDecorationRefreshRestoresTheCompleteFormat() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new EditorFixture(new RichTextDocumentSnapshot("styled text"));
        var editor = fixture.Editor;
        editor.Document.Edit(edit => edit.SetCharacterFormat(new(0, 6), new() { FontWeight = 700, Italic = true }));
        using var layer = editor.Decorations.CreateLayer();
        layer.Set([new(new(0, 6), new() { ForegroundColor = Colors.Green, BackgroundColor = Colors.Yellow })]);
        var before = editor.PresentationSnapshot;
        layer.Set([new(new(0, 6), new() { ForegroundColor = Colors.Red, Underline = RichTextUnderlineStyle.Double })]);
        var after = editor.PresentationSnapshot;
        var nativeFormat = fixture.Handler.PlatformView.Document.GetRange(1, 2).CharacterFormat;
        nativeFormat.Weight = 400;
        nativeFormat.Italic = FormatEffect.Off;
        nativeFormat.Underline = UnderlineType.None;
        nativeFormat.BackgroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 255);

        editor.ApplyDecorationChanges(before, after, new(1, 1));

        AssertMatchesFullProjection(fixture);
        await Task.Yield();
    });

    [Fact]
    public Task DefaultChangesAndOverlappingDecorationsMatchFullProjection() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new EditorFixture(new RichTextDocumentSnapshot("before styled after"));
        var editor = fixture.Editor;
        using var first = editor.Decorations.CreateLayer();
        using var second = editor.Decorations.CreateLayer();
        first.Set([new(new(1, 10), new() { BackgroundColor = Colors.Yellow, Underline = RichTextUnderlineStyle.Double })]);
        second.Set([new(new(4, 10), new() { ForegroundColor = Colors.Green, BackgroundColor = Colors.Pink })]);
        editor.Document.Edit(edit => edit.SetCharacterFormat(new(7, 6), new() { FontWeight = 700, Italic = true }));
        AssertMatchesFullProjection(fixture);
        editor.Document.Edit(edit => edit.SetDefaultCharacterFormat(new() { FontFamily = "Consolas", FontSize = 20, ForegroundColor = Colors.Navy }));
        AssertMatchesFullProjection(fixture);
        first.Set([new(new(2, 8), new() { BackgroundColor = Colors.Blue, Underline = RichTextUnderlineStyle.Wave })]);
        AssertMatchesFullProjection(fixture);
        second.Clear();
        AssertMatchesFullProjection(fixture);
        first.Clear();
        AssertMatchesFullProjection(fixture);
    });

    private static void AssertMatchesFullProjection(EditorFixture fixture)
    {
        using var expected = new EditorFixture(fixture.Editor.PresentationSnapshot);
        var actualRange = fixture.Handler.PlatformView.Document.GetRange(0, 0);
        var expectedRange = expected.Handler.PlatformView.Document.GetRange(0, 0);
        for (var position = 0; position < fixture.Editor.Document.Length; position++)
        {
            actualRange.SetRange(position, position + 1);
            expectedRange.SetRange(position, position + 1);
            var actualFormat = actualRange.CharacterFormat;
            var expectedFormat = expectedRange.CharacterFormat;
            var actual = ReadNativeFormat(actualFormat);
            var expectedValues = ReadNativeFormat(expectedFormat);
            Assert.True(Equals(expectedValues, actual), $"Native properties differ at position {position}.\nActual: {actual}\nExpected: {expectedValues}");
            if (!actualFormat.IsEqual(expectedFormat))
            {
                // TOM can retain inactive values: clearing a highlight can export an explicit
                // \highlight0 instead of omitting it. Verify the exported formatting as well.
                actualRange.GetText(TextGetOptions.FormatRtf, out var actualRtf);
                expectedRange.GetText(TextGetOptions.FormatRtf, out var expectedRtf);
                Assert.True(RtfCodec.Parse(actualRtf.TrimEnd('\0')).ContentEquals(RtfCodec.Parse(expectedRtf.TrimEnd('\0'))),
                    $"Incremental formatting differs from full projection at position {position}.\nActual: {actualRtf}\nExpected: {expectedRtf}");
            }
        }
    }

    private static object ReadNativeFormat(ITextCharacterFormat format) => new
    {
        format.Name, format.Size, format.Weight, format.Bold, format.Italic, format.FontStyle, format.Underline, format.Strikethrough,
        format.ForegroundColor, format.BackgroundColor, format.Position, format.Subscript, format.Superscript,
        format.Spacing, format.FontStretch, format.SmallCaps, format.AllCaps, format.Outline, format.Hidden, format.Kerning,
        format.LanguageTag, format.TextScript, format.ProtectedText, format.LinkType,
    };

    private sealed class EditorFixture : IDisposable
    {
        internal RichEditor Editor { get; }
        internal RichEditorHandler Handler { get; } = new();
        internal EditorFixture(RichTextDocumentSnapshot snapshot)
        {
            Editor = new RichEditor { Document = new RichTextDocument(snapshot) };
            Handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
            Editor.Handler = Handler;
        }
        public void Dispose()
        {
            Editor.Handler = null;
            ((IElementHandler)Handler).DisconnectHandler();
        }
    }
}
