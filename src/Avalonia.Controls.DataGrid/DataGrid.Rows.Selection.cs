// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Avalonia.Controls.DataGridSelection;

namespace Avalonia.Controls
{
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    partial class DataGrid : IDataGridSelectionOwner
    {

        private void SelectDisplayedElement(int slot, bool? isSelectedOverride = null)
        {
            Debug.Assert(IsSlotVisible(slot));
            Control element = DisplayData.GetDisplayedElement(slot);
            if (element is DataGridRow row)
            {
                row.ApplyState(isSelectedOverride);
                row.ApplyCellsState();
                EnsureRowDetailsVisibility(row, raiseNotification: true, animate: true, isSelectedOverride: isSelectedOverride);
            }
            else
            {
                // Assume it's a RowGroupHeader
                DataGridRowGroupHeader groupHeader = element as DataGridRowGroupHeader;
                groupHeader.UpdatePseudoClasses();
            }
        }

        /// <summary>
        /// Resolves the data item a slot displays. Group header and footer slots carry no data item.
        /// </summary>
        /// <remarks>
        /// Projected, so that hierarchical rows put the caller's item into the selection rather than the
        /// node wrapping it. The selection view projects the same way, and the two have to agree: the
        /// model looks items up by identity, so a node stored where an item is expected simply never
        /// matches.
        /// </remarks>
        internal bool TryGetItemForSlot(int slot, out object item)
        {
            item = null;
            if (slot < 0 || DataConnection == null || IsGroupSlot(slot))
            {
                return false;
            }

            int rowIndex = RowIndexFromSlot(slot);
            if (rowIndex < 0 || rowIndex >= DataConnection.Count)
            {
                return false;
            }

            item = ProjectSelectionItem(DataConnection.GetDataItem(rowIndex));
            return item != null;
        }

        /// <summary>
        /// Resolves the slot currently displaying <paramref name="item"/>, or -1 when it is not shown.
        /// </summary>
        internal int SlotForItem(object item)
        {
            if (item == null)
            {
                return -1;
            }

            return TryGetRowIndexFromItem(item, out var rowIndex) ? SlotFromRowIndex(rowIndex) : -1;
        }

        /// <summary>
        /// The slots of the selected items, in ascending order.
        /// </summary>
        /// <remarks>
        /// Derived on demand from the selected items rather than tracked alongside them, which is why
        /// nothing has to be re-indexed when rows move: selection order follows view order, so slots
        /// come out ascending without sorting.
        /// </remarks>
        internal IEnumerable<int> GetSelectedSlots()
        {
            foreach (var item in _selectionModel.SelectedItems)
            {
                var slot = SlotForItem(item);
                if (slot >= 0)
                {
                    yield return slot;
                }
            }
        }

        internal int GetSelectedSlotCount(int lowerBound, int upperBound)
        {
            var count = 0;
            foreach (var slot in GetSelectedSlots())
            {
                if (slot >= lowerBound && slot <= upperBound)
                {
                    count++;
                }
            }

            return count;
        }

        private bool AreAllSlotsSelected(int startSlot, int endSlot)
        {
            if (startSlot > endSlot)
            {
                return true;
            }

            for (int slot = startSlot; slot <= endSlot; slot++)
            {
                if (!IsGroupSlot(slot) && !GetRowSelection(slot))
                {
                    return false;
                }
            }

            return true;
        }

        private void SelectSlots(int startSlot, int endSlot, bool isSelected)
        {
            using (_selectionModel.BatchUpdate())
            {
                for (int slot = startSlot; slot <= endSlot; slot++)
                {
                    if (TryGetItemForSlot(slot, out var item))
                    {
                        _selectionModel.SetSelected(item, isSelected);
                    }
                }
            }
        }

        /// <summary>
        /// Clears the entire selection.
        /// </summary>
        internal void ClearRowSelection(bool resetAnchorSlot)
        {
            if (resetAnchorSlot)
            {
                AnchorSlot = -1;
            }

            _noSelectionChangeCount++;
            try
            {
                _selectionModel.Clear();
            }
            finally
            {
                NoSelectionChangeCount--;
            }
        }

        internal int GetCollapsedSlotCount(int startSlot, int endSlot)
        {
            return _collapsedSlotsTable.GetIndexCount(startSlot, endSlot);
        }

        internal bool GetRowSelection(int slot)
        {
            return TryGetItemForSlot(slot, out var item) && _selectionModel.IsSelected(item);
        }

        internal bool GetRowSelectionFromRowIndex(int rowIndex)
        {
            if (rowIndex < 0 || DataConnection == null || rowIndex >= DataConnection.Count)
            {
                return false;
            }

            var item = ProjectSelectionItem(DataConnection.GetDataItem(rowIndex));
            return item != null && _selectionModel.IsSelected(item);
        }

        internal void SetRowSelection(int slot, bool isSelected, bool setAnchorSlot)
        {
            Debug.Assert(!(!isSelected && setAnchorSlot));
            if (IsSlotOutOfSelectionBounds(slot))
            {
                // Slot no longer maps to a data item (e.g. while resetting the collection)
                return;
            }
            if (_suppressSelectionUpdatesFromRows)
            {
                return;
            }

            _noSelectionChangeCount++;
            try
            {
                if (TryGetItemForSlot(slot, out var item))
                {
                    if (isSelected && SelectionMode == DataGridSelectionMode.Single)
                    {
                        // Replacing rather than adding keeps the swap to a single reported change.
                        _selectionModel.SetSelectedItems(new[] { item });
                    }
                    else
                    {
                        _selectionModel.SetSelected(item, isSelected);
                    }
                }

                if (setAnchorSlot)
                {
                    AnchorSlot = slot;
                }
            }
            finally
            {
                NoSelectionChangeCount--;
            }
        }

        /// <summary>
        /// Deselects items that were removed from the data.
        /// </summary>
        /// <remarks>
        /// Called only from the Remove notification, never from the row-removal bookkeeping: a row also
        /// leaves its slot when it is repositioned, and by then the two are indistinguishable.
        /// </remarks>
        internal void DeselectRemovedItems(System.Collections.IList removedItems)
        {
            if (removedItems == null || removedItems.Count == 0 || _selectionModel is not { Count: > 0 })
            {
                return;
            }

            _selectionModel.RemoveItems(removedItems);
        }

        internal bool PushRowSelectionUpdateSuppression()
        {
            var previous = _suppressSelectionUpdatesFromRows;
            _suppressSelectionUpdatesFromRows = true;
            return previous;
        }

        internal void PopRowSelectionUpdateSuppression(bool previous)
        {
            _suppressSelectionUpdatesFromRows = previous;
        }

        // ---- selection model owner -------------------------------------------------------------

        /// <summary>
        /// The model's single notification point into the grid.
        /// </summary>
        /// <remarks>
        /// Called synchronously mid-mutation, before consumers see the change, for every selection
        /// change regardless of origin - a click, a keyboard range, or consumer code calling
        /// <c>Selection.Select</c>. That is what removes the need for a flag distinguishing the two
        /// directions: there is only one direction.
        /// </remarks>
        void IDataGridSelectionOwner.OnSelectionModelChanged(DataGridSelectionModelChangedEventArgs e)
        {
            // Every selection change reaches the grid through here, so this is where the change is
            // marked as having come from the model. The source is a flag set, so a more specific
            // origin already in scope - a click, a keyboard range - is kept alongside it.
            using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.SelectionModelSync);

            SelectionHasChanged = true;

            AccumulateSelectionChange(e);
            ApplySelectionVisuals(e);
            ApplySelectionChangeToBinding(e);

            if (_noSelectionChangeCount == 0)
            {
                FlushSelectionChanged();
            }
        }

