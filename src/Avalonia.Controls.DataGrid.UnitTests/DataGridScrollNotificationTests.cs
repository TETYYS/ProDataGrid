// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace Avalonia.Controls.DataGridTests;

/// <summary>
/// The grid telling the rest of the window that it scrolled. Apps hang a frozen summary strip or a
/// second grid off these notifications, so a scroll the user can see has to be a scroll the app
/// hears about - and a wheel turn that moves nothing must not pretend otherwise.
/// </summary>
public class DataGridScrollNotificationTests
{
    [AvaloniaFact]
    public void Spinning_The_Wheel_Down_Tells_The_App_The_Grid_Scrolled_Down()
    {
        var (grid, root) = CreateGrid();
        var scrolls = RecordVerticalScrolls(grid);
        try
        {
            SpinWheel(grid, deltaY: -3);

            var reported = Assert.Single(scrolls);
            Assert.True(reported > 0, $"Scrolling down should report a downward offset, got {reported}.");
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Spinning_The_Wheel_Back_Up_Tells_The_App_The_Grid_Scrolled_Up()
    {
        var (grid, root) = CreateGrid();
        try
        {
            SpinWheel(grid, deltaY: -3);
            Pump(grid);

            var scrolls = RecordVerticalScrolls(grid);
            SpinWheel(grid, deltaY: 3);

            var reported = Assert.Single(scrolls);
            Assert.True(reported < 0, $"Scrolling up should report an upward offset, got {reported}.");
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Spinning_The_Wheel_At_The_Top_Reports_Nothing()
    {
        var (grid, root) = CreateGrid();
        var scrolls = RecordVerticalScrolls(grid);
        try
        {
            // Already at the top - there is nowhere further up to go.
            SpinWheel(grid, deltaY: 5);

            Assert.Empty(scrolls);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Scrolling_Sideways_Tells_The_App_How_Far_Across_The_Grid_Is_Now()
    {
        var (grid, root) = CreateGrid(columnWidth: 400);
        var scrolls = RecordHorizontalScrolls(grid);
        try
        {
            SpinWheel(grid, deltaX: -2);

            var reported = Assert.Single(scrolls);
            Assert.Equal(grid.HorizontalOffset, reported);
            Assert.True(reported > 0, "Scrolling right should move the grid off its left edge.");
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Scrolling_Back_To_The_Left_Edge_Reports_That_Edge()
    {
        var (grid, root) = CreateGrid(columnWidth: 400);
        try
        {
            SpinWheel(grid, deltaX: -2);
            Pump(grid);

            var scrolls = RecordHorizontalScrolls(grid);
            SpinWheel(grid, deltaX: 20);

            var reported = Assert.Single(scrolls);
            Assert.Equal(0d, reported);
            Assert.Equal(0d, grid.HorizontalOffset);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Grid_With_Nothing_To_Scroll_Sideways_Reports_Nothing()
    {
        var (grid, root) = CreateGrid(columnWidth: 60);
        var scrolls = RecordHorizontalScrolls(grid);
        try
        {
            // The columns already fit, so there is no sideways travel to report.
            SpinWheel(grid, deltaX: -5);

            Assert.Empty(scrolls);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void A_Diagonal_Scroll_Is_Reported_On_Both_Axes()
    {
        var (grid, root) = CreateGrid(columnWidth: 400);
        var vertical = RecordVerticalScrolls(grid);
        var horizontal = RecordHorizontalScrolls(grid);
        try
        {
            SpinWheel(grid, deltaX: -2, deltaY: -3);

            Assert.Single(vertical);
            Assert.Single(horizontal);
        }
        finally
        {
            root.Close();
        }
    }

    private static List<double> RecordVerticalScrolls(DataGrid grid)
    {
        var offsets = new List<double>();
        grid.VerticalScroll += (_, e) => offsets.Add(e.NewValue);
        return offsets;
    }

    private static List<double> RecordHorizontalScrolls(DataGrid grid)
    {
        var offsets = new List<double>();
        grid.HorizontalScroll += (_, e) => offsets.Add(e.NewValue);
        return offsets;
    }

    private static void SpinWheel(DataGrid grid, double deltaX = 0, double deltaY = 0)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);
        var args = new PointerWheelEventArgs(
            grid,
            pointer,
            grid,
            new Point(grid.Bounds.Width / 2, grid.Bounds.Height / 2),
            0,
            properties,
            KeyModifiers.None,
            new Vector(deltaX, deltaY));

        grid.RaiseEvent(args);
        Pump(grid);
    }

    private static void Pump(DataGrid grid)
    {
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static (DataGrid Grid, Window Root) CreateGrid(double columnWidth = 120)
    {
        var people = new ObservableCollection<Person>(
            Enumerable.Range(0, 80).Select(i => new Person($"Person {i}", $"City {i}")));

        var root = new Window
        {
            Width = 320,
            Height = 240
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = people
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Width = new DataGridLength(columnWidth),
            Binding = new Binding(nameof(Person.Name))
        });

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "City",
            Width = new DataGridLength(columnWidth),
            Binding = new Binding(nameof(Person.City))
        });

        root.Content = grid;
        root.Show();
        Pump(grid);

        return (grid, root);
    }

    private sealed record Person(string Name, string City);
}
