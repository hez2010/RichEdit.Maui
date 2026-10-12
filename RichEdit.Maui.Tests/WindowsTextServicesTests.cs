using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using RichEdit.Maui.Platforms.Windows.NativeText;
using Windows.Win32.Foundation;

namespace RichEdit.Maui.Tests;

public partial class CodeEditorTests
{
    [Fact]
    public Task NativeTextRangesPreserveUnicodeSelectionAndImageGeometry() => WindowsTestHost.RunAsync(() =>
    {
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        using var engine = new WindowsTextServices(new NativeTextTestSite(WinRT.Interop.WindowNative.GetWindowHandle(window)));
        using var document = new WindowsTextDocument(engine);
        document.SetText(Microsoft.UI.Text.TextSetOptions.None, "a\U0001f600b\r\nsecond");
        document.GetText(Microsoft.UI.Text.TextGetOptions.UseLf, out var text);
        Assert.Equal("a\U0001f600b\nsecond", text);
        using var range = (WindowsTextRange)document.GetRange(1, 3);
        range.GetCharacterUtf32(out var character, 0);
        Assert.Equal((uint)'b', character);
        range.GetCharacterUtf32(out character, -1);
        Assert.Equal(0x1f600U, character);
        using var selection = (WindowsTextRange)document.Selection;
        selection.SetRange(1, 3);
        selection.TypeText("X");
        document.GetText(Microsoft.UI.Text.TextGetOptions.UseLf, out text);
        Assert.Equal("aXb\nsecond", text);
        using var matched = (WindowsTextRange)document.GetRange(0, 0);
        matched.MatchSelection();
        Assert.Equal(selection.StartPosition, matched.StartPosition);
        Assert.Equal(selection.EndPosition, matched.EndPosition);
        using var first = (WindowsTextRange)document.GetRange(0, 0);
        first.Expand(Microsoft.UI.Text.TextRangeUnit.Line);
        Assert.Equal(0, first.StartPosition);
        Assert.Equal(4, first.EndPosition);
        using var insertion = (WindowsTextRange)document.GetRange(0, 0);
        using var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new global::Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(RichTextDisplayProjection.TransparentPixel.ToArray());
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }
        stream.Seek(0);
        var before = selection.StartPosition;
        insertion.InsertImage(40, 20, 12, Microsoft.UI.Text.VerticalCharacterAlignment.Baseline, "badge", stream);
        using var image = (WindowsTextRange)document.GetRange(0, 1);
        Assert.Equal('\uFFFC', image.Character);
        Assert.Equal(before + 1, selection.StartPosition);
        image.GetRect(Microsoft.UI.Text.PointOptions.ClientCoordinates | Microsoft.UI.Text.PointOptions.AllowOffClient, out var bounds, out _);
        Assert.InRange(bounds.Width, 39, 41);
        Assert.True(bounds.Height >= 20);
        image.GetText(Microsoft.UI.Text.TextGetOptions.FormatRtf, out var rtf);
        Assert.Contains("\\pict", rtf);
        document.ClearUndoRedoHistory();
        Assert.Equal(0, (int)engine.Send(0xC6));
    });

    [Fact]
    public Task NativeTextFormatAdaptersRetainTOMSemantics() => WindowsTestHost.RunAsync(() =>
    {
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        using var engine = new WindowsTextServices(new NativeTextTestSite(WinRT.Interop.WindowNative.GetWindowHandle(window)));
        SetText(engine, "first\r\nsecond");
        VerifyNativeFormats(engine);
    });

    private static unsafe void VerifyNativeFormats(WindowsTextServices engine)
    {
        var iid = new Guid("C241F5E0-7206-11D8-A2C7-00A0D1D6C6B3");
        var document = (global::Windows.Win32.UI.Controls.RichEdit.ITextDocument2*)engine.QueryInterface(in iid);
        global::Windows.Win32.UI.Controls.RichEdit.ITextRange2* range = null;
        try
        {
            document->Range2(0, 5, &range);
            global::Windows.Win32.UI.Controls.RichEdit.ITextFont2* font;
            global::Windows.Win32.UI.Controls.RichEdit.ITextPara2* paragraph;
            range->GetFont2(&font);
            range->GetPara2(&paragraph);
            using var characters = new WindowsTextFont(engine, (nint)font);
            using var paragraphs = new WindowsTextParagraph(engine, (nint)paragraph);
            characters.Bold = Microsoft.UI.Text.FormatEffect.On;
            characters.Italic = Microsoft.UI.Text.FormatEffect.Off;
            characters.Size = 18;
            characters.ForegroundColor = global::Windows.UI.Color.FromArgb(255, 20, 40, 80);
            characters.LanguageTag = "ja-JP";
            characters.Underline = Microsoft.UI.Text.UnderlineType.Wave;
            characters.FontStretch = global::Windows.UI.Text.FontStretch.SemiCondensed;
            characters.FontStyle = global::Windows.UI.Text.FontStyle.Oblique;
            Assert.Equal(Microsoft.UI.Text.FormatEffect.On, characters.Bold);
            Assert.Equal(Microsoft.UI.Text.FormatEffect.Off, characters.Italic);
            Assert.Equal(18, characters.Size);
            Assert.Equal(global::Windows.UI.Color.FromArgb(255, 20, 40, 80), characters.ForegroundColor);
            Assert.Equal("ja-JP", characters.LanguageTag);
            Assert.Equal(Microsoft.UI.Text.UnderlineType.Wave, characters.Underline);
            Assert.Equal(global::Windows.UI.Text.FontStyle.Oblique, characters.FontStyle);
            using var clone = (WindowsTextFont)characters.GetClone();
            Assert.Equal(global::Windows.UI.Text.FontStyle.Oblique, clone.FontStyle);
            Assert.True(characters.IsEqual(clone));
            characters.Bold = Microsoft.UI.Text.FormatEffect.Off;
            Assert.False(characters.IsEqual(clone));
            characters.SetClone(clone);
            Assert.Equal(Microsoft.UI.Text.FormatEffect.On, characters.Bold);
            Assert.Equal(global::Windows.UI.Text.FontStretch.SemiCondensed, characters.FontStretch);
            Assert.Equal(global::Windows.UI.Text.FontStyle.Oblique, characters.FontStyle);
            paragraphs.Alignment = Microsoft.UI.Text.ParagraphAlignment.Center;
            paragraphs.RightToLeft = Microsoft.UI.Text.FormatEffect.On;
            paragraphs.SetIndents(2, 3, 4);
            paragraphs.SetLineSpacing(Microsoft.UI.Text.LineSpacingRule.Multiple, 1.5f);
            paragraphs.ListType = Microsoft.UI.Text.MarkerType.Arabic;
            paragraphs.ListStyle = Microsoft.UI.Text.MarkerStyle.Period;
            paragraphs.AddTab(80, Microsoft.UI.Text.TabAlignment.Center, Microsoft.UI.Text.TabLeader.Dots);
            Assert.Equal(Microsoft.UI.Text.ParagraphAlignment.Center, paragraphs.Alignment);
            Assert.Equal(Microsoft.UI.Text.FormatEffect.On, paragraphs.RightToLeft);
            Assert.Equal(3, paragraphs.LeftIndent);
            Assert.Equal(1.5f, paragraphs.LineSpacing);
            Assert.Equal(Microsoft.UI.Text.LineSpacingRule.Multiple, paragraphs.LineSpacingRule);
            Assert.Equal(Microsoft.UI.Text.MarkerType.Arabic, paragraphs.ListType);
            Assert.Equal(Microsoft.UI.Text.MarkerStyle.Period, paragraphs.ListStyle);
            Assert.Equal(1, paragraphs.TabCount);
            paragraphs.GetTab(0, out var position, out var alignment, out var leader);
            Assert.Equal(80, position);
            Assert.Equal(Microsoft.UI.Text.TabAlignment.Center, alignment);
            Assert.Equal(Microsoft.UI.Text.TabLeader.Dots, leader);
            BSTR rtf; range->GetText2(8192, &rtf);
            var bytes = WindowsTextValues.TakeBytes(rtf);
            var text = System.Text.Encoding.Latin1.GetString(bytes);
            Assert.Contains("\\ulwave", text);
            Assert.Contains("\\tqc", text);
            Assert.Contains("\\rtlpar", text);
            using var encoded = new WindowsTextValues.Bytes(bytes);
            range->SetText2(8192, encoded.Value);
            BSTR plain;
            range->GetText2(0, &plain);
            Assert.Equal("first", WindowsTextValues.TakeString(plain));
        }
        finally { if (range is not null) range->Release(); document->Release(); }
    }

    [Fact]
    public Task NativeTextServicesOwnTextAndReleaseTheirHost() => WindowsTestHost.RunAsync(() =>
    {
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        var site = new NativeTextTestSite(WinRT.Interop.WindowNative.GetWindowHandle(window));
        using var engine = new WindowsTextServices(site);
        SetText(engine, "first\r\nsecond");
        Assert.Equal(2, (int)engine.Send(0xBA)); // EM_GETLINECOUNT
        SetSelection(engine, -1, -1);
        ReplaceSelection(engine, "!");
        Assert.Equal("first\r\nsecond!", GetText(engine));
        engine.Dispose();
        engine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => engine.Send(0xE));
    });

    [Fact(Explicit = true)]
    public Task NativeTextServicesRenderDistantLinesInABoundedViewport() => WindowsTestHost.RunAsync(async () =>
    {
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        var site = new NativeTextTestSite(WinRT.Interop.WindowNative.GetWindowHandle(window));
        using var engine = new WindowsTextServices(site);
        SetText(engine, string.Concat(Enumerable.Repeat(new string('x', 105) + "\r\n", 100_000)));
        Assert.Equal(10_700_000, (int)engine.Send(0xE));
        Assert.Equal(100_001, (int)engine.Send(0xBA));
        using var device = new CanvasDevice();
        using var target = new CanvasRenderTarget(device, 1024, 650, 96);
        var previous = 0;
        foreach (var line in new[] { 0, 1000, 50_000, 99_960 })
        {
            engine.Send(0xB6, 0, line - previous); // EM_LINESCROLL
            previous = line;
            Assert.Equal(line, (int)engine.Send(0xCE)); // EM_GETFIRSTVISIBLELINE
            using (var drawing = target.CreateDrawingSession())
            {
                drawing.Clear(Microsoft.UI.Colors.White);
                DrawNativeText(engine, drawing);
            }
            var pixels = target.GetPixelColors(100, 100, 300, 300);
            Assert.True(pixels.Count(color => color.R < 100 && color.G < 100 && color.B < 100) > pixels.Length / 100,
                $"Native text is blank at line {line + 1}.");
        }
        await target.SaveAsync(Path.Combine(AppContext.BaseDirectory, "native-text-services-last-lines.png"), CanvasBitmapFileFormat.Png);
        SetSelection(engine, -1, -1);
        ReplaceSelection(engine, "tail");
        Assert.Equal(100_001, (int)engine.Send(0xBA));
    }, TimeSpan.FromMinutes(2));

    private static unsafe void SetText(WindowsTextServices engine, string value)
    {
        engine.Send(0x435, 0, int.MaxValue); // EM_EXLIMITTEXT
        fixed (char* text = value) engine.Send(0xC, 0, (nint)text);
    }
    private static unsafe void SetSelection(WindowsTextServices engine, int start, int end)
    {
        var range = new global::Windows.Win32.UI.Controls.RichEdit.CHARRANGE { cpMin = start, cpMax = end };
        engine.Send(0x437, 0, (nint)(&range)); // EM_EXSETSEL
    }
    private static unsafe void ReplaceSelection(WindowsTextServices engine, string value)
    {
        fixed (char* text = value) engine.Send(0xC2, 0, (nint)text); // EM_REPLACESEL
    }
    private static unsafe string GetText(WindowsTextServices engine)
    {
        var buffer = new char[(int)engine.Send(0xE) + 1];
        fixed (char* text = buffer)
        {
            var length = (int)engine.Send(0xD, (nuint)buffer.Length, (nint)text);
            return new(text, 0, length);
        }
    }

    // The Win2D SDK publishes ICanvasResourceWrapperNative as a native-only ABI.
    // Keep this test bridge scoped to its drawing session and release both QIs.
    private static unsafe void DrawNativeText(WindowsTextServices engine, CanvasDrawingSession drawing)
    {
        var session = WinRT.MarshalInspectable<CanvasDrawingSession>.FromManaged(drawing);
        nint wrapper = 0, target = 0;
        try
        {
            var iid = new Guid("5F10688D-EA55-4D55-A3B0-4DDB55C0C20A");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(session, in iid, out wrapper));
            var targetId = new Guid("2CD90694-12E2-11DC-9FED-001143A055F9");
            var get = (delegate* unmanaged[Stdcall]<nint, nint, float, Guid*, nint*, int>)(*(nint**)wrapper)[3];
            Marshal.ThrowExceptionForHR(get(wrapper, 0, 96, &targetId, &target));
            engine.Draw(target);
        }
        finally
        {
            if (target != 0) Marshal.Release(target);
            if (wrapper != 0) Marshal.Release(wrapper);
            Marshal.Release(session);
        }
    }

    private sealed class NativeTextTestSite(nint window) : IWindowsTextSite
    {
        public nint Window => window;
        public DispatcherQueue Dispatcher => DispatcherQueue.GetForCurrentThread();
        public RECT Client => new() { right = 1024, bottom = 650 };
        public RECT Inset => default;
        public uint PropertyBits => 1 | 2 | 0x1000000;
        public uint ScrollBars => 0x300000;
        public uint MaximumLength => int.MaxValue;
        public uint EditStyle => 0;
        public uint WindowStyle => 0x50000000 | 0x300000 | 4 | 0x40 | 0x80;
        public int HorizontalExtent => 1024;
        public void Invalidate() { }
        public void SetScrollRange(int bar, int minimum, int maximum) { }
        public void SetScrollPosition(int bar, int position) { }
        public void ShowScrollBar(int bar, bool show) { }
        public void EnableScrollBar(int bar, int arrows) { }
        public bool CreateCaret(nint bitmap, int width, int height) => true;
        public void ShowCaret(bool show) { }
        public void MoveCaret(int x, int y) { }
        public void DestroyCaret() { }
        public void ShowDropCaret(bool show, RECT? bounds) { }
        public void Capture(bool capture) { }
        public void Focus() { }
        public void ScreenToClient(ref POINT point) { }
        public void ClientToScreen(ref POINT point) { }
        public int Notify(uint code, nint data) => 0;
    }
}
