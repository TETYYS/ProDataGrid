// Copyright (c) Wieslaw Soltes. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.Selection;

namespace Avalonia.Controls
{
    /// <summary>Controls the initial currency of a generated collection view.</summary>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    enum DataGridGeneratedInitialCurrency
    {
        /// <summary>Retains the collection view's native initial currency.</summary>
        Unchanged,
        /// <summary>Moves currency before the first item.</summary>
        None,
        /// <summary>Moves currency to the first item on the initial page.</summary>
        First,
        /// <summary>Moves currency to the last item on the initial page.</summary>
        Last
    }

    /// <summary>Captures generated collection-view paging, currency, and selection state by stable key.</summary>
    /// <typeparam name="TKey">The stable item key type.</typeparam>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    sealed class DataGridGeneratedCollectionViewSnapshot<TKey>
    {
        internal DataGridGeneratedCollectionViewSnapshot(
            int pageIndex,
            bool hasCurrentKey,
            TKey currentKey,
            DataGridGeneratedSelectionSnapshot<TKey> selection)
        {
            PageIndex = pageIndex;
            HasCurrentKey = hasCurrentKey;
            CurrentKey = currentKey;
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        }

        /// <summary>Gets the zero-based page index.</summary>
        public int PageIndex { get; }

        /// <summary>Gets whether a stable current-item key was captured.</summary>
        public bool HasCurrentKey { get; }

        /// <summary>Gets the captured current-item key.</summary>
        public TKey CurrentKey { get; }

        /// <summary>Gets the detached keyed selection snapshot.</summary>
        public DataGridGeneratedSelectionSnapshot<TKey> Selection { get; }
    }

    /// <summary>
    /// Coordinates a typed <see cref="DataGridCollectionView"/> with stable-key currency and selection.
    /// </summary>
    /// <typeparam name="TItem">The row item type.</typeparam>
    /// <typeparam name="TKey">The stable item key type.</typeparam>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    sealed class DataGridGeneratedCollectionViewController<TItem, TKey> : INotifyPropertyChanged, IDisposable
    {
        private readonly IDataGridItemKey<TItem, TKey> _keyAccessor;
        private readonly IEqualityComparer<TKey> _keyComparer;
        private readonly bool _preserveCurrentItemByKey;
        private bool _hasCurrentKey;
        private TKey _currentKey;
        private bool _currentKeyDetached;
        private bool _pageChanging;
        private bool _synchronizingSelection;
        private bool _restoringCurrency;
        private bool _disposed;

        /// <summary>Initializes a generated collection-view controller.</summary>
        public DataGridGeneratedCollectionViewController(
            DataGridCollectionView view,
            IDataGridItemKey<TItem, TKey> keyAccessor,
            DataGridGeneratedSelectionProfile selectionProfile = null,
            bool preserveCurrentItemByKey = true,
            IEqualityComparer<TKey> keyComparer = null)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            _keyAccessor = keyAccessor ?? throw new ArgumentNullException(nameof(keyAccessor));
            _keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            _preserveCurrentItemByKey = preserveCurrentItemByKey;
            SelectionController = new DataGridGeneratedSelectionController<TItem, TKey>(
                _keyAccessor,
                selectionProfile,
                _keyComparer);
            ResetSelectionIndex();
            SelectionModel = SelectionController.CreateSelectionModel();
            SelectionModel.SelectionChanged += OnSelectionModelChanged;
            SelectionController.SelectionChanged += OnGeneratedSelectionChanged;
            AttachView(View);
            CaptureCurrentKey();
        }

        /// <summary>Raised when <see cref="View"/> is replaced.</summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Gets the active collection view.</summary>
        public DataGridCollectionView View { get; private set; }

        /// <summary>Gets the stable-key selection controller.</summary>
        public DataGridGeneratedSelectionController<TItem, TKey> SelectionController { get; }

        /// <summary>Gets the grid selection model, which identifies rows by their stable key.</summary>
        public DataGridSelectionModel<TItem> SelectionModel { get; }

        /// <summary>Gets whether a stable current-item key is retained.</summary>
        public bool HasCurrentKey => _hasCurrentKey;

        /// <summary>Gets the retained current-item key.</summary>
        public TKey CurrentKey => _currentKey;

        /// <summary>Captures detached paging, currency, and selection state.</summary>
        public DataGridGeneratedCollectionViewSnapshot<TKey> Capture()
        {
            ThrowIfDisposed();
            CaptureCurrentKey();
            CaptureSelection();
            return new DataGridGeneratedCollectionViewSnapshot<TKey>(
                Math.Max(0, View.PageIndex),
                _hasCurrentKey,
                _currentKey,
                SelectionController.Capture());
        }

        /// <summary>Restores detached state and optionally navigates to the current item's page.</summary>
        public void Restore(DataGridGeneratedCollectionViewSnapshot<TKey> snapshot, bool moveToCurrentItemPage = true)
        {
            ThrowIfDisposed();
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            SelectionController.Restore(snapshot.Selection);
            _hasCurrentKey = snapshot.HasCurrentKey;
            _currentKey = snapshot.CurrentKey;
            _currentKeyDetached = false;
            if (_hasCurrentKey && _preserveCurrentItemByKey)
            {
                if (TryMoveCurrentToKey(_currentKey, moveToCurrentItemPage))
                {
                    return;
                }
                _currentKeyDetached = true;
            }

            int pageCount = View.PageSize > 0
                ? Math.Max(1, (View.ItemCount + View.PageSize - 1) / View.PageSize)
                : 1;
            if (View.PageSize > 0 && snapshot.PageIndex >= 0 && snapshot.PageIndex < pageCount)
            {
                View.MoveToPage(snapshot.PageIndex);
            }
        }

