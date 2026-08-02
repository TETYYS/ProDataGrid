// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

#nullable disable

using Avalonia.Collections;
using Avalonia.Controls.Utils;
using Avalonia.Controls.Selection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;

namespace Avalonia.Controls
{
    /// <summary>
    /// Data source management
    /// </summary>
#if !DATAGRID_INTERNAL
public
#else
internal
#endif
    partial class DataGrid
    {

        /// <summary>
        /// ItemsSourceProperty property changed handler.
        /// </summary>
        /// <param name="e">The event arguments.</param>
        private void OnItemsSourcePropertyChanged(AvaloniaPropertyChangedEventArgs e)
        {
            using var selectionScope = BeginSelectionChangeScope(DataGridSelectionChangeSource.ItemsSourceChange, sticky: true);

            _pendingGroupingState = null;

            var oldValue = (IEnumerable)e.OldValue;
            var newItemsSource = (IEnumerable)e.NewValue;
            var switchingFromOwnedHierarchical = ReferenceEquals(oldValue, _hierarchicalItemsSource) && _ownsHierarchicalItemsSource && !ReferenceEquals(oldValue, newItemsSource);

            _ownsHierarchicalItemsSource = ReferenceEquals(newItemsSource, _hierarchicalItemsSource);
            if (!_ownsHierarchicalItemsSource && !ReferenceEquals(newItemsSource, _hierarchicalItemsSource))
            {
                _hierarchicalItemsSource = null;
            }

            if (!_areHandlersSuspended)
            {
                Debug.Assert(DataConnection != null);

                var oldCollectionView = DataConnection.CollectionView;

                if (LoadingOrUnloadingRow)
                {
                    SetValueNoCallback(ItemsSourceProperty, oldValue);
                    throw DataGridError.DataGrid.CannotChangeItemsWhenLoadingRows();
                }

                // Try to commit edit on the old DataSource, but force a cancel if it fails
                if (!CommitEdit())
                {
                    CancelEdit(DataGridEditingUnit.Row, false);
                }

                DataConnection.UnWireEvents(DataConnection.DataSource);
                DataConnection.ClearDataProperties();
                ClearRowGroupHeadersTable();
                DataConnection.DataSource = null;

                // Wrap an IEnumerable in an ICollectionView if it's not already one
                bool setDefaultSelection = false;
                if (newItemsSource is IDataGridCollectionView newCollectionView)
                {
                    setDefaultSelection = true;
                }
                else
                {
                    newCollectionView =  newItemsSource is not null
                        ? DataGridDataConnection.CreateView(newItemsSource)
                        : default;
                }

                DataConnection.DataSource = newCollectionView;

                if (oldCollectionView != DataConnection.CollectionView)
                {
                    RaisePropertyChanged(CollectionViewProperty,
                        oldCollectionView,
                        newCollectionView);
                }

                UpdateSortingAdapterView();
                UpdateFilteringAdapterView();
                UpdateSearchAdapterView();
                UpdateConditionalFormattingAdapterView();

                if (DataConnection.DataSource != null)
                {
                    // Setup the column headers
                    if (DataConnection.DataType != null)
                    {
                        foreach (var column in ColumnsInternal.GetDisplayedColumns())
                        {
                            if (column is DataGridBoundColumn boundColumn)
                            {
                                boundColumn.SetHeaderFromBinding();
                            }
                        }
                    }
                    DataConnection.WireEvents(DataConnection.DataSource);
                }

                UpdateSelectionModelSource();

                // The selection is a set of items, so swapping the view neither invalidates nor
                // reorders it - there is nothing to snapshot and nothing to remap. Only items the new
                // source does not contain have to go, which keeps a swap to an equivalent view (a
                // re-wrapped DataGridCollectionView over the same data) fully selection-preserving.
                DropSelectionForRemovedItems();

                var modelSelectionPending = _selectionModel is { Count: > 0 };

                // Wait for the current cell to be set before we raise any SelectionChanged events
                _makeFirstDisplayedCellCurrentCellPending = true;

                ClearRows(false); //recycle
                RemoveAutoGeneratedColumns();

                // Notify the estimator about the data source change
                RowHeightEstimator?.OnDataSourceChanged(DataConnection.Count);

                // Set the SlotCount (from the data count and number of row group headers) before we make the default selection
                PopulateRowGroupHeadersTable();

                if (!modelSelectionPending)
                {
                    SelectedItem = null;
                    if (DataConnection.CollectionView != null && setDefaultSelection)
                    {
                        SelectedItem = ProjectSelectionItem(DataConnection.CollectionView.CurrentItem);
                    }

                    if (_selectedItemsBinding != null && _selectedItemsBinding.Count > 0)
                    {
                        ApplySelectedItemsFromBinding(_selectedItemsBinding);
                    }
                }
                else
                {
                    CoerceSelectedItem();
                    RefreshVisibleSelection();
                }

                // Treat this like the DataGrid has never been measured because all calculations at
                // this point are invalid until the next layout cycle.  For instance, the ItemsSource
                // can be set when the DataGrid is not part of the visual tree
                _measured = false;
                InvalidateMeasure();

                UpdatePseudoClasses();
                OnDataSourceChangedForSummaries();
                OnDataSourceChangedForValidation();
            }
        }


