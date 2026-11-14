// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Hierarchical;

/// <summary>
/// Covers <see cref="DataGrid.AutoExpandSelectedItem"/>: an application selects an item that lives
/// somewhere inside a collapsed subtree (from a search result, a deep link, a restored session) and
/// expects the grid to open the tree far enough to show it.
/// </summary>
public class DataGridAutoExpandSelectedItemTests
{
    [AvaloniaFact]
    public void Selecting_A_Collapsed_Descendant_Expands_The_Tree_To_Reveal_It()
    {
        var root = BuildTree();
        var (grid, model, window) = CreateGrid(root, autoExpandSelectedItem: true);
        try
        {
            var target = root.Children[1].Children[0];
            Assert.True(model.IndexOf(target) < 0, "The target must start out hidden inside a collapsed branch.");

            grid.SelectedItem = target;
            PumpLayout(grid);

            Assert.Same(target, grid.SelectedItem);
            Assert.True(model.IndexOf(target) >= 0, "The selected item should have been revealed.");
            Assert.Equal(model.IndexOf(target), grid.SelectedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Selecting_A_Collapsed_Descendant_Does_Nothing_When_AutoExpand_Is_Off()
    {
        var root = BuildTree();
        var (grid, model, window) = CreateGrid(root, autoExpandSelectedItem: false);
        try
        {
            var target = root.Children[1].Children[0];

            grid.SelectedItem = target;
            PumpLayout(grid);

            Assert.True(model.IndexOf(target) < 0, "Nothing should have been expanded.");
            Assert.Null(grid.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Turning_AutoExpand_On_Reveals_The_Already_Selected_Item()
    {
        var root = BuildTree();
        var (grid, model, window) = CreateGrid(root, autoExpandSelectedItem: false);
        try
        {
            var branch = root.Children[1];
            var target = branch.Children[0];

            // The user expands the branch, picks a row inside it, then collapses the branch again.
            model.ExpandAll();
            PumpLayout(grid);

            grid.SelectedItem = target;
            PumpLayout(grid);
            Assert.Same(target, grid.SelectedItem);

            model.Collapse(model.FindNode(branch)!);
            PumpLayout(grid);
            Assert.True(model.IndexOf(target) < 0, "Collapsing the branch should hide the selected row.");

            grid.AutoExpandSelectedItem = true;
            PumpLayout(grid);

            Assert.True(model.IndexOf(target) >= 0, "Enabling auto expand should bring the selected row back into view.");
            Assert.Same(target, grid.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Selecting_A_Visible_Item_Leaves_The_Expansion_State_Alone()
    {
        var root = BuildTree();
        var (grid, model, window) = CreateGrid(root, autoExpandSelectedItem: true);
        try
        {
            var visibleBranch = root.Children[0];
            var collapsedBranch = root.Children[1];

            grid.SelectedItem = visibleBranch;
            PumpLayout(grid);

            Assert.Same(visibleBranch, grid.SelectedItem);
            Assert.False(model.FindNode(visibleBranch)!.IsExpanded, "Selecting a visible row must not expand it.");
            Assert.True(model.IndexOf(collapsedBranch.Children[0]) < 0, "Unrelated branches must stay collapsed.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Selecting_An_Item_That_Is_Not_In_The_Tree_Clears_The_Selection()
    {
        var root = BuildTree();
        var (grid, model, window) = CreateGrid(root, autoExpandSelectedItem: true);
        try
        {
            grid.SelectedItem = root.Children[0];
            PumpLayout(grid);
            Assert.NotNull(grid.SelectedItem);

            var stranger = new TreeItem("Not in the tree");
            grid.SelectedItem = stranger;
            PumpLayout(grid);

            Assert.Null(grid.SelectedItem);
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.True(model.IndexOf(stranger) < 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Selecting_A_Deeply_Nested_Descendant_Expands_Every_Ancestor()
    {
        var root = new TreeItem("Root");
        var level1 = new TreeItem("Level 1");
        var level2 = new TreeItem("Level 2");
        var level3 = new TreeItem("Level 3");
        level2.Children.Add(level3);
        level1.Children.Add(level2);
        root.Children.Add(level1);

        var (grid, model, window) = CreateGrid(root, autoExpandSelectedItem: true);
        try
        {
            grid.SelectedItem = level3;
            PumpLayout(grid);

            Assert.Same(level3, grid.SelectedItem);
            Assert.True(model.IndexOf(level1) >= 0);
            Assert.True(model.IndexOf(level2) >= 0);
            Assert.True(model.IndexOf(level3) >= 0);
        }
        finally
        {
            window.Close();
        }
    }

    private static (DataGrid grid, HierarchicalModel model, Window window) CreateGrid(TreeItem root, bool autoExpandSelectedItem)
    {
        var model = new HierarchicalModel(new HierarchicalOptions
        {
            ChildrenSelector = item => ((TreeItem)item).Children,
            IsLeafSelector = item => ((TreeItem)item).Children.Count == 0,
            VirtualizeChildren = false,
            AllowExpandToItemSearch = true
        });

        model.SetRoot(root);

        var grid = new DataGrid
        {
            HierarchicalRowsEnabled = true,
            HierarchicalModel = model,
            AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            AutoExpandSelectedItem = autoExpandSelectedItem
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

        return (grid, model, window);
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
}
