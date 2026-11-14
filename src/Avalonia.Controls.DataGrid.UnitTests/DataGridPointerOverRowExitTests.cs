// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests;

/// <summary>
/// What the row under the mouse looks like as the pointer comes and goes. A highlight that outlives
/// the pointer leaves a row looking hovered while the mouse is somewhere else entirely, so the grid
/// has to give it up once the pointer is gone - but not merely because the pointer moved from one
/// part of the grid to another.
/// </summary>
public class DataGridPointerOverRowExitTests
{
    [AvaloniaFact]
    public void Moving_The_Mouse_Away_From_The_Grid_Leaves_No_Row_Highlighted()
    {
        var (grid, root, outside) = CreateGrid();
        var mouse = new TestMouse(grid, root);
        try
        {
            var row = Row(grid, 1);
            mouse.MoveOver(row);
            Assert.Equal(1, grid.MouseOverRowIndex);
            AssertOnlyHighlighted(grid, row);

            mouse.LeaveGridFor(outside);

            Assert.Null(grid.MouseOverRowIndex);
            Assert.Empty(HighlightedRows(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Moving_Between_Cells_Of_The_Same_Row_Keeps_It_Highlighted()
    {
        var (grid, root, _) = CreateGrid();
        var mouse = new TestMouse(grid, root);
        try
        {
            var row = Row(grid, 1);
            mouse.MoveOver(row);
            AssertOnlyHighlighted(grid, row);

            // Crossing from one cell into the next tells the grid the pointer left it, even though
            // the pointer is still inside - on a descendant.
            var secondCell = row.GetVisualDescendants().OfType<DataGridCell>().ElementAt(1);
            mouse.MoveOver(secondCell);

            Assert.Equal(row.Index, grid.MouseOverRowIndex);
            AssertOnlyHighlighted(grid, row);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Being_Told_The_Grid_Was_Exited_While_The_Mouse_Is_Still_On_A_Cell_Keeps_The_Highlight()
    {
        var (grid, root, _) = CreateGrid();
        var mouse = new TestMouse(grid, root);
        try
        {
            var row = Row(grid, 1);
            var cell = row.GetVisualDescendants().OfType<DataGridCell>().First();
            mouse.MoveOver(cell);
            AssertOnlyHighlighted(grid, row);

            // A flyout opening over the grid, or a cell boundary crossing, can hand the grid an
            // "exited" while the pointer never actually left it. Believing that would blank the
            // highlight out from under a mouse that has not moved.
            mouse.RaiseGridExitedWhileStillInside();

            Assert.Equal(row.Index, grid.MouseOverRowIndex);
            AssertOnlyHighlighted(grid, row);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void The_Highlight_Moves_With_The_Mouse_From_Row_To_Row()
    {
        var (grid, root, _) = CreateGrid();
        var mouse = new TestMouse(grid, root);
        try
        {
            var first = Row(grid, 0);
            mouse.MoveOver(first);
            AssertOnlyHighlighted(grid, first);

            var third = Row(grid, 2);
            mouse.MoveOver(third);

            Assert.Equal(2, grid.MouseOverRowIndex);
            AssertOnlyHighlighted(grid, third);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Coming_Back_Over_A_Row_Highlights_It_Again()
    {
        var (grid, root, outside) = CreateGrid();
        var mouse = new TestMouse(grid, root);
        try
        {
            mouse.MoveOver(Row(grid, 1));
            mouse.LeaveGridFor(outside);
            Assert.Null(grid.MouseOverRowIndex);

            var row = Row(grid, 1);
            mouse.MoveOver(row);

            Assert.Equal(1, grid.MouseOverRowIndex);
            AssertOnlyHighlighted(grid, row);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Sliding_Off_A_Row_Onto_Its_Group_Header_Drops_The_Highlight()
    {
        var (grid, root) = CreateGroupedGrid();
        var mouse = new TestMouse(grid, root);
        try
        {
            var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.IsVisible);
            mouse.MoveOver(row);
            AssertOnlyHighlighted(grid, row);

            var header = grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>().First();
            mouse.MoveOver(header);

            // A group header is not a row, so nothing should look hovered.
            Assert.Null(grid.MouseOverRowIndex);
            Assert.Empty(HighlightedRows(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Sliding_Back_Off_A_Group_Header_Onto_A_Row_Highlights_That_Row()
    {
        var (grid, root) = CreateGroupedGrid();
        var mouse = new TestMouse(grid, root);
        try
        {
            var header = grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>().First();
            mouse.MoveOver(header);
            Assert.Null(grid.MouseOverRowIndex);

            var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.IsVisible);
            mouse.MoveOver(row);

            Assert.Equal(row.Index, grid.MouseOverRowIndex);
            AssertOnlyHighlighted(grid, row);
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Leaving_The_Grid_From_A_Group_Header_Leaves_Nothing_Highlighted()
    {
        var (grid, root) = CreateGroupedGrid();
        var mouse = new TestMouse(grid, root);
        try
        {
            mouse.MoveOver(grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.IsVisible));
            mouse.MoveOver(grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>().First());

            mouse.LeaveGridFor(root);

            Assert.Null(grid.MouseOverRowIndex);
            Assert.Empty(HighlightedRows(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Resting_In_The_Empty_Space_Below_The_Last_Row_Highlights_Nothing()
    {
        // Three rows in a tall grid, so there is a wide empty band under the last one.
        var (grid, root, _) = CreateGrid(rowCount: 3);
        var mouse = new TestMouse(grid, root);
        try
        {
            var lastRow = Row(grid, 2);
            mouse.MoveOver(lastRow);
            AssertOnlyHighlighted(grid, lastRow);

            var presenter = grid.GetVisualDescendants().OfType<DataGridRowsPresenter>().First();
            var belowTheRows = new Point(
                presenter.Bounds.Width / 2,
                lastRow.Bounds.Bottom + (presenter.Bounds.Height - lastRow.Bounds.Bottom) / 2);
            mouse.MoveOverPoint(presenter, grid.TranslatePoint(belowTheRows, grid) ?? belowTheRows);

            Assert.Null(grid.MouseOverRowIndex);
            Assert.Empty(HighlightedRows(grid));
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void Scrolling_The_Hovered_Row_Out_Of_Sight_Leaves_No_Row_Looking_Hovered()
    {
        var (grid, root, _) = CreateGrid(rowCount: 60);
        var mouse = new TestMouse(grid, root);
        try
        {
            var row = Row(grid, 1);
            var restingPoint = PointInside(grid, row);
            mouse.MoveOver(row);
            AssertOnlyHighlighted(grid, row);

            // The mouse stays put; the rows move a long way under it.
            grid.ScrollIntoView(grid.CollectionView.Cast<object>().ElementAt(40), null);
            Pump(grid);

            // Whatever the grid decides about the row that scrolled away, no row that is on screen
            // may be left wearing a highlight the mouse is not on.
            var arrived = RowAtPoint(grid, restingPoint);
            Assert.NotNull(arrived);
            foreach (var highlighted in HighlightedRows(grid))
            {
                Assert.Same(arrived, highlighted);
            }
        }
        finally
        {
            root.Close();
        }
    }

    [AvaloniaFact]
    public void The_First_Move_After_A_Scroll_Highlights_The_Row_Now_Under_The_Mouse()
    {
        var (grid, root, _) = CreateGrid(rowCount: 60);
        var mouse = new TestMouse(grid, root);
        try
        {
            mouse.MoveOver(Row(grid, 1));

            grid.ScrollIntoView(grid.CollectionView.Cast<object>().ElementAt(40), null);
            Pump(grid);

            var nowUnderTheMouse = grid.GetVisualDescendants()
                .OfType<DataGridRow>()
                .First(r => r.IsVisible);
            mouse.MoveOver(nowUnderTheMouse);

            Assert.Equal(nowUnderTheMouse.Index, grid.MouseOverRowIndex);
            AssertOnlyHighlighted(grid, nowUnderTheMouse);
        }
        finally
        {
            root.Close();
        }
    }

    private static DataGridRow Row(DataGrid grid, int index)
        => grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.IsVisible && r.Index == index);

    private static Point PointInside(Visual relativeTo, Visual target)
    {
        var centre = new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
        return target.TranslatePoint(centre, relativeTo) ?? centre;
    }

    private static DataGridRow? RowAtPoint(DataGrid grid, Point point)
    {
        var presenter = grid.GetVisualDescendants().OfType<DataGridRowsPresenter>().FirstOrDefault();
        if (presenter == null)
        {
            return null;
        }

        var presenterPoint = grid.TranslatePoint(point, presenter) ?? point;
        return presenter.Children
            .OfType<DataGridRow>()
            .FirstOrDefault(row => row.IsVisible && row.Bounds.Contains(presenterPoint));
    }

    private static DataGridRow[] HighlightedRows(DataGrid grid)
        => grid.GetSelfAndVisualDescendants()
            .OfType<DataGridRow>()
            .Where(row => ((IPseudoClasses)row.Classes).Contains(":pointerover"))
            .ToArray();

    private static void AssertOnlyHighlighted(DataGrid grid, DataGridRow expected)
    {
        var highlighted = HighlightedRows(grid);
        Assert.Single(highlighted);
        Assert.Same(expected, highlighted[0]);
    }

    private static void Pump(DataGrid grid)
    {
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Drives the enter/move/exit sequence a real pointer produces, so a test can just say where the
    /// mouse now is.
    /// </summary>
    private sealed class TestMouse
    {
        private readonly DataGrid _grid;
        private readonly Window _root;
        private InputElement? _over;

        public TestMouse(DataGrid grid, Window root)
        {
            _grid = grid;
            _root = root;
        }

        public void MoveOver(InputElement target) => MoveOverPoint(target, PointInside(_grid, target));

        public void MoveOverPoint(InputElement target, Point point)
        {
            if (_over != null && !ReferenceEquals(_over, target))
            {
                Raise(InputElement.PointerExitedEvent, _over, point);
            }

            _root.SetPointerOverElementForTests(target);
            Raise(InputElement.PointerMovedEvent, target, point);

            if (!ReferenceEquals(_over, target))
            {
                Raise(InputElement.PointerEnteredEvent, target, point);
            }

            _over = target;
            Pump(_grid);
        }

        /// <summary>
        /// Raises "exited" on the grid without moving the pointer off it - the pointer stays over
        /// whatever it was already over.
        /// </summary>
        public void RaiseGridExitedWhileStillInside()
        {
            Raise(InputElement.PointerExitedEvent, _grid, _over != null ? PointInside(_grid, _over) : default);
            Pump(_grid);
        }

        public void LeaveGridFor(InputElement outside)
        {
            var point = PointInside(_grid, outside);

            if (_over != null)
            {
                Raise(InputElement.PointerExitedEvent, _over, point);
            }

            _root.SetPointerOverElementForTests(outside);
            Raise(InputElement.PointerExitedEvent, _grid, point);

            _over = null;
            Pump(_grid);
        }

        private void Raise(RoutedEvent<PointerEventArgs> routedEvent, InputElement source, Point point)
        {
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);
            source.RaiseEvent(new PointerEventArgs(
                routedEvent, source, pointer, _grid, point, 0, properties, KeyModifiers.None));
        }
    }

    private static (DataGrid Grid, Window Root, Control Outside) CreateGrid(int rowCount = 6)
    {
        var people = new ObservableCollection<Person>(
            Enumerable.Range(0, rowCount).Select(i => new Person($"Person {i}", $"City {i}")));

        var root = new Window
        {
            Width = 420,
            Height = 280
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
            Binding = new Binding(nameof(Person.Name))
        });

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "City",
            Binding = new Binding(nameof(Person.City))
        });

        var outside = new Border { Height = 24 };
        DockPanel.SetDock(outside, Dock.Bottom);

        // A DockPanel rather than a StackPanel so the grid is height-constrained and actually
        // virtualizes - a StackPanel would hand it infinite height and realize every row.
        var panel = new DockPanel();
        panel.Children.Add(outside);
        panel.Children.Add(grid);

        root.Content = panel;
        root.Show();
        Pump(grid);

        return (grid, root, outside);
    }

    private static (DataGrid Grid, Window Root) CreateGroupedGrid()
    {
        var people = new ObservableCollection<Person>
        {
            new("Ada", "London"),
            new("Grace", "London"),
            new("Alan", "Cambridge")
        };

        var view = new DataGridCollectionView(people);
        view.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(Person.City)));

        var root = new Window
        {
            Width = 420,
            Height = 320
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = view
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(Person.Name))
        });

        root.Content = grid;
        root.Show();
        Pump(grid);

        return (grid, root);
    }

    private sealed record Person(string Name, string City);
}
