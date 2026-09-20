---
title: Getting started
description: Install RichEdit.Maui or CodeEdit.Maui, register the native handler, and create your first editor page.
---

# Getting started

The libraries target .NET 10 and require a MAUI project with the workloads for your target platforms.

| Platform | Minimum supported version |
| --- | --- |
| Android | API 26 |
| iOS | 15.0 |
| Mac Catalyst | 15.0 |
| Windows | Windows 10 1809, build 17763 |

The Windows target framework is `net10.0-windows10.0.19041.0`, with a minimum OS version of 17763.

## Install the package

Run this in your MAUI project directory:

```sh
dotnet add package RichEdit.Maui --prerelease
```

For code editing, install the code package instead. It brings in the matching RichEdit.Maui dependency:

```sh
dotnet add package CodeEdit.Maui --prerelease
```

## Register the handler

Add `UseRichEdit()` to the existing builder in `MauiProgram.CreateMauiApp`, before `Build()`:

```csharp
using RichEdit.Maui;

builder.UseRichEdit();
```

`UseCodeEditor()` registers both controls:

```csharp
using CodeEdit.Maui;

builder.UseCodeEditor();
```

## Create a rich-text page

```csharp
using Microsoft.Maui.Controls;
using RichEdit.Maui;

public sealed class EditorPage : ContentPage
{
    public EditorPage()
    {
        var editor = new RichEditor
        {
            Document = RichTextDocument.FromPlainText("Hello, world!\n"),
            Placeholder = "Start writing…",
            FontSize = 17,
            MinimumHeightRequest = 240,
        };

        var undo = new Button { Text = "Undo", Command = editor.Commands.Undo };
        var bold = new Button { Text = "Bold" };
        bold.Clicked += (_, _) => editor.Selection.ToggleBold();

        var layout = new Grid
        {
            Padding = 16,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        layout.Add(new HorizontalStackLayout { Children = { undo, bold } });
        layout.Add(editor, 0, 1);
        Content = layout;
    }
}
```

The star-sized Grid row gives the editor a bounded viewport for native scrolling. Avoid placing it in a vertical `ScrollView` that measures its content with unlimited height.

With no text selected, the Bold button changes the format of subsequent typing.

## Create a code editor

```csharp
using CodeEdit.Maui;

var editor = new CodeEditor
{
    Document = CodeDocument.FromPlainText("public class Example\n{\n}\n"),
    FontSize = 14,
    ShowLineNumbers = true,
    WordWrap = false,
    IndentSize = 4,
    UseTabs = false,
};
```
