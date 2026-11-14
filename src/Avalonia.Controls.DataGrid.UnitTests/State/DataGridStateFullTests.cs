// Copyright (c) Wieslaw Soltes. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Avalonia.Controls.DataGridTests.State;

public class DataGridStateFullTests
{
    [AvaloniaFact]
    public void CaptureAndRestoreState_RestoresLayoutViewSelectionAndScroll()
    {
        var items = StateTestHelper.CreateItems(40);
        var (grid, root) = StateTestHelper.CreateGrid(items, width: 360, height: 140);

        try
        {
            var idColumn = grid.ColumnsInternal[0];
            var nameColumn = grid.ColumnsInternal[1];
            var categoryColumn = grid.ColumnsInternal[2];

            nameColumn.DisplayIndex = 0;
            idColumn.DisplayIndex = 1;
            categoryColumn.IsVisible = false;
            nameColumn.Width = new DataGridLength(180);
            grid.FrozenColumnCount = 1;

            grid.SortingModel.Apply(new[]
            {
                new SortingDescriptor(nameColumn, ListSortDirection.Ascending, nameof(StateTestItem.Name)),
            });

            grid.FilteringModel.Apply(new[]
            {
                new FilteringDescriptor(
                    categoryColumn,
                    FilteringOperator.Equals,
                    nameof(StateTestItem.Category),
                    "A"),
            });

            grid.SearchModel.Apply(new[]
            {
                new SearchDescriptor("Item 1", SearchMatchMode.Contains, SearchTermCombineMode.Any, SearchScope.AllColumns),
            });

            Dispatcher.UIThread.RunJobs();
            grid.SearchModel.MoveTo(0);

            grid.Selection.SelectAt(2);
            grid.Selection.SelectAt(3);

            grid.ScrollIntoView(items[20], nameColumn);
            grid.UpdateLayout();
            grid.UpdateHorizontalOffset(80);

            var options = StateTestHelper.CreateKeyedOptions(grid, items);
            var state = grid.CaptureState(DataGridStateSections.All, options);

            Assert.NotNull(state.Scroll);

            grid.SortingModel.Clear();
            grid.FilteringModel.Clear();
            grid.SearchModel.Clear();
            grid.SelectedItems.Clear();
            grid.SelectedCells.Clear();
            idColumn.DisplayIndex = 0;
            nameColumn.DisplayIndex = 1;
            categoryColumn.IsVisible = true;
            grid.FrozenColumnCount = 0;
            grid.ScrollIntoView(items[0], nameColumn);
            grid.UpdateHorizontalOffset(0);
            grid.UpdateLayout();

            grid.RestoreState(state, DataGridStateSections.All, options);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, grid.FrozenColumnCount);
            Assert.False(categoryColumn.IsVisible);
            Assert.Equal(0, nameColumn.DisplayIndex);

            Assert.Single(grid.SortingModel.Descriptors);
            Assert.Single(grid.FilteringModel.Descriptors);
            Assert.Single(grid.SearchModel.Descriptors);

            var selectedIds = grid.SelectedItems.Cast<StateTestItem>()
                .Select(item => item.Id)
                .OrderBy(id => id)
                .ToArray();

            Assert.Equal(new[] { 12, 14 }, selectedIds);
            Assert.Equal(state.Scroll.HorizontalOffset, grid.HorizontalOffset);
            Assert.Equal(state.Scroll.FirstScrollingSlot, grid.DisplayData.FirstScrollingSlot);
        }
        finally
        {
            root.Close();
        }
    }
}
