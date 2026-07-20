# RichEdit.Maui

`RichEdit.Maui` is a native-handler rich-text editor for .NET MAUI. It combines a bindable editor control, a stable live document, immutable versioned snapshots, and atomic range edits.

The editor uses each platform's native text stack:

- Android 26+: `AppCompatEditText` and editable spans
- iOS and Mac Catalyst: `UITextView`, `NSTextStorage`, and attributed strings
- Windows: WinUI 3 `RichEditBox` and the Text Object Model

Formatting that cannot be rendered exactly on a platform remains in the document and its RTF representation. See the [portable RTF support matrix](RTF_SUPPORT.md) for the exact native, adapted, degraded, preserved, and unsupported behavior.

## Register the handler

```csharp
using RichEdit.Maui;

builder
    .UseMauiApp<App>()
    .UseRichEdit();
```

## Own editor content

Bind one stable `RichTextDocument` to the control. The editor deliberately has no
`Text` or `RtfText` content properties, so native input and application code cannot
compete through projection bindings:

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:rich="clr-namespace:RichEdit.Maui;assembly=RichEdit.Maui">
    <rich:RichEditor x:Name="Editor"
                     Document="{Binding Document}"
                     SelectedRange="{Binding Selection, Mode=TwoWay}"
                     IsReadOnly="{Binding IsReadOnly}"
                     IsSpellCheckEnabled="True"
                     Placeholder="Start writing…"
                     FontSize="17"
                     MinimumHeightRequest="240" />
</ContentPage>
```

Create an initial document from RTF and assign it as a whole. Read-only `Text` and
`RtfText` projections remain on the document for inspection and persistence:

```csharp
Editor.Document = RichTextDocument.FromRtf(rtfText);

var plainText = Editor.Document.Text;
var canonicalRtf = Editor.Document.RtfText;
```

After construction, change content only through `Document.Edit(...)` or selection
operations. RTF serialization is lazy and cached by document version.

Control appearance properties such as `FontFamily`, `FontSize`, and `TextColor` are rendering fallbacks. They are not authored document formatting and are never serialized into `RtfText`.

## Selection and formatting

`Selection` is a stable live facade. Formatting a nonempty selection updates only that range; formatting a caret changes the typing format:

```csharp
Editor.Selection.ToggleBold();
Editor.Selection.ToggleItalic();
Editor.Selection.ToggleUnderline(RichTextUnderlineStyle.Single);
Editor.Selection.ToggleStrikethrough(RichTextStrikethroughStyle.Single);
Editor.Selection.ToggleScript(RichTextScript.Superscript);

Editor.Selection.CharacterFormat.FontFamily = "Georgia";
Editor.Selection.CharacterFormat.FontSize = 20;
Editor.Selection.CharacterFormat.ForegroundColor = Color.FromArgb("#E06C75");
Editor.Selection.CharacterFormat.BackgroundColor = Color.FromArgb("#FFF3A3");
Editor.Selection.ParagraphFormat.Alignment = RichTextAlignment.Center;
```

Each selection-format property has a corresponding mixed-state property, such as `IsFontFamilyMixed`. Authored values remain distinct from effective rendering values:

```csharp
var authoredFont = Editor.Selection.CharacterFormat.FontFamily;
var visibleFont = Editor.Selection.CharacterFormat.EffectiveFontFamily;
var isInherited = Editor.Selection.CharacterFormat.IsFontFamilyInherited;

// Remove explicit run formatting and resume document/view/native inheritance.
Editor.Selection.CharacterFormat.FontFamily = null;
```

The same selection facade provides range replacement, links, fields, images, and list operations.

## Caller-defined lists

The library does not choose a bullet glyph, number style, prefix, suffix, start value, or indentation. Define the complete list in application code:

```csharp
var checklist = new RichTextListDefinition(
[
    new RichTextListLevelDefinition
    {
        Marker = new RichTextListMarker.Bullet("✓"),
        Prefix = string.Empty,
        Suffix = string.Empty,
        LeadingIndent = 24,
        FirstLineIndent = -18,
        MarkerTab = 24,
    },
]);

var outline = new RichTextListDefinition(
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
]);

Editor.Selection.ToggleList(checklist);
Editor.Selection.ToggleList(outline);
```

Document transactions can create one list identity and apply it to several ranges without duplicating its definition:

```csharp
Editor.Document.Edit(edit =>
{
    var listId = edit.CreateList(outline);
    edit.ApplyList(new RichTextRange(0, 10), listId);
    edit.ApplyList(new RichTextRange(30, 15), listId);
});
```

Lists support up to nine independently defined levels, caller-selected bullet text or pictures, numbering style and start value, prefixes and suffixes, nesting, continuation, and explicit restarts.

## Atomic incremental document edits

`RichTextDocument` keeps a stable identity and emits one `Changed` event for each committed transaction:

```csharp
Editor.Document.Edit(
    edit =>
    {
        edit.ReplaceText(new RichTextRange(20, 5), "replacement");
        edit.UpdateCharacterFormat(
            new RichTextRange(20, 11),
            format => format with { FontWeight = 700 });
        edit.SetLink(new RichTextRange(20, 11), "https://example.com");
    },
    new RichTextEditOptions(
        undoBehavior: RichTextUndoBehavior.CreateUnit,
        undoDescription: "Replace title"));
```

The transaction produces one version change, native batch, undo unit, and change notification. The edit builder supports:

- insert, delete, and replace text or rich fragments
- set, update, or clear character and paragraph formats
- document default formats
- list definitions, application, nesting, restart, and picture markers
- links, fields, images, and metadata

`CurrentSnapshot` exposes an immutable view of the current version for safe enumeration. Range-bearing values use `RichTextRange`, whose offsets and lengths are UTF-16 code units.

## MVVM commands

`Commands` exposes `ICommand` instances with editor-aware `CanExecute` state:

```xml
<Button Text="B"
        Command="{Binding Source={x:Reference Editor}, Path=Commands.ToggleBold}" />

<Button Text="Underline"
        Command="{Binding Source={x:Reference Editor}, Path=Commands.ToggleUnderline}"
        CommandParameter="{x:Static rich:RichTextUnderlineStyle.Single}" />
```

Parameterized commands accept `RichTextListCommandRequest`, `RichTextLinkRequest`, `RichTextFieldRequest`, and image values. Advanced toolbars can bind directly to `Selection.CharacterFormat` and `Selection.ParagraphFormat`.

## Events and editor operations

The control exposes `ContentChanged`, `TextChanged`, `SelectionChanged`, `SelectionFormatChanged`, `EffectiveAppearanceChanged`, `LinkInvoked`, `InlineObjectInvoked`, `Pasting`, and `Completed` events. `ContentChanged` includes the atomic `RichTextChangeSet`, including its origin, old and new versions, and bounded changes.

Undo, redo, selection, and portable clipboard operations are available directly:

```csharp
Editor.Undo();
Editor.Redo();
Editor.SelectAll();
await Editor.CopyAsync();
await Editor.CutAsync();
await Editor.PasteAsync();
```

## RTF behavior

The reader follows RTF group scoping, Unicode fallback, code-page, paragraph-default, and ignorable-destination rules. It accepts ANSI, Mac, PC 437, PC 850, and font-specific `\fcharset`/`\cpg` text. Table cells are flattened to tab/newline text. `\line` is represented as U+2028 and `\par` as `\n`, so soft and paragraph breaks remain distinct.

RTF uses opaque 8-bit RGB colors and half-point font sizes. Serialization rounds model values to those wire-format units. Nonzero alpha is degraded to opaque RGB; a fully transparent formatting color means unset/reset instead of an alpha-zero native color.
