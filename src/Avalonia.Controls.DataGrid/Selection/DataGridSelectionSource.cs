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
        private bool _disposed;

        public DataGridSelectionSource(DataGridCollectionView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _items = _view.Cast<object>().ToList();

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
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (ReferenceEquals(_items[i], item))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private void OnViewCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AddItems(e.NewItems);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    RemoveItems(e.OldItems);
                    break;

                case NotifyCollectionChangedAction.Replace:
                    ReplaceItems(e.OldItems, e.NewItems);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    ResetItems();
                    break;

                case NotifyCollectionChangedAction.Move:
                    // Keep selection indexed by item identity. Avalonia's SelectionModel handles
                    // Move as remove/add, which reports a false deselection for moved items.
                    return;
            }
        }

        private void AddItems(IList items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            foreach (object item in items)
            {
                var index = _items.Count;
                _items.Add(item);
                CollectionChanged?.Invoke(
                    this,
                    new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
            }
        }

        private void RemoveItems(IList items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            foreach (object item in items)
            {
                var index = ReferenceIndexOf(item);
                if (index < 0)
                {
                    continue;
                }

                var removed = _items[index];
                _items.RemoveAt(index);
                CollectionChanged?.Invoke(
                    this,
                    new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
            }
        }

        private void ReplaceItems(IList oldItems, IList newItems)
        {
            if (oldItems == null || newItems == null || oldItems.Count != newItems.Count)
            {
                ResetItems();
                return;
            }

            for (var i = 0; i < oldItems.Count; i++)
            {
                var index = ReferenceIndexOf(oldItems[i]);
                if (index < 0)
                {
                    AddItems(new[] { newItems[i] });
                    continue;
                }

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
            _items.AddRange(_view.Cast<object>());
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        private int ReferenceIndexOf(object item)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (ReferenceEquals(_items[i], item))
                {
                    return i;
                }
            }

            return -1;
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
