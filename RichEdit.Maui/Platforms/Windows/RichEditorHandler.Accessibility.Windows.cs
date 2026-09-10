using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Text;
using Microsoft.UI.Xaml.Controls;
using WinRT;
using IValueProvider = Microsoft.UI.Xaml.Automation.Provider.IValueProvider;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private sealed partial class SourceRichEditBox(RichEditorHandler handler) : RichEditBox
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new SourceTextPeer(this, handler);
    }

    // A RichEditBoxAutomationPeer exposes RichEdit's windowless native child directly to UIA.
    // Use a framework peer so every text pattern and child addresses logical source instead.
    [GeneratedWinRTExposedType]
    private sealed partial class SourceTextPeer : FrameworkElementAutomationPeer, ITextProvider, ITextProvider2, ITextEditProvider, IValueProvider
    {
        internal readonly RichEditorHandler Handler;
        private readonly List<SourceImagePeer> _imagePeers = [];
        private RichEditor? _eventEditor;
        internal RichEditor Editor => Handler.VirtualView;
        internal SourceTextPeer(SourceRichEditBox view, RichEditorHandler handler) : base(view)
        {
            Handler = handler;
            Connect();
        }
        internal void Connect()
        {
            if (ReferenceEquals(_eventEditor, Editor)) return;
            Disconnect();
            _eventEditor = Editor;
            _eventEditor.TextChanged += OnTextChanged;
            _eventEditor.SelectionChanged += OnSelectionChanged;
            _eventEditor.DocumentChanged += OnDocumentChanged;
        }
        internal void Disconnect()
        {
            if (_eventEditor is null) return;
            _eventEditor.TextChanged -= OnTextChanged;
            _eventEditor.SelectionChanged -= OnSelectionChanged;
            _eventEditor.DocumentChanged -= OnDocumentChanged;
            _eventEditor = null;
        }
        private void OnTextChanged(object? sender, RichTextTextChangedEventArgs args)
        {
            TextChanged(args.ChangeSet.BeforeSnapshot?.Text ?? "", Editor.Document.Text);
            if (Editor.Composition.IsActive && ListenerExists(AutomationEvents.TextEditTextChanged))
                RaiseTextEditTextChangedEvent(AutomationTextEditChangeType.Composition, args.Changes.Select(static change => change.InsertedText).ToArray());
        }
        private void OnDocumentChanged(object? sender, RichTextDocumentReplacedEventArgs args) => TextChanged(args.OldDocument?.Text ?? "", args.NewDocument.Text);
        private void TextChanged(string before, string after)
        {
            if (ListenerExists(AutomationEvents.TextPatternOnTextChanged)) RaiseAutomationEvent(AutomationEvents.TextPatternOnTextChanged);
            if (before != after && ListenerExists(AutomationEvents.PropertyChanged)) RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, before, after);
        }
        private void OnSelectionChanged(object? sender, RichTextSelectionChangedEventArgs args)
        {
            if (ListenerExists(AutomationEvents.TextPatternOnTextSelectionChanged)) RaiseAutomationEvent(AutomationEvents.TextPatternOnTextSelectionChanged);
        }
        protected override object GetPatternCore(PatternInterface pattern) => pattern is PatternInterface.Text or PatternInterface.Text2 or
            PatternInterface.TextEdit or PatternInterface.Value ? this : base.GetPatternCore(pattern);
        protected override string GetClassNameCore() => nameof(RichEditBox);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        protected override bool IsKeyboardFocusableCore() => true;
        protected override bool HasKeyboardFocusCore() => Handler.PlatformView.FocusState != FocusState.Unfocused;
        protected override void SetFocusCore() => Handler.PlatformView.Focus(FocusState.Programmatic);
        protected override IList<AutomationPeer> GetChildrenCore()
        {
            _imagePeers.RemoveAll(static peer => peer.Image is null);
            foreach (var image in Editor.Document.CurrentSnapshot.Images)
                if (!_imagePeers.Any(peer => peer.Image?.Position == image.Position)) _imagePeers.Add(new(this, image.Position));
            return _imagePeers.OrderBy(static peer => peer.Image?.Position).Cast<AutomationPeer>().ToArray();
        }
        public ITextRangeProvider DocumentRange => Range(new(0, Editor.Document.Length));
        public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;
        public ITextRangeProvider[] GetSelection() => [Range(Editor.SelectedRange)];
        public ITextRangeProvider[] GetVisibleRanges() => Editor.TextLayout.Capture() is { } layout ?
            layout.Lines.SelectMany(static line => line.SourceRanges).Select(range => (ITextRangeProvider)Range(range)).ToArray() : [];
        public ITextRangeProvider RangeFromPoint(Windows.Foundation.Point point)
        {
            var screen = GetBoundingRectangle();
            var scale = Scale;
            var layout = Editor.TextLayout.Capture();
            var hit = layout is null ? null : Editor.TextLayout.HitTest(layout, new((point.X - screen.X) / scale, (point.Y - screen.Y) / scale));
            return Range(new(hit?.Position ?? Editor.SelectionState.Active, 0));
        }
        public ITextRangeProvider RangeFromChild(IRawElementProviderSimple child)
        {
            var image = GetChildrenCore().OfType<SourceImagePeer>().FirstOrDefault(peer => ReferenceEquals(ProviderFromPeer(peer), child));
            if (image?.Image is { } sourceImage) return Range(new(sourceImage.Position, 1));
            var item = Editor.Adornments.FirstOrDefault(item => ReferenceEquals(ProviderFor(item), child));
            return item is null ? throw new ArgumentException("The element is not an editor adornment.", nameof(child)) : Range(new(item.Position, 0));
        }
        public ITextRangeProvider RangeFromAnnotation(IRawElementProviderSimple annotation) => RangeFromChild(annotation);
        public ITextRangeProvider GetCaretRange(out bool isActive)
        {
            isActive = Handler.PlatformView.FocusState != FocusState.Unfocused;
            return Range(new(Editor.SelectionState.Active, 0));
        }
        public ITextRangeProvider GetActiveComposition() => Editor.Composition.Range is { } range ? Range(range) : null!;
        public ITextRangeProvider GetConversionTarget() => GetActiveComposition();
        public bool IsReadOnly => Editor.IsReadOnly;
        public string Value => Editor.Document.Text;
        public void SetValue(string value)
        {
            if (!Editor.TryApplyEdits(Editor.Document.Revision, (RichTextEdit[])[new(new(0, Editor.Document.Length), value)]))
                throw new InvalidOperationException("The editor cannot accept an accessibility edit in its current input state.");
        }
        internal SourceTextRange Range(RichTextRange range) => new(this, range);
        internal IRawElementProviderSimple Provider => ProviderFromPeer(this);
        internal IEnumerable<IRawElementProviderSimple> Providers(RichTextRange range)
        {
            var images = GetChildrenCore().OfType<SourceImagePeer>().Where(peer => peer.Image is { } image && image.Position >= range.Start && image.Position < range.End)
                .Select(peer => (Position: peer.Image!.Position, Provider: (IRawElementProviderSimple?)ProviderFromPeer(peer)));
            var widgets = Editor.Adornments.Where(item => item.Position >= range.Start && item.Position <= range.End)
                .Select(item => (item.Position, Provider: ProviderFor(item)));
            return images.Concat(widgets).OrderBy(static item => item.Position).Select(static item => item.Provider).OfType<IRawElementProviderSimple>();
        }
        internal double Scale => Handler.PlatformView.XamlRoot?.RasterizationScale ?? 1;
        [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
        internal IRawElementProviderSimple? ProviderFor(RichTextAdornment item) => item.View.Handler?.PlatformView is FrameworkElement native &&
            (FromElement(native) ?? CreatePeerForElement(native)) is { } peer ? ProviderFromPeer(peer) : null;
        internal object Attribute(RichTextRange range, int attribute)
        {
            var source = Editor.PresentationSnapshot;
            var value = At(range.Start);
            foreach (var position in source.Runs.Select(static run => run.Start).Concat(source.Paragraphs.Select(static paragraph => paragraph.Start)))
                if (position > range.Start && position < range.End && !Equals(value, At(position))) return DependencyProperty.UnsetValue;
            return value!;

            object? At(int position)
            {
                var format = source.ResolveCharacterFormat(range.IsEmpty ? source.GetCaretFormat(position) : source.GetCharacterFormat(position));
                var paragraph = source.GetParagraphFormat(position);
                return (AutomationTextAttributesEnum)attribute switch
                {
                    AutomationTextAttributesEnum.FontNameAttribute => format.FontFamily ?? Handler.PlatformView.Document.GetDefaultCharacterFormat().Name,
                    AutomationTextAttributesEnum.FontSizeAttribute => format.FontSize ?? Handler.PlatformView.Document.GetDefaultCharacterFormat().Size,
                    AutomationTextAttributesEnum.FontWeightAttribute => format.FontWeight,
                    AutomationTextAttributesEnum.IsItalicAttribute => format.Italic,
                    AutomationTextAttributesEnum.IsHiddenAttribute => format.Hidden || Editor.Folding.FindRange(position) is not null,
                    AutomationTextAttributesEnum.IsReadOnlyAttribute => Editor.IsReadOnly,
                    AutomationTextAttributesEnum.IsActiveAttribute => Handler.PlatformView.FocusState != FocusState.Unfocused,
                    AutomationTextAttributesEnum.IsSubscriptAttribute => format.Script == RichTextScript.Subscript,
                    AutomationTextAttributesEnum.IsSuperscriptAttribute => format.Script == RichTextScript.Superscript,
                    AutomationTextAttributesEnum.ForegroundColorAttribute => ColorValue(format.ForegroundColor ?? Handler.ResolveTextColor() ?? Colors.Black),
                    AutomationTextAttributesEnum.BackgroundColorAttribute => ColorValue(format.BackgroundColor ?? Editor.BackgroundColor ?? Colors.Transparent),
                    AutomationTextAttributesEnum.UnderlineColorAttribute => ColorValue(format.UnderlineColor ?? format.ForegroundColor ?? Handler.ResolveTextColor() ?? Colors.Black),
                    AutomationTextAttributesEnum.StrikethroughColorAttribute => ColorValue(format.StrikethroughColor ?? format.ForegroundColor ?? Handler.ResolveTextColor() ?? Colors.Black),
                    AutomationTextAttributesEnum.UnderlineStyleAttribute => (int)Underline(format.Underline),
                    AutomationTextAttributesEnum.StrikethroughStyleAttribute => (int)(format.Strikethrough switch
                    {
                        RichTextStrikethroughStyle.Single => AutomationTextDecorationLineStyle.Single,
                        RichTextStrikethroughStyle.Double => AutomationTextDecorationLineStyle.Double,
                        _ => AutomationTextDecorationLineStyle.None,
                    }),
                    AutomationTextAttributesEnum.HorizontalTextAlignmentAttribute => (int)paragraph.Alignment,
                    AutomationTextAttributesEnum.IndentationFirstLineAttribute => paragraph.FirstLineIndent,
                    AutomationTextAttributesEnum.StyleNameAttribute => paragraph.StyleName ?? "",
                    AutomationTextAttributesEnum.CultureAttribute => Culture(format.LanguageTag),
                    _ => null, // WinUI maps null to UIA's reserved unsupported value.
                };
            }
            static int ColorValue(Color color)
            {
                color.ToRgba(out var red, out var green, out var blue, out _);
                return red | green << 8 | blue << 16;
            }
            static int Culture(string? tag)
            {
                try { return tag is null ? CultureInfo.CurrentCulture.LCID : CultureInfo.GetCultureInfo(tag).LCID; }
                catch (CultureNotFoundException) { return CultureInfo.InvariantCulture.LCID; }
            }
            static AutomationTextDecorationLineStyle Underline(RichTextUnderlineStyle style) => style switch
            {
                RichTextUnderlineStyle.Single => AutomationTextDecorationLineStyle.Single,
                RichTextUnderlineStyle.Words => AutomationTextDecorationLineStyle.WordsOnly,
                RichTextUnderlineStyle.Double => AutomationTextDecorationLineStyle.Double,
                RichTextUnderlineStyle.Dotted => AutomationTextDecorationLineStyle.Dot,
                RichTextUnderlineStyle.Dash => AutomationTextDecorationLineStyle.Dash,
                RichTextUnderlineStyle.DashDot => AutomationTextDecorationLineStyle.DashDot,
                RichTextUnderlineStyle.DashDotDot => AutomationTextDecorationLineStyle.DashDotDot,
                RichTextUnderlineStyle.Wave => AutomationTextDecorationLineStyle.Wavy,
                RichTextUnderlineStyle.Thick => AutomationTextDecorationLineStyle.ThickSingle,
                RichTextUnderlineStyle.DoubleWave => AutomationTextDecorationLineStyle.DoubleWavy,
                RichTextUnderlineStyle.HeavyWave => AutomationTextDecorationLineStyle.ThickWavy,
                RichTextUnderlineStyle.LongDash => AutomationTextDecorationLineStyle.LongDash,
                _ => AutomationTextDecorationLineStyle.None,
            };
        }
        internal int[] Boundaries(TextUnit unit)
        {
            var source = Editor.Document.CurrentSnapshot;
            IEnumerable<int> boundaries = unit switch
            {
                TextUnit.Character => StringInfo.ParseCombiningCharacters(source.Text),
                TextUnit.Word => Enumerable.Range(0, source.Length).Where(index => index == 0 ||
                    char.IsLetterOrDigit(source.Text[index]) != char.IsLetterOrDigit(source.Text[index - 1]) || char.IsWhiteSpace(source.Text[index]) != char.IsWhiteSpace(source.Text[index - 1])),
                TextUnit.Format => source.Runs.Select(static run => run.Start),
                TextUnit.Line or TextUnit.Paragraph => source.Paragraphs.Select(static paragraph => paragraph.Start),
                _ => new[] { 0 },
            };
            if (unit == TextUnit.Line && Editor.TextLayout.Capture() is { } layout)
                boundaries = boundaries.Concat(layout.Lines.SelectMany(static line => line.SourceRanges).Select(static range => range.Start));
            return [.. boundaries.Append(source.Length).Distinct().Order()];
        }
    }

    [GeneratedWinRTExposedType]
    private sealed partial class SourceImagePeer : AutomationPeer
    {
        private readonly SourceTextPeer _owner;
        private readonly RichTextDocument _document;
        private readonly RichTextTrackedRange _range;
        internal SourceImagePeer(SourceTextPeer owner, int position)
        {
            _owner = owner;
            _document = owner.Editor.Document;
            _range = _document.Tracking.TrackWeakRange(new(position, 1));
        }
        internal RichTextImage? Image => ReferenceEquals(_owner.Editor.Document, _document) && _range.Range is { Length: 1 } range ?
            _document.CurrentSnapshot.Images.FirstOrDefault(image => image.Position == range.Start) : null;
        protected override string GetClassNameCore() => "Image";
        protected override string GetNameCore() => Image?.AlternativeText ?? "";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        protected override bool IsKeyboardFocusableCore() => false;
        protected override Windows.Foundation.Rect GetBoundingRectangleCore()
        {
            if (Image is not { } image || _owner.Editor.TextLayout.Capture() is not { } layout ||
                _owner.Editor.TextLayout.GetRangeBounds(layout, new(image.Position, 1)).FirstOrDefault() is not { Width: > 0 } rect) return default;
            var origin = _owner.GetBoundingRectangle();
            return new(origin.X + rect.X * _owner.Scale, origin.Y + rect.Y * _owner.Scale, rect.Width * _owner.Scale, rect.Height * _owner.Scale);
        }
        protected override bool IsOffscreenCore() => GetBoundingRectangleCore().Width == 0;
    }

    [GeneratedWinRTExposedType]
    private sealed partial class SourceTextRange : ITextRangeProvider, ITextRangeProvider2
    {
        private readonly SourceTextPeer _peer;
        private readonly RichTextDocument _document;
        private RichTextTrackedRange _tracking;
        internal SourceTextRange(SourceTextPeer peer, RichTextRange range)
        {
            _peer = peer;
            _document = peer.Editor.Document;
            _tracking = _document.Tracking.TrackWeakRange(range);
        }
        private RichTextRange Range => ReferenceEquals(_document, _peer.Editor.Document) && _tracking.Range is { } range ? range :
            throw new InvalidOperationException("The accessibility range belongs to a replaced document.");
        private void Set(RichTextRange range) { _tracking.Dispose(); _tracking = _document.Tracking.TrackWeakRange(range); }
        private SourceTextRange Other(ITextRangeProvider range) => range is SourceTextRange other && ReferenceEquals(other._peer, _peer) ? other :
            throw new ArgumentException("The accessibility ranges belong to different editors.", nameof(range));
        public ITextRangeProvider Clone() => _peer.Range(Range);
        public bool Compare(ITextRangeProvider range) => range is SourceTextRange other && ReferenceEquals(other._peer, _peer) && Range == other.Range;
        public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint) =>
            Endpoint(Range, endpoint) - Endpoint(Other(targetRange).Range, targetEndpoint);
        private static int Endpoint(RichTextRange range, TextPatternRangeEndpoint endpoint) => endpoint == TextPatternRangeEndpoint.Start ? range.Start : range.End;
        private void SetEndpoint(TextPatternRangeEndpoint endpoint, int position)
        {
            var range = Range;
            Set(endpoint == TextPatternRangeEndpoint.Start ? new(position, Math.Max(position, range.End) - position) : new(Math.Min(position, range.Start), position - Math.Min(position, range.Start)));
        }
        public void ExpandToEnclosingUnit(TextUnit unit)
        {
            var boundaries = _peer.Boundaries(unit);
            var index = Math.Clamp(Array.BinarySearch(boundaries, Range.Start) is var found && found >= 0 ? found : ~found - 1, 0, Math.Max(0, boundaries.Length - 2));
            Set(new(boundaries[index], boundaries[Math.Min(index + 1, boundaries.Length - 1)] - boundaries[index]));
        }
        public ITextRangeProvider FindText(string text, bool backward, bool ignoreCase)
        {
            ArgumentException.ThrowIfNullOrEmpty(text);
            var range = Range;
            var source = _document.Text.Substring(range.Start, range.Length);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var index = backward ? source.LastIndexOf(text, comparison) : source.IndexOf(text, comparison);
            return index < 0 ? null! : _peer.Range(new(range.Start + index, text.Length));
        }
        public ITextRangeProvider FindAttribute(int attributeId, object value, bool backward)
        {
            var range = Range;
            var runs = _document.CurrentSnapshot.Runs.Where(run => run.End > range.Start && run.Start < range.End);
            foreach (var run in backward ? runs.Reverse() : runs)
            {
                var candidate = new RichTextRange(Math.Max(run.Start, range.Start), Math.Min(run.End, range.End) - Math.Max(run.Start, range.Start));
                if (Equals(_peer.Attribute(candidate, attributeId), value)) return _peer.Range(candidate);
            }
            return null!;
        }
        public object GetAttributeValue(int attributeId) => _peer.Attribute(Range, attributeId);
        public void GetBoundingRectangles(out double[] rectangles)
        {
            var range = Range;
            if (_peer.Editor.TextLayout.Capture() is not { } layout) { rectangles = []; return; }
            var screen = _peer.GetBoundingRectangle();
            var bounds = range.IsEmpty ? _peer.Editor.TextLayout.GetCaretBounds(layout, range.Start) is { } caret ? new[] { caret } : [] :
                _peer.Editor.TextLayout.GetRangeBounds(layout, range);
            rectangles = [.. bounds.SelectMany(rect => new[] { screen.X + rect.X * _peer.Scale, screen.Y + rect.Y * _peer.Scale, rect.Width * _peer.Scale, rect.Height * _peer.Scale })];
        }
        public IRawElementProviderSimple[] GetChildren()
        {
            var range = Range;
            return [.. _peer.Providers(range)];
        }
        public IRawElementProviderSimple GetEnclosingElement() => _peer.Provider;
        public string GetText(int maxLength)
        {
            if (maxLength < -1) throw new ArgumentOutOfRangeException(nameof(maxLength));
            var range = Range;
            var length = maxLength < 0 ? range.Length : Math.Min(range.Length, maxLength);
            if (length > 0 && length < range.Length && char.IsHighSurrogate(_document.Text[range.Start + length - 1])) length--;
            return _document.Text.Substring(range.Start, length);
        }
        public int Move(TextUnit unit, int count)
        {
            if (count == 0) return 0;
            var expanded = !Range.IsEmpty;
            if (expanded) ExpandToEnclosingUnit(unit);
            var start = Range.Start;
            Set(new(start, 0));
            var moved = MoveEndpointByUnit(TextPatternRangeEndpoint.Start, unit, count);
            Set(new(Range.Start, 0));
            if (expanded) ExpandToEnclosingUnit(unit);
            return moved;
        }
        public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
        {
            if (count == 0) return 0;
            var boundaries = _peer.Boundaries(unit);
            var position = Endpoint(Range, endpoint);
            var index = Array.BinarySearch(boundaries, position);
            if (index < 0) index = count > 0 ? ~index - 1 : ~index;
            var target = (int)Math.Clamp((long)index + count, 0, boundaries.Length - 1);
            SetEndpoint(endpoint, boundaries[target]);
            return target - index;
        }
        public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint) =>
            SetEndpoint(endpoint, Endpoint(Other(targetRange).Range, targetEndpoint));
        public void Select() => _peer.Editor.SelectedRange = Range;
        public void AddToSelection() => Select();
        public void RemoveFromSelection() { if (_peer.Editor.SelectedRange == Range) _peer.Editor.SelectedRange = new(Range.Start, 0); }
        public void ScrollIntoView(bool alignToTop) => ((IRichEditorHandler)_peer.Handler).ScrollIntoView(Range);
        public void ShowContextMenu()
        {
            Select();
            _peer.Handler.PlatformView.ContextFlyout?.ShowAt(_peer.Handler.PlatformView);
        }
    }
}
