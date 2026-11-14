// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.Selection;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

public sealed class DataGridSelectionChangingGuaranteeTests
{
    [AvaloniaFact]
    public void DataGrid_Controlled_Selection_Is_Atomic_Preflight()
    {
        ObservableCollection<string> items = new() { "A", "B" };
        (Window window, DataGrid grid) = CreateGrid(items);
        try
        {
            grid.SelectedItem = items[0];
            DataGridSelectionChangingGuarantee? guarantee = null;
            grid.SelectionChanging += (_, e) => guarantee = e.Guarantee;

            grid.SelectedItem = items[1];

            Assert.Equal(DataGridSelectionChangingGuarantee.AtomicPreflight, guarantee);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Caller_Owned_SelectedItems_Change_Is_Post_Change_Reconciliation()
    {
        ObservableCollection<string> items = new() { "A", "B" };
        (Window window, DataGrid grid) = CreateGrid(items);
        try
        {
            ObservableCollection<object> selectedItems = new() { items[0] };
            grid.SelectedItems = selectedItems;
            DataGridSelectionChangingGuarantee? guarantee = null;
            grid.SelectionChanging += (_, e) => guarantee = e.Guarantee;

            selectedItems.Add(items[1]);

            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, guarantee);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Caller_Owned_SelectionModel_Change_Is_Post_Change_Reconciliation()
    {
        ObservableCollection<string> items = new() { "A", "B" };
        DataGridSelectionModel<string> selection = new() { SingleSelect = false };
        (Window window, DataGrid grid) = CreateGrid(items, selection);
        try
        {
            selection.SelectAt(0);
            DataGridSelectionChangingGuarantee? guarantee = null;
            grid.SelectionChanging += (_, e) => guarantee = e.Guarantee;

            selection.SelectAt(1);

            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, guarantee);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void BuiltIn_View_Source_Boundary_Is_Post_Change_Reconciliation()
    {
        ObservableCollection<string> items = new() { "A", "B", "C" };
        (Window window, DataGrid grid) = CreateGrid(items);
        try
        {
            grid.SelectedItem = items[1];
            DataGridSelectionChangingGuarantee? guarantee = null;
            grid.SelectionChanging += (_, e) => guarantee = e.Guarantee;

            items.RemoveAt(0);

            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, guarantee);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ItemsSource_Replacement_Is_Post_Change_Reconciliation()
    {
        ObservableCollection<string> items = new() { "A", "B" };
        (Window window, DataGrid grid) = CreateGrid(items);
        try
        {
            grid.SelectedItem = items[1];
            DataGridSelectionChangingGuarantee? guarantee = null;
            grid.SelectionChanging += (_, e) => guarantee = e.Guarantee;

            grid.ItemsSource = new ObservableCollection<string> { items[1], "C" };

            Assert.Equal(DataGridSelectionChangingGuarantee.PostChangeReconciliation, guarantee);
        }
        finally
        {
            window.Close();
        }
    }

    private static (Window Window, DataGrid Grid) CreateGrid(
        ObservableCollection<string> items,
        DataGridSelectionModel<string>? selection = null)
    {
        Window window = new()
        {
            Width = 400,
            Height = 240,
        };
        window.SetThemeStyles();

        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            ItemsSource = items,
            Selection = selection ?? new DataGridSelectionModel<string> { SingleSelect = false },
        };
        grid.ColumnsInternal.Add(new DataGridTextColumn
        {
            Header = "Value",
            Binding = new Binding("."),
        });
        window.Content = grid;
        window.Show();
        grid.UpdateLayout();
        return (window, grid);
    }
}
