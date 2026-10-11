using System.Collections.Immutable;

namespace RichEdit.Core.Rtf;

/// <summary>Describes an RTF feature that could not be represented exactly.</summary>
public sealed record RtfDiagnostic(string Feature, string Message, long? ByteOffset = null, RichNodeId? Node = null);

/// <summary>Resource limits for untrusted RTF input. Streams remain owned by the caller.</summary>
public sealed record RtfReadOptions
{
    /// <summary>Maximum bytes consumed from the input.</summary>
    public long MaximumBytes { get; init; } = 512L * 1024 * 1024;
    /// <summary>Maximum nested RTF groups.</summary>
    public int MaximumDepth { get; init; } = 128;
    /// <summary>Maximum bytes in one picture or binary payload.</summary>
    public int MaximumAssetBytes { get; init; } = 64 * 1024 * 1024;
    /// <summary>Maximum decoded UTF-16 content and metadata, including annotations and retained revision contexts.</summary>
    public int MaximumTextLength { get; init; } = 64 * 1024 * 1024;
    /// <summary>Maximum allocation records for structural nodes, resource definitions, pending anchors, table descriptors and distinct diagnostics.</summary>
    /// <remarks>Buffered parser records count in addition to their resulting model nodes. Repeated resource definitions also consume this budget.</remarks>
    public int MaximumNodes { get; init; } = 1_000_000;
    /// <summary>Maximum combined graph and dependency entries examined while validating retained revisions.</summary>
    /// <remarks>This bounds repeated historical validation independently of serialized byte and node counts.</remarks>
    public long MaximumRevisionWork { get; init; } = 64_000_000;
    /// <summary>Rejects input when an unsupported feature would be omitted or approximated.</summary>
    public bool RequireLossless { get; init; }

    internal void Validate()
    {
        if (MaximumBytes <= 0 || MaximumDepth is < 1 or > 512 || MaximumAssetBytes <= 0 || MaximumTextLength <= 0 || MaximumNodes <= 0 || MaximumRevisionWork <= 0)
            throw new ArgumentOutOfRangeException(nameof(RtfReadOptions));
    }
}

/// <summary>An imported immutable document and its fidelity diagnostics.</summary>
public sealed record RtfReadResult(RichDocumentSnapshot Snapshot, ImmutableArray<RtfDiagnostic> Diagnostics);

/// <summary>A fidelity failure. No successful lossless operation is reported.</summary>
public sealed class RtfFidelityException : InvalidOperationException
{
    /// <summary>Creates a failure with its feature diagnostics.</summary>
    public RtfFidelityException(ImmutableArray<RtfDiagnostic> diagnostics) : base("RTF cannot represent every requested feature exactly.") => Diagnostics = diagnostics;
    /// <summary>Gets the omitted or approximated features.</summary>
    public ImmutableArray<RtfDiagnostic> Diagnostics { get; }
}

/// <summary>Streams structured rich documents using RTF as their sole interchange representation.</summary>
public static class RtfCodec
{
    /// <summary>Reads and validates a detached snapshot. Unsupported content is reported, never silently retained as unsafe opaque data.</summary>
    public static Task<RtfReadResult> ReadAsync(Stream input, RtfReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("The stream must be readable.", nameof(input));
        options ??= new();
        options.Validate();
        return new RtfDocumentReader(input, options).ReadAsync(cancellationToken);
    }

    /// <summary>Assesses the same immutable state used by the writer before a host opens or replaces its destination.</summary>
    public static ImmutableArray<RtfDiagnostic> Assess(RichDocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = ImmutableArray.CreateBuilder<RtfDiagnostic>();
        if (snapshot.Nodes.Values.OfType<RichList>().Count() > 2000)
            diagnostics.Add(new("lists", "RTF supports at most 2,000 list overrides."));
        foreach (var entry in RtfRevisionExport.Create(snapshot))
            if (entry.Before.Content.Nodes.Values.OfType<RichList>().Count() > 2000 || entry.After.Content.Nodes.Values.OfType<RichList>().Count() > 2000)
                diagnostics.Add(new("revision-lists", $"Revision {entry.Revision.Id} exceeds the RTF capacity of 2,000 list overrides in a retained context."));
        return diagnostics.ToImmutable();
    }

    /// <summary>Writes a lossless RTF representation of supported authored state. Assessment completes before any bytes are written.</summary>
    /// <remarks>Pending revisions use retained RTF contexts with ignorable identity and group metadata.
    /// Readers that omit this metadata receive the current body. Cancellation may leave a partial stream;
    /// atomic file replacement belongs to the host storage service.</remarks>
    public static Task WriteAsync(RichDocumentSnapshot snapshot, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("The stream must be writable.", nameof(output));
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = Assess(snapshot);
        if (!diagnostics.IsEmpty) throw new RtfFidelityException(diagnostics);
        return new RtfProjectionWriter(snapshot, output, semantic: true).WriteAsync(cancellationToken);
    }
}
