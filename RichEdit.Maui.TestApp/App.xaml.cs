using Microsoft.Extensions.DependencyInjection;

namespace RichEdit.Maui.TestApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
#if DEBUG && (IOS || MACCATALYST)
        if (Environment.GetEnvironmentVariable("RICHEDIT_RUN_CODE_TESTS") == "1")
            return new Window(new Tests.CodeEditorTestPage(Environment.GetEnvironmentVariable("RICHEDIT_TEST_FILTER")));
        if (Environment.GetEnvironmentVariable("RICHEDIT_RUN_TESTS") == "1")
        {
            var editor = new RichEditor();
            var started = false;
            editor.Loaded += async (_, _) =>
            {
                if (!started)
                {
                    started = true;
                    await AppleEditorTests.RunAsync(editor);
                }
            };
            return new Window(new ContentPage { Content = editor, SafeAreaEdges = new(SafeAreaRegions.Container) });
        }
#endif
		return new Window(new AppShell());
	}
}
