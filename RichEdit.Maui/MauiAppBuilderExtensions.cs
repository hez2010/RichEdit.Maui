using Microsoft.Maui.Hosting;

namespace RichEdit.Maui;

/// <summary>Provides RichEdit.Maui registration extensions.</summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>Registers the shared native editing backend used by <see cref="RichEditor"/>.</summary>
    /// <param name="builder">The MAUI application builder.</param>
    /// <returns>The supplied builder for fluent configuration.</returns>
    public static MauiAppBuilder UseRichEdit(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<NativeEditorView, RichEditorHandler>();
        });
        return builder;
    }
}
