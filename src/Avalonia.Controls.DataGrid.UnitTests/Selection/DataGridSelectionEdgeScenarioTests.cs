// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

/// <summary>
/// Selection in the awkward moments: the first click of a session is a shift-click with nothing to
/// extend from, the grid only allows one row at a time, the row the user is leaving refuses to
/// commit, or the item an application selects is not one the grid is showing.
/// </summary>
public class DataGridSelectionEdgeScenarioTests
{
    // ---- shift-click with nothing to extend from ---------------------------------------------

    [AvaloniaFact]
    public void Shift_Clicking_Before_Any_Other_Click_Extends_From_The_First_Row()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            // The user has clicked nothing yet, so the range runs from where the grid starts - the
            // top row - down to the row they shift-clicked.
            ClickRow(grid, rowIndex: 2, modifiers: KeyModifiers.Shift);

            Assert.Equal(
                new object[] { items[0], items[1], items[2] },
                grid.SelectedItems.Cast<object>().OrderBy(i => ((Record)i).Amount).ToArray());
            Assert.Same(items[2], grid.SelectedItem);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Shift_Clicking_In_Single_Selection_Mode_Selects_Only_The_Clicked_Row()
    {
        var (grid, items, root) = CreateGrid(selectionMode: DataGridSelectionMode.Single);
        try
        {
            ClickRow(grid, rowIndex: 0);
            ClickRow(grid, rowIndex: 3, modifiers: KeyModifiers.Shift);

            // A grid that holds one row cannot honour a range, and quietly holds the row the user
            // actually clicked rather than the whole span.
            Assert.Equal(new object[] { items[3] }, grid.SelectedItems.Cast<object>().ToArray());
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Select_All_Does_Nothing_In_Single_Selection_Mode()
    {
        var (grid, items, root) = CreateGrid(selectionMode: DataGridSelectionMode.Single);
        try
        {
            ClickRow(grid, rowIndex: 1);

            grid.SelectAll();

            // "Every row" is not something this mode can hold, and picking one of them on the
            // user's behalf would be a guess - so the row they chose is left alone.
            Assert.Equal(new object[] { items[1] }, grid.SelectedItems.Cast<object>().ToArray());
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Switching_To_Single_Selection_Keeps_Only_The_Primary_Row()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            ClickRow(grid, rowIndex: 1);
            ClickRow(grid, rowIndex: 3, modifiers: KeyModifiers.Shift);
            Assert.Equal(3, grid.SelectedItems.Count);

            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.UpdateLayout();

            // Narrowing the mode is not a request for rows, so it trims rather than refuses.
            Assert.True(grid.SelectedItems.Count <= 1);
        }
        finally
        {
            root.Close();
        }
    }

    // ---- the row the user is leaving will not commit ------------------------------------------

    [AvaloniaFact]
    public void Clearing_The_Selection_Is_Refused_While_The_Edited_Row_Will_Not_Commit()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            ClickRow(grid, rowIndex: 0);
            Assert.True(grid.BeginEdit());
            grid.UpdateLayout();

            grid.RowEditEnding += (_, e) => e.Cancel = true;

            grid.SelectedItem = null!;
            grid.UpdateLayout();

            // The application tried to drop the selection while the row was refusing to close.
            // Letting the selection go would strand the open editor on a row nothing points at.
            Assert.Same(items[0], grid.SelectedItem);
            Assert.NotNull(grid.EditingRow);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clearing_The_Selection_Works_Once_The_Row_Is_Allowed_To_Commit()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            ClickRow(grid, rowIndex: 0);
            Assert.True(grid.BeginEdit());
            grid.UpdateLayout();

            var veto = true;
            grid.RowEditEnding += (_, e) => e.Cancel = veto;

            grid.SelectedItem = null!;
            Assert.Same(items[0], grid.SelectedItem);

            veto = false;
            grid.SelectedItem = null!;
            grid.UpdateLayout();

            Assert.Null(grid.SelectedItem);
            Assert.Empty(grid.SelectedItems);
            Assert.Null(grid.EditingRow);
        }
        finally
        {
            root.Close();
        }
    }

    // ---- selecting something the grid is not showing ------------------------------------------

    [AvaloniaFact]
    public void Selecting_An_Item_That_Is_Not_In_The_List_Clears_The_Selection()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            ClickRow(grid, rowIndex: 1);
            Assert.Same(items[1], grid.SelectedItem);

            grid.SelectedItem = new Record { Name = "Not in the grid" };
            grid.UpdateLayout();

