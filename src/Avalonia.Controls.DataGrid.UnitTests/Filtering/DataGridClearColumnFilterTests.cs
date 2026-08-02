// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Filtering;

/// <summary>
/// Taking a filter back off a column. Once a user has narrowed a grid down they need a way back to
/// the full list, and "clear this column" has to leave every other column's filter alone.
/// </summary>
public class DataGridClearColumnFilterTests
{
    [AvaloniaFact]
    public void Clearing_A_Column_Filter_Brings_The_Hidden_Rows_Back()
    {
        var (grid, root, nameColumn, _) = CreateGrid();
        try
        {
            FilterColumn(grid, nameColumn, nameof(Person.Name), "Ada");
            Assert.Equal(new[] { "Ada" }, VisibleNames(grid));

            var cleared = grid.ClearFilter(nameColumn);

            Assert.True(cleared);
            Assert.Equal(new[] { "Ada", "Grace", "Alan" }, VisibleNames(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clearing_One_Column_Leaves_The_Other_Columns_Filter_In_Place()
    {
        var (grid, root, nameColumn, cityColumn) = CreateGrid();
        try
        {
            FilterColumn(grid, nameColumn, nameof(Person.Name), "Ad");
            FilterColumn(grid, cityColumn, nameof(Person.City), "London");
            Assert.Equal(new[] { "Ada" }, VisibleNames(grid));

            Assert.True(grid.ClearFilter(nameColumn));

            // Only the name filter went away, so the city filter still hides everyone outside London.
            Assert.Equal(new[] { "Ada", "Grace" }, VisibleNames(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clearing_A_Column_That_Was_Never_Filtered_Changes_Nothing()
    {
        var (grid, root, nameColumn, cityColumn) = CreateGrid();
        try
        {
            FilterColumn(grid, nameColumn, nameof(Person.Name), "Ada");

            var cleared = grid.ClearFilter(cityColumn);

            Assert.False(cleared);
            Assert.Equal(new[] { "Ada" }, VisibleNames(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clearing_Every_Filtered_Column_Restores_The_Whole_Grid()
    {
        var (grid, root, nameColumn, cityColumn) = CreateGrid();
        try
        {
            FilterColumn(grid, nameColumn, nameof(Person.Name), "A");
            FilterColumn(grid, cityColumn, nameof(Person.City), "London");

            Assert.True(grid.ClearFilter(nameColumn));
            Assert.True(grid.ClearFilter(cityColumn));

            Assert.Equal(new[] { "Ada", "Grace", "Alan" }, VisibleNames(grid));
            Assert.Empty(grid.FilteringModel.Descriptors);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Filter_Written_Against_The_Columns_Sort_Path_Is_Still_Cleared_By_That_Column()
    {
        var (grid, root, nameColumn, _) = CreateGrid();
        try
        {
            // A filter restored from saved settings knows the property, not the column instance.
            grid.FilteringModel.SetOrUpdate(new FilteringDescriptor(
                columnId: "saved-filter-1",
                @operator: FilteringOperator.Contains,
                propertyPath: nameof(Person.Name),
                value: "Ada",
                stringComparison: StringComparison.OrdinalIgnoreCase));

            Assert.Equal(new[] { "Ada" }, VisibleNames(grid));

            var cleared = grid.ClearFilter(nameColumn);

            Assert.True(cleared);
            Assert.Equal(new[] { "Ada", "Grace", "Alan" }, VisibleNames(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Column_From_Another_Grid_Cannot_Clear_This_Grids_Filter()
    {
        var (grid, root, nameColumn, _) = CreateGrid();
        var (otherGrid, otherRoot, otherNameColumn, _) = CreateGrid();
        try
        {
            FilterColumn(grid, nameColumn, nameof(Person.Name), "Ada");

            var cleared = grid.ClearFilter(otherNameColumn);

            Assert.False(cleared);
            Assert.Equal(new[] { "Ada" }, VisibleNames(grid));
            Assert.Single(grid.FilteringModel.Descriptors);
        }
        finally
        {
            otherRoot.Close();
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clearing_Without_Naming_A_Column_Does_Nothing()
    {
        var (grid, root, nameColumn, _) = CreateGrid();
        try
        {
            FilterColumn(grid, nameColumn, nameof(Person.Name), "Ada");

            Assert.False(grid.ClearFilter(null!));

            Assert.Equal(new[] { "Ada" }, VisibleNames(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Clearing_By_Column_Id_Reports_Nothing_Removed_When_The_Id_Is_Unknown()
    {
        var (grid, root, nameColumn, _) = CreateGrid();
        try
        {
            FilterColumn(grid, nameColumn, nameof(Person.Name), "Ada");

            Assert.False(grid.ClearFilterByColumnId("no-such-column"));
            Assert.False(grid.ClearFilterByColumnId(null!));

            Assert.Equal(new[] { "Ada" }, VisibleNames(grid));
        }
        finally
        {
            root.Close();
        }
    }

    private static void FilterColumn(DataGrid grid, DataGridColumn column, string propertyPath, string contains)
    {
        grid.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: column,
            @operator: FilteringOperator.Contains,
            propertyPath: propertyPath,
            value: contains,
            stringComparison: StringComparison.OrdinalIgnoreCase));
    }

    private static string[] VisibleNames(DataGrid grid)
        => grid.CollectionView.Cast<Person>().Select(p => p.Name).ToArray();

    private static (DataGrid Grid, Window Root, DataGridTextColumn Name, DataGridTextColumn City) CreateGrid()
    {
        var people = new ObservableCollection<Person>
        {
            new("Ada", "London"),
            new("Grace", "London"),
            new("Alan", "Cambridge")
        };

        var root = new Window
        {
            Width = 400,
            Height = 240
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = people
        };

        var nameColumn = new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(Person.Name)),
            SortMemberPath = nameof(Person.Name)
        };

        var cityColumn = new DataGridTextColumn
        {
            Header = "City",
            Binding = new Binding(nameof(Person.City)),
            SortMemberPath = nameof(Person.City)
        };

        grid.ColumnsInternal.Add(nameColumn);
        grid.ColumnsInternal.Add(cityColumn);

        root.Content = grid;
        root.Show();
        grid.UpdateLayout();

        return (grid, root, nameColumn, cityColumn);
    }

    private sealed record Person(string Name, string City);
}
