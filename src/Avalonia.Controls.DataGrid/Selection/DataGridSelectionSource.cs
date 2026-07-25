#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Collections;

namespace Avalonia.Controls.DataGridSelection
{
    internal sealed class DataGridSelectionSource : IReadOnlyList<object>, IList, INotifyCollectionChanged, IDataGridIndexOf, IDisposable
    {
        private readonly DataGridCollectionView _view;
        private readonly List<object> _items;
        private readonly Dictionary<object, int> _indexMap;
        private bool _disposed;

        public DataGridSelectionSource(DataGridCollectionView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _items = _view.Cast<object>().ToList();
            _indexMap = new Dictionary<object, int>(_items.Count, ReferenceEqualityComparer.Instance);
            for (var i = 0; i < _items.Count; i++)
                _indexMap[_items[i]] = i;

            if (_view is INotifyCollectionChanged incc)
            {
                incc.CollectionChanged += OnViewCollectionChanged;
            }
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public int Count => _items.Count;

        public object this[int index]
        {
            get => _items[index];
            set => throw new NotSupportedException();
        }

        public bool IsReadOnly => true;

        public bool IsFixedSize => true;

        public object SyncRoot => this;

        public bool IsSynchronized => false;

        public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int Add(object value) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Contains(object value) => IndexOf(value) >= 0;

        public int IndexOf(object value) => _items.IndexOf(value);

        public void Insert(int index, object value) => throw new NotSupportedException();

        public void Remove(object value) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();

        public void CopyTo(Array array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            ((ICollection)_items).CopyTo(array, index);
        }

        public bool TryGetReferenceIndex(object item, out int index)
            => _indexMap.TryGetValue(item, out index);

        private void OnViewCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AddItems(e.NewItems, e.NewStartingIndex);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    RemoveItems(e.OldItems, e.OldStartingIndex);
                    break;

                case NotifyCollectionChangedAction.Replace:
                    ReplaceItems(e.OldItems, e.NewItems, e.OldStartingIndex);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    ResetItems();
                    break;

                case NotifyCollectionChangedAction.Move:
                    // Keep selection indexed by item identity: this projection deliberately does not
                    // follow moves. Avalonia's SelectionModel turns a move into remove+add, which
                    // reports the moved item as deselected and can expand the selection. Because the
                    // model and the grid both resolve through this same projection, indexes stay
                    // consistent with each other even though they no longer match the view's order.
                    return;
            }
        }

        private void AddItems(IList items, int startingIndex)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var index = startingIndex < 0 ? _items.Count : Math.Min(startingIndex + i, _items.Count);
                _items.Insert(index, item);
                // Re-index items that shifted up from the insertion point.
                for (var j = index; j < _items.Count; j++)
                    _indexMap[_items[j]] = j;
                CollectionChanged?.Invoke(
                    this,
                    new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
            }
        }

        private void RemoveItems(IList items, int startingIndex)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            // Walk backwards so startingIndex stays valid for the items still to be removed.
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                var index = startingIndex < 0 ? -1 : startingIndex + i;
                if (index < 0 || index >= _items.Count || !ReferenceEquals(_items[index], item))
                {
                    if (!_indexMap.TryGetValue(item, out index))
                    {
                        continue;
                    }
                }

                var removed = _items[index];
                _items.RemoveAt(index);
                _indexMap.Remove(removed);
                // Re-index items that shifted down after the removal point.
                for (var j = index; j < _items.Count; j++)
                    _indexMap[_items[j]] = j;
                CollectionChanged?.Invoke(
                    this,
                    new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
            }
        }

        private void ReplaceItems(IList oldItems, IList newItems, int startingIndex)
        {
            if (oldItems == null || newItems == null || oldItems.Count != newItems.Count)
            {
                ResetItems();
                return;
            }

            for (var i = 0; i < oldItems.Count; i++)
            {
                var index = startingIndex < 0 ? -1 : startingIndex + i;
                if (index < 0 || index >= _items.Count || !ReferenceEquals(_items[index], oldItems[i]))
                {
                    if (!_indexMap.TryGetValue(oldItems[i], out index))
                    {
                        AddItems(new[] { newItems[i] }, startingIndex < 0 ? -1 : startingIndex + i);
                        continue;
                    }
                }

                _indexMap.Remove(oldItems[i]);
                _indexMap[newItems[i]] = index;
                var oldItem = _items[index];
                _items[index] = newItems[i];
                CollectionChanged?.Invoke(
                    this,
                    new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Replace,
                        newItems[i],
                        oldItem,
                        index));
            }
        }

        private void ResetItems()
        {
            _items.Clear();
            _indexMap.Clear();
            _items.AddRange(_view.Cast<object>());
            for (var i = 0; i < _items.Count; i++)
                _indexMap[_items[i]] = i;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
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

            _disposed = true;
        }
    }
}
