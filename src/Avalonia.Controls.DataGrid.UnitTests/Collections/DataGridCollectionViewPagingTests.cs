using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Collections;

/// <summary>
/// What a user sees in a grid that shows one page of rows at a time. A page is a window onto the
/// data: paging forward and back changes which rows are on screen, and every edit made through the
/// view - adding a row, deleting one, changing a value the view sorts by - has to keep that window
/// full and in the right place, pulling rows in from the next page and pushing rows off the end.
/// </summary>
public class DataGridCollectionViewPagingTests
{
    // ---- moving between pages ----------------------------------------------------------------

    [Fact]
    public void Paging_Forward_And_Back_Shows_Consecutive_Slices()
    {
        var view = PagedView(pageSize: 3, count: 8);

        Assert.Equal(new[] { 0, 1, 2 }, Values(view));

        Assert.True(view.MoveToNextPage());
        Assert.Equal(new[] { 3, 4, 5 }, Values(view));

        Assert.True(view.MoveToNextPage());
        Assert.Equal(new[] { 6, 7 }, Values(view));

        // The last page is short and there is nothing past it.
        Assert.False(view.MoveToNextPage());
        Assert.Equal(2, view.PageIndex);

        Assert.True(view.MoveToPreviousPage());
        Assert.Equal(new[] { 3, 4, 5 }, Values(view));

        Assert.True(view.MoveToFirstPage());
        Assert.Equal(new[] { 0, 1, 2 }, Values(view));

        // Already on the first page, so there is nothing to go back to.
        Assert.False(view.MoveToPreviousPage());

        Assert.True(view.MoveToLastPage());
        Assert.Equal(2, view.PageIndex);
        Assert.Equal(new[] { 6, 7 }, Values(view));
    }

    [Fact]
    public void Moving_To_A_Page_Makes_Its_First_Row_Current()
    {
        var view = PagedView(pageSize: 3, count: 8);
        Assert.Equal(0, ((Row)view.CurrentItem).Value);

        view.MoveToNextPage();

        Assert.Equal(3, ((Row)view.CurrentItem).Value);
        Assert.Equal(0, view.CurrentPosition);
    }

    [Fact]
    public void A_Page_Change_Can_Be_Refused_By_The_Application()
    {
        var view = PagedView(pageSize: 3, count: 8);
        var requested = new List<int>();
        view.PageChanging += (_, e) =>
        {
            requested.Add(e.NewPageIndex);
            e.Cancel = true;
        };

        Assert.False(view.MoveToNextPage());

        // The user clicked "next"; the application vetoed it, so the rows on screen do not change.
        Assert.Equal(new[] { 1 }, requested.ToArray());
        Assert.Equal(0, view.PageIndex);
        Assert.Equal(new[] { 0, 1, 2 }, Values(view));
    }

    [Fact]
    public void Making_The_Page_Bigger_Shows_More_Rows_From_The_Same_Place()
    {
        var view = PagedView(pageSize: 3, count: 8);

        view.PageSize = 4;

        Assert.Equal(new[] { 0, 1, 2, 3 }, Values(view));

        // Eight rows now fit in two pages rather than three.
        Assert.True(view.MoveToNextPage());
        Assert.Equal(new[] { 4, 5, 6, 7 }, Values(view));
        Assert.False(view.MoveToNextPage());
    }

    [Fact]
    public void Turning_Paging_Off_Shows_Every_Row()
    {
        var view = PagedView(pageSize: 3, count: 8);
        view.MoveToNextPage();

        view.PageSize = 0;

        Assert.Equal(Enumerable.Range(0, 8), Values(view));

        // There are no pages left to be on, which the view reports as -1 rather than page zero.
        Assert.Equal(-1, view.PageIndex);
    }

    // ---- deleting a row on a page ------------------------------------------------------------

