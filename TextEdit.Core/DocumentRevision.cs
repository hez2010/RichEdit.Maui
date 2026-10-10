namespace TextEdit.Core;

/// <summary>An opaque immutable revision of one document. The default value cannot authorize an edit.</summary>
public readonly struct DocumentRevision : IEquatable<DocumentRevision>
{
    private readonly object? _identity;
    internal DocumentRevision(object? identity, long version) { _identity = identity; Version = version; }
    internal object? Identity => _identity;

    /// <summary>Gets whether this revision belongs to a live document.</summary>
    public bool IsValid => _identity is not null;
    /// <summary>Gets the monotonically increasing document version.</summary>
    public long Version { get; }
    /// <inheritdoc />
    public bool Equals(DocumentRevision other) => ReferenceEquals(_identity, other._identity) && Version == other.Version;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DocumentRevision other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_identity, Version);
    /// <summary>Compares the owning document and version.</summary>
    public static bool operator ==(DocumentRevision left, DocumentRevision right) => left.Equals(right);
    /// <summary>Compares the owning document and version.</summary>
    public static bool operator !=(DocumentRevision left, DocumentRevision right) => !left.Equals(right);
}