        /// <summary>Refreshes the view while retaining paging, currency, and selection by stable key.</summary>
        public void Refresh()
        {
            ThrowIfDisposed();
            DataGridGeneratedCollectionViewSnapshot<TKey> snapshot = Capture();
            View.Refresh();
            ResetSelectionIndex();
            Restore(snapshot);
        }

        /// <summary>Changes page size while retaining currency and selection by stable key.</summary>
        public void SetPageSize(int pageSize)
        {
            ThrowIfDisposed();
            if (pageSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }

            DataGridGeneratedCollectionViewSnapshot<TKey> snapshot = Capture();
            View.PageSize = pageSize;
            ResetSelectionIndex();
            Restore(snapshot);
        }

        /// <summary>Replaces the active view while retaining currency and selection by stable key.</summary>
        public void ReplaceView(DataGridCollectionView view)
        {
            ThrowIfDisposed();
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }
            if (ReferenceEquals(View, view))
            {
                return;
            }

            DataGridGeneratedCollectionViewSnapshot<TKey> snapshot = Capture();
            DetachView(View);
            View = view;
            ResetSelectionIndex();
            AttachView(View);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(View)));
            Restore(snapshot);
        }

        /// <summary>Moves currency to a stable key, optionally navigating to its page.</summary>
        public bool TryMoveCurrentToKey(TKey key, bool moveToContainingPage = true)
        {
            ThrowIfDisposed();
            int globalIndex = FindGlobalIndex(key);
            if (globalIndex < 0)
            {
                return false;
            }

            if (moveToContainingPage && View.PageSize > 0)
            {
                int pageIndex = globalIndex / View.PageSize;
                if (pageIndex != View.PageIndex)
                {
                    View.MoveToPage(pageIndex);
                }
            }

            object item = View.GetGlobalItemAt(globalIndex);
            if (View.IndexOf(item) < 0)
            {
                return false;
            }

            _restoringCurrency = true;
            try
            {
                bool moved = View.MoveCurrentTo(item);
                if (moved)
                {
                    _hasCurrentKey = true;
                    _currentKey = key;
                    _currentKeyDetached = false;
                }
                return moved;
            }
            finally
            {
                _restoringCurrency = false;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            DetachView(View);
            SelectionModel.SelectionChanged -= OnSelectionModelChanged;
            SelectionController.SelectionChanged -= OnGeneratedSelectionChanged;
            _disposed = true;
        }

        private void AttachView(DataGridCollectionView view)
        {
            view.PageChanging += OnPageChanging;
            view.PageChanged += OnPageChanged;
            view.CurrentChanged += OnCurrentChanged;
        }

        private void DetachView(DataGridCollectionView view)
        {
            view.PageChanging -= OnPageChanging;
            view.PageChanged -= OnPageChanged;
            view.CurrentChanged -= OnCurrentChanged;
        }

        private void OnPageChanging(object sender, PageChangingEventArgs e)
        {
            if (!_currentKeyDetached)
            {
                CaptureCurrentKey();
            }
            _pageChanging = true;
        }

        private void OnPageChanged(object sender, EventArgs e)
        {
            if (_preserveCurrentItemByKey && _hasCurrentKey)
            {
                _currentKeyDetached = !TryMoveCurrentToKey(_currentKey, moveToContainingPage: false);
            }
        }

        private void OnCurrentChanged(object sender, EventArgs e)
        {
            if (_restoringCurrency)
            {
                return;
            }
            if (_pageChanging)
            {
                _pageChanging = false;
                if (!_preserveCurrentItemByKey)
                {
                    CaptureCurrentKey();
                }
                return;
            }
            CaptureCurrentKey();
        }

        private void CaptureCurrentKey()
        {
            if (View.CurrentItem is TItem item)
            {
                _currentKey = _keyAccessor.GetKey(item);
                _hasCurrentKey = true;
                _currentKeyDetached = false;
            }
            else if (!_preserveCurrentItemByKey)
            {
                _currentKey = default;
                _hasCurrentKey = false;
                _currentKeyDetached = false;
            }
        }

        private void OnSelectionModelChanged(object sender, DataGridSelectionModelChangedEventArgs<TItem> e)
        {
            if (_synchronizingSelection)
            {
                return;
            }
            CaptureSelection();
        }

        private void CaptureSelection()
        {
            _synchronizingSelection = true;
            try
            {
                SelectionController.CaptureFrom(SelectionModel);
            }
            finally
            {
                _synchronizingSelection = false;
            }
        }

        private void OnGeneratedSelectionChanged(object sender, DataGridGeneratedSelectionChangedEventArgs e)
        {
            if (_synchronizingSelection)
            {
                return;
            }

            _synchronizingSelection = true;
            try
            {
                SelectionController.ApplyTo(SelectionModel);
            }
            finally
            {
                _synchronizingSelection = false;
            }
        }

        private void ResetSelectionIndex() => SelectionController.ResetSource(new GlobalItems(View));

        private int FindGlobalIndex(TKey key)
        {
            for (int index = 0; index < View.ItemCount; index++)
            {
                if (View.GetGlobalItemAt(index) is TItem item &&
                    _keyComparer.Equals(_keyAccessor.GetKey(item), key))
                {
                    return index;
                }
            }
            return -1;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private sealed class GlobalItems : IReadOnlyList<TItem>
        {
            private readonly DataGridCollectionView _view;

            public GlobalItems(DataGridCollectionView view) => _view = view;

            public int Count => _view.ItemCount;

            public TItem this[int index] => (TItem)_view.GetGlobalItemAt(index);

            public IEnumerator<TItem> GetEnumerator()
            {
                for (int index = 0; index < Count; index++)
                {
                    yield return this[index];
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
