// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Controls.Selection;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

public class DataGridSelectionChangingTests : IDisposable
{
    private readonly List<Window> _windows = new();

    private sealed class HierarchyItem
    {
        public HierarchyItem(string name) => Name = name;

        public string Name { get; }

        public ObservableCollection<HierarchyItem> Children { get; } = new();
    }

    [AvaloniaFact]
    public void Programmatic_Row_Proposal_Is_Raised_Before_Commit()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGridSelectionModel<string> model = new() { SingleSelect = false };
        DataGrid grid = CreateGrid(items, model);
        model.SelectAt(0);
        grid.UpdateLayout();
        DataGridSelectionChangingEventArgs? observed = null;
        bool selectionChangedDuringPreview = true;

        grid.SelectionChanging += (_, e) =>
        {
            observed = e;
            selectionChangedDuringPreview = Equals(grid.SelectedItem, items[1]);
        };

        grid.SelectedItem = items[1];

        Assert.NotNull(observed);
        Assert.False(selectionChangedDuringPreview);
        Assert.True(observed!.Source.HasFlag(DataGridSelectionChangeSource.Programmatic));
        Assert.Equal(new object[] { "B" }, observed.AddedItems);
        Assert.Equal(new object[] { "A" }, observed.RemovedItems);
        Assert.Equal(1, Assert.Single(observed.AddedRows).RowIndex);
        Assert.Equal(0, Assert.Single(observed.RemovedRows).RowIndex);
        Assert.Equal("B", observed.ProposedCurrentItem);
        Assert.Equal(1, observed.ProposedCurrentCell.RowIndex);
        Assert.Equal(1, observed.ProposedAnchor.RowIndex);
        Assert.Equal("B", grid.SelectedItem);
        Assert.Equal(1, grid.SelectedIndex);
    }

    [AvaloniaFact]
    public void Cancellation_Leaves_Row_Current_Anchor_Currency_And_Scroll_Unchanged()
    {
        ObservableCollection<string> items = new(Enumerable.Range(0, 40).Select(i => $"Item {i}"));
        DataGrid grid = CreateGrid(items);
        grid.SelectedItem = items[2];
        grid.UpdateLayout();
        double offset = grid.GetVerticalOffset();
        DataGridCellInfo currentCell = grid.CurrentCell;
        int anchorSlot = grid.AnchorSlot;
        object? currency = grid.DataConnection.CollectionView?.CurrentItem;
        int changedCount = 0;
        grid.SelectionChanged += (_, _) => changedCount++;
        grid.SelectionChanging += (_, e) => e.Cancel = true;

        grid.SelectedItem = items[30];
        grid.UpdateLayout();

        Assert.Equal(items[2], grid.SelectedItem);
        Assert.Equal(2, grid.SelectedIndex);
        Assert.Equal(currentCell, grid.CurrentCell);
        Assert.Equal(anchorSlot, grid.AnchorSlot);
        Assert.Same(currency, grid.DataConnection.CollectionView?.CurrentItem);
        Assert.Equal(offset, grid.GetVerticalOffset());
        Assert.Equal(0, changedCount);
        Assert.Equal(new object[] { items[2] }, grid.SelectedItems.Cast<object>().ToArray());
    }

    [AvaloniaFact]
    public void SelectAll_Cancellation_Commits_Nothing()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectedItem = items[1];
        DataGridSelectionChangingEventArgs? observed = null;
        grid.SelectionChanging += (_, e) =>
        {
            observed = e;
            e.Cancel = true;
        };

        grid.SelectAll();

        Assert.NotNull(observed);
        Assert.True(observed!.Source.HasFlag(DataGridSelectionChangeSource.Command));
        Assert.Equal(new object[] { "A", "C" }, observed.AddedItems);
        Assert.Empty(observed.RemovedItems);
        Assert.Equal(new object[] { "B" }, grid.SelectedItems.Cast<object>().ToArray());
        Assert.Equal("B", grid.SelectedItem);
    }

    [AvaloniaFact]
    public void SelectAllCells_Cancellation_Preserves_Cell_Column_And_Anchor_State()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectionUnit = DataGridSelectionUnit.CellOrRowOrColumnHeader;
        DataGridCellInfo current = grid.CurrentCell;
        int anchorSlot = grid.AnchorSlot;
        DataGridSelectionChangingEventArgs? observed = null;
        grid.SelectionChanging += (_, e) =>
        {
            observed = e;
            e.Cancel = true;
        };

        grid.SelectAllCells();

        Assert.NotNull(observed);
        Assert.True(observed!.Source.HasFlag(DataGridSelectionChangeSource.Command));
        Assert.Equal(3, observed.AddedCells.Count);
        Assert.Single(observed.AddedColumns);
        Assert.Empty(grid.SelectedCells);
        Assert.Empty(grid.SelectedColumns);
        Assert.Equal(current, grid.CurrentCell);
        Assert.Equal(anchorSlot, grid.AnchorSlot);
    }

    [AvaloniaFact]
    public void Bound_Cell_Proposal_Cancellation_Restores_Bound_Collection()
    {
        ObservableCollection<string> items = new() { "A", "B" };
        DataGrid grid = CreateGrid(items);
        grid.SelectionUnit = DataGridSelectionUnit.Cell;
        DataGridColumn column = grid.ColumnsInternal[0];
        ObservableCollection<DataGridCellInfo> bound = new()
        {
            new DataGridCellInfo(items[1], column, 1, column.Index),
        };
        grid.SelectionChanging += (_, e) => e.Cancel = true;

        grid.SelectedCells = bound;

        Assert.Empty(grid.SelectedCells);
        Assert.Empty(bound);
    }

    [AvaloniaFact]
    public void Bound_Row_Proposal_Cancellation_Restores_Bound_Collection()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectedItem = items[0];
        ObservableCollection<object> bound = new() { items[1], items[2] };
        grid.SelectionChanging += (_, e) => e.Cancel = true;

        grid.SelectedItems = bound;

        Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>().ToArray());
        Assert.Equal(new object[] { "A" }, bound);
    }

    [AvaloniaFact]
    public void SelectedItems_Add_Cancellation_Commits_Nothing()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectedItem = items[0];
        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            e.Cancel = true;
        };

        int result = grid.SelectedItems.Add(items[1]);

        Assert.Equal(1, proposals);
        Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>().ToArray());
        Assert.Equal(-1, result);
    }

    [AvaloniaFact]
    public void Realized_Row_IsSelected_Is_Previewed_Before_The_Property_Mutates()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectedItem = items[0];
        grid.UpdateLayout();
        DataGridRow row = grid.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Single(candidate => Equals(candidate.DataContext, items[1]));
        int propertyChanges = 0;
        int proposals = 0;
        row.PropertyChanged += (_, e) =>
        {
            if (e.Property == DataGridRow.IsSelectedProperty)
            {
                propertyChanges++;
            }
        };
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.False(row.IsSelected);
            Assert.Equal("A", grid.SelectedItem);
            Assert.Equal(new object[] { "B" }, e.AddedItems);
            e.Cancel = true;
        };

        row.IsSelected = true;

        Assert.Equal(1, proposals);
        Assert.Equal(0, propertyChanges);
        Assert.False(row.IsSelected);
        Assert.Equal("A", grid.SelectedItem);
        Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>().ToArray());
    }

    [AvaloniaFact]
    public void Realized_Row_IsSelected_Single_Mode_Proposal_Matches_The_Commit()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.SelectedItem = items[0];
        grid.UpdateLayout();
        DataGridRow row = grid.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Single(candidate => Equals(candidate.DataContext, items[1]));
        DataGridSelectionChangingEventArgs? proposal = null;
        grid.SelectionChanging += (_, e) => proposal = e;

        row.IsSelected = true;

        Assert.NotNull(proposal);
        Assert.Equal(new object[] { "B" }, proposal!.AddedItems);
        Assert.Equal(new object[] { "A" }, proposal.RemovedItems);
        Assert.Equal(new object[] { "B" }, grid.SelectedItems.Cast<object>());
        Assert.Equal("B", grid.SelectedItem);
    }

    [AvaloniaFact]
    public void Realized_Row_IsSelected_Single_Mode_Veto_Reports_The_Complete_Delta()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.SelectedItem = items[0];
        grid.UpdateLayout();
        DataGridRow row = grid.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Single(candidate => Equals(candidate.DataContext, items[1]));
        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.Equal(new object[] { "B" }, e.AddedItems);
            Assert.Equal(new object[] { "A" }, e.RemovedItems);
            Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>());
            e.Cancel = true;
        };

        row.IsSelected = true;

        Assert.Equal(1, proposals);
        Assert.False(row.IsSelected);
        Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>());
        Assert.Equal("A", grid.SelectedItem);
    }

    [AvaloniaFact]
    public void Bound_SelectedItems_Incremental_Changes_Are_One_Atomic_Proposal()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        var bound = new ObservableCollection<object> { items[0] };
        grid.SelectedItems = bound;
        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            // The caller-owned bound collection has necessarily emitted its mutation already,
            // but the grid's committed selection/current state is still the old state here.
            Assert.Equal("A", grid.SelectedItem);
            Assert.Equal(0, grid.SelectedIndex);
            Assert.Equal("A", grid.CurrentCell.Item);
            e.Cancel = true;
        };

        bound.Add(items[1]);
        Assert.Equal(new object[] { "A" }, bound);
        Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>().ToArray());

        bound.Remove(items[0]);
        Assert.Equal(new object[] { "A" }, bound);
        Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>().ToArray());

        bound[0] = items[2];
        Assert.Equal(new object[] { "A" }, bound);
        Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>().ToArray());
        Assert.Equal(3, proposals);
    }

    [AvaloniaFact]
    public void SelectionModel_Cancellation_Restores_Model_And_Grid_Atomically()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGridSelectionModel<string> model = new() { SingleSelect = false };
        DataGrid grid = CreateGrid(items, model);
        model.SelectAt(0);
        grid.UpdateLayout();
        DataGridCellInfo current = grid.CurrentCell;
        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            e.Cancel = true;
        };

        model.SelectAt(1);

        Assert.True(proposals > 0);
        Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>().ToArray());
        Assert.Equal(new[] { 0 }, model.SelectedIndexes);
        Assert.Equal(current, grid.CurrentCell);
    }

    [AvaloniaFact]
    public void Source_Reset_Cancellation_Is_One_PostChange_Proposal_And_Remaps_A_Survivor()
    {
        string removed = "A";
        string survivor = "B";
        string remaining = "C";
        var items = new ResettableObservableCollection<string> { removed, survivor, remaining };
        var model = new DataGridSelectionModel<string> { SingleSelect = false };
        DataGrid grid = CreateGrid(items, model);
        model.SelectAt(1);
        grid.UpdateLayout();
        DataGridCellInfo oldCurrent = grid.CurrentCell;
        int oldAnchor = grid.AnchorSlot;
        double oldOffset = grid.GetVerticalOffset();
        int proposals = 0;
        var proposalStacks = new List<string>();
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            proposalStacks.Add(Environment.StackTrace);
            Assert.True(e.Source.HasFlag(DataGridSelectionChangeSource.ItemsSourceChange));
            Assert.Same(survivor, grid.SelectedItem);
            Assert.Equal(oldCurrent, grid.CurrentCell);
            Assert.Equal(oldAnchor, grid.AnchorSlot);
            Assert.Equal(oldOffset, grid.GetVerticalOffset());
            Assert.Empty(e.AddedItems);
            Assert.Empty(e.RemovedItems);
            Assert.Same(survivor, e.ProposedCurrentItem);
            Assert.Equal(0, e.ProposedCurrentCell.RowIndex);
            e.Cancel = true;
        };

        items.ResetWith(survivor, remaining);
        grid.UpdateLayout();

        Assert.True(proposals == 1, string.Join("\n--- proposal ---\n", proposalStacks));
        Assert.Equal(new object[] { survivor }, grid.SelectedItems.Cast<object>().ToArray());
        Assert.Equal(survivor, grid.SelectedItem);
        Assert.Equal(new[] { 0 }, model.SelectedIndexes);
        Assert.Equal(0, grid.CurrentCell.RowIndex);
        Assert.Equal(grid.SlotFromRowIndex(0), grid.AnchorSlot);
        Assert.Equal(oldOffset, grid.GetVerticalOffset());
    }

    [AvaloniaFact]
    public void Source_Remove_Cancellation_Is_One_PostChange_Proposal_And_Preserves_State()
    {
        string removed = "A";
        string survivor = "B";
        var items = new ObservableCollection<string> { removed, survivor, "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectedItem = survivor;
        grid.UpdateLayout();
        DataGridCellInfo oldCurrent = grid.CurrentCell;
        int oldAnchor = grid.AnchorSlot;
        double oldOffset = grid.GetVerticalOffset();
        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.True(e.Source.HasFlag(DataGridSelectionChangeSource.ItemsSourceChange));
            Assert.Same(survivor, grid.SelectedItem);
            Assert.Equal(oldCurrent, grid.CurrentCell);
            Assert.Equal(oldAnchor, grid.AnchorSlot);
            Assert.Equal(oldOffset, grid.GetVerticalOffset());
            Assert.Same(survivor, e.ProposedCurrentItem);
            Assert.Equal(0, e.ProposedCurrentCell.RowIndex);
            e.Cancel = true;
        };

        items.Remove(removed);
        grid.UpdateLayout();

        Assert.Equal(1, proposals);
        Assert.Same(survivor, grid.SelectedItem);
        Assert.Equal(new object[] { survivor }, grid.SelectedItems.Cast<object>());
        Assert.Equal(0, grid.CurrentCell.RowIndex);
        Assert.Equal(grid.SlotFromRowIndex(0), grid.AnchorSlot);
        Assert.Equal(oldOffset, grid.GetVerticalOffset());
    }

    [AvaloniaFact]
    public void ItemsSource_Replacement_Cancellation_Is_One_PostChange_Proposal_And_Keeps_Survivor()
    {
        string survivor = "B";
        var oldItems = new ObservableCollection<string> { "A", survivor, "C" };
        var replacement = new ObservableCollection<string> { survivor, "D" };
        DataGrid grid = CreateGrid(oldItems);
        grid.SelectedItem = survivor;
        grid.UpdateLayout();
        DataGridCellInfo oldCurrent = grid.CurrentCell;
        int oldAnchor = grid.AnchorSlot;
        double oldOffset = grid.GetVerticalOffset();
        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.True(e.Source.HasFlag(DataGridSelectionChangeSource.ItemsSourceChange));
            Assert.Same(survivor, grid.SelectedItem);
            Assert.Equal(oldCurrent, grid.CurrentCell);
            Assert.Equal(oldAnchor, grid.AnchorSlot);
            Assert.Equal(oldOffset, grid.GetVerticalOffset());
            // Selection is a set of items, so an item the replacement source still holds stays
            // selected; only the current cell moves, onto that item's new row.
            Assert.Empty(e.RemovedItems);
            Assert.Same(survivor, e.ProposedCurrentItem);
            e.Cancel = true;
        };

        grid.ItemsSource = replacement;
        grid.UpdateLayout();

        Assert.Equal(1, proposals);
        Assert.Same(survivor, grid.SelectedItem);
        Assert.Equal(new object[] { survivor }, grid.SelectedItems.Cast<object>());
        Assert.Equal(0, grid.CurrentCell.RowIndex);
        Assert.Equal(grid.SlotFromRowIndex(0), grid.AnchorSlot);
        Assert.Equal(oldOffset, grid.GetVerticalOffset());
    }

    [AvaloniaFact]
    public void Source_Remove_Acceptance_Is_One_PostChange_Proposal_And_One_Identity_Cell_Remap()
    {
        string removed = "A";
        string survivor = "B";
        var items = new ObservableCollection<string> { removed, survivor, "C" };
        var model = new DataGridSelectionModel<string> { SingleSelect = false };
        DataGrid grid = CreateGrid(items, model);
        model.SelectAt(0);
        model.SelectAt(1);
        grid.UpdateLayout();
        DataGridColumn column = grid.ColumnsInternal[0];
        grid.SelectedCells = new ObservableCollection<DataGridCellInfo>
        {
            new(survivor, column, 1, column.Index),
            new(items[2], column, 2, column.Index),
        };
        grid.SelectedCells = null!;
        grid.CurrentCell = new DataGridCellInfo(survivor, column, 1, column.Index);
        model.SelectAt(0);
        grid.SetRowSelection(grid.SlotFromRowIndex(1), isSelected: true, setAnchorSlot: true);
        grid.UpdateLayout();
        Assert.Equal(new object[] { removed, survivor }, grid.SelectedItems.Cast<object>());
        Assert.Same(survivor, grid.CurrentCell.Item);
        Assert.Equal(1, grid.CurrentCell.RowIndex);
        Assert.Equal(grid.SlotFromRowIndex(1), grid.AnchorSlot);
        Assert.Equal(new[] { 1, 2 }, grid.SelectedCells.Select(cell => cell.RowIndex));
        Assert.Empty(grid.SelectedColumns);

        int proposals = 0;
        int selectedCellsChanged = 0;
        int selectedColumnsChanged = 0;
        int selectedCellsCollectionChanged = 0;
        int selectedColumnsCollectionChanged = 0;
        NotifyCollectionChangedAction? selectedCellsCollectionAction = null;
        NotifyCollectionChangedAction? selectedColumnsCollectionAction = null;
        void AssertFinalSelectionSurfaces()
        {
            Assert.Equal(new object[] { survivor }, grid.SelectedItems.Cast<object>());
            Assert.Same(survivor, grid.CurrentCell.Item);
            Assert.Equal(0, grid.CurrentCell.RowIndex);
            Assert.Equal(grid.SlotFromRowIndex(0), grid.AnchorSlot);
            Assert.Equal(new[] { 0, 1 }, grid.SelectedCells.Select(cell => cell.RowIndex));
            Assert.Same(column, Assert.Single(grid.SelectedColumns));
        }
        grid.SelectedCellsChanged += (_, _) =>
        {
            selectedCellsChanged++;
            AssertFinalSelectionSurfaces();
        };
        grid.SelectedColumnsChanged += (_, _) =>
        {
            selectedColumnsChanged++;
            AssertFinalSelectionSurfaces();
        };
        ((INotifyCollectionChanged)grid.SelectedCells).CollectionChanged += (_, e) =>
        {
            selectedCellsCollectionChanged++;
            selectedCellsCollectionAction = e.Action;
            AssertFinalSelectionSurfaces();
        };
        ((INotifyCollectionChanged)grid.SelectedColumns).CollectionChanged += (_, e) =>
        {
            selectedColumnsCollectionChanged++;
            selectedColumnsCollectionAction = e.Action;
            AssertFinalSelectionSurfaces();
        };
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.Equal(DataGridSelectionChangeSource.ItemsSourceChange, e.Source);
            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, e.Guarantee);
            Assert.Equal(new object[] { removed }, e.RemovedItems);
            Assert.Equal(0, Assert.Single(e.RemovedRows).RowIndex);
            Assert.Empty(e.AddedItems);
            Assert.Empty(e.RemovedCells);
            Assert.Same(survivor, e.ProposedCurrentItem);
            Assert.Equal(0, e.ProposedCurrentCell.RowIndex);
            Assert.Same(survivor, e.ProposedAnchor.Item);
            Assert.Equal(0, e.ProposedAnchor.RowIndex);

            // The source has published, but every grid-owned surface is still the coherent old
            // state until the post-change transaction commits after all view listeners.
            Assert.Equal(new object[] { removed, survivor }, grid.SelectedItems.Cast<object>());
            Assert.Same(survivor, grid.CurrentCell.Item);
            Assert.Equal(1, grid.CurrentCell.RowIndex);
            Assert.Equal(new[] { 1, 2 }, grid.SelectedCells.Select(cell => cell.RowIndex));
            Assert.Empty(grid.SelectedColumns);
        };

        items.Remove(removed);
        grid.UpdateLayout();

        Assert.Equal(1, proposals);
        Assert.Equal(new object[] { survivor }, grid.SelectedItems.Cast<object>());
        Assert.Same(survivor, grid.SelectedItem);
        Assert.Same(survivor, grid.CurrentCell.Item);
        Assert.Equal(0, grid.CurrentCell.RowIndex);
        Assert.Equal(grid.SlotFromRowIndex(0), grid.AnchorSlot);
        AssertFinalSelectionSurfaces();
        Assert.All(grid.SelectedCells, cell => Assert.Equal(column.Index, cell.ColumnIndex));
        Assert.Equal(1, selectedCellsChanged);
        Assert.Equal(1, selectedColumnsChanged);
        Assert.Equal(1, selectedCellsCollectionChanged);
        Assert.Equal(1, selectedColumnsCollectionChanged);
        Assert.Equal(NotifyCollectionChangedAction.Reset, selectedCellsCollectionAction);
        Assert.Equal(NotifyCollectionChangedAction.Reset, selectedColumnsCollectionAction);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_Remove_Or_Reset_Preserves_Equal_But_Distinct_Occurrences(bool reset)
    {
        EqualItem prefix = new("prefix", "prefix");
        EqualItem removedEqual = new("duplicate", "removed");
        EqualItem survivingEqual = new("duplicate", "survivor");
        EqualItem suffix = new("suffix", "suffix");
        var items = new ResettableObservableCollection<EqualItem>
        {
            prefix,
            removedEqual,
            survivingEqual,
            suffix,
        };
        var model = new DataGridSelectionModel<EqualItem>(ReferenceEqualityComparer.Instance) { SingleSelect = false };
        DataGrid grid = CreateGrid(items, model);
        model.SelectAt(1);
        model.SelectAt(2);
        grid.UpdateLayout();

        DataGridColumn column = grid.ColumnsInternal[0];
        grid.SelectedCells = new ObservableCollection<DataGridCellInfo>
        {
            new(survivingEqual, column, 2, column.Index),
        };
        grid.SelectedCells = null!;
        grid.CurrentCell = new DataGridCellInfo(
            survivingEqual,
            column,
            2,
            column.Index);
        model.SelectAt(1);
        model.SelectAt(2);
        grid.SetRowSelection(
            grid.SlotFromRowIndex(2),
            isSelected: true,
            setAnchorSlot: true);
        grid.DataConnection.CollectionView!.MoveCurrentToPosition(2);
        grid.UpdateLayout();

        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, e.Guarantee);
            Assert.Same(removedEqual, Assert.Single(e.RemovedItems));
            Assert.Equal(1, Assert.Single(e.RemovedRows).RowIndex);
            Assert.Same(survivingEqual, e.ProposedCurrentItem);
            Assert.Same(survivingEqual, e.ProposedCurrentCell.Item);
            Assert.Equal(1, e.ProposedCurrentCell.RowIndex);
            Assert.Same(survivingEqual, e.ProposedAnchor.Item);
            Assert.Equal(1, e.ProposedAnchor.RowIndex);
        };

        if (reset)
        {
            items.ResetWith(prefix, survivingEqual, suffix);
        }
        else
        {
            items.RemoveAt(1);
        }
        grid.UpdateLayout();

        Assert.Equal(1, proposals);
        Assert.Same(survivingEqual, Assert.Single(grid.SelectedItems.Cast<object>()));
        Assert.Equal(new[] { 1 }, model.SelectedIndexes);
        Assert.Same(survivingEqual, model.SelectedItem);
        Assert.Same(survivingEqual, grid.SelectedItem);
        Assert.Equal(1, grid.SelectedIndex);
        Assert.Same(survivingEqual, grid.CurrentCell.Item);
        Assert.Equal(1, grid.CurrentCell.RowIndex);
        Assert.Equal(grid.SlotFromRowIndex(1), grid.AnchorSlot);
        DataGridCellInfo selectedCell = Assert.Single(grid.SelectedCells);
        Assert.Same(survivingEqual, selectedCell.Item);
        Assert.Equal(1, selectedCell.RowIndex);
        Assert.Same(survivingEqual, grid.DataConnection.CollectionView.CurrentItem);
        Assert.Equal(1, grid.DataConnection.CollectionView.CurrentPosition);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_Or_Unselecting_Second_Equal_Occurrence_Keeps_First_Identity(
        bool removeFromSource)
    {
        EqualItem prefix = new("prefix", "prefix");
        EqualItem equalA = new("duplicate", "first");
        EqualItem equalB = new("duplicate", "second");
        EqualItem suffix = new("suffix", "suffix");
        var items = new ObservableCollection<EqualItem> { prefix, equalA, equalB, suffix };
        var model = new DataGridSelectionModel<EqualItem>(ReferenceEqualityComparer.Instance) { SingleSelect = false };
        DataGrid grid = CreateGrid(items, model);
        model.SelectAt(1);
        model.SelectAt(2);
        grid.UpdateLayout();
        Assert.Equal(new object[] { equalA, equalB }, grid.SelectedItems.Cast<object>());

        var removedItems = new List<object>();
        grid.SelectionChanged += (_, e) => removedItems.AddRange(e.RemovedItems.Cast<object>());
        if (removeFromSource)
        {
            // Source reconciliation is enabled only when an application observes/vetoes the
            // proposal. The no-op handler exercises that transactional path.
            grid.SelectionChanging += (_, _) => { };
            items.RemoveAt(2);
        }
        else
        {
            model.DeselectAt(2);
        }
        grid.UpdateLayout();

        Assert.Equal(new[] { 1 }, model.SelectedIndexes);
        Assert.Single(model.SelectedItems);
        Assert.Same(equalA, Assert.Single(grid.SelectedItems.Cast<object>()));
        Assert.Same(equalA, grid.SelectedItem);
        Assert.Equal(new[] { 1 }, model.SelectedIndexes);
        Assert.Same(equalA, model.SelectedItem);
        Assert.Same(equalB, Assert.Single(removedItems));
    }

    [AvaloniaFact]
    public void Filter_Reconciliation_Uses_Final_View_Indexes_And_Exact_Occurrence()
    {
        EqualItem prefix = new("prefix", "prefix");
        EqualItem equalA = new("duplicate", "first");
        EqualItem equalB = new("duplicate", "second");
        EqualItem suffix = new("suffix", "suffix");
        var items = new ObservableCollection<EqualItem> { prefix, equalA, equalB, suffix };
        var view = new DataGridCollectionView(items);
        var model = new DataGridSelectionModel<EqualItem>(ReferenceEqualityComparer.Instance) { SingleSelect = false };
        DataGrid grid = CreateGrid(view, model);
        SetSourceMutationSelectionState(grid, model, equalB, rowIndex: 2);

        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.Equal(DataGridSelectionChangeSource.ItemsSourceChange, e.Source);
            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, e.Guarantee);
            Assert.Empty(e.RemovedItems);
            Assert.Same(equalB, e.ProposedCurrentItem);
            Assert.Same(equalB, e.ProposedCurrentCell.Item);
            Assert.Equal(1, e.ProposedCurrentCell.RowIndex);
            Assert.Same(equalB, e.ProposedAnchor.Item);
            Assert.Equal(1, e.ProposedAnchor.RowIndex);
        };

        view.Filter = item => !ReferenceEquals(item, prefix);
        grid.UpdateLayout();

        Assert.Equal(1, proposals);
        AssertSourceMutationSelectionState(grid, model, equalB, rowIndex: 1, selectionIndex: 1);
    }

    [AvaloniaFact]
    public void Sort_Reconciliation_Uses_Final_View_Indexes_And_Exact_Occurrence()
    {
        EqualItem equalA = new("duplicate", "Y");
        EqualItem equalB = new("duplicate", "Z");
        EqualItem prefix = new("prefix", "X");
        EqualItem suffix = new("suffix", "W");
        var items = new ObservableCollection<EqualItem> { equalA, equalB, prefix, suffix };
        var view = new DataGridCollectionView(items);
        var model = new DataGridSelectionModel<EqualItem>(ReferenceEqualityComparer.Instance) { SingleSelect = false };
        DataGrid grid = CreateGrid(view, model);
        grid.OwnsSortDescriptions = false;
        SetSourceMutationSelectionState(grid, model, equalB, rowIndex: 1);

        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.Equal(DataGridSelectionChangeSource.ItemsSourceChange, e.Source);
            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, e.Guarantee);
            Assert.Empty(e.RemovedItems);
            Assert.Same(equalB, e.ProposedCurrentItem);
            Assert.Same(equalB, e.ProposedCurrentCell.Item);
            Assert.Equal(3, e.ProposedCurrentCell.RowIndex);
            Assert.Same(equalB, e.ProposedAnchor.Item);
            Assert.Equal(3, e.ProposedAnchor.RowIndex);
        };

        view.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(EqualItem.Label), ListSortDirection.Ascending));
        grid.UpdateLayout();

        Assert.Equal(1, proposals);
        AssertSourceMutationSelectionState(grid, model, equalB, rowIndex: 3, selectionIndex: 3);
    }

    [AvaloniaFact]
    public void Group_Reconciliation_Uses_Final_Leaf_Indexes_And_Exact_Occurrence()
    {
        EqualItem equalB = new("duplicate", "selected");
        EqualItem prefix = new("prefix", "prefix");
        EqualItem suffix = new("suffix", "suffix");
        EqualItem equalA = new("duplicate", "other");
        var items = new ObservableCollection<EqualItem> { equalB, prefix, suffix, equalA };
        var view = new DataGridCollectionView(items);
        var model = new DataGridSelectionModel<EqualItem>(ReferenceEqualityComparer.Instance) { SingleSelect = false };
        DataGrid grid = CreateGrid(view, model);
        SetSourceMutationSelectionState(grid, model, equalB, rowIndex: 0);

        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.Equal(DataGridSelectionChangeSource.ItemsSourceChange, e.Source);
            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, e.Guarantee);
            Assert.Empty(e.RemovedItems);
            Assert.Same(equalB, e.ProposedCurrentItem);
            Assert.Same(equalB, e.ProposedCurrentCell.Item);
            Assert.Equal(1, e.ProposedCurrentCell.RowIndex);
            Assert.Same(equalB, e.ProposedAnchor.Item);
            Assert.Equal(1, e.ProposedAnchor.RowIndex);
        };

        var group = new DataGridPathGroupDescription(nameof(EqualItem.Key));
        group.GroupKeys.Add(suffix.Key);
        group.GroupKeys.Add(equalB.Key);
        group.GroupKeys.Add(prefix.Key);
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Add(group);
        }
        grid.UpdateLayout();

        Assert.Equal(1, proposals);
        AssertSourceMutationSelectionState(grid, model, equalB, rowIndex: 1, selectionIndex: 1);
    }

    [AvaloniaFact]
    public void Paged_Source_Reconciliation_Maps_Final_Row_To_Global_Selection_Index()
    {
        EqualItem removed = new("removed", "A");
        EqualItem before = new("before", "B");
        EqualItem beforePage = new("before-page", "C");
        EqualItem selected = new("selected", "D");
        EqualItem tail = new("tail", "E");
        var items = new ObservableCollection<EqualItem> { removed, before, beforePage, selected, tail };
        var view = new DataGridCollectionView(items) { PageSize = 2 };
        Assert.True(view.MoveToPage(1));
        var model = new DataGridSelectionModel<EqualItem>(ReferenceEqualityComparer.Instance) { SingleSelect = false };
        DataGrid grid = CreateGrid(view, model);
        SetSourceMutationSelectionState(grid, model, selected, rowIndex: 1, selectionIndex: 3);

        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            Assert.Equal(DataGridSelectionChangeSource.ItemsSourceChange, e.Source);
            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, e.Guarantee);
            Assert.Empty(e.RemovedItems);
            Assert.Same(selected, e.ProposedCurrentItem);
            Assert.Same(selected, e.ProposedCurrentCell.Item);
            Assert.Equal(0, e.ProposedCurrentCell.RowIndex);
            Assert.Same(selected, e.ProposedAnchor.Item);
            Assert.Equal(0, e.ProposedAnchor.RowIndex);
        };

        items.RemoveAt(0);
        grid.UpdateLayout();

        Assert.Equal(1, proposals);
        AssertSourceMutationSelectionState(grid, model, selected, rowIndex: 0, selectionIndex: 2);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_Reset_Observer_Order_Is_Atomic_For_Surviving_And_Removed_Current(
        bool removeCurrent)
    {
        EqualItem first = new("first", "A");
        EqualItem survivor = new("survivor", "B");
        EqualItem current = new("current", "C");
        EqualItem tail = new("tail", "D");
        var events = new List<string>();
        var items = new ResettableObservableCollection<EqualItem>
        {
            first,
            survivor,
            current,
            tail,
        };
        items.CollectionChanged += (_, _) => events.Add("source.collection-changed");
        var view = new DataGridCollectionView(items);
        var model = new DataGridSelectionModel<EqualItem>(ReferenceEqualityComparer.Instance) { SingleSelect = false };
        DataGrid grid = CreateGrid(view, model);
        grid.OwnsSortDescriptions = false;
        view.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(EqualItem.Label), ListSortDirection.Ascending));
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();
        SetSourceMutationSelectionState(grid, model, current, rowIndex: 2);
        if (removeCurrent)
        {
            model.SelectAt(1);
            model.SelectAt(2);
            grid.UpdateLayout();
            DataGridColumn column = grid.ColumnsInternal[0];
            grid.CurrentCell = new DataGridCellInfo(current, column, 2, column.Index);
            model.SelectAt(1);
            model.SelectAt(2);
            grid.SetRowSelection(grid.SlotFromRowIndex(2), isSelected: true, setAnchorSlot: true);
            view.MoveCurrentToPosition(2);
            grid.UpdateLayout();
        }
        Assert.Equal(removeCurrent ? 2 : 1, grid.SelectedItems.Count);

        EqualItem expectedCurrent = removeCurrent ? survivor : current;
        int expectedCurrentRow = 1;
        EqualItem expectedCurrency = removeCurrent ? survivor : current;
        int expectedCurrencyRow = 1;
        int proposalCount = 0;
        int gridNotificationCount = 0;
        bool observing = true;

        void AssertFinalGridState()
        {
            object[] expectedRows = removeCurrent
                ? new object[] { survivor }
                : new object[] { current };
            Assert.Equal(expectedRows, grid.SelectedItems.Cast<object>());
            Assert.Equal(expectedRows.Length, grid.SelectedItems.Count);
            Assert.Same(expectedCurrent, grid.SelectedItem);
            Assert.Same(expectedCurrent, grid.CurrentCell.Item);
            Assert.Equal(expectedCurrentRow, grid.CurrentCell.RowIndex);
            Assert.Equal(grid.SlotFromRowIndex(expectedCurrentRow), grid.AnchorSlot);
            if (removeCurrent)
            {
                Assert.Empty(grid.SelectedCells);
            }
            else
            {
                DataGridCellInfo cell = Assert.Single(grid.SelectedCells);
                Assert.Same(current, cell.Item);
                Assert.Equal(1, cell.RowIndex);
            }
            Assert.Same(expectedCurrency, view.CurrentItem);
            Assert.Equal(expectedCurrencyRow, view.CurrentPosition);
        }

        view.CurrentChanging += (_, _) => events.Add("view.current-changing");
        view.CurrentChanged += (_, _) => events.Add("view.current-changed");
        grid.SelectionChanging += (_, e) =>
        {
            if (!observing)
            {
                return;
            }
            events.Add("grid.selection-changing");
            proposalCount++;
            Assert.Equal(DataGridSelectionChangeSource.ItemsSourceChange, e.Source);
            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, e.Guarantee);
            Assert.Same(expectedCurrent, e.ProposedCurrentItem);
            Assert.Equal(expectedCurrentRow, e.ProposedCurrentCell.RowIndex);

            // The proposal is post-source but pre-commit: all grid-owned collection surfaces,
            // including Count, still expose the coherent old snapshot.
            Assert.Equal(removeCurrent ? 2 : 1, grid.SelectedItems.Count);
            Assert.Same(current, grid.CurrentCell.Item);
            Assert.Equal(2, grid.CurrentCell.RowIndex);
            DataGridCellInfo oldCell = Assert.Single(grid.SelectedCells);
            Assert.Same(current, oldCell.Item);
            Assert.Equal(2, oldCell.RowIndex);
        };
        grid.SelectionChanged += (_, _) =>
        {
            if (!observing)
            {
                return;
            }
            events.Add("grid.selection-changed");
            gridNotificationCount++;
            AssertFinalGridState();
        };
        grid.PropertyChanged += (_, e) =>
        {
            if (!observing)
            {
                return;
            }
            if (e.Property == DataGrid.SelectedItemProperty ||
                e.Property == DataGrid.SelectedIndexProperty ||
                e.Property == DataGrid.CurrentCellProperty)
            {
                events.Add($"grid.property:{e.Property.Name}");
                gridNotificationCount++;
                AssertFinalGridState();
            }
        };
        ((INotifyCollectionChanged)grid.SelectedCells).CollectionChanged += (_, _) =>
        {
            if (!observing)
            {
                return;
            }
            events.Add("grid.selected-cells");
            gridNotificationCount++;
            AssertFinalGridState();
        };

        if (removeCurrent)
        {
            items.ResetWith(first, survivor, tail);
        }
        else
        {
            items.ResetWith(survivor, current, tail);
        }
        grid.UpdateLayout();
        observing = false;

        Assert.Equal(1, proposalCount);
        Assert.True(gridNotificationCount > 0);
        AssertFinalGridState();
        AssertEventBefore(events, "source.collection-changed", "view.current-changing");
        AssertEventBefore(events, "view.current-changing", "grid.selection-changing");
        AssertEventBefore(events, "grid.selection-changing", "view.current-changed");
        int currentChangedIndex = events.IndexOf("view.current-changed");
        int firstFinalGridNotification = events.FindIndex(
            item => item == "grid.selection-changed" ||
                item == "grid.selected-cells" ||
                item.StartsWith("grid.property:", StringComparison.Ordinal));
        Assert.True(
            currentChangedIndex >= 0 && firstFinalGridNotification > currentChangedIndex,
            string.Join(" -> ", events));
    }

    [AvaloniaFact]
    public void Reentrant_Selection_Is_Rejected_Without_Corrupting_Outer_Proposal()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectedItem = items[0];
        Exception? reentrancyError = null;
        grid.SelectionChanging += (_, _) =>
        {
            reentrancyError = Record.Exception(() => grid.SelectedItem = items[2]);
        };

        grid.SelectedItem = items[1];

        Assert.IsType<InvalidOperationException>(reentrancyError);
        Assert.Equal("B", grid.SelectedItem);
        Assert.Equal(new object[] { "B" }, grid.SelectedItems.Cast<object>().ToArray());
    }

    [AvaloniaFact]
    public void CurrentCell_Reset_Cancellation_Preserves_Current_Cell()
    {
        ObservableCollection<string> items = new() { "A", "B" };
        DataGrid grid = CreateGrid(items);
        DataGridCellInfo current = grid.CurrentCell;
        Assert.True(current.IsValid);
        grid.SelectionChanging += (_, e) => e.Cancel = true;

        grid.CurrentCell = DataGridCellInfo.Unset;

        Assert.Equal(current, grid.CurrentCell);
    }

    [AvaloniaFact]
    public void Selection_State_Restore_Is_One_Atomic_Proposal()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        DataGrid grid = CreateGrid(items);
        grid.SelectedItem = items[0];
        DataGridCellInfo current = grid.CurrentCell;
        DataGridSelectionMode mode = grid.SelectionMode;
        DataGridSelectionUnit unit = grid.SelectionUnit;
        int proposals = 0;
        grid.SelectionChanging += (_, e) =>
        {
            proposals++;
            e.Cancel = true;
        };
        DataGridColumn column = grid.ColumnsInternal[0];
        DataGridSelectionState state = new()
        {
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.Cell,
            SelectedItemKeys = new object[] { items[2] },
            SelectedIndexes = new[] { 2 },
            SelectedCells = new[]
            {
                new DataGridCellState
                {
                    ItemKey = items[2],
                    ColumnKey = column,
                    RowIndex = 2,
                    ColumnIndex = column.Index,
                },
            },
            CurrentCell = new DataGridCellState
            {
                ItemKey = items[2],
                ColumnKey = column,
                RowIndex = 2,
                ColumnIndex = column.Index,
            },
        };

        grid.RestoreSelectionState(state);

        Assert.Equal(1, proposals);
        Assert.Equal(new object[] { "A" }, grid.SelectedItems.Cast<object>().ToArray());
        Assert.Equal(current, grid.CurrentCell);
        Assert.Equal(mode, grid.SelectionMode);
        Assert.Equal(unit, grid.SelectionUnit);
    }

    [AvaloniaFact]
    public void Collapsed_Hierarchy_Selection_Is_Vetoed_Before_Auto_Expansion()
    {
        HierarchyItem rootItem = new("root");
        HierarchyItem childItem = new("child");
        rootItem.Children.Add(childItem);
        HierarchicalModel<HierarchyItem> model = new(new HierarchicalOptions<HierarchyItem>
        {
            ChildrenSelector = item => item.Children,
            VirtualizeChildren = false,
        });
        model.SetRoot(rootItem);
        HierarchicalNode<HierarchyItem> rootNode = model.Root ?? throw new InvalidOperationException();
        model.Expand(rootNode);
        HierarchicalNode<HierarchyItem> childNode = model.FindNode(childItem) ?? throw new InvalidOperationException();

        Window window = new() { Width = 400, Height = 240 };
        window.SetThemeStyles();
        DataGrid grid = new()
        {
            AutoExpandSelectedItem = true,
            AutoGenerateColumns = false,
            HierarchicalModel = model,
            HierarchicalRowsEnabled = true,
            ItemsSource = model.Flattened,
        };
        grid.ColumnsInternal.Add(new DataGridHierarchicalColumn
        {
            Header = "Name",
            Binding = new Binding("Item.Name"),
        });
        window.Content = grid;
        window.Show();
        grid.UpdateLayout();
        model.Collapse(rootNode);
        grid.UpdateLayout();
        Assert.Equal(1, model.Count);
        object? originalSelection = grid.SelectedItem;
        DataGridSelectionChangingEventArgs? observed = null;
        grid.SelectionChanging += (_, e) =>
        {
            observed = e;
            e.Cancel = true;
        };

        grid.SelectedItem = childItem;

        Assert.NotNull(observed);
        Assert.Same(childItem, Assert.Single(observed!.AddedRows).Item);
        Assert.Same(childItem, observed!.ProposedCurrentItem);
        Assert.Same(childNode.Inner, observed.HierarchyNode);
        Assert.Same(childNode.Inner, Assert.Single(observed.HierarchyPath.Skip(1)));
        Assert.Equal(-1, Assert.Single(observed.AddedRows).RowIndex);
        Assert.False(rootNode.IsExpanded);
        Assert.Same(originalSelection, grid.SelectedItem);
    }

    public void Dispose()
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            _windows[i].Close();
        }
        _windows.Clear();
    }

    private DataGrid CreateGrid<T>(IEnumerable<T> items, DataGridSelectionModel<T>? selection = null)
    {
        DataGridSelectionModel resolvedSelection =
            selection ?? new DataGridSelectionModel<T> { SingleSelect = false };
        return CreateGrid((IEnumerable)items, resolvedSelection);
    }

    private DataGrid CreateGrid(IEnumerable items, DataGridSelectionModel selection)
    {
        Window root = new()
        {
            Width = 400,
            Height = 240,
        };
        root.SetThemeStyles();

        DataGrid grid = new()
        {
            ItemsSource = items,
            Selection = selection,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false,
        };
        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Value",
            Binding = new Binding("."),
        });
        root.Content = grid;
        root.Show();
        grid.UpdateLayout();
        _windows.Add(root);
        return grid;
    }

    private static void SetSourceMutationSelectionState(
        DataGrid grid,
        DataGridSelectionModel<EqualItem> model,
        EqualItem item,
        int rowIndex,
        int? selectionIndex = null)
    {
        int modelIndex = selectionIndex ?? rowIndex;
        model.SelectAt(modelIndex);
        grid.UpdateLayout();
        DataGridColumn column = grid.ColumnsInternal[0];
        grid.SelectedCells = new ObservableCollection<DataGridCellInfo>
        {
            new(item, column, rowIndex, column.Index),
        };
        grid.SelectedCells = null!;
        grid.CurrentCell = new DataGridCellInfo(item, column, rowIndex, column.Index);
        model.SelectAt(modelIndex);
        grid.SetRowSelection(grid.SlotFromRowIndex(rowIndex), isSelected: true, setAnchorSlot: true);
        grid.DataConnection.CollectionView!.MoveCurrentToPosition(rowIndex);
        grid.UpdateLayout();
    }

    private static void AssertSourceMutationSelectionState(
        DataGrid grid,
        DataGridSelectionModel<EqualItem> model,
        EqualItem item,
        int rowIndex,
        int selectionIndex)
    {
        Assert.Same(item, Assert.Single(grid.SelectedItems.Cast<object>()));
        Assert.Equal(new[] { selectionIndex }, model.SelectedIndexes);
        Assert.Same(item, model.SelectedItem);
        Assert.Same(item, grid.SelectedItem);
        Assert.Equal(selectionIndex, grid.SelectedIndex);
        Assert.Same(item, grid.CurrentCell.Item);
        Assert.Equal(rowIndex, grid.CurrentCell.RowIndex);
        Assert.Equal(grid.SlotFromRowIndex(rowIndex), grid.AnchorSlot);
        DataGridCellInfo selectedCell = Assert.Single(grid.SelectedCells);
        Assert.Same(item, selectedCell.Item);
        Assert.Equal(rowIndex, selectedCell.RowIndex);
        Assert.Same(item, grid.DataConnection.CollectionView.CurrentItem);
        Assert.Equal(rowIndex, grid.DataConnection.CollectionView.CurrentPosition);
    }

    private static void AssertEventBefore(
        IReadOnlyList<string> events,
        string first,
        string second)
    {
        int firstIndex = -1;
        int secondIndex = -1;
        for (int i = 0; i < events.Count; i++)
        {
            if (firstIndex < 0 && StringComparer.Ordinal.Equals(events[i], first))
            {
                firstIndex = i;
            }
            if (secondIndex < 0 && StringComparer.Ordinal.Equals(events[i], second))
            {
                secondIndex = i;
            }
        }

        Assert.True(
            firstIndex >= 0 && secondIndex > firstIndex,
            string.Join(" -> ", events));
    }

    private sealed class ResettableObservableCollection<T> : ObservableCollection<T>
    {
        public void ResetWith(params T[] items)
        {
            CheckReentrancy();
            Items.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                Items.Add(items[i]);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    private sealed class EqualItem
    {
        public EqualItem(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }

        public string Label { get; }

        public override bool Equals(object? obj) =>
            obj is EqualItem other && StringComparer.Ordinal.Equals(Key, other.Key);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

        public override string ToString() => Label;
    }

}
