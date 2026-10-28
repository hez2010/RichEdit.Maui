---
title: Character and paragraph formatting
description: Authored styles, inheritance, and native rendering.
---

# Character and paragraph formatting

Authored appearance uses platform-independent `RichCharacterStyle`, `RichParagraphStyle`, `RichColor`, `RichLength`, and `RichFontSize`. Colors are RGB. Lengths are stored in twips (1/20 point), and font sizes in half-points. View geometry uses device-independent display units.

Every cascading property is a `StyleValue<T>`:

| Value | Meaning |
| --- | --- |
| Default / `Inherit` | Use the preceding cascade value |
| Explicit value | Apply that value, including false, zero, or null where supported |
| `Initial` | Apply the schema's initial value |

```csharp
editor.ApplyCharacterFormat(new RichCharacterStyle
{
    Bold = true,
    Italic = false,
    Foreground = new RichColor(30, 60, 120),
    Underline = RichUnderline.Single,
});
```

At an empty text selection, character commands change the session's typing format. For selected content, they create an authored transaction. `ToggleBold`, `ToggleItalic`, and `ToggleUnderline` resolve current formatting before choosing the new value.

## Apply, replace, or clear direct formatting

Choose the operation according to whether existing overrides should survive:

| Operation | Effect |
| --- | --- |
| `editor.ApplyCharacterFormat(format)` | Overlays specified properties on selected content, or updates typing format at collapsed carets. |
| `edit.ApplyCharacterFormat(selection, format)` | Overlays selected content in a transaction; collapsed carets do not author content. |
| `edit.ApplyParagraphFormat(selection, format)` | Overlays every paragraph touched by the typed selection. |
| `edit.UpdateCharacterFormat` / `UpdateParagraphFormat` | Transforms each existing direct-format record; useful for clearing one override. |
| `edit.SetCharacterFormat` / `SetParagraphFormat` | Replaces the complete direct-format record at an explicit range or paragraph. |

An unspecified property in an **Apply** operation leaves its existing override unchanged. To restore inheritance for one property, use **Update**:

```csharp
editor.Edit(edit => edit.UpdateCharacterFormat(editor.Selection,
    current => current with { Foreground = StyleValue<RichColor?>.Inherit }));
```

To remove direct character overrides from selected content, return an empty record:

```csharp
editor.Edit(edit => edit.UpdateCharacterFormat(editor.Selection,
    _ => new RichCharacterStyle()));
// Reset pending overrides separately for subsequent typing:
editor.TypingCharacterFormat = new RichCharacterStyle();
```

These operations retain named style references. Consequently, clearing direct formatting can reveal bold or color inherited from a style. Setting `Bold = false` explicitly turns bold off; clearing its override restores inheritance. Selected paragraph ends include their character defaults.

## Paragraph formatting, spacing, and tabs

Selection-aware paragraph operations work with text, block, and cell selections and visit each selected paragraph once:

```csharp
editor.Edit(edit => edit.ApplyParagraphFormat(editor.Selection,
    new RichParagraphStyle
    {
        Alignment = RichAlignment.Justify,
        LeadingIndent = RichLength.FromPoints(24),
        FirstLineIndent = RichLength.FromPoints(-12),
        SpaceAfter = RichLength.FromPoints(8),
        LineSpacing = RichLineSpacing.Multiple(1.5),
        TabStops = new RichTabStops(new[]
        {
            new RichTabStop(RichLength.FromPoints(180),
                RichTabAlignment.Right, RichTabLeader.Dots),
        }),
    }), new RichEditOptions(Description: "Format paragraphs"));
```

`FirstLineIndent` is relative to `LeadingIndent`; a negative value creates a hanging indent. Tab stops have unique positive positions measured from the paragraph's leading edge. `RichLineSpacing` offers `Automatic`, `AtLeast`, `Exactly`, and `Multiple`; a fixed height can clip content on some native engines. Use `Direction` for paragraph flow and character direction for inline overrides.

