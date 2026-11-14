using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Collections;

/// <summary>
/// A grid that both groups its rows and shows one page of them at a time. Only the rows on the page
/// get group headers, and every edit has to keep both things true at once: the page stays full, and
/// the rows on it stay filed under the right headers.
/// </summary>
public class DataGridCollectionViewGroupedPagingTests
{
    [Fact]
    public void A_Page_Only_Carries_The_Groups_Its_Own_Rows_Belong_To()
    {
        var view = GroupedPagedView(pageSize: 3);

        // Page one is entirely team A, so team B has no header on it.
        Assert.Equal(new[] { "A" }, GroupKeys(view));
        Assert.Equal(new[] { 0, 1, 2 }, Values(view));

        view.MoveToNextPage();

        // Page two straddles the boundary, so it carries both headers.
        Assert.Equal(new[] { "A", "B" }, GroupKeys(view));
        Assert.Equal(new[] { 3 }, GroupValues(view, "A"));
        Assert.Equal(new[] { 4, 5 }, GroupValues(view, "B"));
    }

    [Fact]
    public void Deleting_A_Row_Pulls_A_Row_From_The_Next_Group_Onto_The_Page()
    {
        var view = GroupedPagedView(pageSize: 3);
        view.MoveToNextPage();
        Assert.Equal(new[] { 3, 4, 5 }, Values(view));

        view.RemoveAt(0);

        // Row 3 was the last of team A on this page; losing it empties that header and brings
        // row 6 down from the next page to keep the page full.
        Assert.Equal(new[] { "B" }, GroupKeys(view));
        Assert.Equal(new[] { 4, 5, 6 }, Values(view));
    }

    [Fact]
    public void Deleting_The_Last_Row_Of_The_Last_Page_Takes_The_User_Back_A_Page()
    {
        var view = GroupedPagedView(pageSize: 3, count: 7);
        view.MoveToLastPage();
        Assert.Equal(new[] { 6 }, Values(view));

        view.RemoveAt(0);

        Assert.Equal(1, view.PageIndex);
        Assert.Equal(new[] { 3, 4, 5 }, Values(view));
    }

    [Fact]
    public void Adding_A_Row_On_A_Full_Grouped_Page_Makes_Room_For_It()
    {
        var view = GroupedPagedView(pageSize: 3);

        var added = Assert.IsType<Row>(view.AddNew());

        // The page still holds three rows: the two that fit plus the one being typed in.
        Assert.Equal(3, view.Count);
        Assert.Same(added, view.GetItemAt(2));
    }

    [Fact]
    public void Committing_A_New_Row_Files_It_Under_Its_Group_And_Restores_The_Page()
    {
        var source = Source(8);
        var view = GroupedPagedView(source, pageSize: 3);

        var added = Assert.IsType<Row>(view.AddNew());
        added.Value = 99;
        added.Team = "B";
        view.CommitNew();

        // Team B starts after every team A row, so the new row is not on the first page - which
        // goes back to the three rows it was showing.
        Assert.Equal(new[] { "A" }, GroupKeys(view));
        Assert.Equal(new[] { 0, 1, 2 }, Values(view));
        Assert.Contains(added, source);

        // Paging on to find it, the row is there under team B.
        Assert.True(AppearsOnSomePage(view, 99));
    }

    [Fact]
    public void Cancelling_A_New_Row_On_A_Grouped_Page_Puts_The_Page_Back()
    {
        var source = Source(8);
        var view = GroupedPagedView(source, pageSize: 3);

        var added = Assert.IsType<Row>(view.AddNew());
        view.CancelNew();

        Assert.Equal(new[] { "A" }, GroupKeys(view));
        Assert.Equal(new[] { 0, 1, 2 }, Values(view));
        Assert.DoesNotContain(added, source);
    }

    [Fact]
    public void Editing_A_Rows_Group_Moves_It_Under_The_Other_Header()
    {
        var view = GroupedPagedView(pageSize: 3);
        view.MoveToNextPage();
        Assert.Equal(new[] { 3 }, GroupValues(view, "A"));

        var edited = (Row)view.GetItemAt(0);
        view.EditItem(edited);
        edited.Team = "B";
        view.CommitEdit();

        // Row 3 is a team B row now. Team A has nothing left on this page, so its header goes.
        Assert.Equal(new[] { "B" }, GroupKeys(view));
        Assert.Contains(3, Values(view));
    }

