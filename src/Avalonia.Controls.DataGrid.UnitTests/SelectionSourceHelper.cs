using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridSelection;
using Xunit;

namespace Avalonia.Controls.DataGridTests;

/// <summary>
/// The selection model no longer holds a source collection to compare against - it stores items and
/// derives indexes from the grid's view on demand. What used to be checked by comparing
/// <c>Selection.Source</c> to the view is now checked by asserting that the indexes the model reports
/// are the positions those items actually occupy.
/// </summary>
internal static class SelectionSource
{
    public static void AssertTracksView(DataGrid grid, DataGridSelectionModel selection)
    {
        var view = grid.CollectionView.Cast<object>().ToList();

        foreach (var item in selection.SelectedItems)
        {
            Assert.Equal(view.IndexOf(item), IndexOfInModel(selection, item));
        }

        Assert.Equal(
            selection.SelectedItems.Where(view.Contains).Select(item => view.IndexOf(item)).OrderBy(i => i),
            selection.SelectedIndexes);
    }

    private static int IndexOfInModel(DataGridSelectionModel selection, object item)
        => selection.View?.IndexOf(item) ?? -1;
}
