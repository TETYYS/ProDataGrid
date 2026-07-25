using System.Collections;
using System.Linq;
using Avalonia.Controls.Selection;
using Xunit;

namespace Avalonia.Controls.DataGridTests;

/// <summary>
/// The grid does not hand its CollectionView to the selection model directly; it retargets the model
/// onto an internal projection of that view (DataGridSelectionSource) so collection moves survive as
/// moves instead of being reported to the model as remove/add deselections. Tests therefore assert
/// that the source tracks the view rather than that it is the view.
/// </summary>
internal static class SelectionSource
{
    /// <summary>Asserts the model's source projects <paramref name="grid"/>'s view, and returns it.</summary>
    public static IEnumerable AssertTracksView(DataGrid grid, ISelectionModel selection)
    {
        Assert.NotNull(selection.Source);
        Assert.Equal(grid.CollectionView.Cast<object>(), selection.Source!.Cast<object>());
        return selection.Source;
    }
}
