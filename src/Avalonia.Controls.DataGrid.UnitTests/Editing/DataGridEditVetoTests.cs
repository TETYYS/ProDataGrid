// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Editing;

/// <summary>
/// Covers an application vetoing edits through the editing events - a locked record that must not be
/// opened for editing, and a row the user is not allowed to leave until it is valid.
/// </summary>
public class DataGridEditVetoTests
{
    [AvaloniaFact]
    public void Cancelling_BeginningEdit_Keeps_The_Cell_Out_Of_Edit_Mode()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            var attempts = 0;
            grid.BeginningEdit += (_, e) =>
            {
                attempts++;
                if (ReferenceEquals(e.Row.DataContext, items[0]))
                {
                    e.Cancel = true;
                }
            };

            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);
            Assert.False(grid.BeginEdit());
            grid.UpdateLayout();

            Assert.Equal(1, attempts);
            Assert.Null(GetEditor(grid));
            Assert.Equal(-1, grid.EditingColumnIndex);

            // A row the handler allows still opens normally.
            SetCurrentCell(grid, rowIndex: 1, columnIndex: 0);
            Assert.True(grid.BeginEdit());
            grid.UpdateLayout();

            Assert.NotNull(GetEditor(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Cancelling_CellEditEnding_Keeps_The_Editor_Open()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);
            Assert.True(grid.BeginEdit());
            grid.UpdateLayout();

            var editor = GetEditor(grid);
            Assert.NotNull(editor);
            editor!.Text = "Rejected";

            var veto = true;
            grid.CellEditEnding += (_, e) => e.Cancel = veto;

            Assert.False(grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true));
            grid.UpdateLayout();

            Assert.NotNull(GetEditor(grid));
            Assert.Equal("Row 0", items[0].Name);

            // Once the handler stops vetoing, the same commit goes through.
            veto = false;
            Assert.True(grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true));
            grid.UpdateLayout();

            Assert.Equal("Rejected", items[0].Name);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Cancelling_RowEditEnding_Holds_The_User_On_The_Row()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);
            Assert.True(grid.BeginEdit());
            grid.UpdateLayout();

            var editor = GetEditor(grid);
            Assert.NotNull(editor);
            editor!.Text = string.Empty;

            // The application refuses to let an empty name leave the row.
            grid.RowEditEnding += (_, e) =>
            {
                if (e.Row.DataContext is Record record && string.IsNullOrEmpty(record.Name))
                {
                    e.Cancel = true;
                }
            };

            Assert.False(grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true));
            grid.UpdateLayout();

            Assert.NotNull(grid.EditingRow);
            Assert.Same(items[0], grid.EditingRow!.DataContext);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Vetoed_Row_Commit_Blocks_Moving_The_Selection_Away()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);
            Assert.True(grid.BeginEdit());
            grid.UpdateLayout();

            grid.RowEditEnding += (_, e) => e.Cancel = true;

            grid.SelectedItem = items[2];
            grid.UpdateLayout();

            // Selection could not move because the row edit refused to end.
            Assert.Same(items[0], grid.SelectedItem);
            Assert.NotNull(grid.EditingRow);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void RowEditEnded_Reports_The_Action_That_Finished_The_Edit()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            DataGridEditAction? lastAction = null;
            grid.RowEditEnded += (_, e) => lastAction = e.EditAction;

            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);
            Assert.True(grid.BeginEdit());
            grid.UpdateLayout();
            GetEditor(grid)!.Text = "Committed";

            Assert.True(grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true));
            grid.UpdateLayout();

            Assert.Equal(DataGridEditAction.Commit, lastAction);
            Assert.Equal("Committed", items[0].Name);

            SetCurrentCell(grid, rowIndex: 1, columnIndex: 0);
            Assert.True(grid.BeginEdit());
            grid.UpdateLayout();
            GetEditor(grid)!.Text = "Discarded";

            Assert.True(grid.CancelEdit(DataGridEditingUnit.Row));
            grid.UpdateLayout();

            Assert.Equal(DataGridEditAction.Cancel, lastAction);
            Assert.Equal("Row 1", items[1].Name);
        }
        finally
        {
            root.Close();
        }
    }

    private static TextBox? GetEditor(DataGrid grid)
    {
        return grid.GetVisualDescendants()
            .OfType<DataGridCell>()
            .Select(cell => cell.Content as TextBox)
            .FirstOrDefault(textBox => textBox != null);
    }

    private static void SetCurrentCell(DataGrid grid, int rowIndex, int columnIndex)
    {
        var slot = grid.SlotFromRowIndex(rowIndex);
        grid.UpdateSelectionAndCurrency(columnIndex, slot, DataGridSelectionAction.SelectCurrent, scrollIntoView: true);
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static (DataGrid grid, ObservableCollection<Record> items, Window root) CreateGrid()
    {
        var items = new ObservableCollection<Record>();
        for (var i = 0; i < 3; i++)
        {
            items.Add(new Record { Name = "Row " + i, Amount = i });
        }

        var root = new Window
        {
            Width = 640,
            Height = 480
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Extended
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

        return (grid, items, root);
    }

    /// <summary>
    /// Implements <see cref="IEditableObject"/> because the grid only lets a row edit be cancelled
    /// when the item can restore its own previous values.
    /// </summary>
    private sealed class Record : IEditableObject
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
