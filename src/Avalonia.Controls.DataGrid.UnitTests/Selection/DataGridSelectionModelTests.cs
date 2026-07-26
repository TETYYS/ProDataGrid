using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls.DataGridSelection;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

/// <summary>
/// The selection model stores items and derives every index from the view. These cover the property
/// that buys: reordering the view - a move, a re-sort - changes what the indexes read without changing
/// the selection, and without the model being told anything.
/// </summary>
public class DataGridSelectionModelTests
{
    [Fact]
    public void Move_Updates_SelectedIndex_Without_Changing_Selection()
    {
        var items = new ObservableCollection<string> { "a", "b", "c", "d" };
        var (model, _) = CreateModel(items);
        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        model.Select("b");
        Assert.Equal(1, model.SelectedIndex);
        changes.Clear();

        items.Move(1, 3);

        Assert.Equal(new[] { "a", "c", "d", "b" }, items.ToArray());
        Assert.Equal(3, model.SelectedIndex);
        Assert.Equal("b", model.SelectedItem);
        Assert.Empty(changes);
    }

    [Fact]
    public void Resort_Updates_SelectedIndex_Without_Changing_Selection()
    {
        var items = new ObservableCollection<string> { "c", "a", "b" };
        var view = new DataGridCollectionView(items);
        var (model, _) = CreateModel(view);
        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        model.Select("c");
        Assert.Equal(0, model.SelectedIndex);
        changes.Clear();

        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(
            Comparer<object>.Create((x, y) => string.CompareOrdinal((string?)x, (string?)y))));

        Assert.Equal(new[] { "a", "b", "c" }, view.Cast<string>().ToArray());
        Assert.Equal(2, model.SelectedIndex);
        Assert.Equal("c", model.SelectedItem);
        Assert.Empty(changes);
    }

    [Fact]
    public void Insert_Above_Selection_Shifts_SelectedIndex()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);

        model.Select("c");
        Assert.Equal(2, model.SelectedIndex);

        items.Insert(0, "z");

        Assert.Equal(3, model.SelectedIndex);
        Assert.Equal("c", model.SelectedItem);
    }

    [Fact]
    public void Removing_The_Selected_Item_Deselects_It()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);

        model.Select("b");
        model.RemoveItems(new[] { "b" });

        Assert.Equal(0, model.Count);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.Null(model.SelectedItem);
    }

    [Fact]
    public void SelectRange_Binds_To_Items_Not_Positions()
    {
        var items = new ObservableCollection<string> { "a", "b", "c", "d" };
        var (model, _) = CreateModel(items);

        model.SelectRange(1, 2);
        Assert.Equal(new object?[] { "b", "c" }, model.SelectedItems.ToArray());

        // The range was resolved to items when it was made, so moving one of them keeps it selected
        // and simply changes where it reports as being.
        items.Move(1, 3);

        Assert.Equal(new[] { "a", "c", "d", "b" }, items.ToArray());
        Assert.Equal(new object?[] { "c", "b" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void SelectedItems_Are_Reported_In_View_Order()
    {
        var items = new ObservableCollection<string> { "a", "b", "c", "d" };
        var (model, _) = CreateModel(items);

        model.Select("d");
        model.Select("b");

        Assert.Equal(new object?[] { "b", "d" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void Batch_Reports_One_Net_Change()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        using (model.BatchUpdate())
        {
            model.Select("a");
            model.Select("b");
            model.Select("c");
        }

        var change = Assert.Single(changes);
        Assert.Equal(new object?[] { "a", "b", "c" }, change.SelectedItems.ToArray());
        Assert.Empty(change.DeselectedItems);
    }

    [Fact]
    public void Item_Touched_Twice_In_A_Batch_Is_Not_Reported()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.Select("a");

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        using (model.BatchUpdate())
        {
            model.Deselect("a");
            model.Select("a");
        }

        Assert.Empty(changes);
        Assert.True(model.IsSelected("a"));
    }

    [Fact]
    public void SingleSelect_Keeps_Only_The_Latest()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SingleSelect = true;

        model.Select("a");
        model.Select("c");

        Assert.Equal(new object?[] { "c" }, model.SelectedItems.ToArray());
        Assert.Equal(2, model.SelectedIndex);
    }

    [Fact]
    public void RetainOnly_Drops_Items_The_Grid_Says_Are_Gone()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SelectRange(0, 2);

        model.RetainOnly(item => !Equals(item, "b"));

        Assert.Equal(new object?[] { "a", "c" }, model.SelectedItems.ToArray());
    }

    private static (DataGridSelectionModel<string> Model, DataGridCollectionViewSelectionView View) CreateModel(
        ObservableCollection<string> items)
        => CreateModel(new DataGridCollectionView(items));

    private static (DataGridSelectionModel<string> Model, DataGridCollectionViewSelectionView View) CreateModel(
        DataGridCollectionView view)
    {
        var model = new DataGridSelectionModel<string>();
        var selectionView = new DataGridCollectionViewSelectionView(
            view,
            EqualityComparer<object>.Default,
            model.InvalidateOrder);
        model.AttachView(selectionView);
        return (model, selectionView);
    }
}
