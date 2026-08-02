// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// The view a grid hands its selection model while it has no rows to offer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A grid acquires its rows and its selection model independently, and nothing orders those two
    /// events - in XAML they are just properties, written in whatever order the markup happens to
    /// list them. Leaving the model view-less in between made that order observable: the same two
    /// assignments behaved differently depending on which came first, which is not a distinction the
    /// markup was making.
    /// </para>
    /// <para>
    /// So a grid always supplies a view, and one that is merely empty says exactly the right thing -
    /// there are no rows yet, so no position names one. That is a different statement from having no
    /// view at all, which is what a model nobody has attached to a grid has, and is the only case
    /// left that reports an index as an error.
    /// </para>
    /// </remarks>
    internal sealed class EmptyDataGridSelectionView : IDataGridSelectionView, IDataGridSelectionSourceMembership
    {
        public static readonly EmptyDataGridSelectionView Instance = new();

        private EmptyDataGridSelectionView()
        {
        }

        public int Count => 0;

        public IEnumerable<object?> Items => Array.Empty<object?>();

        public bool TryGetItemAt(int index, out object? item)
        {
            item = null;
            return false;
        }

        public int IndexOf(object? item) => -1;

        /// <remarks>
        /// Keeps everything. Having no rows to show is not evidence that the data behind them is
        /// gone - the grid may simply not have been given any yet - and a selection seeded before
        /// the rows arrive would not survive being told otherwise.
        /// </remarks>
        public Func<object?, bool> SnapshotSourceMembership() => static _ => true;
    }
}
