// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Utils;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

/// <summary>
/// Covers picking a run of columns by their headers once the user has rearranged the grid: the range
/// has to follow what is on screen (display order, skipping hidden columns), not the order the
/// columns were declared in.
/// </summary>
public class DataGridColumnHeaderRangeSelectionTests
{
    [AvaloniaFact]
    public void Shift_Clicking_Two_Headers_Selects_Everything_Between_Them()
    {
        var items = CreateItems(2);
        var (grid, root) = CreateGrid(items);
        try
        {
            ClickHeader(grid, grid.ColumnsInternal[0]);
            ClickHeader(grid, grid.ColumnsInternal[2], KeyModifiers.Shift);

            Assert.Equal(3, grid.SelectedColumns.Count);
            Assert.Equal(items.Count * 3, grid.SelectedCells.Count);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Shift_Clicking_Follows_The_Order_Columns_Were_Dragged_Into()
    {
        var items = CreateItems(2);
        var (grid, root) = CreateGrid(items);
        try
        {
            var first = grid.ColumnsInternal[0];
            var second = grid.ColumnsInternal[1];
            var third = grid.ColumnsInternal[2];
            var fourth = grid.ColumnsInternal[3];

            // The user drags the last column to the front: display order is now D, A, B, C.
            fourth.DisplayIndex = 0;
            grid.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // Selecting from the leftmost header to the second one must pick D and A, not A and B.
            ClickHeader(grid, fourth);
            ClickHeader(grid, first, KeyModifiers.Shift);

            Assert.Equal(2, grid.SelectedColumns.Count);
            Assert.Contains(fourth, grid.SelectedColumns);
            Assert.Contains(first, grid.SelectedColumns);
            Assert.DoesNotContain(second, grid.SelectedColumns);
            Assert.DoesNotContain(third, grid.SelectedColumns);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Hidden_Column_Inside_The_Range_Is_Not_Selected()
    {
        var items = CreateItems(2);
        var (grid, root) = CreateGrid(items);
        try
        {
            var first = grid.ColumnsInternal[0];
            var second = grid.ColumnsInternal[1];
            var third = grid.ColumnsInternal[2];

            second.IsVisible = false;
            grid.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            ClickHeader(grid, first);
            ClickHeader(grid, third, KeyModifiers.Shift);

            Assert.Contains(first, grid.SelectedColumns);
            Assert.Contains(third, grid.SelectedColumns);
            Assert.DoesNotContain(second, grid.SelectedColumns);

            // Only the two visible columns contributed cells.
            Assert.Equal(items.Count * 2, grid.SelectedCells.Count);
            Assert.DoesNotContain(grid.SelectedCells, cell => ReferenceEquals(cell.Column, second));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Ctrl_Clicking_Adds_A_Disjoint_Column_To_The_Selection()
    {
        var items = CreateItems(2);
        var (grid, root) = CreateGrid(items);
        try
        {
            var first = grid.ColumnsInternal[0];
            var third = grid.ColumnsInternal[2];
            var ctrl = GetCtrlOrCmdModifier(grid);

            ClickHeader(grid, first);
            ClickHeader(grid, third, ctrl);

            Assert.Equal(2, grid.SelectedColumns.Count);
            Assert.Contains(first, grid.SelectedColumns);
            Assert.Contains(third, grid.SelectedColumns);
            Assert.Equal(items.Count * 2, grid.SelectedCells.Count);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Selecting_A_Column_Then_Another_Replaces_The_Whole_Cell_Selection()
    {
        var items = CreateItems(3);
        var (grid, root) = CreateGrid(items);
        try
        {
            var first = grid.ColumnsInternal[0];
            var third = grid.ColumnsInternal[2];

            ClickHeader(grid, first);
            Assert.Equal(items.Count, grid.SelectedCells.Count);

            ClickHeader(grid, third);

            Assert.Equal(items.Count, grid.SelectedCells.Count);
            Assert.All(grid.SelectedCells, cell => Assert.Same(third, cell.Column));
            Assert.Equal(new[] { third }, grid.SelectedColumns);
            Assert.DoesNotContain(":selected", GetHeader(grid, first).Classes);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Selecting_Every_Cell_Of_A_Column_By_Hand_Marks_The_Column_Selected()
    {
        var items = CreateItems(2);
        var (grid, root) = CreateGrid(items);
        try
        {
            var column = grid.ColumnsInternal[1];

            var cells = new ObservableCollection<DataGridCellInfo>();
            for (var rowIndex = 0; rowIndex < items.Count; rowIndex++)
            {
                cells.Add(new DataGridCellInfo(items[rowIndex], column, rowIndex, column.Index, isValid: true));
            }

            grid.SelectedCells = cells;
            grid.UpdateLayout();

            // Every cell in the column is selected, so the column header reads as selected too.
            Assert.Contains(column, grid.SelectedColumns);
            Assert.Contains(":selected", GetHeader(grid, column).Classes);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Deselecting_One_Cell_Un_Marks_The_Column()
    {
        var items = CreateItems(3);
        var (grid, root) = CreateGrid(items);
        try
        {
            var column = grid.ColumnsInternal[1];

            var cells = new ObservableCollection<DataGridCellInfo>();
            for (var rowIndex = 0; rowIndex < items.Count; rowIndex++)
            {
                cells.Add(new DataGridCellInfo(items[rowIndex], column, rowIndex, column.Index, isValid: true));
            }

            grid.SelectedCells = cells;
            grid.UpdateLayout();
            Assert.Contains(column, grid.SelectedColumns);

            cells.RemoveAt(0);
            grid.UpdateLayout();

            Assert.DoesNotContain(column, grid.SelectedColumns);
            Assert.DoesNotContain(":selected", GetHeader(grid, column).Classes);
        }
        finally
        {
            root.Close();
        }
    }

    private static KeyModifiers GetCtrlOrCmdModifier(Control target)
    {
        return KeyboardHelper.GetPlatformCtrlOrCmdKeyModifier(target);
    }

    private static void ClickHeader(DataGrid grid, DataGridColumn column, KeyModifiers modifiers = KeyModifiers.None)
    {
        var header = GetHeader(grid, column);
        var center = new Point(header.Bounds.Width / 2, header.Bounds.Height / 2);
        var position = header.TranslatePoint(center, grid) ?? center;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);

        header.RaiseEvent(new PointerPressedEventArgs(header, pointer, grid, position, 0, properties, modifiers, clickCount: 1));
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridColumnHeader GetHeader(DataGrid grid, DataGridColumn column)
    {
        return grid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .First(header => ReferenceEquals(header.OwningColumn, column));
    }

    private static ObservableCollection<Row> CreateItems(int count)
    {
        var items = new ObservableCollection<Row>();
        for (var i = 0; i < count; i++)
        {
            items.Add(new Row { A = "A" + i, B = "B" + i, C = "C" + i, D = "D" + i });
        }

        return items;
    }

    private static (DataGrid grid, Window root) CreateGrid(IEnumerable<Row> items)
    {
        var root = new Window
        {
            Width = 800,
            Height = 480
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserSelectColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrColumnHeader,
            HeadersVisibility = DataGridHeadersVisibility.All
        };

        foreach (var name in new[] { nameof(Row.A), nameof(Row.B), nameof(Row.C), nameof(Row.D) })
        {
            grid.ColumnsInternal.Add(new DataGridTextColumn
            {
                Header = name,
                Binding = new Binding(name)
            });
        }

        root.Content = grid;
        root.Show();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();
        return (grid, root);
    }

    private sealed class Row
    {
        public string A { get; set; } = string.Empty;

        public string B { get; set; } = string.Empty;

        public string C { get; set; } = string.Empty;

        public string D { get; set; } = string.Empty;
    }
}