        /// <summary>
        /// Membership test for the underlying data, ignoring whether the current filter, page or
        /// expansion state lets an item through, so that a selected row taken out of sight is hidden
        /// rather than deselected.
        /// </summary>
        /// <remarks>
        /// Each view answers this itself, because only it knows what its source holds beyond what it
        /// shows. Falling back to <see cref="DataGridSelection.IDataGridSelectionView.IndexOf"/> for a
        /// view that does not is a last resort and reads hidden as removed.
        /// </remarks>
        private Func<object, bool> SnapshotSelectionSourceMembership()
        {
            if (_selectionView is DataGridSelection.IDataGridSelectionSourceMembership source)
            {
                var contains = source.SnapshotSourceMembership();
                return item => contains(item);
            }

            var view = _selectionView;
            return item => (view?.IndexOf(item) ?? -1) >= 0;
        }

        /// <summary>
        /// Deselects items the underlying data no longer contains, after a change that did not say
        /// which ones went (a Reset).
        /// </summary>
        internal void DropSelectionForRemovedItems()
        {
            if (_selectionModel is not { Count: > 0 })
            {
                return;
            }

            _selectionModel.RetainOnly(SnapshotSelectionSourceMembership());
        }

        internal void RefreshRowsAndColumns(bool clearRows)
        {
            using var activity = DataGridDiagnostics.RefreshRowsAndColumns();
            using var _ = DataGridDiagnostics.BeginDataGridRefresh();
            activity?.SetTag(DataGridDiagnostics.Tags.ClearRows, clearRows);
            activity?.SetTag(DataGridDiagnostics.Tags.AutoGenerateColumns, AutoGenerateColumns);
            activity?.SetTag(DataGridDiagnostics.Tags.Columns, ColumnsItemsInternal.Count);
            activity?.SetTag(DataGridDiagnostics.Tags.Rows, DataConnection?.Count ?? 0);
            activity?.SetTag(DataGridDiagnostics.Tags.SlotCount, SlotCount);

            if (_measured)
            {
                try
                {
                    _noCurrentCellChangeCount++;

                    if (clearRows)
                    {
                        ClearRows(false);
                        ClearRowGroupHeadersTable();
                        PopulateRowGroupHeadersTable();
                    }
                    if (AutoGenerateColumns)
                    {
                        //Column auto-generation refreshes the rows too
                        AutoGenerateColumnsPrivate();
                    }
                    foreach (DataGridColumn column in ColumnsItemsInternal)
                    {
                        //We don't need to refresh the state of AutoGenerated column headers because they're up-to-date
                        if (!column.IsAutoGenerated && column.HasHeaderCell)
                        {
                            column.HeaderCell.UpdatePseudoClasses();
                        }
                    }

                    RefreshRows(recycleRows: false, clearRows: false);

                    if (ColumnDefinitions.Count > 0 && CurrentColumnIndex == -1)
                    {
                        MakeFirstDisplayedCellCurrentCell();
                    }
                    else
                    {
                        _makeFirstDisplayedCellCurrentCellPending = false;
                        _desiredCurrentColumnIndex = -1;
                        FlushCurrentCellChanged();
                    }
                }
                finally
                {
                    NoCurrentCellChangeCount--;
                }
            }
            else
            {
                if (clearRows)
                {
                    ClearRows(recycle: false);
                }
                ClearRowGroupHeadersTable();
                PopulateRowGroupHeadersTable();
            }

            RequestPointerOverRefresh();

            activity?.SetTag(DataGridDiagnostics.Tags.FirstDisplayedSlot, DisplayData.FirstScrollingSlot);
            activity?.SetTag(DataGridDiagnostics.Tags.LastDisplayedSlot, DisplayData.LastScrollingSlot);
            activity?.SetTag(DataGridDiagnostics.Tags.DisplayedSlots, DisplayData.NumDisplayedScrollingElements);
        }


