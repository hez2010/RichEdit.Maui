#if WINDOWS || ((DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST))
using CodeEdit.Maui;
using RichEdit.Core;
using RichEdit.Core.Rtf;
using System.Security.Cryptography;
using CoreSelection = RichEdit.Core.RichTextSelection;

namespace RichEdit.Maui.Tests;

internal sealed record EditingScenario(string Id, string[] Features, Func<EditingScenarioContext, Task> Run);
internal sealed record EditingObservation(string Step, string Selection, Point Scroll, Rect? Caret, bool Focused, string TextHash, string NativeFont);

internal sealed partial class EditingScenarioContext(Func<View, Task> attach)
{
    internal RichEditor Rich { get; private set; } = null!;
    internal CodeEditor Code { get; private set; } = null!;
    internal Grid Host { get; private set; } = null!;
    private NativeEditorView _codeSurface = null!;
    private readonly List<Action> _disconnectPeers = [];
    internal List<EditingObservation> Observations { get; } = [];
    private readonly HashSet<string> _behaviorFailures = [];
    internal void VerifyBehavior() => Check(_behaviorFailures.Count == 0, string.Join(Environment.NewLine, _behaviorFailures));
    internal NativeEditorView Surface => Rich is not null
        ? Rich.NativeView.FindStructuredSurface(Rich.Selection) ?? Rich.NativeView : _codeSurface;
    internal RichEditorHandler Handler => (RichEditorHandler)Surface.Handler!;
    internal string Text => Rich is not null ? Rich.Document.CurrentSnapshot.ProjectText().Text.ToString() : Code.Document.Text;

    internal async Task OpenReference()
    {
        using var input = typeof(EditingScenarioContext).Assembly.GetManifestResourceStream("ReferenceDocument")!;
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        Rich = new RichEditor { Document = new RichDocument(imported.Snapshot) };
        Host = new Grid { Children = { Rich } };
        await attach(Host);
        await Settle();
    }

    internal async Task OpenCode(string source)
    {
        Code = new CodeEditor { Document = new CodeDocument(source), FontSize = 14, WordWrap = false };
        _codeSurface = ((Grid)Code.Content).Children.OfType<NativeEditorView>().Single();
        Host = new Grid { Children = { Code } };
        await attach(Host);
        await Settle();
    }

    internal async Task<RichEditor> AddRichPeer(RichSelection selection)
    {
        var peer = new RichEditor { Document = Rich.Document, Selection = selection };
        Host.ColumnDefinitions = new() { new(GridLength.Star), new(GridLength.Star) };
        Grid.SetColumn(peer, 1); Host.Children.Add(peer); await Settle();
        _disconnectPeers.Add(() => { peer.Content?.Handler?.DisconnectHandler(); peer.NativeView.Handler?.DisconnectHandler(); peer.Handler?.DisconnectHandler(); });
        return peer;
    }

#if IOS || MACCATALYST
    internal async Task CheckNativeViewportBaseline(bool reference = false)
    {
        Foundation.NSAttributedString? referenceText = null;
        if (reference) { await OpenReference(); referenceText = new Foundation.NSAttributedString(Handler.PlatformView.AttributedText); }
        var editor = new Editor { Text = string.Join("\n", Enumerable.Range(0, 150).Select(i => $"Native baseline paragraph {i}")), AutoSize = EditorAutoSizeOption.Disabled };
        Host = new Grid { Children = { editor } }; await attach(Host); await Settle();
        var native = (UIKit.UITextView)editor.Handler!.PlatformView!;
        if (referenceText is not null) { native.AttributedText = referenceText; await Settle(); }
        native.BecomeFirstResponder();
        var start = native.Text!.IndexOf(reference ? "The text you are reading is aligned left" : "Native baseline paragraph 40", StringComparison.Ordinal);
        native.SelectedRange = new(start, 4); native.ScrollRangeToVisible(native.SelectedRange); await Settle();
        using var point = native.GetPosition(native.BeginningOfDocument, start)!;
        var caret = native.GetCaretRectForPosition(point);
        native.SetContentOffset(new(0, caret.Y - 100), false); await Settle(80);
        var before = native.ContentOffset;
        native.TextStorage.BeginEditing();
        native.TextStorage.AddAttribute(UIKit.UIStringAttributeKey.ForegroundColor, UIKit.UIColor.Red, new(start, 4));
        native.TextStorage.EndEditing();
        await Settle(); var after = native.ContentOffset;
        native.ResignFirstResponder(); editor.Handler.DisconnectHandler(); referenceText?.Dispose();
        Check(Math.Abs(before.Y - after.Y) < 2, $"unmodified MAUI Editor native baseline moved from {before} to {after}");
    }
#endif

    internal async Task<CodeEditor> AddCodePeer(int caret)
    {
        var peer = new CodeEditor { Document = Code.Document, SelectionState = new(caret, caret), FontSize = Code.FontSize };
        Host.ColumnDefinitions = new() { new(GridLength.Star), new(GridLength.Star) };
        Grid.SetColumn(peer, 1); Host.Children.Add(peer); await Settle();
        _disconnectPeers.Add(() => { ((Grid)peer.Content).Children.OfType<NativeEditorView>().Single().Handler?.DisconnectHandler(); peer.Handler?.DisconnectHandler(); });
        return peer;
    }

    internal RichTextPoint Find(string text, int offset = 0)
    {
        var projection = Rich.Document.CurrentSnapshot.ProjectText();
        var position = projection.Text.ToString().IndexOf(text, StringComparison.Ordinal);
        Check(position >= 0, $"Fixture contains {text}");
        return projection.GetPoint(position + offset)!.Value;
    }

