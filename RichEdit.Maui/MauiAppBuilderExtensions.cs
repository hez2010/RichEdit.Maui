using Microsoft.Maui.Hosting;

namespace RichEdit.Maui;

/// <summary>Provides RichEdit.Maui registration extensions.</summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>Registers the native <see cref="RichEditor"/> handler.</summary>
    /// <param name="builder">The MAUI application builder.</param>
    /// <returns>The supplied builder for fluent configuration.</returns>
    public static MauiAppBuilder UseRichEdit(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<RichEditor, RichEditorHandler>());
        return builder;
    }
}
