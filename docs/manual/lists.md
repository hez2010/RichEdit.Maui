---
title: Lists and numbering
description: Define bullets and numbered lists, reuse document list identities, and control nesting and restart behavior.
---

# Lists and numbering

Lists are paragraph metadata. Bullets and numbers are drawn separately and occupy no positions in `Document.Text`.

## Define a numbered list

A <xref:RichEdit.Maui.RichTextListDefinition> contains one to nine levels. Each level supplies its marker, prefix, suffix, and layout:

```csharp
RichTextListLevelDefinition[] levels =
[
    new RichTextListLevelDefinition
    {
        Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1),
        Prefix = string.Empty,
        Suffix = ".",
        LeadingIndent = 24,
        FirstLineIndent = -18,
        MarkerTab = 24,
    },
];

var numbered = new RichTextListDefinition(levels);
editor.Selection.ToggleList(numbered);
```

`ToggleList` removes the list when all selected paragraphs already use an equivalent definition at the requested level. `SetList` always applies it. An empty selection targets the caret's paragraph.

`LeadingIndent` and `MarkerTab` are in points. A negative `FirstLineIndent` places the marker to the left of wrapped content. Each level can have its own layout.

## Use bullets or other number styles

Replace the marker in a level with one of these values:

```csharp
var bullet = new RichTextListMarker.Bullet("•");
var letters = new RichTextListMarker.Number(RichTextListNumberStyle.LowerLetter, 1);
var roman = new RichTextListMarker.Number(RichTextListNumberStyle.UpperRoman, 1);
```

Numbered lists also support lower Roman and upper letter sequences. Start values must be positive; bullet text must be nonempty.

Picture markers use `RichTextListMarker.Picture` with a document-owned `RichTextListPicture` and fallback text. Add the picture using `SetListPicture` before applying a definition that refers to it. Apple uses the [fallback text](../../RTF_SUPPORT.md#lists).

## Continue the same list across ranges

Create one list identity and reuse it when separate paragraph ranges should belong to the same list:

```csharp
var document = RichTextDocument.FromPlainText("One\nNote\nTwo\n");
document.Edit(edit =>
{
    var id = edit.CreateList(numbered);
    edit.ApplyList(new RichTextRange(0, 3), id);
    edit.ApplyList(new RichTextRange(9, 3), id);
});
```

Reusing the identity continues numbering across the intervening paragraph. A new identity starts a separate list, even with the same definition. `CurrentSnapshot.Lists` holds the definitions by document-local ID; `UpdateList` changes one of them.

## Change nesting and restarts

```csharp
editor.Selection.ChangeListLevel(1);  // Indent.
editor.Selection.ChangeListLevel(-1); // Outdent.
editor.Selection.RestartList(1);
editor.Selection.ClearList();
```

Outdenting above level zero removes list state. Indenting beyond the definition repeats the last marker style and layout progression, up to nine levels. Each level has an independent counter; advancing an outer level does not reset a nested counter.

Document transactions provide `ChangeListLevel`, `RestartList`, and `RemoveList` for explicit ranges.
