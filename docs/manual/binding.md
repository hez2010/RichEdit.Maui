---
title: XAML and data binding
description: Bind live documents, directional selections, formatting controls, and editing commands in a MAUI view.
---

# XAML and data binding

Bind `Document` to a live document object. Typing updates that object in place.

## Bind an editor and toolbar

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:rich="clr-namespace:RichEdit.Maui;assembly=RichEdit.Maui"
    x:Class="MyApp.EditorPage">
    <Grid Padding="16" RowDefinitions="Auto,*" RowSpacing="8">
        <HorizontalStackLayout Spacing="8">
            <Button Text="Undo"
                    Command="{Binding Source={x:Reference Editor}, Path=Commands.Undo}" />
            <Button Text="Redo"
                    Command="{Binding Source={x:Reference Editor}, Path=Commands.Redo}" />
            <Label Text="{Binding Document.Length, StringFormat='Length: {0}'}" />
        </HorizontalStackLayout>
        <rich:RichEditor x:Name="Editor"
                         Grid.Row="1"
                         Document="{Binding Document}"
                         SelectionState="{Binding Selection, Mode=TwoWay}"
                         IsReadOnly="{Binding IsReadOnly}"
                         Placeholder="Start writing…" />
    </Grid>
</ContentPage>
```

The binding context supplies a `RichTextDocument Document`, a `RichTextSelectionState Selection`, and an `IsReadOnly` boolean. The document implements `INotifyPropertyChanged` for content, history, and saved state.

`Document` defaults to a one-way binding. Raise the view model's property notification when replacing the document. `SelectedRange` and `SelectionState` support two-way binding; `SelectionState` also preserves selection direction.

## Show formatting state

`Selection.CharacterFormat` and `Selection.ParagraphFormat` are bindable objects that follow the current selection. A font-size control can bind to `Selection.CharacterFormat.FontSize` and use `IsFontSizeMixed` to display a mixed selection.

For font family, size, and foreground color, the character-format object also distinguishes [inherited and effective values](formatting.md#mixed-and-inherited-values).

Formatting buttons can call `Selection.ToggleBold()`, `ToggleItalic()`, or another selection operation. Built-in commands update their own `CanExecute` state; custom toolbar commands need to refresh when selection, formatting, or read-only state changes.

## Bind CodeEditor

Declare its namespace and use a `CodeDocument` in the view model:

```xml
<code:CodeEditor
    xmlns:code="clr-namespace:CodeEdit.Maui;assembly=CodeEdit.Maui"
    Document="{Binding Document}"
    SelectionState="{Binding Selection, Mode=TwoWay}"
    IsReadOnly="{Binding IsReadOnly}"
    ShowLineNumbers="True"
    WordWrap="False" />
```

The selection uses the same UTF-16 range and direction types. Status displays can bind to `CaretPosition`, `LineCount`, and `Document.IsModified`.

## Document lifetime

A live document can attach to one editor at a time. Multiple editors need separate document instances.

`DocumentChanged` provides the old and new documents so you can move subscriptions and cancel requests for the old content.
