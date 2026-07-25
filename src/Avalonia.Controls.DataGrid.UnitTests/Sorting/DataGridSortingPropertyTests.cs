// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridSorting;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Sorting;

public class DataGridSortingPropertyTests
{
    [AvaloniaFact]
    public void Custom_Sorting_Model_Applies_To_View()
    {
        var items = new ObservableCollection<Item>
        {
            new("B"),
            new("A"),
            new("C")
        };

        var sortingModel = new SortingModel();
        var grid = CreateGrid(items, sortingModel);
        grid.UpdateLayout();

        sortingModel.Apply(new[]
        {
            new SortingDescriptor("Name", ListSortDirection.Ascending, nameof(Item.Name))
        });

        grid.UpdateLayout();

        var view = Assert.IsType<DataGridCollectionView>(grid.ItemsSource);
        var sort = Assert.Single(view.SortDescriptions);
        Assert.Equal(nameof(Item.Name), sort.PropertyPath);
        Assert.Equal(ListSortDirection.Ascending, sort.Direction);
        Assert.Same(sortingModel, grid.SortingModel);
    }

    [AvaloniaFact]
    public void Sorting_Property_Raises_PropertyChanged_On_Replace()
    {
        var grid = new DataGrid();
        var newModel = new SortingModel();
        var propertyNames = new List<string>();

        grid.PropertyChanged += (_, e) =>
        {
            if (e.Property == DataGrid.SortingModelProperty)
            {
                propertyNames.Add(e.Property.Name);
                Assert.Same(newModel, e.NewValue);
            }
        };

        grid.SortingModel = newModel;

        Assert.Equal(new[] { nameof(DataGrid.SortingModel) }, propertyNames);
    }

    [AvaloniaFact]
    public void Selection_Preserved_When_Custom_Adapter_Reorders_Items()
    {
        var items = new ObservableCollection<Item>
        {
            new("B"),
            new("A"),
            new("C")
        };

        var sortingModel = new SortingModel { OwnsViewSorts = true };
        var factory = new ReorderingAdapterFactory(items);

        var grid = CreateGrid(items, sortingModel, factory);
        grid.UpdateLayout();

        var selected = items[0]; // "B"
        grid.SelectedItem = selected;
        grid.UpdateLayout();

        Assert.Equal(0, grid.SelectedIndex);

        sortingModel.Apply(new[]
        {
            new SortingDescriptor("Name", ListSortDirection.Ascending, nameof(Item.Name))
        });

        grid.UpdateLayout();

        Assert.Equal(new[] { "A", "B", "C" }, items.Select(x => x.Name));
        Assert.Same(selected, grid.SelectedItem);
        Assert.Contains(selected, grid.SelectedItems.Cast<object>());
        Assert.Equal(1, grid.SelectedIndex);

        // The selection model indexes into DataGridSelectionSource, which deliberately does not
        // follow moves (see Move_Does_Not_Report_Selected_Item_As_Deselected), so its index still
        // refers to the pre-reorder position. What must hold is that it still means the same item.
        Assert.Same(selected, grid.Selection.SelectedItem);
    }

    [AvaloniaFact]
    public void Changing_SortingAdapterFactory_Recreates_Adapter()
    {
        var items = new ObservableCollection<Item>
        {
            new("B"),
            new("A")
        };

        var grid = new DataGrid
        {
            CanUserSortColumns = true,
            AutoGenerateColumns = false,
            ItemsSource = new DataGridCollectionView(items)
        };

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(Item.Name)),
            SortMemberPath = nameof(Item.Name)
        });

        var factory = new CountingSortingAdapterFactory();

        grid.SortingAdapterFactory = factory;

        Assert.Equal(1, factory.CreateCount);

        var field = typeof(DataGrid).GetField("_sortingAdapter", BindingFlags.Instance | BindingFlags.NonPublic);
        var adapter = field!.GetValue(grid);
        Assert.IsType<CountingSortingAdapterFactory.CountingSortingAdapter>(adapter);
    }

    private static DataGrid CreateGrid(IEnumerable<Item> items, ISortingModel sortingModel, IDataGridSortingAdapterFactory? adapterFactory = null)
    {
        var root = new Window
        {
            Width = 250,
            Height = 150,
        };

        root.SetThemeStyles();

        var view = new DataGridCollectionView(items);

        var grid = new DataGrid
        {
            CanUserSortColumns = true,
            AutoGenerateColumns = false
        };

        grid.SortingAdapterFactory = adapterFactory;
        grid.SortingModel = sortingModel;
        grid.ItemsSource = view;

        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(Item.Name)),
            SortMemberPath = nameof(Item.Name)
        });

        root.Content = grid;
        root.Show();
        return grid;
    }

    public record Item(string Name);

    private sealed class ReorderingAdapterFactory : IDataGridSortingAdapterFactory
    {
        private readonly ObservableCollection<Item> _items;

        public ReorderingAdapterFactory(ObservableCollection<Item> items)
        {
            _items = items;
        }

        public DataGridSortingAdapter Create(DataGrid grid, ISortingModel model)
        {
            return new ReorderingAdapter(model, () => grid.ColumnDefinitions, _items);
        }

        private sealed class ReorderingAdapter : DataGridSortingAdapter
        {
            private readonly ObservableCollection<Item> _items;

            public ReorderingAdapter(
                ISortingModel model,
                Func<IEnumerable<DataGridColumn>> columns,
                ObservableCollection<Item> items)
                : base(model, columns)
            {
                _items = items;
            }

            protected override bool TryApplyModelToView(
                IReadOnlyList<SortingDescriptor> descriptors,
                IReadOnlyList<SortingDescriptor> previousDescriptors,
                out bool changed)
            {
                ApplySort(descriptors);
                changed = true;
                return true;
            }

            private void ApplySort(IReadOnlyList<SortingDescriptor> descriptors)
            {
                var descriptor = descriptors?.FirstOrDefault();
                if (descriptor == null)
                {
                    return;
                }

                var ordered = descriptor.Direction == ListSortDirection.Ascending
                    ? _items.OrderBy(x => x.Name).ToList()
                    : _items.OrderByDescending(x => x.Name).ToList();

                for (int target = 0; target < ordered.Count; target++)
                {
                    var item = ordered[target];
                    var current = _items.IndexOf(item);
                    if (current >= 0 && current != target)
                    {
                        _items.Move(current, target);
                    }
                }
            }
        }
    }

    private sealed class CountingSortingAdapterFactory : IDataGridSortingAdapterFactory
    {
        public int CreateCount { get; private set; }

        public DataGridSortingAdapter Create(DataGrid grid, ISortingModel model)
        {
            CreateCount++;
            return new CountingSortingAdapter(model, () => grid.ColumnDefinitions);
        }

        internal sealed class CountingSortingAdapter : DataGridSortingAdapter
        {
            public CountingSortingAdapter(ISortingModel model, Func<IEnumerable<DataGridColumn>> columns)
                : base(model, columns)
            {
            }
        }
    }
}
