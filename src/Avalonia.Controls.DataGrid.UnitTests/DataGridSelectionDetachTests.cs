// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.Selection;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Avalonia.Controls.DataGridTests;

public class DataGridSelectionDetachTests
{
    [AvaloniaFact]
    public void Switching_tabs_does_not_throw_when_selection_model_items_are_bound()
    {
        var items = new ObservableCollection<SelectionItem>(
            Enumerable.Range(1, 100).Select(i => new SelectionItem { Name = $"Item {i:000}" }));

        var selectionModel = new DataGridSelectionModel<object?>();

        var grid = new DataGrid
        {
            ItemsSource = items,
            Selection = selectionModel,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false,
            Height = 200
        };

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(SelectionItem.Name))
        });

        var selectionView = new ItemsControl
        {
            ItemsSource = selectionModel.SelectedItems
        };

        var tabContent = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                grid,
                selectionView
            }
        };

        var tabControl = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Selection", Content = tabContent },
                new TabItem { Header = "Other", Content = new TextBlock { Text = "Other tab" } }
            }
        };

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = tabControl
        };
        window.SetThemeStyles(DataGridTheme.SimpleV2);

        Exception? exception = null;
        try
        {
            exception = Record.Exception(() =>
            {
                window.Show();
                tabControl.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                grid.UpdateLayout();

                SelectionSource.AssertTracksView(grid, selectionModel);

                selectionModel.SelectAt(0);
                selectionModel.SelectAt(1);
                selectionModel.SelectAt(2);

                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                grid.UpdateLayout();

                tabControl.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            });
        }
        finally
        {
            window.Close();
        }

        Assert.Null(exception);
    }

    [AvaloniaFact]
    public void Switching_tabs_and_immediately_reattaching_does_not_clear_selection_source()
    {
        var items = new ObservableCollection<SelectionItem>(
            Enumerable.Range(1, 100).Select(i => new SelectionItem { Name = $"Item {i:000}" }));

        var selectionModel = new DataGridSelectionModel<object?>();

        var grid = new DataGrid
        {
            ItemsSource = items,
            Selection = selectionModel,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false,
            Height = 200
        };

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(SelectionItem.Name))
        });

        var selectionView = new ItemsControl
        {
            ItemsSource = selectionModel.SelectedItems
        };

        var tabContent = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                grid,
                selectionView
            }
        };

        var tabControl = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Selection", Content = tabContent },
                new TabItem { Header = "Other", Content = new TextBlock { Text = "Other tab" } }
            }
        };

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = tabControl
        };
        window.SetThemeStyles(DataGridTheme.SimpleV2);

        Exception? exception = null;
        try
        {
            exception = Record.Exception(() =>
            {
                window.Show();
                tabControl.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                grid.UpdateLayout();

                SelectionSource.AssertTracksView(grid, selectionModel);

                selectionModel.SelectAt(0);
                selectionModel.SelectAt(1);

                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                grid.UpdateLayout();

                tabControl.SelectedIndex = 1;
                tabControl.SelectedIndex = 0;

                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                grid.UpdateLayout();
            });
        }
        finally
        {
            window.Close();
        }

        Assert.Null(exception);
        SelectionSource.AssertTracksView(grid, selectionModel);
    }

    private sealed class SelectionItem
    {
        public string Name { get; set; } = string.Empty;
    }
}
