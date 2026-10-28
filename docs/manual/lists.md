---
title: Lists and numbering
description: Semantic lists with native editing and RTF persistence.
---

# Lists and numbering

A `RichList` owns ordered `RichListItem` nodes. Each item contains paragraphs or nested blocks. List membership is structural and does not depend on a paragraph's font or a marker embedded in its text.

```csharp
RichNodeId list = default;
editor.Edit(edit =>
{
    list = edit.WrapInList(edit.Body, 0, 1, numbered: true,
        startNumber: 1, numberStyle: RichListNumberingStyle.Decimal);
});
```

Use `SetListNumbering` to set numbered/bulleted mode, the starting counter, and decimal, upper/lower Roman, or upper/lower alphabetic numbering. Bullet lists use the neutral Decimal number-style value. `UnwrapList` returns its items' blocks to their containing flow.

`parent`, `index`, and `count` identify contiguous blocks in one story, list item, or table cell. The example wraps the first body block. For a toolbar acting on a selection, resolve the selected blocks and their common parent before calling `WrapInList`; a paragraph indent alone does not change list nesting.

## Restart and change numbering

```csharp
editor.Edit(edit => edit.SetListNumbering(list,
    numbered: true, startNumber: 3,
    numberStyle: RichListNumberingStyle.UpperRoman),
    new RichEditOptions(Description: "Restart numbering"));
```

This starts the existing list at III without rewriting its item text. Use `numbered: false` and the default `Decimal` style for bullets. The available numbered styles are `Decimal`, `UpperRoman`, `LowerRoman`, `UpperLetter`, and `LowerLetter`.

## Create a nested list

A nested list is a block inside a `RichListItem`. This example appends a child paragraph to the first item of the previously created list, then wraps that paragraph:

```csharp
editor.Edit(edit =>
{
    var itemId = edit.Snapshot.Get<RichList>(list).Items[0];
    int index = edit.Snapshot.Get<RichListItem>(itemId).Blocks.Count;
    edit.InsertParagraph(itemId, index, "Nested item");
    edit.WrapInList(itemId, index, 1, numbered: false);
}, new RichEditOptions(Description: "Insert nested list"));
```

`WrapInList` retains wrapped block identities. Each item starts with a paragraph; wrapping a nonparagraph block adds an empty marker paragraph before it. To remove an entire list's membership, call `UnwrapList(list)`. To remove one item's membership, use `UnwrapListItem(itemId)`.

## Native editing and persistence

`SplitListItem`, `JoinListItem`, and `UnwrapListItem` are semantic editing operations. Native Enter splits an item; appropriate empty-item Enter and beginning-of-item Backspace operations update the list structure and retain native focus. The parent document owns their undo.

RTF uses list definitions and overrides, with semantic metadata retaining the block graph. Nesting is limited to the codec's supported nine levels, and a document can export at most 2,000 list overrides. Custom marker strings, arbitrary prefix/suffix templates, picture bullets, and custom marker fonts are outside the authored list profile.
