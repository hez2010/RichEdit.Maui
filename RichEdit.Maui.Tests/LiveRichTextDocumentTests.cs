using System.Reflection;

namespace RichEdit.Maui.Tests;

public sealed class LiveRichTextDocumentTests
{
    private static readonly RichTextListDefinition NumberedOutline = new(
    [
        new RichTextListLevelDefinition
        {
            Marker = new RichTextListMarker.Number(RichTextListNumberStyle.UpperRoman, 4),
            Prefix = "(",
            Suffix = ")",
            LeadingIndent = 36,
            FirstLineIndent = -24,
            MarkerTab = 36,
        },
        new RichTextListLevelDefinition
        {
            Marker = new RichTextListMarker.Bullet("→"),
            Prefix = string.Empty,
            Suffix = string.Empty,
            LeadingIndent = 54,
            FirstLineIndent = -18,
            MarkerTab = 54,
        },
    ]);

    [Fact]
    public void EditCommitsOneObservableAtomicChange()
    {
        var document = new RichTextDocument();
        RichTextDocumentChangedEventArgs? observed = null;
        var eventCount = 0;
        document.Changed += (_, args) =>
        {
            eventCount++;
            observed = args;
        };

        var changeSet = document.Edit(
            edit =>
            {
                edit.InsertText(0, "hello");
                edit.UpdateCharacterFormat(
                    new RichTextRange(0, 5),
                    format => format with { FontWeight = 700 });
                edit.SetMetadata("source", "test");
            },
            new RichTextEditOptions(
                undoDescription: "Create greeting",
                tag: "transaction"));

        Assert.Equal(1, eventCount);
        Assert.Same(changeSet, observed!.ChangeSet);
        Assert.Equal(0, changeSet.VersionBefore);
        Assert.Equal(1, changeSet.VersionAfter);
        Assert.Equal(RichTextChangeOrigin.Programmatic, changeSet.Origin);
        Assert.Equal("transaction", changeSet.Tag);
        Assert.Contains(changeSet.Changes, change => change.Kind == RichTextChangeKind.Text);
        Assert.Contains(changeSet.Changes, change => change.Kind == RichTextChangeKind.CharacterFormat);
        Assert.Contains(changeSet.Changes, change => change.Kind == RichTextChangeKind.Metadata);
        Assert.Equal("hello", document.Text);
        Assert.True(document.GetCharacterFormat(new RichTextRange(0, 5)).RepresentativeFormat.Bold);
    }

