// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls.DataGridSorting;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Sorting;

/// <summary>
/// Sort order as columns come and go. A grid keeps its sort in a model that outlives any one column,
/// so a column arriving late has to pick up the sort that is already running, and a column leaving
/// must not take everyone else's sort with it.
/// </summary>
public class DataGridColumnSortLifecycleTests
{
    [AvaloniaFact]
    public void A_Column_Added_To_An_Already_Sorted_Grid_Shows_That_Sort()
    {
        var (grid, root) = CreateGrid();
        try
        {
            // The user saved a layout sorted by score and it is restored before the columns are built.
            grid.SortingModel.SetOrUpdate(new SortingDescriptor(
                columnId: "Score",
                direction: ListSortDirection.Descending,
                propertyPath: nameof(Person.Score)));

            var scoreColumn = AddColumn(grid, "Score", nameof(Person.Score), columnKey: "Score");
            grid.UpdateLayout();

            Assert.Equal(ListSortDirection.Descending, scoreColumn.SortDirection);
            Assert.Equal(new[] { 9, 5, 1 }, VisibleScores(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Column_That_Arrives_Already_Sorted_Sorts_The_Grid_On_Arrival()
    {
        var (grid, root) = CreateGrid();
        try
        {
            // Declared already sorted, the way a restored layout or a XAML declaration expresses it,
            // and dropped into a grid that is already showing rows.
            var column = new DataGridTextColumn
            {
                Header = "Score",
                Binding = new Binding(nameof(Person.Score)),
                SortMemberPath = nameof(Person.Score),
                SortDirection = ListSortDirection.Ascending
            };

            grid.ColumnsInternal.Add(column);
            grid.UpdateLayout();

            Assert.Equal(new[] { 1, 5, 9 }, VisibleScores(grid));
            Assert.Equal(ListSortDirection.Ascending, column.SortDirection);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Pre_Sorted_Column_Arriving_Before_The_Grid_Is_Shown_Sorts_It_Too()
    {
        var source = new ObservableCollection<Person>
        {
            new("Bex", 5),
            new("Ada", 1),
            new("Cyd", 9)
        };

        var root = new Window { Width = 420, Height = 260 };
        root.SetThemeStyles();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = new DataGridCollectionView(source)
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Score",
            Binding = new Binding(nameof(Person.Score)),
            SortMemberPath = nameof(Person.Score),
            SortDirection = ListSortDirection.Ascending
        });

        root.Content = grid;
        root.Show();
        grid.UpdateLayout();

        try
        {
            Assert.Equal(new[] { 1, 5, 9 }, VisibleScores(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Column_With_Nothing_To_Sort_On_Loses_Its_Sort_Indicator_When_It_Arrives()
    {
        var (grid, root) = CreateGrid();
        try
        {
            // No binding, no sort path, no comparer - there is nothing this column could sort by.
            var column = new DataGridTextColumn
            {
                Header = "Notes",
                SortDirection = ListSortDirection.Ascending
            };

            grid.ColumnsInternal.Add(column);
            grid.UpdateLayout();

            Assert.Null(column.SortDirection);
            Assert.Empty(grid.SortingModel.Descriptors);
            Assert.Equal(new[] { 5, 1, 9 }, VisibleScores(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Removing_A_Sorted_Column_Leaves_The_Other_Columns_Sort_Alone()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var nameColumn = AddColumn(grid, "Name", nameof(Person.Name));
            var scoreColumn = AddColumn(grid, "Score", nameof(Person.Score));
            grid.UpdateLayout();

            scoreColumn.SortDirection = ListSortDirection.Ascending;
            grid.UpdateLayout();
            Assert.Equal(new[] { 1, 5, 9 }, VisibleScores(grid));

            grid.ColumnsInternal.Remove(nameColumn);
            grid.UpdateLayout();

            Assert.Equal(new[] { 1, 5, 9 }, VisibleScores(grid));
            Assert.Equal(ListSortDirection.Ascending, scoreColumn.SortDirection);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Giving_A_Column_A_Comparer_Before_It_Is_Sorted_Does_Not_Sort_The_Grid()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var column = AddColumn(grid, "Name", nameof(Person.Name));
            grid.UpdateLayout();

            column.CustomSortComparer = new ByLastLetter();
            grid.UpdateLayout();

            // Handing a column a comparer is not the same as asking for a sort.
            Assert.Null(column.SortDirection);
            Assert.Empty(grid.SortingModel.Descriptors);
            Assert.Equal(new[] { "Bex", "Ada", "Cyd" }, VisibleNames(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Swapping_The_Comparer_On_A_Sorted_Column_Reorders_The_Rows()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var column = AddColumn(grid, "Name", nameof(Person.Name));
            grid.UpdateLayout();

            column.SortDirection = ListSortDirection.Ascending;
            grid.UpdateLayout();
            Assert.Equal(new[] { "Ada", "Bex", "Cyd" }, VisibleNames(grid));

            column.CustomSortComparer = new ByLastLetter();
            grid.UpdateLayout();

            // Last letters: "Ada" -> a, "Cyd" -> d, "Bex" -> x.
            Assert.Equal(new[] { "Ada", "Cyd", "Bex" }, VisibleNames(grid));
            Assert.Equal(ListSortDirection.Ascending, column.SortDirection);

            column.SortDirection = ListSortDirection.Descending;
            grid.UpdateLayout();

            Assert.Equal(new[] { "Bex", "Cyd", "Ada" }, VisibleNames(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clearing_A_Sort_Puts_The_Rows_Back_In_Their_Original_Order()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var column = AddColumn(grid, "Score", nameof(Person.Score));
            grid.UpdateLayout();

            column.SortDirection = ListSortDirection.Ascending;
            grid.UpdateLayout();
            Assert.Equal(new[] { 1, 5, 9 }, VisibleScores(grid));

            column.SortDirection = null;
            grid.UpdateLayout();

            Assert.Equal(new[] { 5, 1, 9 }, VisibleScores(grid));
            Assert.Empty(grid.SortingModel.Descriptors);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Sort_Applied_Through_The_Model_Shows_Up_On_The_Column_Header()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var nameColumn = AddColumn(grid, "Name", nameof(Person.Name));
            var scoreColumn = AddColumn(grid, "Score", nameof(Person.Score));
            grid.UpdateLayout();

            nameColumn.SortDirection = ListSortDirection.Ascending;
            grid.UpdateLayout();

            // Something outside the headers - a saved view, a toolbar - re-sorts the grid.
            grid.SortingModel.Clear();
            grid.SortingModel.SetOrUpdate(new SortingDescriptor(
                columnId: scoreColumn,
                direction: ListSortDirection.Descending,
                propertyPath: nameof(Person.Score)));
            grid.UpdateLayout();

            Assert.Null(nameColumn.SortDirection);
            Assert.Equal(ListSortDirection.Descending, scoreColumn.SortDirection);
            Assert.Equal(new[] { 9, 5, 1 }, VisibleScores(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Sorting_By_A_Second_Column_Breaks_Ties_In_The_First()
    {
        var (grid, root) = CreateGrid(new[]
        {
            new Person("Bex", 5),
            new Person("Ada", 5),
            new Person("Cyd", 1)
        });

        try
        {
            var nameColumn = AddColumn(grid, "Name", nameof(Person.Name));
            var scoreColumn = AddColumn(grid, "Score", nameof(Person.Score));
            grid.UpdateLayout();

            scoreColumn.SortDirection = ListSortDirection.Ascending;
            nameColumn.SortDirection = ListSortDirection.Ascending;
            grid.UpdateLayout();

            Assert.Equal(new[] { "Cyd", "Ada", "Bex" }, VisibleNames(grid));
        }
        finally
        {
            root.Close();
        }
    }

    private static DataGridTextColumn AddColumn(DataGrid grid, string header, string path, object? columnKey = null)
    {
        var column = new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            SortMemberPath = path
        };

        if (columnKey != null)
        {
            column.ColumnKey = columnKey;
        }

        grid.ColumnsInternal.Add(column);
        return column;
    }

    private static string[] VisibleNames(DataGrid grid)
        => grid.CollectionView.Cast<Person>().Select(p => p.Name).ToArray();

    private static int[] VisibleScores(DataGrid grid)
        => grid.CollectionView.Cast<Person>().Select(p => p.Score).ToArray();

    private static (DataGrid Grid, Window Root) CreateGrid(IEnumerable<Person>? people = null)
    {
        var source = new ObservableCollection<Person>(people ?? new[]
        {
            new Person("Bex", 5),
            new Person("Ada", 1),
            new Person("Cyd", 9)
        });

        var root = new Window
        {
            Width = 420,
            Height = 260
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = new DataGridCollectionView(source)
        };

        root.Content = grid;
        root.Show();
        grid.UpdateLayout();

        return (grid, root);
    }

    private sealed record Person(string Name, int Score);

    /// <summary>
    /// Orders names by their last letter - an order nothing else in these tests would produce, so a
    /// grid that ends up in it can only have used this comparer.
    /// </summary>
    private sealed class ByLastLetter : IComparer
    {
        public int Compare(object? x, object? y) => Key(x).CompareTo(Key(y));

        private static char Key(object? value)
        {
            var text = value as string ?? (value as Person)?.Name ?? string.Empty;
            return text.Length > 0 ? text[^1] : '\0';
        }
    }
}
