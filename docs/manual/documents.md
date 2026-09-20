---
title: Documents and snapshots
description: Load text and RTF, inspect immutable snapshots, and understand source offsets and document ownership.
---

# Documents and snapshots

<xref:RichEdit.Maui.RichTextDocument> holds the text, formatting, history, and saved state. Native typing and programmatic edits update the same instance.

## Load and export content

```csharp
var plain = RichTextDocument.FromPlainText("First line\r\nSecond line");
var rich = RichTextDocument.FromRtf(@"{\rtf1\ansi Hello, \b world\b0 !}");

editor.Document = rich;
string text = rich.Text;
string rtf = rich.RtfText;
```

`FromPlainText(null)` creates an empty document. `FromRtf` throws `FormatException` for invalid RTF.

New documents start with empty history and `IsModified == false`. Assign a new document when opening a file; use `Document.Edit` to insert content into the current document with undo support.

`RtfText` serializes the supported content into canonical RTF, which can differ from the imported file. Serialization is lazy and cached.

## Offsets and line breaks

All positions are zero-based UTF-16 offsets into normalized `Text`. A <xref:RichEdit.Maui.RichTextRange> describes `[Start, End)`, with `End = Start + Length`.

```csharp
var document = RichTextDocument.FromPlainText("A😀B\r\nC");
// Text is "A😀B\nC", Length is 6.
var emoji = new RichTextRange(1, 2);
string selected = document.GetText(emoji); // "😀"
```

The emoji occupies two UTF-16 code units, so its range has length 2. Offsets refer to the normalized text, after CRLF and CR have become LF.

| Source value | Meaning |
| --- | --- |
| `\n` | Paragraph break; CRLF and CR input are normalized to LF |
| `\u2028` | Soft line break within a paragraph |
| `\uFFFC` | Logical placeholder for an inline image |
| Empty range at `Length` | Caret at the end of the document |

Document edit methods reject out-of-bounds ranges. Assigning an editor's selection clamps its endpoints to the document length.

## Snapshots

Capture `CurrentSnapshot` on the UI thread before starting background work:

```csharp
var snapshot = editor.Document.CurrentSnapshot;
string capturedText = snapshot.Text;

foreach (var run in snapshot.Runs)
{
    string runText = capturedText.Substring(run.Range.Start, run.Range.Length);
    System.Diagnostics.Debug.WriteLine($"{runText}: {run.Format.FontWeight}");
}
```

Snapshots retain their content after subsequent edits. Besides text and format runs, they expose paragraphs, links, fields, images, list definitions, document defaults, and metadata.

`Version` increases on changes, including undo and redo. `Revision` combines that version with document identity and is the token accepted by `TryApplyEdits` and decoration layers.

## Source documents

<xref:CodeEdit.Maui.CodeDocument> exposes text-only editing with the same history and snapshot model. Its <xref:CodeEdit.Maui.CodeDocumentSnapshot> also converts between offsets and line/column positions:

```csharp
var source = CodeDocument.FromPlainText("first\nsecond\n");
var snapshot = source.CurrentSnapshot;
var secondLine = snapshot.GetLineRange(2);
var position = snapshot.GetPosition(secondLine.Start); // Line 2, Column 1
int offset = snapshot.GetOffset(new CodePosition(2, 4));
```

Line and column numbers are one-based; offsets remain zero-based UTF-16. A trailing LF creates a final empty logical line.
