namespace RichEdit.Maui.Tests;

public sealed partial class NativeTestApplication : Microsoft.UI.Xaml.Application
{
    public NativeTestApplication()
    {
        UnhandledException += (_, args) => Console.Error.WriteLine(args.Exception);
        InitializeComponent();
    }
}
