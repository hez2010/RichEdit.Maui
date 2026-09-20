---
title: Character and paragraph formatting
description: Apply rich formatting, show mixed and inherited selection values, and separate document styles from display defaults.
---

# Character and paragraph formatting

Use `Selection.CharacterFormat` and `Selection.ParagraphFormat` for formatting controls. Use a document transaction to format a known range independently of the active selection.

## Format the selection

```csharp
editor.Document = RichTextDocument.FromPlainText("A heading\nA paragraph.\n");
editor.SelectedRange = new RichTextRange(0, 9);
editor.Selection.ToggleBold();
editor.Selection.ToggleItalic();
editor.Selection.ToggleUnderline(RichTextUnderlineStyle.Single);

editor.Selection.CharacterFormat.FontFamily = "Georgia";
editor.Selection.CharacterFormat.FontSize = 20;
editor.Selection.CharacterFormat.ForegroundColor = Colors.DarkBlue;
editor.Selection.ParagraphFormat.Alignment = RichTextAlignment.Center;
```

At an empty selection, character operations change the typing format for subsequent text. Paragraph operations affect intersecting paragraphs, or the caret's paragraph when the selection is empty.

Toggles enable a style if the selection is not uniformly using it; otherwise they disable it. `ToggleScript` supports superscript and subscript, and `ToggleStrikethrough` accepts a chosen strike style.

## Preserve unrelated formatting

`SetCharacterFormat` replaces the format over a range. `UpdateCharacterFormat` transforms each existing run, which preserves properties you do not change:

```csharp
var document = editor.Document;
document.Edit(edit =>
{
    edit.UpdateCharacterFormat(new RichTextRange(0, document.Length),
        format => format with { ForegroundColor = Colors.DarkSlateBlue });
    edit.UpdateParagraphFormat(new RichTextRange(0, document.Length),
        format => format with { SpaceAfter = 8 });
}, new RichTextEditOptions(undoDescription: "Style document"));
```

`SetCharacterFormats` accepts an ordered, non-overlapping batch of runs. For temporary syntax colors or diagnostics, use [decorations](decorations.md).

## Mixed and inherited values

Font family, font size, and foreground color can be inherited. Their selection properties distinguish declared values from the values actually displayed:

| Property example | Meaning |
| --- | --- |
| `FontFamily` | Declared value, or null for inherited or mixed values |
| `IsFontFamilyMixed` | The selected content does not share one declared value |
| `IsFontFamilyInherited` | The selected content inherits this property |
| `EffectiveFontFamily` | Value after resolving document and editor defaults |
| `IsEffectiveFontFamilyMixed` | Effective values differ within the selection |

Assign `null` to `FontFamily`, `FontSize`, or `ForegroundColor` to restore inheritance. A font picker can display `EffectiveFontFamily` unless `IsEffectiveFontFamilyMixed` is true. Use `IsFontFamilyInherited` when you also want to distinguish inherited values from explicit formatting.

To inspect formatting without changing selection, use `Document.GetCharacterFormat(range)` or `GetParagraphFormat(range)`. Their summary objects report values across the requested range.

## Document and display defaults

Control properties such as `editor.FontFamily`, `FontSize`, and `TextColor` provide display defaults for inherited text. They are not saved in the document.

Set a document default when the format should be saved:

```csharp
editor.Document.Edit(edit => edit.SetDefaultCharacterFormat(
    new RichTextCharacterFormat { FontFamily = "Georgia", FontSize = 12 }));
```

`ClearCharacterFormat` resets a range to the document default, leaving font family, size, and foreground color inherited. `ClearParagraphFormat` resets affected paragraphs to the current default paragraph format.

## Units and rendering

Document font sizes, indents, and paragraph spacing use points. RTF stores font sizes in half-point increments and colors as opaque 8-bit RGB. Export rounds to those units; fully transparent formatting colors mean reset.

Rendering of advanced formats such as underline styles, borders, and character scale varies by platform, as recorded in the [RTF support matrix](../../RTF_SUPPORT.md).
