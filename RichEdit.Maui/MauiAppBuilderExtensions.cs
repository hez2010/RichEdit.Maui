using Microsoft.Maui.Hosting;

namespace RichEdit.Maui;

public static class MauiAppBuilderExtensions
{
    public static MauiAppBuilder UseRichEdit(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<RichEditor, RichEditorHandler>());
        return builder;
    }
}
