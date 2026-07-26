// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

#nullable disable

using Avalonia.Collections;
using Avalonia.Controls.Utils;
using Avalonia.Interactivity;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.Selection;
using Avalonia.Threading;
using Avalonia.Utilities;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace Avalonia.Controls
{
    /// <summary>
    /// Selection management
    /// </summary>
#if !DATAGRID_INTERNAL
public
#else
internal
#endif
    partial class DataGrid
    {

        public void SelectAll()
        {
            using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.Command);
            SetRowsSelection(0, SlotCount - 1);
        }


        /// <summary>
        /// Selects items and updates currency based on parameters
        /// </summary>
        /// <param name="columnIndex">column index to make current</param>
        /// <param name="item">data item or CollectionViewGroup to make current</param>
        /// <param name="backupSlot">slot to use in case the item is no longer valid</param>
        /// <param name="action">selection action to perform</param>
        /// <param name="scrollIntoView">whether or not the new current item should be scrolled into view</param>
        internal void ProcessSelectionAndCurrency(int columnIndex, object item, int backupSlot, DataGridSelectionAction action, bool scrollIntoView)
        {
            _noSelectionChangeCount++;
            _noCurrentCellChangeCount++;
            try
            {
                int slot = -1;
                if (item is DataGridCollectionViewGroup group)
                {
                    DataGridRowGroupInfo groupInfo = RowGroupInfoFromCollectionViewGroup(group);
                    if (groupInfo != null)
                    {
                        slot = groupInfo.Slot;
                    }
                }
                else
                {
                    slot = SlotFromRowIndex(DataConnection.IndexOf(item));
                }
                if (slot == -1)
                {
                    slot = backupSlot;
                }
                if (slot < 0 || slot >= SlotCount)
                {
                    return;
                }

                columnIndex = CoerceColumnIndexToVisible(columnIndex);

                // The switch below is the only thing that touches selection now. It used to be preceded
                // by a parallel update of the selection model, because the grid and the model were two
                // separate stores that each had to be told; with one store the switch is sufficient.
                switch (action)
                {
                    case DataGridSelectionAction.AddCurrentToSelection:
                        SetRowSelection(slot, isSelected: true, setAnchorSlot: true);
                        break;
                    case DataGridSelectionAction.RemoveCurrentFromSelection:
                        SetRowSelection(slot, isSelected: false, setAnchorSlot: false);
                        break;
                    case DataGridSelectionAction.SelectFromAnchorToCurrent:
                        if (SelectionMode == DataGridSelectionMode.Extended && AnchorSlot != -1)
                        {
                            int anchorSlot = AnchorSlot;
                            if (slot <= anchorSlot)
                            {
                                SetRowsSelection(slot, anchorSlot);
                            }
                            else
                            {
                                SetRowsSelection(anchorSlot, slot);
                            }
                        }
                        else
                        {
                            goto case DataGridSelectionAction.SelectCurrent;
                        }
                        break;
                    case DataGridSelectionAction.SelectCurrent:
                        ClearRowSelection(slot, setAnchorSlot: true);
                        break;
                    case DataGridSelectionAction.None:
                        break;
                }

                bool currentCellChanged = CurrentSlot != slot || (CurrentColumnIndex != columnIndex && columnIndex != -1);
                int scrollColumnIndex = columnIndex;

                if (currentCellChanged)
                {
                    if (columnIndex == -1)
                    {
                        var fallbackColumnIndex = CoerceColumnIndexToVisible(CurrentColumnIndex);
                        if (fallbackColumnIndex != -1)
                        {
                            columnIndex = fallbackColumnIndex;
                        }
                        else
                        {
                            DataGridColumn firstVisibleColumn = ColumnsInternal.FirstVisibleNonFillerColumn;
                            if (firstVisibleColumn != null)
                            {
                                columnIndex = firstVisibleColumn.Index;
                            }
                        }
                    }
                    if (columnIndex != -1)
                    {
                        if (!SetCurrentCellCore(
                                columnIndex, slot,
                                commitEdit: true,
                                endRowEdit: SlotFromRowIndex(SelectedIndex) != slot))
                        {
                            return;
                        }
                    }

                    scrollColumnIndex = columnIndex;
                }

                if (scrollIntoView)
                {
                    if (scrollColumnIndex == -1)
                    {
                        scrollColumnIndex = CoerceColumnIndexToVisible(CurrentColumnIndex);
                        if (scrollColumnIndex == -1)
                        {
                            DataGridColumn firstVisibleColumn = ColumnsInternal.FirstVisibleNonFillerColumn;
                            if (firstVisibleColumn != null)
                            {
                                scrollColumnIndex = firstVisibleColumn.Index;
                            }
                        }
                    }

                    if (scrollColumnIndex != -1 &&
                        !TryExpandCollapsedSlotForScroll(slot))
                    {
                        return;
                    }

                    if (scrollColumnIndex != -1 &&
                        !ScrollSlotIntoView(
                            scrollColumnIndex, slot,
                            forCurrentCellChange: currentCellChanged,
                            forceHorizontalScroll: false))
                    {
                        return;
                    }
                }
                _successfullyUpdatedSelection = true;
            }
            finally
            {
                NoCurrentCellChangeCount--;
                NoSelectionChangeCount--;
            }
        }

        private int CoerceColumnIndexToVisible(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= ColumnsItemsInternal.Count)
            {
                return -1;
            }

            if (ColumnsItemsInternal[columnIndex].IsVisible)
            {
                return columnIndex;
            }

            var fallbackColumn = ColumnsInternal.FirstVisibleNonFillerColumn;
            return fallbackColumn?.Index ?? -1;
        }


        internal void RefreshVisibleSelection()
        {
            if (DisplayData == null)
            {
                return;
            }

            for (int slot = DisplayData.FirstScrollingSlot;
                slot > -1 && slot <= DisplayData.LastScrollingSlot;
                slot++)
            {
                var element = DisplayData.GetDisplayedElement(slot);
                if (element is DataGridRow row)
                {
                    row.ApplyState();
                    row.ApplyCellsState();
                }
                else if (element is DataGridRowGroupHeader groupHeader)
                {
                    groupHeader.UpdatePseudoClasses();
                }
            }
        }



        /// <summary>
        /// Brings the grid's derived state - the primary item, the row visuals - back in line with the
        /// selection. There is nothing to copy: the selection itself is already authoritative.
        /// </summary>
        internal void RefreshSelectionFromModel()
        {
            CoerceSelectedItem();
            RefreshVisibleSelection();
            RestoreCurrencyWithinBounds();
        }

        /// <summary>
        /// Brings currency back inside the grid when it has been left naming a slot that no longer
        /// exists.
        /// </summary>
        /// <remarks>
        /// Selection names items and so survives the rows being rebuilt on its own, but currency is
        /// a position and a position can be invalidated by rows going away. Nothing else will notice:
        /// the selection has no opinion about where the current cell is, so an out-of-range
        /// <see cref="CurrentSlot"/> would simply persist. It is put back onto a selected row when
        /// there is one, and cleared when there is not.
        /// </remarks>
        private void RestoreCurrencyWithinBounds()
        {
            if (CurrentSlot >= -1 && CurrentSlot < SlotCount)
            {
                return;
            }

            var slot = -1;
            foreach (var selectedSlot in GetSelectedSlots())
            {
                slot = selectedSlot;
                break;
            }

            if (slot < 0)
            {
                SetCurrentCellCore(-1, -1);
                return;
            }

            var columnIndex = CurrentColumnIndex != -1 ? CurrentColumnIndex : FirstDisplayedNonFillerColumnIndex;
            SetCurrentCellCore(columnIndex, slot);
        }

        internal bool UpdateSelectionAndCurrency(int columnIndex, int slot, DataGridSelectionAction action, bool scrollIntoView)
        {
            _successfullyUpdatedSelection = false;
            bool effectiveScrollIntoView = scrollIntoView || AutoScrollToSelectedItem;

            _noSelectionChangeCount++;
            _noCurrentCellChangeCount++;
            try
            {
                if (ColumnsInternal.RowGroupSpacerColumn.IsRepresented &&
                    columnIndex == ColumnsInternal.RowGroupSpacerColumn.Index)
                {
                    columnIndex = -1;
                }
                if (IsSlotOutOfSelectionBounds(slot) || (columnIndex != -1 && IsColumnOutOfBounds(columnIndex)))
                {
                    return false;
                }

                int newCurrentPosition = -1;
                object item = ItemFromSlot(slot, ref newCurrentPosition);

                if (EditingRow != null && slot != EditingRow.Slot && !CommitEdit(DataGridEditingUnit.Row, true))
                {
                    return false;
                }

                if (DataConnection.CollectionView != null &&
                    DataConnection.CollectionView.CurrentPosition != newCurrentPosition)
                {
                    DataConnection.MoveCurrentTo(item, slot, columnIndex, action, effectiveScrollIntoView);
                }
                else
                {
                    ProcessSelectionAndCurrency(columnIndex, item, slot, action, effectiveScrollIntoView);
                }
            }
            finally
            {
                NoCurrentCellChangeCount--;
                NoSelectionChangeCount--;
            }

            return _successfullyUpdatedSelection;
        }


        private void SetAndSelectCurrentCell(int columnIndex,
                                             int slot,
                                             bool forceCurrentCellSelection)
        {
            DataGridSelectionAction action = forceCurrentCellSelection ? DataGridSelectionAction.SelectCurrent : DataGridSelectionAction.None;
            UpdateSelectionAndCurrency(columnIndex, slot, action, scrollIntoView: false);
        }


        private void FlushSelectionChanged()
        {
            if (SelectionHasChanged && _noSelectionChangeCount == 0 && !_makeFirstDisplayedCellCurrentCellPending)
            {
                CoerceSelectedItem();
                if (AutoScrollToSelectedItem)
                {
                    RequestAutoScrollToSelection();
                }
                if (NoCurrentCellChangeCount != 0)
                {
                    // current cell is changing, don't raise SelectionChanged until it's done
                    return;
                }
                SelectionHasChanged = false;

                if (_flushCurrentCellChanged)
                {
                    FlushCurrentCellChanged();
                }

                if (_pendingSelectionAdded.Count > 0 || _pendingSelectionRemoved.Count > 0)
                {
                    var added = _pendingSelectionAdded.ToArray();
                    var removed = _pendingSelectionRemoved.ToArray();
                    _pendingSelectionAdded.Clear();
                    _pendingSelectionRemoved.Clear();

                    var e = new DataGridSelectionChangedEventArgs(
                        SelectionChangedEvent,
                        removed,
                        added,
                        CurrentSelectionChangeSource,
                        CurrentSelectionTriggerEvent);
                    ((RoutedEventArgs)e).Source = this;
                    OnSelectionChanged(e);
                }
            }
        }

        private void SetSelectedItemsCollection(IList value)
        {
            IList newValue = value ?? _selectedItemsView;
            IList oldValue = SelectedItems;

            if (ReferenceEquals(oldValue, newValue))
            {
                return;
            }

            DetachBoundSelectedItems();
            _selectedItemsBinding = ReferenceEquals(newValue, _selectedItemsView) ? null : newValue;
            AttachBoundSelectedItems();

            RaisePropertyChanged(SelectedItemsProperty, oldValue, SelectedItems);

            if (_selectedItemsBinding != null)
            {
                ApplySelectedItemsFromBinding(_selectedItemsBinding);
            }
        }

        private void AttachBoundSelectedItems()
        {
            if (_selectedItemsBinding is INotifyCollectionChanged incc)
            {
                _selectedItemsBindingNotifications = incc;
                WeakEventHandlerManager.Subscribe<INotifyCollectionChanged, NotifyCollectionChangedEventArgs, DataGrid>(
                    _selectedItemsBindingNotifications,
                    nameof(INotifyCollectionChanged.CollectionChanged),
                    OnBoundSelectedItemsCollectionChanged);
            }
        }

        private void DetachBoundSelectedItems()
        {
            if (_selectedItemsBindingNotifications != null)
            {
                WeakEventHandlerManager.Unsubscribe<NotifyCollectionChangedEventArgs, DataGrid>(
                    _selectedItemsBindingNotifications,
                    nameof(INotifyCollectionChanged.CollectionChanged),
                    OnBoundSelectedItemsCollectionChanged);
                _selectedItemsBindingNotifications = null;
            }
        }

        private void OnBoundSelectedItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncingSelectedItems || _selectedItemsBinding == null)
            {
                return;
            }

            using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.Programmatic);
            try
            {
                _syncingSelectedItems = true;
                ApplySelectedItemsChangeFromBinding(e);
            }
            finally
            {
                _syncingSelectedItems = false;
            }
        }


        /// <summary>
        /// Makes the selection match a bound SelectedItems collection.
        /// </summary>
        /// <remarks>
        /// A bound collection is the one place a second store legitimately exists - the consumer owns
        /// it - so this direction, and the mirror back in <see cref="ApplySelectionChangeToBinding"/>,
        /// keep the <c>_syncingSelectedItems</c> guard. Everything the grid itself owns now reads and
        /// writes one store and needs no such guard.
        /// </remarks>
        private void ApplySelectedItemsFromBinding(IList boundItems)
        {
            using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.Programmatic);
            bool previousSync = _syncingSelectedItems;
            _syncingSelectedItems = true;
            try
            {
                _selectionModel.SetSelectedItems(boundItems);
            }
            finally
            {
                _syncingSelectedItems = previousSync;
            }
        }

        private void ApplySelectedItemsChangeFromBinding(NotifyCollectionChangedEventArgs e)
        {
            if (_selectedItemsBinding == null)
            {
                return;
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Move:
                    // The order a consumer keeps its selected items in says nothing about which rows
                    // are selected.
                    break;
                case NotifyCollectionChangedAction.Add when !_selectionModel.SingleSelect:
                    using (_selectionModel.BatchUpdate())
                    {
                        foreach (object item in e.NewItems)
                        {
                            _selectionModel.Select(item);
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    using (_selectionModel.BatchUpdate())
                    {
                        foreach (object item in e.OldItems)
                        {
                            _selectionModel.Deselect(item);
                        }
                    }
                    break;
                default:
                    ApplySelectedItemsFromBinding(_selectedItemsBinding);
                    break;
            }
        }

        private void ApplySelectionChangeToBinding(DataGridSelectionModelChangedEventArgs e)
        {
            if (_selectedItemsBinding == null || _syncingSelectedItems)
            {
                return;
            }

            _syncingSelectedItems = true;
            try
            {
                foreach (var item in e.DeselectedItems)
                {
                    if (_selectedItemsBinding.Contains(item))
                    {
                        _selectedItemsBinding.Remove(item);
                    }
                }

                foreach (var item in e.SelectedItems)
                {
                    if (!_selectedItemsBinding.Contains(item))
                    {
                        _selectedItemsBinding.Add(item);
                    }
                }
            }
            finally
            {
                _syncingSelectedItems = false;
            }
        }

        private void SetSelectedCellsCollection(IList<DataGridCellInfo> value)
        {
            IList<DataGridCellInfo> newValue = value ?? (IList<DataGridCellInfo>)_selectedCellsView;
            IList<DataGridCellInfo> oldValue = SelectedCells;

            if (ReferenceEquals(oldValue, newValue))
            {
                return;
            }

            DetachBoundSelectedCells();
            _selectedCellsBinding = ReferenceEquals(newValue, _selectedCellsView) ? null : newValue;
            AttachBoundSelectedCells();

            RaisePropertyChanged(SelectedCellsProperty, oldValue, SelectedCells);

            if (_selectedCellsBinding != null)
            {
                ApplySelectedCellsFromBinding(_selectedCellsBinding);
            }
        }

        private void AttachBoundSelectedCells()
        {
            if (_selectedCellsBinding is INotifyCollectionChanged incc)
            {
                _selectedCellsBindingNotifications = incc;
                WeakEventHandlerManager.Subscribe<INotifyCollectionChanged, NotifyCollectionChangedEventArgs, DataGrid>(
                    _selectedCellsBindingNotifications,
                    nameof(INotifyCollectionChanged.CollectionChanged),
                    OnBoundSelectedCellsCollectionChanged);
            }
        }

        private void DetachBoundSelectedCells()
        {
            if (_selectedCellsBindingNotifications != null)
            {
                WeakEventHandlerManager.Unsubscribe<NotifyCollectionChangedEventArgs, DataGrid>(
                    _selectedCellsBindingNotifications,
                    nameof(INotifyCollectionChanged.CollectionChanged),
                    OnBoundSelectedCellsCollectionChanged);
                _selectedCellsBindingNotifications = null;
            }
        }

        private void OnBoundSelectedCellsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncingSelectedCells || _selectedCellsBinding == null)
            {
                return;
            }

            using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.Programmatic);
            try
            {
                _syncingSelectedCells = true;
                ApplySelectedCellsChangeFromBinding(e);
            }
            finally
            {
                _syncingSelectedCells = false;
            }
        }

        private void OnSelectedCellsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncingSelectedCells || _selectedCellsBinding == null)
            {
                return;
            }

            try
            {
                _syncingSelectedCells = true;
                ApplySelectedCellsChangeToBinding(e);
            }
            finally
            {
                _syncingSelectedCells = false;
            }
        }

        private void ApplySelectedCellsFromBinding(IList<DataGridCellInfo> boundCells)
        {
            bool previousSync = _syncingSelectedCells;
            _syncingSelectedCells = true;
            try
            {
                var removed = _selectedCellsView.ToList();
                ClearCellSelectionInternal(clearRows: true, raiseEvent: false);

                var added = new List<DataGridCellInfo>();
                foreach (var cell in boundCells)
                {
                    if (!TryNormalizeCell(cell, out var normalized))
                    {
                        continue;
                    }

                    if (AddCellSelectionInternal(normalized, added))
                    {
                        SetRowSelection(SlotFromRowIndex(normalized.RowIndex), isSelected: true, setAnchorSlot: false);
                    }
                }

                if (added.Count > 0 || removed.Count > 0)
                {
                    RaiseSelectedCellsChanged(added, removed);
                }
            }
            finally
            {
                _syncingSelectedCells = previousSync;
            }
        }

        private void ApplySelectedCellsChangeFromBinding(NotifyCollectionChangedEventArgs e)
        {
            if (ReferenceEquals(_selectedCellsBinding, _selectedCellsView))
            {
                return;
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    ApplySelectedCellsFromBinding(_selectedCellsBinding);
                    break;
                default:
                    ApplySelectedCellsFromBinding(_selectedCellsBinding);
                    break;
            }
        }

        private void ApplySelectedCellsChangeToBinding(NotifyCollectionChangedEventArgs e)
        {
            if (_selectedCellsBinding == null || ReferenceEquals(_selectedCellsBinding, _selectedCellsView))
            {
                return;
            }

            void CopyToBinding()
            {
                _selectedCellsBinding.Clear();
                foreach (var cell in _selectedCellsView)
                {
                    _selectedCellsBinding.Add(cell);
                }
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    CopyToBinding();
                    break;
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Move:
                    CopyToBinding();
                    break;
            }
        }

        private void SetSelectedColumnsCollection(IList<DataGridColumn> value)
        {
            IList<DataGridColumn> newValue = value ?? (IList<DataGridColumn>)_selectedColumnsView;
            IList<DataGridColumn> oldValue = SelectedColumns;

            if (ReferenceEquals(oldValue, newValue))
            {
                return;
            }

            DetachBoundSelectedColumns();
            _selectedColumnsBinding = ReferenceEquals(newValue, _selectedColumnsView) ? null : newValue;
            AttachBoundSelectedColumns();

            RaisePropertyChanged(SelectedColumnsProperty, oldValue, SelectedColumns);

            if (_selectedColumnsBinding != null)
            {
                ApplySelectedColumnsFromBinding(_selectedColumnsBinding);
            }
        }

        private void AttachBoundSelectedColumns()
        {
            if (_selectedColumnsBinding is INotifyCollectionChanged incc)
            {
                _selectedColumnsBindingNotifications = incc;
                WeakEventHandlerManager.Subscribe<INotifyCollectionChanged, NotifyCollectionChangedEventArgs, DataGrid>(
                    _selectedColumnsBindingNotifications,
                    nameof(INotifyCollectionChanged.CollectionChanged),
                    OnBoundSelectedColumnsCollectionChanged);
            }
        }

        private void DetachBoundSelectedColumns()
        {
            if (_selectedColumnsBindingNotifications != null)
            {
                WeakEventHandlerManager.Unsubscribe<NotifyCollectionChangedEventArgs, DataGrid>(
                    _selectedColumnsBindingNotifications,
                    nameof(INotifyCollectionChanged.CollectionChanged),
                    OnBoundSelectedColumnsCollectionChanged);
                _selectedColumnsBindingNotifications = null;
            }
        }

        private void OnBoundSelectedColumnsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncingSelectedColumns || _selectedColumnsBinding == null)
            {
                return;
            }

            using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.Programmatic);
            try
            {
                _syncingSelectedColumns = true;
                ApplySelectedColumnsChangeFromBinding(e);
            }
            finally
            {
                _syncingSelectedColumns = false;
            }
        }

        private void OnSelectedColumnsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncingSelectedColumns || _selectedColumnsBinding == null)
            {
                return;
            }

            try
            {
                _syncingSelectedColumns = true;
                ApplySelectedColumnsChangeToBinding(e);
            }
            finally
            {
                _syncingSelectedColumns = false;
            }
        }

        private void ApplySelectedColumnsFromBinding(IList<DataGridColumn> boundColumns)
        {
            bool previousSync = _syncingSelectedColumns;
            _syncingSelectedColumns = true;
            try
            {
                var removedColumns = _selectedColumnsView.ToList();
                var removedCells = _selectedCellsView.ToList();

                ClearCellSelectionInternal(clearRows: true, raiseEvent: false);

                var addedCells = new List<DataGridCellInfo>();
                var rowCount = DataConnection?.Count ?? 0;
                if (rowCount <= 0)
                {
                    if (removedColumns.Count > 0 || removedCells.Count > 0)
                    {
                        RaiseSelectedCellsChanged(Array.Empty<DataGridCellInfo>(), removedCells);
                        RaiseSelectedColumnsChanged(Array.Empty<DataGridColumn>(), removedColumns);
                    }

                    return;
                }

                foreach (var column in boundColumns)
                {
                    if (column == null)
                    {
                        continue;
                    }

                    var columnIndex = column.Index;
                    if (columnIndex < 0 || columnIndex >= ColumnsItemsInternal.Count)
                    {
                        continue;
                    }

                    SelectCellRangeInternal(0, rowCount - 1, columnIndex, columnIndex, addedCells);
                }

                if (addedCells.Count > 0 || removedCells.Count > 0)
                {
                    RaiseSelectedCellsChanged(addedCells, removedCells);
                }

                var newColumns = _selectedColumnsView.ToList();
                var removedSet = new HashSet<DataGridColumn>(removedColumns);
                var newSet = new HashSet<DataGridColumn>(newColumns);

                var addedColumns = new List<DataGridColumn>();
                foreach (var column in newColumns)
                {
                    if (!removedSet.Contains(column))
                    {
                        addedColumns.Add(column);
                    }
                }

                var removedDelta = new List<DataGridColumn>();
                foreach (var column in removedColumns)
                {
                    if (!newSet.Contains(column))
                    {
                        removedDelta.Add(column);
                    }
                }

                if (addedColumns.Count > 0 || removedDelta.Count > 0)
                {
                    RaiseSelectedColumnsChanged(addedColumns, removedDelta);
                }
            }
            finally
            {
                _syncingSelectedColumns = previousSync;
            }
        }

        private void ApplySelectedColumnsChangeFromBinding(NotifyCollectionChangedEventArgs e)
        {
            if (ReferenceEquals(_selectedColumnsBinding, _selectedColumnsView))
            {
                return;
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    ApplySelectedColumnsFromBinding(_selectedColumnsBinding);
                    break;
                default:
                    ApplySelectedColumnsFromBinding(_selectedColumnsBinding);
                    break;
            }
        }

        private void ApplySelectedColumnsChangeToBinding(NotifyCollectionChangedEventArgs e)
        {
            if (_selectedColumnsBinding == null || ReferenceEquals(_selectedColumnsBinding, _selectedColumnsView))
            {
                return;
            }

            void CopyToBinding()
            {
                _selectedColumnsBinding.Clear();
                foreach (var column in _selectedColumnsView)
                {
                    _selectedColumnsBinding.Add(column);
                }
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    CopyToBinding();
                    break;
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Move:
                    CopyToBinding();
                    break;
            }
        }

        private bool TryNormalizeCell(DataGridCellInfo cell, out DataGridCellInfo normalized)
        {
            normalized = default;

            if (cell.ColumnIndex < 0 ||
                cell.RowIndex < 0 ||
                ColumnsItemsInternal == null ||
                cell.ColumnIndex >= ColumnsItemsInternal.Count)
            {
                return false;
            }

            var column = ColumnsItemsInternal[cell.ColumnIndex];
            if (column == null || !column.IsVisible)
            {
                return false;
            }

            if (DataConnection == null || cell.RowIndex >= DataConnection.Count)
            {
                return false;
            }

            var item = DataConnection.GetDataItem(cell.RowIndex);
            normalized = new DataGridCellInfo(item, column, cell.RowIndex, cell.ColumnIndex, isValid: true);
            return true;
        }

        internal void RemapSelectedCellsToCurrentRows()
        {
            if (_selectedCellsView.Count == 0 || DataConnection == null)
            {
                return;
            }

            var remapped = new List<DataGridCellInfo>(_selectedCellsView.Count);
            var seen = new HashSet<(int RowIndex, int ColumnIndex)>();
            var resolvedRows = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            var rowsToSelect = new List<int>();
            var selectedRows = new HashSet<int>();
            var changed = false;

            foreach (var cell in _selectedCellsView)
            {
                if (!cell.IsValid || cell.Item == null)
                {
                    changed = true;
                    continue;
                }

                var columnIndex = cell.ColumnIndex;
                if (columnIndex < 0 && cell.Column != null)
                {
                    columnIndex = cell.Column.Index;
                }

                if (columnIndex < 0)
                {
                    changed = true;
                    continue;
                }

                var column = (ColumnsItemsInternal != null &&
                              columnIndex < ColumnsItemsInternal.Count)
                    ? ColumnsItemsInternal[columnIndex]
                    : cell.Column;
                if (column == null)
                {
                    changed = true;
                    continue;
                }

                if (!resolvedRows.TryGetValue(cell.Item, out var rowIndex))
                {
                    if (!TryGetRowIndexFromItem(cell.Item, out rowIndex))
                    {
                        changed = true;
                        continue;
                    }

                    resolvedRows[cell.Item] = rowIndex;
                }

                if (!seen.Add((rowIndex, columnIndex)))
                {
                    changed = true;
                    continue;
                }

                if (selectedRows.Add(rowIndex))
                {
                    rowsToSelect.Add(rowIndex);
                }

                if (rowIndex != cell.RowIndex || columnIndex != cell.ColumnIndex)
                {
                    changed = true;
                }

                remapped.Add(new DataGridCellInfo(cell.Item, column, rowIndex, columnIndex, isValid: true));
            }

            if (!changed && remapped.Count == _selectedCellsView.Count)
            {
                return;
            }

            var previousSync = _syncingSelectedCells;
            _syncingSelectedCells = true;
            try
            {
                var removed = _selectedCellsView.ToList();
                _selectedCells.Clear();
                _selectedCellsView.Clear();
                ClearHeaderSelectionTracking();
                _selectedColumnCounts.Clear();
                _selectedColumnIndices.Clear();
                _selectedColumnsView.Clear();

                var added = new List<DataGridCellInfo>(remapped.Count);
                foreach (var cell in remapped)
                {
                    AddCellSelectionInternal(cell, added);
                }

                foreach (var rowIndex in rowsToSelect)
                {
                    var slot = SlotFromRowIndex(rowIndex);
                    if (slot >= 0)
                    {
                        SetRowSelection(slot, isSelected: true, setAnchorSlot: false);
                    }
                }

                foreach (var rowIndex in _selectedCells.Keys)
                {
                    if (IsRowFullySelectedByCells(rowIndex))
                    {
                        _selectedRowHeaderIndices.Add(rowIndex);
                    }
                }

                if (added.Count > 0 || removed.Count > 0)
                {
                    RaiseSelectedCellsChanged(added, removed);
                }

                ApplySelectedCellsChangeToBinding(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
            finally
            {
                _syncingSelectedCells = previousSync;
            }
        }

        private void ClearCellSelectionInternal(bool clearRows, bool raiseEvent = true)
        {
            if (_selectedCells.Count == 0 &&
                _selectedCellsView.Count == 0 &&
                _selectedColumnCounts.Count == 0 &&
                _selectedColumnsView.Count == 0)
            {
                return;
            }

            var removed = _selectedCellsView.ToList();

            _selectedCells.Clear();
            _selectedCellsView.Clear();
            ClearHeaderSelectionTracking();
            ClearSelectedColumnsInternal(raiseEvent);

            if (clearRows && SelectionUnit != DataGridSelectionUnit.FullRow)
            {
                foreach (var cell in removed)
                {
                    int slot = SlotFromRowIndex(cell.RowIndex);
                    if (slot >= 0)
                    {
                        SetRowSelection(slot, isSelected: false, setAnchorSlot: false);
                    }
                }
            }

            if (raiseEvent)
            {
                RaiseSelectedCellsChanged(Array.Empty<DataGridCellInfo>(), removed);
            }
        }

        private bool AddCellSelectionInternal(DataGridCellInfo cell, List<DataGridCellInfo>? addedCollector = null)
        {
            if (!_selectedCells.TryGetValue(cell.RowIndex, out var columns))
            {
                columns = new HashSet<int>();
                _selectedCells[cell.RowIndex] = columns;
            }

            if (!columns.Add(cell.ColumnIndex))
            {
                return false;
            }

            _selectedCellsView.Add(cell);
            UpdateSelectedColumnCount(cell.ColumnIndex, delta: 1);

            addedCollector?.Add(cell);
            return true;
        }

        private bool RemoveCellSelectionInternal(int rowIndex, int columnIndex, List<DataGridCellInfo>? removedCollector = null)
        {
            if (!_selectedCells.TryGetValue(rowIndex, out var columns) || !columns.Remove(columnIndex))
            {
                return false;
            }

            if (columns.Count == 0)
            {
                _selectedCells.Remove(rowIndex);
            }

            var existing = _selectedCellsView.FirstOrDefault(x => x.RowIndex == rowIndex && x.ColumnIndex == columnIndex);
            if (existing.IsValid)
            {
                _selectedCellsView.Remove(existing);
                removedCollector?.Add(existing);
            }

            UpdateSelectedColumnCount(columnIndex, delta: -1);

            if (_selectedRowHeaderIndices.Contains(rowIndex) && !IsRowFullySelectedByCells(rowIndex))
            {
                _selectedRowHeaderIndices.Remove(rowIndex);
            }

            if (_selectedColumnHeaderIndices.Contains(columnIndex) && !IsColumnFullySelectedByCells(columnIndex))
            {
                _selectedColumnHeaderIndices.Remove(columnIndex);
            }

            return true;
        }

        private void RaiseSelectedCellsChanged(IReadOnlyList<DataGridCellInfo> addedCells, IReadOnlyList<DataGridCellInfo> removedCells)
        {
            if (addedCells.Count == 0 && removedCells.Count == 0)
            {
                return;
            }

            UpdateSelectionVisuals(addedCells);
            UpdateSelectionVisuals(removedCells);
            UpdateRowSelectionVisuals(addedCells);
            UpdateRowSelectionVisuals(removedCells);
            UpdateColumnHeaderSelectionVisuals(addedCells);
            UpdateColumnHeaderSelectionVisuals(removedCells);

            RequestSelectionOverlayRefresh();
            SelectedCellsChanged?.Invoke(this, new DataGridSelectedCellsChangedEventArgs(addedCells, removedCells));
        }

        private void RaiseSelectedColumnsChanged(IReadOnlyList<DataGridColumn> addedColumns, IReadOnlyList<DataGridColumn> removedColumns)
        {
            if (addedColumns.Count == 0 && removedColumns.Count == 0)
            {
                return;
            }

            SelectedColumnsChanged?.Invoke(this, new DataGridSelectedColumnsChangedEventArgs(addedColumns, removedColumns));
        }

        private void ClearSelectedColumnsInternal(bool raiseEvent)
        {
            if (_selectedColumnCounts.Count == 0 && _selectedColumnsView.Count == 0 && _selectedColumnIndices.Count == 0)
            {
                return;
            }

            var removed = _selectedColumnsView.ToList();
            if (removed.Count == 0 && _selectedColumnIndices.Count > 0)
            {
                foreach (var index in _selectedColumnIndices)
                {
                    var column = GetColumnForIndex(index);
                    if (column != null)
                    {
                        removed.Add(column);
                    }
                }
            }
            _selectedColumnCounts.Clear();
            _selectedColumnIndices.Clear();
            _selectedColumnHeaderIndices.Clear();
            _selectedColumnsView.Clear();

            foreach (var column in removed)
            {
                column?.HeaderCell?.UpdatePseudoClasses();
            }

            if (raiseEvent)
            {
                RaiseSelectedColumnsChanged(Array.Empty<DataGridColumn>(), removed);
            }
        }

        private void UpdateSelectedColumnCount(int columnIndex, int delta)
        {
            if (DataConnection == null)
            {
                return;
            }

            var rowCount = DataConnection.Count;
            if (rowCount <= 0)
            {
                return;
            }

            var nextCount = delta;
            if (_selectedColumnCounts.TryGetValue(columnIndex, out var current))
            {
                nextCount = current + delta;
            }

            if (nextCount <= 0)
            {
                _selectedColumnCounts.Remove(columnIndex);
            }
            else
            {
                _selectedColumnCounts[columnIndex] = nextCount;
            }

            var shouldSelect = nextCount >= rowCount;
            if (shouldSelect)
            {
                MarkColumnSelected(columnIndex);
            }
            else
            {
                MarkColumnUnselected(columnIndex);
            }
        }

        private void MarkColumnSelected(int columnIndex)
        {
            if (!_selectedColumnIndices.Add(columnIndex))
            {
                return;
            }

            var column = GetColumnForIndex(columnIndex);
            if (column == null)
            {
                return;
            }

            InsertSelectedColumn(column);

            if (!_syncingSelectedColumns)
            {
                RaiseSelectedColumnsChanged(new[] { column }, Array.Empty<DataGridColumn>());
            }

            column.HeaderCell?.UpdatePseudoClasses();
        }

        private void MarkColumnUnselected(int columnIndex)
        {
            if (!_selectedColumnIndices.Remove(columnIndex))
            {
                return;
            }

            var column = GetColumnForIndex(columnIndex);
            if (column == null)
            {
                return;
            }

            _selectedColumnsView.Remove(column);

            if (!_syncingSelectedColumns)
            {
                RaiseSelectedColumnsChanged(Array.Empty<DataGridColumn>(), new[] { column });
            }

            column.HeaderCell?.UpdatePseudoClasses();
        }

        private void InsertSelectedColumn(DataGridColumn column)
        {
            var insertIndex = 0;
            while (insertIndex < _selectedColumnsView.Count &&
                   _selectedColumnsView[insertIndex].Index < column.Index)
            {
                insertIndex++;
            }

            if (insertIndex >= _selectedColumnsView.Count)
            {
                _selectedColumnsView.Add(column);
            }
            else
            {
                _selectedColumnsView.Insert(insertIndex, column);
            }
        }

        private DataGridColumn GetColumnForIndex(int columnIndex)
        {
            if (ColumnsItemsInternal == null ||
                columnIndex < 0 ||
                columnIndex >= ColumnsItemsInternal.Count)
            {
                return null;
            }

            return ColumnsItemsInternal[columnIndex];
        }

        internal bool IsColumnSelected(DataGridColumn column)
        {
            if (column == null || SelectionUnit == DataGridSelectionUnit.FullRow)
            {
                return false;
            }

            return _selectedColumnIndices.Contains(column.Index);
        }

        internal bool IsColumnCurrent(DataGridColumn column)
        {
            if (column == null || SelectionUnit == DataGridSelectionUnit.FullRow)
            {
                return false;
            }

            return ReferenceEquals(CurrentColumn, column);
        }

        internal void RefreshSelectedColumnsFromCounts()
        {
            var rowCount = DataConnection?.Count ?? 0;
            if (rowCount <= 0)
            {
                ClearSelectedColumnsInternal(raiseEvent: true);
                return;
            }

            var selectedSnapshot = _selectedColumnIndices.ToList();
            foreach (var index in selectedSnapshot)
            {
                var count = _selectedColumnCounts.TryGetValue(index, out var stored) ? stored : 0;
                if (count < rowCount)
                {
                    MarkColumnUnselected(index);
                }
            }

            foreach (var pair in _selectedColumnCounts)
            {
                if (pair.Value >= rowCount)
                {
                    MarkColumnSelected(pair.Key);
                }
            }
        }

        private void UpdateSelectionVisuals(IReadOnlyList<DataGridCellInfo> cells)
        {
            if (cells.Count == 0 || DisplayData == null)
            {
                return;
            }

            foreach (var cell in cells)
            {
                if (!cell.IsValid)
                {
                    continue;
                }

                int slot = SlotFromRowIndex(cell.RowIndex);
                if (!IsSlotVisible(slot))
                {
                    continue;
                }

                if (DisplayData.GetDisplayedElement(slot) is DataGridRow row &&
                    cell.ColumnIndex >= 0 &&
                    cell.ColumnIndex < row.Cells.Count)
                {
                    row.Cells[cell.ColumnIndex].UpdatePseudoClasses();
                }
            }
        }

        private void UpdateRowSelectionVisuals(IReadOnlyList<DataGridCellInfo> cells)
        {
            if (cells.Count == 0 || DisplayData == null)
            {
                return;
            }

            HashSet<int>? updated = null;
            foreach (var cell in cells)
            {
                if (!cell.IsValid)
                {
                    continue;
                }

                int slot = SlotFromRowIndex(cell.RowIndex);
                if (!IsSlotVisible(slot))
                {
                    continue;
                }

                if (DisplayData.GetDisplayedElement(slot) is DataGridRow row)
                {
                    updated ??= new HashSet<int>();
                    if (!updated.Add(slot))
                    {
                        continue;
                    }

                    row.UpdateSelectionPseudoClasses();
                }
            }
        }

        private void UpdateColumnHeaderSelectionVisuals(IReadOnlyList<DataGridCellInfo> cells)
        {
            if (cells.Count == 0 || ColumnsItemsInternal == null)
            {
                return;
            }

            HashSet<int>? updated = null;
            foreach (var cell in cells)
            {
                if (!cell.IsValid)
                {
                    continue;
                }

                var columnIndex = cell.ColumnIndex;
                if (columnIndex < 0 || columnIndex >= ColumnsItemsInternal.Count)
                {
                    continue;
                }

                updated ??= new HashSet<int>();
                if (!updated.Add(columnIndex))
                {
                    continue;
                }

                ColumnsItemsInternal[columnIndex]?.HeaderCell?.UpdatePseudoClasses();
            }
        }

        internal bool IsCellSelected(int rowIndex, int columnIndex)
        {
            return _selectedCells.TryGetValue(rowIndex, out var columns) && columns.Contains(columnIndex);
        }

        internal bool IsRowFullySelected(int slot)
        {
            if (SelectionUnit == DataGridSelectionUnit.FullRow)
            {
                return GetRowSelection(slot);
            }

            if (slot < 0 || ColumnsItemsInternal == null || ColumnsItemsInternal.Count == 0)
            {
                return false;
            }

            if (!GetRowSelection(slot))
            {
                return false;
            }

            int rowIndex = RowIndexFromSlot(slot);
            if (rowIndex < 0)
            {
                return false;
            }

            if (!_selectedCells.TryGetValue(rowIndex, out var columns) || columns.Count == 0)
            {
                return SelectionUnit == DataGridSelectionUnit.CellOrRowHeader ||
                       SelectionUnit == DataGridSelectionUnit.CellOrRowOrColumnHeader;
            }

            int visibleColumnCount = GetVisibleSelectableColumnCount();
            return visibleColumnCount > 0 && columns.Count >= visibleColumnCount;
        }

        private int GetVisibleSelectableColumnCount()
        {
            if (ColumnsItemsInternal == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < ColumnsItemsInternal.Count; i++)
            {
                var column = ColumnsItemsInternal[i];
                if (column != null && column.IsVisible && column is not DataGridFillerColumn)
                {
                    count++;
                }
            }

            return count;
        }

        private void ClearHeaderSelectionTracking()
        {
            _selectedRowHeaderIndices.Clear();
            _selectedColumnHeaderIndices.Clear();
        }

        private void SetRowHeaderSelectionRange(int startRow, int endRow, bool append)
        {
            if (!append)
            {
                _selectedRowHeaderIndices.Clear();
            }

            if (DataConnection == null || startRow > endRow)
            {
                return;
            }

            var first = Math.Max(0, startRow);
            var last = Math.Min(endRow, DataConnection.Count - 1);
            for (var rowIndex = first; rowIndex <= last; rowIndex++)
            {
                _selectedRowHeaderIndices.Add(rowIndex);
            }
        }

        private void SetColumnHeaderSelectionRange(int startColumn, int endColumn, bool append)
        {
            if (!append)
            {
                _selectedColumnHeaderIndices.Clear();
            }

            if (ColumnsInternal == null)
            {
                return;
            }

            var first = Math.Min(startColumn, endColumn);
            var last = Math.Max(startColumn, endColumn);
            for (var displayIndex = first; displayIndex <= last; displayIndex++)
            {
                var column = ColumnsInternal.GetColumnAtDisplayIndex(displayIndex);
                if (column == null || column is DataGridFillerColumn)
                {
                    continue;
                }

                _selectedColumnHeaderIndices.Add(column.Index);
            }
        }

        private bool IsRowHeaderSelectionActive(int rowIndex) => _selectedRowHeaderIndices.Contains(rowIndex);

        private bool IsColumnHeaderSelectionActive(int columnIndex) => _selectedColumnHeaderIndices.Contains(columnIndex);

        private int GetColumnDisplayIndex(int columnIndex)
        {
            if (ColumnsItemsInternal == null || columnIndex < 0 || columnIndex >= ColumnsItemsInternal.Count)
            {
                return -1;
            }

            var column = ColumnsItemsInternal[columnIndex];
            return column?.DisplayIndex ?? -1;
        }

        private int GetColumnIndexFromDisplayIndex(int displayIndex)
        {
            if (ColumnsInternal == null || displayIndex < 0 || displayIndex >= ColumnsInternal.DisplayIndexMap.Count)
            {
                return -1;
            }

            var column = ColumnsInternal.GetColumnAtDisplayIndex(displayIndex);
            if (column == null || column is DataGridFillerColumn)
            {
                return -1;
            }

            return column.Index;
        }

        private bool TryGetSelectionDisplayIndexes(int anchorColumnIndex, int targetColumnIndex, out int anchorDisplayIndex, out int targetDisplayIndex)
        {
            anchorDisplayIndex = -1;
            targetDisplayIndex = -1;

            if (ColumnsItemsInternal == null ||
                anchorColumnIndex < 0 ||
                targetColumnIndex < 0 ||
                anchorColumnIndex >= ColumnsItemsInternal.Count ||
                targetColumnIndex >= ColumnsItemsInternal.Count)
            {
                return false;
            }

            anchorDisplayIndex = GetColumnDisplayIndex(anchorColumnIndex);
            targetDisplayIndex = GetColumnDisplayIndex(targetColumnIndex);

            if (anchorDisplayIndex >= 0 && targetDisplayIndex >= 0)
            {
                return true;
            }

            // Fallback to logical indexes if a display index cannot be resolved.
            anchorDisplayIndex = anchorColumnIndex;
            targetDisplayIndex = targetColumnIndex;
            return true;
        }

        private List<int> GetVisibleColumnIndexesInDisplayRange(int startDisplayIndex, int endDisplayIndex)
        {
            var result = new List<int>();
            var columnsInternal = ColumnsInternal;
            var columnsItems = ColumnsItemsInternal;
            if (columnsInternal == null || columnsItems == null || columnsItems.Count == 0)
            {
                return result;
            }

            var mapCount = columnsInternal.DisplayIndexMap.Count;
            if (mapCount <= 0)
            {
                return result;
            }

            var first = Math.Max(0, Math.Min(startDisplayIndex, endDisplayIndex));
            var last = Math.Min(mapCount - 1, Math.Max(startDisplayIndex, endDisplayIndex));
            if (first > last)
            {
                return result;
            }

            var capacity = Math.Max(0, last - first + 1);
            if (capacity > 0)
            {
                result.Capacity = capacity;
            }

            for (var displayIndex = first; displayIndex <= last; displayIndex++)
            {
                var column = columnsInternal.GetColumnAtDisplayIndex(displayIndex);
                if (column == null || column is DataGridFillerColumn || !column.IsVisible)
                {
                    continue;
                }

                var columnIndex = column.Index;
                if (columnIndex < 0 || columnIndex >= columnsItems.Count)
                {
                    continue;
                }

                result.Add(columnIndex);
            }

            return result;
        }

        private bool IsRowFullySelectedByCells(int rowIndex)
        {
            if (!_selectedCells.TryGetValue(rowIndex, out var columns) || columns.Count == 0)
            {
                return false;
            }

            var visibleColumnCount = GetVisibleSelectableColumnCount();
            return visibleColumnCount > 0 && columns.Count >= visibleColumnCount;
        }

        private bool IsColumnFullySelectedByCells(int columnIndex)
        {
            if (DataConnection == null)
            {
                return false;
            }

            return _selectedColumnCounts.TryGetValue(columnIndex, out var count) && count >= DataConnection.Count;
        }

        internal bool AllowsRowHeaderSelection =>
            CanUserSelectRows &&
            (SelectionUnit == DataGridSelectionUnit.FullRow ||
             SelectionUnit == DataGridSelectionUnit.CellOrRowHeader ||
             SelectionUnit == DataGridSelectionUnit.CellOrRowOrColumnHeader);

        internal bool AllowsColumnHeaderSelection =>
            CanUserSelectColumns &&
            (SelectionUnit == DataGridSelectionUnit.CellOrColumnHeader ||
             SelectionUnit == DataGridSelectionUnit.CellOrRowOrColumnHeader);

        internal bool GetCellSelectionFromSlot(int slot, int columnIndex)
        {
            if (SelectionUnit == DataGridSelectionUnit.FullRow)
            {
                return GetRowSelection(slot);
            }

            int rowIndex = RowIndexFromSlot(slot);
            if (rowIndex < 0)
            {
                return false;
            }

            return IsCellSelected(rowIndex, columnIndex);
        }

        public void SelectAllCells()
        {
            if (DataConnection == null || ColumnsInternal == null)
            {
                return;
            }

            using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.Command);
            var removed = _selectedCellsView.ToList();
            ClearCellSelectionInternal(clearRows: true, raiseEvent: false);

            var added = new List<DataGridCellInfo>();
            var visibleColumns = ColumnsInternal.GetVisibleColumns().ToList();

            for (int rowIndex = 0; rowIndex < DataConnection.Count; rowIndex++)
            {
                int slot = SlotFromRowIndex(rowIndex);
                if (slot < 0 || IsGroupSlot(slot))
                {
                    continue;
                }

                foreach (var column in visibleColumns)
                {
                    var cell = new DataGridCellInfo(
                        DataConnection.GetDataItem(rowIndex),
                        column,
                        rowIndex,
                        column.Index,
                        isValid: true);
                    AddCellSelectionInternal(cell, added);
                }

                SetRowSelection(slot, isSelected: true, setAnchorSlot: false);
            }

            if (visibleColumns.Count > 0 && DataConnection.Count > 0)
            {
                _cellAnchor = new DataGridCellCoordinates(visibleColumns[0].Index, SlotFromRowIndex(0));
            }

            RaiseSelectedCellsChanged(added, removed);
            _successfullyUpdatedSelection = true;
        }

        private void AddSingleCellSelection(int columnIndex, int slot, List<DataGridCellInfo> addedCollector)
        {
            if (DataConnection == null)
            {
                return;
            }

            int rowIndex = RowIndexFromSlot(slot);
            if (rowIndex < 0 || columnIndex < 0 || columnIndex >= ColumnsItemsInternal.Count)
            {
                return;
            }

            var column = ColumnsItemsInternal[columnIndex];
            if (column == null || !column.IsVisible)
            {
                return;
            }

            var item = DataConnection.GetDataItem(rowIndex);
            var cell = new DataGridCellInfo(item, column, rowIndex, columnIndex, isValid: true);

            if (AddCellSelectionInternal(cell, addedCollector))
            {
                _cellAnchor = new DataGridCellCoordinates(columnIndex, slot);
                SetRowSelection(slot, isSelected: true, setAnchorSlot: false);
            }
        }

        private void RemoveCellSelectionFromSlot(int slot, int columnIndex, List<DataGridCellInfo> removedCollector)
        {
            int rowIndex = RowIndexFromSlot(slot);
            if (rowIndex < 0)
            {
                return;
            }

            if (RemoveCellSelectionInternal(rowIndex, columnIndex, removedCollector))
            {
                if (!_selectedCells.TryGetValue(rowIndex, out var remaining) || remaining.Count == 0)
                {
                    SetRowSelection(slot, isSelected: false, setAnchorSlot: false);
                }
            }
        }

        private void SelectCellRangeInternal(int startRowIndex, int endRowIndex, int startColumnIndex, int endColumnIndex, List<DataGridCellInfo> addedCollector)
        {
            if (DataConnection == null || ColumnsItemsInternal == null || startRowIndex > endRowIndex || startColumnIndex > endColumnIndex)
            {
                return;
            }

            var columnsItems = ColumnsItemsInternal;
            var rowCount = DataConnection.Count;
            if (rowCount <= 0 || columnsItems.Count == 0)
            {
                return;
            }

            var firstRow = Math.Max(0, startRowIndex);
            var lastRow = Math.Min(endRowIndex, rowCount - 1);
            if (firstRow > lastRow)
            {
                return;
            }

            var firstColumn = Math.Max(0, startColumnIndex);
            var lastColumn = Math.Min(endColumnIndex, columnsItems.Count - 1);
            if (firstColumn > lastColumn)
            {
                return;
            }

            for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
            {
                var slot = SlotFromRowIndex(rowIndex);
                if (slot < 0 || IsGroupSlot(slot))
                {
                    continue;
                }

                var item = DataConnection.GetDataItem(rowIndex);
                for (var columnIndex = firstColumn; columnIndex <= lastColumn; columnIndex++)
                {
                    var column = columnsItems[columnIndex];
                    if (column == null || !column.IsVisible)
                    {
                        continue;
                    }

                    var cell = new DataGridCellInfo(item, column, rowIndex, columnIndex, isValid: true);
                    AddCellSelectionInternal(cell, addedCollector);
                }

                SetRowSelection(slot, isSelected: true, setAnchorSlot: false);
            }
        }

        private void SelectCellRangeByDisplayIndexInternal(int startRowIndex, int endRowIndex, int startDisplayIndex, int endDisplayIndex, List<DataGridCellInfo> addedCollector)
        {
            if (DataConnection == null || ColumnsItemsInternal == null || startRowIndex > endRowIndex)
            {
                return;
            }

            var columnsItems = ColumnsItemsInternal;
            var rowCount = DataConnection.Count;
            if (rowCount <= 0 || columnsItems.Count == 0)
            {
                return;
            }

            var columnIndexes = GetVisibleColumnIndexesInDisplayRange(startDisplayIndex, endDisplayIndex);
            if (columnIndexes.Count == 0)
            {
                return;
            }

            var firstRow = Math.Max(0, startRowIndex);
            var lastRow = Math.Min(endRowIndex, rowCount - 1);
            if (firstRow > lastRow)
            {
                return;
            }

            for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
            {
                var slot = SlotFromRowIndex(rowIndex);
                if (slot < 0 || IsGroupSlot(slot))
                {
                    continue;
                }

                var item = DataConnection.GetDataItem(rowIndex);
                foreach (var columnIndex in columnIndexes)
                {
                    var column = columnsItems[columnIndex];
                    if (column == null || !column.IsVisible)
                    {
                        continue;
                    }

                    var cell = new DataGridCellInfo(item, column, rowIndex, columnIndex, isValid: true);
                    AddCellSelectionInternal(cell, addedCollector);
                }

                SetRowSelection(slot, isSelected: true, setAnchorSlot: false);
            }
        }

        private void NormalizeBoundSelectionForSingleMode()
        {
            if (_selectedItemsBinding == null)
            {
                return;
            }

            _selectedItemsBinding.Clear();
            if (_selectionModel.Count > 0)
            {
                _selectedItemsBinding.Add(_selectionModel.SelectedItems[0]);
            }
        }


        /// <summary>
        /// call when: selection changes or SelectedItems object changes
        /// </summary>
        internal void CoerceSelectedItem()
        {
            object selectedItem = null;

            if (SelectionMode == DataGridSelectionMode.Extended &&
                CurrentSlot != -1 &&
                GetRowSelection(CurrentSlot))
            {
                selectedItem = CurrentItem;
            }
            else if (_selectionModel != null)
            {
                selectedItem = _selectionModel.SelectedItem;
            }

            SetValueNoCallback(SelectedItemProperty, ProjectSelectionItem(selectedItem));

            // Update the SelectedIndex
            int newIndex = -1;

            if (selectedItem != null)
            {
                if (!TryGetRowIndexFromItem(selectedItem, out newIndex))
                {
                    newIndex = -1;
                }
            }

            SetValueNoCallback(SelectedIndexProperty, newIndex);
        }


        internal IEnumerable<object> GetSelectionInclusive(int startRowIndex, int endRowIndex)
        {
            int startSlot = SlotFromRowIndex(startRowIndex);
            int endSlot = SlotFromRowIndex(endRowIndex);
            foreach (int slot in GetSelectedSlots())
            {
                if (slot < startSlot)
                {
                    continue;
                }

                if (slot > endSlot)
                {
                    break;
                }

                yield return DataConnection.GetDataItem(RowIndexFromSlot(slot));
            }
        }


        /// <summary>
        /// Raises the SelectionChanged event and clears the _selectionChanged.
        /// This event won't get raised again until after _selectionChanged is set back to true.
        /// </summary>
        protected virtual void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            using var activity = DataGridDiagnostics.SelectionChanged();
            using var _ = DataGridDiagnostics.BeginSelectionChanged();

            var dataGridArgs = e as DataGridSelectionChangedEventArgs;
            if (activity != null)
            {
                activity.SetTag(DataGridDiagnostics.Tags.AddedCount, e.AddedItems?.Count ?? 0);
                activity.SetTag(DataGridDiagnostics.Tags.RemovedCount, e.RemovedItems?.Count ?? 0);
                if (dataGridArgs != null)
                {
                    activity.SetTag(DataGridDiagnostics.Tags.SelectionSource, dataGridArgs.Source.ToString());
                    activity.SetTag(DataGridDiagnostics.Tags.UserInitiated, dataGridArgs.IsUserInitiated);
                }
            }

            DataGridDiagnostics.RecordSelectionChanged(dataGridArgs?.Source ?? DataGridSelectionChangeSource.Unknown);
            RaiseEvent(e);
        }

        private int _noSelectionChangeCount;

        private bool _successfullyUpdatedSelection;


        /// <summary>
        /// Occurs when the <see cref="P:Avalonia.Controls.DataGrid.SelectedItem" /> or
        /// <see cref="P:Avalonia.Controls.DataGrid.SelectedItems" /> property value changes.
        /// </summary>
        public event EventHandler<SelectionChangedEventArgs> SelectionChanged
        {
            add { AddHandler(SelectionChangedEvent, value); }
            remove { RemoveHandler(SelectionChangedEvent, value); }
        }


        private int NoSelectionChangeCount
        {
            get
            {
                return _noSelectionChangeCount;
            }
            set
            {
                _noSelectionChangeCount = value;
                if (value == 0)
                {
                    FlushSelectionChanged();
                }
            }
        }


        // This flag indicates whether selection has actually changed during a selection operation,
        // and exists to ensure that FlushSelectionChanged doesn't unnecessarily raise SelectionChanged.
        internal bool SelectionHasChanged
        {
            get;
            set;
        }


        internal int AnchorSlot
        {
            get;
            private set;
        }


        private void OnSelectedIndexChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (!_areHandlersSuspended)
            {
                using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.Programmatic);
                int index = (int)e.NewValue;

                // GetDataItem returns null if index is >= Count, we do not check newValue
                // against Count here to avoid enumerating through an Enumerable twice
                // Setting SelectedItem coerces the finally value of the SelectedIndex
                object newSelectedItem = (index < 0) ? null : DataConnection.GetDataItem(index);
                var projectedItem = ProjectSelectionItem(newSelectedItem);
                SelectedItem = projectedItem;
                if (!Equals(SelectedItem, projectedItem))
                {
                    SetValueNoCallback(SelectedIndexProperty, (int)e.OldValue);
                }
            }
        }

        private void OnSelectedItemChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (!_areHandlersSuspended)
            {
                using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.Programmatic);
                var normalizedItem = ProjectSelectionItem(e.NewValue);
                var normalizedOld = ProjectSelectionItem(e.OldValue);
                if (!Equals(normalizedItem, e.NewValue))
                {
                    SetValueNoCallback(SelectedItemProperty, normalizedItem);
                }

                int selectionIndex = (normalizedItem == null) ? -1 : GetSelectionModelIndexOfItem(normalizedItem);
                if (selectionIndex == -1 && normalizedItem != null)
                {
                    if (TryAutoExpandSelectionItem(normalizedItem))
                    {
                        selectionIndex = GetSelectionModelIndexOfItem(normalizedItem);
                    }
                }
                if (selectionIndex == -1)
                {
                    // If the Item is null or it's not found, clear the Selection
                    if (!CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
                    {
                        // Edited value couldn't be committed or aborted
                        SetValueNoCallback(SelectedItemProperty, normalizedOld);
                        return;
                    }

                    // Clear all row selections
                    ClearRowSelection(resetAnchorSlot: true);

                    if (DataConnection.CollectionView != null)
                    {
                        DataConnection.CollectionView.MoveCurrentTo(null);
                    }
                }
                else
                {
                    int slot = SlotFromSelectionIndex(selectionIndex);
                    if (slot == -1)
                    {
                        SetValueNoCallback(SelectedIndexProperty, selectionIndex);
                        return;
                    }
                    if (slot != CurrentSlot)
                    {
                        if (!CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
                        {
                            // Edited value couldn't be committed or aborted
                            SetValueNoCallback(SelectedItemProperty, normalizedOld);
                            return;
                        }
                        if (slot >= SlotCount || slot < -1)
                        {
                            if (DataConnection.CollectionView != null)
                            {
                                int moveIndex = RowIndexFromSlot(slot);
                                DataConnection.CollectionView.MoveCurrentToPosition(moveIndex);
                            }
                        }
                    }

                    int oldSelectedIndex = SelectedIndex;
                    SetValueNoCallback(SelectedIndexProperty, selectionIndex);
                    try
                    {
                        _noSelectionChangeCount++;
                        int columnIndex = CurrentColumnIndex;

                        if (columnIndex == -1)
                        {
                            columnIndex = FirstDisplayedNonFillerColumnIndex;
                        }
                        if (IsSlotOutOfSelectionBounds(slot))
                        {
                            ClearRowSelection(slotException: slot, setAnchorSlot: true);
                            return;
                        }

                        UpdateSelectionAndCurrency(columnIndex, slot, DataGridSelectionAction.SelectCurrent, scrollIntoView: false);
                    }
                    finally
                    {
                        NoSelectionChangeCount--;
                    }

                    if (!_successfullyUpdatedSelection)
                    {
                        SetValueNoCallback(SelectedIndexProperty, oldSelectedIndex);
                        SetValueNoCallback(SelectedItemProperty, normalizedOld);
                    }
                    else
                    {
                        RequestAutoScrollToSelection();
                    }
                }
            }
        }

        private bool TryAutoExpandSelectionItem(object item)
        {
            if (!AutoExpandSelectedItem || !_hierarchicalRowsEnabled || _hierarchicalModel == null)
            {
                return false;
            }

            if (_autoExpandingSelection)
            {
                return false;
            }

            if (IsHierarchicalItemVisible(item))
            {
                return false;
            }

            if (_hierarchicalModel is Avalonia.Controls.DataGridHierarchical.IHierarchicalModelExpander expander)
            {
                _autoExpandingSelection = true;
                try
                {
                    return expander.TryExpandToItem(item, out _);
                }
                finally
                {
                    _autoExpandingSelection = false;
                }
            }

            return false;
        }

        private bool IsHierarchicalItemVisible(object item)
        {
            if (_hierarchicalModel == null)
            {
                return false;
            }

            foreach (var node in _hierarchicalModel.Flattened)
            {
                if (ReferenceEquals(node, item) || ReferenceEquals(node.Item, item))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnSelectionModeChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (!_areHandlersSuspended)
            {
                using var _ = BeginSelectionChangeScope(DataGridSelectionChangeSource.Programmatic);

                // Noted so that attaching can tell which side moved last; see
                // AttachSelectionModelHandlers.
                if (_externalSubscriptionsDetached)
                {
                    _selectionModeSetWhileDetached = true;
                }

                ClearRowSelection(resetAnchorSlot: true);
                if (_selectionModel != null)
                {
                    _selectionModel.SingleSelect = SelectionMode == DataGridSelectionMode.Single;
                }
            }
        }

        private void OnSelectionUnitChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_areHandlersSuspended)
            {
                return;
            }

            var newValue = (DataGridSelectionUnit)e.NewValue;
            if (newValue == DataGridSelectionUnit.FullRow)
            {
                ClearCellSelectionInternal(clearRows: false);
            }
            else
            {
                ClearRowSelection(resetAnchorSlot: true);
                _cellAnchor = new DataGridCellCoordinates(-1, -1);
            }

            RefreshVisibleSelection();
            RequestSelectionOverlayRefresh();
        }

        private void OnAutoScrollToSelectedItemChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_areHandlersSuspended)
            {
                return;
            }

            if (AutoScrollToSelectedItem)
            {
                RequestAutoScrollToSelection();
            }
            else
            {
                _autoScrollPending = false;
            }
        }

        private void OnAutoExpandSelectedItemChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_areHandlersSuspended)
            {
                return;
            }

            if (!AutoExpandSelectedItem)
            {
                return;
            }

            var current = ProjectSelectionItem(SelectedItem);
            if (current != null)
            {
                TryAutoExpandSelectionItem(current);
            }
        }

        private void RequestAutoScrollToSelection()
        {
            if (_autoScrollPending || !AutoScrollToSelectedItem)
            {
                return;
            }

            _autoScrollPending = true;

            if (!IsAttachedToVisualTree || _rowsPresenter == null)
            {
                return;
            }

            ScheduleAutoScrollToSelection();
        }

        private void TryExecutePendingAutoScroll()
        {
            if (!_autoScrollPending || !AutoScrollToSelectedItem)
            {
                return;
            }

            if (!IsAttachedToVisualTree || _rowsPresenter == null)
            {
                return;
            }

            ScheduleAutoScrollToSelection();
        }

        private void ScheduleAutoScrollToSelection()
        {
            var token = ++_autoScrollRequestToken;
            Dispatcher.UIThread.Post(_ => PerformAutoScrollToSelection(token), DispatcherPriority.Background);
        }

        private void PerformAutoScrollToSelection(int token)
        {
            if (token != _autoScrollRequestToken)
            {
                return;
            }

            _autoScrollPending = false;

            if (!AutoScrollToSelectedItem || !IsAttachedToVisualTree || _rowsPresenter == null)
            {
                return;
            }

            if (!TryGetAutoScrollTarget(out var item, out var column))
            {
                return;
            }

            if (!TryAutoScrollSelectionTarget(item, column))
            {
                ScrollIntoView(item, column);
            }

            ComputeScrollBarsLayout();

            if (UseLogicalScrollable && _rowsPresenter != null)
            {
                _rowsPresenter.SyncOffset(HorizontalOffset, GetVerticalOffset());
                _rowsPresenter.RaiseScrollInvalidated(EventArgs.Empty);
            }
        }

        private bool TryGetAutoScrollTarget(out object item, out DataGridColumn column)
        {
            item = null;
            column = null;

            if (DisplayData == null || ColumnsInternal == null)
            {
                return false;
            }

            if (CurrentSlot != -1 && GetRowSelection(CurrentSlot))
            {
                item = CurrentItem;
            }
            else
            {
                item = SelectedItem;
            }

            if (item == null || DataConnection == null || !TryGetRowIndexFromItem(item, out _))
            {
                return false;
            }

            column = CurrentColumn;

            if (column == null || !column.IsVisible)
            {
                column = ColumnsInternal.FirstVisibleNonFillerColumn;
            }

            return true;
        }

        private bool TryAutoScrollSelectionTarget(object item, DataGridColumn column)
        {
            if (column == null ||
                !column.IsVisible ||
                !TryGetRowIndexFromItem(item, out var rowIndex))
            {
                return false;
            }

            int slot = SlotFromRowIndex(rowIndex);
            if (slot < 0 || IsSlotOutOfBounds(slot))
            {
                return false;
            }

            if (!TryExpandCollapsedSlotForScroll(slot))
            {
                return false;
            }

            return ScrollSlotIntoView(
                column.Index,
                slot,
                forCurrentCellChange: false,
                forceHorizontalScroll: false);
        }

        private bool TryExpandCollapsedSlotForScroll(int slot)
        {
            if (!_collapsedSlotsTable.Contains(slot))
            {
                return true;
            }

            int previousGroupSlot = RowGroupHeadersTable.GetPreviousIndex(slot);
            if (previousGroupSlot < 0)
            {
                return false;
            }

            DataGridRowGroupInfo rowGroupInfo = RowGroupHeadersTable.GetValueAt(previousGroupSlot);
            if (rowGroupInfo == null)
            {
                return false;
            }

            ExpandRowGroupParentChain(rowGroupInfo.Level, rowGroupInfo.Slot);

            // Mirror ScrollIntoView behavior after expanding collapsed parents.
            NegVerticalOffset = 0;
            SetVerticalOffset(0);
            ResetDisplayedRows();
            DisplayData.FirstScrollingSlot = 0;
            ComputeScrollBarsLayout();

            return !_collapsedSlotsTable.Contains(slot);
        }

        private void CancelPendingAutoScroll()
        {
            if (_autoScrollPending)
            {
                _autoScrollPending = false;
            }

            _autoScrollRequestToken++;
        }

    }
}
