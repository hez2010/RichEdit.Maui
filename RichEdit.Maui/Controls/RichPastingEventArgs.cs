using System.Collections.Immutable;
using RichEdit.Core;
using RichEdit.Core.Rtf;

namespace RichEdit.Maui;

/// <summary>A proposed structured paste, with import diagnostics available before any document mutation.</summary>
public sealed class RichPastingEventArgs : EventArgs
{
    private RichDocumentFragment _fragment;
    internal RichPastingEventArgs(RichDocumentFragment fragment, bool plainText, ImmutableArray<RtfDiagnostic> diagnostics)
    { _fragment = fragment; UsePlainText = plainText; Diagnostics = diagnostics; }
    /// <summary>Gets or replaces the proposed immutable fragment.</summary>
    public RichDocumentFragment Fragment { get => _fragment; set => _fragment = value ?? throw new ArgumentNullException(nameof(value)); }
    /// <summary>Gets or sets whether to insert the fragment's explicit text projection using current typing attributes.</summary>
    public bool UsePlainText { get; set; }
    /// <summary>Gets features omitted or approximated by RTF import.</summary>
    public ImmutableArray<RtfDiagnostic> Diagnostics { get; }
    /// <summary>Gets or sets whether the operation is canceled.</summary>
    public bool Cancel { get; set; }
}
