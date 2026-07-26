// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// Describes items that entered or left the selection.
    /// </summary>
    /// <remarks>
    /// Reports items, not indexes. An index would be meaningless here: it is derived from the view and
    /// may already read differently by the time a handler runs.
    /// </remarks>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    class DataGridSelectionModelChangedEventArgs : EventArgs
    {
        internal DataGridSelectionModelChangedEventArgs(
            IReadOnlyList<object?> selectedItems,
            IReadOnlyList<object?> deselectedItems)
        {
            SelectedItems = selectedItems;
            DeselectedItems = deselectedItems;
        }

        /// <summary>Items added to the selection.</summary>
        public IReadOnlyList<object?> SelectedItems { get; }

        /// <summary>Items removed from the selection.</summary>
        public IReadOnlyList<object?> DeselectedItems { get; }
    }

    /// <summary>
    /// Strongly typed <see cref="DataGridSelectionModelChangedEventArgs"/>.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    sealed class DataGridSelectionModelChangedEventArgs<T> : DataGridSelectionModelChangedEventArgs
    {
        internal DataGridSelectionModelChangedEventArgs(
            IReadOnlyList<object?> selectedItems,
            IReadOnlyList<object?> deselectedItems)
            : base(selectedItems, deselectedItems)
        {
            SelectedItems = Cast(selectedItems);
            DeselectedItems = Cast(deselectedItems);
        }

        /// <summary>Items added to the selection.</summary>
        public new IReadOnlyList<T> SelectedItems { get; }

        /// <summary>Items removed from the selection.</summary>
        public new IReadOnlyList<T> DeselectedItems { get; }

        private static IReadOnlyList<T> Cast(IReadOnlyList<object?> items)
        {
            if (items.Count == 0)
            {
                return Array.Empty<T>();
            }

            return items.OfType<T>().ToArray();
        }
    }
}
