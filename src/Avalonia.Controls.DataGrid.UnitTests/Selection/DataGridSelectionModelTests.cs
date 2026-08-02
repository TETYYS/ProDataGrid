using System;
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
    public void SelectedItems_Keeps_Insertion_Order_Among_Items_No_Longer_In_The_View()
    {
        // Enough of them to get past the size at which Array.Sort stops using insertion sort and
        // stops happening to be stable - below seventeen this passes either way.
        var items = new ObservableCollection<string>(
            Enumerable.Range(0, 20).Select(i => $"item{i:D2}"));
        var (model, _) = CreateModel(items);
        model.SelectRange(0, items.Count - 1);

        var expected = items.ToArray();

        // Taken out of the source without going through the model, so they stay selected while no
        // longer being anywhere in the view. They all share one view index, and only the tiebreak
        // decides what order they come back in.
        for (var i = items.Count - 1; i >= 2; i--)
        {
            items.RemoveAt(i);
        }

        Assert.Equal(expected, model.SelectedItems.Cast<string>().ToArray());
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
    public void SelectRange_In_SingleSelect_Is_Refused()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SingleSelect = true;
        model.Select("b");

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        // Naming three rows when one can be held is a contradiction the caller has to resolve. Any
        // row the model kept would be a guess dressed up as a policy.
        Assert.Throws<InvalidOperationException>(() => model.SelectRange(0, 2));
        Assert.Throws<InvalidOperationException>(() => model.SelectRange(2, 0));

        // Refused before anything was applied, so the selection is exactly what it was.
        Assert.Equal(new object?[] { "b" }, model.SelectedItems.ToArray());
        Assert.Empty(changes);
    }

    [Fact]
    public void SelectRange_Of_One_Row_Is_Allowed_In_SingleSelect()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SingleSelect = true;

        // The refusal is about the rows asked for, not about which method did the asking - and a
        // degenerate range names exactly one row.
        model.SelectRange(1, 1);

        Assert.Equal(new object?[] { "b" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void SetSelectedItems_With_Several_Items_Is_Refused_In_SingleSelect()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SingleSelect = true;
        model.Select("a");

        Assert.Throws<InvalidOperationException>(() => model.SetSelectedItems(new[] { "b", "c" }));

        Assert.Equal(new object?[] { "a" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void SetSelectedItems_Counts_Distinct_Items_In_SingleSelect()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SingleSelect = true;

        // The same row listed twice is still one row, so there is nothing contradictory to refuse.
        model.SetSelectedItems(new[] { "b", "b" });

        Assert.Equal(new object?[] { "b" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void SelectRange_Leads_From_The_End_It_Was_Extended_Towards()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);

        model.SelectRange(0, items.Count - 1);

        Assert.Equal(new object?[] { "a", "b", "c" }, model.SelectedItems.ToArray());
        // The lead is where the next shift-click extends from, so it has to be the end the user
        // dragged to.
        Assert.Equal("c", model.SelectedItem);
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

    [Fact]
    public void SingleSelect_Trim_Keeps_The_Lead_When_The_Lead_Is_Selected()
    {
        var items = new ObservableCollection<string> { "a", "b", "c", "d" };
        var (model, _) = CreateModel(items);

        // Each Select() makes its item the lead, so after these three the lead is "c" - one of the
        // items still selected when SingleSelect turns on.
        model.Select("a");
        model.Select("b");
        model.Select("c");
        Assert.Equal("c", model.SelectedItem);

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        model.SingleSelect = true;

        Assert.Equal(new object?[] { "c" }, model.SelectedItems.ToArray());
        var change = Assert.Single(changes);
        Assert.Empty(change.SelectedItems);
        Assert.Equal(new HashSet<object?> { "a", "b" }, new HashSet<object?>(change.DeselectedItems));
    }

    [Fact]
    public void SingleSelect_Trim_Keeps_First_In_View_Order_When_There_Is_No_Selected_Lead()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);

        // SelectAll never sets a lead, so the trim has nothing to prefer and falls back to view order.
        model.SelectAll();
        Assert.Equal(new object?[] { "a", "b", "c" }, model.SelectedItems.ToArray());

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        model.SingleSelect = true;

        Assert.Equal(new object?[] { "a" }, model.SelectedItems.ToArray());
        var change = Assert.Single(changes);
        Assert.Empty(change.SelectedItems);
        Assert.Equal(new HashSet<object?> { "b", "c" }, new HashSet<object?>(change.DeselectedItems));
    }

    [Fact]
    public void SelectAll_Is_Refused_When_SingleSelect_Is_On()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SingleSelect = true;
        model.Select("a");

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        Assert.Throws<InvalidOperationException>(() => model.SelectAll());

        Assert.Equal(new object?[] { "a" }, model.SelectedItems.ToArray());
        Assert.Empty(changes);
    }

    [Fact]
    public void SelectAll_Over_One_Row_Is_Allowed_When_SingleSelect_Is_On()
    {
        var items = new ObservableCollection<string> { "a" };
        var (model, _) = CreateModel(items);
        model.SingleSelect = true;

        // "Every row" and "the one row" are the same request here, so there is no contradiction.
        model.SelectAll();

        Assert.Equal(new object?[] { "a" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void DeselectRange_Treats_Inverted_Arguments_As_The_Same_Range()
    {
        var items = new ObservableCollection<string> { "a", "b", "c", "d", "e" };
        var (model, _) = CreateModel(items);
        model.SelectRange(0, 4);

        // from > to: the range is still resolved by min/max, not rejected.
        model.DeselectRange(3, 1);

        Assert.Equal(new object?[] { "a", "e" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void IsIndexSelected_Reflects_Selection_And_Rejects_A_Row_That_Is_Not_There()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.Select("b");

        Assert.True(model.IsIndexSelected(1));
        Assert.False(model.IsIndexSelected(0));

        // False would say "row 99 is not selected", which is not the truth about a grid with three
        // rows - the truth is that the question is about a row that does not exist.
        Assert.Throws<ArgumentOutOfRangeException>(() => model.IsIndexSelected(99));
    }

    [Fact]
    public void DeselectAt_Deselects_By_Index_And_Rejects_A_Row_That_Is_Not_There()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SelectRange(0, 2);

        model.DeselectAt(1);
        Assert.Equal(new object?[] { "a", "c" }, model.SelectedItems.ToArray());

        Assert.Throws<ArgumentOutOfRangeException>(() => model.DeselectAt(99));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.SelectAt(99));
        Assert.Equal(new object?[] { "a", "c" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void SelectedItem_Setter_Replaces_The_Selection_And_Null_Clears_It()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SelectRange(0, 2);

        model.SelectedItem = "b";
        Assert.Equal(new object?[] { "b" }, model.SelectedItems.ToArray());

        model.SelectedItem = null;
        Assert.Equal(0, model.Count);
        Assert.Null(model.SelectedItem);
    }

    [Fact]
    public void AnchorItem_And_AnchorIndex_Default_To_None_And_Reflect_A_Set_Value()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);

        Assert.Null(model.AnchorItem);
        Assert.Equal(-1, model.AnchorIndex);

        model.AnchorItem = "b";
        Assert.Equal("b", model.AnchorItem);
        Assert.Equal(1, model.AnchorIndex);
    }

    [Fact]
    public void Placeholder_Row_Is_Tracked_By_Identity_And_Hidden_From_The_Typed_Projection()
    {
        var items = new ObservableCollection<string> { "a", "b" };
        // A row comparer that is deliberately not reference equality, so the two halves below are
        // visibly different rules rather than the same rule twice.
        var model = new DataGridSelectionModel<string>(StringComparer.OrdinalIgnoreCase);
        model.AttachView(new DataGridCollectionViewSelectionView(
            new DataGridCollectionView(items),
            EqualityComparer<object>.Default,
            model.InvalidateOrder));

        var placeholder = DataGridCollectionView.NewItemPlaceholder;

        model.Select("a");
        // Not a string, so this resolves to the inherited, untyped Select(object?) - the same surface
        // the grid selects the new-item row through.
        model.Select(placeholder);

        // Real rows still go through the row comparer.
        Assert.True(model.IsSelected("A"));
        // The sentinel isn't a string, so it falls back to identity - which is what a singleton
        // sentinel wants: it is itself, and it never collides with a real row.
        Assert.True(model.IsSelected(placeholder));
        Assert.Equal(2, model.Count);

        // A ViewModel bound to the typed SelectedItems never has to know the sentinel exists.
        Assert.Equal(new[] { "a" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void Typed_AnchorItem_Reads_As_Default_When_The_Anchor_Is_The_Placeholder_Row()
    {
        var model = new DataGridSelectionModel<string>();
        var untyped = (DataGridSelectionModel)model;

        // Shift-extending from the new-item row anchors on the sentinel through the untyped surface.
        untyped.AnchorItem = DataGridCollectionView.NewItemPlaceholder;

        // The typed read degrades to default rather than throwing, so a typed binding survives it...
        Assert.Null(model.AnchorItem);
        // ...while the grid's own range logic still sees the anchor it set.
        Assert.Same(DataGridCollectionView.NewItemPlaceholder, untyped.AnchorItem);
    }

    [Fact]
    public void SelectedIndex_Set_Selects_The_Row_At_That_Position()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);

        // The ordinary way a ViewModel restores a selection: hand back a position and let the model
        // resolve it against the rows it has.
        model.SelectedIndex = 2;

        Assert.Equal("c", model.SelectedItem);
        Assert.Equal(new object?[] { "c" }, model.SelectedItems.ToArray());

        // Setting it again replaces rather than adds - a position names one row, not one more.
        model.SelectedIndex = 0;

        Assert.Equal(new object?[] { "a" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void SelectedIndex_Set_To_Minus_One_Clears_The_Selection()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SelectRange(0, 1);

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        // -1 is what SelectedIndex reads as when nothing is selected, so writing it back has to
        // mean the same thing - a binding that round-trips the value must not select row -1 or
        // leave the old selection standing.
        model.SelectedIndex = -1;

        Assert.Empty(model.SelectedItems);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.Null(model.SelectedItem);

        // One change, naming both rows that were let go.
        var change = Assert.Single(changes);
        Assert.Equal(new HashSet<object?> { "a", "b" }, new HashSet<object?>(change.DeselectedItems));
    }

    [Fact]
    public void A_Range_Over_Rows_That_Are_Not_There_Is_An_Error()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.Select("a");

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        // Rows 5 to 9 of a three-row grid, and rows 0 to 3 where only 0 to 2 exist. Narrowing
        // either to the rows that do exist would select something other than what was asked for,
        // and doing so silently would hide that the caller's idea of the grid is out of date.
        var beyond = Assert.Throws<ArgumentOutOfRangeException>(() => model.SelectRange(5, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.SelectRange(0, items.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.DeselectRange(3, 999));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.DeselectRange(-5, 1));

        // The message says what was there, since the index alone does not explain the refusal.
        Assert.Contains("3 rows", beyond.Message);

        // A refused call changes nothing.
        Assert.Equal(new object?[] { "a" }, model.SelectedItems.ToArray());
        Assert.Empty(changes);
    }

    [Fact]
    public void A_Range_Given_Backwards_Is_The_Same_Range()
    {
        var items = new ObservableCollection<string> { "a", "b", "c", "d", "e" };
        var (model, _) = CreateModel(items);

        // Dragging upwards names the same rows as dragging down over them, so only the lead differs.
        model.SelectRange(3, 1);

        Assert.Equal(new object?[] { "b", "c", "d" }, model.SelectedItems.ToArray());
        Assert.Equal("b", model.SelectedItem);
    }

    [Fact]
    public void Selecting_By_Item_Needs_A_View_Too()
    {
        var model = new DataGridSelectionModel<string>();

        // Selecting by item does not need a position, but it does need somewhere the selection will
        // be shown - a model no grid has taken has no such place, and putting items into one only
        // looks like it worked.
        Assert.Throws<InvalidOperationException>(() => model.Select("a"));
        Assert.Throws<InvalidOperationException>(() => model.Deselect("a"));
        Assert.Throws<InvalidOperationException>(() => model.SetSelectedItems(new[] { "a" }));
        Assert.Throws<InvalidOperationException>(() => model.SelectedItem = "a");

        Assert.Empty(model.SelectedItems);
    }

    [Fact]
    public void Clicking_The_Selected_Row_Again_In_Single_Mode_Leaves_It_Selected_And_Reports_Nothing()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SingleSelect = true;
        model.Select("b");

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        // Single mode deselects everything but the row being selected - and that row is this one,
        // so it has to be spared. Clearing it and putting it straight back would leave the
        // selection right while telling every listener the row had been deselected and reselected.
        model.Select("b");

        Assert.Equal(new object?[] { "b" }, model.SelectedItems.ToArray());
        Assert.Equal(1, model.SelectedIndex);
        Assert.Empty(changes);
    }

    /// <summary>
    /// A model that overrides the args factory to hand back the base type, as a model adding its own
    /// metadata through the base args would.
    /// </summary>
    private sealed class BaseArgsModel : DataGridSelectionModel<string>
    {
        protected override DataGridSelectionModelChangedEventArgs CreateChangedArgs(
            IReadOnlyList<object?> selected,
            IReadOnlyList<object?> deselected)
            => new DataGridSelectionModelChangedEventArgs(selected, deselected);
    }

    [Fact]
    public void Typed_SelectionChanged_Survives_A_Subclass_Overriding_The_Args_Factory()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var model = new BaseArgsModel();
        model.AttachView(new DataGridCollectionViewSelectionView(
            new DataGridCollectionView(items),
            EqualityComparer<object>.Default,
            model.InvalidateOrder));

        var changes = new List<DataGridSelectionModelChangedEventArgs<string>>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        // Both the factory and the raiser are extension points, so overriding one must not break the
        // other. Assuming the args were the ones the factory here would have made threw
        // InvalidCastException on the next selection change.
        model.Select("b");

        var change = Assert.Single(changes);
        Assert.Equal(new[] { "b" }, change.SelectedItems.ToArray());
        Assert.Empty(change.DeselectedItems);
    }

    /// <summary>
    /// A model deriving straight from the untyped base and customizing nothing, as a consumer who
    /// wants selection state without a typed projection would write.
    /// </summary>
    private sealed class UntypedModel : DataGridSelectionModel
    {
    }

    [Fact]
    public void Every_Operation_That_Needs_Rows_Is_An_Error_Before_There_Are_Any()
    {
        var model = new DataGridSelectionModel<string>();

        // Positions name nothing without a view, and "all" has nothing to enumerate, so none of
        // these can be carried out. Returning quietly would leave the caller with an empty
        // selection and no indication of why, and holding the request until a view arrives would
        // only defer the mistake instead of reporting it.
        Assert.Throws<InvalidOperationException>(() => model.SelectRange(0, 2));
        Assert.Throws<InvalidOperationException>(() => model.DeselectRange(0, 2));
        Assert.Throws<InvalidOperationException>(() => model.SelectAt(0));
        Assert.Throws<InvalidOperationException>(() => model.DeselectAt(0));
        Assert.Throws<InvalidOperationException>(() => model.SelectAll());
        Assert.Throws<InvalidOperationException>(() => model.IsIndexSelected(0));

        var byIndex = Assert.Throws<InvalidOperationException>(() => model.SelectedIndex = 0);

        // The message has to name the operation and say what to do instead, since both remedies are
        // non-obvious from the call site.
        Assert.Contains(nameof(DataGridSelectionModel.SelectedIndex), byIndex.Message);
        Assert.Contains("Selection", byIndex.Message);
        Assert.Contains(nameof(DataGridSelectionModel.Select), byIndex.Message);
    }

    [Fact]
    public void Clearing_The_Selection_Needs_No_Rows()
    {
        var model = new DataGridSelectionModel<string>();

        // -1 is what SelectedIndex reads as when nothing is selected, so a binding must be able to
        // write its own reading back without a view to do it in. Clearing is discarding what the
        // model holds, which is meaningful whether or not anything is on screen.
        model.SelectedIndex = -1;
        model.Clear();

        Assert.Empty(model.SelectedItems);
        Assert.Equal(-1, model.SelectedIndex);
    }

    [Fact]
    public void A_SelectedIndex_Set_Before_The_Rows_Arrive_Is_Not_Applied_Later()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var model = new DataGridSelectionModel<string>();

        Assert.Throws<InvalidOperationException>(() => model.SelectedIndex = 2);

        model.AttachView(new DataGridCollectionViewSelectionView(
            new DataGridCollectionView(items),
            EqualityComparer<object>.Default,
            model.InvalidateOrder));

        // The rejected write left nothing behind to be honoured once rows showed up. Holding it
        // would have meant a call that threw still changed the selection, a moment later and out of
        // order with everything the caller did in between.
        Assert.Empty(model.SelectedItems);
        Assert.Equal(-1, model.SelectedIndex);
    }

    [Fact]
    public void Selecting_A_Range_Is_An_Error_Again_Once_The_Grid_Lets_The_Model_Go()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);
        model.SelectRange(0, 1);

        // What the grid does when it hands the model back: the selection is left alone, because it
        // is a set of items and detaching invalidates nothing about it...
        model.AttachView(null);

        Assert.Equal(new[] { "a", "b" }, model.SelectedItems.ToArray());
        Assert.True(model.IsSelected("a"));

        // ...but there is nowhere left to show a change, so every way of making one is refused.
        Assert.Throws<InvalidOperationException>(() => model.SelectRange(0, 2));
        Assert.Throws<InvalidOperationException>(() => model.DeselectRange(0, 2));
        Assert.Throws<InvalidOperationException>(() => model.Select("c"));
        Assert.Throws<InvalidOperationException>(() => model.Deselect("a"));

        // Reading what it holds is still fine - that is what surviving the detach means.
        Assert.Equal(new[] { "a", "b" }, model.SelectedItems.ToArray());
    }

    [Fact]
    public void A_Model_That_Customizes_Nothing_Still_Reports_Its_Changes()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var model = new UntypedModel();
        model.AttachView(new DataGridCollectionViewSelectionView(
            new DataGridCollectionView(items),
            EqualityComparer<object>.Default,
            model.InvalidateOrder));

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        // The args factory is an extension point, so the base has to supply a working default -
        // deriving from the model must not oblige the deriver to reimplement its event payload.
        model.Select("b");
        model.Deselect("b");

        Assert.Collection(
            changes,
            e =>
            {
                Assert.Equal(new object?[] { "b" }, e.SelectedItems.ToArray());
                Assert.Empty(e.DeselectedItems);
            },
            e =>
            {
                Assert.Empty(e.SelectedItems);
                Assert.Equal(new object?[] { "b" }, e.DeselectedItems.ToArray());
            });
    }

    [Fact]
    public void Disposing_A_Batch_Twice_Does_Not_Swallow_The_Next_Change()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var (model, _) = CreateModel(items);

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        model.SelectionChanged += (_, e) => changes.Add(e);

        // IDisposable requires Dispose to be idempotent, and a caller that both wraps the scope and
        // releases it explicitly is entitled to expect that. The cost of getting it wrong lands on
        // the change after this one, not on this one: a second decrement drives the batch depth
        // below zero, and the next batch closes at -1 instead of 0 and never reports what it did.
        var batch = model.BatchUpdate();
        model.Select("a");
        batch.Dispose();
        batch.Dispose();

        Assert.Single(changes);

        using (model.BatchUpdate())
        {
            model.Select("c");
        }

        Assert.Equal(2, changes.Count);
        Assert.Equal(new object?[] { "c" }, changes[1].SelectedItems.ToArray());
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
