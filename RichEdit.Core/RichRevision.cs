using System.Collections.Immutable;

namespace RichEdit.Core;

/// <summary>Authorship and grouping for tracked rich transactions.</summary>
public sealed record RichRevisionInfo
{
    /// <summary>Creates an authored change group. Timestamps use the UTC-minute precision of RTF.</summary>
    public RichRevisionInfo(string author, Guid? group = null, DateTimeOffset? created = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        var date = (created ?? DateTimeOffset.UtcNow).UtcDateTime;
        if (date.Year is < 1900 or > 2411) throw new ArgumentOutOfRangeException(nameof(created));
        Author = author;
        Group = group ?? Guid.NewGuid();
        if (Group == Guid.Empty) throw new ArgumentException("A revision group requires a nonempty identity.", nameof(group));
        Created = new(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0, TimeSpan.Zero);
    }

    /// <summary>Gets the author's display name.</summary>
    public string Author { get; }
    /// <summary>Gets the persistent identity shared by transactions resolved together.</summary>
    public Guid Group { get; }
    /// <summary>Gets the UTC creation minute.</summary>
    public DateTimeOffset Created { get; }
}

/// <summary>The retained before and after values of one authored item.</summary>
public sealed record RichRevisionValue<T>(T? Before, T? After) where T : class;

/// <summary>A retained authored transaction, independent of undo history.</summary>
/// <remarks>Only changed nodes and resources are retained. A dependency identifies an earlier
/// transaction touching the same nodes, resources, or document defaults.</remarks>
public sealed class RichRevision
{
    internal RichRevision(long id, RichRevisionInfo info, RichRevisionDelta delta, ImmutableHashSet<long> dependencies)
    { Id = id; Info = info; Delta = delta; Dependencies = dependencies; }

    /// <summary>Gets the document-local transaction identity.</summary>
    public long Id { get; }
    /// <summary>Gets authorship and change-group metadata.</summary>
    public RichRevisionInfo Info { get; }
    /// <summary>Gets earlier pending transactions required by this change.</summary>
    public ImmutableHashSet<long> Dependencies { get; }
    /// <summary>Gets changed nodes, including deleted text and structural content.</summary>
    public ImmutableDictionary<RichNodeId, RichRevisionValue<RichNode>> Nodes => Delta.Nodes;
    /// <summary>Gets changed reusable styles.</summary>
    public ImmutableDictionary<RichStyleId, RichRevisionValue<RichStyleDefinition>> Styles => Delta.Styles;
    /// <summary>Gets changed image resources.</summary>
    public ImmutableDictionary<RichAssetId, RichRevisionValue<RichImageAsset>> Assets => Delta.Assets;
    /// <summary>Gets changed bookmarks.</summary>
    public ImmutableDictionary<RichAnnotationId, RichRevisionValue<RichBookmark>> Bookmarks => Delta.Bookmarks;
    /// <summary>Gets changed comments.</summary>
    public ImmutableDictionary<RichAnnotationId, RichRevisionValue<RichComment>> Comments => Delta.Comments;
    /// <summary>Gets changed authored character defaults, or null when unchanged.</summary>
    public RichRevisionValue<RichCharacterStyle>? DefaultCharacter => Delta.DefaultCharacter;
    /// <summary>Gets changed authored paragraph defaults, or null when unchanged.</summary>
    public RichRevisionValue<RichParagraphStyle>? DefaultParagraph => Delta.DefaultParagraph;
    internal RichRevisionDelta Delta { get; }
}

internal sealed class RichRevisionDelta
{
    internal ImmutableDictionary<RichNodeId, RichRevisionValue<RichNode>> Nodes { get; init; } = ImmutableDictionary<RichNodeId, RichRevisionValue<RichNode>>.Empty;
    internal ImmutableDictionary<RichStyleId, RichRevisionValue<RichStyleDefinition>> Styles { get; init; } = ImmutableDictionary<RichStyleId, RichRevisionValue<RichStyleDefinition>>.Empty;
    internal ImmutableDictionary<RichAssetId, RichRevisionValue<RichImageAsset>> Assets { get; init; } = ImmutableDictionary<RichAssetId, RichRevisionValue<RichImageAsset>>.Empty;
    internal ImmutableDictionary<RichAnnotationId, RichRevisionValue<RichBookmark>> Bookmarks { get; init; } = ImmutableDictionary<RichAnnotationId, RichRevisionValue<RichBookmark>>.Empty;
    internal ImmutableDictionary<RichAnnotationId, RichRevisionValue<RichComment>> Comments { get; init; } = ImmutableDictionary<RichAnnotationId, RichRevisionValue<RichComment>>.Empty;
    internal RichRevisionValue<RichCharacterStyle>? DefaultCharacter { get; init; }
    internal RichRevisionValue<RichParagraphStyle>? DefaultParagraph { get; init; }
    internal bool IsEmpty => Nodes.IsEmpty && Styles.IsEmpty && Assets.IsEmpty && Bookmarks.IsEmpty && Comments.IsEmpty && DefaultCharacter is null && DefaultParagraph is null;

