// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.DataGridSorting;
using Avalonia.Controls.Selection;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Hierarchical;

public class HierarchicalSelectionExpandCollapseTests
{
    private sealed class TreeItem
    {
        public TreeItem(string name)
        {
            Name = name;
            Children = new ObservableCollection<TreeItem>();
        }

        public string Name { get; }

        public ObservableCollection<TreeItem> Children { get; }
    }

    [AvaloniaFact]
    public void SelectionModel_Allows_MouseSelection_After_ExpandAll_CollapseAll()
    {
        var root = BuildTree();
        var model = new HierarchicalModel(new HierarchicalOptions
        {
            ChildrenSelector = item => ((TreeItem)item).Children,
            IsLeafSelector = item => ((TreeItem)item).Children.Count == 0,
            VirtualizeChildren = false
        });
        model.SetRoot(root);

        var selection = new DataGridSelectionModel<TreeItem> { SingleSelect = true };

        var grid = new DataGrid
        {
            HierarchicalRowsEnabled = true,
            HierarchicalModel = model,
            Selection = selection,
            SelectionMode = DataGridSelectionMode.Single,
            AutoGenerateColumns = false
        };

        grid.ColumnsInternal.Add(new DataGridHierarchicalColumn
        {
            Header = "Name",
            Binding = new Binding("Item.Name")
        });

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = grid
        };

        window.SetThemeStyles();
        window.Show();
        PumpLayout(grid);

        model.ExpandAll();
        PumpLayout(grid);
        model.CollapseAll();
        PumpLayout(grid);
        model.ExpandAll();
        PumpLayout(grid);

        var target = root.Children[1];
        var exception = Record.Exception(() => InvokeMouseSelection(grid, model, target));

        Assert.Null(exception);
        // The selection holds the caller's item, not the node wrapping it.
        Assert.Same(target, selection.SelectedItem);

