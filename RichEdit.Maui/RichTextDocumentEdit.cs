namespace RichEdit.Maui;

/// <summary>
/// Builds one atomic transaction against a <see cref="RichTextDocument"/>.
/// </summary>
/// <remarks>
/// Instances are supplied to
/// <see cref="RichTextDocument.Edit(Action{RichTextDocumentEdit}, RichTextEditOptions)"/>
/// and are valid only
/// for the duration of that callback. Each operation observes earlier operations in
/// the same transaction.
/// </remarks>
public sealed class RichTextDocumentEdit
{
    private readonly List<RichTextChange> _changes = [];

    internal RichTextDocumentEdit(RichTextDocumentSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    internal RichTextDocumentSnapshot Snapshot { get; private set; }

    internal IReadOnlyList<RichTextChange> Changes => _changes;

    /// <summary>
    /// Inserts plain text at a UTF-16 document offset.
    /// </summary>
    /// <param name="position">The insertion offset.</param>
    /// <param name="text">The text to insert. Null is treated as an empty string.</param>
    /// <param name="format">
    /// An optional character format for the inserted text; the caret format is used
    /// when omitted.
    /// </param>
    public void InsertText(
        int position,
        string? text,
        RichTextCharacterFormat? format = null) =>
        ReplaceText(new RichTextRange(position, 0), text, format);

    /// <summary>
    /// Deletes a range of logical text and any inline objects it contains.
    /// </summary>
    /// <param name="range">The range to delete.</param>
    public void DeleteText(RichTextRange range) => ReplaceText(range, string.Empty);

    /// <summary>
    /// Replaces a range of logical text.
    /// </summary>
    /// <param name="range">The range to replace.</param>
    /// <param name="text">The replacement text. Null is treated as an empty string.</param>
    /// <param name="format">
    /// An optional character format for replacement text; the caret format is used
    /// when omitted.
    /// </param>
    public void ReplaceText(
        RichTextRange range,
        string? text,
        RichTextCharacterFormat? format = null)
    {
        range.Validate(Snapshot.Text.Length, nameof(range));
        var before = Snapshot;
        Snapshot = Snapshot.Replace(range.ToRange(), text, format);
        _changes.AddRange(RichTextDocument.CreateDelta(before, Snapshot));
    }

    /// <summary>Replaces a range with an immutable rich document fragment.</summary>
    /// <param name="range">The range to replace.</param>
    /// <param name="fragment">The rich fragment to insert.</param>
    public void ReplaceFragment(RichTextRange range, RichTextDocumentFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        range.Validate(Snapshot.Length, nameof(range));
        var source = fragment.Snapshot;
        var insertionStart = range.Start;
        ReplaceText(range, source.Text, source.DefaultCharacterFormat);
        if (source.Length == 0)
        {
            return;
        }

        var pictureIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var picture in source.ListPictures.Values)
        {
            var id = GetAvailablePictureId(picture.Id);
            pictureIds.Add(picture.Id, id);
            SetListPicture(picture with { Id = id });
        }

        var listIds = new Dictionary<RichTextListId, RichTextListId>();
        foreach (var pair in source.Lists.OrderBy(static pair => pair.Key.Value))
        {
            var levels = pair.Value.Levels.Select(level => level.Marker switch
            {
                RichTextListMarker.Picture picture => level with
                {
                    Marker = new RichTextListMarker.Picture(
                        pictureIds.GetValueOrDefault(picture.PictureId, picture.PictureId),
                        picture.FallbackText),
                },
                _ => level,
            });
            listIds.Add(pair.Key, CreateList(new RichTextListDefinition(levels)));
        }

        foreach (var run in source.Runs)
        {
            SetCharacterFormat(
                new RichTextRange(insertionStart + run.Range.Start, run.Range.Length),
                run.Format);
        }

        foreach (var paragraph in source.Paragraphs)
        {
            var format = paragraph.Format;
            if (format.List is { } item && listIds.TryGetValue(item.ListId, out var newListId))
            {
                format = format with
                {
                    List = new RichTextListItemFormat(newListId, item.Level, item.RestartAt),
                };
            }

            SetParagraphFormat(
                new RichTextRange(
                    insertionStart + paragraph.Range.Start,
                    paragraph.Range.Length),
                format);
        }

        foreach (var link in source.Links)
        {
            SetLink(
                new RichTextRange(insertionStart + link.Range.Start, link.Range.Length),
                link.Target,
                link.ToolTip);
        }

        if (!source.Fields.IsDefaultOrEmpty)
        {
            Snapshot = Snapshot.With(fields: Snapshot.Fields.Concat(source.Fields.Select(field =>
                field with
                {
                    Range = new RichTextRange(
                        insertionStart + field.Range.Start,
                        field.Range.Length),
                })));
            _changes.Add(new RichTextRangeChange(
                RichTextChangeKind.Field,
                new RichTextRange(insertionStart, 0),
                new RichTextRange(insertionStart, source.Length)));
        }

        if (!source.Images.IsDefaultOrEmpty)
        {
            Snapshot = Snapshot.With(images: Snapshot.Images.Concat(source.Images.Select(image =>
                image with { Position = insertionStart + image.Position })));
            _changes.Add(new RichTextRangeChange(
                RichTextChangeKind.Image,
                new RichTextRange(insertionStart, 0),
                new RichTextRange(insertionStart, source.Length)));
        }
    }

