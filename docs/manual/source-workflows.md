---
title: Source editing recipes
description: Implement source navigation, search, multiple selections, line-ending conversion, and file saving.
---

# Source editing recipes

`CodeEditor` provides editing primitives. The application owns search UI, file dialogs, encoding policy, and language services. These recipes use `CodeEdit.Maui` and `TextEdit.Core`; run control and document operations on their owner thread.

## Go to a line and column

UI line numbers commonly start at one, while the source APIs use zero-based UTF-16 coordinates. Clamp the requested column to the logical line, excluding its terminator:

```csharp
static void GoToLine(CodeEditor code, int lineNumber, int columnNumber = 1)
{
    ArgumentOutOfRangeException.ThrowIfLessThan(lineNumber, 1);
    ArgumentOutOfRangeException.ThrowIfLessThan(columnNumber, 1);
    int line = Math.Min(lineNumber, code.LineCount) - 1;
    var range = code.GetLineRange(line);
    int offset = range.Start + Math.Min(columnNumber - 1, range.Length);

    code.SelectedRange = new TextSpan(offset, 0);
    code.ScrollIntoView(code.SelectedRange);
    code.Focus();
}
```

A column counts UTF-16 units, not graphemes or visual tab cells. For visual navigation, use native [layout and hit testing](layout.md). `RevealAsync` controls viewport alignment without moving selection or focus.

## Find and replace literal text

Compute matches against one immutable snapshot and publish all replacements against that snapshot's revision. The following small-document recipe performs case-sensitive, nonoverlapping literal replacement in one undo entry:

```csharp
static bool ReplaceAll(CodeEditor code, string query, string replacement)
{
    ArgumentException.ThrowIfNullOrEmpty(query);
    ArgumentNullException.ThrowIfNull(replacement);
    var snapshot = code.Document.CurrentSnapshot;
    string text = snapshot.Text;
    var edits = new List<TextReplacement>();

    for (int offset = 0; offset <= text.Length - query.Length;)
    {
        int match = text.IndexOf(query, offset, StringComparison.Ordinal);
        if (match < 0) break;
        edits.Add(new TextReplacement(new TextSpan(match, query.Length), replacement));
        offset = match + query.Length;
    }

    return edits.Count > 0 && code.TryApplyEdits(snapshot.Revision, edits,
        options: new TextEditOptions(Description: "Replace all"));
}
```

`TryApplyEdits` respects the view's read-only state, composition, length limit, and document revision. A false return must not trigger an unchecked retry at the same offsets. This recipe also returns false when there are no matches. For a find-only command, assign the matched `TextSpan` to `SelectedRange` and reveal it.

The example materializes the source with `snapshot.Text`. For large files, use `snapshot.TextBuffer.GetChunks(range)` or bounded `GetText` reads and account for matches crossing chunk boundaries. Background search must return to the owner dispatcher before publishing; see [checked edits](background-work.md). Case folding, whole-word rules, regular expressions, and search wrapping are application policies.

## Edit several ranges together

Construct a `TextSelectionSet` from match ranges when the user should type into all matches. Its primary index addresses the supplied list before normalization:

```csharp
var code = new CodeEditor { Document = new CodeDocument("name = name;") };
code.Selections = new TextSelectionSet(new[]
{
    new TextSelection(0, 4),
    new TextSelection(7, 11),
}, primaryIndex: 1);

code.ReplaceSelections("value"); // value = value;
code.ClearSecondarySelections();
```

Duplicate carets and overlapping or touching ranges normalize into one selection. Selection IDs and direction are retained where possible; the primary selection controls native input. `SelectionsChanged` observes changes to secondary selections as well as the primary.

For a virtual caret beyond the end of a line, keep its real offset at that logical line end and specify unmaterialized spaces:

```csharp
var line = code.GetLineRange(0);
code.Selections = new TextSelectionSet(new[]
{
    new TextSelection(line.End, line.End)
    {
        AnchorVirtualSpaces = 4,
        ActiveVirtualSpaces = 4,
    },
});
code.ReplaceSelections("// note");
```

The spaces and inserted text commit together. Undo restores the prior virtual selection. Virtual space is valid only beyond a logical line end, not a soft wrap; `SelectRectangle` and Alt-pointer selection use native geometry for rectangular editing.

## Preserve or convert line endings

`code.NewLine` controls future Enter input and accepts only `"\n"`, `"\r\n"`, or `"\r"`. It leaves existing text unchanged. `CodeDocument` preserves mixed terminators, and `snapshot.GetLine(index)` exposes both content and terminator lengths.

An explicit conversion is one document transaction:

```csharp
if (!code.IsReadOnly && code.TryCompleteComposition())
{
    code.Document.Edit(edit => edit.NormalizeLineEndings("\n"),
        new TextEditOptions(Description: "Convert to LF"));
    code.NewLine = "\n";
}
```

The guard supplies the view's read-only policy because this recipe uses a direct document edit. Conversion can change offsets; reacquire search results and line ranges afterward. An LF-only view of text is not interchangeable with original CRLF offsets.

## Save source text

Capture after composition completes, write the captured snapshot, and acknowledge that capture only after the destination is committed. `TextSnapshot.WriteTo(TextWriter)` streams source chunks without building one large string:

```csharp
static async Task<bool> SaveSourceAsync(CodeEditor code, string destination,
    CancellationToken cancellationToken)
{
    if (!code.TryCompleteComposition()) return false;
    var document = code.Document;
    var capture = document.CaptureSave();
    string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";

    try
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var writer = new StreamWriter(temporary, append: false,
                encoding: new System.Text.UTF8Encoding(false, true));
            capture.Snapshot.WriteTo(writer);
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(temporary, destination, overwrite: true);
        document.MarkSaved(capture);
        return true;
    }
    finally
    {
        if (File.Exists(temporary)) File.Delete(temporary);
    }
}
```

Call this method from the UI thread so acknowledgement resumes on the owner context. It uses UTF-8 without a BOM and rejects invalid surrogate encoding. Adapt file replacement to your storage provider's durability and atomicity requirements. The chunk writer itself has no cancellation parameter; cancellation here is checked before writing and before replacement. Surface I/O errors in application UI and serialize saves targeting the same file. Edits made during writing remain modified because `MarkSaved` acknowledges the captured state.

For opening a text file, decode it with your encoding policy, construct a `CodeDocument` on the UI thread, and assign it after resolving composition. Encoding detection, BOM preservation, and file-system watching belong to the application; the document preserves decoded UTF-16 text and exact line endings. See [History and saving](history-and-saving.md) for saved-state behavior.
