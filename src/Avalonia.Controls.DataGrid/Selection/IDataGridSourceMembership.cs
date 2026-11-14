// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// Implemented by an items source that holds more than it enumerates - one that applies its own
    /// filter before the grid sees it - to say which items it still holds.
    /// </summary>
    /// <remarks>
    /// A reset keeps every selected item the source still holds, so that filtering a selected row out
    /// of sight hides it rather than deselecting it. A source that filters itself enumerates only what
    /// passes, which would read every hidden row as removed; this lets it answer for the rest.
    /// </remarks>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    interface IDataGridSourceMembership
    {
        /// <summary>
        /// Whether the source holds <paramref name="item"/>, whether or not it currently enumerates it.
        /// </summary>
        bool ContainsSourceItem(object item);
    }
}
