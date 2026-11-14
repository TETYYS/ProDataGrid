// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

#nullable disable

using Avalonia.Interactivity;
using System;
using System.Collections.Generic;

namespace Avalonia.Controls
{
#if !DATAGRID_INTERNAL
public
#else
internal
#endif
    partial class DataGrid
    {
        private bool ApplyCellSelectionRange(DataGridCellRange range, bool append, DataGridSelectionChangeSource source, RoutedEventArgs triggerEventArgs)
        {
            if (DataConnection == null)
            {
                return false;
            }

            using var _ = BeginSelectionChangeScope(source, triggerEventArgs);
            var proposed = BuildCellSelectionProposal(
                append,
                range.StartRow,
                range.EndRow,
                range.StartColumn,
                range.EndColumn,
                columnIndexesAreDisplayIndexes: false);
            int proposedAnchorSlot = SlotFromRowIndex(range.StartRow);
            var proposedCurrentCell = CreateCellInfo(range.StartColumn, proposedAnchorSlot);
            if (!TryPreviewCellSelection(
                    proposed,
                    proposedCurrentCell,
                    CreateAnchorInfo(proposedAnchorSlot, range.StartColumn)))
            {
                return false;
            }

            using var commit = BeginSelectionCommit();
            if (EditingRow != null && proposedAnchorSlot != EditingRow.Slot &&
                !CommitEdit(DataGridEditingUnit.Row, true))
            {
                return false;
            }

            using var columnsDelta = BeginSelectedColumnsDelta();
            var added = new List<DataGridCellInfo>();
            var removed = new List<DataGridCellInfo>();

            if (!append)
            {
                if (_selectedCellsView.Count > 0)
                {
                    removed.AddRange(_selectedCellsView);
                }

                ClearCellSelectionInternal(clearRows: true, raiseEvent: false);
            }

            SelectCellRangeInternal(range.StartRow, range.EndRow, range.StartColumn, range.EndColumn, added);

            if (added.Count > 0 || removed.Count > 0)
            {
                RaiseSelectedCellsChanged(added, removed);
            }

            var anchorSlot = SlotFromRowIndex(range.StartRow);
            if (anchorSlot >= 0 && !IsGroupSlot(anchorSlot))
            {
                _cellAnchor = new DataGridCellCoordinates(range.StartColumn, anchorSlot);
            }

            if (triggerEventArgs?.Source is not DataGridColumnHeader)
            {
                _columnHeaderAnchorIndex = range.StartColumn;
            }

            _successfullyUpdatedSelection = true;
            return true;
        }

        private bool ApplyColumnHeaderSelectionRange(int startDisplayIndex, int endDisplayIndex, bool append, DataGridSelectionChangeSource source, RoutedEventArgs triggerEventArgs)
        {
            if (DataConnection == null || ColumnsInternal == null)
            {
                return false;
            }

            using var _ = BeginSelectionChangeScope(source, triggerEventArgs);
            var proposed = BuildCellSelectionProposal(
                append,
                0,
                DataConnection.Count - 1,
                startDisplayIndex,
                endDisplayIndex,
                columnIndexesAreDisplayIndexes: true);
            int proposedColumnIndex = GetColumnIndexFromDisplayIndex(Math.Min(startDisplayIndex, endDisplayIndex));
            int proposedAnchorSlot = SlotFromRowIndex(0);
            if (!TryPreviewCellSelection(
                    proposed,
                    CreateCellInfo(proposedColumnIndex, proposedAnchorSlot),
                    CreateAnchorInfo(proposedAnchorSlot, proposedColumnIndex)))
            {
                return false;
            }

            using var commit = BeginSelectionCommit();
            if (EditingRow != null && proposedAnchorSlot != EditingRow.Slot &&
                !CommitEdit(DataGridEditingUnit.Row, true))
            {
                return false;
            }

            using var columnsDelta = BeginSelectedColumnsDelta();
            var added = new List<DataGridCellInfo>();
            var removed = new List<DataGridCellInfo>();

            if (!append)
            {
                if (_selectedCellsView.Count > 0)
                {
                    removed.AddRange(_selectedCellsView);
                }

                ClearCellSelectionInternal(clearRows: true, raiseEvent: false);
            }

            var first = Math.Min(startDisplayIndex, endDisplayIndex);
            var last = Math.Max(startDisplayIndex, endDisplayIndex);
            for (var displayIndex = first; displayIndex <= last; displayIndex++)
            {
                var column = ColumnsInternal.GetColumnAtDisplayIndex(displayIndex);
                if (column == null || column is DataGridFillerColumn)
                {
                    continue;
                }

                SelectCellRangeInternal(0, DataConnection.Count - 1, column.Index, column.Index, added);
            }

            if (added.Count > 0 || removed.Count > 0)
            {
                RaiseSelectedCellsChanged(added, removed);
            }

            _successfullyUpdatedSelection = true;
            return true;
        }
    }
}