        window.Close();
    }

    private static void InvokeMouseSelection(DataGrid grid, HierarchicalModel model, TreeItem target)
    {
        var rowIndex = model.IndexOf(target);
        if (rowIndex < 0)
        {
            throw new InvalidOperationException("Target item is not visible in the flattened hierarchy.");
        }

        var slot = grid.SlotFromRowIndex(rowIndex);
        var handled = grid.UpdateStateOnMouseLeftButtonDown(
            CreateLeftPointerArgs(grid),
            columnIndex: 0,
            slot: slot,
            allowEdit: false);

        if (!handled)
        {
            throw new InvalidOperationException("Mouse selection was not handled by the grid.");
        }
    }

    private static PointerPressedEventArgs CreateLeftPointerArgs(Control target)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        return new PointerPressedEventArgs(target, pointer, target, new Point(0, 0), 0, properties, KeyModifiers.None);
    }

    [AvaloniaFact]
    public void SelectedSlots_Are_Ascending_When_A_Sorted_View_Reorders_The_Flattened_Rows()
    {
        var root = new TreeItem("root");
        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            root.Children.Add(new TreeItem(name));
        }

        var model = new HierarchicalModel(new HierarchicalOptions
        {
            ChildrenSelector = item => ((TreeItem)item).Children,
            AutoExpandRoot = true,
            MaxAutoExpandDepth = 1,
        });
        model.SetRoot(root);
        model.Expand(model.Root!);

        // A descending view over the flattened list. The grid still picks the hierarchical selection
        // view - it matches on SourceCollection - so selection is ordered by the model while the
        // rows are ordered by the view, and the two disagree.
        var view = new DataGridCollectionView(model.Flattened);
        view.SortDescriptions.Add(DataGridSortDescription.FromComparer(
            Comparer<object>.Create((x, y) => -string.Compare(
                ((x as HierarchicalNode)?.Item as TreeItem)?.Name,
                ((y as HierarchicalNode)?.Item as TreeItem)?.Name,
                StringComparison.Ordinal))));

        var grid = new DataGrid
        {
            HierarchicalRowsEnabled = true,
            HierarchicalModel = model,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false
        };

        grid.ItemsSource = view;
        grid.ColumnsInternal.Add(new DataGridHierarchicalColumn
        {
            Header = "Name",
            Binding = new Binding("Item.Name")
        });

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = grid
        };

        window.Show();
        PumpLayout(grid);

        grid.SelectAll();

        var slots = grid.GetSelectedSlots().ToList();

        // Every caller is promised ascending slots - GetSelectionInclusive stops at the first slot
        // past its range, and RestoreCurrencyWithinBounds takes the first as the topmost selected
        // row. Model order gave 0,4,3,2,1 here.
        Assert.Equal(slots.OrderBy(x => x).ToArray(), slots.ToArray());
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, slots.ToArray());
    }

    [AvaloniaFact]
    public void A_Reset_Keeps_The_Selection_Of_A_Row_A_Collapsed_Parent_Hides()
    {
        var root = BuildTree();
        var model = new HierarchicalModel(new HierarchicalOptions
        {
            ChildrenSelector = item => ((TreeItem)item).Children,
            IsLeafSelector = item => ((TreeItem)item).Children.Count == 0,
            VirtualizeChildren = false
        });
        model.SetRoot(root);
        model.ExpandAll();

        var selection = new DataGridSelectionModel<TreeItem> { SingleSelect = false };

        var grid = new DataGrid
        {
            HierarchicalRowsEnabled = true,
            HierarchicalModel = model,
            Selection = selection,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false
        };

        // Wrapped in a view rather than handed the flattened list directly, which is what puts the
        // reset through DataGridDataConnection instead of the hierarchical shortcut.
        grid.ItemsSource = new DataGridCollectionView(model.Flattened);
        grid.ColumnsInternal.Add(new DataGridHierarchicalColumn
        {
            Header = "Name",
            Binding = new Binding("Item.Name")
        });

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = grid
        };

        window.SetThemeStyles();
        window.Show();
        PumpLayout(grid);

        var parent = root.Children[0];
        var hidden = parent.Children[0];
        selection.Select(hidden);
        PumpLayout(grid);

        // Collapsing takes the row off screen and deliberately leaves the selection alone.
        model.Collapse(model.FindNode(parent)!);
        PumpLayout(grid);

        Assert.True(selection.IsSelected(hidden));
        Assert.Equal(-1, model.IndexOf(hidden));

        // Swapping in an equivalent view over the same data drops only what the data no longer
        // holds, and a collapsed row is still held.
        grid.ItemsSource = new DataGridCollectionView(model.Flattened);
        PumpLayout(grid);

        Assert.True(selection.IsSelected(hidden));

        // A rebuild resets the flattened list, and a reset says nothing about what it removed. The
        // item is still in the tree, so it stays selected - only the rows moved.
        model.SetRoot(root);
        PumpLayout(grid);

        Assert.True(selection.IsSelected(hidden));

        model.ExpandAll();
        PumpLayout(grid);

        Assert.True(selection.IsSelected(hidden));

        window.Close();
    }

    [AvaloniaFact]
    public void SelectAll_Takes_The_Rows_On_Screen_And_Not_The_Ones_A_Collapsed_Parent_Hides()
    {
        var root = BuildTree();
        var model = new HierarchicalModel(new HierarchicalOptions
        {
            ChildrenSelector = item => ((TreeItem)item).Children,
            IsLeafSelector = item => ((TreeItem)item).Children.Count == 0,
            VirtualizeChildren = false
        });
        model.SetRoot(root);

        // Only the top level opened, so every grandchild is still folded away.
        model.Expand(model.Root!);

        var selection = new DataGridSelectionModel<TreeItem>();

        var grid = new DataGrid
        {
            HierarchicalRowsEnabled = true,
            HierarchicalModel = model,
            Selection = selection,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false
        };

        grid.ColumnsInternal.Add(new DataGridHierarchicalColumn
        {
            Header = "Name",
            Binding = new Binding("Item.Name")
        });

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = grid
        };

        window.SetThemeStyles();
        window.Show();
        PumpLayout(grid);

        var onScreen = model.Flattened.Select(node => (TreeItem)node.Item!).ToArray();
        var hidden = root.Children[0].Children[0];
        Assert.Contains(root.Children[0], onScreen);
        Assert.DoesNotContain(hidden, onScreen);

        selection.SelectAll();
        PumpLayout(grid);

        // Select-all means the rows the grid is showing. A collapsed branch is not shown, and
        // selecting rows the user cannot see would hand a delete or an export far more than was
        // asked for.
        Assert.Equal(onScreen, selection.SelectedItems.ToArray());
        Assert.False(selection.IsSelected(hidden));

        // What lands in the selection is the caller's item, not the node the grid wraps it in.
        Assert.True(selection.IsSelected(root.Children[0]));

        // And the grid agrees: every row it is showing reads as selected.
        Assert.Equal(Enumerable.Range(0, onScreen.Length).ToArray(), grid.GetSelectedSlots().ToArray());

        window.Close();
    }

    [AvaloniaFact]
    public void Selecting_A_Row_Past_The_Last_One_On_Screen_Does_Nothing()
    {
        var root = BuildTree();
        var model = new HierarchicalModel(new HierarchicalOptions
        {
            ChildrenSelector = item => ((TreeItem)item).Children,
            IsLeafSelector = item => ((TreeItem)item).Children.Count == 0,
            VirtualizeChildren = false
        });
        model.SetRoot(root);
        model.Expand(model.Root!);

        var selection = new DataGridSelectionModel<TreeItem>();

        var grid = new DataGrid
        {
            HierarchicalRowsEnabled = true,
            HierarchicalModel = model,
            Selection = selection,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false
        };

        grid.ColumnsInternal.Add(new DataGridHierarchicalColumn
        {
            Header = "Name",
            Binding = new Binding("Item.Name")
        });

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = grid
        };

        window.SetThemeStyles();
        window.Show();
        PumpLayout(grid);

        var visibleCount = model.Count;
        selection.Select((TreeItem)model.Flattened[0].Item!);

        var changes = new List<DataGridSelectionModelChangedEventArgs>();
        selection.SelectionChanged += (_, e) => changes.Add(e);

        // Collapsing a branch shortens the list of rows, so a position noted while it was open can
        // name a row that is no longer there. That is the caller working from a stale picture of
        // the grid, and it is told so rather than left to wonder why nothing was selected.
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.SelectAt(visibleCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.DeselectAt(visibleCount + 10));

        // A refused call leaves the selection exactly as it was.
        Assert.Equal(new[] { model.Flattened[0].Item }, selection.SelectedItems.ToArray());
        Assert.Empty(changes);

        window.Close();
    }

    private static void PumpLayout(DataGrid grid)
    {
        Dispatcher.UIThread.RunJobs();
        if (grid.GetVisualRoot() is Window window)
        {
            window.ApplyTemplate();
            window.UpdateLayout();
        }
        grid.ApplyTemplate();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static TreeItem BuildTree()
    {
        var root = new TreeItem("Root");
        for (var i = 0; i < 3; i++)
        {
            var child = new TreeItem($"Child {i + 1}");
            for (var j = 0; j < 2; j++)
            {
                child.Children.Add(new TreeItem($"Child {i + 1}.{j + 1}"));
            }
            root.Children.Add(child);
        }

        return root;
    }
}