    internal async Task Select(RichTextPoint point, int length = 0, bool reversed = false)
    {
        Rich.Selection = reversed ? new CoreSelection(point with { Offset = point.Offset + length }, point)
            : new CoreSelection(point, point with { Offset = point.Offset + length });
        Rich.Focus();
        Rich.TextLayout.ScrollIntoView(point);
        await Settle();
        PositionCaretInViewport(100);
        await Settle(80);
        AssertNativeSelection();
    }

    internal async Task SelectCode(int start, int length = 0, bool reversed = false)
    {
        Code.SelectionState = reversed ? new(start + length, start) : new(start, start + length);
        Code.Focus();
        await Code.RevealAsync(start);
        await Settle();
        PositionCaretInViewport(100);
        await Settle(80);
        AssertNativeSelection();
    }

    internal async Task ObserveEdit(string name, Action edit, bool stableScroll = true, bool sameSelection = false)
    {
        var before = Observe(name + "/before");
        var viewport = Rich is not null ? Rich.TextLayout.Capture()?.Viewport : Code.TextLayout.Capture()?.Viewport;
        var wasVisible = before.Caret is { } caret && viewport is { } bounds && caret.Top >= bounds.Top - 1 && caret.Bottom <= bounds.Bottom + 1;
        edit();
        for (var frame = 0; frame < 12; frame++)
        {
            await Task.Delay(20);
            var after = Observe(name + $"/frame-{frame}");
            AssertNativeSelection();
            if (frame == 11) Check(after.Focused, name + ": native focus retained after layout");
            if (sameSelection) Equal(before.Selection, after.Selection, name + ": selection retained");
            if (stableScroll && (Math.Abs(before.Scroll.Y - after.Scroll.Y) > 2 || Math.Abs(before.Scroll.X - after.Scroll.X) > 2))
                _behaviorFailures.Add($"{name}: viewport moved from {before.Scroll} to {after.Scroll}");
            if (frame == 11 && wasVisible && viewport is { } visible)
                Check(after.Caret is { Height: > 0 } final && double.IsFinite(final.Y) && final.Top >= visible.Top - 1 && final.Bottom <= visible.Bottom + 1,
                    $"{name}: caret remains visible after edit; caret={after.Caret}, viewport={visible}");
        }
    }

    internal EditingObservation Observe(string step)
    {
        Rect? caret;
        string selection;
        if (Rich is not null)
        {
            static string Range(CoreSelection text) => $"{text.Anchor.Paragraph.Value}:{text.Anchor.Offset}->{text.Active.Paragraph.Value}:{text.Active.Offset}";
            selection = Rich.Selection switch
            {
                CoreSelection text => Range(text),
                RichTextSelectionSet set => string.Join(";", set.Select(item => Range(item.Selection))) + (set.Count > 1 ? $" primary={set.PrimaryIndex}" : ""),
                _ => Rich.Selection.ToString()!,
            };
            var point = Rich.Selection switch { CoreSelection text => text.Active, RichTextSelectionSet set => set.Primary.Selection.Active, _ => (RichTextPoint?)null };
            var layout = Rich.TextLayout.Capture();
            caret = point is { } p && layout is not null ? Rich.TextLayout.GetCaretBounds(layout, p) : null;
        }
        else
        {
            selection = Code.SelectionState.ToString();
            var layout = Code.TextLayout.Capture();
            caret = layout is null ? null : Code.TextLayout.GetCaretBounds(layout, Code.SelectedRange.End);
        }
        var result = new EditingObservation(step, selection, ScrollOffset(), caret, NativeFocused(),
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Text))), ReadNativeFont());
        Observations.Add(result);
        return result;
    }

    internal void AssertNativeSelection()
    {
        if (Surface.Composition.IsActive) return;
        if (Rich is not null && Rich.Selection is not (CoreSelection or RichTextSelectionSet)) return;
        var native = ReadNativeSelection();
        var mapped = Handler.NativeProjection.ToSource(native);
        Equal(Surface.SelectionState.Range, mapped.Range, $"native/model selection; raw={native}");
    }

    internal async Task<byte[]> SaveRich()
    {
        using var stream = new MemoryStream();
        await RichEdit.Core.Rtf.RtfCodec.WriteAsync(Rich.Document.CurrentSnapshot, stream);
        return stream.ToArray();
    }

    internal async Task CheckUndo(byte[] before, Action redoCheck)
    {
        await ObserveEdit("Undo", Rich.Undo, stableScroll: false);
        var restored = await SaveRich();
        Check(before.AsSpan().SequenceEqual(restored), "Undo restores all reference semantics and resources");
        AssertNativeSelection();
        await ObserveEdit("Redo", Rich.Redo, stableScroll: false);
        redoCheck();
        AssertNativeSelection();
    }

    internal static Task Settle(int milliseconds = 350) => Task.Delay(milliseconds);
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Equal<T>(T expected, T actual, string message)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}; actual {actual}"); }

    internal void Disconnect()
    {
        foreach (var disconnect in _disconnectPeers) disconnect();
        if (Rich is not null) { Rich.Unfocus(); Rich.Content?.Handler?.DisconnectHandler(); Rich.NativeView.Handler?.DisconnectHandler(); Rich.Handler?.DisconnectHandler(); }
        if (Code is not null) { Code.Unfocus(); Code.Handler?.DisconnectHandler(); _codeSurface.Handler?.DisconnectHandler(); }
    }
}
#endif
