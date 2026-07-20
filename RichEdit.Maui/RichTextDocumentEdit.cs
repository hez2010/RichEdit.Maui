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

        if (!source.Fields.IsDefaultOrEmpty)
        {
            var nextFieldId = GetNextFieldId();
            var importedFields = source.Fields.Select(field => field with
            {
                Id = new RichTextFieldId(nextFieldId++),
                Range = new RichTextRange(
                    insertionStart + field.Range.Start,
                    field.Range.Length),
            });
            Snapshot = Snapshot.With(fields: Snapshot.Fields.Concat(importedFields));
            _changes.Add(new RichTextRangeChange(
                RichTextChangeKind.Field,
                new RichTextRange(insertionStart, 0),
                new RichTextRange(insertionStart, source.Length)));
        }

        if (source.Length == 0)
        {
            return;
        }

        var usedListIds = source.Paragraphs
            .Select(static paragraph => paragraph.Format.List?.ListId)
            .OfType<RichTextListId>()
            .ToHashSet();
        var usedPictureIds = source.Lists
            .Where(pair => usedListIds.Contains(pair.Key))
            .SelectMany(static pair => pair.Value.Levels)
            .Select(static level => (level.Marker as RichTextListMarker.Picture)?.PictureId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var pictureIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var picture in source.ListPictures.Values.Where(picture =>
                     usedPictureIds.Contains(picture.Id)))
        {
            var id = GetAvailablePictureId(picture.Id);
            pictureIds.Add(picture.Id, id);
            SetListPicture(picture with { Id = id });
        }

        var listIds = new Dictionary<RichTextListId, RichTextListId>();
        foreach (var pair in source.Lists
                     .Where(pair => usedListIds.Contains(pair.Key))
                     .OrderBy(static pair => pair.Key.Value))
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

    /// <summary>
    /// Removes list-item state and list-owned indentation from intersecting paragraphs.
    /// </summary>
    /// <param name="range">The range whose paragraphs leave their lists.</param>
    public void RemoveList(RichTextRange range)
    {
        range.Validate(Snapshot.Length, nameof(range));
        var defaultFormat = Snapshot.DefaultParagraphFormat;
        Snapshot = Snapshot.ApplyParagraphFormat(
            range.ToRange(),
            format => RemoveListFormat(
                format,
                defaultFormat,
                Snapshot.Lists));
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.List, range, range));
    }

    /// <summary>
    /// Changes list nesting for intersecting list paragraphs. Outdenting above the
    /// first level removes list state. Indenting beyond the defined levels extends
    /// the document-local definition by repeating its final marker style and layout
    /// progression, up to the RTF limit of nine levels.
    /// </summary>
    /// <param name="range">The range whose list items are changed.</param>
    /// <param name="delta">The signed level adjustment.</param>
    public void ChangeListLevel(RichTextRange range, int delta)
    {
        range.Validate(Snapshot.Length, nameof(range));
        if (delta == 0)
        {
            return;
        }

        var affectedParagraphs = GetAffectedParagraphs(range);
        var lists = Snapshot.Lists;
        foreach (var group in affectedParagraphs
            .Select(static paragraph => paragraph.Format.List)
            .OfType<RichTextListItemFormat>()
            .GroupBy(static item => item.ListId))
        {
            if (!lists.TryGetValue(group.Key, out var definition))
            {
                continue;
            }

            var requiredLevel = group
                .Select(item => Math.Clamp((long)item.Level + delta, 0, 8))
                .Max();
            if (requiredLevel >= definition.Levels.Length)
            {
                lists = lists.SetItem(
                    group.Key,
                    ExtendListDefinition(definition, checked((int)requiredLevel + 1)));
            }
        }

        if (!ReferenceEquals(lists, Snapshot.Lists))
        {
            Snapshot = Snapshot.With(lists: lists);
        }

        var defaultFormat = Snapshot.DefaultParagraphFormat;
        Snapshot = Snapshot.ApplyParagraphFormat(range.ToRange(), format =>
        {
            if (format.List is not { } item ||
                !Snapshot.Lists.TryGetValue(item.ListId, out var definition))
            {
                return format;
            }

            var requestedLevel = (long)item.Level + delta;
            if (requestedLevel < 0)
            {
                return RemoveListFormat(format, defaultFormat, Snapshot.Lists);
            }

            var level = (int)Math.Min(requestedLevel, definition.Levels.Length - 1L);
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

    private IReadOnlyList<RichTextParagraph> GetAffectedParagraphs(RichTextRange range)
    {
        var lastPosition = range.IsEmpty ? range.Start : range.End - 1;
        var firstParagraph = range.Start == 0
            ? 0
            : Snapshot.Text.LastIndexOf('\n', range.Start - 1) + 1;
        var lastParagraph = lastPosition == 0
            ? 0
            : Snapshot.Text.LastIndexOf('\n', lastPosition - 1) + 1;
        return Snapshot.Paragraphs
            .Where(paragraph =>
                paragraph.Start >= firstParagraph && paragraph.Start <= lastParagraph)
            .ToArray();
    }

    private static RichTextListDefinition ExtendListDefinition(
        RichTextListDefinition definition,
        int levelCount)
    {
        var levels = definition.Levels.ToList();
        while (levels.Count < levelCount)
        {
            var previous = levels[^1];
            var step = GetListIndentStep(levels);
            levels.Add(previous with
            {
                LeadingIndent = AddRtfIndent(previous.LeadingIndent, step),
                MarkerTab = previous.MarkerTab > 0
                    ? AddRtfIndent(previous.MarkerTab, step)
                    : 0,
            });
        }

        return new RichTextListDefinition(levels);
    }

    private static double GetListIndentStep(
        IReadOnlyList<RichTextListLevelDefinition> levels)
    {
        var last = levels[^1];
        if (levels.Count > 1)
        {
            var preceding = levels[^2];
            var progression = last.LeadingIndent - preceding.LeadingIndent;
            if (progression > 0)
            {
                return progression;
            }
        }

        if (Math.Abs(last.FirstLineIndent) > 0)
        {
            return Math.Abs(last.FirstLineIndent);
        }

        if (last.LeadingIndent > 0)
        {
            return last.LeadingIndent;
        }

        return last.MarkerTab;
    }

    private static RichTextParagraphFormat RemoveListFormat(
        RichTextParagraphFormat format,
        RichTextParagraphFormat defaultFormat,
        IReadOnlyDictionary<RichTextListId, RichTextListDefinition> lists)
    {
        if (format.List is not { } item)
        {
            return format;
        }

        var resetTabStops = lists.TryGetValue(item.ListId, out var definition) &&
            (uint)item.Level < (uint)definition.Levels.Length &&
            definition.Levels[item.Level].MarkerTab > 0;
        return format with
        {
            LeadingIndent = defaultFormat.LeadingIndent,
            FirstLineIndent = defaultFormat.FirstLineIndent,
            TabStops = resetTabStops ? defaultFormat.TabStops : format.TabStops,
            List = null,
            NativeList = null,
        };
    }

    private static double AddRtfIndent(double value, double increment)
    {
        var result = value + increment;
        return double.IsFinite(result) && result * 20d is >= int.MinValue and <= int.MaxValue
            ? result
            : value;
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
        var affected = GetAffectedLinkRange(range);
        Snapshot = Snapshot.SetLink(range.ToRange(), target, toolTip);
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Link, affected, affected));
    }

    /// <summary>
    /// Removes every hyperlink intersecting a range.
    /// </summary>
    /// <param name="range">The range from which links are removed.</param>
    public void RemoveLinks(RichTextRange range)
    {
        range.Validate(Snapshot.Text.Length, nameof(range));
        var affected = GetAffectedLinkRange(range);
        Snapshot = Snapshot.RemoveLinks(range.ToRange());
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Link, affected, affected));
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
    /// <returns>The stable document-local identity allocated for the field.</returns>
    public RichTextFieldId InsertField(
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
        var id = new RichTextFieldId(GetNextFieldId());
        var field = new RichTextField(
            id,
            new RichTextRange(position, insertedLength),
            instruction);
        Snapshot = Snapshot.With(fields: Snapshot.Fields.Append(field));
        _changes.Add(new RichTextRangeChange(
            RichTextChangeKind.Field,
            new RichTextRange(position, 0),
            new RichTextRange(position, insertedLength)));
        return id;
    }

    /// <summary>
    /// Updates one identified field and replaces its visible result atomically.
    /// </summary>
    /// <param name="fieldId">The stable document-local field identity.</param>
    /// <param name="instruction">The replacement RTF field instruction.</param>
    /// <param name="result">The replacement visible result. Null is treated as empty.</param>
    /// <param name="format">An optional character format for the replacement result.</param>
    public void UpdateField(
        RichTextFieldId fieldId,
        string instruction,
        string? result,
        RichTextCharacterFormat? format = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        var original = Snapshot.Fields.FirstOrDefault(field => field.Id == fieldId) ??
            throw new ArgumentException("The field does not exist in this document.", nameof(fieldId));
        UpdateFieldCore(original, instruction, result, format);
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

        UpdateFieldCore(matches[0], instruction, result, format);
    }

    private void UpdateFieldCore(
        RichTextField original,
        string instruction,
        string? result,
        RichTextCharacterFormat? format)
    {
        result = NormalizeText(result);
        var range = original.Range;
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
                    field.Id == original.Id
                        ? field with { Instruction = instruction }
                        : field));
                _changes.Add(new RichTextRangeChange(RichTextChangeKind.Field, range, range));
            }

            return;
        }

        var oldDocumentLength = Snapshot.Length;
        Snapshot = Snapshot.With(fields: Snapshot.Fields.Where(field => field.Id != original.Id));
        ReplaceText(range, result, format);
        var insertedLength = Snapshot.Length - (oldDocumentLength - range.Length);
        var newRange = new RichTextRange(range.Start, insertedLength);
        Snapshot = Snapshot.With(fields: Snapshot.Fields.Append(
            original with { Range = newRange, Instruction = instruction }));
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Field, range, newRange));
    }

    /// <summary>Removes one field while retaining its visible result text.</summary>
    /// <param name="fieldId">The stable document-local field identity.</param>
    public void RemoveField(RichTextFieldId fieldId)
    {
        var field = Snapshot.Fields.FirstOrDefault(candidate => candidate.Id == fieldId) ??
            throw new ArgumentException("The field does not exist in this document.", nameof(fieldId));
        Snapshot = Snapshot.With(fields: Snapshot.Fields.Where(candidate => candidate.Id != fieldId));
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Field, field.Range, field.Range));
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
        var affected = GetAffectedFieldRange(range);
        Snapshot = Snapshot.With(fields: Snapshot.Fields.Where(field =>
            !FieldIntersectsRange(field, range)));
        _changes.Add(new RichTextRangeChange(RichTextChangeKind.Field, affected, affected));
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

    private RichTextRange GetAffectedLinkRange(RichTextRange range)
    {
        var intersecting = Snapshot.Links.Where(link =>
            range.IsEmpty
                ? link.Start < range.Start && link.End > range.Start
                : link.End > range.Start && link.Start < range.End).ToArray();
        if (intersecting.Length == 0)
        {
            return range;
        }

        var start = Math.Min(range.Start, intersecting[0].Start);
        var end = Math.Max(range.End, intersecting[^1].End);
        return new RichTextRange(start, end - start);
    }

    private RichTextRange GetAffectedFieldRange(RichTextRange range)
    {
        var intersecting = Snapshot.Fields.Where(field =>
            FieldIntersectsRange(field, range)).ToArray();
        if (intersecting.Length == 0)
        {
            return range;
        }

        var start = Math.Min(range.Start, intersecting[0].Start);
        var end = Math.Max(range.End, intersecting[^1].End);
        return new RichTextRange(start, end - start);
    }

    private int GetNextFieldId() => checked(Snapshot.Fields
        .Select(static field => field.Id.Value)
        .DefaultIfEmpty()
        .Max() + 1);

    private static bool FieldIntersectsRange(RichTextField field, RichTextRange range)
    {
        if (field.Range.IsEmpty)
        {
            return range.IsEmpty
                ? field.Start == range.Start
                : field.Start >= range.Start && field.Start < range.End;
        }

        return range.IsEmpty
            ? field.Start < range.Start && field.End > range.Start
            : field.End > range.Start && field.Start < range.End;
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
