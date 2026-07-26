// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// <see cref="IList"/> façade over a <see cref="DataGridSelectionModel"/>, for binding to
    /// <c>DataGrid.SelectedItems</c>.
    /// </summary>
    /// <remarks>
    /// Holds no items of its own - every read goes to the model and every write is a model operation.
    /// It is a projection, not a second copy, so it cannot disagree with the selection it presents.
    /// </remarks>
    internal sealed class DataGridSelectedItemsView : IList, INotifyCollectionChanged, INotifyPropertyChanged, IDisposable
    {
        private readonly DataGridSelectionModel _model;
        private bool _disposed;

        public DataGridSelectedItemsView(DataGridSelectionModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _model.SelectionChanged += OnModelSelectionChanged;
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Count => _model.Count;

        public bool IsFixedSize => false;

        public bool IsReadOnly => false;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public object? this[int index]
        {
            get
            {
                var items = _model.SelectedItems;
                if (index < 0 || index >= items.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return items[index];
            }
            set => throw new NotSupportedException();
        }

        public int Add(object? value)
        {
            _model.Select(value);
            return IndexOf(value);
        }

        public void Clear() => _model.Clear();

        public bool Contains(object? value) => _model.IsSelected(value);

        public int IndexOf(object? value)
        {
            var items = _model.SelectedItems;
            var comparer = _model.Comparer;
            for (int i = 0; i < items.Count; i++)
            {
                if (comparer.Equals(items[i], value))
                {
                    return i;
                }
            }

            return -1;
        }

        public void Insert(int index, object? value) => throw new NotSupportedException();

        public void Remove(object? value) => _model.Deselect(value);

        public void RemoveAt(int index)
        {
            var items = _model.SelectedItems;
            if (index < 0 || index >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            _model.Deselect(items[index]);
        }

        public void CopyTo(Array array, int index)
        {
            if (array is null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            var i = index;
            foreach (var item in _model.SelectedItems)
            {
                array.SetValue(item, i++);
            }
        }

        public IEnumerator GetEnumerator() => _model.SelectedItems.GetEnumerator();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _model.SelectionChanged -= OnModelSelectionChanged;
            _disposed = true;
        }

        private void OnModelSelectionChanged(object? sender, DataGridSelectionModelChangedEventArgs e)
        {
            if (e.DeselectedItems.Count > 0)
            {
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove, ToList(e.DeselectedItems), -1));
            }

            if (e.SelectedItems.Count > 0)
            {
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add, ToList(e.SelectedItems), -1));
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        private static IList ToList(IReadOnlyList<object?> items)
        {
            var list = new List<object?>(items.Count);
            list.AddRange(items);
            return list;
        }
    }
}
