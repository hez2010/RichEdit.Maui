using RichEdit.Core;
using System.Runtime.CompilerServices;
using CoreTextSelection = RichEdit.Core.RichTextSelection;

namespace RichEdit.Maui;

public sealed partial class RichTextDocument
{
    private static readonly ConditionalWeakTable<RichDocument, RichTextDocument> StructuredProjections = new();
    internal RichDocument? StructuredDocument { get; }
    internal RichTextProjection? StructuredProjection { get; private set; }
    private readonly IDisposable? _structuredSubscription;
    private object? _structuredOrigin;
    private RichTextChangeSet? _lastStructuredChange;
    private RichTextChangeSet? _structuredCorrections;

    private string SerializeStructured()
    {
        using var stream = new MemoryStream();
        RichEdit.Core.Rtf.RtfCodec.WriteAsync(StructuredProjection!.Snapshot, stream).GetAwaiter().GetResult();
        return System.Text.Encoding.ASCII.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    internal static RichTextDocument ProjectStructured(RichDocument document) => StructuredProjections.GetValue(document, static model => new(model));

    private RichTextDocument(RichDocument document)
    {
        StructuredDocument = document;
        StructuredProjection = document.CurrentSnapshot.ProjectText();
        _snapshot = RichTextDocumentSnapshot.CreateStructuredProjection(StructuredProjection);
        Tracking = new(() => Length, document.VerifyAccess);
        _structuredSubscription = document.SubscribeProjection(PublishStructured);
        document.StateChanged += (_, _) => RaiseUndoStateChanged();
    }

    private void PublishStructured(RichDocumentChangedEventArgs change)
    {
        var before = _snapshot;
        StructuredProjection = change.After.ProjectText();
        _snapshot = RichTextDocumentSnapshot.CreateStructuredProjection(StructuredProjection);
        _lastStructuredChange = new(before.Version, _snapshot.Version, (RichTextChangeOrigin)change.Options.Origin,
            [.. CreateDelta(before, _snapshot)], change.Options.Tag, _structuredOrigin, before, _snapshot, undoDescription: change.Options.Description)
        { NativeCorrections = _structuredCorrections };
        InvalidateRtfCache();
        RaiseChanged(_lastStructuredChange, before, undoStateChanged: true);
    }

    internal static CoreTextSelection ToStructuredSelection(RichTextProjection projection, TextSelectionState selection)
    {
        var anchor = projection.GetPoint(selection.Anchor) ?? throw new ArgumentException("The native selection addresses a structural separator.");
        var active = projection.GetPoint(selection.Active) ?? throw new ArgumentException("The native selection addresses a structural separator.");
        return new(anchor, active);
    }

    private RichTextChangeSet CommitStructuredChanges(IReadOnlyList<RichTextChange> changes, RichTextDocumentSnapshot candidate,
        RichTextEditOptions options, RichTextChangeOrigin origin, object? sourceToken, RichTextChangeSet? corrections = null)
    {
        var document = StructuredDocument!;
        var view = FindEditingView(sourceToken);
        var version = Version;
        _structuredOrigin = sourceToken;
        _structuredCorrections = corrections;
        try
        {
            void Apply(RichDocumentEdit edit)
            {
                ApplyStructuredText(edit, changes, view?.StructuredSession?.TypingCharacterFormat);
                // Native fonts, proofing languages, and display overrides are not
                // authored typing attributes. Explicit formatting commands have
                // their own format-only transaction; text input uses session state.
                if (!changes.Any(change => change.Kind == RichTextChangeKind.Text) && changes.Count != 0)
                    ApplyStructuredFormatting(edit, candidate, changes);
                if (view is not null)
                {
                    var projection = edit.Snapshot.ProjectText();
                    var selections = view.GetSelectionsAfterEdit(changes, projection.Text.Length);
                    edit.SetSelection(new RichTextSelectionSet(projection.Snapshot,
                        selections.Select(item => new RichTextSelectionItem(ToStructuredSelection(projection, new(item.Anchor, item.Active)))
                        { Id = item.Id, PreferredVisualX = item.PreferredVisualX }), selections.PrimaryIndex));
                }
            }
            var metadata = new RichEditOptions(options.UndoDescription, options.Tag, (TextChangeOrigin)origin);
            if (view?.StructuredSession is { } session)
            {
                if (view.IsReadOnly) return new(version, version, origin, [], options.Tag);
                var result = session.Edit(Apply, metadata);
                if (!result.Accepted) throw new InvalidOperationException($"The native rich edit was rejected: {result.Rejection}.");
            }
            else document.Edit(Apply, metadata);
            return Version == version ? new(version, version, origin, [], options.Tag) : _lastStructuredChange!;
        }
        finally { _structuredOrigin = null; _structuredCorrections = null; }
    }

    internal static void ApplyStructuredText(RichDocumentEdit edit, IEnumerable<RichTextChange> changes, RichCharacterStyle? typingFormat = null)
    {
        foreach (var change in changes.OfType<RichTextTextChange>())
        {
            var projection = edit.Snapshot.ProjectText();
            var selection = ToStructuredSelection(projection, new(change.OldRange.Start, change.OldRange.End));
            edit.ReplaceSelection(selection, change.InsertedText, typingFormat);
        }
    }

    private static void ApplyStructuredFormatting(RichDocumentEdit edit, RichTextDocumentSnapshot candidate, IReadOnlyList<RichTextChange> changes)
    {
        var projection = edit.Snapshot.ProjectText();
        var expected = RichTextDocumentSnapshot.CreateStructuredProjection(projection);
        foreach (var change in changes.Where(change => change.Kind == RichTextChangeKind.CharacterFormat))
            foreach (var paragraph in projection.Paragraphs)
            {
                var start = Math.Max(change.NewRange.Start, paragraph.Range.Start);
                var end = Math.Min(change.NewRange.End, paragraph.Range.End);
                if (start >= end) continue;
                foreach (var run in candidate.Runs.Where(run => run.End > start && run.Start < end))
                {
                    var first = Math.Max(start, run.Start); var last = Math.Min(end, run.End);
                    if (run.Format == expected.GetCharacterFormat(first)) continue;
                    var position = paragraph.Range.Start;
                    foreach (var id in projection.Snapshot.Get<RichParagraph>(paragraph.Paragraph).Inlines)
                    {
                        var inline = projection.Snapshot.Get<RichInline>(id);
                        var next = position + RichDocumentSnapshot.InlineLength(inline);
                        var from = Math.Max(first, position); var to = Math.Min(last, next);
                        if (from < to)
                            edit.SetCharacterFormat(new(projection.Story, paragraph.Paragraph, from - paragraph.Range.Start), to - from,
                                inline.Format.Overlay(CharacterOverrides(expected.GetCharacterFormat(from), run.Format)));
                        position = next;
                        if (position >= last) break;
                    }
                }
            }
        var unsupported = changes.FirstOrDefault(change => change.Kind is not (RichTextChangeKind.Text or RichTextChangeKind.CharacterFormat or RichTextChangeKind.ParagraphFormat));
        if (unsupported is not null) throw new InvalidOperationException("Use structured document commands for authored objects, resources, lists, and defaults.");
        foreach (var change in changes.Where(change => change.Kind == RichTextChangeKind.ParagraphFormat))
            foreach (var paragraph in projection.Paragraphs.Where(paragraph => paragraph.Range.End >= change.NewRange.Start && paragraph.Range.Start <= change.NewRange.End))
                if (candidate.GetParagraphFormat(paragraph.Range.Start) != expected.GetParagraphFormat(paragraph.Range.Start))
                    edit.SetParagraphFormat(paragraph.Paragraph, projection.Snapshot.Get<RichParagraph>(paragraph.Paragraph).Format.Overlay(
                        ParagraphOverrides(expected.GetParagraphFormat(paragraph.Range.Start), candidate.GetParagraphFormat(paragraph.Range.Start))));
    }

    internal static RichCharacterStyle CharacterOverrides(RichTextCharacterFormat before, RichTextCharacterFormat after)
    {
        var value = ToStructured(after);
        return new()
        {
            FontFamily = Equals(before.FontFamily, after.FontFamily) ? default : value.FontFamily,
            FontSize = Equals(before.FontSize, after.FontSize) ? default : value.FontSize,
            Bold = Equals(before.Bold, after.Bold) ? default : value.Bold,
            Italic = Equals(before.Italic, after.Italic) ? default : value.Italic,
            Underline = Equals(before.Underline, after.Underline) ? default : value.Underline,
            UnderlineColor = Equals(before.UnderlineColor, after.UnderlineColor) ? default : value.UnderlineColor,
            Strike = Equals(before.Strikethrough, after.Strikethrough) ? default : value.Strike,
            Foreground = Equals(before.ForegroundColor, after.ForegroundColor) ? default : value.Foreground,
            Background = Equals(before.BackgroundColor, after.BackgroundColor) ? default : value.Background,
            Script = Equals(before.Script, after.Script) ? default : value.Script,
            CharacterSpacing = Equals(before.CharacterSpacing, after.CharacterSpacing) ? default : value.CharacterSpacing,
            SmallCaps = Equals(before.SmallCaps, after.SmallCaps) ? default : value.SmallCaps,
            AllCaps = Equals(before.AllCaps, after.AllCaps) ? default : value.AllCaps,
            Outline = Equals(before.Outline, after.Outline) ? default : value.Outline,
            Shadow = Equals(before.Shadow, after.Shadow) ? default : value.Shadow,
            Hidden = Equals(before.Hidden, after.Hidden) ? default : value.Hidden,
            Language = Equals(before.LanguageTag, after.LanguageTag) ? default : value.Language,
            Direction = Equals(before.Direction, after.Direction) ? default : value.Direction,
        };
    }

    internal static RichCharacterStyle ToStructured(RichTextCharacterFormat value) => new()
    {
        FontFamily = value.FontFamily, FontSize = value.FontSize is { } size ? RichFontSize.FromPoints(size) : null,
        Bold = value.Bold, Italic = value.Italic,
        Underline = value.Underline switch { RichTextUnderlineStyle.Single => RichUnderline.Single, RichTextUnderlineStyle.Double => RichUnderline.Double,
            RichTextUnderlineStyle.Dotted => RichUnderline.Dotted, RichTextUnderlineStyle.Dash => RichUnderline.Dashed,
            RichTextUnderlineStyle.Wave => RichUnderline.Wave, RichTextUnderlineStyle.Words => RichUnderline.Words, _ => RichUnderline.None },
        UnderlineColor = Color(value.UnderlineColor), Foreground = Color(value.ForegroundColor), Background = Color(value.BackgroundColor),
        Strike = value.Strikethrough switch { RichTextStrikethroughStyle.Single => RichStrike.Single, RichTextStrikethroughStyle.Double => RichStrike.Double, _ => RichStrike.None },
        Script = value.Script switch { RichTextScript.Subscript => RichScript.Subscript, RichTextScript.Superscript => RichScript.Superscript, _ => RichScript.Normal },
        CharacterSpacing = RichLength.FromPoints(value.CharacterSpacing), SmallCaps = value.SmallCaps, AllCaps = value.AllCaps,
        Outline = value.Outline, Shadow = value.Shadow, Hidden = value.Hidden, Language = value.LanguageTag,
        Direction = Direction(value.Direction),
    };
    private static RichParagraphStyle ParagraphOverrides(RichTextParagraphFormat before, RichTextParagraphFormat after)
    {
        var value = ToStructured(after);
        return new()
        {
            Alignment = Equals(before.Alignment, after.Alignment) ? default : value.Alignment,
            Direction = Equals(before.Direction, after.Direction) ? default : value.Direction,
            LeadingIndent = Equals(before.LeadingIndent, after.LeadingIndent) ? default : value.LeadingIndent,
            TrailingIndent = Equals(before.TrailingIndent, after.TrailingIndent) ? default : value.TrailingIndent,
            FirstLineIndent = Equals(before.FirstLineIndent, after.FirstLineIndent) ? default : value.FirstLineIndent,
            SpaceBefore = Equals(before.SpaceBefore, after.SpaceBefore) ? default : value.SpaceBefore,
            SpaceAfter = Equals(before.SpaceAfter, after.SpaceAfter) ? default : value.SpaceAfter,
            Background = Equals(before.BackgroundColor, after.BackgroundColor) ? default : value.Background,
        };
    }

    internal static RichParagraphStyle ToStructured(RichTextParagraphFormat value) => new()
    {
        Alignment = value.Alignment switch { RichTextAlignment.Center => RichAlignment.Center, RichTextAlignment.Right => RichAlignment.Right,
            RichTextAlignment.Justified => RichAlignment.Justify, _ => RichAlignment.Left },
        Direction = Direction(value.Direction), LeadingIndent = RichLength.FromPoints(value.LeadingIndent), TrailingIndent = RichLength.FromPoints(value.TrailingIndent),
        FirstLineIndent = RichLength.FromPoints(value.FirstLineIndent), SpaceBefore = RichLength.FromPoints(value.SpaceBefore), SpaceAfter = RichLength.FromPoints(value.SpaceAfter),
        Background = Color(value.BackgroundColor),
    };
    private static RichColor? Color(Color? value) => value is null ? null : new((byte)Math.Round(value.Red * 255), (byte)Math.Round(value.Green * 255), (byte)Math.Round(value.Blue * 255));
    private static RichDirection Direction(RichTextDirection value) => value switch
    { RichTextDirection.LeftToRight => RichDirection.LeftToRight, RichTextDirection.RightToLeft => RichDirection.RightToLeft, _ => RichDirection.Automatic };
}