    [Fact]
    public void Editing_A_Row_Out_Of_The_Filter_Empties_Its_Group()
    {
        var view = GroupedPagedView(pageSize: 4);
        view.Filter = o => ((Row)o).Value != int.MinValue;
        view.MoveToNextPage();
        Assert.Equal(new[] { "B" }, GroupKeys(view));

        var edited = (Row)view.GetItemAt(0);
        view.EditItem(edited);
        edited.Value = int.MinValue;
        view.CommitEdit();

        Assert.DoesNotContain(int.MinValue, Values(view));
    }

    // ---- transactions the view refuses --------------------------------------------------------

    [Fact]
    public void A_New_Row_Cannot_Be_Cancelled_While_Another_Row_Is_Being_Edited()
    {
        var view = GroupedPagedView(pageSize: 3);
        view.EditItem(view.GetItemAt(0));

        Assert.Throws<System.InvalidOperationException>(view.CancelNew);
        Assert.Throws<System.InvalidOperationException>(view.CommitNew);
    }

    [Fact]
    public void An_Edit_Cannot_Be_Committed_Or_Cancelled_While_A_Row_Is_Being_Added()
    {
        var view = GroupedPagedView(pageSize: 3);
        view.AddNew();

        Assert.Throws<System.InvalidOperationException>(view.CommitEdit);
        Assert.Throws<System.InvalidOperationException>(view.CancelEdit);
    }

    [Fact]
    public void A_Row_Cannot_Be_Added_To_A_List_The_View_Cannot_Create_Items_For()
    {
        // A read-only array is not something a new row can be appended to.
        var view = new DataGridCollectionView(new[] { new Row { Value = 1 } });

        Assert.False(view.CanAddNew);
        Assert.Throws<System.InvalidOperationException>(() => view.AddNew());
    }

    [Fact]
    public void An_Edit_Cannot_Be_Cancelled_On_A_Row_That_Cannot_Restore_Itself()
    {
        // Row keeps no copy of its previous values, so there is nothing to cancel back to and the
        // view says so rather than silently leaving the typed-in value in place.
        var view = GroupedPagedView(pageSize: 3);
        view.EditItem(view.GetItemAt(0));

        Assert.False(view.CanCancelEdit);
        Assert.Throws<System.InvalidOperationException>(view.CancelEdit);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static ObservableCollection<Row> Source(int count)
        => new(Enumerable.Range(0, count).Select(i => new Row { Value = i, Team = i < 4 ? "A" : "B" }));

    private static DataGridCollectionView GroupedPagedView(int pageSize, int count = 8)
        => GroupedPagedView(Source(count), pageSize);

    private static DataGridCollectionView GroupedPagedView(ObservableCollection<Row> source, int pageSize)
    {
        var view = new DataGridCollectionView(source);
        view.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(Row.Team)));
        view.PageSize = pageSize;
        return view;
    }

    /// <summary>Pages from the first page to the last looking for a row.</summary>
    private static bool AppearsOnSomePage(DataGridCollectionView view, int value)
    {
        view.MoveToFirstPage();
        do
        {
            if (Values(view).Contains(value))
            {
                return true;
            }
        }
        while (view.MoveToNextPage());

        return false;
    }

    private static int[] Values(DataGridCollectionView view)
        => Enumerable.Range(0, view.Count).Select(i => ((Row)view.GetItemAt(i)).Value).ToArray();

    private static string[] GroupKeys(DataGridCollectionView view)
        => view.Groups.Cast<DataGridCollectionViewGroup>().Select(g => (string)g.Key).ToArray();

    private static int[] GroupValues(DataGridCollectionView view, string key)
        => view.Groups
            .Cast<DataGridCollectionViewGroup>()
            .Single(g => (string)g.Key == key)
            .Items
            .Cast<Row>()
            .Select(r => r.Value)
            .ToArray();

    public class Row
    {
        public int Value { get; set; }
        public string Team { get; set; } = "A";
    }
}
