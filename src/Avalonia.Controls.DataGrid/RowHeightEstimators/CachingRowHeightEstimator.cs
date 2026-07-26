// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.Controls
{
    /// <summary>
    /// Advanced implementation of <see cref="IDataGridRowHeightEstimator"/> that caches
    /// individual row heights for more accurate scrolling with variable height rows.
    /// </summary>
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    class CachingRowHeightEstimator : IDataGridRowHeightEstimator, IDataGridRowHeightEstimatorStateful
    {
        private const double DefaultHeight = 22.0;
        private const int MaxRowGroupLevels = 10;
        private const int MaxCacheSize = 10000; // Limit memory usage

        private double _defaultRowHeight = DefaultHeight;
        private double _rowHeightEstimate = DefaultHeight;
        private double _rowDetailsHeightEstimate;
        private double[] _rowGroupHeaderHeightsByLevel;
        private int _totalItemCount;

        // Cache for individual row heights
        private readonly Dictionary<int, double> _measuredHeights = new();
        private readonly Dictionary<int, double> _detailsHeights = new();
        
        // Running statistics for estimation
        private double _sumMeasuredHeights;
        private int _measuredCount;
        private double _minMeasuredHeight = double.MaxValue;
        private double _maxMeasuredHeight = double.MinValue;

        // Collapsed slot tracking from DataGrid
        private int _lastCollapsedSlotCount;
        private int _lastDetailsCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="CachingRowHeightEstimator"/> class.
        /// </summary>
        public CachingRowHeightEstimator()
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
                if (_measuredCount == 0)
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
            // Manage cache size
            if (_measuredHeights.Count >= MaxCacheSize && !_measuredHeights.ContainsKey(slot))
            {
                // Simple eviction: remove oldest entries (lowest slot numbers when scrolling down)
                // This is a simple heuristic; more sophisticated LRU could be implemented
                TrimCache();
            }

            bool isNew = !_measuredHeights.ContainsKey(slot);
            double oldHeight = isNew ? 0 : _measuredHeights[slot];

            _measuredHeights[slot] = measuredHeight;

            // Update statistics
            if (isNew)
            {
                _sumMeasuredHeights += measuredHeight;
                _measuredCount++;
            }
            else
            {
                _sumMeasuredHeights = _sumMeasuredHeights - oldHeight + measuredHeight;
            }

            _minMeasuredHeight = Math.Min(_minMeasuredHeight, measuredHeight);
            _maxMeasuredHeight = Math.Max(_maxMeasuredHeight, measuredHeight);

            // Update running estimate
            if (_measuredCount > 0)
            {
                _rowHeightEstimate = _sumMeasuredHeights / _measuredCount;
            }

            // Handle details
            if (hasDetails && detailsHeight > 0)
            {
                _detailsHeights[slot] = detailsHeight;
                _rowDetailsHeightEstimate = detailsHeight;
            }
            else if (_detailsHeights.ContainsKey(slot))
            {
                _detailsHeights.Remove(slot);
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

            // Return cached height if available
            if (_measuredHeights.TryGetValue(slot, out double cachedHeight))
            {
                double height = cachedHeight;
                if (hasDetails && _detailsHeights.TryGetValue(slot, out double detailsHeight))
                {
                    height += detailsHeight;
                }
                else if (hasDetails)
                {
                    height += _rowDetailsHeightEstimate;
                }
                return height;
            }

            // Otherwise return estimate
            double estimatedHeight = _rowHeightEstimate;
            if (hasDetails)
            {
                estimatedHeight += _rowDetailsHeightEstimate;
            }
            return estimatedHeight;
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

            // Calculate regular row heights using cached values where available
            int regularRowCount = visibleSlotCount - totalHeaderCount;
            double cachedTotal = 0;
            int cachedCount = 0;

            foreach (var kvp in _measuredHeights)
            {
                if (kvp.Key < totalSlotCount)
                {
                    cachedTotal += kvp.Value;
                    cachedCount++;
                }
            }

            // Combine cached and estimated heights
            int uncachedCount = regularRowCount - cachedCount;
            double rowHeight = cachedTotal + (uncachedCount * _rowHeightEstimate);

            // Add details heights (use cached where available)
            double detailsHeight = 0;
            foreach (var kvp in _detailsHeights)
            {
                if (kvp.Key < totalSlotCount)
                {
                    detailsHeight += kvp.Value;
                    detailsVisibleCount--;
                }
            }
            detailsHeight += Math.Max(0, detailsVisibleCount) * _rowDetailsHeightEstimate;

            return headerHeight + rowHeight + detailsHeight;
        }

        /// <inheritdoc/>
        public int EstimateSlotAtOffset(double verticalOffset, int totalSlotCount)
        {
            if (verticalOffset <= 0 || totalSlotCount <= 0)
                return 0;

            // Use cached heights for more accurate estimation
            double accumulatedHeight = 0;
            int slot = 0;

            // Walk through cached heights
            var sortedSlots = _measuredHeights.Keys.OrderBy(k => k).ToList();
            int cacheIndex = 0;

            while (slot < totalSlotCount && accumulatedHeight < verticalOffset)
            {
                if (cacheIndex < sortedSlots.Count && sortedSlots[cacheIndex] == slot)
                {
                    accumulatedHeight += _measuredHeights[slot];
                    cacheIndex++;
                }
                else
                {
                    accumulatedHeight += _rowHeightEstimate;
                }
                slot++;
            }

            return Math.Min(Math.Max(0, slot - 1), totalSlotCount - 1);
        }

        /// <inheritdoc/>
        public double EstimateOffsetToSlot(int slot)
        {
            if (slot <= 0)
                return 0;

            double offset = 0;

            // Use cached heights for accurate calculation
            for (int i = 0; i < slot; i++)
            {
                if (_measuredHeights.TryGetValue(i, out double height))
                {
                    offset += height;
                }
                else
                {
                    offset += _rowHeightEstimate;
                }
            }

            return offset;
        }

        /// <inheritdoc/>
        public void UpdateFromDisplayedRows(int firstDisplayedSlot, int lastDisplayedSlot, double[] displayedHeights, double verticalOffset, double negVerticalOffset, int collapsedSlotCount, int detailsCount)
        {
            if (displayedHeights == null || displayedHeights.Length == 0)
                return;

            // Note: Individual height recording happens before this call via RecordMeasuredHeight
            // for each displayed element with the correct slot. The displayedHeights array contains
            // only visible slots (collapsed slots are skipped), so we cannot assume contiguous slots
            // when iterating from firstDisplayedSlot to lastDisplayedSlot.
            // Height recording is already handled by the caller.
            
            // Store collapsed count and details count for more accurate calculations if needed
            _lastCollapsedSlotCount = collapsedSlotCount;
            _lastDetailsCount = detailsCount;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _measuredHeights.Clear();
            _detailsHeights.Clear();
            _sumMeasuredHeights = 0;
            _measuredCount = 0;
            _minMeasuredHeight = double.MaxValue;
            _maxMeasuredHeight = double.MinValue;
            _rowHeightEstimate = _defaultRowHeight;
            _rowDetailsHeightEstimate = 0;

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

            // Shift cached heights for slots at or after the insertion point
            var slotsToUpdate = _measuredHeights.Keys.Where(k => k >= startIndex).OrderByDescending(k => k).ToList();
            foreach (var slot in slotsToUpdate)
            {
                var height = _measuredHeights[slot];
                _measuredHeights.Remove(slot);
                _measuredHeights[slot + count] = height;
            }

            // Same for details
            var detailsToUpdate = _detailsHeights.Keys.Where(k => k >= startIndex).OrderByDescending(k => k).ToList();
            foreach (var slot in detailsToUpdate)
            {
                var height = _detailsHeights[slot];
                _detailsHeights.Remove(slot);
                _detailsHeights[slot + count] = height;
            }
        }

        /// <inheritdoc/>
        public void OnItemsRemoved(int startIndex, int count)
        {
            _totalItemCount = Math.Max(0, _totalItemCount - count);

            // Remove cached heights for deleted slots and shift remaining
            var slotsToRemove = _measuredHeights.Keys.Where(k => k >= startIndex && k < startIndex + count).ToList();
            foreach (var slot in slotsToRemove)
            {
                var height = _measuredHeights[slot];
                _measuredHeights.Remove(slot);
                _sumMeasuredHeights -= height;
                _measuredCount--;
            }

            // Shift slots after the removed range
            var slotsToShift = _measuredHeights.Keys.Where(k => k >= startIndex + count).OrderBy(k => k).ToList();
            foreach (var slot in slotsToShift)
            {
                var height = _measuredHeights[slot];
                _measuredHeights.Remove(slot);
                _measuredHeights[slot - count] = height;
            }

            // Same for details
            var detailsToRemove = _detailsHeights.Keys.Where(k => k >= startIndex && k < startIndex + count).ToList();
            foreach (var slot in detailsToRemove)
            {
                _detailsHeights.Remove(slot);
            }

            var detailsToShift = _detailsHeights.Keys.Where(k => k >= startIndex + count).OrderBy(k => k).ToList();
            foreach (var slot in detailsToShift)
            {
                var height = _detailsHeights[slot];
                _detailsHeights.Remove(slot);
                _detailsHeights[slot - count] = height;
            }

            // Recalculate min/max
            RecalculateMinMax();
        }

        /// <inheritdoc/>
        public void OnItemsMoved(int oldStartIndex, int newStartIndex, int count)
        {
            if (count <= 0 || oldStartIndex == newStartIndex)
            {
                return;
            }

            // Every height is kept, so the totals and the min/max carry over untouched. Only which
            // slot each height belongs to changes.
            MovedRowHeights.Apply(_measuredHeights, oldStartIndex, newStartIndex, count);
            MovedRowHeights.Apply(_detailsHeights, oldStartIndex, newStartIndex, count);
        }

        /// <inheritdoc/>
        public RowHeightEstimatorState CaptureState()
        {
            return new CachingState(
                _defaultRowHeight,
                _rowHeightEstimate,
                _rowDetailsHeightEstimate,
                (double[])_rowGroupHeaderHeightsByLevel.Clone(),
                _totalItemCount,
                new Dictionary<int, double>(_measuredHeights),
                new Dictionary<int, double>(_detailsHeights),
                _sumMeasuredHeights,
                _measuredCount,
                _minMeasuredHeight,
                _maxMeasuredHeight,
                _lastCollapsedSlotCount,
                _lastDetailsCount);
        }

        /// <inheritdoc/>
        public bool TryRestoreState(RowHeightEstimatorState state)
        {
            if (state is not CachingState snapshot)
            {
                return false;
            }

            _defaultRowHeight = snapshot.DefaultRowHeight;
            _rowHeightEstimate = snapshot.RowHeightEstimate;
            _rowDetailsHeightEstimate = snapshot.RowDetailsHeightEstimate;
            _totalItemCount = snapshot.TotalItemCount;
            _sumMeasuredHeights = snapshot.SumMeasuredHeights;
            _measuredCount = snapshot.MeasuredCount;
            _minMeasuredHeight = snapshot.MinMeasuredHeight;
            _maxMeasuredHeight = snapshot.MaxMeasuredHeight;
            _lastCollapsedSlotCount = snapshot.LastCollapsedSlotCount;
            _lastDetailsCount = snapshot.LastDetailsCount;

            _measuredHeights.Clear();
            foreach (var entry in snapshot.MeasuredHeights)
            {
                _measuredHeights[entry.Key] = entry.Value;
            }

            _detailsHeights.Clear();
            foreach (var entry in snapshot.DetailsHeights)
            {
                _detailsHeights[entry.Key] = entry.Value;
            }

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
                AlgorithmName = "Caching (Individual Heights)",
                CurrentRowHeightEstimate = _rowHeightEstimate,
                CachedHeightCount = _measuredHeights.Count,
                TotalRowCount = _totalItemCount,
                EstimatedTotalHeight = CalculateTotalHeight(_totalItemCount, 0, Array.Empty<int>(), _detailsHeights.Count),
                MinMeasuredHeight = _minMeasuredHeight == double.MaxValue ? _rowHeightEstimate : _minMeasuredHeight,
                MaxMeasuredHeight = _maxMeasuredHeight == double.MinValue ? _rowHeightEstimate : _maxMeasuredHeight,
                AverageMeasuredHeight = _measuredCount > 0 ? _sumMeasuredHeights / _measuredCount : _rowHeightEstimate,
                AdditionalInfo = $"CacheSize: {_measuredHeights.Count}/{MaxCacheSize}, DetailsCount: {_detailsHeights.Count}"
            };
        }

        private sealed class CachingState : RowHeightEstimatorState
        {
            public CachingState(
                double defaultRowHeight,
                double rowHeightEstimate,
                double rowDetailsHeightEstimate,
                double[] rowGroupHeaderHeightsByLevel,
                int totalItemCount,
                Dictionary<int, double> measuredHeights,
                Dictionary<int, double> detailsHeights,
                double sumMeasuredHeights,
                int measuredCount,
                double minMeasuredHeight,
                double maxMeasuredHeight,
                int lastCollapsedSlotCount,
                int lastDetailsCount)
                : base(nameof(CachingRowHeightEstimator))
            {
                DefaultRowHeight = defaultRowHeight;
                RowHeightEstimate = rowHeightEstimate;
                RowDetailsHeightEstimate = rowDetailsHeightEstimate;
                RowGroupHeaderHeightsByLevel = rowGroupHeaderHeightsByLevel;
                TotalItemCount = totalItemCount;
                MeasuredHeights = measuredHeights;
                DetailsHeights = detailsHeights;
                SumMeasuredHeights = sumMeasuredHeights;
                MeasuredCount = measuredCount;
                MinMeasuredHeight = minMeasuredHeight;
                MaxMeasuredHeight = maxMeasuredHeight;
                LastCollapsedSlotCount = lastCollapsedSlotCount;
                LastDetailsCount = lastDetailsCount;
            }

            public double DefaultRowHeight { get; }
            public double RowHeightEstimate { get; }
            public double RowDetailsHeightEstimate { get; }
            public double[] RowGroupHeaderHeightsByLevel { get; }
            public int TotalItemCount { get; }
            public Dictionary<int, double> MeasuredHeights { get; }
            public Dictionary<int, double> DetailsHeights { get; }
            public double SumMeasuredHeights { get; }
            public int MeasuredCount { get; }
            public double MinMeasuredHeight { get; }
            public double MaxMeasuredHeight { get; }
            public int LastCollapsedSlotCount { get; }
            public int LastDetailsCount { get; }
        }

        private void TrimCache()
        {
            // Remove 25% of the cache, preferring entries far from recent access
            int removeCount = MaxCacheSize / 4;
            var keysToRemove = _measuredHeights.Keys.OrderBy(k => k).Take(removeCount).ToList();
            
            foreach (var key in keysToRemove)
            {
                var height = _measuredHeights[key];
                _measuredHeights.Remove(key);
                _sumMeasuredHeights -= height;
                _measuredCount--;
            }

            RecalculateMinMax();
        }

        private void RecalculateMinMax()
        {
            if (_measuredHeights.Count == 0)
            {
                _minMeasuredHeight = double.MaxValue;
                _maxMeasuredHeight = double.MinValue;
            }
            else
            {
                _minMeasuredHeight = _measuredHeights.Values.Min();
                _maxMeasuredHeight = _measuredHeights.Values.Max();
            }
        }
    }
}
