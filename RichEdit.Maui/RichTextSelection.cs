using System.ComponentModel;

namespace RichEdit.Maui;

/// <summary>
/// Represents one editor's current selection, typing formats, and selection commands.
/// </summary>
public sealed class RichTextSelection : INotifyPropertyChanged
{
    private readonly RichEditor _editor;
    private RichTextCharacterFormat[]? _cachedCharacterFormats;
    private RichTextParagraphFormat[]? _cachedParagraphFormats;
    private RichTextRange _cachedFormatRange;
    private long _cachedFormatVersion = -1;
    private RichTextCharacterFormat? _cachedTypingCharacterFormat;

    internal RichTextSelection(RichEditor editor)
    {
        _editor = editor;
        CharacterFormat = new RichTextSelectionCharacterFormat(this);
        ParagraphFormat = new RichTextSelectionParagraphFormat(this);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the current UTF-16 document range.</summary>
    public RichTextRange Range => _editor.SelectedRange;

    /// <summary>Gets the selected logical plain text.</summary>
    public string Text => _editor.Document.GetText(Range);

    /// <summary>Gets the live bindable character-format facade.</summary>
    public RichTextSelectionCharacterFormat CharacterFormat { get; }

    /// <summary>Gets the live bindable paragraph-format facade.</summary>
    public RichTextSelectionParagraphFormat ParagraphFormat { get; }

    /// <summary>Gets the declared character format used by newly typed text.</summary>
    public RichTextCharacterFormat TypingCharacterFormat => _editor.TypingCharacterFormat;

    /// <summary>Gets the paragraph format used by newly typed text.</summary>
    public RichTextParagraphFormat TypingParagraphFormat => _editor.TypingParagraphFormat;

    internal RichEditor Editor => _editor;

    /// <summary>Moves the selection to a validated document range.</summary>
    /// <param name="range">The new range.</param>
    public void Select(RichTextRange range) => _editor.SelectedRange = range;

    /// <summary>Replaces selected content and collapses the caret after the insertion.</summary>
    /// <param name="text">The replacement text. Null is treated as empty.</param>
    public void ReplaceText(string? text)
    {
        if (_editor.IsReadOnly)
        {
            return;
        }

        text ??= string.Empty;
        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var start = Range.Start;
        _editor.EditDocument(
            edit => edit.ReplaceText(Range, normalized, TypingCharacterFormat),
            new RichTextRange(start + normalized.Length, 0));
    }

    /// <summary>Replaces selected content with a portable rich fragment.</summary>
    /// <param name="fragment">The fragment to insert.</param>
    public void ReplaceFragment(RichTextDocumentFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (_editor.IsReadOnly)
        {
            return;
        }

        var start = Range.Start;
        _editor.EditDocument(
            edit => edit.ReplaceFragment(Range, fragment),
            new RichTextRange(start + fragment.Text.Length, 0));
    }

    /// <summary>Updates character formatting across the selection.</summary>
    /// <param name="update">The transformation applied to each selected run.</param>
    public void UpdateCharacterFormat(
        Func<RichTextCharacterFormat, RichTextCharacterFormat> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (_editor.IsReadOnly)
        {
            return;
        }

        if (Range.IsEmpty)
        {
            _editor.SetTypingCharacterFormat(
                update(TypingCharacterFormat) ??
                throw new InvalidOperationException("A format update cannot return null."));
            return;
        }

        _editor.Document.Edit(edit => edit.UpdateCharacterFormat(Range, update));
    }

    /// <summary>Updates paragraph formatting across every intersecting paragraph.</summary>
    /// <param name="update">The transformation applied to each paragraph format.</param>
    public void UpdateParagraphFormat(
        Func<RichTextParagraphFormat, RichTextParagraphFormat> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (_editor.IsReadOnly)
        {
            return;
        }

        _editor.Document.Edit(edit => edit.UpdateParagraphFormat(Range, update));
    }

    /// <summary>Toggles bold formatting across the selection.</summary>
    public void ToggleBold()
    {
        var enable = !AllCharacterFormats(static format => format.Bold);
        UpdateCharacterFormat(format => format with
        {
            FontWeight = enable ? Math.Max(format.FontWeight, 700) : 400,
        });
    }

    /// <summary>Toggles italic formatting across the selection.</summary>
    public void ToggleItalic()
    {
        var enable = !AllCharacterFormats(static format => format.Italic);
        UpdateCharacterFormat(format => format with { Italic = enable });
    }

    /// <summary>Toggles a caller-selected underline style.</summary>
    /// <param name="style">The style to apply when enabling underline.</param>
    public void ToggleUnderline(RichTextUnderlineStyle style)
    {
        if (style == RichTextUnderlineStyle.None)
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }

        var enable = !AllCharacterFormats(format => format.Underline == style);
        UpdateCharacterFormat(format => format with
        {
            Underline = enable ? style : RichTextUnderlineStyle.None,
        });
    }

