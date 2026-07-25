using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls.Selection;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

/// <summary>
/// The selection model indexes into DataGridSelectionSource, a projection of the grid's view, rather
/// than into the view itself. These cover the case the projection cannot resolve by item identity:
/// the same object reference present more than once, where a reference-keyed lookup only knows one
/// of the positions and the change notification's index is the only way to tell them apart.
/// </summary>
public class DataGridSelectionSourceTests
{
    [AvaloniaFact]
    public void Removing_First_Of_Two_Reference_Duplicates_Shifts_Selection_On_The_Later_One()
    {
        var duplicate = new object();
        var other = new object();
        var items = new ObservableCollection<object> { duplicate, other, duplicate };
        var (grid, selection) = CreateGrid(items);

        selection.Select(2); // the trailing occurrence

        items.RemoveAt(0); // the leading occurrence

        Assert.Equal(new[] { other, duplicate }, items.ToArray());
        SelectionSource.AssertTracksView(grid, selection);
        Assert.Equal(1, selection.SelectedIndex);
        Assert.Same(duplicate, selection.SelectedItem);
    }

    [AvaloniaFact]
    public void Replacing_First_Of_Two_Reference_Duplicates_Leaves_Selection_On_The_Later_One()
    {
        var duplicate = new object();
        var other = new object();
        var replacement = new object();
        var items = new ObservableCollection<object> { duplicate, other, duplicate };
        var (grid, selection) = CreateGrid(items);

        selection.Select(2); // the trailing occurrence

        items[0] = replacement; // the leading occurrence

        Assert.Equal(new[] { replacement, other, duplicate }, items.ToArray());
        SelectionSource.AssertTracksView(grid, selection);
        Assert.Equal(2, selection.SelectedIndex);
        Assert.Same(duplicate, selection.SelectedItem);
    }

    [AvaloniaFact]
    public void Inserting_Before_A_Reference_Duplicate_Shifts_Selection_On_The_Later_One()
    {
        var duplicate = new object();
        var other = new object();
        var items = new ObservableCollection<object> { duplicate, other, duplicate };
        var (grid, selection) = CreateGrid(items);

        selection.Select(2); // the trailing occurrence

        items.Insert(0, new object());

        SelectionSource.AssertTracksView(grid, selection);
        Assert.Equal(3, selection.SelectedIndex);
        Assert.Same(duplicate, selection.SelectedItem);
    }

    private static (DataGrid Grid, SelectionModel<object> Selection) CreateGrid(ObservableCollection<object> items)
    {
        var selection = new SelectionModel<object> { SingleSelect = false };
        var grid = new DataGrid
        {
            ItemsSource = new DataGridCollectionView(items),
            Selection = selection,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false
        };

        return (grid, selection);
    }
}