    [Fact]
    public void ThrowingEditRollsBackEveryOperation()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertText(0, "before"));
        var snapshot = document.CurrentSnapshot;
        var version = document.Version;
        var eventCount = 0;
        document.Changed += (_, _) => eventCount++;

        Assert.Throws<InvalidOperationException>(() => document.Edit(edit =>
        {
            edit.ReplaceText(new RichTextRange(0, 6), "after");
            edit.SetMetadata("partial", "must not escape");
            throw new InvalidOperationException("Abort transaction");
        }));

        Assert.Equal(0, eventCount);
        Assert.Equal(version, document.Version);
        Assert.Same(snapshot, document.CurrentSnapshot);
        Assert.Equal("before", document.Text);
        Assert.Empty(document.CurrentSnapshot.Metadata);
    }

    [Fact]
    public void SemanticNoOpDoesNotAdvanceVersionOrRaiseChanged()
    {
        var document = new RichTextDocument();
        var eventCount = 0;
        document.Changed += (_, _) => eventCount++;

        var empty = document.Edit(_ => { });
        var emptyReplacement = document.Edit(edit =>
            edit.ReplaceText(RichTextRange.Empty, string.Empty));

        Assert.True(empty.IsEmpty);
        Assert.True(emptyReplacement.IsEmpty);
        Assert.Equal(0, document.Version);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void RtfSerializationIsCachedByDocumentVersion()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertText(0, "cached"));

        var first = document.RtfText;
        var second = document.RtfText;
        Assert.Same(first, second);

        document.Edit(edit => edit.InsertText(document.Length, "!"));
        var third = document.RtfText;
        Assert.NotSame(first, third);
        Assert.Same(third, document.RtfText);
    }

    [Fact]
    public void SnapshotRtfAndFormatSummariesAreCached()
    {
        var document = new RichTextDocument();
        document.Edit(edit =>
        {
            edit.InsertText(0, "ab");
            edit.SetDefaultCharacterFormat(RichTextCharacterFormat.Default with
            {
                FontFamily = "Document Default",
            });
            edit.UpdateCharacterFormat(
                new RichTextRange(1, 1),
                format => format with { FontFamily = "Explicit" });
        });

        var snapshot = document.CurrentSnapshot;
        Assert.Same(snapshot.RtfText, snapshot.RtfText);

        var range = new RichTextRange(0, 2);
        var first = document.GetCharacterFormat(range);
        var second = document.GetCharacterFormat(range);
        Assert.Same(first, second);
        Assert.True(first.FontFamily.IsMixed);
        Assert.False(first.FontFamily.IsInherited);
        Assert.True(first.EffectiveFontFamily.IsMixed);
        Assert.Equal("Document Default", first.EffectiveFontFamily.Value);
        Assert.False(first.GetValue(static format => format.Bold).IsMixed);
    }

    [Fact]
    public void DocumentDefaultsRemainInheritedAcrossEditingAndRtfRoundTrip()
    {
        var foreground = Color.FromRgb(0x12, 0x34, 0x56);
        var defaults = RichTextCharacterFormat.Default with
        {
            FontFamily = "Georgia",
            FontSize = 17,
            ForegroundColor = foreground,
        };
        var document = new RichTextDocument();
        document.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(defaults);
            edit.InsertText(0, "inherited");
        });

        var run = Assert.Single(document.CurrentSnapshot.Runs).Format;
        Assert.Null(run.FontFamily);
        Assert.Null(run.FontSize);
        Assert.Null(run.ForegroundColor);
        var summary = document.GetCharacterFormat(new RichTextRange(0, document.Length));
        Assert.True(summary.FontFamily.IsInherited);
        Assert.Equal("Georgia", summary.EffectiveFontFamily.Value);
        Assert.Equal(17, summary.EffectiveFontSize.Value);
        Assert.Equal(foreground, summary.EffectiveForegroundColor.Value);

        var parsed = new RichTextDocument { RtfText = document.RtfText };
        var parsedRun = Assert.Single(parsed.CurrentSnapshot.Runs).Format;
        Assert.Null(parsedRun.FontFamily);
        Assert.Null(parsedRun.FontSize);
        Assert.Null(parsedRun.ForegroundColor);
        Assert.Equal(defaults.FontFamily, parsed.DefaultCharacterFormat.FontFamily);
        Assert.Equal(defaults.FontSize, parsed.DefaultCharacterFormat.FontSize);
        Assert.Equal(defaults.ForegroundColor, parsed.DefaultCharacterFormat.ForegroundColor);
    }

    [Fact]
    public void ExplicitValueEqualToDocumentDefaultRemainsExplicitInRtf()
    {
        var document = new RichTextDocument();
        document.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(RichTextCharacterFormat.Default with
            {
                FontFamily = "Georgia",
            });
            edit.InsertText(0, "explicit");
            edit.UpdateCharacterFormat(
                new RichTextRange(0, 8),
                format => format with { FontFamily = "Georgia" });
        });

        var parsed = new RichTextDocument { RtfText = document.RtfText };
        Assert.Equal("Georgia", Assert.Single(parsed.CurrentSnapshot.Runs).Format.FontFamily);

        parsed.Edit(edit => edit.ClearCharacterFormat(new RichTextRange(0, parsed.Length)));
        Assert.Null(Assert.Single(parsed.CurrentSnapshot.Runs).Format.FontFamily);
    }

    [Fact]
    public void InvalidRtfAssignmentIsAtomic()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertText(0, "preserved"));
        var snapshot = document.CurrentSnapshot;
        var version = document.Version;

        Assert.Throws<FormatException>(() => document.RtfText = "not RTF");

        Assert.Equal(version, document.Version);
        Assert.Same(snapshot, document.CurrentSnapshot);
        Assert.Equal("preserved", document.Text);
    }

    [Fact]
    public void UndoAndRedoPublishBoundedIncrementalChanges()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertText(0, "abc"));
        RichTextChangeSet? observed = null;
        document.Changed += (_, args) => observed = args.ChangeSet;

        document.Undo();

        Assert.Equal(string.Empty, document.Text);
        Assert.Equal(RichTextChangeOrigin.Undo, observed!.Origin);
        Assert.DoesNotContain(
            observed.Changes,
            change => change.Kind == RichTextChangeKind.Reset);
        Assert.Contains(observed.Changes, change => change.Kind == RichTextChangeKind.Text);

        document.Redo();

        Assert.Equal("abc", document.Text);
        Assert.Equal(RichTextChangeOrigin.Redo, observed!.Origin);
        Assert.DoesNotContain(
            observed.Changes,
            change => change.Kind == RichTextChangeKind.Reset);
    }

    [Fact]
    public void AdjacentNativeTypingUsesOneManagedUndoUnit()
    {
        var document = new RichTextDocument();
        var sourceToken = new object();

        var first = document.CurrentSnapshot.Replace(0..0, "a");
        document.ReplaceSnapshotFromNative(first, sourceToken);
        var second = document.CurrentSnapshot.Replace(1..1, "b");
        document.ReplaceSnapshotFromNative(second, sourceToken);
        var third = document.CurrentSnapshot.Replace(2..2, "c");
        document.ReplaceSnapshotFromNative(third, sourceToken);

        Assert.Equal("abc", document.Text);
        document.Undo();
        Assert.Equal(string.Empty, document.Text);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void NativeTypingFromDifferentEditorsDoesNotMergeUndoUnits()
    {
        var document = new RichTextDocument();
        var first = document.CurrentSnapshot.Replace(0..0, "a");
        document.ReplaceSnapshotFromNative(first, new object());
        var second = document.CurrentSnapshot.Replace(1..1, "b");
        document.ReplaceSnapshotFromNative(second, new object());

        document.Undo();
        Assert.Equal("a", document.Text);
        Assert.True(document.CanUndo);
    }

    [Fact]
    public void ImagePayloadOnlyUndoPublishesAnImageDelta()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertImage(
            0,
            RichTextImage.FromBytes(0, "image/png", [1, 2, 3], 12, 12)));
        document.ClearUndoHistory();
        document.Edit(edit => edit.UpdateImage(
            0,
            RichTextImage.FromBytes(0, "image/png", [4, 5, 6], 12, 12)));
        RichTextChangeSet? observed = null;
        document.Changed += (_, args) => observed = args.ChangeSet;

        document.Undo();

        Assert.Contains(observed!.Changes, change => change.Kind == RichTextChangeKind.Image);
        Assert.Equal([1, 2, 3], document.CurrentSnapshot.Images[0].Data);
    }

    [Fact]
    public void ListDefinitionsAreStoredOnceAndReferencedByParagraphs()
    {
        var document = new RichTextDocument();
        RichTextListId listId = default;
        document.Edit(edit =>
        {
            edit.InsertText(0, "first\nsecond");
            listId = edit.CreateList(NumberedOutline);
            edit.ApplyList(new RichTextRange(0, 12), listId);
        });

        var snapshot = document.CurrentSnapshot;
        Assert.Single(snapshot.Lists);
        Assert.Equal(NumberedOutline, snapshot.Lists[listId]);
        Assert.All(snapshot.Paragraphs, paragraph =>
            Assert.Equal(listId, paragraph.Format.List?.ListId));

        document.Edit(edit => edit.ChangeListLevel(new RichTextRange(6, 6), 1));
        Assert.Equal(0, document.CurrentSnapshot.Paragraphs[0].Format.List?.Level);
        Assert.Equal(1, document.CurrentSnapshot.Paragraphs[1].Format.List?.Level);
    }

    [Fact]
    public void RichFragmentImportsFormattingLinksAndRemappedLists()
    {
        var source = new RichTextDocument();
        source.Edit(edit =>
        {
            edit.InsertText(0, "item");
            edit.UpdateCharacterFormat(
                new RichTextRange(0, 4),
                format => format with { Italic = true });
            edit.SetLink(new RichTextRange(0, 4), "https://example.com");
            var listId = edit.CreateList(NumberedOutline);
            edit.ApplyList(new RichTextRange(0, 4), listId);
        });
        var fragment = RichTextDocumentFragment.FromRtf(source.RtfText);

        var target = new RichTextDocument();
        target.Edit(edit =>
        {
            edit.InsertText(0, "AB");
            edit.ReplaceFragment(new RichTextRange(1, 0), fragment);
        });

        var snapshot = target.CurrentSnapshot;
        Assert.Equal("AitemB", target.Text);
        Assert.True(snapshot.GetCharacterFormat(1).Italic);
        Assert.Contains(snapshot.Links, link =>
            link.Range == new RichTextRange(1, 4) &&
            link.Target == "https://example.com");
        Assert.Single(snapshot.Lists);
        Assert.NotNull(snapshot.GetParagraphFormat(1).List);
    }

    [Fact]
    public void MultiParagraphDeletionPreservesOnlyTheSurvivingListItems()
    {
        var document = new RichTextDocument();
        document.Edit(edit =>
        {
            edit.InsertText(0, "one\ntwo\nthree\nfour");
            var listId = edit.CreateList(NumberedOutline);
            edit.ApplyList(new RichTextRange(0, 18), listId);
        });
        RichTextChangeSet? observed = null;
        document.Changed += (_, args) => observed = args.ChangeSet;

        document.Edit(edit => edit.DeleteText(new RichTextRange(4, 10)));

        Assert.Equal("one\nfour", document.Text);
        Assert.Equal([0, 4], document.CurrentSnapshot.Paragraphs.Select(static item => item.Range.Start));
        Assert.All(document.CurrentSnapshot.Paragraphs, paragraph =>
            Assert.NotNull(paragraph.Format.List));
        Assert.Single(document.CurrentSnapshot.Lists);
        var textChange = Assert.Single(observed!.Changes.OfType<RichTextTextChange>());
        Assert.Equal(new RichTextRange(4, 10), textChange.OldRange);
        Assert.Equal(string.Empty, textChange.InsertedText);
    }

    [Fact]
    public void AffectedRangeMapsEarlierFormattingThroughLaterTextChanges()
    {
        var changes = new RichTextChangeSet(
            1,
            2,
            RichTextChangeOrigin.Programmatic,
            [
                new RichTextRangeChange(
                    RichTextChangeKind.CharacterFormat,
                    new RichTextRange(100, 10),
                    new RichTextRange(100, 10)),
                new RichTextTextChange(new RichTextRange(50, 0), "y"),
            ],
            tag: null);

        Assert.Equal(
            new RichTextRange(50, 61),
            changes.GetAffectedRange(documentLength: 121));
    }

    [Fact]
    public void SameTextReplacementReportsFormattingWithoutATextChange()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertText(0, "same"));

        var changes = document.Edit(edit => edit.ReplaceText(
            new RichTextRange(0, 4),
            "same",
            RichTextCharacterFormat.Default with { FontWeight = 700 }));

        Assert.False(changes.IsTextChanged);
        Assert.DoesNotContain(changes.Changes, change => change.Kind == RichTextChangeKind.Text);
        Assert.Contains(changes.Changes, change => change.Kind == RichTextChangeKind.CharacterFormat);
        Assert.True(document.CurrentSnapshot.GetCharacterFormat(3).Bold);
    }

    [Fact]
    public void FieldUpdateChangesInstructionAndVisibleResultAtomically()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertField(0, "OLD", "old"));

        var changes = document.Edit(edit => edit.UpdateField(
            new RichTextRange(0, 3),
            "NEW",
            "updated"));

        Assert.Equal("updated", document.Text);
        var field = Assert.Single(document.CurrentSnapshot.Fields);
        Assert.Equal(new RichTextRange(0, 7), field.Range);
        Assert.Equal("NEW", field.Instruction);
        Assert.True(changes.IsTextChanged);
        Assert.Contains(changes.Changes, change => change.Kind == RichTextChangeKind.Field);
    }

    [Fact]
    public void PublicSurfaceContainsOnlyTheNewContentAndListApis()
    {
        var editorType = typeof(RichEditor);
        Assert.NotNull(editorType.GetProperty(nameof(RichEditor.RtfText)));
        Assert.NotNull(editorType.GetProperty(nameof(RichEditor.Selection)));
        Assert.Null(editorType.GetMethod("LoadRtf", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(editorType.GetMethod("ToRtf", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(editorType.GetMethod("ToggleBulletedList", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(editorType.GetMethod("ToggleNumberedList", BindingFlags.Public | BindingFlags.Instance));

        var exportedNames = editorType.Assembly.GetExportedTypes()
            .Select(static type => type.Name)
            .ToArray();
        Assert.DoesNotContain("RichTextCapabilities", exportedNames);
        Assert.DoesNotContain("RichTextFeatureSupport", exportedNames);
        Assert.DoesNotContain("RichTextPlatformSupport", exportedNames);
        Assert.DoesNotContain("RichTextListFormat", exportedNames);
        Assert.DoesNotContain("RichListKind", exportedNames);
    }

}
