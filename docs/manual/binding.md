---
title: XAML and data binding
description: Bind native controls to shared documents and typed selection state.
---

# XAML and data binding

Bind the rich control to a `RichEdit.Core.RichDocument` property. The document is a long-lived model; the control creates and owns its view session.

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:rich="clr-namespace:RichEdit.Maui;assembly=RichEdit.Maui">
    <Grid RowDefinitions="Auto,*">
        <Button Text="Undo"
                Command="{Binding Source={x:Reference Editor}, Path=Commands.Undo}" />
        <rich:RichEditor x:Name="Editor" Grid.Row="1"
                         Document="{Binding Document}"
                         Selection="{Binding Selection, Mode=TwoWay}"
                         FontSize="18" />
    </Grid>
</ContentPage>
```

The view model's selection property is `RichSelection`. It can contain a text selection set, object, cell, block, or boundary selection. Use `GetSelectedCharacterFormats` to compute application toolbar mixed-state summaries. View fallback font/color properties do not author formatting.

For source editing, bind `CodeEditor.Document` to `CodeDocument` and `Selections` to `TextSelectionSet`. Primary `SelectedRange` and `SelectionState` conveniences remain available.

Sharing the same document between two controls provides split views with independent selection and presentation. Replacing one control's document leaves the other view attached to the original document and history. Replacing a document clears that view's transient presentation producers and invalidates pending results.
