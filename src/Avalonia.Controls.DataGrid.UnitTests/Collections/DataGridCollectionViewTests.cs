using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Reflection;
using Avalonia.Collections;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.Selection;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Collections;

public class DataGridCollectionViewTests
{
    [Fact]
    public void Move_Reorders_View_And_Raises_Move()
    {
        var items = new ObservableCollection<int> { 1, 2, 3, 4 };
        var view = new DataGridCollectionView(items);

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items.Move(1, 3);

        Assert.Equal(new[] { 1, 3, 4, 2 }, view.Cast<int>().ToArray());

        var move = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Move, move.Action);
        Assert.Equal(1, move.OldStartingIndex);
        Assert.Equal(3, move.NewStartingIndex);
        var oldItems = Assert.IsAssignableFrom<IList>(move.OldItems);
        Assert.Equal(2, Assert.Single(oldItems.Cast<int>()));
        var newItems = Assert.IsAssignableFrom<IList>(move.NewItems);
        Assert.Equal(2, Assert.Single(newItems.Cast<int>()));
    }

    [AvaloniaFact]
    public void Move_Does_Not_Report_Selected_Item_As_Deselected()
    {
        var first = new object();
        var selected = new object();
        var third = new object();
        var fourth = new object();
        var items = new ObservableCollection<object> { first, selected, third, fourth };
        var view = new DataGridCollectionView(items);
        var selection = new DataGridSelectionModel<object>
        {
            SingleSelect = false
        };
        var grid = new DataGrid
        {
            ItemsSource = view,
            Selection = selection,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false
        };

        var changes = new List<DataGridSelectionModelChangedEventArgs<object>>();
        selection.SelectionChanged += (_, e) => changes.Add(e);

        selection.SelectAt(1);
        changes.Clear();

        items.Move(1, 3);

        Assert.Equal(new[] { first, third, fourth, selected }, view.Cast<object>().ToArray());
        Assert.Single(selection.SelectedItems);
        Assert.Same(selected, selection.SelectedItems[0]);

        // The reported index follows the row. It used to stay at 1 - knowingly stale, because the
        // model stored the index rather than the item and a move could not be expressed.
        Assert.Equal(3, selection.SelectedIndex);
        Assert.True(grid.GetRowSelectionFromRowIndex(3));
        Assert.False(grid.GetRowSelectionFromRowIndex(1));

        // A move is neither a selection nor a deselection, so nothing at all is reported.
        Assert.Empty(changes);
    }

    [AvaloniaFact]
    public void Move_Does_Not_Expand_Selection_During_Row_Preparation()
    {
        var items = new ObservableCollection<object>(
            Enumerable.Range(0, 60).Select(i => (object)new MoveItem(i)));
        var selected = items[4];
        var view = new DataGridCollectionView(items);
        var selection = new DataGridSelectionModel<object>
        {
            SingleSelect = false
        };
        var grid = new DataGrid
        {
            ItemsSource = view,
            Selection = selection,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.All
        };
        grid.ColumnsInternal.Add(new DataGridTextColumn { Header = "Value", Binding = new Binding(nameof(MoveItem.Value)) });

        var window = new Window
        {
            Width = 260,
            Height = 130,
            Content = grid
        };
        window.SetThemeStyles();

        try
        {
            window.Show();
            PumpLayout(window, grid);

            selection.SelectAt(4);
            PumpLayout(window, grid);

            var changes = new List<DataGridSelectionModelChangedEventArgs<object>>();
            selection.SelectionChanged += (_, e) => changes.Add(e);

            foreach (var targetIndex in new[] { 22, 2, 35, 7, 40, 1 })
            {
                var currentIndex = items.IndexOf(selected);
                items.Move(currentIndex, targetIndex);
                PumpLayout(window, grid);

                var selectedItem = Assert.Single(selection.SelectedItems);
                Assert.Same(selected, selectedItem);
            }

            Assert.DoesNotContain(changes, e => e.SelectedItems.Count > 0 || e.DeselectedItems.Count > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Move_With_Filter_Raises_Move_For_Filtered_Index()
    {
        var two = new FilterItem(2);
        var four = new FilterItem(4);
        var six = new FilterItem(6);
        var items = new ObservableCollection<FilterItem>
        {
            new(1),
            two,
            new(3),
            four,
            six
        };
        var view = new DataGridCollectionView(items)
        {
            Filter = item => ((FilterItem)item).Value % 2 == 0
        };

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items.Move(1, 4);

        Assert.Equal(new[] { 4, 6, 2 }, view.Cast<FilterItem>().Select(x => x.Value).ToArray());
        var move = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Move, move.Action);
        Assert.Equal(0, move.OldStartingIndex);
        Assert.Equal(2, move.NewStartingIndex);
        Assert.Same(two, Assert.Single(move.OldItems!.Cast<FilterItem>()));
    }

    [Fact]
    public void Uses_SourceList_For_ObservableCollection_When_No_Local_Transforms()
    {
        var items = new ObservableCollection<int> { 1, 2, 3 };
        var view = new DataGridCollectionView(items);

        var internalList = GetInternalList(view);

        Assert.Same(items, internalList);
    }

    [Fact]
    public void Uses_Local_Array_When_Sorting_And_Reverts_When_Cleared()
    {
        var items = new ObservableCollection<int> { 2, 1 };
        var view = new DataGridCollectionView(items);

        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(Comparer<int>.Default));

        var sortedInternal = GetInternalList(view);
        Assert.NotSame(items, sortedInternal);

        view.SortDescriptions.Clear();

        var restoredInternal = GetInternalList(view);
        Assert.Same(items, restoredInternal);
    }

    [Fact]
    public void Move_Preserves_Current_Item()
    {
        var items = new ObservableCollection<string> { "a", "b", "c", "d" };
        var view = new DataGridCollectionView(items);

        Assert.True(view.MoveCurrentTo("c"));
        Assert.Equal("c", view.CurrentItem);
        Assert.Equal(2, view.CurrentPosition);

        items.Move(2, 0);

        Assert.Equal("c", view.CurrentItem);
        Assert.Equal(0, view.CurrentPosition);
    }

    [Fact]
    public void Swap_With_Remove_And_Insert_Updates_View()
    {
        var items = new ObservableCollection<int> { 10, 20, 30, 40 };
        var view = new DataGridCollectionView(items);

        Swap(items, 1, 3);

        Assert.Equal(new[] { 10, 40, 30, 20 }, view.Cast<int>().ToArray());
    }

    [Fact]
    public void Move_Reapplies_Sorting()
    {
        var items = new ObservableCollection<Row>
        {
            new() { Value = 3 },
            new() { Value = 1 },
            new() { Value = 2 }
        };

        var view = new DataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(Row.Value), ListSortDirection.Ascending));

        Assert.Equal(new[] { 1, 2, 3 }, view.Cast<Row>().Select(x => x.Value).ToArray());

        items.Move(0, 2);

        Assert.Equal(new[] { 1, 2, 3 }, view.Cast<Row>().Select(x => x.Value).ToArray());
    }

    [Fact]
    public void Add_Raises_Add_And_Updates_View()
    {
        var items = new ObservableCollection<int> { 1, 2 };
        var view = new DataGridCollectionView(items);

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items.Add(3);

        Assert.Equal(new[] { 1, 2, 3 }, view.Cast<int>().ToArray());

        var add = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, add.Action);
        var newItems = Assert.IsAssignableFrom<IList>(add.NewItems);
        Assert.Equal(3, Assert.Single(newItems.Cast<int>()));
        Assert.Equal(2, add.NewStartingIndex);
    }

    [Fact]
    public void AddNew_Uses_PreAdd_Count_For_Index_When_Using_SourceList()
    {
        var items = new ObservableCollection<SimpleItem>
        {
            new() { Value = 1 },
            new() { Value = 2 }
        };
        var view = new DataGridCollectionView(items);

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        var newItem = Assert.IsType<SimpleItem>(view.AddNew());

        Assert.Equal(3, view.Count);
        Assert.Same(newItem, items[2]);
        Assert.Equal(2, view.IndexOf(newItem));

        var add = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, add.Action);
        Assert.Equal(2, add.NewStartingIndex);

        view.CommitNew();
    }

    [Fact]
    public void Remove_Raises_Remove_And_Updates_View()
    {
        var items = new ObservableCollection<int> { 1, 2, 3 };
        var view = new DataGridCollectionView(items);

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items.RemoveAt(1);

        Assert.Equal(new[] { 1, 3 }, view.Cast<int>().ToArray());

        var remove = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Remove, remove.Action);
        var oldItems = Assert.IsAssignableFrom<IList>(remove.OldItems);
        Assert.Equal(2, Assert.Single(oldItems.Cast<int>()));
        Assert.Equal(1, remove.OldStartingIndex);
    }

    [Fact]
    public void RemoveAt_Using_SourceList_Removes_Only_One_Duplicate()
    {
        var items = new ObservableCollection<int> { 1, 2, 2, 3 };
        var view = new DataGridCollectionView(items);

        view.RemoveAt(1);

        Assert.Equal(new[] { 1, 2, 3 }, items.ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, view.Cast<int>().ToArray());
    }

    [Fact]
    public void Remove_Filtered_Out_Item_Does_Not_Raise_Remove()
    {
        var items = new ObservableCollection<int> { 1, 2, 3, 4 };
        var view = new DataGridCollectionView(items)
        {
            Filter = item => (int)item % 2 == 0
        };

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items.Remove(1);

        Assert.Empty(changes);
        Assert.Equal(new[] { 2, 4 }, view.Cast<int>().ToArray());
    }

    [Fact]
    public void Replace_Raises_One_Replace_And_Updates_View()
    {
        var items = new ObservableCollection<int> { 1, 2, 3 };
        var view = new DataGridCollectionView(items);

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items[1] = 5;

        Assert.Equal(new[] { 1, 5, 3 }, view.Cast<int>().ToArray());

        // One Replace, passed through as it arrived. The view used to split it into a Remove and
        // an Add - it even carried an isReplace flag down into ProcessRemoveEvent to patch up the
        // difference - which told consumers the item had been taken away and an unrelated one put
        // in its place. Anything keyed on the item, selection above all, lost the row that way.
        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Replace, change.Action);
        Assert.Equal(2, Assert.Single(Assert.IsAssignableFrom<IList>(change.OldItems).Cast<int>()));
        Assert.Equal(5, Assert.Single(Assert.IsAssignableFrom<IList>(change.NewItems).Cast<int>()));
        Assert.Equal(1, change.OldStartingIndex);
        Assert.Equal(1, change.NewStartingIndex);
    }

    [Fact]
    public void Replace_When_Sorted_Repositions_The_Replacement()
    {
        var items = new ObservableCollection<int> { 1, 2, 3 };
        var view = new DataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(Comparer<int>.Default));

        Assert.Equal(new[] { 1, 2, 3 }, view.Cast<int>().ToArray());

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items[1] = 5;

        // Sorting is evaluated on an insertion, so it is evaluated here too - 5 belongs at the end.
        // This used to come out as {1,5,3}: the sorted-insert check skipped the last position, so
        // the replacement stayed where the removal had left it.
        Assert.Equal(new[] { 1, 3, 5 }, view.Cast<int>().ToArray());

        // A replacement that has to move is a replacement and a move, not a removal and an
        // insertion. Both keep the row, so whatever is keyed on the item keeps its hold on it.
        Assert.Collection(
            changes,
            e =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Replace, e.Action);
                Assert.Equal(2, Assert.Single(Assert.IsAssignableFrom<IList>(e.OldItems).Cast<int>()));
                Assert.Equal(5, Assert.Single(Assert.IsAssignableFrom<IList>(e.NewItems).Cast<int>()));
                Assert.Equal(1, e.OldStartingIndex);
            },
            e =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Move, e.Action);
                Assert.Equal(1, e.OldStartingIndex);
                Assert.Equal(2, e.NewStartingIndex);
            });
    }

    [Fact]
    public void Replace_When_Sorted_Carries_Currency_Along_With_The_Item_It_Is_On()
    {
        var items = new ObservableCollection<int> { 1, 2, 3, 4 };
        var view = new DataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(Comparer<int>.Default));

        view.MoveCurrentToPosition(2);
        Assert.Equal(3, view.CurrentItem);

        // 2 becomes 5, which belongs at the end, so 3 and 4 each slide one place towards the front.
        items[1] = 5;

        Assert.Equal(new[] { 1, 3, 4, 5 }, view.Cast<int>().ToArray());

        // Currency is on an item, not on the index that item happened to be at. Without this the
        // position stayed at 2 and started reporting 4 as the current item.
        Assert.Equal(3, view.CurrentItem);
        Assert.Equal(1, view.CurrentPosition);
    }

    [Fact]
    public void Replace_When_Sorted_Moves_Currency_Onto_The_Replacement()
    {
        var items = new ObservableCollection<int> { 1, 2, 3, 4 };
        var view = new DataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(Comparer<int>.Default));

        view.MoveCurrentToPosition(1);
        Assert.Equal(2, view.CurrentItem);

        // The current item is the one being replaced, so currency follows the replacement to
        // wherever the sort puts it.
        items[1] = 5;

        Assert.Equal(new[] { 1, 3, 4, 5 }, view.Cast<int>().ToArray());
        Assert.Equal(5, view.CurrentItem);
        Assert.Equal(3, view.CurrentPosition);
    }

    [Fact]
    public void Replace_When_Sorted_Raises_Only_A_Replace_If_The_Position_Holds()
    {
        var items = new ObservableCollection<int> { 1, 2, 5 };
        var view = new DataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(Comparer<int>.Default));

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items[1] = 3; // still sorts between 1 and 5

        Assert.Equal(new[] { 1, 3, 5 }, view.Cast<int>().ToArray());

        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Replace, change.Action);
    }

    [Fact]
    public void Move_When_Sorted_Changes_Nothing_And_Raises_Nothing()
    {
        var items = new ObservableCollection<int> { 3, 1, 2 };
        var view = new DataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(Comparer<int>.Default));

        Assert.Equal(new[] { 1, 2, 3 }, view.Cast<int>().ToArray());

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items.Move(0, 2);

        // Sorted order comes from the comparer, and moving an item in the source changes no sort
        // key. The view was already showing the right thing, so there is nothing to report. This
        // used to refresh, announcing a wholesale replacement of a collection that had not visibly
        // changed at all.
        Assert.Equal(new[] { 1, 2, 3 }, view.Cast<int>().ToArray());
        Assert.Empty(changes);
    }

    [Fact]
    public void Replace_When_Sorted_Moves_The_Replacement_Towards_The_Front()
    {
        var items = new ObservableCollection<int> { 1, 3, 5 };
        var view = new DataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(Comparer<int>.Default));

        Assert.Equal(new[] { 1, 3, 5 }, view.Cast<int>().ToArray());

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        // The last row becomes a value that sorts second, so it has to travel back over the row
        // above it. The companion test covers a replacement moving the other way; the two
        // directions slide the rows in between opposite ways and only one of them can be right.
        items[2] = 2;

        Assert.Equal(new[] { 1, 2, 3 }, view.Cast<int>().ToArray());

        Assert.Collection(
            changes,
            e =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Replace, e.Action);
                Assert.Equal(5, Assert.Single(Assert.IsAssignableFrom<IList>(e.OldItems).Cast<int>()));
                Assert.Equal(2, Assert.Single(Assert.IsAssignableFrom<IList>(e.NewItems).Cast<int>()));
                Assert.Equal(2, e.OldStartingIndex);
            },
            e =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Move, e.Action);
                Assert.Equal(2, e.OldStartingIndex);
                Assert.Equal(1, e.NewStartingIndex);
            });
    }

    [Fact]
    public void Replace_When_Sorted_Carries_Currency_Along_As_Rows_Slide_Down()
    {
        var items = new ObservableCollection<int> { 1, 3, 5, 7 };
        var view = new DataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(Comparer<int>.Default));

        view.MoveCurrentToPosition(1);
        Assert.Equal(3, view.CurrentItem);

        // 7 becomes 2, which belongs second, so 3 and 5 each slide one place towards the end.
        items[3] = 2;

        Assert.Equal(new[] { 1, 2, 3, 5 }, view.Cast<int>().ToArray());

        // Currency is on an item, not on the index that item happened to be at.
        Assert.Equal(3, view.CurrentItem);
        Assert.Equal(2, view.CurrentPosition);
    }

    [Fact]
    public void Replace_When_Filtered_Takes_The_Row_Away_If_The_Replacement_Does_Not_Match()
    {
        var items = new ObservableCollection<int> { 1, 2, 3, 4 };
        var view = new DataGridCollectionView(items)
        {
            Filter = item => (int)item % 2 == 0
        };

        Assert.Equal(new[] { 2, 4 }, view.Cast<int>().ToArray());

        // A filter can admit one of a replaced pair and not the other, so this cannot be reported
        // as one row changing what it shows - the row genuinely goes away.
        items[1] = 7;

        Assert.Equal(new[] { 4 }, view.Cast<int>().ToArray());
    }

    [Fact]
    public void Replace_When_Filtered_Brings_A_Row_Back_If_The_Replacement_Matches()
    {
        var items = new ObservableCollection<int> { 1, 2, 3 };
        var view = new DataGridCollectionView(items)
        {
            Filter = item => (int)item % 2 == 0
        };

        Assert.Equal(new[] { 2 }, view.Cast<int>().ToArray());

        // The other half of the same case: what was filtered out is swapped for something that
        // passes, and a row appears where there was none.
        items[0] = 4;

        Assert.Equal(new[] { 4, 2 }, view.Cast<int>().ToArray());
    }

    [Fact]
    public void MoveRange_When_Filtered_Leaves_The_Visible_Rows_In_Source_Order()
    {
        var items = new ObservableRangeCollection<int> { 1, 2, 3, 4, 5, 6 };
        var view = new DataGridCollectionView(items)
        {
            Filter = item => (int)item % 2 == 0
        };

        Assert.Equal(new[] { 2, 4, 6 }, view.Cast<int>().ToArray());

        // A filter breaks the correspondence between source positions and view positions, so a run
        // that travels together at the source is not a run here - only two of these three rows are
        // even on screen, and they are not adjacent.
        items.MoveRange(0, 4, 2);

        Assert.Equal(new[] { 3, 4, 5, 6, 1, 2 }, items.ToArray());
        Assert.Equal(new[] { 4, 6, 2 }, view.Cast<int>().ToArray());
    }

    [Fact]
    public void MoveRange_When_Sorted_Changes_Nothing_And_Raises_Nothing()
    {
        var items = new ObservableRangeCollection<int> { 5, 6, 1, 2, 3, 4 };
        var view = new DataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(Comparer<int>.Default));

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, view.Cast<int>().ToArray());

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        // Same as moving a single item: sorted order comes from the comparer, and relocating a run
        // in the source changes no item's sort key. Nothing the user can see has moved, so nothing
        // is reported - a refresh here would announce a wholesale replacement of the rows.
        items.MoveRange(0, 4, 2);

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, view.Cast<int>().ToArray());
        Assert.Empty(changes);
    }

    [Fact]
    public void Reset_Raises_Reset_And_Clears_View()
    {
        var items = new ObservableCollection<int> { 1, 2, 3 };
        var view = new DataGridCollectionView(items);

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        items.Clear();

        Assert.Empty(view.Cast<int>());
        var reset = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, reset.Action);
    }

    private static void Swap<T>(ObservableCollection<T> items, int sourceIndex, int targetIndex)
    {
        var first = items[sourceIndex];
        var second = items[targetIndex];

        items.RemoveAt(sourceIndex);
        items.Insert(sourceIndex, second);

        items.RemoveAt(targetIndex);
        items.Insert(targetIndex, first);
    }

    private static void PumpLayout(Window window, DataGrid grid)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private class Row
    {
        public int Value { get; set; }
    }

    private sealed class FilterItem
    {
        public FilterItem(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    private sealed class MoveItem
    {
        public MoveItem(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    private class SimpleItem
    {
        public int Value { get; set; }
    }

    private static IList GetInternalList(DataGridCollectionView view)
    {
        var field = typeof(DataGridCollectionView)
            .GetField("_internalList", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<IList>(field!.GetValue(view));
    }

    [Fact]
    public void AddNew_On_BindingList_Adds_Row()
    {
        var table = new DataTable();
        table.Columns.Add(new DataColumn("Value", typeof(int)));
        var view = new DataGridCollectionView(table.DefaultView);

        Assert.Equal(0, table.Rows.Count);
        var newRow = Assert.IsType<DataRowView>(view.AddNew());
        Assert.Equal(0, table.Rows.Count);
        newRow["Value"] = 42;
        view.CommitNew();

        var values = table.Rows.Cast<DataRow>().Select(r => (int)r["Value"]).ToArray();

        Assert.Equal(new[] { 42 }, values);
        Assert.Equal(1, view.Count);
    }

    [Fact]
    public void BindingList_Add_Updates_View()
    {
        var table = new DataTable();
        table.Columns.Add(new DataColumn("Value", typeof(int)));
        var view = new DataGridCollectionView(table.DefaultView);

        table.Rows.Add(1);
        table.Rows.Add(2);

        Assert.Equal(2, view.Count);
        Assert.Equal(new[] { 1, 2 }, view.Cast<DataRowView>().Select(r => (int)r["Value"]).ToArray());
    }

    [Fact]
    public void BindingList_SortDescriptions_Apply_Sort()
    {
        var table = new DataTable();
        table.Columns.Add(new DataColumn("Value", typeof(int)));
        table.Rows.Add(2);
        table.Rows.Add(1);
        var view = new DataGridCollectionView(table.DefaultView);

        view.SortDescriptions.Add(DataGridSortDescription.FromPath("Value", ListSortDirection.Ascending));

        Assert.Equal(new[] { 1, 2 }, view.Cast<DataRowView>().Select(r => (int)r["Value"]).ToArray());
    }

    [Fact]
    public void BindingList_ItemChanged_Raises_Remove_Then_Add()
    {
        var source = new BindingList<int> { 1 };
        var view = new DataGridCollectionView(source);

        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);

        source[0] = 42;

        Assert.NotEmpty(changes);
        if (changes.Count == 1)
        {
            Assert.Equal(NotifyCollectionChangedAction.Reset, changes[0].Action);
        }
        else
        {
            Assert.Collection(
                changes,
                e =>
                {
                    Assert.Equal(NotifyCollectionChangedAction.Remove, e.Action);
                    Assert.Equal(0, e.OldStartingIndex);
                    var oldItems = Assert.IsAssignableFrom<IList>(e.OldItems);
                    Assert.Single(oldItems);
                },
                e =>
                {
                    Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
                    Assert.Equal(0, e.NewStartingIndex);
                    var newItems = Assert.IsAssignableFrom<IList>(e.NewItems);
                    Assert.Single(newItems);
                });
        }

        Assert.Equal(42, (int)view[0]);
    }

    [Fact]
    public void WeakCollectionChangedHandler_Unsubscribes_When_Target_Lost()
    {
        var source = new TrackingNotifyCollectionChanged();
        var weak = new WeakReference<DataGridCollectionView>(null!);
        var handler = CreateWeakCollectionChangedHandler(weak);

        source.CollectionChanged += handler;
        Assert.Equal(1, source.SubscriptionCount);

        source.RaiseReset();

        Assert.Equal(0, source.SubscriptionCount);
    }

    [Fact]
    public void WeakListChangedHandler_Unsubscribes_When_Target_Lost()
    {
        var source = new TrackingBindingList();
        var weak = new WeakReference<DataGridCollectionView>(null!);
        var handler = CreateWeakListChangedHandler(weak);

        source.ListChanged += handler;
        Assert.Equal(1, source.SubscriptionCount);

        source.RaiseReset();

        Assert.Equal(0, source.SubscriptionCount);
    }

    private static NotifyCollectionChangedEventHandler CreateWeakCollectionChangedHandler(
        WeakReference<DataGridCollectionView> weak)
    {
        var method = typeof(DataGridCollectionView)
            .GetMethod("CreateWeakCollectionChangedHandler", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<NotifyCollectionChangedEventHandler>(method!.Invoke(null, new object[] { weak }));
    }

    private static ListChangedEventHandler CreateWeakListChangedHandler(
        WeakReference<DataGridCollectionView> weak)
    {
        var method = typeof(DataGridCollectionView)
            .GetMethod("CreateWeakListChangedHandler", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<ListChangedEventHandler>(method!.Invoke(null, new object[] { weak }));
    }

    private sealed class TrackingNotifyCollectionChanged : INotifyCollectionChanged
    {
        private NotifyCollectionChangedEventHandler? _collectionChanged;

        public int SubscriptionCount { get; private set; }

        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add
            {
                _collectionChanged += value;
                SubscriptionCount = _collectionChanged?.GetInvocationList().Length ?? 0;
            }
            remove
            {
                _collectionChanged -= value;
                SubscriptionCount = _collectionChanged?.GetInvocationList().Length ?? 0;
            }
        }

        public void RaiseReset()
        {
            _collectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    private sealed class TrackingBindingList : IBindingList
    {
        private readonly List<object?> _items = new();
        private ListChangedEventHandler? _listChanged;

        public int SubscriptionCount { get; private set; }

        public event ListChangedEventHandler? ListChanged
        {
            add
            {
                _listChanged += value;
                SubscriptionCount = _listChanged?.GetInvocationList().Length ?? 0;
            }
            remove
            {
                _listChanged -= value;
                SubscriptionCount = _listChanged?.GetInvocationList().Length ?? 0;
            }
        }

        public void RaiseReset()
        {
            _listChanged?.Invoke(this, new ListChangedEventArgs(ListChangedType.Reset, -1));
        }

        public bool AllowNew => true;
        public bool AllowEdit => true;
        public bool AllowRemove => true;
        public bool SupportsChangeNotification => true;
        public bool SupportsSearching => false;
        public bool SupportsSorting => false;
        public bool IsSorted => false;
        public ListSortDirection SortDirection => ListSortDirection.Ascending;
        public PropertyDescriptor? SortProperty => null;

        public object AddNew() => throw new NotSupportedException();
        public void AddIndex(PropertyDescriptor property) => throw new NotSupportedException();
        public void ApplySort(PropertyDescriptor property, ListSortDirection direction) => throw new NotSupportedException();
        public int Find(PropertyDescriptor property, object key) => -1;
        public void RemoveIndex(PropertyDescriptor property) => throw new NotSupportedException();
        public void RemoveSort() => throw new NotSupportedException();

        public int Add(object? value)
        {
            _items.Add(value);
            return _items.Count - 1;
        }

        public void Clear() => _items.Clear();
        public bool Contains(object? value) => _items.Contains(value);
        public int IndexOf(object? value) => _items.IndexOf(value);
        public void Insert(int index, object? value) => _items.Insert(index, value);
        public void Remove(object? value) => _items.Remove(value);
        public void RemoveAt(int index) => _items.RemoveAt(index);
        public object? this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public bool IsFixedSize => false;
        public bool IsReadOnly => false;
        public int Count => _items.Count;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        public IEnumerator GetEnumerator() => _items.GetEnumerator();
    }
}