        void IDataGridSelectionOwner.OnSelectionModelSingleSelectChanged(bool singleSelect)
        {
            // While the grid is detached, SelectionMode is whatever the consumer set last and the
            // model does not get to overrule it. Attaching re-imposes the mode onto the model, never
            // the other way round - see AttachSelectionModelHandlers.
            if (_externalSubscriptionsDetached)
            {
                return;
            }

            var mode = singleSelect ? DataGridSelectionMode.Single : DataGridSelectionMode.Extended;
            if (SelectionMode == mode)
            {
                return;
            }

            // No callback: OnSelectionModeChanged would push the value straight back into the model
            // and clear the selection on the way through.
            SetValueNoCallback(SelectionModeProperty, mode);
        }

        /// <summary>
        /// Folds a model change into the pending delta for the grid's own SelectionChanged event, which
        /// spans a whole grid operation rather than a single model call.
        /// </summary>
        private void AccumulateSelectionChange(DataGridSelectionModelChangedEventArgs e)
        {
            var comparer = _selectionModel.Comparer;

            foreach (var item in e.SelectedItems)
            {
                if (!RemoveFirst(_pendingSelectionRemoved, item, comparer))
                {
                    _pendingSelectionAdded.Add(item);
                }
            }

            foreach (var item in e.DeselectedItems)
            {
                if (!RemoveFirst(_pendingSelectionAdded, item, comparer))
                {
                    _pendingSelectionRemoved.Add(item);
                }
            }

            static bool RemoveFirst(List<object> list, object item, IEqualityComparer<object> comparer)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (comparer.Equals(list[i], item))
                    {
                        list.RemoveAt(i);
                        return true;
                    }
                }

                return false;
            }
        }

        private void ApplySelectionVisuals(DataGridSelectionModelChangedEventArgs e)
        {
            if (DisplayData == null)
            {
                return;
            }

            Apply(e.DeselectedItems, isSelected: false);
            Apply(e.SelectedItems, isSelected: true);

            void Apply(IReadOnlyList<object> items, bool isSelected)
            {
                foreach (var item in items)
                {
                    var slot = SlotForItem(item);
                    if (slot >= 0 && IsSlotVisible(slot))
                    {
                        SelectDisplayedElement(slot, isSelected);
                    }
                }
            }
        }
    }
}