    /// <summary>
    /// Replaces all selected character-format properties with one format.
    /// </summary>
    /// <param name="range">The nonempty text range to format.</param>
    /// <param name="format">The format to apply.</param>
    public void SetCharacterFormat(RichTextRange range, RichTextCharacterFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        UpdateCharacterFormat(range, _ => format);
    }

    /// <summary>
    /// Updates character formats in a range while preserving run boundaries.
    /// </summary>
    /// <param name="range">The nonempty text range to format.</param>
    /// <param name="update">A transformation applied to every intersecting run.</param>
    public void UpdateCharacterFormat(
        RichTextRange range,
        Func<RichTextCharacterFormat, RichTextCharacterFormat> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        range.Validate(Snapshot.Text.Length, nameof(range));
        if (range.IsEmpty)
        {
            return;
        }

        Snapshot = Snapshot.ApplyCharacterFormat(range.ToRange(), update);
        _changes.Add(new RichTextRangeChange(
            RichTextChangeKind.CharacterFormat,
            range,
            range));
    }

    /// <summary>
    /// Resets character formatting in a range to the document default format.
    /// </summary>
    /// <param name="range">The nonempty text range to reset.</param>
    public void ClearCharacterFormat(RichTextRange range) =>
        SetCharacterFormat(
            range,
            RichTextDocumentSnapshot.CreateInheritedCharacterFormat(
                Snapshot.DefaultCharacterFormat));

    /// <summary>
    /// Replaces the paragraph format of every paragraph intersecting a range.
    /// </summary>
    /// <param name="range">The range whose paragraphs are affected.</param>
    /// <param name="format">The format to apply.</param>
    public void SetParagraphFormat(RichTextRange range, RichTextParagraphFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        UpdateParagraphFormat(range, _ => format);
    }

    /// <summary>
    /// Updates the format of every paragraph intersecting a range.
    /// </summary>
    /// <param name="range">The range whose paragraphs are affected.</param>
    /// <param name="update">A transformation applied to each paragraph format.</param>
    public void UpdateParagraphFormat(
        RichTextRange range,
        Func<RichTextParagraphFormat, RichTextParagraphFormat> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        range.Validate(Snapshot.Text.Length, nameof(range));
        Snapshot = Snapshot.ApplyParagraphFormat(range.ToRange(), update);
        _changes.Add(new RichTextRangeChange(
            RichTextChangeKind.ParagraphFormat,
            range,
            range));
    }

    /// <summary>
    /// Resets affected paragraphs to the document default paragraph format.
    /// </summary>
    /// <param name="range">The range whose paragraphs are reset.</param>
    public void ClearParagraphFormat(RichTextRange range) =>
        SetParagraphFormat(range, Snapshot.DefaultParagraphFormat);

    /// <summary>
    /// Changes the declared default character format.
    /// </summary>
    /// <param name="format">The new document default.</param>
    public void SetDefaultCharacterFormat(RichTextCharacterFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        Snapshot = Snapshot.With(defaultCharacterFormat: format);
        _changes.Add(WholeDocumentChange(RichTextChangeKind.DefaultFormat));
    }

    /// <summary>
    /// Changes the declared default paragraph format.
    /// </summary>
    /// <param name="format">The new document default.</param>
    public void SetDefaultParagraphFormat(RichTextParagraphFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        Snapshot = Snapshot.With(defaultParagraphFormat: format);
        _changes.Add(WholeDocumentChange(RichTextChangeKind.DefaultFormat));
    }

