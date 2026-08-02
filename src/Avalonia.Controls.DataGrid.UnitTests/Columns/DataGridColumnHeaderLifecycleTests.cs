// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Columns;

/// <summary>
/// The strip of column headers as columns are hidden, removed and re-added, and as a grid-wide
/// header menu is attached after the headers are already on screen. A header left behind by a column
/// that is gone is a header the user can still click.
/// </summary>
public class DataGridColumnHeaderLifecycleTests
{
    [AvaloniaFact]
    public void Removing_A_Column_Takes_Its_Header_With_It()
    {
        var (grid, root) = CreateGrid();
        try
        {
            Assert.Equal(new[] { "Name", "City", "Score" }, HeaderTexts(grid));

            grid.ColumnsInternal.Remove(grid.ColumnsInternal.First(c => Equals(c.Header, "City")));
            Pump(grid);

            Assert.Equal(new[] { "Name", "Score" }, HeaderTexts(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Removing_Every_Column_Leaves_No_Headers_Behind()
    {
        var (grid, root) = CreateGrid();
        try
        {
            while (grid.ColumnsInternal.Count > 0)
            {
                grid.ColumnsInternal.RemoveAt(grid.ColumnsInternal.Count - 1);
            }

            Pump(grid);

            Assert.Empty(HeaderTexts(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Hiding_A_Column_Takes_Its_Header_Off_The_Strip()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var city = grid.ColumnsInternal.First(c => Equals(c.Header, "City"));

            city.IsVisible = false;
            Pump(grid);

            Assert.Equal(new[] { "Name", "Score" }, HeaderTexts(grid));

            city.IsVisible = true;
            Pump(grid);

            Assert.Equal(new[] { "Name", "City", "Score" }, HeaderTexts(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Re_Adding_A_Removed_Column_Puts_A_Single_Header_Back()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var city = grid.ColumnsInternal.First(c => Equals(c.Header, "City"));

            grid.ColumnsInternal.Remove(city);
            Pump(grid);
            grid.ColumnsInternal.Add(city);
            Pump(grid);

            // Every column has a header again, and the removal did not leave a second "City" behind.
            Assert.Equal(new[] { "City", "Name", "Score" }, HeaderTexts(grid).OrderBy(t => t).ToArray());
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Attaching_A_Grid_Wide_Header_Menu_Reaches_Headers_That_Are_Already_On_Screen()
    {
        var (grid, root) = CreateGrid();
        try
        {
            Assert.All(Headers(grid), header => Assert.Null(header.ContextMenu));

            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "Choose columns" });
            grid.ColumnHeaderContextMenu = menu;
            Pump(grid);

            Assert.All(Headers(grid), header => Assert.Same(menu, header.ContextMenu));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Column_With_Its_Own_Header_Menu_Keeps_It_When_The_Grid_Gets_One()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var ownMenu = new ContextMenu();
            ownMenu.Items.Add(new MenuItem { Header = "Score options" });
            grid.ColumnsInternal.First(c => Equals(c.Header, "Score")).HeaderContextMenu = ownMenu;
            Pump(grid);

            var gridMenu = new ContextMenu();
            gridMenu.Items.Add(new MenuItem { Header = "Choose columns" });
            grid.ColumnHeaderContextMenu = gridMenu;
            Pump(grid);

            Assert.Same(ownMenu, HeaderFor(grid, "Score").ContextMenu);
            Assert.Same(gridMenu, HeaderFor(grid, "Name").ContextMenu);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Swapping_The_Grid_Wide_Header_Menu_Replaces_The_Old_One()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var first = new ContextMenu();
            first.Items.Add(new MenuItem { Header = "Old" });
            grid.ColumnHeaderContextMenu = first;
            Pump(grid);

            var second = new ContextMenu();
            second.Items.Add(new MenuItem { Header = "New" });
            grid.ColumnHeaderContextMenu = second;
            Pump(grid);

            Assert.All(Headers(grid), header => Assert.Same(second, header.ContextMenu));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Column_Added_After_The_Menu_Was_Attached_Gets_It_Too()
    {
        var (grid, root) = CreateGrid();
        try
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "Choose columns" });
            grid.ColumnHeaderContextMenu = menu;
            Pump(grid);

            grid.ColumnsInternal.Add(new DataGridTextColumn
            {
                Header = "Notes",
                Binding = new Binding(nameof(Person.Notes))
            });
            Pump(grid);

            Assert.Same(menu, HeaderFor(grid, "Notes").ContextMenu);
        }
        finally
        {
            root.Close();
        }
    }

    /// <summary>
    /// The headers a user can see and click - the filler that pads out the strip is not one of them.
    /// </summary>
    private static DataGridColumnHeader[] Headers(DataGrid grid)
        => grid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .Where(header =>
                header.OwningColumn != null &&
                header.IsVisible &&
                header.OwningColumn != grid.ColumnsInternal.FillerColumn &&
                header.OwningColumn != grid.ColumnsInternal.RowGroupSpacerColumn)
            .ToArray();

    private static string[] HeaderTexts(DataGrid grid)
        => Headers(grid).Select(header => header.Content as string ?? string.Empty).ToArray();

    private static DataGridColumnHeader HeaderFor(DataGrid grid, string header)
        => Headers(grid).First(h => Equals(h.OwningColumn.Header, header));

    private static void Pump(DataGrid grid)
    {
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static (DataGrid Grid, Window Root) CreateGrid()
    {
        var people = new ObservableCollection<Person>
        {
            new("Ada", "London", 9, "first"),
            new("Bex", "Cambridge", 5, "second")
        };

        var root = new Window
        {
            Width = 520,
            Height = 260
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = people
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(Person.Name))
        });

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "City",
            Binding = new Binding(nameof(Person.City))
        });

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Score",
            Binding = new Binding(nameof(Person.Score))
        });

        root.Content = grid;
        root.Show();
        Pump(grid);

        return (grid, root);
    }

    private sealed record Person(string Name, string City, int Score, string Notes);
}
