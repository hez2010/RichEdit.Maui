namespace RichEdit.Maui;

internal static class RichTextPositionMap
{
    internal static int? Map(int position, RichTextTextChange change, PositionAffinity affinity,
        TrackingDeletionBehavior deletion = TrackingDeletionBehavior.Preserve)
    {
        var old = change.OldRange;
        if (position < old.Start)
            return position;
        if (position > old.End || !old.IsEmpty && position == old.End)
            return checked(position + change.NewRange.Length - old.Length);
        if (!old.IsEmpty && deletion == TrackingDeletionBehavior.Invalidate)
            return null;

        return affinity == PositionAffinity.BeforeInsertion ? old.Start : change.NewRange.End;
    }

    internal static TextSpan? Map(TextSpan range, RichTextTextChange change, PositionAffinity startAffinity,
        PositionAffinity endAffinity, TrackingDeletionBehavior deletion)
    {
        if (deletion == TrackingDeletionBehavior.Invalidate && !change.OldRange.IsEmpty &&
            (range.IsEmpty ? change.OldRange.Start <= range.Start && range.Start < change.OldRange.End :
                change.OldRange.Start < range.End && range.Start < change.OldRange.End))
            return null;

        var start = Map(range.Start, change, startAffinity)!.Value;
        var end = Map(range.End, change, endAffinity)!.Value;
        return new(start, Math.Max(0, end - start));
    }
}
