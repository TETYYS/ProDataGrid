// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia.Collections;

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// <see cref="IDataGridSelectionView"/> over a <see cref="DataGridCollectionView"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads straight through to the live view. It holds no copy of the item order, so unlike the
    /// projection it replaces it has no way to drift out of step with what the grid displays - a move or
    /// a re-sort needs no handling here at all.
    /// </para>
    /// <para>
    /// The one piece of state is an index lookup, kept so that resolving an item's position stays O(1)
    /// on large views. The collection-changed subscription exists solely to discard it; it carries no
    /// logic and the cache is rebuilt from the view on next use, so a missed or out-of-order
    /// notification can only cost time, never correctness.
    /// </para>
    /// </remarks>
    internal sealed class DataGridCollectionViewSelectionView : IDataGridSelectionView, IDataGridSelectionSourceMembership, IDisposable
    {
        private readonly DataGridCollectionView _view;
        private readonly IEqualityComparer<object> _comparer;
        private readonly Action? _onOrderChanged;
        private Dictionary<object, int>? _indexes;
        private bool _disposed;

        public DataGridCollectionViewSelectionView(
            DataGridCollectionView view,
            IEqualityComparer<object> comparer,
            Action? onOrderChanged = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _comparer = comparer ?? EqualityComparer<object>.Default;
            _onOrderChanged = onOrderChanged;

            if (_view is INotifyCollectionChanged incc)
            {
                incc.CollectionChanged += OnViewCollectionChanged;
            }
        }

        public DataGridCollectionView View => _view;

        /// <remarks>
        /// The unpaged count. Selection indexes are positions in the whole filtered and sorted
        /// sequence, not in whichever page happens to be on screen - the grid's own SelectedIndex has
        /// always meant that, and a selection that only pages could address would come and go as the
        /// user turned them.
        /// </remarks>
        public int Count => _view.ItemCount;

        public IEnumerable<object?> Items
        {
            get
            {
                var count = _view.ItemCount;
                for (var i = 0; i < count; i++)
                {
                    yield return _view.GetGlobalItemAt(i);
                }
            }
        }

        public bool TryGetItemAt(int index, out object? item)
        {
            if (index < 0 || index >= _view.ItemCount)
            {
                item = null;
                return false;
            }

            item = _view.GetGlobalItemAt(index);
            return true;
        }

        public int IndexOf(object? item)
        {
            if (item is null)
            {
                return -1;
            }

            var indexes = _indexes ??= BuildIndexes();
            return indexes.TryGetValue(item, out var index) ? index : -1;
        }

        /// <summary>
        /// Membership test for the underlying data, ignoring the current filter and page. Used to decide
        /// what a reset actually removed, so that filtering a selected row out of sight hides it rather
        /// than discarding the selection.
        /// </summary>
        /// <remarks>
        /// Returns a snapshot rather than answering one item at a time: the caller tests every selected
        /// item at once, and scanning the source per item would be O(selected × source) on exactly the
        /// path a filtered grid takes most often.
        /// </remarks>
        public Func<object?, bool> SnapshotSourceMembership()
        {
            var indexes = _indexes ??= BuildIndexes();

            if (_view.SourceCollection is not IEnumerable source)
            {
                // No separate source to consult - the view is all there is.
                return item => item is not null && indexes.ContainsKey(item);
            }

            var inSource = new HashSet<object>(_comparer);
            foreach (var candidate in source)
            {
                if (candidate is not null)
                {
                    inSource.Add(candidate);
                }
            }

            return item => item is not null && (inSource.Contains(item) || indexes.ContainsKey(item));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_view is INotifyCollectionChanged incc)
            {
                incc.CollectionChanged -= OnViewCollectionChanged;
            }

            _indexes = null;
            _disposed = true;
        }

        private Dictionary<object, int> BuildIndexes()
        {
            var count = _view.ItemCount;
            var map = new Dictionary<object, int>(count, _comparer);
            for (var i = 0; i < count; i++)
            {
                if (_view.GetGlobalItemAt(i) is { } item)
                {
                    // A collection holding the same item twice can only report one position for it.
                    // Selection is by item, so both occurrences are the same selection either way.
                    map[item] = i;
                }
            }

            return map;
        }

        private void OnViewCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _indexes = null;
            _onOrderChanged?.Invoke();
        }
    }
}
