using CodeEdit.Lsp;

namespace CodeEdit.Maui;

/// <summary>Resolves the foreground color of a semantic token.</summary>
/// <param name="token">The complete token, including its type, modifiers, and source range.</param>
/// <returns>The token color, or null to leave the normal text appearance unchanged.</returns>
/// <remarks>Called on the editor's UI thread. The callback should be fast and must not modify the editor.</remarks>
public delegate Color? CodeEditorTheme(SemanticToken token);
