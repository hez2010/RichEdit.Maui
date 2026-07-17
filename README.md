# RichEdit.Maui

`RichEdit.Maui` is a native-handler rich text editor for .NET MAUI. Its immutable `RichTextDocument` is the source of truth for text, character runs, paragraph formatting, lists, hyperlinks, fields, and inline images. The control exposes two-way `Document` and plain-text `Text` bindings and reads native edits and formatting back into the document model.

The editor renders with each platform's native text stack:

- Android: `AppCompatEditText` with `SpannableStringBuilder` spans
- iOS and Mac Catalyst: `UITextView` with `NSAttributedString`
- Windows: `RichEditBox` and `ITextDocument`

The model deliberately keeps formatting that a particular native stack cannot render exactly. For example, Android preserves justified alignment and advanced list metadata while using a simpler visual fallback. Moving the same document to another platform or saving it as RTF does not discard those values.

## Register the handler

```csharp
using RichEdit.Maui;

builder
    .UseMauiApp<App>()
    .UseRichEdit();
```

## Add the editor

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:rich="clr-namespace:RichEdit.Maui;assembly=RichEdit.Maui">
    <rich:RichEditor x:Name="Editor"
                     Text="{Binding PlainText, Mode=TwoWay}"
                     Placeholder="Start writing…"
                     FontSize="17"
                     MinimumHeightRequest="240" />
</ContentPage>
```

Formatting commands apply to the current selection. With a collapsed selection they update the typing style:

```csharp
Editor.ToggleBold();
Editor.ToggleItalic();
Editor.ToggleUnderline();
Editor.ToggleStrikethrough();
Editor.ToggleSuperscript();
Editor.ToggleSubscript();
Editor.ToggleBulletedList();
Editor.ToggleNumberedList();
Editor.UpdateSelectedCharacterFormat(format => format with
{
    FontFamily = "Georgia",
    FontSize = 20,
    ForegroundColor = Color.FromArgb("#E06C75"),
    BackgroundColor = Color.FromArgb("#FFF3A3"),
});
Editor.UpdateSelectedParagraphFormat(format => format with
{
    Alignment = RichTextAlignment.Center,
});
```

`SelectedCharacterFormat` and `SelectedParagraphFormat` are `null` when the selection contains mixed formatting. `TypingCharacterFormat` and `TypingParagraphFormat` always expose the format that will be used for newly inserted text.

## RTF persistence

The [Microsoft RTF 1.9.1 specification](https://go.microsoft.com/fwlink/?LinkId=120924) is the editor's canonical persistence format:

```csharp
var rtf = Editor.ToRtf();
Editor.LoadRtf(rtf);

var document = RichTextDocument.FromRtf(rtf);
var sameRtf = document.ToRtf();
```

The reader follows RTF group scoping, Unicode fallback, code-page, paragraph-default, and ignorable-destination rules. It accepts ANSI, Mac, PC 437, PC 850, and font-specific `\fcharset`/`\cpg` text. Table cells are flattened to tab/newline text rather than concatenated. `\line` is represented as U+2028, while `\par` is represented as `\n`, so soft and paragraph breaks remain distinct.

RTF uses opaque 8-bit RGB colors and half-point font sizes. Serialization rounds model values to those wire-format units. Nonzero alpha is degraded to opaque RGB; a fully transparent formatting color means unset/reset rather than an alpha-zero native color.

See the [portable RTF support matrix](RTF_SUPPORT.md) for the exact iOS,
Mac Catalyst, Android 26, and WinUI 3 behavior, including documented visual
fallbacks and model-only properties.

## Document model

Runs are normalized to cover the complete text without gaps or overlaps. Every paragraph has a format entry, semantic ranges cannot overlap, and every image occupies one U+FFFC object-replacement character. Input line endings are normalized to `\n`.

List labels are presentation metadata, not characters in `Text`. For example, plain `- foo` and `1. foo` lines remain plain text. Lists are created through the list APIs or imported RTF metadata:

```csharp
var document = new RichTextDocument(
    "First item\nFirst step",
    paragraphs:
    [
        new RichTextParagraph(
            0,
            RichTextParagraphFormat.Default with
            {
                List = new RichTextListFormat
                {
                    Id = 1,
                    Kind = RichListKind.Bulleted,
                },
            }),
        new RichTextParagraph(
            11,
            RichTextParagraphFormat.Default with
            {
                List = new RichTextListFormat
                {
                    Id = 2,
                    Kind = RichListKind.Numbered,
                },
            }),
    ]);
Editor.LoadDocument(document);
```

`Text` is the plain-text representation for ordinary two-way binding. Use `Document`, `ToRtf()`, or `LoadRtf()` when formatting must be persisted.
