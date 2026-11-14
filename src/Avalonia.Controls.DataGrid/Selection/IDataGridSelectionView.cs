// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// Read-only, ordered access to the items a selection index refers to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately a <em>pull-based</em> accessor rather than a materialised projection.
    /// Selection is stored by item, never by index, so nothing here has to be kept in step with the
    /// underlying view: every index the selection model reports is computed from this accessor at the
    /// moment it is asked for. A reorder of the view therefore needs no notification, no remapping and
    /// no bookkeeping - the next read simply produces the new answer.
    /// </para>
    /// <para>
    /// Implementations may cache to keep <see cref="IndexOf"/> cheap, but that cache is an optimisation
    /// only: it must never be able to disagree with the live view about ordering.
    /// </para>
    /// </remarks>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    interface IDataGridSelectionView
    {
        /// <summary>Number of items currently addressable by index.</summary>
        int Count { get; }

        /// <summary>
        /// Gets the item at <paramref name="index"/>, or <c>false</c> if the index is out of range.
        /// </summary>
        bool TryGetItemAt(int index, out object? item);

        /// <summary>
        /// Gets the current index of <paramref name="item"/>, or -1 when it is not in the view.
        /// </summary>
        int IndexOf(object? item);

        /// <summary>Enumerates the items in view order.</summary>
        IEnumerable<object?> Items { get; }
    }
}