        internal void UpdateStateOnCurrentChanged(object currentItem, int currentPosition)
        {
            using var selectionScope = BeginSelectionChangeScope(DataGridSelectionChangeSource.ItemsSourceChange);

            var currentSelectionIndex = currentPosition;
            if (_selectionModel != null && TryGetPagingInfo(out _, out var pageStart))
            {
                currentSelectionIndex = pageStart + currentPosition;
            }

            if (currentItem == CurrentItem && currentItem == SelectedItem && currentSelectionIndex == SelectedIndex)
            {
                // The DataGrid's CurrentItem is already up-to-date, so we don't need to do anything
                return;
            }

            int columnIndex = CurrentColumnIndex;
            if (columnIndex == -1)
            {
                if (IsColumnOutOfBounds(_desiredCurrentColumnIndex) ||
                    (ColumnsInternal.RowGroupSpacerColumn.IsRepresented && _desiredCurrentColumnIndex == ColumnsInternal.RowGroupSpacerColumn.Index))
                {
                    columnIndex = FirstDisplayedNonFillerColumnIndex;
                }
                else
                {
                    columnIndex = _desiredCurrentColumnIndex;
                }
            }
            _desiredCurrentColumnIndex = -1;

            int slot = currentItem != null ? SlotFromSelectionIndex(currentSelectionIndex) : -1;
            bool currentInSelection = currentItem != null &&
                slot >= 0 &&
                GetRowSelection(slot);

            if (currentItem != null && (slot < 0 || slot >= SlotCount))
            {
                ClearRowSelection(true);
                SetCurrentCellCore(-1, -1);
                return;
            }

            if (_selectionModel is { Count: > 0 } && !currentInSelection)
            {
                RefreshSelectionFromModel();
                return;
            }

            try
            {
                _noSelectionChangeCount++;
                _noCurrentCellChangeCount++;

                if (!CommitEdit())
                {
                    CancelEdit(DataGridEditingUnit.Row, false);
                }

                if (currentItem == null)
                {
                    ClearRowSelection(true);
                    SetCurrentCellCore(-1, -1);
                }
                else if (currentInSelection)
                {
                    ProcessSelectionAndCurrency(columnIndex, currentItem, slot, DataGridSelectionAction.None, false);
                }
                else
                {
                    ClearRowSelection(true);
                    ProcessSelectionAndCurrency(columnIndex, currentItem, slot, DataGridSelectionAction.SelectCurrent, false);
                }
            }
            finally
            {
                NoCurrentCellChangeCount--;
                NoSelectionChangeCount--;
            }
        }


        // Returns the item or the CollectionViewGroup that is used as the DataContext for a given slot.
        // If the DataContext is an item, rowIndex is set to the index of the item within the collection
        internal object ItemFromSlot(int slot, ref int rowIndex)
        {
            if (IsGroupSlot(slot))
            {
                var info = RowGroupHeadersTable.GetValueAt(slot) ?? RowGroupFootersTable.GetValueAt(slot);
                return info?.CollectionViewGroup;
            }
            else
            {
                rowIndex = RowIndexFromSlot(slot);
                return DataConnection.GetDataItem(rowIndex);
            }
        }


        private void ColumnsInternal_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnColumnsInternalBindingChanged(e);
            OnColumnsChangedForValidation();

            if (e.Action == NotifyCollectionChangedAction.Add
                || e.Action == NotifyCollectionChangedAction.Remove
                || e.Action == NotifyCollectionChangedAction.Reset)
            {
                UpdatePseudoClasses();
                UpdateSearchAdapterView();
            }
        }

    }
}
