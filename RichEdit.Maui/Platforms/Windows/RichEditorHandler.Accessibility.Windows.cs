using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Text;
using Microsoft.UI.Xaml.Controls;
using WinRT;
using TextEdit.Core;
using IValueProvider = Microsoft.UI.Xaml.Automation.Provider.IValueProvider;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    partial void CompleteCompositionTrackingCore()
    {
        if (((IElementHandler)this).PlatformView is FrameworkElement view)
            (FrameworkElementAutomationPeer.FromElement(view) as SourceTextPeer)?.CompleteCompositionTracking();
    }

    private sealed partial class SourceRichEditBox(RichEditorHandler handler) : RichEditBox
    {
        protected override void OnDragOver(Microsoft.UI.Xaml.DragEventArgs args) => handler.OnNativeDragOver(this, args);
        protected override void OnDrop(Microsoft.UI.Xaml.DragEventArgs args) => handler.OnNativeDrop(this, args);
        protected override AutomationPeer OnCreateAutomationPeer() => new SourceTextPeer(this, handler);
    }

    // A RichEditBoxAutomationPeer exposes RichEdit's windowless native child directly to UIA.
    // Use a framework peer so every text pattern and child addresses logical source instead.
    [GeneratedWinRTExposedType]
    private sealed partial class SourceTextPeer : FrameworkElementAutomationPeer, ITextProvider, ITextProvider2, ITextEditProvider, IValueProvider
    {
        internal readonly RichEditorHandler Handler;
        private readonly List<SourceImagePeer> _imagePeers = [];
        private NativeEditorView? _eventEditor;
        private readonly List<WeakReference<SourceTextRange>> _provisionalRanges = [];
        private TextSnapshot _inputText = TextSnapshot.Empty;

        internal void TrackCompositionRange(SourceTextRange range) => _provisionalRanges.Add(new(range));

        internal void CompleteCompositionTracking()
        {
            foreach (var reference in _provisionalRanges)
                if (reference.TryGetTarget(out var range))
                    range.CompleteCompositionTracking();
            _provisionalRanges.Clear();
        }

        internal NativeEditorView Editor => Handler.VirtualView;

        internal SourceTextPeer(SourceRichEditBox view, RichEditorHandler handler) : base(view)
        {
            Handler = handler;
            Connect();
        }

        internal void Connect()
        {
            if (ReferenceEquals(_eventEditor, Editor))
                return;

            Disconnect();
            _eventEditor = Editor;
            _eventEditor.TextChanged += OnTextChanged;
            _eventEditor.SelectionsChanged += OnSelectionChanged;
            _eventEditor.DocumentChanged += OnDocumentChanged;
            _eventEditor.CompositionChanged += OnCompositionChanged;
            _inputText = Editor.InputSnapshot.TextBuffer;
        }

        internal void Disconnect()
        {
            if (_eventEditor is null)
                return;

            _eventEditor.TextChanged -= OnTextChanged;
            _eventEditor.SelectionsChanged -= OnSelectionChanged;
            _eventEditor.DocumentChanged -= OnDocumentChanged;
            _eventEditor.CompositionChanged -= OnCompositionChanged;
            _eventEditor = null;
        }

        private void OnTextChanged(object? sender, RichTextTextChangedEventArgs args)
        {
            TextChanged(Editor.InputSnapshot.TextBuffer);
            if (Editor.Composition.IsActive && ListenerExists(AutomationEvents.TextEditTextChanged))
                RaiseTextEditTextChangedEvent(AutomationTextEditChangeType.Composition, args.Changes.Select(static change => change.InsertedText).ToArray());
        }

        private void OnCompositionChanged(object? sender, RichTextCompositionChangedEventArgs args)
        {
            OnSelectionChanged(sender, EventArgs.Empty);
            var text = Editor.InputSnapshot.TextBuffer;
            if (!_inputText.ContentEquals(text))
                TextChanged(text);
            if (ListenerExists(AutomationEvents.TextEditTextChanged))
                RaiseTextEditTextChangedEvent(args.Current.IsActive ? AutomationTextEditChangeType.Composition :
                    AutomationTextEditChangeType.CompositionFinalized, (string[])[text.ToString()]);
        }

        private void OnDocumentChanged(object? sender, RichTextDocumentReplacedEventArgs args) => TextChanged(Editor.InputSnapshot.TextBuffer);

        private void TextChanged(TextSnapshot after)
        {
            var before = _inputText;
            _inputText = after;
            if (ListenerExists(AutomationEvents.TextPatternOnTextChanged))
                RaiseAutomationEvent(AutomationEvents.TextPatternOnTextChanged);
            if (ListenerExists(AutomationEvents.PropertyChanged) && !before.ContentEquals(after))
                RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, before.ToString(), after.ToString());
        }

        private void OnSelectionChanged(object? sender, EventArgs args)
        {
            if (ListenerExists(AutomationEvents.TextPatternOnTextSelectionChanged))
                RaiseAutomationEvent(AutomationEvents.TextPatternOnTextSelectionChanged);
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
            _imagePeers.RemoveAll(static peer => peer.CanonicalImage is null);
            foreach (var image in Editor.Document.CurrentSnapshot.Images)
                if (!_imagePeers.Any(peer => peer.CanonicalImage?.Position == image.Position))
                    _imagePeers.Add(new(this, image.Position));

            return _imagePeers.Where(static peer => peer.Image is not null).OrderBy(static peer => peer.Image!.Position).Cast<AutomationPeer>().ToArray();
        }

        public ITextRangeProvider DocumentRange => Range(new(0, Editor.InputSnapshot.Length));

        public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Multiple;

        public ITextRangeProvider[] GetSelection() => Editor.Composition.IsActive
            ? [Range(Editor.InputSelection.Range)]
            : Editor.Selections.Select(item => (ITextRangeProvider)Range(new(item.Range.Start, item.Range.Length))).ToArray();

        public ITextRangeProvider[] GetVisibleRanges() => Editor.TextLayout.CaptureInput() is { } layout ?
            layout.Lines.SelectMany(static line => line.SourceRanges).Select(range => (ITextRangeProvider)Range(range)).ToArray() : [];

        public ITextRangeProvider RangeFromPoint(Windows.Foundation.Point point)
        {
            var screen = GetBoundingRectangle();
            var scale = Scale;
            var layout = Editor.TextLayout.CaptureInput();
            var hit = layout is null ? null : Editor.TextLayout.HitTest(layout, new((point.X - screen.X) / scale, (point.Y - screen.Y) / scale));
            return Range(new(hit?.Position ?? Editor.SelectionState.Active, 0));
        }

        public ITextRangeProvider RangeFromChild(IRawElementProviderSimple child)
        {
            var image = GetChildrenCore().OfType<SourceImagePeer>().FirstOrDefault(peer => ReferenceEquals(ProviderFromPeer(peer), child));
            if (image?.Image is { } sourceImage)
                return Range(new(sourceImage.Position, 1));

            var item = Editor.Adornments.FirstOrDefault(item => ReferenceEquals(ProviderFor(item), child));
            return item is null ? throw new ArgumentException("The element is not an editor adornment.", nameof(child)) : Range(new(item.Position, 0));
        }

        public ITextRangeProvider RangeFromAnnotation(IRawElementProviderSimple annotation) => RangeFromChild(annotation);

        public ITextRangeProvider GetCaretRange(out bool isActive)
        {
            isActive = Handler.PlatformView.FocusState != FocusState.Unfocused;
            return Range(new(Editor.InputSelection.Active, 0));
        }

        public ITextRangeProvider GetActiveComposition() => Editor.Composition.Range is { } range ? Range(range) : null!;

        public ITextRangeProvider GetConversionTarget() => GetActiveComposition();

        public bool IsReadOnly => Editor.IsReadOnly;

        public string Value => Editor.InputSnapshot.Text;

        public void SetValue(string value)
        {
            if (!Editor.TryApplyEdits(Editor.Document.Revision, (RichTextEdit[])[new(new(0, Editor.Document.Length), value)]))
                throw new InvalidOperationException("The editor cannot accept an accessibility edit in its current input state.");
        }

        internal SourceTextRange Range(TextSpan range) => new(this, range);

        internal IRawElementProviderSimple Provider => ProviderFromPeer(this);

        internal IEnumerable<IRawElementProviderSimple> Providers(TextSpan range)
        {
            var images = GetChildrenCore().OfType<SourceImagePeer>().Where(peer => peer.Image is { } image && image.Position >= range.Start && image.Position < range.End)
                .Select(peer => (Position: peer.Image!.Position, Provider: (IRawElementProviderSimple?)ProviderFromPeer(peer)));
            var widgets = Editor.Adornments.Where(item => item.Position >= range.Start && item.Position <= range.End)
                .Select(item => (item.Position, Provider: ProviderFor(item)));
            return images.Concat(widgets).OrderBy(static item => item.Position).Select(static item => item.Provider).OfType<IRawElementProviderSimple>();
        }

        internal double Scale => Handler.PlatformView.XamlRoot?.RasterizationScale ?? 1;

        [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
        internal IRawElementProviderSimple? ProviderFor(RichTextAdornment item) => item.CreatedView?.Handler?.PlatformView is FrameworkElement native &&
            (FromElement(native) ?? CreatePeerForElement(native)) is { } peer ? ProviderFromPeer(peer) : null;

        internal object Attribute(TextSpan range, int attribute)
        {
            var source = Editor.PresentationSnapshot;
            var value = At(range.Start);
            for (var index = source.FindRunIndex(range.Start); index < source.Runs.Length && source.Runs[index].Start < range.End; index++)
                if (source.Runs[index].Start > range.Start && !Equals(value, At(source.Runs[index].Start)))
                    return DependencyProperty.UnsetValue;
            for (var index = source.FindParagraphIndex(range.Start); index < source.Paragraphs.Length && source.Paragraphs[index].Start < range.End; index++)
                if (source.Paragraphs[index].Start > range.Start && !Equals(value, At(source.Paragraphs[index].Start)))
                    return DependencyProperty.UnsetValue;

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
                try
                {
                    return tag is null ? CultureInfo.CurrentCulture.LCID : CultureInfo.GetCultureInfo(tag).LCID;
                }
                catch (CultureNotFoundException)
                {
                    return CultureInfo.InvariantCulture.LCID;
                }
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
            var source = Editor.InputSnapshot;
            IEnumerable<int> boundaries = unit switch
            {
                TextUnit.Character => StringInfo.ParseCombiningCharacters(source.Text),
                TextUnit.Word => Enumerable.Range(0, source.Length).Where(index => index == 0 ||
                    char.IsLetterOrDigit(source.Text[index]) != char.IsLetterOrDigit(source.Text[index - 1]) || char.IsWhiteSpace(source.Text[index]) != char.IsWhiteSpace(source.Text[index - 1])),
                TextUnit.Format => source.Runs.Select(static run => run.Start),
                TextUnit.Line or TextUnit.Paragraph => source.Paragraphs.Select(static paragraph => paragraph.Start),
                _ => new[] { 0 },
            };
            if (unit == TextUnit.Line && Editor.TextLayout.CaptureInput() is { } layout)
                boundaries = boundaries.Concat(layout.Lines.SelectMany(static line => line.SourceRanges).Select(static range => range.Start));

            return [.. boundaries.Append(source.Length).Distinct().Order()];
        }
    }

    [GeneratedWinRTExposedType]
    private sealed partial class SourceImagePeer : AutomationPeer
    {
        private readonly SourceTextPeer _owner;
        private readonly RichTextDocument _document;
        private readonly TextTrackedRange _range;

        internal SourceImagePeer(SourceTextPeer owner, int position)
        {
            _owner = owner;
            _document = owner.Editor.Document;
            _range = _document.Tracking.TrackWeakRange(new(position, 1));
        }

        internal RichTextImage? CanonicalImage => ReferenceEquals(_owner.Editor.Document, _document) && _range.Range is { Length: 1 } range ?
            _document.CurrentSnapshot.Images.FirstOrDefault(image => image.Position == range.Start) : null;

        internal RichTextImage? Image
        {
            get
            {
                if (CanonicalImage is not { } image)
                    return null;
                var range = _owner.Editor.MapRangeToInput(new(image.Position, 1));
                return range.Length == 1 ? _owner.Editor.InputSnapshot.Images.FirstOrDefault(item => item.Position == range.Start) : null;
            }
        }

        protected override string GetClassNameCore() => "Image";

        protected override string GetNameCore() => Image?.AlternativeText ?? "";

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;

        protected override bool IsKeyboardFocusableCore() => false;

        protected override Windows.Foundation.Rect GetBoundingRectangleCore()
        {
            if (Image is not { } image || _owner.Editor.TextLayout.CaptureInput() is not { } layout ||
                _owner.Editor.TextLayout.GetRangeBounds(layout, new(image.Position, 1)).FirstOrDefault() is not { Width: > 0 } rect)
                return default;

            var origin = _owner.GetBoundingRectangle();
            return new(origin.X + rect.X * _owner.Scale, origin.Y + rect.Y * _owner.Scale, rect.Width * _owner.Scale, rect.Height * _owner.Scale);
        }

        protected override bool IsOffscreenCore() => GetBoundingRectangleCore().Width == 0;
    }

    [GeneratedWinRTExposedType]
    private sealed partial class SourceTextRange : ITextRangeProvider, ITextRangeProvider2
    {
        private readonly SourceTextPeer _peer;
        private readonly RichTextDocument _rootDocument;
        private RichTextDocument _document;
        private TextTrackedRange _tracking;
        private RichTextDocument? _previewOwner;
        private TextTrackedRange? _previewTracking;

        internal SourceTextRange(SourceTextPeer peer, TextSpan range)
        {
            _peer = peer;
            _rootDocument = peer.Editor.Document;
            _document = peer.Editor.InputDocument;
            _tracking = _document.Tracking.TrackWeakRange(range);
            if (!ReferenceEquals(_document, _rootDocument))
                peer.TrackCompositionRange(this);
        }

        private TextSpan Range
        {
            get
            {
                if (!ReferenceEquals(_rootDocument, _peer.Editor.Document) || _tracking.Range is not { } range)
                    throw new InvalidOperationException("The accessibility range belongs to a replaced document.");
                var input = _peer.Editor.InputDocument;
                if (ReferenceEquals(_document, _rootDocument) && !ReferenceEquals(input, _rootDocument))
                {
                    if (!ReferenceEquals(_previewOwner, input))
                    {
                        _previewTracking?.Dispose();
                        var mapped = _peer.Editor.MapRangeToInput(range);
                        _previewOwner = input;
                        _previewTracking = input.Tracking.TrackWeakRange(mapped);
                        _peer.TrackCompositionRange(this);
                    }
                    return _previewTracking!.Range!.Value;
                }
                return range;
            }
        }

        internal void CompleteCompositionTracking()
        {
            _previewTracking?.Dispose();
            _previewTracking = null;
            _previewOwner = null;
            if (!ReferenceEquals(_rootDocument, _peer.Editor.Document) || ReferenceEquals(_document, _rootDocument))
                return;
            var range = _tracking.Range ?? TextSpan.Empty;
            _tracking.Dispose();
            _document = _rootDocument;
            _tracking = _document.Tracking.TrackWeakRange(range.Clamp(_document.Length));
        }

        private void Set(TextSpan range)
        {
            _tracking.Dispose();
            _previewTracking?.Dispose();
            _previewTracking = null;
            _previewOwner = null;
            _document = _peer.Editor.InputDocument;
            _tracking = _document.Tracking.TrackWeakRange(range);
            if (!ReferenceEquals(_document, _rootDocument))
                _peer.TrackCompositionRange(this);
        }

        private SourceTextRange Other(ITextRangeProvider range) => range is SourceTextRange other && ReferenceEquals(other._peer, _peer) ? other :
            throw new ArgumentException("The accessibility ranges belong to different editors.", nameof(range));

        public ITextRangeProvider Clone() => _peer.Range(Range);

        public bool Compare(ITextRangeProvider range) => range is SourceTextRange other && ReferenceEquals(other._peer, _peer) && Range == other.Range;

        public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint) =>
            Endpoint(Range, endpoint) - Endpoint(Other(targetRange).Range, targetEndpoint);

        private static int Endpoint(TextSpan range, TextPatternRangeEndpoint endpoint) => endpoint == TextPatternRangeEndpoint.Start ? range.Start : range.End;

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
            var source = _peer.Editor.InputSnapshot.GetText(range);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var index = backward ? source.LastIndexOf(text, comparison) : source.IndexOf(text, comparison);
            return index < 0 ? null! : _peer.Range(new(range.Start + index, text.Length));
        }

        public ITextRangeProvider FindAttribute(int attributeId, object value, bool backward)
        {
            var range = Range;
            if (range.IsEmpty)
                return null!;

            var source = _peer.Editor.PresentationSnapshot;
            var boundaries = source.Runs.Select(static run => run.Start).Concat(source.Paragraphs.Select(static paragraph => paragraph.Start))
                .Where(position => position > range.Start && position < range.End).Append(range.Start).Append(range.End).Distinct().Order().ToArray();
            TextSpan? match = null;
            for (var index = backward ? boundaries.Length - 2 : 0; index >= 0 && index < boundaries.Length - 1; index += backward ? -1 : 1)
            {
                var candidate = new TextSpan(boundaries[index], boundaries[index + 1] - boundaries[index]);
                if (Equals(_peer.Attribute(candidate, attributeId), value))
                    match = match is { } preceding ? new(Math.Min(preceding.Start, candidate.Start), preceding.Length + candidate.Length) : candidate;
                else if (match is not null)
                    break;
            }

            return match is { } found ? _peer.Range(found) : null!;
        }

        public object GetAttributeValue(int attributeId) => _peer.Attribute(Range, attributeId);

        public void GetBoundingRectangles(out double[] rectangles)
        {
            var range = Range;
            if (_peer.Editor.TextLayout.CaptureInput() is not { } layout)
            {
                rectangles = [];
                return;
            }

            var screen = _peer.GetBoundingRectangle();
            var bounds = range.IsEmpty ? _peer.Editor.TextLayout.GetCaretBounds(layout, range.Start) is { } caret ? new[] { caret } : [] :
                _peer.Editor.TextLayout.GetRangeBounds(layout, range);
            rectangles = [.. bounds.SelectMany(rect => new[]
                {
                screen.X + rect.X * _peer.Scale,
                screen.Y + rect.Y * _peer.Scale,
                rect.Width * _peer.Scale,
                rect.Height * _peer.Scale
                })];
        }

        public IRawElementProviderSimple[] GetChildren()
        {
            var range = Range;
            return [.. _peer.Providers(range)];
        }

        public IRawElementProviderSimple GetEnclosingElement() => _peer.Provider;

        public string GetText(int maxLength)
        {
            if (maxLength < -1)
                throw new ArgumentOutOfRangeException(nameof(maxLength));

            var range = Range;
            var length = maxLength < 0 ? range.Length : Math.Min(range.Length, maxLength);
            if (length > 0 && length < range.Length && char.IsHighSurrogate(_peer.Editor.InputSnapshot.TextBuffer[range.Start + length - 1]))
                length--;

            return _peer.Editor.InputSnapshot.GetText(new(range.Start, length));
        }

        public int Move(TextUnit unit, int count)
        {
            if (count == 0)
                return 0;

            var range = Range;
            if (!range.IsEmpty)
            {
                var boundaries = _peer.Boundaries(unit);
                var last = Math.Max(0, boundaries.Length - 2);
                var index = Array.BinarySearch(boundaries, range.Start);
                index = Math.Clamp(index >= 0 ? index : ~index - 1, 0, last);
                var target = (int)Math.Clamp((long)index + count, 0, last);
                if (target == index)
                    return 0;

                Set(new(boundaries[target], boundaries[target + 1] - boundaries[target]));
                return target - index;
            }

            var moved = MoveEndpointByUnit(TextPatternRangeEndpoint.Start, unit, count);
            Set(new(Range.Start, 0));
            return moved;
        }

        public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
        {
            if (count == 0)
                return 0;

            var boundaries = _peer.Boundaries(unit);
            var position = Endpoint(Range, endpoint);
            var index = Array.BinarySearch(boundaries, position);
            if (index < 0)
                index = count > 0 ? ~index - 1 : ~index;

            var target = (int)Math.Clamp((long)index + count, 0, boundaries.Length - 1);
            SetEndpoint(endpoint, boundaries[target]);
            return target - index;
        }

        public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint) =>
            SetEndpoint(endpoint, Endpoint(Other(targetRange).Range, targetEndpoint));

        public void Select()
        {
            if (_peer.Editor.Composition.IsActive)
                _peer.Editor.InputSelection = new(Range.Start, Range.End);
            else
                _peer.Editor.Selections = new((TextSelection[])[new(Range.Start, Range.End)]);
        }

        public void AddToSelection()
        {
            if (_peer.Editor.Composition.IsActive)
                throw new InvalidOperationException("Additional selections cannot be changed during composition.");
            var selections = _peer.Editor.Selections;
            _peer.Editor.Selections = new(selections.Append(new TextSelection(Range.Start, Range.End)), selections.PrimaryIndex);
        }

        public void RemoveFromSelection()
        {
            if (_peer.Editor.Composition.IsActive)
                throw new InvalidOperationException("Additional selections cannot be changed during composition.");
            var removed = Range;
            var selections = _peer.Editor.Selections;
            var remaining = new List<TextSelection>();
            foreach (var item in selections)
            {
                var range = item.Range;
                if (range.IsEmpty)
                {
                    if (range.Start < removed.Start || range.Start > removed.End)
                        remaining.Add(item);
                    continue;
                }
                if (removed.IsEmpty || range.End <= removed.Start || range.Start >= removed.End)
                {
                    remaining.Add(item);
                    continue;
                }
                var reversed = item.Anchor > item.Active;
                if (range.Start < removed.Start)
                    remaining.Add(item with { Anchor = reversed ? removed.Start : range.Start, Active = reversed ? range.Start : removed.Start,
                        Id = reversed || range.End <= removed.End ? item.Id : Guid.NewGuid() });
                if (range.End > removed.End)
                    remaining.Add(item with { Anchor = reversed ? range.End : removed.End, Active = reversed ? removed.End : range.End,
                        Id = !reversed || range.Start >= removed.Start ? item.Id : Guid.NewGuid() });
            }
            _peer.Editor.Selections = remaining.Count == 0 ? TextSelectionSet.Caret(removed.Start) :
                new(remaining, Math.Max(0, remaining.FindIndex(item => item.Id == selections.Primary.Id)));
        }

        public void ScrollIntoView(bool alignToTop) => ((IRichEditorHandler)_peer.Handler).ScrollIntoView(Range);

        public void ShowContextMenu()
        {
            Select();
            _peer.Handler.PlatformView.ContextFlyout?.ShowAt(_peer.Handler.PlatformView);
        }
    }
}
