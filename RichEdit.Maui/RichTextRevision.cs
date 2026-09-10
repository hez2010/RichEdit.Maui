namespace RichEdit.Maui;

/// <summary>Identifies an immutable revision of one document. The default value is invalid.</summary>
public readonly struct RichTextRevision : IEquatable<RichTextRevision>
{
    internal object? Identity { get; }
    internal RichTextRevision(object? identity, long version) { Identity = identity; Version = version; }

    /// <summary>Gets the monotonically increasing version within the owning document.</summary>
    public long Version { get; }
    /// <inheritdoc />
    public bool Equals(RichTextRevision other) => ReferenceEquals(Identity, other.Identity) && Version == other.Version;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RichTextRevision other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Identity, Version);
    /// <summary>Compares document identity and version.</summary>
    public static bool operator ==(RichTextRevision left, RichTextRevision right) => left.Equals(right);
    /// <summary>Compares document identity and version.</summary>
    public static bool operator !=(RichTextRevision left, RichTextRevision right) => !left.Equals(right);
}