            Assert.Null(grid.SelectedItem);
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.Empty(grid.SelectedItems);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Selecting_A_Row_The_Filter_Hides_Selects_Nothing()
    {
        var items = Records(4);
        var view = new DataGridCollectionView(items);
        var (grid, root) = CreateGrid(view);
        try
        {
            view.Filter = o => ((Record)o).Amount < 2;
            grid.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            grid.SelectedItem = items[0];
            grid.UpdateLayout();
            Assert.Same(items[0], grid.SelectedItem);

            // Row 3 is filtered out. It is not a row the user could have clicked, so there is
            // nothing for the grid to select - and it says so rather than pointing at a row that
            // is not there.
            grid.SelectedItem = items[3];
            grid.UpdateLayout();

            Assert.Null(grid.SelectedItem);
            Assert.Empty(grid.SelectedItems);
            Assert.False(grid.GetRowSelectionFromRowIndex(0));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Filtering_A_Selected_Row_Out_Of_View_Hides_It_Without_Losing_It()
    {
        var items = Records(4);
        var view = new DataGridCollectionView(items);
        var (grid, root) = CreateGrid(view);
        try
        {
            grid.SelectedItem = items[3];
            grid.UpdateLayout();
            Assert.Equal(3, grid.SelectedIndex);

            view.Filter = o => ((Record)o).Amount < 2;
            grid.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // The row is off screen, so there is no index to report - but the user's choice is not
            // thrown away by a filter they may well undo in a moment.
            Assert.Equal(2, view.Count);
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.Contains(items[3], grid.SelectedItems.Cast<object>());

            view.Filter = null;
            grid.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, grid.SelectedIndex);
            Assert.Same(items[3], grid.SelectedItem);
        }
        finally
        {
            root.Close();
        }
    }

    // ---- selecting by index ------------------------------------------------------------------

    [AvaloniaFact]
    public void Selecting_By_Index_Past_The_End_Selects_Nothing()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            grid.SelectedIndex = 1;
            grid.UpdateLayout();
            Assert.Same(items[1], grid.SelectedItem);

            grid.SelectedIndex = 99;
            grid.UpdateLayout();

            // There is no row 99, so there is no row to point at. The grid reports an empty
            // selection rather than leaving the caller believing row 99 is selected.
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.Null(grid.SelectedItem);
            Assert.Empty(grid.SelectedItems);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Selecting_Index_Minus_One_Clears_The_Selection()
    {
        var (grid, _, root) = CreateGrid();
        try
        {
            grid.SelectedIndex = 2;
            grid.UpdateLayout();

            grid.SelectedIndex = -1;
            grid.UpdateLayout();

            Assert.Null(grid.SelectedItem);
            Assert.Empty(grid.SelectedItems);
        }
        finally
        {
            root.Close();
        }
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static ObservableCollection<Record> Records(int count)
        => new(Enumerable.Range(0, count).Select(i => new Record { Name = "Row " + i, Amount = i }));

    private static void ClickRow(DataGrid grid, int rowIndex, KeyModifiers modifiers = KeyModifiers.None)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(
            RawInputModifiers.LeftMouseButton,
            PointerUpdateKind.LeftButtonPressed);
        var args = new PointerPressedEventArgs(grid, pointer, grid, new Point(0, 0), 0, properties, modifiers);

        grid.UpdateStateOnMouseLeftButtonDown(args, columnIndex: 0, slot: grid.SlotFromRowIndex(rowIndex), allowEdit: false);
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static (DataGrid grid, ObservableCollection<Record> items, Window root) CreateGrid(
        DataGridSelectionMode selectionMode = DataGridSelectionMode.Extended)
    {
        var items = Records(4);
        var (grid, root) = CreateGrid(new DataGridCollectionView(items), selectionMode);
        return (grid, items, root);
    }

    private static (DataGrid grid, Window root) CreateGrid(
        DataGridCollectionView view,
        DataGridSelectionMode selectionMode = DataGridSelectionMode.Extended)
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
            SelectionMode = selectionMode
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(Record.Name)) { Mode = BindingMode.TwoWay }
        });

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Amount",
            Binding = new Binding(nameof(Record.Amount)) { Mode = BindingMode.TwoWay }
        });

        root.Content = grid;
        root.Show();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();

        return (grid, root);
    }

    /// <summary>
    /// Implements <see cref="IEditableObject"/> so that a row edit can be cancelled - the grid only
    /// offers that when the item can restore its own previous values.
    /// </summary>
    public sealed class Record : IEditableObject
    {
        private string? _nameBackup;
        private int _amountBackup;

        public string Name { get; set; } = string.Empty;
        public int Amount { get; set; }

        public void BeginEdit()
        {
            _nameBackup = Name;
            _amountBackup = Amount;
        }

        public void CancelEdit()
        {
            Name = _nameBackup ?? string.Empty;
            Amount = _amountBackup;
        }

        public void EndEdit()
        {
            _nameBackup = null;
        }
    }
}
