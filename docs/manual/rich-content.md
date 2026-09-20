---
title: Links, fields, images, and fragments
description: Insert links, fields, images, and rich fragments, and store document metadata.
---

# Links, fields, images, and fragments

## Add a hyperlink

```csharp
editor.Document = RichTextDocument.FromPlainText("Project website");
editor.SelectedRange = new RichTextRange(0, editor.Document.Length);
editor.Selection.SetLink("https://github.com/hez2010/RichEdit.Maui", "Source repository");
```

`SetLink` requires a nonempty selection. `RemoveLinks()` removes intersecting links while retaining their display text. The document edit builder exposes both operations for explicit ranges.

`LinkInvoked` reports the link's `Range`, `Target`, and `ToolTip`. Handle it to open a URL or navigate within your app, then set `Handled`.

## Insert and update a field

Fields retain an instruction and visible result. The editor does not evaluate the instruction:

```csharp
editor.Selection.InsertField("DATE", "20 September 2026");
```

Use a document-local identity when you plan to update the same field later:

```csharp
var document = RichTextDocument.FromPlainText("Date: ");
RichTextFieldId fieldId = default;
document.Edit(edit =>
    fieldId = edit.InsertField(document.Length, "DATE", "20 September 2026"));

document.Edit(edit =>
    edit.UpdateField(fieldId, "DATE", "21 September 2026"));
```

Snapshot `Fields` entries expose field identities and current ranges. `RemoveField` removes the field instruction while retaining its result text; delete that range separately to remove the text too.

## Insert an image

```csharp
static void InsertPng(RichEditor editor, byte[] pngBytes)
{
    var image = RichTextImage.FromBytes(
        position: editor.SelectedRange.Start,
        mediaType: "image/png",
        data: pngBytes,
        width: 160,
        height: 90,
        alternativeText: "Diagram of the document workflow");

    editor.Selection.InsertImage(image);
}
```

The image replaces the selection and occupies one U+FFFC character. `FromBytes` copies the encoded bytes; width and height are in points. PNG and JPEG render on all supported platforms.

`Source` is an optional identifier and is not saved to RTF; the encoded bytes carry the image. Crop, rotation, and other image formats have [platform-specific behavior](../../RTF_SUPPORT.md).

`InlineObjectInvoked` reports the activated image, for example when opening an image inspector.

## Insert a rich fragment

<xref:RichEdit.Maui.RichTextDocumentFragment> holds immutable content for insertion or clipboard transfer:

```csharp
var fragment = RichTextDocumentFragment.FromRtf(
    @"{\rtf1\ansi A \b formatted\b0 fragment.}");
editor.Selection.ReplaceFragment(fragment);
```

`FromPlainText` creates a fragment that inherits the destination typing format. `Document.Edit` also supports `ReplaceFragment` for an explicit range, preserving the existing document and its history.

## Store application metadata

```csharp
editor.Document.Edit(edit => edit.SetMetadata("review-status", "draft"));
string status = editor.Document.CurrentSnapshot.Metadata["review-status"];
```

Set a value to `null` to remove it. Metadata participates in history but is not serialized to RTF. Save it separately if it needs to survive reopening the file.
