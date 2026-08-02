// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

/// <summary>
/// Covers the scenario of an application driving column selection from its view model:
/// <c>SelectedColumns</c> is bound to a collection the view model owns, the view model mutates
/// that collection to highlight columns, and column headers the user clicks show up in it.
/// </summary>
public class DataGridSelectedColumnsBindingTests
{
    [AvaloniaFact]
    public void Adding_Column_To_Bound_Collection_Selects_Whole_Column()
    {
        var items = CreateItems(3);
        var (grid, root) = CreateGrid(items);
        try
        {
            var viewModelSelection = new ObservableCollection<DataGridColumn>();
            grid.SelectedColumns = viewModelSelection;
            grid.UpdateLayout();

            var priceColumn = grid.ColumnsInternal[1];
            viewModelSelection.Add(priceColumn);
            grid.UpdateLayout();

            // Every row's cell in that column is selected, and no other column is.
            Assert.Equal(items.Count, grid.SelectedCells.Count);
            Assert.All(grid.SelectedCells, cell => Assert.Same(priceColumn, cell.Column));
            Assert.Contains(priceColumn, grid.SelectedColumns);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Removing_One_Column_From_Bound_Collection_Keeps_The_Others_Selected()
    {
        var items = CreateItems(2);
        var (grid, root) = CreateGrid(items);
        try
        {
            var nameColumn = grid.ColumnsInternal[0];
            var priceColumn = grid.ColumnsInternal[1];

            var viewModelSelection = new ObservableCollection<DataGridColumn> { nameColumn, priceColumn };
            grid.SelectedColumns = viewModelSelection;
            grid.UpdateLayout();

            Assert.Equal(items.Count * 2, grid.SelectedCells.Count);

            var events = new List<DataGridSelectedColumnsChangedEventArgs>();
            grid.SelectedColumnsChanged += (_, e) => events.Add(e);

            viewModelSelection.Remove(nameColumn);
            grid.UpdateLayout();

            Assert.Equal(items.Count, grid.SelectedCells.Count);
            Assert.All(grid.SelectedCells, cell => Assert.Same(priceColumn, cell.Column));

            var removed = events.SelectMany(e => e.RemovedColumns).ToList();
            Assert.Contains(nameColumn, removed);
            Assert.DoesNotContain(priceColumn, removed);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clearing_Bound_Collection_Clears_The_Column_Selection()
    {
        var items = CreateItems(3);
        var (grid, root) = CreateGrid(items);
        try
        {
            var nameColumn = grid.ColumnsInternal[0];
            var viewModelSelection = new ObservableCollection<DataGridColumn> { nameColumn };
            grid.SelectedColumns = viewModelSelection;
            grid.UpdateLayout();

            Assert.NotEmpty(grid.SelectedCells);
            var nameHeader = GetColumnHeader(grid, nameColumn);
            Assert.Contains(":selected", nameHeader.Classes);

            viewModelSelection.Clear();
            grid.UpdateLayout();

            Assert.Empty(grid.SelectedColumns);
            Assert.Empty(grid.SelectedCells);
            Assert.DoesNotContain(":selected", nameHeader.Classes);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Replacing_Column_In_Bound_Collection_Moves_The_Selection()
    {
        var items = CreateItems(2);
        var (grid, root) = CreateGrid(items);
        try
        {
            var nameColumn = grid.ColumnsInternal[0];
            var priceColumn = grid.ColumnsInternal[1];

            var viewModelSelection = new ObservableCollection<DataGridColumn> { nameColumn };
            grid.SelectedColumns = viewModelSelection;
            grid.UpdateLayout();

            viewModelSelection[0] = priceColumn;
            grid.UpdateLayout();

            Assert.Equal(items.Count, grid.SelectedCells.Count);
            Assert.All(grid.SelectedCells, cell => Assert.Same(priceColumn, cell.Column));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clicking_A_Column_Header_Publishes_The_Column_To_The_Bound_Collection()
    {
        var items = CreateItems(3);
        var (grid, root) = CreateGrid(items);
        try
        {
            var viewModelSelection = new ObservableCollection<DataGridColumn>();
            grid.SelectedColumns = viewModelSelection;
            grid.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var priceColumn = grid.ColumnsInternal[1];
            ClickColumnHeader(grid, priceColumn);

            Assert.Contains(priceColumn, viewModelSelection);
            Assert.Single(viewModelSelection);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clicking_Another_Column_Header_Replaces_The_Column_In_The_Bound_Collection()
    {
        var items = CreateItems(3);
        var (grid, root) = CreateGrid(items);
        try
        {
            var viewModelSelection = new ObservableCollection<DataGridColumn>();
            grid.SelectedColumns = viewModelSelection;
            grid.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var nameColumn = grid.ColumnsInternal[0];
            var priceColumn = grid.ColumnsInternal[1];

            ClickColumnHeader(grid, nameColumn);
            Assert.Equal(new[] { nameColumn }, viewModelSelection);

            ClickColumnHeader(grid, priceColumn);
            Assert.Equal(new[] { priceColumn }, viewModelSelection);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Null_Entry_In_The_Bound_Collection_Is_Skipped()
    {
        var items = CreateItems(2);
        var (grid, root) = CreateGrid(items);
        try
        {
            var priceColumn = grid.ColumnsInternal[1];

            // A view model that has not resolved every column yet must not break the grid.
            var viewModelSelection = new ObservableCollection<DataGridColumn> { null!, priceColumn };
            grid.SelectedColumns = viewModelSelection;
            grid.UpdateLayout();

            Assert.Equal(items.Count, grid.SelectedCells.Count);
            Assert.All(grid.SelectedCells, cell => Assert.Same(priceColumn, cell.Column));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Column_Removed_From_The_Grid_Stops_Contributing_To_The_Selection()
    {
        var items = CreateItems(2);
        var (grid, root) = CreateGrid(items);
        try
        {
            var extraColumn = new DataGridTextColumn
            {
                Header = "Extra",
                Binding = new Binding(nameof(Product.Name))
            };
            grid.ColumnsInternal.Add(extraColumn);
            grid.UpdateLayout();

            var priceColumn = grid.ColumnsInternal[1];
            var viewModelSelection = new ObservableCollection<DataGridColumn> { priceColumn, extraColumn };
            grid.SelectedColumns = viewModelSelection;
            grid.UpdateLayout();

            Assert.Equal(items.Count * 2, grid.SelectedCells.Count);

            // The user removes the column while the view model still remembers it as selected.
            grid.ColumnsInternal.Remove(extraColumn);
            grid.UpdateLayout();

            viewModelSelection.Move(0, 1);
            grid.UpdateLayout();

            Assert.Equal(items.Count, grid.SelectedCells.Count);
            Assert.All(grid.SelectedCells, cell => Assert.Same(priceColumn, cell.Column));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Selecting_Columns_On_An_Empty_Grid_Selects_Nothing_And_Recovers_When_Rows_Arrive()
    {
        var items = new ObservableCollection<Product>();
        var (grid, root) = CreateGrid(items);
        try
        {
            var nameColumn = grid.ColumnsInternal[0];
            var viewModelSelection = new ObservableCollection<DataGridColumn> { nameColumn };
            grid.SelectedColumns = viewModelSelection;
            grid.UpdateLayout();

            Assert.Empty(grid.SelectedCells);

            items.Add(new Product { Name = "A", Price = 1 });
            grid.UpdateLayout();

            // Re-applying the same view model selection once there are rows now highlights the column.
            viewModelSelection.Clear();
            viewModelSelection.Add(nameColumn);
            grid.UpdateLayout();

            Assert.Single(grid.SelectedCells);
            Assert.Same(nameColumn, grid.SelectedCells[0].Column);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Detaching_The_Bound_Collection_Stops_It_From_Driving_The_Grid()
    {
        var items = CreateItems(3);
        var (grid, root) = CreateGrid(items);
        try
        {
            var nameColumn = grid.ColumnsInternal[0];
            var priceColumn = grid.ColumnsInternal[1];

            var firstViewModelSelection = new ObservableCollection<DataGridColumn> { nameColumn };
            grid.SelectedColumns = firstViewModelSelection;
            grid.UpdateLayout();

            var secondViewModelSelection = new ObservableCollection<DataGridColumn> { priceColumn };
            grid.SelectedColumns = secondViewModelSelection;
            grid.UpdateLayout();

            Assert.All(grid.SelectedCells, cell => Assert.Same(priceColumn, cell.Column));

            // The collection that is no longer bound must not be able to change the selection.
            firstViewModelSelection.Add(nameColumn);
            grid.UpdateLayout();

            Assert.All(grid.SelectedCells, cell => Assert.Same(priceColumn, cell.Column));
            Assert.DoesNotContain(nameColumn, grid.SelectedColumns);
        }
        finally
        {
            root.Close();
        }
    }

    private static void ClickColumnHeader(DataGrid grid, DataGridColumn column, KeyModifiers modifiers = KeyModifiers.None)
    {
        var header = GetColumnHeader(grid, column);

        var center = new Point(header.Bounds.Width / 2, header.Bounds.Height / 2);
        var position = header.TranslatePoint(center, grid) ?? center;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);

        header.RaiseEvent(new PointerPressedEventArgs(header, pointer, grid, position, 0, properties, modifiers, clickCount: 1));
        grid.UpdateLayout();
    }

    private static DataGridColumnHeader GetColumnHeader(DataGrid grid, DataGridColumn column)
    {
        return grid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .First(header => ReferenceEquals(header.OwningColumn, column));
    }

    private static ObservableCollection<Product> CreateItems(int count)
    {
        var items = new ObservableCollection<Product>();
        for (var i = 0; i < count; i++)
        {
            items.Add(new Product { Name = "Item " + i, Price = i * 10 });
        }

        return items;
    }

    private static (DataGrid grid, Window root) CreateGrid(IEnumerable<Product> items)
    {
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
            CanUserSelectColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrColumnHeader,
            HeadersVisibility = DataGridHeadersVisibility.All
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(Product.Name))
        });

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Price",
            Binding = new Binding(nameof(Product.Price))
        });

        root.Content = grid;
        root.Show();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (grid, root);
    }

    private sealed class Product
    {
        public string Name { get; set; } = string.Empty;

        public int Price { get; set; }
    }
}
