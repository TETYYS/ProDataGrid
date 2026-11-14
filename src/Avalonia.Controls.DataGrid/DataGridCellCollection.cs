// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Avalonia.Controls
{
    /// <summary>
    /// The cells of a <see cref="DataGridRow"/>, in column order.
    /// </summary>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    sealed class DataGridCellCollection
    {
        private List<DataGridCell> _cells;
        private DataGridRow _owningRow;

        internal event EventHandler<DataGridCellEventArgs>? CellAdded;
        internal event EventHandler<DataGridCellEventArgs>? CellRemoved;

        internal DataGridCellCollection(DataGridRow owningRow)
        {
            _owningRow = owningRow;
            _cells = new List<DataGridCell>();
        }

        /// <summary>
        /// Gets the number of cells in the row.
        /// </summary>
        public int Count
        {
            get
            {
                return _cells.Count;
            }
        }

        /// <summary>
        /// Returns an enumerator over the cells. A struct enumerator, so <c>foreach</c> does not allocate.
        /// </summary>
        public List<DataGridCell>.Enumerator GetEnumerator()
        {
            return _cells.GetEnumerator();
        }

        internal void Insert(int cellIndex, DataGridCell cell)
        {
            Debug.Assert(cellIndex >= 0 && cellIndex <= _cells.Count);

            cell.OwningRow = _owningRow;
            _cells.Insert(cellIndex, cell);

            CellAdded?.Invoke(this, new DataGridCellEventArgs(cell));
        }

        internal void RemoveAt(int cellIndex)
        {
            DataGridCell dataGridCell = _cells[cellIndex];
            _cells.RemoveAt(cellIndex);
            dataGridCell.OwningRow = null;
            CellRemoved?.Invoke(this, new DataGridCellEventArgs(dataGridCell));
        }

        /// <summary>
        /// Gets the cell at <paramref name="index"/>.
        /// </summary>
        public DataGridCell this[int index]
        {
            get
            {
                if (index < 0 || index >= _cells.Count)
                {
                    throw DataGridError.DataGrid.ValueMustBeBetween("index", "Index", 0, true, _cells.Count, false);
                }
                return _cells[index];
            }
        }
    }
}
