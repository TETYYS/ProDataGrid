// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

/// <summary>
/// Covers the slot counting behind <see cref="DataGridRowDetailsVisibilityMode.VisibleWhenSelected"/>,
/// which the row height calculation leans on for the details term of the scroll extent.
/// </summary>
public class DataGridSelectedSlotCountTests
{
    [AvaloniaFact]
    public void Counts_Only_Selected_Rows_Inside_The_Range()
    {
        var items = new ObservableCollection<string> { "A", "B", "C", "D", "E" };
        var grid = CreateGrid(items);

        grid.Selection.Select("B");
        grid.Selection.Select("D");

        Assert.Equal(2, grid.GetSelectedSlotCount(0, grid.SlotCount - 1));
        Assert.Equal(1, grid.GetSelectedSlotCount(0, 2));
        Assert.Equal(1, grid.GetSelectedSlotCount(3, grid.SlotCount - 1));
        Assert.Equal(0, grid.GetSelectedSlotCount(4, 4));
    }

    [AvaloniaFact]
    public void Range_Split_Is_Additive()
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, 20).Select(i => $"Item {i}"));
        var grid = CreateGrid(items);

        foreach (var index in new[] { 0, 1, 5, 12, 19 })
        {
            grid.Selection.Select(items[index]);
        }

        var total = grid.GetSelectedSlotCount(0, grid.SlotCount - 1);
        Assert.Equal(5, total);

        // EdgedRowsHeightCalculated counts the viewport and the rows past it separately and adds them
        // rather than re-counting the whole range, so the split has to come out the same at any point.
        for (var split = 0; split < grid.SlotCount; split++)
        {
            Assert.Equal(
                total,
                grid.GetSelectedSlotCount(0, split) + grid.GetSelectedSlotCount(split + 1, grid.SlotCount - 1));
        }
    }

    [AvaloniaFact]
    public void Bounds_Outside_The_Slot_Range_Are_Clamped()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var grid = CreateGrid(items);

        grid.Selection.Select("A");
        grid.Selection.Select("C");

        Assert.Equal(2, grid.GetSelectedSlotCount(-50, grid.SlotCount + 50));
        Assert.Equal(0, grid.GetSelectedSlotCount(grid.SlotCount, grid.SlotCount + 10));
    }

    [AvaloniaFact]
    public void Empty_Selection_Counts_Nothing()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var grid = CreateGrid(items);

        Assert.Equal(0, grid.GetSelectedSlotCount(0, grid.SlotCount - 1));
    }

    [AvaloniaFact]
    public void Deselecting_Drops_The_Row_From_The_Count()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var grid = CreateGrid(items);

        grid.Selection.Select("A");
        grid.Selection.Select("B");
        Assert.Equal(2, grid.GetSelectedSlotCount(0, grid.SlotCount - 1));

        grid.Selection.Deselect("A");
        Assert.Equal(1, grid.GetSelectedSlotCount(0, grid.SlotCount - 1));
    }

    private static DataGrid CreateGrid(IEnumerable items)
    {
        var root = new Window
        {
            Width = 250,
            Height = 150,
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Extended,
            RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.VisibleWhenSelected,
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Value",
            Binding = new Binding(".")
        });

        root.Content = grid;
        root.Show();
        root.UpdateLayout();
        return grid;
    }
}
