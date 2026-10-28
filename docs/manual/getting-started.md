---
title: Getting started
description: Install the packages and create native rich and source editors.
---

# Getting started

Use .NET 10 and the MAUI workload for each target. Minimum supported targets are Android API 26, iOS 15, Mac Catalyst 15, and Windows 10.0.17763.

```sh
dotnet add package RichEdit.Maui --version 0.5.0-preview.3
dotnet add package CodeEdit.Maui --version 0.5.0-preview.3
```

`RichEdit.Maui` includes the `RichEdit.Core` and `TextEdit.Core` assemblies. `CodeEdit.Maui` depends on `RichEdit.Maui`, so installing either editor brings in the required document types without separate core packages. Register the controls in `MauiProgram`:

```csharp
using RichEdit.Maui;
using CodeEdit.Maui;

builder.UseRichEdit();
builder.UseCodeEditor();
```

Create rich content using the structured model:

```csharp
using RichEdit.Core;
using RichEdit.Maui;

var document = RichDocument.FromPlainText("Hello, world!");
var editor = new RichEditor { Document = document, FontSize = 18 };
editor.SelectAll();
editor.ApplyCharacterFormat(new RichCharacterStyle { Bold = true });
```

A source editor uses a separate plain-text model:

```csharp
using CodeEdit.Maui;

var code = new CodeEditor
{
    Document = new CodeDocument("class Example\r\n{\r\n}\r\n"),
    ShowLineNumbers = true,
    WordWrap = false,
    IndentSize = 4,
    TabSize = 4,
};
```

Give the editor a bounded layout area, such as a star-sized Grid row. Native scrolling and lazy widget realization depend on a finite viewport. Use [RTF streams](documents.md) to load rich documents and [binding](binding.md) to connect toolbar state.
