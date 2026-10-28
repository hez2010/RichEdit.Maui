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

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:code="clr-namespace:CodeEdit.Maui;assembly=CodeEdit.Maui">
    <Grid RowDefinitions="Auto,*">
        <Button Text="Redo"
                Command="{Binding Source={x:Reference SourceEditor}, Path=Commands.Redo}" />
        <code:CodeEditor x:Name="SourceEditor" Grid.Row="1"
                         Document="{Binding SourceDocument}"
                         Selections="{Binding SourceSelections, Mode=TwoWay}"
                         ShowLineNumbers="True" WordWrap="False" />
    </Grid>
</ContentPage>
```

Initialize document and selection properties to valid non-null values before binding. Notify property changes when the view model replaces a document or selection. A rich selection must refer to nodes in its bound document; source selections must fit the source length. Avoid simultaneously driving `Selections`, `SelectionState`, and `SelectedRange` from independent view-model properties, since all affect the same selection state.

## Split views

Sharing the same document between two controls provides split views with independent selection and presentation. Replacing one control's document leaves the other view attached to the original document and history. Replacing a document clears that view's transient presentation producers and invalidates pending results.

```csharp
var document = RichEdit.Core.RichDocument.FromPlainText("Shared document");
var editingPane = new RichEdit.Maui.RichEditor { Document = document };
var previewPane = new RichEdit.Maui.RichEditor
{
    Document = document,
    IsReadOnly = true,
};
```

Use separate selection properties for each pane. Binding both panes to one selection property intentionally synchronizes their selections. View fonts, read-only state, presentation producers, and scroll positions remain independent. For linked source scrolling, see [Layout](layout.md).

## Dirty state and toolbar updates

Both document models expose `IsModified`, `CanUndo`, `CanRedo`, `UndoDescription`, and `RedoDescription`. `CodeDocument` implements `INotifyPropertyChanged`, so source dirty state can bind to `code.Document.IsModified`. `RichDocument` uses `StateChanged` instead; forward that event into view-model property notifications, or bind to `RichEditor.IsModified`, `CanUndo`, and `CanRedo`, which the control notifies. Save completion and edits from any pane can change these values without a local selection change.

Use `SelectionChanged` and `ContentChanged` to refresh [formatting summaries](formatting.md#toolbar-state). Use `CodeEditor.SelectionsChanged` when the toolbar depends on secondary carets. `DocumentChanged` signals replacement, so detach handlers from the previous document and attach to the new one. [Events and threading](events-and-threading.md) explains notification order and reentrancy.
