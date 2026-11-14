// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.Selection;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Selection;

public class DataGridSelectionOriginTests
{
    [AvaloniaFact]
    public void Programmatic_SelectedItem_Sets_Programmatic_Source()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var grid = CreateGrid(items);
        grid.UpdateLayout();

        DataGridSelectionChangedEventArgs? args = null;
        grid.SelectionChanged += (_, e) => args = e as DataGridSelectionChangedEventArgs;

        grid.SelectedItem = items[1];
        grid.UpdateLayout();

        Assert.NotNull(args);
        AssertFlags(args!, DataGridSelectionChangeSource.Programmatic, isUserInitiated: false);
        Assert.Null(args!.TriggerEvent);
    }

    [AvaloniaFact]
    public void SelectAll_Sets_Command_Source()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var grid = CreateGrid(items);
        grid.UpdateLayout();

        DataGridSelectionChangedEventArgs? args = null;
        grid.SelectionChanged += (_, e) => args = e as DataGridSelectionChangedEventArgs;

        grid.SelectAll();
        grid.UpdateLayout();

        Assert.NotNull(args);
        AssertFlags(args!, DataGridSelectionChangeSource.Command, isUserInitiated: true);
    }

    [AvaloniaFact]
    public void ItemsSource_Change_Sets_ItemsSourceChange_Source()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var items2 = new ObservableCollection<string> { "X", "Y" };
        var view1 = new DataGridCollectionView(items);
        var view2 = new DataGridCollectionView(items2);
        var grid = CreateGrid(view1);
        grid.UpdateLayout();

        grid.SelectedItem = items[1];
        grid.UpdateLayout();

        DataGridSelectionChangedEventArgs? args = null;
        grid.SelectionChanged += (_, e) => args = e as DataGridSelectionChangedEventArgs;

        grid.ItemsSource = view2;
        grid.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        grid.UpdateLayout();

        if (args != null)
        {
            AssertFlags(args!, DataGridSelectionChangeSource.ItemsSourceChange, isUserInitiated: false);
        }
        else
        {
            Assert.True(grid.CurrentSelectionChangeSource.HasFlag(DataGridSelectionChangeSource.ItemsSourceChange));
        }
    }

    [AvaloniaFact]
    public void SelectionModel_Select_Sets_SelectionModelSync_Source()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var selection = new DataGridSelectionModel<string> { SingleSelect = false };

        var grid = CreateGrid(items, selection);
        grid.UpdateLayout();

        DataGridSelectionChangedEventArgs? args = null;
        grid.SelectionChanged += (_, e) => args = e as DataGridSelectionChangedEventArgs;

        selection.SelectAt(1);
        grid.UpdateLayout();

        Assert.NotNull(args);
        AssertFlags(args!, DataGridSelectionChangeSource.SelectionModelSync, isUserInitiated: false);
    }

    [AvaloniaFact]
    public void Keyboard_Move_Sets_Keyboard_Source_And_Trigger()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var grid = CreateGrid(items);
        grid.UpdateLayout();

        grid.SelectedIndex = 0;
        grid.UpdateLayout();

        DataGridSelectionChangedEventArgs? args = null;
        grid.SelectionChanged += (_, e) => args = e as DataGridSelectionChangedEventArgs;

        var keyArgs = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Route = InputElement.KeyDownEvent.RoutingStrategies,
            Source = grid,
            Key = Key.Down,
            PhysicalKey = PhysicalKey.ArrowDown,
            KeyModifiers = KeyModifiers.None,
            KeyDeviceType = KeyDeviceType.Keyboard
        };

        InvokeDataGridKeyDown(grid, keyArgs);
        grid.UpdateLayout();

        Assert.NotNull(args);
        AssertFlags(args!, DataGridSelectionChangeSource.Keyboard, isUserInitiated: true);
        Assert.Same(keyArgs, args!.TriggerEvent);
    }

    [AvaloniaFact]
    public void Pointer_Select_Sets_Pointer_Source_And_Trigger()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var grid = CreateGrid(items);
        grid.UpdateLayout();

        DataGridSelectionChangedEventArgs? args = null;
        grid.SelectionChanged += (_, e) => args = e as DataGridSelectionChangedEventArgs;

        var pointerArgs = CreatePointerPressedArgs(grid);
        InvokePrivateUpdateStateOnMouseLeftButtonDown(grid, pointerArgs, columnIndex: 0, slot: 1, allowEdit: false);
        grid.UpdateLayout();

        Assert.NotNull(args);
        AssertFlags(args!, DataGridSelectionChangeSource.Pointer, isUserInitiated: true);
        Assert.Same(pointerArgs, args!.TriggerEvent);
    }

    private static DataGrid CreateGrid(IEnumerable items)
    {
        var root = new Window
        {
            Width = 400,
            Height = 240,
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            ItemsSource = items,
            SelectionMode = DataGridSelectionMode.Extended
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

    private static void InvokeDataGridKeyDown(DataGrid grid, KeyEventArgs args)
    {
        var method = typeof(DataGrid).GetMethod(
            "DataGrid_KeyDown",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method?.Invoke(grid, new object[] { grid, args });
    }

    private static DataGrid CreateGrid(IEnumerable items, DataGridSelectionModel<string> selection)
    {
        var root = new Window
        {
            Width = 400,
            Height = 240,
        };

        root.SetThemeStyles();

        var grid = new DataGrid
        {
            ItemsSource = items,
            Selection = selection,
            SelectionMode = DataGridSelectionMode.Extended
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

    private static void AssertFlags(DataGridSelectionChangedEventArgs args, DataGridSelectionChangeSource expected, bool isUserInitiated)
    {
        Assert.IsType<DataGridSelectionChangedEventArgs>(args);
        Assert.True(args.Source.HasFlag(expected), $"Expected {expected}, but got {args.Source}");
        Assert.Equal(isUserInitiated, args.IsUserInitiated);
    }

    private static PointerPressedEventArgs CreatePointerPressedArgs(DataGrid grid)
    {
        var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);

        return new PointerPressedEventArgs(
            grid,
            pointer,
            grid,
            new Point(0, 0),
            0,
            properties,
            KeyModifiers.None);
    }

    private static void InvokePrivateUpdateStateOnMouseLeftButtonDown(DataGrid grid, PointerPressedEventArgs args, int columnIndex, int slot, bool allowEdit)
    {
        var method = typeof(DataGrid).GetMethod("UpdateStateOnMouseLeftButtonDown", BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
            types: new[] { typeof(PointerPressedEventArgs), typeof(int), typeof(int), typeof(bool) }, modifiers: null);

        Assert.NotNull(method);
        method!.Invoke(grid, new object[] { args, columnIndex, slot, allowEdit });
    }
}
