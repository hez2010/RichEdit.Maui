using System.Collections.Immutable;
using System.Globalization;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Reader
    {
        private IEnumerable<KeyValuePair<RichTextListId, RichTextListDefinition>> CreateListDefinitions(RichTextParagraph[] paragraphs)
        {
            var tables = _lists.GroupBy(static pair => pair.Key.ListId)
                .ToDictionary(static group => group.Key, static group => group.ToDictionary(static pair => pair.Key.Level, static pair => pair.Value));
            var overrides = _listOverrideLevels.GroupBy(static pair => pair.Key.OverrideId)
                .ToDictionary(static group => group.Key, static group => group.ToDictionary(static pair => pair.Key.Level, static pair => pair.Value));
            var paragraphLevels = paragraphs.Where(static paragraph => paragraph.Format.NativeList is not null)
                .GroupBy(static paragraph => paragraph.Format.NativeList!.Id)
                .ToDictionary(static group => group.Key, static group => group.GroupBy(static paragraph => paragraph.Format.NativeList!.Level)
                    .ToDictionary(static level => level.Key, static level => level.FirstOrDefault(static paragraph => !paragraph.Format.NativeList!.Restart) ?? level.First()));
            foreach (var (identity, modelId) in _modelListIds)
            {
                var listId = identity.IsTableList ? identity.Id : _listOverrides.GetValueOrDefault(identity.Id);
                Dictionary<int, ParsedListDefinition> parsed = (identity.IsTableList || _listOverrides.ContainsKey(identity.Id)) && tables.TryGetValue(listId, out var levelsById)
                    ? new Dictionary<int, ParsedListDefinition>(levelsById) : [];
                if (!identity.IsTableList && overrides.TryGetValue(identity.Id, out var replacements))
                    foreach (var pair in replacements)
                        parsed[pair.Key] = pair.Value;
                if (parsed.Count == 0)
                    continue;

                var used = paragraphLevels.GetValueOrDefault(modelId) ?? [];
                var maximumLevel = Math.Max(parsed.Keys.Max(), used.Keys.DefaultIfEmpty().Max());
                var fallback = parsed.GetValueOrDefault(0, parsed.OrderBy(static pair => pair.Key).First().Value);
                var levels = new RichTextListLevelDefinition[maximumLevel + 1];
                for (var level = 0; level < levels.Length; level++)
                {
                    var definition = parsed.GetValueOrDefault(level, fallback);
                    var paragraph = used.GetValueOrDefault(level)?.Format;
                    RichTextListMarker marker;
                    if (definition.Kind == RichListKind.Numbered)
                        marker = new RichTextListMarker.Number(definition.NumberFormat switch
                        {
                            ListNumberFormat.UpperRoman => RichTextListNumberStyle.UpperRoman,
                            ListNumberFormat.LowerRoman => RichTextListNumberStyle.LowerRoman,
                            ListNumberFormat.UpperLetter => RichTextListNumberStyle.UpperLetter,
                            ListNumberFormat.LowerLetter => RichTextListNumberStyle.LowerLetter,
                            _ => RichTextListNumberStyle.Arabic,
                        }, paragraph?.NativeList is { Kind: RichListKind.Numbered, Restart: false } item ? item.StartAt : definition.StartAt);
                    else if (GetListPictureId(definition) is { } pictureId)
                        marker = new RichTextListMarker.Picture(pictureId, definition.BulletText);
                    else
                        marker = new RichTextListMarker.Bullet(definition.BulletText);

                    levels[level] = new()
                    {
                        Marker = marker,
                        Prefix = definition.Prefix,
                        Suffix = definition.Suffix,
                        LeadingIndent = definition.LeadingIndent ?? paragraph?.LeadingIndent ?? 0,
                        FirstLineIndent = definition.FirstLineIndent ?? paragraph?.FirstLineIndent ?? 0,
                        MarkerTab = definition.MarkerTab ?? (paragraph is { TabStops.IsDefaultOrEmpty: false } ? paragraph.TabStops[0].Position : 0),
                    };
                }

                yield return new(new RichTextListId(modelId), new RichTextListDefinition(levels));
            }
        }

        private void EnsureParagraphList(ReaderState state)
        {
            if (_paragraphListHandled || state.ListOverride <= 0)
            {
                return;
            }

            var definition = ResolveList(state.ListOverride, state.ListLevel);
            if (definition is null)
            {
                return;
            }

            var level = Math.Clamp(state.ListLevel, 0, 8);
            _listItems[_lineStart] = new RichTextListFormat
            {
                Id = GetModelListId(state.ListOverride),
                Level = level,
                Kind = definition.Value.Kind,
                NumberStyle = definition.Value.NumberFormat switch
                {
                    ListNumberFormat.UpperRoman => RichListNumberStyle.UpperRoman,
                    ListNumberFormat.LowerRoman => RichListNumberStyle.LowerRoman,
                    ListNumberFormat.UpperLetter => RichListNumberStyle.UpperLetter,
                    ListNumberFormat.LowerLetter => RichListNumberStyle.LowerLetter,
                    _ => RichListNumberStyle.Arabic,
                },
                StartAt = definition.Value.StartAt,
                Restart = _listOverrideStartAt.ContainsKey((state.ListOverride, level)) &&
                    !_nextListNumbers.ContainsKey(GetListCounterKey(state.ListOverride, level)) &&
                    !IsContinuationStartOverride(state.ListOverride, level),
                Prefix = definition.Value.Prefix,
                Suffix = definition.Value.Suffix,
                BulletText = definition.Value.BulletText,
                PictureId = definition.Value.Kind == RichListKind.Bulleted
                    ? GetListPictureId(definition)
                    : null,
            };
            if (definition.Value.Kind == RichListKind.Numbered)
            {
                _ = GetNextListNumber(state.ListOverride, state.ListLevel, definition.Value);
            }

            _paragraphListHandled = true;
        }

        private void TrackParagraphFormat(ReaderState state)
        {
            if (state.Destination != Destination.Body)
            {
                return;
            }

            if (state.ParagraphFormat == _defaultParagraphFormat)
            {
                _paragraphs.Remove(_lineStart);
            }
            else
            {
                _paragraphs[_lineStart] = state.ParagraphFormat;
            }
        }

        private ParsedListDefinition? ResolveList(int overrideId, int level)
        {
            if (overrideId <= 0)
            {
                return null;
            }

            level = Math.Clamp(level, 0, 8);
            ParsedListDefinition? result = null;
            if (_listOverrideLevels.TryGetValue((overrideId, level), out var formatOverride))
            {
                // A \listoverrideformat level replaces the base list level.
                result = formatOverride;
            }
            else if (_listOverrides.TryGetValue(overrideId, out var listId))
            {
                if (_lists.TryGetValue((listId, level), out var definition))
                {
                    result = definition;
                }
                else if (_lists.TryGetValue((listId, 0), out definition))
                {
                    result = definition;
                }
                else
                {
                    for (var fallbackLevel = 1; fallbackLevel < 9; fallbackLevel++)
                    {
                        if (_lists.TryGetValue((listId, fallbackLevel), out definition))
                        {
                            result = definition;
                            break;
                        }
                    }
                }
            }
            else
            {
                return null;
            }

            if (result is { } resolved &&
                _listOverrideStartAt.TryGetValue((overrideId, level), out var startAt))
            {
                result = resolved with { StartAt = startAt };
            }

            return result;
        }

        private int GetModelListId(int overrideId)
        {
            var identity = GetListIdentity(overrideId);
            if (!_modelListIds.TryGetValue(identity, out var modelListId))
            {
                modelListId = _nextModelListId++;
                _modelListIds.Add(identity, modelListId);
            }

            return modelListId;
        }

        private (bool IsTableList, int Id) GetListIdentity(int overrideId) =>
            _listOverrides.TryGetValue(overrideId, out var listId) && !HasFormatOverride(overrideId)
                ? (true, listId)
                : (false, overrideId);

        private bool HasFormatOverride(int overrideId)
        {
            for (var level = 0; level < 9; level++)
            {
                if (_listOverrideLevels.ContainsKey((overrideId, level)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasStartOverride(int overrideId)
        {
            for (var level = 0; level < 9; level++)
            {
                if (_listOverrideStartAt.ContainsKey((overrideId, level)))
                {
                    return true;
                }
            }

            return false;
        }

        private (bool IsTableList, int Id, int Level) GetListCounterKey(
            int overrideId,
            int level)
        {
            // A start-at override begins its own numbering thread even though its
            // marker definition continues to come from the shared base list.
            if (HasStartOverride(overrideId))
            {
                return (false, overrideId, Math.Clamp(level, 0, 8));
            }

            var identity = GetListIdentity(overrideId);
            return (identity.IsTableList, identity.Id, Math.Clamp(level, 0, 8));
        }

        private bool IsContinuationStartOverride(int overrideId, int level)
        {
            // Writers encode numbering continuation across overrides by starting the
            // next override exactly where the shared list counter stopped. Such an
            // override is not an explicit user restart.
            level = Math.Clamp(level, 0, 8);
            if (!_listOverrideStartAt.TryGetValue((overrideId, level), out var startAt))
            {
                return false;
            }

            var identity = GetListIdentity(overrideId);
            return _nextListNumbers.TryGetValue(
                (identity.IsTableList, identity.Id, level),
                out var expected) &&
                   startAt == expected;
        }

        private int GetNextListNumber(
            int overrideId,
            int level,
            ParsedListDefinition definition)
        {
            var key = GetListCounterKey(overrideId, level);
            var number = _nextListNumbers.GetValueOrDefault(key, definition.StartAt);
            _nextListNumbers[key] = number == int.MaxValue ? number : number + 1;
            return number;
        }

        private void UpdateNumberCounter(int overrideId, int level, string marker)
        {
            var definition = ResolveList(overrideId, level);
            if (definition is not { Kind: RichListKind.Numbered })
            {
                return;
            }

            var key = GetListCounterKey(overrideId, level);
            if (TryParseListNumberMarker(marker, definition, out var parsed))
            {
                _nextListNumbers[key] = parsed.Number == int.MaxValue
                    ? parsed.Number
                    : parsed.Number + 1;
            }
            else
            {
                _ = GetNextListNumber(overrideId, level, definition.Value);
            }
        }

        private static bool TryParseListNumberMarker(
            string marker,
            ParsedListDefinition? definition,
            out NumberMarker parsed)
        {
            if (definition is { Kind: RichListKind.Numbered } known)
            {
                var content = marker.AsSpan();
                content = !content.IsEmpty && content[^1] == '\t'
                    ? content[..^1]
                    : content.TrimEnd();
                if (content.StartsWith(known.Prefix, StringComparison.Ordinal) &&
                    content.EndsWith(known.Suffix, StringComparison.Ordinal) &&
                    content.Length >= known.Prefix.Length + known.Suffix.Length)
                {
                    var token = content.Slice(
                        known.Prefix.Length,
                        content.Length - known.Prefix.Length - known.Suffix.Length);
                    if (TryParseNumberToken(token, known.NumberFormat, out var number))
                    {
                        parsed = new NumberMarker(
                            number,
                            known.Suffix.Length > 0 ? known.Suffix[0] : '\0',
                            known.NumberFormat);
                        return true;
                    }
                }
            }

            return TryParseNumberMarker(marker.AsSpan().TrimStart(), out parsed, out _);
        }

        private static bool TryParseNumberToken(
            ReadOnlySpan<char> token,
            ListNumberFormat format,
            out int number)
        {
            if (format == ListNumberFormat.Arabic)
            {
                return int.TryParse(
                    token,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out number) && number > 0;
            }

            if (format is ListNumberFormat.UpperRoman or ListNumberFormat.LowerRoman &&
                TryParseRoman(token, out number, out var romanFormat))
            {
                return romanFormat == format;
            }

            if (format is ListNumberFormat.UpperLetter or ListNumberFormat.LowerLetter &&
                TryParseLetters(token, out number, out var letterFormat))
            {
                return letterFormat == format;
            }

            number = 0;
            return false;
        }

        private static RichListKind? InferListKind(string marker)
        {
            var content = marker.AsSpan().TrimStart();
            if (content.Length == 0)
            {
                return null;
            }

            if (IsBulletMarker(content[0]))
            {
                return RichListKind.Bulleted;
            }

            return RichListKind.Numbered;
        }

        private static string GetBulletText(string marker, string? fallback)
        {
            if (!string.IsNullOrEmpty(fallback))
            {
                return fallback;
            }

            var content = marker.AsSpan().Trim();
            return content.Length > 0 && IsBulletMarker(content[0])
                ? content[0].ToString()
                : fallback ?? "•";
        }
    }
}
