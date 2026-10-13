#if (DEBUG || EDITOR_TESTS) && ANDROID
using Android.Text.Style;
using Android.Widget;
using Microsoft.Maui.Platform;
using RichEdit.Core;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

internal static class RichEditorThemeTests
{
    internal static async Task RunAsync()
    {
        var app = Application.Current!;
        var window = app.Windows[0];
        var originalPage = window.Page;
        var originalTheme = app.UserAppTheme;
        try
        {
            foreach (var initialTheme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                app.UserAppTheme = initialTheme;
                var page = new RichEditorPage();
                window.Page = page;
                var editor = page.FindByName<RichEditor>("Editor");
                var peer = page.FindByName<RichEditor>("Peer");
                await WaitFor(() => editor.Document.CurrentSnapshot.Nodes.Values.OfType<RichList>().Any() &&
                    editor.Content?.Handler?.PlatformView is RichDocumentHost { Height: > 0 });
                await Task.Delay(200);
                var host = (RichDocumentHost)editor.Content!.Handler!.PlatformView!;
                var snapshot = editor.Document.CurrentSnapshot;
                var list = snapshot.Nodes.Values.OfType<RichList>().First();
                var paragraph = snapshot.Get<RichListItem>(list.Items[0]).Blocks[0];
                var point = new RichTextPoint(snapshot.Body, paragraph, 1);
                // Distinguish an authored color from the view's fallback color.
                editor.Edit(edit => edit.ApplyCharacterFormat(
                    new RichEdit.Core.RichTextSelection(point, point with { Offset = 2 }),
                    new RichCharacterStyle { Foreground = new RichColor(200, 40, 80) }));
                snapshot = editor.Document.CurrentSnapshot;
                editor.Selection = new RichEdit.Core.RichTextSelection(point, point);
                editor.Focus();
                await Task.Delay(300);
                var selection = editor.Selection;
                var scroll = host.ScrollY;
                var native = (EditText)host.FindFocus()!;
                var anchor = native.SelectionStart;
                var active = native.SelectionEnd;
                var revision = editor.Document.Revision;
                var canUndo = editor.CanUndo;
                var authoredColors = Foregrounds(host);
                Check(authoredColors.Length > 0, "authored text color is rendered");

                foreach (var theme in new[] { initialTheme, initialTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark, initialTheme })
                {
                    app.UserAppTheme = theme;
                    await Task.Delay(200);
                    var prefix = theme == AppTheme.Dark ? "Dark" : "Light";
                    var ink = (Color)app.Resources[prefix + "Ink"];
                    var background = (Color)app.Resources[prefix + "Surface"];
                    Check(editor.TextColor == ink && peer.TextColor == ink, "both sample views follow the theme");
                    Check(editor.BackgroundColor == background && peer.BackgroundColor == background, "sample foreground and background are paired");
                    var views = Descendants<TextView>(host).ToArray();
                    Check(views.OfType<EditText>().Count() > 1, "reference uses multiple native text flows");
                    Check(views.Any(view => view is not EditText && view.Text == "•"), "reference has native bullets");
                    Check(views.Any(view => view is not EditText && view.Text?.EndsWith('.') == true), "reference has native numbers");
                    Check(views.All(view => view.CurrentTextColor == ink.ToPlatform().ToArgb()), "native text, cells, bullets and numbers follow the theme");
                    Check(authoredColors.SequenceEqual(Foregrounds(host)), "explicit RTF colors stay unchanged");
                    Check(ReferenceEquals(snapshot, editor.Document.CurrentSnapshot) && revision == editor.Document.Revision, "theme does not edit the reference");
                    Check(canUndo == editor.CanUndo, "theme does not add undo history");
                    Check(Equals(selection, editor.Selection), "theme preserves rich selection");
                    Check(native.SelectionStart == anchor && native.SelectionEnd == active && native.HasFocus, "theme preserves native selection and focus");
                    Check(host.ScrollY == scroll, "theme preserves scroll position");
                }

                // A list item created after a theme change gets the current marker color.
                app.UserAppTheme = AppTheme.Dark;
                using var info = new Android.Views.InputMethods.EditorInfo();
                using var connection = native.OnCreateInputConnection(info)!;
                using var newline = new Java.Lang.String("\n");
                connection.CommitText(newline, 1);
                await Task.Delay(200);
                Check(editor.Document.CurrentSnapshot.Get<RichList>(list.Id).Items.Count == list.Items.Count + 1, "native Enter adds a list item");
                var dark = ((Color)app.Resources["DarkInk"]).ToPlatform().ToArgb();
                Check(Descendants<TextView>(host).All(view => view.CurrentTextColor == dark), "new text flows and markers use the current theme");
                editor.Undo();
                await Task.Delay(100);
                Check(editor.Document.CurrentSnapshot.Get<RichList>(list.Id).Items.Count == list.Items.Count, "list edit still undoes");
                editor.Content.Handler!.DisconnectHandler();
                editor.Handler!.DisconnectHandler();
            }
        }
        finally
        {
            app.UserAppTheme = originalTheme;
            window.Page = originalPage;
        }
    }

    private static int[] Foregrounds(Android.Views.View root) => Descendants<EditText>(root)
        .SelectMany(view => view.EditableText!.GetSpans(0, view.EditableText.Length(), Java.Lang.Class.FromType(typeof(ForegroundColorSpan)))!
            .OfType<ForegroundColorSpan>().Select(span => span.ForegroundColor)).ToArray();

    private static IEnumerable<T> Descendants<T>(Android.Views.View view) where T : Android.Views.View
    {
        if (view is T found) yield return found;
        if (view is Android.Views.ViewGroup group)
            for (var index = 0; index < group.ChildCount; index++)
                foreach (var child in Descendants<T>(group.GetChildAt(index)!)) yield return child;
    }

    private static async Task WaitFor(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 100 && !ready(); attempt++) await Task.Delay(50);
        Check(ready(), "sample reference loaded");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
