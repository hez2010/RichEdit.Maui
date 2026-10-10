using System.Collections.Immutable;

namespace TextEdit.Core;

/// <summary>A replacement in the coordinates of the snapshot supplied to an input rule.</summary>
public sealed record TextReplacement(TextSpan Range, string Text);

/// <summary>A synchronous input-rule callback's immutable input and proposed dependent changes.</summary>
/// <remarks>All proposals address <see cref="Input"/>. A callback must not mutate a live editor or retain this builder for later use.</remarks>
public sealed class TextInputContext
{
    private readonly List<TextReplacement> _edits = [];
    private bool _active = true;
    private TextSelectionSet? _resultingSelections;

    internal TextInputContext(TextSnapshot before, TextSnapshot input, TextSelectionSet beforeSelections,
        TextSelectionSet selections, ImmutableArray<TextReplacement> inputChanges, bool composition)
    {
        Before = before;
        Input = input;
        BeforeSelections = beforeSelections;
        Selections = selections;
        InputChanges = inputChanges;
        IsCompositionCommit = composition;
    }

    /// <summary>Gets the committed source before native input.</summary>
    public TextSnapshot Before { get; }
    /// <summary>Gets source after the originating input, before any rule proposals.</summary>
    public TextSnapshot Input { get; }
    /// <summary>Gets selections before the originating input.</summary>
    public TextSelectionSet BeforeSelections { get; }
    /// <summary>Gets selections after originating input, before dependent changes.</summary>
    public TextSelectionSet Selections { get; }
    /// <summary>Gets the sequential replacements made by originating input.</summary>
    public ImmutableArray<TextReplacement> InputChanges { get; }
    /// <summary>Gets whether this input commits a completed native composition.</summary>
    public bool IsCompositionCommit { get; }

    /// <summary>Proposes a dependent replacement against <see cref="Input"/>. Overlapping proposals reject the entire plan.</summary>
    public void Replace(TextSpan range, string text)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(text);
        range.Validate(Input.Length);
        _edits.Add(new(range, text));
    }

    /// <summary>Proposes all resulting selections in final coordinates. Only one rule can set them.</summary>
    public void SetSelections(TextSelectionSet selections)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(selections);
        if (_resultingSelections is not null)
            throw new InvalidOperationException("Resulting selections have already been proposed.");
        _resultingSelections = selections;
    }

    private void VerifyActive()
    {
        if (!_active)
            throw new InvalidOperationException("An input-rule builder is valid only during its callback.");
    }

    internal (IReadOnlyList<TextReplacement> Edits, TextSelectionSet? Selections) Finish()
    {
        _active = false;
        return (_edits, _resultingSelections);
    }
}

/// <summary>A fully validated dependent input plan, ready to commit together with its originating input.</summary>
public sealed record TextInputPlan(TextSnapshot Snapshot, TextSelectionSet Selections,
    ImmutableArray<TextReplacement> Replacements);

/// <summary>Plans dependent text changes synchronously in ascending priority and registration order.</summary>
/// <remarks>Each rule sees the same immutable input. Conflicting replacements reject the whole plan. Registration and planning belong to the owning editor's dispatcher.</remarks>
public sealed class TextInputRules
{
    private readonly List<Registration> _rules = [];
    private long _nextOrder;
    private bool _planning;

    /// <summary>Gets the number of registered rules.</summary>
    public int Count => _rules.Count;

    /// <summary>Registers a synchronous rule. Disposing the registration removes it.</summary>
    public IDisposable Register(Action<TextInputContext> rule, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(rule);
        VerifyIdle();
        var registration = new Registration(this, rule, priority, _nextOrder++);
        _rules.Add(registration);
        return registration;
    }

    /// <summary>Validates proposals without modifying any live document. Throws if a rule fails or proposals conflict.</summary>
    public TextInputPlan Plan(TextSnapshot before, TextSnapshot input, TextSelectionSet beforeSelections,
        TextSelectionSet selections, IEnumerable<TextReplacement> inputChanges, bool isCompositionCommit = false)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(beforeSelections);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(inputChanges);
        beforeSelections.Validate(before.Length);
        selections.Validate(input.Length);
        VerifyIdle();
        _planning = true;
        try
        {
            var changes = inputChanges.ToImmutableArray();
            var edits = new List<TextReplacement>();
            TextSelectionSet? explicitSelections = null;
            foreach (var rule in _rules.OrderBy(rule => rule.Priority).ThenBy(rule => rule.Order))
            {
                var context = new TextInputContext(before, input, beforeSelections, selections, changes, isCompositionCommit);
                (IReadOnlyList<TextReplacement> Edits, TextSelectionSet? Selections) proposal;
                try { rule.Callback(context); }
                finally { proposal = context.Finish(); }
                edits.AddRange(proposal.Edits);
                if (proposal.Selections is { } proposed)
                {
                    if (explicitSelections is not null)
                        throw new InvalidOperationException("Multiple input rules proposed resulting selections.");
                    explicitSelections = proposed;
                }
            }

            var ordered = edits.OrderBy(edit => edit.Range.Start).ThenBy(edit => edit.Range.End).ToArray();
            for (var i = 1; i < ordered.Length; i++)
                if (ordered[i].Range.Start < ordered[i - 1].Range.End || ordered[i].Range.Start == ordered[i - 1].Range.Start)
                    throw new InvalidOperationException("Input-rule replacements overlap or share an insertion position.");

            var result = input;
            var resulting = selections.ToArray();
            foreach (var edit in ordered.Reverse())
            {
                result = result.Replace(edit.Range, edit.Text);
                for (var i = 0; i < resulting.Length; i++)
                    resulting[i] = resulting[i] with { Anchor = Map(resulting[i].Anchor, edit), Active = Map(resulting[i].Active, edit) };
            }
            var selection = explicitSelections ?? new TextSelectionSet(resulting, selections.PrimaryIndex);
            selection.Validate(result.Length);
            return new(result, selection, [.. ordered.Reverse()]);
        }
        finally { _planning = false; }
    }

    private static int Map(int position, TextReplacement edit) => position < edit.Range.Start ? position :
        position > edit.Range.End ? checked(position + edit.Text.Length - edit.Range.Length) : checked(edit.Range.Start + edit.Text.Length);

    private void VerifyIdle()
    {
        if (_planning)
            throw new InvalidOperationException("Input rules cannot be changed or recursively invoked during planning.");
    }

    private sealed class Registration(TextInputRules owner, Action<TextInputContext> callback, int priority, long order) : IDisposable
    {
        internal Action<TextInputContext> Callback { get; } = callback;
        internal int Priority { get; } = priority;
        internal long Order { get; } = order;
        public void Dispose()
        {
            owner.VerifyIdle();
            owner._rules.Remove(this);
        }
    }
}
