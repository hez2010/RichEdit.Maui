using CodeEdit.Maui;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public class AdornmentTests
{
    private static Microsoft.UI.Xaml.Window? _window;

    [Fact]
    public Task DensePresentationWorkloadRecordsDeviceBaseline() => WithNativeEditor(async (editor, _) =>
        await EditorContractTests.Cases.Single(static test => test.Name.StartsWith("performance dense", StringComparison.Ordinal)).Run(editor));

    [Fact]
    public Task AccessibilityRangesAddressSourceAcrossReservations() => WithNativeEditor(async (editor, handler) =>
    {
        editor.Document = RichTextDocument.FromPlainText("A😀 bc\ntail");
        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(handler.PlatformView);
        var text = (Microsoft.UI.Xaml.Automation.Provider.ITextProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Text);
        var original = text.DocumentRange;
        using var item = editor.Adornments.Add(4, new Button
            {
                Text = "annotation",
                WidthRequest = 60,
                HeightRequest = 28
            }, new() { Placement = RichTextAdornmentPlacement.Inline });
        await Task.Delay(150);
        Assert.Equal(editor.Document.Text, text.DocumentRange.GetText(-1));
        Assert.Equal(editor.Document.Text, original.GetText(-1));
        var found = text.DocumentRange.FindText("bc", false, false);
        found.Select();
        Assert.Equal(new RichTextRange(4, 2), editor.SelectedRange);
        Assert.Equal("bc", text.GetSelection().Single().GetText(-1));
        editor.Selection.CharacterFormat.Bold = true;
        await Task.Delay(50);
        Assert.Equal(700, found.GetAttributeValue(40007)); // UIA_FontWeightAttributeId
        Assert.Equal(400, text.DocumentRange.FindText("tail", false, false).GetAttributeValue(40007));
        Assert.NotEqual(700, text.DocumentRange.GetAttributeValue(40007));
        found.GetBoundingRectangles(out var bounds);
        Assert.NotEmpty(bounds);
        var caret = text.DocumentRange.Clone();
        caret.MoveEndpointByRange(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.End, caret, Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start);
        Assert.Equal(2, caret.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.End, Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 2));
        Assert.Equal("A😀", caret.GetText(-1));
        editor.Document.Edit(edit => edit.InsertText(0, "!"));
        Assert.Equal("bc", found.GetText(-1));
        Assert.Equal("A😀 bc\ntail", original.GetText(-1));
        var value = (Microsoft.UI.Xaml.Automation.Provider.IValueProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Value);
        Assert.Equal(editor.Document.Text, value.Value);
        editor.IsReadOnly = true;
        Assert.True(value.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => value.SetValue("blocked"));
        editor.IsReadOnly = false;
        editor.MaxLength = 2;
        Assert.Throws<InvalidOperationException>(() => value.SetValue(new string('x', editor.Document.Length + 1)));
    });

    [Fact]
    public Task SharedNativeProjectionContracts() => WithNativeEditor(async (editor, _) =>
    {
        foreach (var test in EditorContractTests.Cases.Where(test => test.Name.StartsWith("projection ", StringComparison.Ordinal)))
        {
            EditorContractTests.Reset(editor);
            await test.Run(editor);
        }
    });

    [Fact]
    public Task AccessibilityMovementStopsAtTheLastNonemptyUnit() => WithNativeEditor((editor, handler) =>
    {
        editor.Document = RichTextDocument.FromPlainText("abc");
        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(handler.PlatformView);
        var text = (Microsoft.UI.Xaml.Automation.Provider.ITextProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Text);
        var range = text.DocumentRange.FindText("c", false, false);
        Assert.Equal(0, range.Move(Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 1));
        Assert.Equal("c", range.GetText(-1));
        Assert.Equal(-1, range.Move(Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, -1));
        Assert.Equal("b", range.GetText(-1));
        Assert.Equal(1, range.Move(Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 10));
        Assert.Equal("c", range.GetText(-1));
        return Task.CompletedTask;
    });

    [Fact]
    public Task AccessibilityFindsParagraphAndPresentationSubranges() => WithNativeEditor((editor, handler) =>
    {
        editor.Document = RichTextDocument.FromPlainText("a\nb\nc");
        editor.Document.Edit(edit => edit.SetParagraphFormat(new(2, 0), new() { Alignment = RichTextAlignment.Center }));
        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(handler.PlatformView);
        var text = (Microsoft.UI.Xaml.Automation.Provider.ITextProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Text);
        var alignment = (int)Microsoft.UI.Xaml.AutomationTextAttributesEnum.HorizontalTextAlignmentAttribute;
        Assert.Equal("b\n", text.DocumentRange.FindAttribute(alignment, (int)RichTextAlignment.Center, false).GetText(-1));
        using var layer = editor.Decorations.CreateLayer();
        layer.TrySet(editor.Document.Revision, (RichTextDecoration[])[new(new(1, 2), new() { ForegroundColor = Microsoft.Maui.Graphics.Colors.Red })]);
        var foreground = (int)Microsoft.UI.Xaml.AutomationTextAttributesEnum.ForegroundColorAttribute;
        Assert.Equal("\nb", text.DocumentRange.FindAttribute(foreground, 255, false).GetText(-1));
        layer.TrySet(editor.Document.Revision, (RichTextDecoration[])[new(new(0, 1), new() { ForegroundColor = Microsoft.Maui.Graphics.Colors.Red }),
            new(new(4, 1), new() { ForegroundColor = Microsoft.Maui.Graphics.Colors.Red })]);
        Assert.Equal("a", text.DocumentRange.FindAttribute(foreground, 255, false).GetText(-1));
        Assert.Equal("c", text.DocumentRange.FindAttribute(foreground, 255, true).GetText(-1));
        return Task.CompletedTask;
    });

    [Fact]
    public Task InlineReservationsPreserveSourceAndNativeEditing() => WithNativeEditor(async (editor, handler) =>
    {
        editor.Document = RichTextDocument.FromPlainText("abc def\ntail");
        var revision = editor.Document.Revision;
        var rtf = editor.Document.RtfText;
        using var item = editor.Adornments.Add(3, new Button { Text = "parameter:", WidthRequest = 80, HeightRequest = 24 },
            new() { Placement = RichTextAdornmentPlacement.Inline });
        await Task.Delay(150);
        Assert.Equal("abc def\ntail", editor.Document.Text);
        Assert.Equal(revision, editor.Document.Revision);
        Assert.Equal(rtf, editor.Document.RtfText);
        Assert.False(editor.CanUndo);
        handler.PlatformView.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out var display);
        Assert.Contains("abc\uFFFC def", display);
        editor.SelectedRange = new(3, 0);
        Assert.Equal(4, handler.PlatformView.Document.Selection.StartPosition);
        handler.PlatformView.Document.GetRange(4, 4).SetText(Microsoft.UI.Text.TextSetOptions.None, "!");
        await Task.Delay(150);
        Assert.Equal("abc! def\ntail", editor.Document.Text);
        Assert.Equal(4, item.Position);
        Assert.True(editor.CanUndo);
        editor.Undo();
        await Task.Delay(150);
        Assert.Equal("abc def\ntail", editor.Document.Text);
        Assert.Equal(3, item.Position);
    });

    [Fact]
    public Task SharedNativeLayoutContracts() => WithNativeEditor(async (editor, _) =>
    {
        foreach (var test in EditorContractTests.Cases.Where(test => test.Name.StartsWith("layout ", StringComparison.Ordinal)))
        {
            EditorContractTests.Reset(editor);
            await test.Run(editor);
        }
    });

    [Fact]
    public Task SharedNativeAdornmentContracts() => WithNativeEditor(async (editor, _) =>
    {
        foreach (var test in EditorContractTests.Cases.Where(test => test.Name.StartsWith("adornments ", StringComparison.Ordinal)))
        {
            EditorContractTests.Reset(editor);
            await test.Run(editor);
        }
    });

    [Fact]
    public Task SampleIndicatorsExpandParentsAndKeepNestedFolds() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("head\none\ntwo\ntail") };
        using var indicators = new CodeEditorFoldIndicators(editor);
        var outer = new RichTextRange(5, 8);
        var inner = new RichTextRange(9, 4);
        editor.Folding.SetCollapsedRanges((RichTextRange[])[outer, inner]);
        Assert.Single(editor.Adornments);
        ClickSampleIndicator(editor);
        Assert.Equal(inner, Assert.Single(editor.Folding.CollapsedRanges));
        Assert.Single(editor.Adornments);
        ClickSampleIndicator(editor);
        Assert.Empty(editor.Folding.CollapsedRanges);
        Assert.Empty(editor.Adornments);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task SampleIndicatorsKeepIndependentButtonsForFoldsOnOneLine() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("one ABC two DEF end") };
        using var indicators = new CodeEditorFoldIndicators(editor);
        editor.Folding.SetCollapsedRanges((RichTextRange[])[new(4, 3), new(12, 3)]);
        var row = Assert.IsType<HorizontalStackLayout>(Assert.Single(editor.Adornments).View);
        Assert.Equal(2, row.Count);
        ((Button)row[0]).Command.Execute(null);
        Assert.Equal(new RichTextRange(12, 3), Assert.Single(editor.Folding.CollapsedRanges));
        ClickSampleIndicator(editor);
        Assert.Empty(editor.Folding.CollapsedRanges);
    });

    [Fact]
    public Task AdornmentsValidateOwnershipAndThreadBeforeMutating() => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abcd") };
        var label = new Label { Text = "annotation" };
        using var item = editor.Adornments.Add(2, label);
        Assert.Throws<ArgumentException>(() => editor.Adornments.Add(3, label));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Adornments.Add(5, new Label()));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Adornments.MarginWidth = double.NaN);
        var exception = await Task.Run(() => Record.Exception(() => editor.Adornments.Clear()));
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Single(editor.Adornments);
        editor.Document.Edit(edit => edit.InsertText(2, "!"));
        Assert.Equal(3, item.Position);
        editor.Undo();
        Assert.Equal(2, item.Position);
        editor.Document.Edit(edit => edit.DeleteText(new(1, 2)));
        Assert.Empty(editor.Adornments);
        Assert.Null(label.Parent);
        editor.Undo();
        Assert.Empty(editor.Adornments);
    });

    [Fact]
    public Task SampleEllipsisStaysWithHeaderWhenTextIsInsertedAfterFold() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("aaa\nbbb\nccc\nddd"), SelectedRange = new(4, 7) };
        using var indicators = new CodeEditorFoldIndicators(editor);
        new CodeEditorActions(editor).CollapseSelection();
        var range = Assert.Single(editor.Folding.CollapsedRanges);
        editor.Document.Edit(edit => edit.InsertText(range.End, "\n"));
        Assert.Equal(range, Assert.Single(editor.Folding.CollapsedRanges));
        Assert.Equal(range.End, Assert.Single(editor.Adornments, item => item.Options.Placement == RichTextAdornmentPlacement.Overlay).Position);
        editor.Undo();
        Assert.Equal(range.End, Assert.Single(editor.Adornments, item => item.Options.Placement == RichTextAdornmentPlacement.Overlay).Position);
    });

    [Fact]
    public Task AdornmentsSurviveHandlerReattachment() => WithNativeEditor(async (editor, handler) =>
    {
        editor.Document = RichTextDocument.FromPlainText("head\nbody\ntail");
        var view = new Button { Text = "+", WidthRequest = 24, HeightRequest = 20 };
        editor.Adornments.MarginWidth = 32;
        using var item = editor.Adornments.Add(5, view, new() { Placement = RichTextAdornmentPlacement.LeftMargin });
        await Task.Delay(100);
        _window!.Content = null;
        editor.Handler = null;
        ((IElementHandler)handler).DisconnectHandler();
        var replacement = new RichEditorHandler();
        replacement.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.Handler = replacement;
        _window.Content = replacement.ContainerView ?? replacement.PlatformView;
        try
        {
            await Task.Delay(150);
            Assert.Single(editor.Adornments);
            Assert.NotNull(view.Handler);
            Assert.True(view.Bounds.X >= 0);
            Assert.Equal("head\nbody\ntail", editor.Document.Text);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            _window.Content = null;
            editor.Handler = null;
            ((IElementHandler)replacement).DisconnectHandler();
        }
    });

    [Theory]
    [InlineData(RichTextAdornmentPlacement.Inline)]
    [InlineData(RichTextAdornmentPlacement.AboveLine)]
    [InlineData(RichTextAdornmentPlacement.BelowLine)]
    public Task EmptyFieldsKeepZeroLengthAtReservationBoundaries(RichTextAdornmentPlacement placement) => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor();
        editor.Document.Edit(edit => edit.InsertField(0, "PAGE", ""));
        using var item = editor.Adornments.Add(0, new Label(), new() { Placement = placement });
        item.MeasuredSize = new(20, 20);
        var projection = RichTextDisplayProjection.Create(editor, 200);
        var projected = projection.Project(editor.Document.CurrentSnapshot, 200);
        Assert.Equal(0, Assert.Single(projected.Fields).Length);
        Assert.True(editor.Document.CurrentSnapshot.ContentEquals(projection.Unproject(projected)));
    });

    [Theory]
    [InlineData(RichTextAdornmentPlacement.AboveLine)]
    [InlineData(RichTextAdornmentPlacement.BelowLine)]
    public Task FoldedRowAnchorsDoNotPublishReservations(RichTextAdornmentPlacement placement) => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abcd") };
        using var item = editor.Adornments.Add(2, new Label(), new() { Placement = placement });
        editor.Folding.Collapse(new(1, 2));
        Assert.True(RichTextDisplayProjection.Create(editor, 200).IsEmpty);
        editor.Folding.ExpandAll();
        Assert.False(RichTextDisplayProjection.Create(editor, 200).IsEmpty);
    });

    private static void ClickSampleIndicator(CodeEditor editor)
    {
        var row = (HorizontalStackLayout)Assert.Single(editor.Adornments).View;
        ((Button)row[0]).Command.Execute(null);
    }

    private static Task WithNativeEditor(Func<RichEditor, RichEditorHandler, Task> test) => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new RichEditor();
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.Handler = handler;
        _window ??= new Microsoft.UI.Xaml.Window();
        var panel = new Microsoft.UI.Xaml.Controls.Grid();
        panel.Children.Add(handler.ContainerView ?? handler.PlatformView);
        _window.Content = panel;
        _window.Activate();
        try
        {
            await test(editor, handler);
        }
        finally
        {
            _window.Content = null;
            editor.Handler = null;
            ((IElementHandler)handler).DisconnectHandler();
        }
    });
}