    internal long EstimatedBytes => 1024L + Styles.Count * 1024L + Bookmarks.Count * 256L +
        Nodes.Values.SelectMany(value => new[] { value.Before, value.After }).OfType<RichNode>()
            .Sum(node => 256L + (node is RichText text ? text.Content.Length * 2L : 0)) +
        Assets.Values.SelectMany(value => new[] { value.Before, value.After }).OfType<RichImageAsset>().Sum(asset => (long)asset.Data.Length) +
        Comments.Values.SelectMany(value => new[] { value.Before, value.After }).OfType<RichComment>()
            .Sum(comment => 256L + 2L * (comment.Author.Length + (long)comment.Text.Length));

    internal static RichRevisionDelta Between(RichDocumentSnapshot before, RichDocumentSnapshot after) => new()
    {
        Nodes = Difference(before.Nodes, after.Nodes, RichDocumentSnapshot.SameNode),
        Styles = Difference(before.Styles.Definitions, after.Styles.Definitions),
        Assets = Difference(before.Assets, after.Assets),
        Bookmarks = Difference(before.Bookmarks, after.Bookmarks),
        Comments = Difference(before.Comments, after.Comments),
        DefaultCharacter = before.DefaultCharacter == after.DefaultCharacter ? null : new(before.DefaultCharacter, after.DefaultCharacter),
        DefaultParagraph = before.DefaultParagraph == after.DefaultParagraph ? null : new(before.DefaultParagraph, after.DefaultParagraph),
    };

    internal bool Overlaps(RichRevisionDelta other) =>
        Nodes.Keys.Any(other.Nodes.ContainsKey) || Styles.Keys.Any(other.Styles.ContainsKey) ||
        Assets.Keys.Any(other.Assets.ContainsKey) || Bookmarks.Keys.Any(other.Bookmarks.ContainsKey) ||
        Comments.Keys.Any(other.Comments.ContainsKey) || DefaultCharacter is not null && other.DefaultCharacter is not null ||
        DefaultParagraph is not null && other.DefaultParagraph is not null || References(other) || other.References(this);

    private bool References(RichRevisionDelta other)
    {
        foreach (var node in Nodes.Values.SelectMany(value => new[] { value.Before, value.After }).OfType<RichNode>())
        {
            if (node.Parent is { } parent && other.Nodes.ContainsKey(parent) || RichDocumentSnapshot.Children(node).Any(other.Nodes.ContainsKey)) return true;
            if (node is RichInline inline && inline.Style is { } characterStyle && other.Styles.ContainsKey(characterStyle)) return true;
            if (node is RichParagraph paragraph && paragraph.Style is { } paragraphStyle && other.Styles.ContainsKey(paragraphStyle)) return true;
            if (node is RichImage image && other.Assets.ContainsKey(image.Asset)) return true;
        }
        foreach (var style in Styles.Values.SelectMany(value => new[] { value.Before, value.After }).OfType<RichStyleDefinition>())
            if (style.BasedOn is { } based && other.Styles.ContainsKey(based) || style.NextStyle is { } next && other.Styles.ContainsKey(next)) return true;
        foreach (var range in Bookmarks.Values.SelectMany(value => new[] { value.Before?.Range, value.After?.Range })
            .Concat(Comments.Values.SelectMany(value => new[] { value.Before?.Range, value.After?.Range })).OfType<RichTextSelection>())
            if (other.Nodes.ContainsKey(range.Anchor.Paragraph) || other.Nodes.ContainsKey(range.Active.Paragraph)) return true;
        return false;
    }

    private static ImmutableDictionary<TKey, RichRevisionValue<TValue>> Difference<TKey, TValue>(
        ImmutableDictionary<TKey, TValue> before, ImmutableDictionary<TKey, TValue> after, Func<TValue, TValue, bool>? same = null)
        where TKey : notnull where TValue : class
    {
        if (ReferenceEquals(before, after)) return ImmutableDictionary<TKey, RichRevisionValue<TValue>>.Empty;
        var result = ImmutableDictionary.CreateBuilder<TKey, RichRevisionValue<TValue>>();
        foreach (var pair in before)
        {
            var next = after.GetValueOrDefault(pair.Key);
            if (next is null || !(same?.Invoke(pair.Value, next) ?? EqualityComparer<TValue>.Default.Equals(pair.Value, next)))
                result.Add(pair.Key, new(pair.Value, next));
        }
        foreach (var pair in after)
            if (!before.ContainsKey(pair.Key)) result.Add(pair.Key, new(null, pair.Value));
        return result.ToImmutable();
    }
}
