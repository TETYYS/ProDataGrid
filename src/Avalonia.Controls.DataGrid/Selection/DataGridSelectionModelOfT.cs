// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// Strongly typed <see cref="DataGridSelectionModel"/>.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <remarks>
    /// A typed facade only - all state and logic live in the base class, so the typed and untyped views
    /// of the selection can never disagree.
    /// </remarks>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    class DataGridSelectionModel<T> : DataGridSelectionModel
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="comparer">
        /// Comparer used to decide whether two items are the same item. Defaults to
        /// <see cref="EqualityComparer{T}.Default"/> for <typeparamref name="T"/>.
        /// </param>
        public DataGridSelectionModel(IEqualityComparer<T>? comparer = null)
            : base(comparer is null ? null : new Untyped(comparer))
        {
        }

        /// <summary>Raised after the selection has changed and the grid's visuals are consistent.</summary>
        public new event EventHandler<DataGridSelectionModelChangedEventArgs<T>>? SelectionChanged;

        /// <summary>The selected items, in the view's current order.</summary>
        public new IReadOnlyList<T> SelectedItems => base.SelectedItems.OfType<T>().ToArray();

        /// <summary>The primary selected item: the one most recently operated on, or the first in view order.</summary>
        public new T? SelectedItem
        {
            get => base.SelectedItem is T typed ? typed : default;
            set => base.SelectedItem = value;
        }

        /// <summary>The item shift-range selection extends from, or the default.</summary>
        public new T? AnchorItem
        {
            get => base.AnchorItem is T typed ? typed : default;
            set => base.AnchorItem = value;
        }

        /// <summary>Adds <paramref name="item"/> to the selection.</summary>
        public void Select(T item) => base.Select(item);

        /// <summary>Removes <paramref name="item"/> from the selection.</summary>
        public void Deselect(T item) => base.Deselect(item);

        /// <summary>Determines whether <paramref name="item"/> is selected.</summary>
        public bool IsSelected(T item) => base.IsSelected(item);

        /// <summary>Replaces the selection with <paramref name="items"/> in a single change.</summary>
        public void SetSelectedItems(IEnumerable<T>? items) => base.SetSelectedItems(items);

        /// <inheritdoc />
        protected override DataGridSelectionModelChangedEventArgs CreateChangedArgs(
            IReadOnlyList<object?> selected,
            IReadOnlyList<object?> deselected)
            => new DataGridSelectionModelChangedEventArgs<T>(selected, deselected);

        /// <inheritdoc />
        protected override void OnSelectionChanged(DataGridSelectionModelChangedEventArgs e)
        {
            base.OnSelectionChanged(e);

            if (SelectionChanged is not { } handler)
            {
                return;
            }

            // Normally this is the very object CreateChangedArgs made just above. It need not be:
            // CreateChangedArgs is an extension point, and a derived model that overrides it to
            // return args of its own is entitled to - so the typed event is rebuilt from what
            // arrived rather than cast to what was expected. Casting made overriding one virtual
            // method break another.
            var typed = e as DataGridSelectionModelChangedEventArgs<T>
                ?? new DataGridSelectionModelChangedEventArgs<T>(e.SelectedItems, e.DeselectedItems);

            handler(this, typed);
        }

        private sealed class Untyped : IEqualityComparer<object?>
        {
            private readonly IEqualityComparer<T> _inner;

            public Untyped(IEqualityComparer<T> inner) => _inner = inner;

            public new bool Equals(object? x, object? y)
                => x is T a && y is T b ? _inner.Equals(a, b) : ReferenceEquals(x, y);

            public int GetHashCode(object? obj) => obj is T t ? _inner.GetHashCode(t!) : 0;
        }
    }
}