`HeadingLevel`, `KeepWithNext`, `KeepTogether`, `WidowControl`, and `Hyphenation` retain document intent. This continuous editor does not provide paginated layout, so retained pagination properties do not produce page-layout behavior.

## Reusable styles

`RichStyleSheet` contains named paragraph and character definitions with `BasedOn` and `NextStyle` references. Inheritance must be acyclic. The cascade applies document defaults, paragraph styles and overrides, then inline character styles and direct overrides. `ResolveCharacter`, `ResolveParagraph`, and selected-format queries expose resolved values.

Changing a style affects all content that references it. Style definitions and assignments are authored inside a document transaction.

For a new document, define a body style and a heading whose next paragraph uses the body style:

```csharp
var document = RichDocument.FromPlainText("Document title");
var bodyStyle = new RichStyleId(1);
var headingStyle = new RichStyleId(2);
document.Edit(edit =>
{
    edit.SetStyles(new RichStyleSheet(new[]
    {
        new RichStyleDefinition(bodyStyle, "Body", RichStyleKind.Paragraph)
        {
            Character = new RichCharacterStyle { FontSize = RichFontSize.FromPoints(12) },
        },
        new RichStyleDefinition(headingStyle, "Heading", RichStyleKind.Paragraph)
        {
            BasedOn = bodyStyle,
            NextStyle = bodyStyle,
            Character = new RichCharacterStyle
            {
                Bold = true,
                FontSize = RichFontSize.FromPoints(20),
            },
            Paragraph = new RichParagraphStyle { HeadingLevel = 1, KeepWithNext = true },
        },
    }));
    var paragraph = edit.Snapshot.Get<RichStory>(edit.Body).Blocks[0];
    edit.SetParagraphStyle(paragraph, headingStyle);
});
editor.Document = document;
```

`SetStyles` replaces the whole stylesheet. For an existing document, carry forward definitions still referenced by content and allocate unused style IDs. Style IDs are document-local positive values up to 32,767; `BasedOn` must reference a style of the same kind. Use `SetCharacterStyle` to apply a `RichStyleKind.Character` definition to a paragraph-local range. `SetDefaultCharacterFormat` and `SetDefaultParagraphFormat` set authored document defaults below named styles in the cascade.

## Toolbar state

Read resolved selection values from the current snapshot rather than inspecting a single text run:

```csharp
var formats = editor.Document.CurrentSnapshot.GetSelectedCharacterFormats(editor.Selection);
bool? bold = formats.Length == 0 ? null
    : formats.All(format => format.Bold) ? true
    : formats.All(format => !format.Bold) ? false
    : null; // Mixed selection.
```

Refresh toolbar state on `SelectionChanged` and `ContentChanged`, including changes from another view. At collapsed carets, also account for `TypingCharacterFormat`: its explicit properties override resolved content values for future input. Observe the control's `PropertyChanged` for `TypingCharacterFormat` when a formatting action changes pending input without changing content or selection. `GetSelectedCharacterFormats` reports document formatting and does not include that session override. For paragraph toolbar state, use `GetSelectedText(selection)`, deduplicate paragraph IDs, and call `snapshot.ResolveParagraph(id)`.

## Supported appearance

Character support includes bold, italic, underline variants/colors, strike-through, foreground/highlight, shading, baseline/script placement, spacing, horizontal scaling, language, direction, capitals, outline, shadow, and hidden state. Paragraph support includes alignment, direction, indents, before/after spacing, line spacing, tabs/leaders, headings, keep/widow settings, hyphenation, shading, and uniform borders.

`FontFamily`, `FontSize`, and `TextColor` on the view are fallback appearance and do not change saved content. Presentation layers compose afterward. Native engines can approximate some retained properties. Explicit colors in the document keep their authored values when the application theme changes.

Body text wraps within the editor's available width, accounting for paragraph indents. Table text uses the cell's width. Alignment does not change font size: each paragraph's text keeps its resolved character formatting.
