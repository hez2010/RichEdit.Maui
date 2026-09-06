using Microsoft.Maui.Hosting;
using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>Provides CodeEditor registration for MAUI applications.</summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>Registers the RichEditor handler used internally by CodeEditor.</summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The supplied builder.</returns>
    public static MauiAppBuilder UseCodeEditor(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseRichEdit();
        return builder;
    }
}
