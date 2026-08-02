// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Avalonia.Controls.DataGridHierarchical
{
    /// <summary>
    /// Observable collection with basic range helpers for the flattened hierarchical view.
    /// </summary>
    internal sealed class ObservableRangeCollection<T> : ObservableCollection<T>
    {
        public ObservableRangeCollection()
        {
        }

        public ObservableRangeCollection(IEnumerable<T> items)
            : base(items)
        {
        }

        public void AddRange(IEnumerable<T> items)
        {
            InsertRange(Count, items);
        }

        public void InsertRange(int index, IEnumerable<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var materialized = Materialize(items);
            if (materialized.Count == 0)
            {
                return;
            }

            CheckReentrancy();

            for (var i = 0; i < materialized.Count; i++)
            {
                Items.Insert(index + i, materialized[i]);
            }

            var notifyItems = materialized as IList ?? materialized.ToList();
            RaiseChange(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                notifyItems,
                index));
        }

        public IList<T> GetRange(int index, int count)
        {
            if (index < 0 || count < 0 || index + count > Count)
            {
                throw new ArgumentOutOfRangeException();
            }

            var buffer = new List<T>(count);
            for (var i = 0; i < count; i++)
            {
                buffer.Add(Items[index + i]);
            }

            return buffer;
        }

        /// <summary>
        /// Relocates a run of <paramref name="count"/> items so that it starts at
        /// <paramref name="newIndex"/>, as a single move.
        /// </summary>
        /// <param name="oldIndex">Where the run starts now.</param>
        /// <param name="newIndex">Where it starts once it has been moved, in the resulting list.</param>
        /// <param name="count">How many items travel together.</param>
        /// <remarks>
        /// <para>
        /// Relocating a run is a rotation of the span it travels over, so it is done as one: nothing
        /// leaves the collection and nothing joins it, and a single <see cref="NotifyCollectionChangedAction.Move"/>
        /// says so. Taking the items out and putting them back would end in the same arrangement but
        /// would tell every consumer that those items were removed and different ones added, which is
        /// not what happened and costs anything keyed on the items - selection, scroll anchoring,
        /// row containers - the thing it was keyed on.
        /// </para>
        /// <para>
        /// <paramref name="newIndex"/> follows the convention of
        /// <see cref="System.Collections.ObjectModel.ObservableCollection{T}.Move"/>: it is the index
        /// the run occupies afterwards, not the index it would be inserted at while the run is still
        /// in its old place.
        /// </para>
        /// </remarks>
        public void MoveRange(int oldIndex, int newIndex, int count)
        {
            // Checked before the shortcuts below, so that a run that does not fit is rejected
            // whether or not it would have moved anywhere.
            if (count < 0 || oldIndex < 0 || newIndex < 0 || oldIndex + count > Count || newIndex + count > Count)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (count == 0 || oldIndex == newIndex)
            {
                return;
            }

            CheckReentrancy();

            var moved = new List<T>(count);
            for (var i = 0; i < count; i++)
            {
                moved.Add(Items[oldIndex + i]);
            }

            if (newIndex > oldIndex)
            {
                // Everything between the run's old end and its new end slides back over it.
                for (var i = 0; i < newIndex - oldIndex; i++)
                {
                    Items[oldIndex + i] = Items[oldIndex + count + i];
                }
            }
            else
            {
                // Same, the other way: walking backwards keeps each read ahead of its overwrite.
                for (var i = oldIndex - newIndex - 1; i >= 0; i--)
                {
                    Items[newIndex + count + i] = Items[newIndex + i];
                }
            }

            for (var i = 0; i < count; i++)
            {
                Items[newIndex + i] = moved[i];
            }

            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Move,
                (IList)moved,
                newIndex,
                oldIndex));
        }

        public void RemoveRange(int index, int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (index < 0 || index + count > Count)
            {
                throw new ArgumentOutOfRangeException();
            }

            CheckReentrancy();
            var removed = new List<T>(count);
            for (var i = 0; i < count; i++)
            {
                removed.Add(Items[index]);
                Items.RemoveAt(index);
            }

            var notifyItems = (IList)removed;
            RaiseChange(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove,
                notifyItems,
                index));
        }

        public void ResetWith(IEnumerable<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            CheckReentrancy();

            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            RaiseReset();
        }

        private void RaiseChange(NotifyCollectionChangedEventArgs args)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(args);
        }

        private void RaiseReset()
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        private static IList<T> Materialize(IEnumerable<T> items)
        {
            if (items is IList<T> list)
            {
                return list;
            }

            return items.ToList();
        }
    }
}
