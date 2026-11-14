// Copyright (c) Wieslaw Soltes. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using Avalonia.Collections;
using Avalonia.Controls;
using Xunit;

namespace Avalonia.Controls.DataGridTests.ColumnDefinitions;

public sealed class DataGridGeneratedCollectionViewControllerTests
{
    [Fact]
    public void Page_navigation_restores_sticky_currency_when_key_returns()
    {
        Row[] rows = CreateRows();
        var view = new DataGridCollectionView(rows) { PageSize = 2 };
        view.MoveCurrentTo(rows[1]);
        using var controller = new DataGridGeneratedCollectionViewController<Row, int>(view, new RowKey());

        Assert.True(view.MoveToPage(1));
        Assert.Equal(2, controller.CurrentKey);
        Assert.True(view.MoveToPage(0));

        Assert.Same(rows[1], view.CurrentItem);
        Assert.Equal(2, controller.CurrentKey);
    }

    [Fact]
    public void Page_size_change_preserves_current_item_and_selection_by_key()
    {
        Row[] rows = CreateRows();
        var view = new DataGridCollectionView(rows) { PageSize = 2 };
        using var controller = new DataGridGeneratedCollectionViewController<Row, int>(view, new RowKey());
        Assert.True(controller.TryMoveCurrentToKey(5));
        controller.SelectionController.SelectKey(2);
        controller.SelectionController.SelectKey(5);

        controller.SetPageSize(3);

        Assert.Equal(1, view.PageIndex);
        Assert.Equal(5, ((Row)view.CurrentItem!).Id);
        Assert.Equal(new[] { 2, 5 }, controller.SelectionController.SelectedItemKeys);
    }

    [Fact]
    public void View_replacement_rehydrates_currency_and_selection_to_new_instances()
    {
        Row[] rows = CreateRows();
        var first = new DataGridCollectionView(rows) { PageSize = 2 };
        using var controller = new DataGridGeneratedCollectionViewController<Row, int>(first, new RowKey());
        Assert.True(controller.TryMoveCurrentToKey(4));
        controller.SelectionController.SelectKey(1);
        controller.SelectionController.SelectKey(4);
        Row[] replacements = CreateRows("replacement-");
        var second = new DataGridCollectionView(replacements) { PageSize = 2 };

        controller.ReplaceView(second);

        Assert.Same(second, controller.View);
        Assert.Same(replacements[3], second.CurrentItem);
        Assert.Equal(new[] { 1, 4 }, controller.SelectionController.SelectedItemKeys);
        Assert.Equal(
            new[] { replacements[0], replacements[3] },
            controller.SelectionController.GetSelectedItems());
    }

    [Fact]
    public void Explicit_currency_and_selection_model_changes_become_authoritative()
    {
        Row[] rows = CreateRows();
        var view = new DataGridCollectionView(rows) { PageSize = 2 };
        using var controller = new DataGridGeneratedCollectionViewController<Row, int>(view, new RowKey());
        Assert.True(view.MoveToPage(1));
        Assert.True(view.MoveCurrentTo(rows[3]));
        controller.SelectionModel.Select(rows[3]);

        Assert.Equal(4, controller.CurrentKey);
        Assert.Equal(new[] { 4 }, controller.SelectionController.SelectedItemKeys);
        DataGridGeneratedCollectionViewSnapshot<int> snapshot = controller.Capture();
        controller.SelectionController.Clear();
        Assert.True(view.MoveToFirstPage());

        controller.Restore(snapshot);

        Assert.Equal(1, view.PageIndex);
        Assert.Same(rows[3], view.CurrentItem);
        Assert.Equal(new[] { 4 }, controller.SelectionController.SelectedItemKeys);
        Assert.False(controller.TryMoveCurrentToKey(99));
    }

    [Fact]
    public void Disabled_currency_preservation_accepts_page_currency_and_dispose_stops_use()
    {
        Row[] rows = CreateRows();
        var view = new DataGridCollectionView(rows) { PageSize = 2 };
        var controller = new DataGridGeneratedCollectionViewController<Row, int>(
            view,
            new RowKey(),
            preserveCurrentItemByKey: false);

        Assert.True(view.MoveToPage(1));
        Assert.Equal(3, controller.CurrentKey);

        controller.Dispose();

        Assert.Throws<ObjectDisposedException>(() => controller.Refresh());
        Assert.Throws<ObjectDisposedException>(() => controller.SetPageSize(1));
    }

    private static Row[] CreateRows(string prefix = "row-") =>
    [
        new Row(1, prefix + "1"),
        new Row(2, prefix + "2"),
        new Row(3, prefix + "3"),
        new Row(4, prefix + "4"),
        new Row(5, prefix + "5"),
        new Row(6, prefix + "6")
    ];

    private sealed record Row(int Id, string Name);

    private sealed class RowKey : IDataGridItemKey<Row, int>
    {
        public int GetKey(Row item) => item.Id;
    }
}
