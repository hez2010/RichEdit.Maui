using Microsoft.Maui;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

// Clipboard cases and the existing native tests share a process-wide clipboard.
[Collection("Native editor")]
public class PlatformContractTests
{
    private static Microsoft.UI.Xaml.Window? _window;
    public static IEnumerable<object[]> Scenarios => EditorContractTests.Cases.Select(test => new object[] { test.Name });

    [Theory]
    [MemberData(nameof(Scenarios), DisableDiscoveryEnumeration = true)]
    public Task NativeEditorContract(string name) => WindowsTestHost.RunClipboardAsync(async () =>
    {
        var editor = new RichEditor();
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.Handler = handler;
        try
        {
            _window ??= new Microsoft.UI.Xaml.Window();
            var grid = new Microsoft.UI.Xaml.Controls.Grid();
            grid.RowDefinitions.Add(new Microsoft.UI.Xaml.Controls.RowDefinition());
            grid.RowDefinitions.Add(new Microsoft.UI.Xaml.Controls.RowDefinition { Height = Microsoft.UI.Xaml.GridLength.Auto });
            var focusTarget = new Microsoft.UI.Xaml.Controls.Button { Content = "Focus target" };
            Microsoft.UI.Xaml.Controls.Grid.SetRow(focusTarget, 1);
            grid.Children.Add(handler.ContainerView ?? handler.PlatformView);
            grid.Children.Add(focusTarget);
            _window.Content = grid;
            _window.Activate();
            if (name.StartsWith("focus", StringComparison.Ordinal)) focusTarget.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            else handler.PlatformView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Task.Delay(50);
            EditorContractTests.Reset(editor);
            await EditorContractTests.Cases.Single(test => test.Name == name).Run(editor);
        }
        finally
        {
            _window!.Content = null;
            editor.Handler = null;
            ((IElementHandler)handler).DisconnectHandler();
        }
    });
}

[CollectionDefinition("Native editor", DisableParallelization = true)]
public class NativeEditorCollection;
