// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// Selection state for a <see cref="DataGrid"/>, stored as a set of items.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every index this model reports - <see cref="SelectedIndex"/>, <see cref="AnchorIndex"/>, the
    /// order of <see cref="SelectedItems"/> - is computed on demand from <see cref="View"/>. No index is
    /// ever stored, so no index can ever go stale. Reordering the view (a move, a re-sort, a column
    /// drag) requires nothing of this model: it holds the same items, and the next read of an index
    /// simply produces the new answer.
    /// </para>
    /// <para>
    /// The model has exactly one owner - the grid that created it - which is notified through a direct
    /// internal call before <see cref="SelectionChanged"/> reaches anyone else. The owner updates
    /// visuals and never calls back in, so there is a single mutation path whether a change originated
    /// from a click or from consumer code, and no re-entrancy to suppress.
    /// </para>
    /// </remarks>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    abstract class DataGridSelectionModel : INotifyPropertyChanged
    {
        private static readonly object?[] s_empty = Array.Empty<object?>();

        private readonly HashSet<object?> _selected;
        private readonly List<object?> _insertionOrder = new();
        private readonly IEqualityComparer<object?> _comparer;

        private IDataGridSelectionView? _view;
        private object? _anchorItem;
        private object? _leadItem;
        private bool _hasAnchor;
        private bool _hasLead;
        private bool _singleSelect;

        private int _batchDepth;

        // Items touched since the batch opened, paired with whether they were selected at that point.
        // Comparing that against the final state is what makes the reported change the net one: an item
        // deselected and reselected within a batch never changed, and is not reported.
        private List<(object? Item, bool WasSelected)>? _touched;
        private HashSet<object?>? _touchedSet;

        // Cached projection of _insertionOrder into view order. Dropped whenever the selection or the
        // view's ordering changes; rebuilt on demand.
        private IReadOnlyList<object?>? _viewOrdered;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="comparer">
        /// Comparer used to decide whether two items are the same item. Defaults to
        /// <see cref="EqualityComparer{T}.Default"/>, which is reference equality for ordinary classes
        /// and value equality for records and value types.
        /// </param>
        protected DataGridSelectionModel(IEqualityComparer<object?>? comparer = null)
        {
            _comparer = comparer ?? EqualityComparer<object?>.Default;
            _selected = new HashSet<object?>(_comparer);
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raised after the selection has changed and the grid's visuals are consistent.</summary>
        public event EventHandler<DataGridSelectionModelChangedEventArgs>? SelectionChanged;

        /// <summary>The ordered items selection indexes refer to. Assigned by the owning grid.</summary>
        public IDataGridSelectionView? View => _view;

        /// <summary>Number of selected items.</summary>
        public int Count => _selected.Count;

        /// <summary>
        /// When true, selecting an item deselects everything else, and any operation naming more than
        /// one row is refused rather than narrowed.
        /// </summary>
        /// <remarks>
        /// Turning this on with several items selected trims down to <see cref="SelectedItem"/> instead
        /// of throwing. A mode change is not a request for rows, so there is no contradictory request to
        /// refuse - and the grid sets this itself whenever SelectionMode changes, so throwing would make
        /// the control throw at itself over a selection the consumer made while the mode still allowed it.
        /// </remarks>
        public bool SingleSelect
        {
            get => _singleSelect;
            set
            {
                if (_singleSelect == value)
                {
                    return;
                }

                _singleSelect = value;
                if (value && _selected.Count > 1)
                {
                    // SelectedItem already is "the one to keep": the lead when it is still selected,
                    // and otherwise the first in view order.
                    var keep = SelectedItem;
                    using (BatchUpdate())
                    {
                        ClearCore(except: keep);
                    }
                }

                // The owner first, so the control's selection mode already agrees by the time
                // consumers are told - the same ordering the selection change itself uses.
                Owner?.OnSelectionModelSingleSelectChanged(value);
                RaisePropertyChanged(nameof(SingleSelect));
            }
        }

        /// <summary>
        /// The selected items, in the view's current order. Items no longer present in the view keep
        /// their relative order and come last.
        /// </summary>
        public IReadOnlyList<object?> SelectedItems => _viewOrdered ??= BuildViewOrdered();

        /// <summary>
        /// The current indexes of the selected items, ascending. Items not presently in the view are
        /// omitted rather than reported as -1.
        /// </summary>
        /// <remarks>Computed on every read, like every other index this model reports.</remarks>
        public IReadOnlyList<int> SelectedIndexes
        {
            get
            {
                if (_view is null || _selected.Count == 0)
                {
                    return Array.Empty<int>();
                }

                var result = new List<int>(_selected.Count);
                foreach (var item in SelectedItems)
                {
                    var index = _view.IndexOf(item);
                    if (index >= 0)
                    {
                        result.Add(index);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// The primary selected item: the one most recently operated on, or the first in view order.
        /// </summary>
        public object? SelectedItem
        {
            get
            {
                if (_hasLead && _selected.Contains(_leadItem))
                {
                    return _leadItem;
                }

                return _selected.Count == 0 ? null : FirstInViewOrder();
            }
            set
            {
                // Null is "nothing is selected", which is what the getter reports when nothing is.
                // A binding must be able to write its own reading back, so this is a clear rather
                // than a selection of no item.
                if (value is null)
                {
                    Clear();
                    return;
                }

                SetSelectedItems(new[] { value });
            }
        }

        /// <summary>
        /// Index of <see cref="SelectedItem"/> in the current view, or -1.
        /// </summary>
        /// <remarks>Computed on every read - it cannot disagree with the view's order.</remarks>
        /// <exception cref="InvalidOperationException">
        /// A non-negative index was assigned while the model is not attached to a view.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The assigned index is past the end of the view.
        /// </exception>
        public int SelectedIndex
        {
            get
            {
                var item = SelectedItem;
                return item is null && _selected.Count == 0 ? -1 : IndexOf(item);
            }
            set
            {
                if (value < 0)
                {
                    // The one value that means the same thing with or without rows: nothing is
                    // selected. It is also what the getter reports when nothing is, so a binding
                    // can round-trip its own reading without needing a view to do it in.
                    Clear();
                    return;
                }

                var view = RequireView(nameof(SelectedIndex));

                if (!view.TryGetItemAt(value, out var item))
                {
                    throw IndexOutOfRange(nameof(value), value, view);
                }

                SetSelectedItems(new[] { item });
            }
        }

        /// <summary>The item shift-range selection extends from, or null.</summary>
        public object? AnchorItem
        {
            get => _hasAnchor ? _anchorItem : null;
            set
            {
                _anchorItem = value;
                _hasAnchor = value is not null;
            }
        }

        /// <summary>Index of <see cref="AnchorItem"/> in the current view, or -1.</summary>
        public int AnchorIndex => _hasAnchor ? IndexOf(_anchorItem) : -1;

        /// <summary>Determines whether <paramref name="item"/> is selected.</summary>
        public bool IsSelected(object? item) => _selected.Contains(item);

        /// <summary>Determines whether the item at <paramref name="index"/> is selected.</summary>
        /// <exception cref="InvalidOperationException">The model is not attached to a view.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="index"/> is past the end of the view.
        /// </exception>
        public bool IsIndexSelected(int index)
        {
            var view = RequireView(nameof(IsIndexSelected));
            RequireInRange(nameof(index), index, view);

            return view.TryGetItemAt(index, out var item) && _selected.Contains(item);
        }

        /// <summary>Adds <paramref name="item"/> to the selection.</summary>
        /// <exception cref="InvalidOperationException">The model is not attached to a view.</exception>
        public void Select(object? item)
        {
            RequireView(nameof(Select));

            using (BatchUpdate())
            {
                if (_singleSelect)
                {
                    ClearCore(except: item);
                }

                AddCore(item);
                // Re-selecting an already-selected item is still the one the user just acted on, so it
                // becomes the lead even though the set did not change.
                SetLead(item);
            }
        }

        /// <summary>Removes <paramref name="item"/> from the selection.</summary>
        /// <exception cref="InvalidOperationException">The model is not attached to a view.</exception>
        public void Deselect(object? item)
        {
            RequireView(nameof(Deselect));

            using (BatchUpdate())
            {
                RemoveCore(item);
            }
        }

        /// <summary>Selects or deselects <paramref name="item"/>.</summary>
        public void SetSelected(object? item, bool isSelected)
        {
            if (isSelected)
            {
                Select(item);
            }
            else
            {
                Deselect(item);
            }
        }

        /// <summary>Selects the item currently at <paramref name="index"/>.</summary>
        /// <exception cref="InvalidOperationException">The model is not attached to a view.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="index"/> is past the end of the view.
        /// </exception>
        public void SelectAt(int index)
        {
            var view = RequireView(nameof(SelectAt));

            if (!view.TryGetItemAt(index, out var item))
            {
                throw IndexOutOfRange(nameof(index), index, view);
            }

            Select(item);
        }

        /// <summary>Deselects the item currently at <paramref name="index"/>.</summary>
        /// <exception cref="InvalidOperationException">The model is not attached to a view.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="index"/> is past the end of the view.
        /// </exception>
        public void DeselectAt(int index)
        {
            var view = RequireView(nameof(DeselectAt));

            if (!view.TryGetItemAt(index, out var item))
            {
                throw IndexOutOfRange(nameof(index), index, view);
            }

            Deselect(item);
        }

        /// <summary>
        /// Selects every item between the two indexes inclusive. The range is resolved to items
        /// immediately, so a later reorder moves the selection with the items rather than the positions.
        /// </summary>
        /// <remarks>
        /// The two ends may be given in either order - a range dragged upwards is the same range -
        /// but both have to name rows. An end past the last one is not a range the view can narrow
        /// to a smaller one on the caller's behalf; it is a range over rows that are not there.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The model is not attached to a view, or the range covers more than one row while
        /// <see cref="SingleSelect"/> is on.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Either end is past the end of the view.
        /// </exception>
        public void SelectRange(int fromIndex, int toIndex)
        {
            var view = RequireView(nameof(SelectRange));
            RequireInRange(nameof(fromIndex), fromIndex, view);
            RequireInRange(nameof(toIndex), toIndex, view);

            var start = Math.Min(fromIndex, toIndex);
            var end = Math.Max(fromIndex, toIndex);

            // A range of one row is a range single selection can honour, so the refusal is about the
            // rows asked for rather than about the method that asked for them.
            if (_singleSelect && end > start)
            {
                throw SingleSelectRefused(nameof(SelectRange), end - start + 1);
            }

            using (BatchUpdate())
            {
                for (int i = start; i <= end; i++)
                {
                    if (view.TryGetItemAt(i, out var item))
                    {
                        AddCore(item);
                    }
                }

                if (view.TryGetItemAt(toIndex, out var lead))
                {
                    SetLead(lead);
                }
            }
        }

        /// <summary>Deselects every item between the two indexes inclusive.</summary>
        /// <exception cref="InvalidOperationException">The model is not attached to a view.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Either end is past the end of the view.
        /// </exception>
        public void DeselectRange(int fromIndex, int toIndex)
        {
            var view = RequireView(nameof(DeselectRange));
            RequireInRange(nameof(fromIndex), fromIndex, view);
            RequireInRange(nameof(toIndex), toIndex, view);

            var start = Math.Min(fromIndex, toIndex);
            var end = Math.Max(fromIndex, toIndex);

            using (BatchUpdate())
            {
                for (int i = start; i <= end; i++)
                {
                    if (view.TryGetItemAt(i, out var item))
                    {
                        RemoveCore(item);
                    }
                }
            }
        }

        /// <summary>Selects every item in the view.</summary>
        /// <remarks>
        /// Refused under <see cref="SingleSelect"/> unless the view holds at most one row, in which
        /// case "every item" and "the one item" are the same request.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The model is not attached to a view, or the view holds more than one row while
        /// <see cref="SingleSelect"/> is on.
        /// </exception>
        public void SelectAll()
        {
            var view = RequireView(nameof(SelectAll));

            if (_singleSelect && view.Count > 1)
            {
                throw SingleSelectRefused(nameof(SelectAll), view.Count);
            }

            using (BatchUpdate())
            {
                foreach (var item in view.Items)
                {
                    AddCore(item);
                }
            }
        }

        /// <summary>Clears the selection.</summary>
        public void Clear()
        {
            using (BatchUpdate())
            {
                ClearCore(except: null, hasExcept: false);
            }
        }

        /// <summary>
        /// Replaces the selection with <paramref name="items"/> in a single change.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The model is not attached to a view, or <paramref name="items"/> names more than one
        /// distinct item while <see cref="SingleSelect"/> is on.
        /// </exception>
        public void SetSelectedItems(IEnumerable? items)
        {
            if (items is null)
            {
                // Replacing the selection with nothing is clearing it, which needs no rows.
                Clear();
                return;
            }

            RequireView(nameof(SetSelectedItems));

            // Enumerated once and up front, for two reasons: the caller's sequence need not survive a
            // second pass, and the count has to be known before anything is applied so that a refusal
            // leaves the selection exactly as it was.
            var distinct = new HashSet<object?>(_comparer);
            var incoming = new List<object?>();
            foreach (var item in items)
            {
                if (distinct.Add(item))
                {
                    incoming.Add(item);
                }
            }

            // The same item listed twice still names one row, which is why the check is on the
            // distinct count rather than on how long the caller's sequence happened to be.
            if (_singleSelect && incoming.Count > 1)
            {
                throw SingleSelectRefused(nameof(SetSelectedItems), incoming.Count);
            }

            using (BatchUpdate())
            {
                for (int i = _insertionOrder.Count - 1; i >= 0; i--)
                {
                    var existing = _insertionOrder[i];
                    if (!distinct.Contains(existing))
                    {
                        RemoveCore(existing);
                    }
                }

                foreach (var item in incoming)
                {
                    AddCore(item);
                }

                if (incoming.Count > 0)
                {
                    SetLead(incoming[^1]);
                }
            }
        }

        /// <summary>
        /// Defers notifications until the returned scope is disposed, so a multi-step change is
        /// reported once.
        /// </summary>
        public IDisposable BatchUpdate() => new Batch(this);

        /// <summary>
        /// Creates the event args for a change. Overridden by the typed model to produce typed args.
        /// </summary>
        protected virtual DataGridSelectionModelChangedEventArgs CreateChangedArgs(
            IReadOnlyList<object?> selected,
            IReadOnlyList<object?> deselected)
            => new(selected, deselected);

        /// <summary>Raises <see cref="SelectionChanged"/>.</summary>
        protected virtual void OnSelectionChanged(DataGridSelectionModelChangedEventArgs e)
            => SelectionChanged?.Invoke(this, e);

        /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
        protected void RaisePropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // ---- owner-facing surface -------------------------------------------------------------

        /// <summary>
        /// The grid that owns this model. Notified synchronously, ahead of <see cref="SelectionChanged"/>.
        /// </summary>
        internal IDataGridSelectionOwner? Owner { get; set; }

        internal IEqualityComparer<object?> Comparer => _comparer;

        internal void AttachView(IDataGridSelectionView? view)
        {
            _view = view;
            InvalidateOrder();
        }

        /// <summary>
        /// Tells the model the view's ordering changed. Selection content is unaffected - this only
        /// drops the cached view-order projection.
        /// </summary>
        internal void InvalidateOrder()
        {
            _viewOrdered = null;
            RaisePropertyChanged(nameof(SelectedItems));
            RaisePropertyChanged(nameof(SelectedIndex));
        }

        /// <summary>Drops items that were removed from the underlying data.</summary>
        internal void RemoveItems(IEnumerable items)
        {
            using (BatchUpdate())
            {
                foreach (var item in items)
                {
                    RemoveCore(item);
                }
            }
        }

        /// <summary>
        /// Drops every selected item for which <paramref name="keep"/> returns false. Used after a reset,
        /// where the grid decides what "still exists" means - membership of the source collection, not of
        /// a filtered view, so that filtering hides a selection rather than destroying it.
        /// </summary>
        internal void RetainOnly(Func<object?, bool> keep)
        {
            using (BatchUpdate())
            {
                for (int i = _insertionOrder.Count - 1; i >= 0; i--)
                {
                    var item = _insertionOrder[i];
                    if (!keep(item))
                    {
                        RemoveCore(item);
                    }
                }
            }
        }

        internal void SetLead(object? item)
        {
            _leadItem = item;
            _hasLead = true;
        }

        // ---- internals ------------------------------------------------------------------------

        /// <summary>
        /// The attached view, or an exception for an operation the missing view leaves meaningless.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Guards every operation whose meaning comes from the view rather than from an item: the
        /// index-based ones, which name positions in a list that does not exist, and
        /// <see cref="SelectAll"/>, whose "all" the view alone defines. There is no honest answer to
        /// "select rows 3 to 7" when there are no rows to count.
        /// </para>
        /// <para>
        /// Returning quietly is the worst of the options - the call looks like it worked, the
        /// selection stays empty, and the mistake surfaces somewhere else entirely - and holding the
        /// request until a view arrives is barely better, because it defers a caller's mistake
        /// instead of reporting it.
        /// </para>
        /// <para>
        /// The item-based operations are not guarded, and that is a limit of this rule rather than a
        /// pattern to follow: the grid drives them itself before a view exists - from a bound
        /// SelectedItems collection and from state restore, neither of which controls whether
        /// ItemsSource has been assigned yet - so guarding them would make the control throw at
        /// itself over property order.
        /// </para>
        /// </remarks>
        /// <summary>Throws unless <paramref name="index"/> names a row the view has.</summary>
        private static void RequireInRange(string parameter, int index, IDataGridSelectionView view)
        {
            if (index < 0 || index >= view.Count)
            {
                throw IndexOutOfRange(parameter, index, view);
            }
        }

        /// <summary>
        /// The exception for an index that names no row, reporting what the caller asked for and
        /// what was there - a position on its own says nothing about why it was refused.
        /// </summary>
        private static ArgumentOutOfRangeException IndexOutOfRange(
            string parameter,
            int index,
            IDataGridSelectionView view)
            => new(
                parameter,
                index,
                view.Count == 0
                    ? "The view has no rows, so no index names one."
                    : $"The view has {view.Count} rows, so the last index that names one is {view.Count - 1}.");

        /// <summary>
        /// The exception for an operation that named more rows than single selection can hold.
        /// </summary>
        /// <remarks>
        /// Narrowing the request would mean choosing a row on the caller's behalf, and every rule for
        /// choosing one - the first, the last, the end a drag finished on - is a guess that looks like
        /// a policy. The caller knows which row it meant; the model does not, so it says so instead of
        /// silently keeping one and discarding the rest.
        /// </remarks>
        private static InvalidOperationException SingleSelectRefused(string operation, int requested)
            => new(
                $"{operation} named {requested} rows while SingleSelect is on, which can hold one. " +
                "Turn SingleSelect off to select a range, or name the single row to select.");

        private IDataGridSelectionView RequireView(string operation)
            => _view ?? throw new InvalidOperationException(
                $"{operation} needs a view to resolve against, and this selection model is not " +
                "attached to one. Assign it to a DataGrid's Selection property first, or select " +
                "by item with Select, which needs no view.");

        private int IndexOf(object? item) => _view?.IndexOf(item) ?? -1;

        private object? FirstInViewOrder()
        {
            var ordered = SelectedItems;
            return ordered.Count > 0 ? ordered[0] : null;
        }

        private IReadOnlyList<object?> BuildViewOrdered()
        {
            if (_insertionOrder.Count == 0)
            {
                return s_empty;
            }

            if (_view is null)
            {
                return _insertionOrder.ToArray();
            }

            var result = _insertionOrder.ToArray();
            var keys = new long[result.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var index = _view.IndexOf(result[i]);
                // Items no longer in the view sort after everything else, keeping insertion order
                // among themselves. That last part is what the low half of the key buys: every
                // absent item shares the same view index, and Array.Sort is not stable, so above
                // sixteen elements ties come back in an arbitrary - and varying - order.
                keys[i] = ((long)(index < 0 ? int.MaxValue : index) << 32) | (uint)i;
            }

            Array.Sort(keys, result);
            return result;
        }

        private void Touch(object? item, bool wasSelected)
        {
            _touchedSet ??= new HashSet<object?>(_comparer);
            if (_touchedSet.Add(item))
            {
                (_touched ??= new List<(object?, bool)>()).Add((item, wasSelected));
            }
        }

        private bool AddCore(object? item)
        {
            if (!_selected.Add(item))
            {
                return false;
            }

            Touch(item, wasSelected: false);
            _insertionOrder.Add(item);
            _viewOrdered = null;
            return true;
        }

        private bool RemoveCore(object? item)
        {
            if (!_selected.Remove(item))
            {
                return false;
            }

            Touch(item, wasSelected: true);

            for (int i = 0; i < _insertionOrder.Count; i++)
            {
                if (_comparer.Equals(_insertionOrder[i], item))
                {
                    _insertionOrder.RemoveAt(i);
                    break;
                }
            }

            _viewOrdered = null;

            if (_hasLead && _comparer.Equals(_leadItem, item))
            {
                _leadItem = null;
                _hasLead = false;
            }

            return true;
        }

        private void ClearCore(object? except, bool hasExcept = true)
        {
            for (int i = _insertionOrder.Count - 1; i >= 0; i--)
            {
                var item = _insertionOrder[i];
                if (hasExcept && _comparer.Equals(item, except))
                {
                    continue;
                }

                RemoveCore(item);
            }
        }

        private void Flush()
        {
            var touched = _touched;
            _touched = null;
            _touchedSet = null;

            if (touched is null)
            {
                return;
            }

            var added = new List<object?>();
            var removed = new List<object?>();
            foreach (var (item, wasSelected) in touched)
            {
                var isSelected = _selected.Contains(item);
                if (isSelected == wasSelected)
                {
                    continue;
                }

                (isSelected ? added : removed).Add(item);
            }

            if (added.Count == 0 && removed.Count == 0)
            {
                return;
            }

            var args = CreateChangedArgs(added, removed);

            // The owner brings the grid's visuals in line first, so that by the time consumers see
            // SelectionChanged the control already agrees with the model.
            Owner?.OnSelectionModelChanged(args);

            RaisePropertyChanged(nameof(Count));
            RaisePropertyChanged(nameof(SelectedItems));
            RaisePropertyChanged(nameof(SelectedItem));
            RaisePropertyChanged(nameof(SelectedIndex));

            OnSelectionChanged(args);
        }

        private sealed class Batch : IDisposable
        {
            private readonly DataGridSelectionModel _owner;
            private bool _disposed;

            public Batch(DataGridSelectionModel owner)
            {
                _owner = owner;
                _owner._batchDepth++;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (--_owner._batchDepth == 0)
                {
                    _owner.Flush();
                }
            }
        }
    }
}
