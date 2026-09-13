using System.Collections.Immutable;
using System.Globalization;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Writer
    {
        private void WriteListTables()
        {
            if (_listDefinitions.Count == 0)
            {
                return;
            }

            _output.Append(@"{\*\listtable").Append("\r\n");
            WriteListPictures();
            foreach (var definition in _listDefinitions)
            {
                _output.Append(@"{\list\listtemplateid").Append(definition.ListId)
                    .Append(definition.IsMultilevel ? @"\listhybrid" : @"\listsimple1");

                var levelCount = definition.IsMultilevel ? 9 : 1;
                for (var level = 0; level < levelCount; level++)
                {
                    WriteListLevel(definition.Levels[level], level);
                }

                _output.Append(@"\listrestarthdn0\listid")
                    .Append(definition.ListId)
                    .Append(@"{\listname ;}}")
                    .Append("\r\n");
            }

            _output.Append('}').Append("\r\n");
            _output.Append(@"{\*\listoverridetable").Append("\r\n");
            foreach (var listOverride in _listOverrides)
            {
                var levelCount = listOverride.HasStartOverrides
                    ? (listOverride.Definition.IsMultilevel ? 9 : 1)
                    : 0;
                _output.Append(@"{\listoverride\listid")
                    .Append(listOverride.Definition.ListId)
                    .Append(@"\listoverridecount").Append(levelCount)
                    .Append(@"\ls").Append(listOverride.OverrideId);
                for (var level = 0; level < levelCount; level++)
                {
                    _output.Append(@"{\lfolevel");
                    if (listOverride.StartAtByLevel[level] is { } startAt)
                    {
                        _output.Append(@"\listoverridestartat\levelstartat").Append(startAt);
                    }

                    _output.Append('}');
                }

                _output.Append('}').Append("\r\n");
            }

            _output.Append('}').Append("\r\n");
        }

        private void WriteListPictures()
        {
            if (_listPictures.Length == 0)
            {
                return;
            }

            _output.Append(@"{\*\listpicture").Append("\r\n");
            foreach (var picture in _listPictures)
            {
                _ = TryGetPictureControl(
                    picture.MediaType,
                    picture.Data.AsSpan(),
                    out var control,
                    out var isRaster);
                _output.Append(@"{\*\shppict");
                WritePictureData(
                    control,
                    isRaster,
                    picture.Data.AsSpan(),
                    picture.Width,
                    picture.Height,
                    default,
                    picture.AlternativeText,
                    rotation: 0);
                _output.Append('}').Append("\r\n");
            }

            _output.Append('}').Append("\r\n");
        }

        private void WriteListLevel(ListLevelDefinition? definition, int level)
        {
            var numberFormat = definition switch
            {
                { Kind: RichListKind.Bulleted } => 23,
                { Kind: RichListKind.Numbered } => (int)definition.NumberFormat,
                _ => 255,
            };
            _output.Append(@"{\listlevel\levelnfc").Append(numberFormat)
                .Append(@"\levelnfcn").Append(numberFormat)
                .Append(@"\leveljc0\leveljcn0\levelfollow0\levelstartat")
                .Append(definition?.StartAt ?? 1);
            if (level > 0)
            {
                // RichTextListFormat counters are independent per level. Prevent an
                // increment at a superior level from implicitly resetting this one.
                _output.Append(@"\levelnorestart1");
            }

            WriteListLevelText(definition, level);
            if (definition?.PictureId is { } pictureId &&
                _listPictureIndices.TryGetValue(pictureId, out var pictureIndex))
            {
                _output.Append(@"\levelpicture").Append(pictureIndex);
            }

            var indent = checked((level + 1) * 720);
            _output.Append(@"\fi-360\li").Append(indent)
                .Append(@"\lin").Append(indent)
                .Append('}');
        }

        private void WriteListLevelText(ListLevelDefinition? definition, int level)
        {
            _output.Append(@"{\leveltext");
            if (definition is null)
            {
                WriteHexByte(0);
                _output.Append(@";}{\levelnumbers;}");
                return;
            }

            if (definition.Kind == RichListKind.Bulleted)
            {
                var bulletLength = Math.Min(definition.BulletText.Length, byte.MaxValue);
                WriteHexByte(bulletLength);
                WriteText(definition.BulletText.AsSpan(0, bulletLength));
                _output.Append(@";}{\levelnumbers;}");
                return;
            }

            var prefixLength = Math.Min(definition.Prefix.Length, byte.MaxValue - 1);
            var suffixLength = Math.Min(
                definition.Suffix.Length,
                byte.MaxValue - prefixLength - 1);
            WriteHexByte(prefixLength + suffixLength + 1);
            WriteText(definition.Prefix.AsSpan(0, prefixLength));
            WriteHexByte(level);
            WriteText(definition.Suffix.AsSpan(0, suffixLength));
            _output.Append(@";}{\levelnumbers");
            WriteHexByte(prefixLength + 1);
            _output.Append(";}");
        }

        private void WriteHexByte(int value) =>
            _output.Append(@"\'").Append(value.ToString("x2", CultureInfo.InvariantCulture));

        private List<ListDefinition> BuildListDefinitions()
        {
            var definitions = new List<ListDefinition>();
            var definitionsById = new Dictionary<int, ListDefinition>();
            foreach (var paragraph in _document.Paragraphs)
            {
                if (paragraph.Format.NativeList is not { } list)
                {
                    continue;
                }

                if (!definitionsById.TryGetValue(list.Id, out var definition))
                {
                    definition = new ListDefinition(definitions.Count + 1, list.Id);
                    var id = new RichTextListId(list.Id);
                    var source = _document.Lists[id];
                    // Keep ancestor levels even when no paragraph currently uses them.
                    // RichEdit imports listtext as content when the first level is empty.
                    for (var level = 0; level < source.Levels.Length; level++)
                    {
                        definition.AddLevel(RichTextListConversions.ToNative(id, level, null, source));
                    }

                    definitionsById.Add(list.Id, definition);
                    definitions.Add(definition);
                }
            }

            var nextOverrideId = 0;
            ListOverrideDefinition AddOverride(
                ListDefinition definition,
                int?[] startAtByLevel)
            {
                nextOverrideId++;
                if (nextOverrideId > MaximumListOverrideId)
                {
                    throw new InvalidOperationException(
                        $"RTF 1.9.1 permits at most {MaximumListOverrideId} list override IDs in one document.");
                }

                var result = new ListOverrideDefinition(
                    definition,
                    nextOverrideId,
                    startAtByLevel);
                _listOverrides.Add(result);
                return result;
            }

            var activeOverrides = new Dictionary<int, ListOverrideDefinition>();
            foreach (var definition in definitions)
            {
                activeOverrides.Add(
                    definition.SourceId,
                    AddOverride(definition, new int?[9]));
            }

            var nextNumbers = new Dictionary<(int ListId, int Level), int>();
            foreach (var paragraph in _document.Paragraphs)
            {
                if (paragraph.Format.NativeList is not { } list)
                {
                    continue;
                }

                var definition = definitionsById[list.Id];
                var listOverride = activeOverrides[list.Id];
                if (list.Kind == RichListKind.Numbered && list.Restart)
                {
                    var starts = new int?[9];
                    for (var level = 0; level < starts.Length; level++)
                    {
                        if (definition.Levels[level] is { Kind: RichListKind.Numbered } levelDefinition)
                        {
                            starts[level] = nextNumbers.GetValueOrDefault(
                                (list.Id, level),
                                levelDefinition.StartAt);
                        }
                    }

                    starts[list.Level] = list.StartAt;
                    listOverride = AddOverride(definition, starts);
                    activeOverrides[list.Id] = listOverride;
                }

                _listsByItemStart.Add(
                    paragraph.Start,
                    new ListItemDefinition(listOverride, list));
                if (list.Kind == RichListKind.Numbered)
                {
                    var key = (list.Id, list.Level);
                    var number = list.Restart
                        ? list.StartAt
                        : nextNumbers.GetValueOrDefault(key, list.StartAt);
                    nextNumbers[key] = number == int.MaxValue ? number : number + 1;
                }
            }

            return definitions;
        }

        private sealed class ListDefinition(int listId, int sourceId)
        {
            public int ListId { get; } = listId;

            public int SourceId { get; } = sourceId;

            public ListLevelDefinition?[] Levels { get; } = new ListLevelDefinition?[9];

            public bool IsMultilevel => Levels[0] is null ||
                Array.FindIndex(Levels, 1, static level => level is not null) >= 1;

            public void AddLevel(RichTextListFormat format)
            {
                Levels[format.Level] ??= new ListLevelDefinition(
                    format.Kind,
                    format.StartAt,
                    format.Prefix,
                    format.Suffix,
                    format.BulletText,
                    format.PictureId,
                    format.NumberStyle switch
                    {
                        RichListNumberStyle.UpperRoman => ListNumberFormat.UpperRoman,
                        RichListNumberStyle.LowerRoman => ListNumberFormat.LowerRoman,
                        RichListNumberStyle.UpperLetter => ListNumberFormat.UpperLetter,
                        RichListNumberStyle.LowerLetter => ListNumberFormat.LowerLetter,
                        _ => ListNumberFormat.Arabic,
                    });
            }
        }

        private sealed record ListLevelDefinition(
            RichListKind Kind,
            int StartAt,
            string Prefix,
            string Suffix,
            string BulletText,
            string? PictureId,
            ListNumberFormat NumberFormat);

        private sealed class ListOverrideDefinition(
            ListDefinition definition,
            int overrideId,
            int?[] startAtByLevel)
        {
            public ListDefinition Definition { get; } = definition;

            public int OverrideId { get; } = overrideId;

            public int?[] StartAtByLevel { get; } = startAtByLevel;

            public bool HasStartOverrides =>
                Array.Exists(StartAtByLevel, static startAt => startAt is not null);
        }

        private readonly record struct ListItemDefinition(
            ListOverrideDefinition Override,
            RichTextListFormat Format);
    }
}
