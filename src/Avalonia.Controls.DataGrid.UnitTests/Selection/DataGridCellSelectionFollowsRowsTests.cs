// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

/// <summary>
/// A user selects a block of cells and then the rows underneath move - the grid is re-sorted, a row
/// above is deleted, rows are inserted. The selection belongs to the rows the user picked, not to the
/// positions they happened to be at, so it travels with them; a selected row that is deleted takes its
/// cells with it. These also cover what the column headers do as a column fills up with selected cells
/// and empties out again.
/// </summary>
public class DataGridCellSelectionFollowsRowsTests
{
    // ---- the selection follows its rows ------------------------------------------------------

    [AvaloniaFact]
    public void Re_Sorting_Moves_The_Cell_Selection_With_Its_Rows()
    {
        var items = Items("Ann", "Bea", "Carl");
        var view = new DataGridCollectionView(items);
        var grid = CreateGrid(view);

        SelectCells(grid, (RowIndex:0, ColumnIndex:0), (RowIndex:2, ColumnIndex:1));

        view.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(Row.Name), ListSortDirection.Descending));
        grid.UpdateLayout();

        // Ann fell to the bottom and Carl rose to the top; the cells the user picked are still the
        // ones on Ann and Carl, now at the row indexes those items occupy.
        Assert.Equal(new[] { "Carl", "Bea", "Ann" }, view.Cast<Row>().Select(r => r.Name).ToArray());
        Assert.Equal(
            new[] { ("Ann", 0), ("Carl", 1) },
            SelectedCellNames(grid).OrderBy(c => c.Item1).ToArray());
        Assert.True(grid.IsCellSelected(0, 1));
        Assert.True(grid.IsCellSelected(2, 0));
    }

    [AvaloniaFact]
    public void Deleting_A_Row_Above_The_Selection_Keeps_The_Same_Cells_Selected()
    {
        var items = Items("Ann", "Bea", "Carl");
        var grid = CreateGrid(new DataGridCollectionView(items));

        SelectCells(grid, (RowIndex:2, ColumnIndex:1));

        items.RemoveAt(0);
        grid.UpdateLayout();

        // Carl is row 1 now. The user's cell is still on Carl.
        Assert.Equal(new[] { ("Carl", 1) }, SelectedCellNames(grid));
        Assert.True(grid.IsCellSelected(1, 1));
        Assert.False(grid.IsCellSelected(2, 1));
    }

    [AvaloniaFact]
    public void Deleting_The_Row_A_Selected_Cell_Is_On_Drops_That_Cell()
    {
        var items = Items("Ann", "Bea", "Carl");
        var grid = CreateGrid(new DataGridCollectionView(items));

        SelectCells(grid, (RowIndex:0, ColumnIndex:0), (RowIndex:1, ColumnIndex:0));

        items.RemoveAt(1);
        grid.UpdateLayout();

        Assert.Equal(new[] { ("Ann", 0) }, SelectedCellNames(grid));
    }

    [AvaloniaFact]
    public void Inserting_A_Row_Above_The_Selection_Shifts_It_Down()
    {
        var items = Items("Ann", "Bea");
        var grid = CreateGrid(new DataGridCollectionView(items));

        SelectCells(grid, (RowIndex:1, ColumnIndex:0));

        items.Insert(0, new Row { Name = "Aaron" });
        grid.UpdateLayout();

        Assert.Equal(new[] { ("Bea", 0) }, SelectedCellNames(grid));
        Assert.True(grid.IsCellSelected(2, 0));
    }

    [AvaloniaFact]
    public void Replacing_The_Whole_List_Drops_A_Selection_Of_Rows_That_Are_Gone()
    {
        var items = Items("Ann", "Bea");
        var grid = CreateGrid(new DataGridCollectionView(items));

        SelectCells(grid, (RowIndex:0, ColumnIndex:0), (RowIndex:1, ColumnIndex:1));

        items.Clear();
        items.Add(new Row { Name = "Dana" });
        grid.UpdateLayout();

        Assert.Empty(grid.SelectedCells);
        Assert.Empty(grid.SelectedColumns);
    }

    // ---- what the column headers do ----------------------------------------------------------

    [AvaloniaFact]
    public void A_Column_Counts_As_Selected_Once_Every_One_Of_Its_Cells_Is()
    {
        var items = Items("Ann", "Bea");
        var grid = CreateGrid(new DataGridCollectionView(items));
        var columns = grid.Columns.ToList();

        var events = new List<DataGridSelectedColumnsChangedEventArgs>();
        grid.SelectedColumnsChanged += (_, e) => events.Add(e);

        // Only half the column, so it is not the column that is selected - just two cells in it.
        SelectCells(grid, (RowIndex:0, ColumnIndex:0));
        Assert.Empty(grid.SelectedColumns);
        Assert.Empty(events);

        SelectCells(grid, (RowIndex:0, ColumnIndex:0), (RowIndex:1, ColumnIndex:0));

        Assert.Equal(new[] { columns[0] }, grid.SelectedColumns.ToArray());
        Assert.Same(columns[0], Assert.Single(Assert.Single(events).AddedColumns));
    }

    [AvaloniaFact]
    public void Taking_One_Cell_Back_Out_Of_A_Full_Column_Unselects_The_Column()
    {
        var items = Items("Ann", "Bea");
        var grid = CreateGrid(new DataGridCollectionView(items));
        var columns = grid.Columns.ToList();

        SelectCells(grid, (RowIndex: 0, ColumnIndex: 0), (RowIndex: 1, ColumnIndex: 0));
        Assert.Equal(new[] { columns[0] }, grid.SelectedColumns.ToArray());

        var events = new List<DataGridSelectedColumnsChangedEventArgs>();
        grid.SelectedColumnsChanged += (_, e) => events.Add(e);

        SelectCells(grid, (RowIndex: 0, ColumnIndex: 0));

        Assert.Empty(grid.SelectedColumns);
        Assert.Single(grid.SelectedCells);
        Assert.Same(columns[0], Assert.Single(Assert.Single(events).RemovedColumns));
    }

    [AvaloniaFact]
    public void Replacing_The_Cell_Selection_Reports_The_Columns_That_Stop_Being_Selected()
    {
        var items = Items("Ann", "Bea");
        var grid = CreateGrid(new DataGridCollectionView(items));
        var columns = grid.Columns.ToList();

        grid.SelectAllCells();
        grid.UpdateLayout();
        Assert.Equal(columns, grid.SelectedColumns.ToList());

        var events = new List<DataGridSelectedColumnsChangedEventArgs>();
        grid.SelectedColumnsChanged += (_, e) => events.Add(e);

        grid.SelectedCells = new ObservableCollection<DataGridCellInfo>();
        grid.UpdateLayout();

        Assert.Empty(grid.SelectedCells);
        Assert.Empty(grid.SelectedColumns);
        Assert.Equal(columns, events.SelectMany(e => e.RemovedColumns).ToList());
    }

    [AvaloniaFact]
    public void A_Column_That_Stays_Selected_Through_A_Rebuild_Is_Not_Reported_Again()
    {
        var items = Items("Ann", "Bea");
        var grid = CreateGrid(new DataGridCollectionView(items));
        var columns = grid.Columns.ToList();

        SelectCells(grid, (RowIndex: 0, ColumnIndex: 0), (RowIndex: 1, ColumnIndex: 0));
        Assert.Equal(new[] { columns[0] }, grid.SelectedColumns.ToArray());

        var events = new List<DataGridSelectedColumnsChangedEventArgs>();
        grid.SelectedColumnsChanged += (_, e) => events.Add(e);

        // The first column is fully selected before and after, so nothing about it changed - only
        // the second column becomes selected.
        SelectCells(grid,
            (RowIndex: 0, ColumnIndex: 0), (RowIndex: 1, ColumnIndex: 0),
            (RowIndex: 0, ColumnIndex: 1), (RowIndex: 1, ColumnIndex: 1));

        Assert.Equal(columns, grid.SelectedColumns.ToList());
        Assert.Same(columns[1], Assert.Single(Assert.Single(events).AddedColumns));
        Assert.Empty(events.SelectMany(e => e.RemovedColumns));
    }

    [AvaloniaFact]
    public void A_Bound_SelectedColumns_Collection_Follows_A_Cell_Selection_Rebuild()
    {
        var items = Items("Ann", "Bea");
        var grid = CreateGrid(new DataGridCollectionView(items));
        var columns = grid.Columns.ToList();

        var bound = new ObservableCollection<DataGridColumn>();
        grid.SelectedColumns = bound;

        SelectCells(grid, (RowIndex: 0, ColumnIndex: 0), (RowIndex: 1, ColumnIndex: 0));
        Assert.Equal(new[] { columns[0] }, bound.ToArray());

        SelectCells(grid, (RowIndex: 0, ColumnIndex: 1), (RowIndex: 1, ColumnIndex: 1));

        // The application's own collection has to end up describing the same thing the grid does.
        Assert.Equal(new[] { columns[1] }, bound.ToArray());
    }

    [AvaloniaFact]
    public void Deleting_The_Row_That_Was_Missing_Completes_The_Column()
    {
        var items = Items("Ann", "Bea", "Carl");
        var grid = CreateGrid(new DataGridCollectionView(items));
        var columns = grid.Columns.ToList();

        SelectCells(grid, (RowIndex:0, ColumnIndex:0), (RowIndex:1, ColumnIndex:0));
        Assert.Empty(grid.SelectedColumns);

        // Removing the one unselected row leaves every remaining row selected in that column.
        items.RemoveAt(2);
        grid.UpdateLayout();

        Assert.Equal(new[] { columns[0] }, grid.SelectedColumns.ToArray());
    }

    [AvaloniaFact]
    public void Selecting_Every_Cell_Selects_Every_Column()
    {
        var items = Items("Ann", "Bea");
        var grid = CreateGrid(new DataGridCollectionView(items));
        var columns = grid.Columns.ToList();

        grid.SelectAllCells();
        grid.UpdateLayout();

        Assert.Equal(4, grid.SelectedCells.Count);
        Assert.Equal(columns, grid.SelectedColumns.ToList());
    }

    [AvaloniaFact]
    public void Switching_To_Row_Selection_Reports_The_Selected_Columns_As_Deselected()
    {
        var items = Items("Ann", "Bea");
        var grid = CreateGrid(new DataGridCollectionView(items));
        var columns = grid.Columns.ToList();

        grid.SelectAllCells();
        grid.UpdateLayout();
        Assert.Equal(columns, grid.SelectedColumns.ToList());

        var events = new List<DataGridSelectedColumnsChangedEventArgs>();
        grid.SelectedColumnsChanged += (_, e) => events.Add(e);

        // The application switches the grid to picking whole rows. Cell and column selection is not
        // something that mode has, so it goes - and anything bound to SelectedColumns is told.
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        grid.UpdateLayout();

        Assert.Empty(grid.SelectedCells);
        Assert.Empty(grid.SelectedColumns);
        Assert.Equal(columns, events.SelectMany(e => e.RemovedColumns).Distinct().ToList());
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static ObservableCollection<Row> Items(params string[] names)
        => new(names.Select(n => new Row { Name = n }));

    private static void SelectCells(DataGrid grid, params (int RowIndex, int ColumnIndex)[] cells)
    {
        var columns = grid.Columns.ToList();
        var selection = new ObservableCollection<DataGridCellInfo>(
            cells.Select(c => new DataGridCellInfo(
                grid.DataConnection.GetDataItem(c.RowIndex),
                columns[c.ColumnIndex],
                c.RowIndex,
                c.ColumnIndex,
                isValid: true)));

        grid.SelectedCells = selection;
        grid.UpdateLayout();
    }

    /// <summary>The selected cells as (row item name, column index), in a stable order.</summary>
    private static (string, int)[] SelectedCellNames(DataGrid grid)
        => grid.SelectedCells
            .Select(c => (((Row)c.Item!).Name, c.ColumnIndex))
            .OrderBy(c => c.Item1)
            .ThenBy(c => c.Item2)
            .ToArray();

    private static DataGrid CreateGrid(DataGridCollectionView view)
    {
        var root = new Window
        {
            Width = 640,
            Height = 480
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            ItemsSource = view,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionUnit = DataGridSelectionUnit.Cell,
            SelectionMode = DataGridSelectionMode.Extended
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(Row.Name))
        });

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Team",
            Binding = new Binding(nameof(Row.Team))
        });

        root.Content = grid;
        root.Show();
        grid.UpdateLayout();

        return grid;
    }

    public class Row
    {
        public string Name { get; set; } = string.Empty;
        public string Team { get; set; } = string.Empty;
    }
}