    [Fact]
    public void Deleting_A_Row_Pulls_The_Next_Page_Up_To_Fill_The_Gap()
    {
        var view = PagedView(pageSize: 3, count: 8);

        var changes = Record(view);
        view.RemoveAt(1);

        // The page stays full: the row that used to head the next page slides into the space.
        Assert.Equal(new[] { 0, 2, 3 }, Values(view));

        Assert.Equal(
            new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add },
            changes.Select(c => c.Action).ToArray());
        Assert.Equal(1, changes[0].OldStartingIndex);
        Assert.Equal(2, changes[1].NewStartingIndex);
        Assert.Equal(3, ((Row)changes[1].NewItems![0]!).Value);
    }

    [Fact]
    public void Deleting_A_Row_On_The_Last_Page_Just_Shortens_It()
    {
        var view = PagedView(pageSize: 3, count: 8);
        view.MoveToLastPage();

        var changes = Record(view);
        view.RemoveAt(0);

        Assert.Equal(new[] { 7 }, Values(view));
        var removed = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Remove, removed.Action);
    }

    [Fact]
    public void Deleting_The_Only_Row_On_The_Last_Page_Takes_The_User_Back_A_Page()
    {
        var view = PagedView(pageSize: 3, count: 7);
        view.MoveToLastPage();
        Assert.Equal(2, view.PageIndex);
        Assert.Equal(new[] { 6 }, Values(view));

        view.RemoveAt(0);

        // The page the user was on no longer exists, so they end up on the last one that does
        // rather than staring at an empty grid.
        Assert.Equal(1, view.PageIndex);
        Assert.Equal(new[] { 3, 4, 5 }, Values(view));
    }

    // ---- adding a row while paging -----------------------------------------------------------

    [Fact]
    public void Adding_A_Row_On_A_Full_Page_Pushes_The_Last_Row_Onto_The_Next_Page()
    {
        var source = Source(8);
        var view = PagedView(source, pageSize: 3);

        var changes = Record(view);
        var added = Assert.IsType<Row>(view.AddNew());

        // Row 2 leaves this page to make room for the one being typed in.
        Assert.Equal(3, view.Count);
        Assert.Equal(new[] { 0, 1 }, Values(view).Take(2).ToArray());
        Assert.Same(added, view.GetItemAt(2));

        Assert.Equal(
            new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add },
            changes.Select(c => c.Action).ToArray());
        Assert.Equal(2, changes[0].OldStartingIndex);
        Assert.Equal(2, changes[1].NewStartingIndex);
    }

    [Fact]
    public void Cancelling_A_New_Row_On_A_Full_Page_Brings_The_Pushed_Off_Row_Back()
    {
        var source = Source(8);
        var view = PagedView(source, pageSize: 3);
        var added = Assert.IsType<Row>(view.AddNew());

        view.CancelNew();

        Assert.Equal(new[] { 0, 1, 2 }, Values(view));
        Assert.DoesNotContain(added, source);
    }

    [Fact]
    public void Committing_A_New_Row_Sends_It_To_The_Page_Its_Sort_Order_Puts_It_On()
    {
        var source = Source(8);
        var view = PagedView(source, pageSize: 3, sorted: true);

        var added = Assert.IsType<Row>(view.AddNew());
        added.Value = 99;
        view.CommitNew();

        // Sorted last, so it belongs on the final page - the user's current page goes back to
        // showing the three rows it started with.
        Assert.Equal(new[] { 0, 1, 2 }, Values(view));
        Assert.Contains(added, source);

        view.MoveToLastPage();
        Assert.Equal(new[] { 6, 7, 99 }, Values(view));
    }

    [Fact]
    public void Adding_A_Row_On_The_Last_Page_Does_Not_Push_Anything_Away()
    {
        var source = Source(7);
        var view = PagedView(source, pageSize: 3);
        view.MoveToLastPage();

        var changes = Record(view);
        var added = Assert.IsType<Row>(view.AddNew());
        view.CommitNew();

        Assert.Equal(2, view.Count);
        Assert.Same(added, view.GetItemAt(1));
        Assert.DoesNotContain(changes, c => c.Action == NotifyCollectionChangedAction.Remove &&
                                            c.OldStartingIndex == 0);
    }

    // ---- editing a row while paging ----------------------------------------------------------

    [Fact]
    public void Editing_A_Row_So_It_Sorts_Onto_A_Later_Page_Takes_It_Off_This_One()
    {
        var source = Source(9);
        var view = PagedView(source, pageSize: 3, sorted: true);
        var edited = (Row)view.GetItemAt(1);

        view.EditItem(edited);
        edited.Value = 99;
        view.CommitEdit();

        // The row is gone from the page the user is on, and the page has stayed full by taking
        // the next row in sort order.
        Assert.Equal(new[] { 0, 2, 3 }, Values(view));

        view.MoveToLastPage();
        Assert.Contains(99, Values(view));
    }

    [Fact]
    public void Editing_A_Row_So_It_Sorts_Onto_An_Earlier_Page_Pulls_A_Row_Down_From_It()
    {
        var source = Source(9);
        var view = PagedView(source, pageSize: 3, sorted: true);
        view.MoveToNextPage();
        Assert.Equal(new[] { 3, 4, 5 }, Values(view));

        var edited = (Row)view.GetItemAt(1);
        view.EditItem(edited);
        edited.Value = -1;
        view.CommitEdit();

        // Row -1 now heads the first page, which pushes row 2 down onto the page the user is on.
        Assert.Equal(new[] { 2, 3, 5 }, Values(view));

        view.MoveToFirstPage();
        Assert.Equal(new[] { -1, 0, 1 }, Values(view));
    }

    [Fact]
    public void Editing_A_Row_Out_Of_The_Filter_On_The_Last_Page_Removes_It()
    {
        var source = Source(7);
        var view = PagedView(source, pageSize: 3);
        view.Filter = o => ((Row)o).Value != int.MinValue;
        view.MoveToLastPage();
        Assert.Equal(new[] { 6 }, Values(view));

        var edited = (Row)view.GetItemAt(0);
        view.EditItem(edited);
        edited.Value = int.MinValue;
        view.CommitEdit();

        // Its page emptied out, so the user lands on the previous one.
        Assert.Equal(1, view.PageIndex);
        Assert.Equal(new[] { 3, 4, 5 }, Values(view));
    }

    // ---- a pending row blocks the page move --------------------------------------------------

    [Fact]
    public void A_Half_Typed_New_Row_Blocks_Paging_Away_From_It()
    {
        var view = PagedView(pageSize: 3, count: 8);
        view.AddNew();

        // Nothing has committed the pending row, so the page cannot change - the row would
        // otherwise be stranded on a page nobody is looking at.
        Assert.False(view.MoveToNextPage());
        Assert.Equal(0, view.PageIndex);

        view.CommitNew();
        Assert.True(view.MoveToNextPage());
    }

    [Fact]
    public void The_Page_Size_Cannot_Be_Changed_While_A_Row_Is_Being_Added()
    {
        var view = PagedView(pageSize: 3, count: 8);
        view.AddNew();

        Assert.Throws<System.InvalidOperationException>(() => view.PageSize = 4);
    }

    // ---- source changes while paging ---------------------------------------------------------

    [Fact]
    public void A_Row_Appended_By_The_Application_Shows_Up_On_The_Page_It_Belongs_To()
    {
        var source = Source(7);
        var view = PagedView(source, pageSize: 3);

        source.Add(new Row { Value = 7 });

        // Appended past the end, so the page the user is on is unaffected.
        Assert.Equal(new[] { 0, 1, 2 }, Values(view));

        view.MoveToLastPage();
        Assert.Equal(2, view.PageIndex);
        Assert.Equal(new[] { 6, 7 }, Values(view));
    }

    [Fact]
    public void A_Row_Inserted_Above_The_Current_Page_Shifts_The_Page_Down()
    {
        var source = Source(9);
        var view = PagedView(source, pageSize: 3);
        view.MoveToNextPage();
        Assert.Equal(new[] { 3, 4, 5 }, Values(view));

        source.Insert(0, new Row { Value = -1 });

        // Everything moved one place along, so the second page now starts a row earlier.
        Assert.Equal(new[] { 2, 3, 4 }, Values(view));
    }

    [Fact]
    public void A_Row_Removed_By_The_Application_Above_The_Current_Page_Shifts_The_Page_Up()
    {
        var source = Source(9);
        var view = PagedView(source, pageSize: 3);
        view.MoveToNextPage();

        source.RemoveAt(0);

        Assert.Equal(new[] { 4, 5, 6 }, Values(view));
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static ObservableCollection<Row> Source(int count)
        => new(Enumerable.Range(0, count).Select(i => new Row { Value = i }));

    private static DataGridCollectionView PagedView(int pageSize, int count)
        => PagedView(Source(count), pageSize);

    private static DataGridCollectionView PagedView(
        ObservableCollection<Row> source,
        int pageSize,
        bool sorted = false)
    {
        var view = new DataGridCollectionView(source);
        if (sorted)
        {
            view.SortDescriptions.Add(
                DataGridSortDescription.FromPath(nameof(Row.Value), ListSortDirection.Ascending));
        }

        view.PageSize = pageSize;
        return view;
    }

    private static List<NotifyCollectionChangedEventArgs> Record(DataGridCollectionView view)
    {
        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);
        return changes;
    }

    /// <summary>
    /// The rows the page is showing, read the way the grid reads them - by index, which is what a
    /// row's slot resolves to.
    /// </summary>
    private static int[] Values(DataGridCollectionView view)
        => Enumerable.Range(0, view.Count).Select(i => ((Row)view.GetItemAt(i)).Value).ToArray();

    public class Row
    {
        public int Value { get; set; }
    }
}