    /// <summary>
    /// Adds a reusable list definition and returns its document-local identity.
    /// </summary>
    /// <param name="definition">The complete caller-defined list levels.</param>
    /// <returns>The newly allocated positive list identifier.</returns>
    public RichTextListId CreateList(RichTextListDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var nextValue = Snapshot.Lists.Keys
            .Select(static id => id.Value)
            .DefaultIfEmpty()
            .Max();
        var id = new RichTextListId(checked(nextValue + 1));
        Snapshot = Snapshot.With(lists: Snapshot.Lists.Add(id, definition));
        _changes.Add(new RichTextRangeChange(
            RichTextChangeKind.List,
            RichTextRange.Empty,
            RichTextRange.Empty));
        return id;
    }

    /// <summary>Replaces an existing reusable list definition.</summary>
    /// <param name="listId">The document-local list identifier.</param>
    /// <param name="definition">The replacement list levels.</param>
    public void UpdateList(RichTextListId listId, RichTextListDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!Snapshot.Lists.ContainsKey(listId))
        {
            throw new ArgumentException("The list does not exist in this document.", nameof(listId));
        }

        Snapshot = Snapshot.With(lists: Snapshot.Lists.SetItem(listId, definition));
        _changes.Add(CreateListDefinitionChange([listId]));
    }

    /// <summary>Applies an existing list definition to intersecting paragraphs.</summary>
    /// <param name="range">The range whose paragraphs become list items.</param>
    /// <param name="listId">The existing document-local list identifier.</param>
    /// <param name="level">The zero-based definition level.</param>
    public void ApplyList(RichTextRange range, RichTextListId listId, int level = 0)
    {
        range.Validate(Snapshot.Length, nameof(range));
        if (!Snapshot.Lists.TryGetValue(listId, out var definition))
        {
            throw new ArgumentException("The list does not exist in this document.", nameof(listId));
        }

        if ((uint)level >= (uint)definition.Levels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        var levelDefinition = definition.Levels[level];
        Snapshot = Snapshot.ApplyParagraphFormat(range.ToRange(), format => format with
        {
            LeadingIndent = levelDefinition.LeadingIndent,
            FirstLineIndent = levelDefinition.FirstLineIndent,
            TabStops = levelDefinition.MarkerTab > 0
                ? [new RichTextTabStop(levelDefinition.MarkerTab)]
                : format.TabStops,
            List = new RichTextListItemFormat(listId, level),
        });
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.List, range, range));
    }

    /// <summary>Removes list-item state from intersecting paragraphs.</summary>
    /// <param name="range">The range whose paragraphs leave their lists.</param>
    public void RemoveList(RichTextRange range)
    {
        range.Validate(Snapshot.Length, nameof(range));
        Snapshot = Snapshot.ApplyParagraphFormat(
            range.ToRange(),
            static format => format with { List = null, NativeList = null });
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.List, range, range));
    }

    /// <summary>Changes list nesting for intersecting list paragraphs.</summary>
    /// <param name="range">The range whose list items are changed.</param>
    /// <param name="delta">The signed level adjustment.</param>
    public void ChangeListLevel(RichTextRange range, int delta)
    {
        range.Validate(Snapshot.Length, nameof(range));
        if (delta == 0)
        {
            return;
        }

        Snapshot = Snapshot.ApplyParagraphFormat(range.ToRange(), format =>
        {
            if (format.List is not { } item ||
                !Snapshot.Lists.TryGetValue(item.ListId, out var definition))
            {
                return format;
            }

            var level = Math.Clamp(item.Level + delta, 0, definition.Levels.Length - 1);
            var levelDefinition = definition.Levels[level];
            return format with
            {
                LeadingIndent = levelDefinition.LeadingIndent,
                FirstLineIndent = levelDefinition.FirstLineIndent,
                TabStops = levelDefinition.MarkerTab > 0
                    ? [new RichTextTabStop(levelDefinition.MarkerTab)]
                    : format.TabStops,
                List = new RichTextListItemFormat(item.ListId, level, item.RestartAt),
            };
        });
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.List, range, range));
    }

    /// <summary>Restarts intersecting numbered-list items at a positive value.</summary>
    /// <param name="range">The range whose list items are restarted.</param>
    /// <param name="startAt">The positive restart value.</param>
    public void RestartList(RichTextRange range, int startAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startAt);
        range.Validate(Snapshot.Length, nameof(range));
        Snapshot = Snapshot.ApplyParagraphFormat(range.ToRange(), format =>
        {
            if (format.List is not { } item ||
                !Snapshot.Lists.TryGetValue(item.ListId, out var definition) ||
                definition.Levels[item.Level].Marker is not RichTextListMarker.Number)
            {
                return format;
            }

            return format with
            {
                List = new RichTextListItemFormat(item.ListId, item.Level, startAt),
            };
        });
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.List, range, range));
    }

    /// <summary>Adds or replaces an owned list-marker picture.</summary>
    /// <param name="picture">The picture value and unique document-local identifier.</param>
    public void SetListPicture(RichTextListPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var pictures = Snapshot.ListPictures.SetItem(picture.Id, picture);
        Snapshot = Snapshot.With(listPictures: pictures.Values);
        var affectedLists = Snapshot.Lists
            .Where(pair => pair.Value.Levels.Any(level =>
                level.Marker is RichTextListMarker.Picture marker &&
                string.Equals(marker.PictureId, picture.Id, StringComparison.Ordinal)))
            .Select(static pair => pair.Key);
        _changes.Add(CreateListDefinitionChange(affectedLists));
    }

    /// <summary>Removes an unreferenced owned list-marker picture.</summary>
    /// <param name="pictureId">The ordinal picture identifier.</param>
    public void RemoveListPicture(string pictureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pictureId);
        if (Snapshot.Lists.Values
            .SelectMany(static definition => definition.Levels)
            .Any(level => level.Marker is RichTextListMarker.Picture picture &&
                string.Equals(picture.PictureId, pictureId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("A list definition still references this picture.");
        }

        Snapshot = Snapshot.With(
            listPictures: Snapshot.ListPictures.Remove(pictureId).Values);
        _changes.Add(new RichTextRangeChange(
            RichTextChangeKind.List,
            RichTextRange.Empty,
            RichTextRange.Empty));
    }

    /// <summary>
    /// Creates or replaces a hyperlink over a nonempty range.
    /// </summary>
    /// <param name="range">The hyperlink display-text range.</param>
    /// <param name="target">The application-defined link target.</param>
    /// <param name="toolTip">An optional RTF hyperlink tooltip.</param>
    public void SetLink(RichTextRange range, string target, string? toolTip = null)
    {
        range.Validate(Snapshot.Text.Length, nameof(range));
        Snapshot = Snapshot.SetLink(range.ToRange(), target, toolTip);
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Link, range, range));
    }

    /// <summary>
    /// Removes every hyperlink intersecting a range.
    /// </summary>
    /// <param name="range">The range from which links are removed.</param>
    public void RemoveLinks(RichTextRange range)
    {
        range.Validate(Snapshot.Text.Length, nameof(range));
        Snapshot = Snapshot.RemoveLinks(range.ToRange());
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Link, range, range));
    }

    /// <summary>
    /// Inserts an inline image and its U+FFFC logical placeholder.
    /// </summary>
    /// <param name="position">The insertion offset.</param>
    /// <param name="image">The owned image payload and presentation metadata.</param>
    public void InsertImage(int position, RichTextImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var range = new RichTextRange(position, 0);
        range.Validate(Snapshot.Text.Length, nameof(position));
        Snapshot = Snapshot.InsertImage(position, image);
        _changes.Add(new RichTextTextChange(
            range,
            RichTextDocument.ObjectReplacementCharacter.ToString()));
        _changes.Add(new RichTextRangeChange(
            RichTextChangeKind.Image,
            range,
            new RichTextRange(position, 1)));
    }

    /// <summary>
    /// Updates an existing inline image without changing its logical position.
    /// </summary>
    /// <param name="position">The image's U+FFFC document position.</param>
    /// <param name="image">The replacement image value.</param>
    public void UpdateImage(int position, RichTextImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Snapshot.Images.Any(candidate => candidate.Position == position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), "No image exists at this position.");
        }

        Snapshot = Snapshot.With(images: Snapshot.Images.Select(candidate =>
            candidate.Position == position ? image with { Position = position } : candidate));
        var range = new RichTextRange(position, 1);
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Image, range, range));
    }

    /// <summary>
    /// Removes an inline image and its U+FFFC logical placeholder.
    /// </summary>
    /// <param name="position">The image's U+FFFC document position.</param>
    public void RemoveImage(int position)
    {
        if (!Snapshot.Images.Any(candidate => candidate.Position == position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), "No image exists at this position.");
        }

        DeleteText(new RichTextRange(position, 1));
    }

    /// <summary>
    /// Inserts a field result and associates it with an RTF field instruction.
    /// </summary>
    /// <param name="position">The result insertion offset.</param>
    /// <param name="instruction">The RTF field instruction.</param>
    /// <param name="result">The visible field result.</param>
    /// <param name="format">An optional character format for the result.</param>
    public void InsertField(
        int position,
        string instruction,
        string? result,
        RichTextCharacterFormat? format = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        result ??= string.Empty;
        var beforeLength = Snapshot.Length;
        InsertText(position, result, format);
        var insertedLength = Snapshot.Length - beforeLength;
        var field = new RichTextField(position, insertedLength, instruction);
        Snapshot = Snapshot.With(fields: Snapshot.Fields.Append(field));
        _changes.Add(new RichTextRangeChange(
            RichTextChangeKind.Field,
            new RichTextRange(position, 0),
            new RichTextRange(position, insertedLength)));
    }

    /// <summary>
    /// Updates one field instruction and replaces its visible result atomically.
    /// </summary>
    /// <param name="range">The exact current result range of one field.</param>
    /// <param name="instruction">The replacement RTF field instruction.</param>
    /// <param name="result">The replacement visible result. Null is treated as empty.</param>
    /// <param name="format">An optional character format for the replacement result.</param>
    public void UpdateField(
        RichTextRange range,
        string instruction,
        string? result,
        RichTextCharacterFormat? format = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        range.Validate(Snapshot.Length, nameof(range));
        var matches = Snapshot.Fields.Where(field => field.Range == range).ToArray();
        if (matches.Length != 1)
        {
            throw new ArgumentException(
                "The range must identify exactly one existing field.",
                nameof(range));
        }

        result = NormalizeText(result);
        var original = matches[0];
        if (string.Equals(
            Snapshot.Text.Substring(range.Start, range.Length),
            result,
            StringComparison.Ordinal))
        {
            if (format is not null)
            {
                SetCharacterFormat(range, format);
            }

            if (!string.Equals(original.Instruction, instruction, StringComparison.Ordinal))
            {
                Snapshot = Snapshot.With(fields: Snapshot.Fields.Select(field =>
                    field.Range == range
                        ? field with { Instruction = instruction }
                        : field));
                _changes.Add(new RichTextRangeChange(RichTextChangeKind.Field, range, range));
            }

            return;
        }

        var oldDocumentLength = Snapshot.Length;
        ReplaceText(range, result, format);
        var insertedLength = Snapshot.Length - (oldDocumentLength - range.Length);
        var newRange = new RichTextRange(range.Start, insertedLength);
        Snapshot = Snapshot.With(fields: Snapshot.Fields.Append(
            new RichTextField(newRange, instruction)));
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Field, range, newRange));
    }

    private static string NormalizeText(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    /// <summary>
    /// Removes fields intersecting a range while retaining their visible result text.
    /// </summary>
    /// <param name="range">The field range to clear.</param>
    public void RemoveFields(RichTextRange range)
    {
        range.Validate(Snapshot.Text.Length, nameof(range));
        Snapshot = Snapshot.With(fields: Snapshot.Fields.Where(field =>
            field.End <= range.Start || field.Start >= range.End));
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Field, range, range));
    }

    /// <summary>
    /// Sets or removes an application metadata value.
    /// </summary>
    /// <param name="name">The ordinal metadata key.</param>
    /// <param name="value">The value to set, or null to remove the key.</param>
    public void SetMetadata(string name, string? value)
    {
        Snapshot = Snapshot.SetMetadata(name, value);
        _changes.Add(WholeDocumentChange(RichTextChangeKind.Metadata));
    }

    private RichTextRangeChange WholeDocumentChange(RichTextChangeKind kind)
    {
        var range = new RichTextRange(0, Snapshot.Text.Length);
        return new RichTextRangeChange(kind, range, range);
    }

    private RichTextRangeChange CreateListDefinitionChange(
        IEnumerable<RichTextListId> listIds)
    {
        var ids = listIds.ToHashSet();
        var affectedParagraphs = Snapshot.Paragraphs
            .Where(paragraph => paragraph.Format.List is { } item && ids.Contains(item.ListId))
            .ToArray();
        var range = affectedParagraphs.Length == 0
            ? RichTextRange.Empty
            : new RichTextRange(
                affectedParagraphs[0].Range.Start,
                affectedParagraphs[^1].Range.End - affectedParagraphs[0].Range.Start);
        return new RichTextRangeChange(RichTextChangeKind.List, range, range);
    }

    private string GetAvailablePictureId(string requestedId)
    {
        if (!Snapshot.ListPictures.ContainsKey(requestedId))
        {
            return requestedId;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = string.Concat(requestedId, "-", suffix.ToString());
            if (!Snapshot.ListPictures.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }
}
