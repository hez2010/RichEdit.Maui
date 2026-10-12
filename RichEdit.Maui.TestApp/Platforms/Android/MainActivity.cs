using Android.App;
using Android.Content.PM;
using Android.OS;

namespace RichEdit.Maui.TestApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
#if DEBUG || EDITOR_TESTS
    private bool _testsStarted;

    protected override void OnPostResume()
    {
        base.OnPostResume();
        if (!_testsStarted && Intent?.GetStringExtra("editing-scenarios") is { } suite)
        {
            _testsStarted = true;
            Microsoft.Maui.Controls.Application.Current!.Windows[0].Page = new Tests.EditingScenarioPage(suite, Intent?.GetStringExtra("test-filter"));
        }
        if (!_testsStarted && Intent?.GetBooleanExtra("run-rich-host-tests", false) == true)
        {
            _testsStarted = true;
            Microsoft.Maui.Controls.Application.Current!.Windows[0].Page = new Tests.RichStructuredHostTestPage();
        }
        if (!_testsStarted && Intent?.GetStringExtra("rtf-clipboard-action") is { } action)
        {
            _testsStarted = true;
            Microsoft.Maui.Controls.Application.Current!.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500),
                async () => await Tests.RtfClipboardExchangeTests.RunAsync(action));
        }
        if (!_testsStarted && Intent?.GetBooleanExtra("run-code-editor-tests", false) == true)
        {
            _testsStarted = true;
            Microsoft.Maui.Controls.Application.Current!.Windows[0].Page = new Tests.CodeEditorTestPage(Intent?.GetStringExtra("test-filter"));
        }
        if (!_testsStarted && Intent?.GetBooleanExtra("run-editor-tests", false) == true)
        {
            _testsStarted = true;
            var editor = new NativeEditorView();
            editor.Loaded += async (_, _) => await Tests.AndroidEditorTests.RunAsync(editor, Intent?.GetStringExtra("test-filter"));
            Microsoft.Maui.Controls.Application.Current!.Windows[0].Page = new ContentPage
            {
                Content = editor,
                SafeAreaEdges = new(SafeAreaRegions.Container),
            };
        }
    }
#endif
}
