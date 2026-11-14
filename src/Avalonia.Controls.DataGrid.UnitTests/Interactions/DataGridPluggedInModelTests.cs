// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using Avalonia.Controls.DataGridClipboard;
using Avalonia.Controls.DataGridEditing;
using Avalonia.Controls.DataGridFilling;
using Avalonia.Controls.DataGridInteractions;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Interactions;

/// <summary>
/// Turning a custom behaviour on and back off at runtime - the shape of a "simple mode" toggle or a
/// per-document override. Handing a model back is how an app says "go back to normal", so the grid
/// has to end up working again rather than sitting there with nothing plugged in.
/// </summary>
public class DataGridPluggedInModelTests
{
    [AvaloniaFact]
    public void Turning_Off_A_Custom_Fill_Rule_Brings_The_Built_In_One_Back()
    {
        var items = new ObservableCollection<Row>
        {
            new() { A = 1, B = 2, C = 0, D = 0 }
        };

        var (grid, root) = CreateGrid(items);
        try
        {
            grid.FillModel = new ConstantFillModel(99);
            Fill(grid);

            Assert.Equal(99, items[0].C);
            Assert.Equal(99, items[0].D);

            items[0].C = 0;
            items[0].D = 0;

            grid.FillModel = null!;
            Fill(grid);

            // Back to the stock behaviour: 1, 2 continues as 3, 4.
            Assert.Equal(3, items[0].C);
            Assert.Equal(4, items[0].D);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void An_App_Watching_The_Fill_Rule_Hears_It_Change_And_Change_Back()
    {
        var (grid, root) = CreateGrid(new ObservableCollection<Row> { new() { A = 1, B = 2 } });
        try
        {
            var changes = 0;
            grid.PropertyChanged += (_, e) =>
            {
                if (e.Property == DataGrid.FillModelProperty)
                {
                    changes++;
                }
            };

            var custom = new ConstantFillModel(7);
            grid.FillModel = custom;
            Assert.Same(custom, grid.FillModel);

            grid.FillModel = null!;

            Assert.Equal(2, changes);
            Assert.NotNull(grid.FillModel);
            Assert.NotSame(custom, grid.FillModel);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Handing_Back_The_Same_Fill_Rule_Is_Not_Reported_As_A_Change()
    {
        var (grid, root) = CreateGrid(new ObservableCollection<Row> { new() { A = 1, B = 2 } });
        try
        {
            var custom = new ConstantFillModel(7);
            grid.FillModel = custom;

            var changes = 0;
            grid.PropertyChanged += (_, e) =>
            {
                if (e.Property == DataGrid.FillModelProperty)
                {
                    changes++;
                }
            };

            grid.FillModel = custom;

            Assert.Equal(0, changes);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Turning_Off_A_Custom_Drag_Rule_Leaves_The_Grid_With_A_Working_One()
    {
        var (grid, root) = CreateGrid(new ObservableCollection<Row> { new() { A = 1, B = 2 } });
        try
        {
            var custom = new NeverDragsModel();
            grid.RangeInteractionModel = custom;

            // A one-pixel twitch does not start a drag-select while the custom rule is in charge.
            Assert.False(grid.RangeInteractionModel.IsSelectionDragThresholdMet(
                new Point(0, 0), new Point(100, 100)));

            grid.RangeInteractionModel = null!;

            Assert.NotNull(grid.RangeInteractionModel);
            Assert.NotSame(custom, grid.RangeInteractionModel);
            Assert.True(grid.RangeInteractionModel.IsSelectionDragThresholdMet(
                new Point(0, 0), new Point(100, 100)));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Turning_Off_A_Custom_Typing_Rule_Leaves_The_Grid_With_A_Working_One()
    {
        var (grid, root) = CreateGrid(new ObservableCollection<Row> { new() { A = 1, B = 2 } });
        try
        {
            var custom = new NeverEditsModel();
            grid.EditingInteractionModel = custom;
            Assert.Same(custom, grid.EditingInteractionModel);

            grid.EditingInteractionModel = null!;

            Assert.NotNull(grid.EditingInteractionModel);
            Assert.NotSame(custom, grid.EditingInteractionModel);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Turning_Off_A_Custom_Paste_Rule_Leaves_The_Grid_With_A_Working_One()
    {
        var (grid, root) = CreateGrid(new ObservableCollection<Row> { new() { A = 1, B = 2 } });
        try
        {
            var custom = new RefusingImportModel();
            grid.ClipboardImportModel = custom;
            Assert.Same(custom, grid.ClipboardImportModel);

            grid.ClipboardImportModel = null!;

            Assert.NotNull(grid.ClipboardImportModel);
            Assert.NotSame(custom, grid.ClipboardImportModel);
        }
        finally
        {
            root.Close();
        }
    }

    private static void Fill(DataGrid grid)
    {
        // The two filled-in cells dragged out across the rest of the row.
        var source = new DataGridCellRange(0, 0, 0, 1);
        var target = new DataGridCellRange(0, 0, 0, 3);
        grid.FillModel.ApplyFill(new DataGridFillContext(grid, source, target));
    }

    private static (DataGrid Grid, Window Root) CreateGrid(ObservableCollection<Row> items)
    {
        var root = new Window
        {
            Width = 520,
            Height = 260
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionUnit = DataGridSelectionUnit.Cell,
            ItemsSource = items
        };

        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            grid.ColumnsInternal.Add(new DataGridNumericColumn
            {
                Header = name,
                Binding = new Binding(name)
            });
        }

        root.Content = grid;
        root.Show();
        grid.UpdateLayout();

        return (grid, root);
    }

    private sealed class Row : System.ComponentModel.INotifyPropertyChanged
    {
        private int _a;
        private int _b;
        private int _c;
        private int _d;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public int A { get => _a; set => Set(ref _a, value, nameof(A)); }
        public int B { get => _b; set => Set(ref _b, value, nameof(B)); }
        public int C { get => _c; set => Set(ref _c, value, nameof(C)); }
        public int D { get => _d; set => Set(ref _d, value, nameof(D)); }

        private void Set(ref int field, int value, string name)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }

    private sealed class ConstantFillModel : IDataGridFillModel
    {
        private readonly int _value;

        public ConstantFillModel(int value) => _value = value;

        public void ApplyFill(DataGridFillContext context)
        {
            var target = context.TargetRange;
            for (var rowIndex = target.StartRow; rowIndex <= target.EndRow; rowIndex++)
            {
                using var scope = context.BeginRowEdit(rowIndex, out var item);
                if (item == null)
                {
                    continue;
                }

                for (var columnIndex = target.StartColumn; columnIndex <= target.EndColumn; columnIndex++)
                {
                    if (context.SourceRange.Contains(rowIndex, columnIndex))
                    {
                        continue;
                    }

                    context.TrySetCellText(rowIndex, columnIndex, _value.ToString());
                }
            }
        }
    }

    private sealed class NeverDragsModel : IDataGridRangeInteractionModel
    {
        public bool IsSelectionDragThresholdMet(Point start, Point current) => false;

        public DataGridCellPosition ResolveSelectionAnchor(DataGridSelectionAnchorContext context)
            => context.CurrentCell;

        public DataGridCellRange BuildSelectionRange(DataGridSelectionRangeContext context)
            => new(context.Anchor.RowIndex, context.Anchor.ColumnIndex, context.Anchor.RowIndex, context.Anchor.ColumnIndex);

        public DataGridCellRange BuildFillHandleRange(DataGridFillHandleRangeContext context)
            => context.SourceRange;

        public DataGridAutoScrollDirection GetAutoScrollDirection(DataGridAutoScrollContext context)
            => new(0, 0);
    }

    private sealed class NeverEditsModel : IDataGridEditingInteractionModel
    {
        public bool IsTextInputFromGrid(DataGridTextInputContext context) => false;

        public bool ShouldBeginEditOnPointer(DataGridPointerEditContext context) => false;

        public string GetTextInputForEdit(DataGridTextInputEditContext context) => string.Empty;

        public bool TryApplyTextInput(DataGridTextInputApplyContext context) => false;
    }

    private sealed class RefusingImportModel : IDataGridClipboardImportModel
    {
        public bool Paste(DataGridClipboardImportContext context) => false;
    }
}
