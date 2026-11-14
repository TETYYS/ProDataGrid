// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.Controls
{
    /// <summary>
    /// Default implementation of <see cref="IDataGridRowHeightEstimator"/> that uses
    /// a simple average-based estimation algorithm. This matches the original DataGrid behavior.
    /// </summary>
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    class DefaultRowHeightEstimator : IDataGridRowHeightEstimator, IDataGridRowHeightEstimatorStateful
    {
        private const double DefaultHeight = 22.0;
        private const int MaxRowGroupLevels = 10;

        private double _defaultRowHeight = DefaultHeight;
        private double _rowHeightEstimate = DefaultHeight;
        private double _rowDetailsHeightEstimate;
        private double[] _rowGroupHeaderHeightsByLevel;
        private int _lastEstimatedRow = -1;
        private int _totalItemCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultRowHeightEstimator"/> class.
        /// </summary>
        public DefaultRowHeightEstimator()
        {
            _rowGroupHeaderHeightsByLevel = new double[MaxRowGroupLevels];
            for (int i = 0; i < MaxRowGroupLevels; i++)
            {
                _rowGroupHeaderHeightsByLevel[i] = DefaultHeight;
            }
        }

        /// <inheritdoc/>
        public double DefaultRowHeight
        {
            get => _defaultRowHeight;
            set
            {
                _defaultRowHeight = value;
                if (_rowHeightEstimate == DefaultHeight)
                {
                    _rowHeightEstimate = value;
                }
            }
        }

        /// <inheritdoc/>
        public double RowHeightEstimate => _rowHeightEstimate;

        /// <inheritdoc/>
        public double RowDetailsHeightEstimate => _rowDetailsHeightEstimate;

        /// <inheritdoc/>
        public double GetRowGroupHeaderHeightEstimate(int level)
        {
            if (level < 0 || level >= MaxRowGroupLevels)
                return DefaultHeight;
            return _rowGroupHeaderHeightsByLevel[level];
        }

        /// <inheritdoc/>
        public void RecordMeasuredHeight(int slot, double measuredHeight, bool hasDetails = false, double detailsHeight = 0)
        {
            // The default implementation doesn't cache individual heights,
            // it only updates the estimate when UpdateFromDisplayedRows is called.
            
            if (hasDetails && detailsHeight > 0)
            {
                _rowDetailsHeightEstimate = detailsHeight;
            }
        }

        /// <inheritdoc/>
        public void RecordRowGroupHeaderHeight(int slot, int level, double measuredHeight)
        {
            if (level >= 0 && level < MaxRowGroupLevels)
            {
                _rowGroupHeaderHeightsByLevel[level] = measuredHeight;
            }
        }

        /// <inheritdoc/>
        public double GetEstimatedHeight(int slot, bool isRowGroupHeader = false, int rowGroupLevel = 0, bool hasDetails = false)
        {
            if (isRowGroupHeader)
            {
                return GetRowGroupHeaderHeightEstimate(rowGroupLevel);
            }

            double height = _rowHeightEstimate;
            if (hasDetails)
            {
                height += _rowDetailsHeightEstimate;
            }
            return height;
        }

        /// <inheritdoc/>
        public double CalculateTotalHeight(int totalSlotCount, int collapsedSlotCount, int[] rowGroupHeaderCounts, int detailsVisibleCount)
        {
            if (totalSlotCount <= 0)
                return 0;

            int visibleSlotCount = totalSlotCount - collapsedSlotCount;
            
            // Calculate row group header heights
            double headerHeight = 0;
            int totalHeaderCount = 0;
            if (rowGroupHeaderCounts != null)
            {
                for (int i = 0; i < rowGroupHeaderCounts.Length && i < MaxRowGroupLevels; i++)
                {
                    headerHeight += rowGroupHeaderCounts[i] * _rowGroupHeaderHeightsByLevel[i];
                    totalHeaderCount += rowGroupHeaderCounts[i];
                }
            }

            // Calculate regular row heights
            int regularRowCount = visibleSlotCount - totalHeaderCount;
            double rowHeight = regularRowCount * _rowHeightEstimate;

            // Add details heights
            double detailsHeight = detailsVisibleCount * _rowDetailsHeightEstimate;

            return headerHeight + rowHeight + detailsHeight;
        }

        /// <inheritdoc/>
        public int EstimateSlotAtOffset(double verticalOffset, int totalSlotCount)
        {
            if (verticalOffset <= 0 || totalSlotCount <= 0)
                return 0;

            double singleRowHeight = _rowHeightEstimate;
            if (singleRowHeight <= 0)
                singleRowHeight = DefaultHeight;

            int estimatedSlot = (int)(verticalOffset / singleRowHeight);
            return Math.Min(Math.Max(0, estimatedSlot), totalSlotCount - 1);
        }

        /// <inheritdoc/>
        public double EstimateOffsetToSlot(int slot)
        {
            if (slot <= 0)
                return 0;

            return slot * _rowHeightEstimate;
        }

        /// <inheritdoc/>
        public void UpdateFromDisplayedRows(int firstDisplayedSlot, int lastDisplayedSlot, double[] displayedHeights, double verticalOffset, double negVerticalOffset, int collapsedSlotCount, int detailsCount)
        {
            if (displayedHeights == null || displayedHeights.Length == 0)
                return;

            // Note: Individual height recording happens before this call via RecordMeasuredHeight
            // for each displayed element. The displayedHeights array is only used for reference here.
            // Collapsed slots are skipped in the displayed elements, so we cannot assume contiguous slots.

            // Only update estimate when we've scrolled to or past our previous position
            // Original DataGrid uses >= not > to allow updates when staying at same position
            if (lastDisplayedSlot >= _lastEstimatedRow)
            {
                _lastEstimatedRow = lastDisplayedSlot;

                // Calculate total height of all rows up to and including displayed
                // This matches the original: totalRowsHeight = _verticalOffset - NegVerticalOffset + displayedHeights
                double totalHeight = verticalOffset - negVerticalOffset;
                for (int i = 0; i < displayedHeights.Length; i++)
                {
                    totalHeight += displayedHeights[i];
                }

                // Original logic subtracts details heights before calculating the estimate
                // This ensures the estimate represents row heights WITHOUT details
                totalHeight -= detailsCount * _rowDetailsHeightEstimate;

                // Original logic: RowHeightEstimate = totalRowsHeight / visibleCount
                // where visibleCount = _lastEstimatedRow + 1 - collapsedCount
                if (_lastEstimatedRow >= 0)
                {
                    int visibleCount = _lastEstimatedRow + 1 - collapsedSlotCount;
                    if (visibleCount > 0)
                    {
                        _rowHeightEstimate = totalHeight / visibleCount;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _rowHeightEstimate = _defaultRowHeight;
            _rowDetailsHeightEstimate = 0;
            _lastEstimatedRow = -1;

            for (int i = 0; i < MaxRowGroupLevels; i++)
            {
                _rowGroupHeaderHeightsByLevel[i] = DefaultHeight;
            }
        }

        /// <inheritdoc/>
        public void OnDataSourceChanged(int newItemCount)
        {
            _totalItemCount = newItemCount;
            Reset();
        }

        /// <inheritdoc/>
        public void OnItemsInserted(int startIndex, int count)
        {
            _totalItemCount += count;
            // Reset estimate tracking if items were inserted before our measured range
            if (startIndex <= _lastEstimatedRow)
            {
                _lastEstimatedRow = -1;
            }
        }

        /// <inheritdoc/>
        public void OnItemsRemoved(int startIndex, int count)
        {
            _totalItemCount = Math.Max(0, _totalItemCount - count);
            // Reset estimate tracking if items were removed from our measured range
            if (startIndex <= _lastEstimatedRow)
            {
                _lastEstimatedRow = Math.Max(-1, startIndex - 1);
            }
        }

        /// <inheritdoc/>
        public void OnItemsMoved(int oldStartIndex, int newStartIndex, int count)
        {
            if (count <= 0 || oldStartIndex == newStartIndex)
            {
                return;
            }

            // The item count is the same, so only the running estimate is affected, and only from
            // the first row that moved onwards.
            int firstMoved = Math.Min(oldStartIndex, newStartIndex);
            if (firstMoved <= _lastEstimatedRow)
            {
                _lastEstimatedRow = Math.Max(-1, firstMoved - 1);
            }
        }

        /// <inheritdoc/>
        public RowHeightEstimatorState CaptureState()
        {
            return new DefaultState(
                _defaultRowHeight,
                _rowHeightEstimate,
                _rowDetailsHeightEstimate,
                (double[])_rowGroupHeaderHeightsByLevel.Clone(),
                _lastEstimatedRow,
                _totalItemCount);
        }

        /// <inheritdoc/>
        public bool TryRestoreState(RowHeightEstimatorState state)
        {
            if (state is not DefaultState snapshot)
            {
                return false;
            }

            _defaultRowHeight = snapshot.DefaultRowHeight;
            _rowHeightEstimate = snapshot.RowHeightEstimate;
            _rowDetailsHeightEstimate = snapshot.RowDetailsHeightEstimate;
            _lastEstimatedRow = snapshot.LastEstimatedRow;
            _totalItemCount = snapshot.TotalItemCount;

            if (snapshot.RowGroupHeaderHeightsByLevel != null)
            {
                var length = Math.Min(snapshot.RowGroupHeaderHeightsByLevel.Length, MaxRowGroupLevels);
                for (int i = 0; i < MaxRowGroupLevels; i++)
                {
                    _rowGroupHeaderHeightsByLevel[i] = i < length
                        ? snapshot.RowGroupHeaderHeightsByLevel[i]
                        : DefaultHeight;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public RowHeightEstimatorDiagnostics GetDiagnostics()
        {
            return new RowHeightEstimatorDiagnostics
            {
                AlgorithmName = "Default (Average-based)",
                CurrentRowHeightEstimate = _rowHeightEstimate,
                CachedHeightCount = 0, // Default doesn't cache individual heights
                TotalRowCount = _totalItemCount,
                EstimatedTotalHeight = _totalItemCount * _rowHeightEstimate,
                MinMeasuredHeight = _rowHeightEstimate,
                MaxMeasuredHeight = _rowHeightEstimate,
                AverageMeasuredHeight = _rowHeightEstimate,
                AdditionalInfo = $"LastEstimatedRow: {_lastEstimatedRow}, RowGroupLevels: {string.Join(", ", _rowGroupHeaderHeightsByLevel.Take(3).Select(h => h.ToString("F1")))}"
            };
        }

        private sealed class DefaultState : RowHeightEstimatorState
        {
            public DefaultState(
                double defaultRowHeight,
                double rowHeightEstimate,
                double rowDetailsHeightEstimate,
                double[] rowGroupHeaderHeightsByLevel,
                int lastEstimatedRow,
                int totalItemCount)
                : base(nameof(DefaultRowHeightEstimator))
            {
                DefaultRowHeight = defaultRowHeight;
                RowHeightEstimate = rowHeightEstimate;
                RowDetailsHeightEstimate = rowDetailsHeightEstimate;
                RowGroupHeaderHeightsByLevel = rowGroupHeaderHeightsByLevel;
                LastEstimatedRow = lastEstimatedRow;
                TotalItemCount = totalItemCount;
            }

            public double DefaultRowHeight { get; }
            public double RowHeightEstimate { get; }
            public double RowDetailsHeightEstimate { get; }
            public double[] RowGroupHeaderHeightsByLevel { get; }
            public int LastEstimatedRow { get; }
            public int TotalItemCount { get; }
        }
    }
}
