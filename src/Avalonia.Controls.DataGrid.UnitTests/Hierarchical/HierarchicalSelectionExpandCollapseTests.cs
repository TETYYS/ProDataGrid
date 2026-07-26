// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Controls.DataGridSelection;
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
