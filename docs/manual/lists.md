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

`SplitListItem`, `JoinListItem`, and `UnwrapListItem` are semantic editing operations. Native Enter splits an item; appropriate empty-item Enter and beginning-of-item Backspace operations update the list structure and retain native focus. The parent document owns their undo.

RTF uses list definitions and overrides, with semantic metadata retaining the block graph. Nesting is limited to the codec's supported nine levels, and a document can export at most 2,000 list overrides. Custom marker strings, arbitrary prefix/suffix templates, picture bullets, and custom marker fonts are outside the authored list profile.
