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

## Reusable styles

`RichStyleSheet` contains named paragraph and character definitions with `BasedOn` and `NextStyle` references. Inheritance must be acyclic. The cascade applies document defaults, paragraph styles and overrides, then inline character styles and direct overrides. `ResolveCharacter`, `ResolveParagraph`, and selected-format queries expose resolved values.

Use `SetStyles`, `SetParagraphStyle`, `SetCharacterStyle`, `SetCharacterFormat`, and `SetParagraphFormat` inside a rich edit. Set-format operations replace the specified direct record; preserve other direct properties explicitly when modifying only part of it. Changing a style affects all content that references it.

Character support includes bold, italic, underline variants/colors, strike-through, foreground/highlight, shading, baseline/script placement, spacing, horizontal scaling, language, direction, capitals, outline, shadow, and hidden state. Paragraph support includes alignment, direction, indents, before/after spacing, line spacing, tabs/leaders, headings, keep/widow settings, hyphenation, shading, and uniform borders.

`FontFamily`, `FontSize`, and `TextColor` on the view are fallback appearance and do not change saved content. Presentation layers compose afterward. Native engines can approximate some retained properties; see [RTF support](../../RTF_SUPPORT.md).

For a light/dark app theme, bind the editor's fallback text color and background together. Explicit colors in the document keep their authored values.

```xml
<rich:RichEditor
    TextColor="{AppThemeBinding Light=Black, Dark=White}"
    BackgroundColor="{AppThemeBinding Light=White, Dark=#192933}" />
```

Body text wraps within the editor's available width, accounting for paragraph indents. Table text uses the cell's width. Alignment does not change font size: each paragraph's text keeps its resolved character formatting.
