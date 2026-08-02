// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// Answers whether an item is still in the data a selection view sits on, as opposed to whether
    /// the view is currently showing it.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="IDataGridSelectionView"/>, which is about positions in what is on
    /// screen. The two questions come apart wherever the view shows less than it holds - a filter, a
    /// page, a collapsed branch - and a reset, which says nothing about what it removed, has to ask
    /// the second one. Answering it with <see cref="IDataGridSelectionView.IndexOf"/> deselects every
    /// row that was merely hidden.
    /// </remarks>
    internal interface IDataGridSelectionSourceMembership
    {
        /// <summary>
        /// Snapshots which items the underlying data holds, as a predicate applied to every selected
        /// item at once - answering one item at a time would rescan the source per item.
        /// </summary>
        Func<object?, bool> SnapshotSourceMembership();
    }
}
