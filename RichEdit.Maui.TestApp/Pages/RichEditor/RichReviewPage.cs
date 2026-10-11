using RichEdit.Core;

namespace RichEdit.Maui.TestApp;

internal sealed partial class RichReviewPage : ContentPage
{
    private readonly RichDocument _document;
    private readonly RichReviewView _review;
    private readonly Picker _groups = new() { Title = "Change group" };
    private readonly Label _status = new() { FontSize = 12 };

    public RichReviewPage(RichDocument document)
    {
        _document = document;
        Title = "Review changes";
        Shell.SetNavBarIsVisible(this, true);
        _review = new() { Source = document, FontSize = 17, Mode = RichReviewMode.Original };
        var mode = new Picker { Title = "View", ItemsSource = new[] { "Original", "Final" }, SelectedIndex = 0 };
        mode.SelectedIndexChanged += (_, _) => _review.Mode = mode.SelectedIndex == 0 ? RichReviewMode.Original : RichReviewMode.Final;
        var accept = new Button { Text = "Accept group" };
        var reject = new Button { Text = "Reject group" };
        accept.Clicked += async (_, _) => await ResolveAsync(true);
        reject.Clicked += async (_, _) => await ResolveAsync(false);
        var grid = new Grid { Padding = 18, RowSpacing = 10, RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(new Label { Text = "Compare the original and final document, then accept or reject a tracked group. Both actions can be undone in the editor." });
        var tools = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        tools.Add(mode); tools.Add(_groups); tools.Add(accept); tools.Add(reject);
        grid.Add(tools, 0, 1); grid.Add(_review, 0, 2); grid.Add(_status, 0, 3);
        Content = grid;
        RefreshGroups();
    }

    protected override void OnAppearing() { base.OnAppearing(); _document.Changed += OnDocumentChanged; RefreshGroups(); }
    protected override void OnDisappearing() { _document.Changed -= OnDocumentChanged; base.OnDisappearing(); }
    private void OnDocumentChanged(object? sender, RichDocumentChangedEventArgs args) => RefreshGroups();

    private void RefreshGroups()
    {
        var selected = (_groups.SelectedItem as ChangeGroup)?.Id;
        var groups = _document.CurrentSnapshot.Revisions.Values.OrderBy(item => item.Id).GroupBy(item => item.Info.Group)
            .Select((group, index) => new ChangeGroup(group.Key, $"{index + 1}. {group.First().Info.Author} · {group.Count()} edits")).ToArray();
        _groups.ItemsSource = groups;
        _groups.SelectedIndex = groups.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(groups, item => item.Id == selected));
        _status.Text = groups.Length == 0 ? "No pending changes. Turn on Track changes in the editor to create a group." : $"{groups.Length} pending groups";
    }

    private async Task ResolveAsync(bool accept)
    {
        if (_groups.SelectedItem is not ChangeGroup group) return;
        var snapshot = _document.CurrentSnapshot;
        var affected = snapshot.GetRevisionResolution(group.Id, accept).Select(item => item.Info.Group).Distinct().Count();
        var verb = accept ? "Accept" : "Reject";
        if (!await DisplayAlertAsync($"{verb} changes", $"{verb} {group.Label}? This resolves {affected} group(s), including related changes.", verb, "Cancel")) return;
        try
        {
            var result = _document.TryEdit(snapshot.Revision, edit =>
            {
                if (accept) edit.AcceptRevision(group.Id); else edit.RejectRevision(group.Id);
            }, new($"{verb} change group"));
            if (!result.Accepted) _status.Text = $"Changes were not resolved: {result.Rejection}.";
        }
        catch (InvalidOperationException error) { _status.Text = error.Message; }
    }

    private sealed record ChangeGroup(Guid Id, string Label)
    {
        public override string ToString() => Label;
    }
}