    /// <summary>Toggles a caller-selected strikethrough style.</summary>
    /// <param name="style">The style to apply when enabling strikethrough.</param>
    public void ToggleStrikethrough(RichTextStrikethroughStyle style)
    {
        if (style == RichTextStrikethroughStyle.None)
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }

        var enable = !AllCharacterFormats(format => format.Strikethrough == style);
        UpdateCharacterFormat(format => format with
        {
            Strikethrough = enable ? style : RichTextStrikethroughStyle.None,
        });
    }

    /// <summary>Toggles a caller-selected superscript or subscript state.</summary>
    /// <param name="script">The script state to toggle.</param>
    public void ToggleScript(RichTextScript script)
    {
        if (script == RichTextScript.Normal)
        {
            throw new ArgumentOutOfRangeException(nameof(script));
        }

        var enable = !AllCharacterFormats(format => format.Script == script);
        UpdateCharacterFormat(format => format with
        {
            Script = enable ? script : RichTextScript.Normal,
        });
    }

    /// <summary>Toggles an explicitly defined list at a nesting level.</summary>
    /// <param name="definition">The complete caller-defined list.</param>
    /// <param name="level">The zero-based level to apply.</param>
    public void ToggleList(RichTextListDefinition definition, int level = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if ((uint)level >= (uint)definition.Levels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        var snapshot = _editor.Document.CurrentSnapshot;
        var remove = AllParagraphFormats(format =>
            format.List is { } item &&
            item.Level == level &&
            snapshot.Lists.TryGetValue(item.ListId, out var existing) &&
            existing == definition);
        if (remove)
        {
            ClearList();
        }
        else
        {
            SetList(definition, level);
        }
    }

    /// <summary>Creates and applies an explicitly defined list.</summary>
    /// <param name="definition">The complete caller-defined list.</param>
    /// <param name="level">The zero-based level to apply.</param>
    public void SetList(RichTextListDefinition definition, int level = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (_editor.IsReadOnly)
        {
            return;
        }

        var existingId = FindEquivalentListId(
            _editor.Document.CurrentSnapshot,
            definition);
        _editor.Document.Edit(edit =>
        {
            var listId = existingId ?? edit.CreateList(definition);
            edit.ApplyList(Range, listId, level);
        });
    }

    internal static RichTextListId? FindEquivalentListId(
        RichTextDocumentSnapshot snapshot,
        RichTextListDefinition definition) =>
        snapshot.Lists
            .Where(pair => pair.Value == definition)
            .Select(static pair => (RichTextListId?)pair.Key)
            .FirstOrDefault();

    /// <summary>Continues an existing document list at a nesting level.</summary>
    /// <param name="listId">The existing document-local list identifier.</param>
    /// <param name="level">The zero-based level to apply.</param>
    public void SetList(RichTextListId listId, int level = 0)
    {
        if (_editor.IsReadOnly)
        {
            return;
        }

        _editor.Document.Edit(edit => edit.ApplyList(Range, listId, level));
    }

    internal void SetList(RichTextListItemFormat item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_editor.IsReadOnly)
        {
            return;
        }

        _editor.Document.Edit(edit =>
        {
            edit.ApplyList(Range, item.ListId, item.Level);
            if (item.RestartAt is { } startAt)
            {
                edit.RestartList(Range, startAt);
            }
        });
    }

    /// <summary>Removes list state from selected paragraphs.</summary>
    public void ClearList()
    {
        if (!_editor.IsReadOnly)
        {
            _editor.Document.Edit(edit => edit.RemoveList(Range));
        }
    }

    /// <summary>Changes selected list levels by a signed delta.</summary>
    /// <param name="delta">The signed nesting-level adjustment.</param>
    public void ChangeListLevel(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        if (!_editor.IsReadOnly)
        {
            _editor.Document.Edit(edit => edit.ChangeListLevel(Range, delta));
        }
    }

    /// <summary>Restarts selected numbered-list items at a positive value.</summary>
    /// <param name="startAt">The new positive counter value.</param>
    public void RestartList(int startAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startAt);
        if (!_editor.IsReadOnly)
        {
            _editor.Document.Edit(edit => edit.RestartList(Range, startAt));
        }
    }

    /// <summary>Creates or replaces a hyperlink over the selection.</summary>
    /// <param name="target">The application-defined target.</param>
    /// <param name="toolTip">An optional RTF tooltip.</param>
    public void SetLink(string target, string? toolTip = null)
    {
        if (_editor.IsReadOnly)
        {
            return;
        }

        _editor.Document.Edit(edit => edit.SetLink(Range, target, toolTip));
    }

    /// <summary>Removes hyperlinks intersecting the selection.</summary>
    public void RemoveLinks()
    {
        if (!_editor.IsReadOnly)
        {
            _editor.Document.Edit(edit => edit.RemoveLinks(Range));
        }
    }

    /// <summary>Replaces selected content with one inline image.</summary>
    /// <param name="image">The owned image value.</param>
    public void InsertImage(RichTextImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (_editor.IsReadOnly)
        {
            return;
        }

        var start = Range.Start;
        _editor.EditDocument(
            edit =>
            {
                edit.DeleteText(Range);
                edit.InsertImage(start, image);
            },
            new RichTextRange(start + 1, 0));
    }

    /// <summary>Replaces selected content with a field result.</summary>
    /// <param name="instruction">The RTF field instruction.</param>
    /// <param name="result">The visible field result.</param>
    public void InsertField(string instruction, string result)
    {
        if (_editor.IsReadOnly)
        {
            return;
        }

        var start = Range.Start;
        var insertedLength = result
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Length;
        _editor.EditDocument(
            edit =>
            {
                edit.DeleteText(Range);
                edit.InsertField(start, instruction, result, TypingCharacterFormat);
            },
            new RichTextRange(start + insertedLength, 0));
    }

    internal IReadOnlyList<RichTextCharacterFormat> GetCharacterFormats()
    {
        EnsureFormatCache();
        if (_cachedCharacterFormats is not null)
        {
            return _cachedCharacterFormats;
        }

        _cachedCharacterFormats = Range.IsEmpty
            ? [TypingCharacterFormat]
            : [.. _editor.Document.GetCharacterFormat(Range).Formats];
        return _cachedCharacterFormats;
    }

    internal IReadOnlyList<RichTextParagraphFormat> GetParagraphFormats()
    {
        EnsureFormatCache();
        return _cachedParagraphFormats ??=
            [.. _editor.Document.GetParagraphFormat(Range).Formats];
    }

    internal void Refresh()
    {
        InvalidateFormatCache();
        OnPropertyChanged(nameof(Range));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(TypingCharacterFormat));
        OnPropertyChanged(nameof(TypingParagraphFormat));
    }

    internal void RefreshFormatting()
    {
        Refresh();
        CharacterFormat.Refresh();
        ParagraphFormat.Refresh();
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool AllCharacterFormats(Func<RichTextCharacterFormat, bool> predicate) =>
        GetCharacterFormats().All(predicate);

    private bool AllParagraphFormats(Func<RichTextParagraphFormat, bool> predicate) =>
        GetParagraphFormats().All(predicate);

    private void EnsureFormatCache()
    {
        var version = _editor.Document.Version;
        var range = Range;
        var typingFormat = TypingCharacterFormat;
        if (_cachedFormatVersion == version &&
            _cachedFormatRange == range &&
            _cachedTypingCharacterFormat == typingFormat)
        {
            return;
        }

        _cachedFormatVersion = version;
        _cachedFormatRange = range;
        _cachedTypingCharacterFormat = typingFormat;
        _cachedCharacterFormats = null;
        _cachedParagraphFormats = null;
    }

    private void InvalidateFormatCache()
    {
        _cachedFormatVersion = -1;
        _cachedCharacterFormats = null;
        _cachedParagraphFormats = null;
    }

}
