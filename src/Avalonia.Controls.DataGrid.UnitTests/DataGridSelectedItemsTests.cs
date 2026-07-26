using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.Selection;
using Avalonia.Controls.DataGridSorting;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests;

public class DataGridSelectedItemsTests
{
    [AvaloniaFact]
    public void SelectedItems_Raises_CollectionChanged_On_Selection_Change()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var grid = CreateGrid(items);
        var changes = new List<NotifyCollectionChangedEventArgs>();

        ((INotifyCollectionChanged)grid.SelectedItems).CollectionChanged += (_, e) => changes.Add(e);

        grid.SelectedItem = items[1];
        grid.SelectedItem = items[2];

        Assert.Collection(
            changes,
            e =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
                var newItems = Assert.IsAssignableFrom<IList>(e.NewItems);
                Assert.Equal("B", Assert.Single(newItems.Cast<string>()));
            },
            e =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Remove, e.Action);
                var oldItems = Assert.IsAssignableFrom<IList>(e.OldItems);
                Assert.Equal("B", Assert.Single(oldItems.Cast<string>()));
            },
            e =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
                var newItems = Assert.IsAssignableFrom<IList>(e.NewItems);
                Assert.Equal("C", Assert.Single(newItems.Cast<string>()));
            });
    }

    [AvaloniaFact]
    public void SelectedItems_Binding_Applies_ViewModel_Selection()
    {
        var vm = new SelectionViewModel();
        vm.SelectedItems.Add(vm.Items[1]);
        vm.SelectedItems.Add(vm.Items[3]);

        var grid = CreateGrid(vm.Items);
        grid.Bind(DataGrid.SelectedItemsProperty, new Binding(nameof(SelectionViewModel.SelectedItems))
        {
            Mode = BindingMode.TwoWay,
            Source = vm
        });

        grid.UpdateLayout();

        var rows = GetRows(grid);
        Assert.True(rows.First(x => x.Index == 1).IsSelected);
        Assert.True(rows.First(x => x.Index == 3).IsSelected);
        Assert.All(rows.Where(x => x.Index != 1 && x.Index != 3), r => Assert.False(r.IsSelected));
    }

    [AvaloniaFact]
    public void SelectedItems_Binding_Updates_ViewModel_When_Selection_Changes()
    {
        var vm = new SelectionViewModel();
        var grid = CreateGrid(vm.Items);

        grid.Bind(DataGrid.SelectedItemsProperty, new Binding(nameof(SelectionViewModel.SelectedItems))
        {
            Mode = BindingMode.TwoWay,
            Source = vm
        });

        grid.SelectAll();
        grid.UpdateLayout();

        Assert.Equal(vm.Items.Count, vm.SelectedItems.Count);

        grid.SelectedItem = null;
        grid.UpdateLayout();

        Assert.Empty(vm.SelectedItems);
    }

    [AvaloniaFact]
    public void Modifying_Bound_SelectedItems_Updates_DataGrid()
    {
        var vm = new SelectionViewModel();
        var grid = CreateGrid(vm.Items);

        grid.Bind(DataGrid.SelectedItemsProperty, new Binding(nameof(SelectionViewModel.SelectedItems))
        {
            Mode = BindingMode.TwoWay,
            Source = vm
        });

        vm.SelectedItems.Add(vm.Items[2]);
        vm.SelectedItems.Add(vm.Items[4]);

        grid.UpdateLayout();

        var rows = GetRows(grid);
        Assert.True(rows.First(x => x.Index == 2).IsSelected);
        Assert.True(rows.First(x => x.Index == 4).IsSelected);
        Assert.All(rows.Where(x => x.Index != 2 && x.Index != 4), r => Assert.False(r.IsSelected));
    }

    [AvaloniaFact]
    public void Removing_From_Bound_SelectedItems_Deselects_Row()
    {
        var vm = new SelectionViewModel();
        var grid = CreateGrid(vm.Items);

        grid.Bind(DataGrid.SelectedItemsProperty, new Binding(nameof(SelectionViewModel.SelectedItems))
        {
            Mode = BindingMode.TwoWay,
            Source = vm
        });

        vm.SelectedItems.Add(vm.Items[1]);
        grid.UpdateLayout();

        var rows = GetRows(grid);
        Assert.True(rows.First(x => x.DataContext == vm.Items[1]).IsSelected);

        vm.SelectedItems.Remove(vm.Items[1]);
        grid.UpdateLayout();

        rows = GetRows(grid);
        Assert.All(rows, r => Assert.False(r.IsSelected));
    }

    [AvaloniaFact]
    public void Selection_Preserved_When_Inserting_Before_Selected_Item()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var grid = CreateGrid(items);

        grid.SelectedItem = items[1];
        grid.UpdateLayout();

        items.Insert(0, "Z");
        var selectedImmediately = grid.SelectedItems.Cast<string>().ToList();
        Assert.Equal(new[] { "B" }, selectedImmediately);
        grid.UpdateLayout();

        var selectedItems = grid.SelectedItems.Cast<string>().ToList();
        Assert.Equal(new[] { "B" }, selectedItems);
        Assert.Equal("B", grid.SelectedItem);
        var selectedRow = GetRows(grid).First(r => r.IsSelected);
        Assert.Equal("B", selectedRow.DataContext);
    }

    [AvaloniaFact]
    public void SelectedItems_Preserved_When_Adding_Item_Before_Current_In_Sorted_View()
    {
        var items = new ObservableCollection<SortableItem>(Enumerable.Range(1, 5).Select(i => new SortableItem(i)));
        var view = new DataGridCollectionView(items);
        var grid = CreateSortableGrid(view);

        ApplyIdSort(view, ListSortDirection.Descending);
        grid.UpdateLayout();

        grid.SelectedItem = items[1];
        grid.SelectedItems.Add(items[2]);
        grid.UpdateLayout();

        var selectedIds = grid.SelectedItems.Cast<SortableItem>().Select(x => x.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 2, 3 }, selectedIds);

        items.Add(new SortableItem(6));
        grid.UpdateLayout();

        selectedIds = grid.SelectedItems.Cast<SortableItem>().Select(x => x.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 2, 3 }, selectedIds);
        Assert.Contains(grid.SelectedItem, grid.SelectedItems.Cast<object>());
        Assert.Equal(view.IndexOf(grid.SelectedItem), grid.SelectedIndex);
    }

    [AvaloniaFact]
    public void Selection_Preserved_When_Items_Moved()
    {
        var items = new ObservableCollection<string> { "A", "B", "C", "D" };
        var grid = CreateGrid(items);

        grid.SelectedItem = items[1];
        grid.SelectedItems.Add(items[3]);
        grid.UpdateLayout();

        var expected = grid.SelectedItems.Cast<string>().OrderBy(x => x).ToArray();

        // Reorder via Move operations (matching sample behavior) and reapply selection snapshot.
        ReorderWithSelectionPreserve(items, new[] { "D", "B", "C", "A" }, grid);
        grid.UpdateLayout();

        var selected = grid.SelectedItems.Cast<string>().OrderBy(x => x).ToArray();
        Assert.Equal(expected, selected);
        Assert.Contains(grid.SelectedItem, grid.SelectedItems.Cast<object>());
    }

    [AvaloniaFact]
    public void Selection_Preserved_When_Sorting_Model_Reorders_Items()
    {
        var items = new ObservableCollection<SortableItem>
        {
            new SortableItem(3),
            new SortableItem(1),
            new SortableItem(2)
        };
        var view = new DataGridCollectionView(items);
        var grid = CreateSortableGrid(view);

        grid.UpdateLayout();

        grid.SelectedItem = items[2];
        grid.UpdateLayout();

        var selected = Assert.IsType<SortableItem>(grid.SelectedItem);
        var model = grid.SortingModel;
        var column = grid.ColumnDefinitions[0];

        model.Toggle(new SortingDescriptor(column, ListSortDirection.Ascending, column.SortMemberPath, culture: view.Culture));
        grid.UpdateLayout();

        Assert.Same(selected, grid.SelectedItem);
        Assert.Contains(selected, grid.SelectedItems.Cast<object>());
        Assert.Equal(view.IndexOf(selected), grid.SelectedIndex);
    }

    [AvaloniaFact]
    public void Selection_Preserved_When_External_Sort_In_Observe_Mode()
    {
        var items = new ObservableCollection<SortableItem>
        {
            new SortableItem(3),
            new SortableItem(1),
            new SortableItem(2)
        };
        var view = new DataGridCollectionView(items);
        var grid = CreateSortableGrid(view);

        grid.OwnsSortDescriptions = false;
        grid.UpdateLayout();

        grid.SelectedItem = items[1];
        grid.UpdateLayout();

        var selected = grid.SelectedItem;

        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(SortableItem.Id), ListSortDirection.Descending));
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();

        Assert.Same(selected, grid.SelectedItem);
        Assert.Contains(selected, grid.SelectedItems.Cast<object>());
        Assert.Equal(view.IndexOf(selected), grid.SelectedIndex);
    }

    [AvaloniaFact]
    public void SelectedItems_Survive_Repeated_Adds_And_Sorts()
    {
        var items = new ObservableCollection<SortableItem>(Enumerable.Range(1, 6).Select(i => new SortableItem(i)));
        var view = new DataGridCollectionView(items);
        var grid = CreateSortableGrid(view);

        grid.UpdateLayout();

        grid.SelectedItem = items[0];
        grid.SelectedItems.Add(items[1]);
        grid.SelectedItems.Add(items[2]);
        grid.UpdateLayout();

        items.Add(new SortableItem(7));

        ApplyIdSort(view, ListSortDirection.Ascending);
        grid.UpdateLayout();

        items.Add(new SortableItem(8));

        ApplyIdSort(view, ListSortDirection.Descending);
        grid.UpdateLayout();

        items.Add(new SortableItem(9));
        grid.UpdateLayout();

        var selectedIds = grid.SelectedItems.Cast<SortableItem>().Select(x => x.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, selectedIds);
        var current = Assert.IsType<SortableItem>(grid.SelectedItem);
        Assert.Contains(current, grid.SelectedItems.Cast<SortableItem>());
    }

    private static DataGrid CreateGrid(IList items)
    {
        var root = new Window
        {
            Width = 250,
            Height = 150,
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            ItemsSource = items,
            SelectionMode = DataGridSelectionMode.Extended,
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Value",
            Binding = new Binding(".")
        });

        root.Content = grid;
        root.Show();
        return grid;
    }

    private static DataGrid CreateSortableGrid(IEnumerable items)
    {
        var root = new Window
        {
            Width = 250,
            Height = 150,
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            ItemsSource = items,
            SelectionMode = DataGridSelectionMode.Extended,
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Id",
            Binding = new Binding(nameof(SortableItem.Id)),
            SortMemberPath = nameof(SortableItem.Id)
        });

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(SortableItem.Name))
        });

        root.Content = grid;
        root.Show();
        return grid;
    }

    private static void ApplyIdSort(DataGridCollectionView view, ListSortDirection direction)
    {
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(SortableItem.Id), direction));
        }
    }

    private static IReadOnlyList<DataGridRow> GetRows(DataGrid grid)
    {
        return grid.GetSelfAndVisualDescendants().OfType<DataGridRow>().ToList();
    }

    private static ISortingModel GetSortingModel(DataGrid grid) => grid.SortingModel;

    private static void ReorderWithSelectionPreserve(IList<string> items, IList<string> ordered, DataGrid grid)
    {
        var snapshot = grid.SelectedItems.Cast<object>().ToList();
        for (int targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var item = ordered[targetIndex];
            var currentIndex = items.IndexOf(item);
            if (currentIndex >= 0 && currentIndex != targetIndex && items is ObservableCollection<string> oc)
            {
                oc.Move(currentIndex, targetIndex);
            }
        }

        grid.SelectedItems.Clear();
        foreach (var item in snapshot)
        {
            grid.SelectedItems.Add(item);
        }
    }

    private sealed class StubSelectionModel : ISelectionModel
    {
        public IEnumerable? Source { get; set; }

        public bool SingleSelect { get; set; }

        public int SelectedIndex { get; set; }

        public IReadOnlyList<int> SelectedIndexes { get; set; } = Array.Empty<int>();

        public object? SelectedItem { get; set; }

        public IReadOnlyList<object?> SelectedItems { get; set; } = Array.Empty<object?>();

        public int AnchorIndex { get; set; }

        public int Count { get; set; }

        public event EventHandler<SelectionModelIndexesChangedEventArgs>? IndexesChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<SelectionModelSelectionChangedEventArgs>? SelectionChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? LostSelection
        {
            add { }
            remove { }
        }

        public event EventHandler? SourceReset
        {
            add { }
            remove { }
        }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public void BeginBatchUpdate()
        {
        }

        public void EndBatchUpdate()
        {
        }

        public bool IsSelected(int index) => SelectedIndexes.Contains(index);

        public void Select(int index) => throw new NotSupportedException();

        public void Deselect(int index) => throw new NotSupportedException();

        public void SelectRange(int start, int end) => throw new NotSupportedException();

        public void DeselectRange(int start, int end) => throw new NotSupportedException();

        public void SelectAll() => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();
    }

    private class SortableItem
    {
        public SortableItem(int id, string? name = null)
        {
            Id = id;
            Name = name ?? $"Item {id}";
        }

        public int Id { get; }

        public string Name { get; }
    }

    private class SelectionViewModel
    {
        public ObservableCollection<string> Items { get; } =
            new(Enumerable.Range(0, 6).Select(x => $"Item {x}").ToList());

        public ObservableCollection<object> SelectedItems { get; } = new();
    }
}
