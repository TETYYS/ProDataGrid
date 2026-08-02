using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Collections;

/// <summary>
/// Reading the rows of a paged view while the user is part way through adding one. Whether the rows
/// are walked through or asked for by index, the view has to describe the same page - anything else
/// and a consumer that does both, as bindings and exports do, sees two different grids.
/// </summary>
public class DataGridCollectionViewPagedAddNewConsistencyTests
{
    [Fact]
    public void Walking_A_Paged_View_While_Adding_Gives_The_Same_Rows_As_Asking_By_Index()
    {
        var source = Source(8);
        var view = new DataGridCollectionView(source) { PageSize = 3 };

        var added = (Row)view.AddNew();
        added.Value = 99;

        // The page gave its last slot to the row being typed in, so row 2 has moved to the next
        // page. Walking the view used to hand back row 2 as well, one row more than Count.
        Assert.Equal(new[] { 0, 1, 99 }, Enumerated(view));
        Assert.Equal(new[] { 0, 1, 99 }, ByIndex(view));
        Assert.Equal(3, view.Count);
    }

    [Fact]
    public void The_Displaced_Row_Comes_Back_When_The_New_Row_Is_Abandoned()
    {
        var source = Source(8);
        var view = new DataGridCollectionView(source) { PageSize = 3 };

        view.AddNew();
        Assert.DoesNotContain(2, Enumerated(view));

        view.CancelNew();

        // The row only stepped aside to make room for the one being typed in.
        Assert.Equal(new[] { 0, 1, 2 }, Enumerated(view));
        Assert.Equal(new[] { 0, 1, 2 }, ByIndex(view));
    }

    [Fact]
    public void Walking_A_Sorted_Paged_View_While_Adding_Agrees_With_The_Index()
    {
        var source = Source(8);
        var view = new DataGridCollectionView(source) { PageSize = 3 };
        view.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(Row.Value), ListSortDirection.Descending));

        var added = (Row)view.AddNew();
        added.Value = 99;

        Assert.Equal(ByIndex(view), Enumerated(view));
        Assert.Equal(view.Count, Enumerated(view).Length);
    }

    [Fact]
    public void Walking_A_Page_With_No_Pending_Row_Is_Unchanged()
    {
        var view = new DataGridCollectionView(Source(8)) { PageSize = 3 };

        Assert.Equal(new[] { 0, 1, 2 }, Enumerated(view));

        view.MoveToLastPage();
        Assert.Equal(new[] { 6, 7 }, Enumerated(view));
    }

    [Fact]
    public void Walking_An_Unpaged_View_While_Adding_Still_Puts_The_New_Row_Last()
    {
        var view = new DataGridCollectionView(Source(4));
        view.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(Row.Value), ListSortDirection.Ascending));

        var added = (Row)view.AddNew();
        added.Value = -1;

        // Still being typed in, so it is not sorted into place yet - it sits at the end, and the
        // walk agrees with the index about that.
        Assert.Equal(new[] { 0, 1, 2, 3, -1 }, Enumerated(view));
        Assert.Equal(new[] { 0, 1, 2, 3, -1 }, ByIndex(view));
    }

    private static ObservableCollection<Row> Source(int count)
        => new(Enumerable.Range(0, count).Select(i => new Row { Value = i }));

    private static int[] Enumerated(DataGridCollectionView view)
    {
        var values = new List<int>();
        foreach (Row row in view)
        {
            values.Add(row.Value);
        }

        return values.ToArray();
    }

    private static int[] ByIndex(DataGridCollectionView view)
        => Enumerable.Range(0, view.Count).Select(i => ((Row)view.GetItemAt(i)).Value).ToArray();

    public class Row
    {
        public int Value { get; set; }
    }
}
