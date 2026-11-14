// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Editing;

/// <summary>
/// Covers the spreadsheet-style "just start typing" flow: with
/// <see cref="DataGridEditTriggers.TextInput"/> enabled, typing over a selected cell opens the editor
/// and the typed character replaces the old value instead of being swallowed.
/// </summary>
public class DataGridTypeToEditTests
{
    [AvaloniaFact]
    public void Typing_Over_A_Cell_Opens_The_Editor_Seeded_With_The_Typed_Text()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            SetCurrentCell(grid, rowIndex: 1, columnIndex: 0);

            TypeText(grid, "N");

            var editor = GetEditingTextBox(grid);
            Assert.NotNull(editor);
            Assert.Equal("N", editor!.Text);
            Assert.Equal(1, editor.CaretIndex);
            Assert.Equal("Row 1", items[1].Name);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Committing_After_Typing_Writes_The_Typed_Text_To_The_Item()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);

            TypeText(grid, "Z");
            Assert.True(grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true));
            grid.UpdateLayout();

            Assert.Equal("Z", items[0].Name);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Cancelling_After_Typing_Restores_The_Original_Value()
    {
        var (grid, items, root) = CreateGrid();
        try
        {
            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);

            TypeText(grid, "Z");
            grid.CancelEdit(DataGridEditingUnit.Row);
            grid.UpdateLayout();

            Assert.Equal("Row 0", items[0].Name);
            Assert.Null(GetEditingTextBox(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Typing_Does_Not_Start_An_Edit_When_The_Trigger_Is_Not_Enabled()
    {
        var (grid, items, root) = CreateGrid(editTriggers: DataGridEditTriggers.F2);
        try
        {
            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);

            TypeText(grid, "Z");

            Assert.Null(GetEditingTextBox(grid));
            Assert.Equal("Row 0", items[0].Name);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Typing_Does_Not_Start_An_Edit_On_A_ReadOnly_Grid()
    {
        var (grid, items, root) = CreateGrid(isReadOnly: true);
        try
        {
            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);

            TypeText(grid, "Z");

            Assert.Null(GetEditingTextBox(grid));
            Assert.Equal("Row 0", items[0].Name);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Typing_While_Already_Editing_Is_Left_To_The_Editor()
    {
        var (grid, _, root) = CreateGrid();
        try
        {
            SetCurrentCell(grid, rowIndex: 0, columnIndex: 0);

            TypeText(grid, "A");
            var editor = GetEditingTextBox(grid);
            Assert.NotNull(editor);
            Assert.Equal("A", editor!.Text);

            // A second keystroke goes to the focused TextBox, not through the grid's type-to-edit path,
            // so the grid must not restart the edit and wipe what is already typed.
            TypeText(grid, "B");

            Assert.Same(editor, GetEditingTextBox(grid));
            Assert.Equal("A", GetEditingTextBox(grid)!.Text);
        }
        finally
        {
            root.Close();
        }
    }

    private static void TypeText(DataGrid grid, string text)
    {
        grid.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Route = InputElement.TextInputEvent.RoutingStrategies,
            Source = grid,
            Text = text
        });

        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();
    }

    private static TextBox? GetEditingTextBox(DataGrid grid)
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

    private static (DataGrid grid, ObservableCollection<RowItem> items, Window root) CreateGrid(
        DataGridEditTriggers editTriggers = DataGridEditTriggers.Default | DataGridEditTriggers.TextInput,
        bool isReadOnly = false)
    {
        var items = new ObservableCollection<RowItem>();
        for (var i = 0; i < 3; i++)
        {
            items.Add(new RowItem { Name = "Row " + i, Amount = i });
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
            IsReadOnly = isReadOnly,
            EditTriggers = editTriggers,
            SelectionMode = DataGridSelectionMode.Extended
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(RowItem.Name))
        });

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Amount",
            Binding = new Binding(nameof(RowItem.Amount))
        });

        root.Content = grid;
        root.Show();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();

        return (grid, items, root);
    }

    private sealed class RowItem
    {
        public string Name { get; set; } = string.Empty;

        public int Amount { get; set; }
    }
}
