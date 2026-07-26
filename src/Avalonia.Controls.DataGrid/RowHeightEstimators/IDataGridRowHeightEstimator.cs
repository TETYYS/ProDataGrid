// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;

namespace Avalonia.Controls
{
    /// <summary>
    /// Defines the contract for row height estimation and scroll position calculation in DataGrid.
    /// Implementations can provide different algorithms for handling variable row heights.
    /// </summary>
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    interface IDataGridRowHeightEstimator
    {
        /// <summary>
        /// Gets or sets the default row height used as initial estimate.
        /// </summary>
        double DefaultRowHeight { get; set; }

        /// <summary>
        /// Gets the current estimated height for unmeasured rows.
        /// </summary>
        double RowHeightEstimate { get; }

        /// <summary>
        /// Gets the current estimated height for row details.
        /// </summary>
        double RowDetailsHeightEstimate { get; }

        /// <summary>
        /// Gets the current estimated height for row group headers at a specific level.
        /// </summary>
        /// <param name="level">The group level (0 = top level).</param>
        /// <returns>The estimated height for row group headers at this level.</returns>
        double GetRowGroupHeaderHeightEstimate(int level);

        /// <summary>
        /// Records an actual measured row height for a specific slot.
        /// </summary>
        /// <param name="slot">The slot index of the row.</param>
        /// <param name="measuredHeight">The actual measured height of the row.</param>
        /// <param name="hasDetails">Whether the row has details visible.</param>
        /// <param name="detailsHeight">The height of the row details if visible.</param>
        void RecordMeasuredHeight(int slot, double measuredHeight, bool hasDetails = false, double detailsHeight = 0);

        /// <summary>
        /// Records an actual measured row group header height.
        /// </summary>
        /// <param name="slot">The slot index of the row group header.</param>
        /// <param name="level">The group level.</param>
        /// <param name="measuredHeight">The actual measured height.</param>
        void RecordRowGroupHeaderHeight(int slot, int level, double measuredHeight);

        /// <summary>
        /// Gets the estimated or cached height for a specific slot.
        /// </summary>
        /// <param name="slot">The slot index.</param>
        /// <param name="isRowGroupHeader">Whether the slot is a row group header.</param>
        /// <param name="rowGroupLevel">The row group level if it's a header.</param>
        /// <param name="hasDetails">Whether the row has details visible.</param>
        /// <returns>The estimated or cached height.</returns>
        double GetEstimatedHeight(int slot, bool isRowGroupHeader = false, int rowGroupLevel = 0, bool hasDetails = false);

        /// <summary>
        /// Calculates the total estimated height for all rows in the data source.
        /// </summary>
        /// <param name="totalSlotCount">The total number of slots (rows + group headers).</param>
        /// <param name="collapsedSlotCount">The number of collapsed slots.</param>
        /// <param name="rowGroupHeaderCounts">The count of row group headers at each level.</param>
        /// <param name="detailsVisibleCount">The number of rows with details visible.</param>
        /// <returns>The total estimated height.</returns>
        double CalculateTotalHeight(int totalSlotCount, int collapsedSlotCount, int[] rowGroupHeaderCounts, int detailsVisibleCount);

        /// <summary>
        /// Estimates which slot would be at a given vertical offset.
        /// </summary>
        /// <param name="verticalOffset">The vertical offset from the top.</param>
        /// <param name="totalSlotCount">The total number of slots.</param>
        /// <returns>The estimated slot index at the given offset.</returns>
        int EstimateSlotAtOffset(double verticalOffset, int totalSlotCount);

        /// <summary>
        /// Estimates the vertical offset to a specific slot.
        /// </summary>
        /// <param name="slot">The target slot index.</param>
        /// <returns>The estimated vertical offset to the slot.</returns>
        double EstimateOffsetToSlot(int slot);

        /// <summary>
        /// Called when the displayed rows change to update internal estimates.
        /// </summary>
        /// <param name="firstDisplayedSlot">The first displayed slot index.</param>
        /// <param name="lastDisplayedSlot">The last displayed slot index.</param>
        /// <param name="displayedHeights">Array of actual heights for displayed slots.</param>
        /// <param name="verticalOffset">The current vertical scroll offset.</param>
        /// <param name="negVerticalOffset">The negative vertical offset (partial row at top).</param>
        /// <param name="collapsedSlotCount">The number of collapsed slots from 0 to lastDisplayedSlot.</param>
        /// <param name="detailsCount">The number of rows with details visible from 0 to lastDisplayedSlot.</param>
        void UpdateFromDisplayedRows(int firstDisplayedSlot, int lastDisplayedSlot, double[] displayedHeights, double verticalOffset, double negVerticalOffset, int collapsedSlotCount, int detailsCount);

        /// <summary>
        /// Resets all cached heights and estimates.
        /// </summary>
        void Reset();

        /// <summary>
        /// Called when the data source changes.
        /// </summary>
        /// <param name="newItemCount">The new total item count.</param>
        void OnDataSourceChanged(int newItemCount);

        /// <summary>
        /// Called when items are inserted.
        /// </summary>
        /// <param name="startIndex">The starting index of insertion.</param>
        /// <param name="count">The number of items inserted.</param>
        void OnItemsInserted(int startIndex, int count);

        /// <summary>
        /// Called when items are removed.
        /// </summary>
        /// <param name="startIndex">The starting index of removal.</param>
        /// <param name="count">The number of items removed.</param>
        void OnItemsRemoved(int startIndex, int count);

        /// <summary>
        /// Called when a block of items changes position.
        /// </summary>
        /// <remarks>
        /// The item count does not change, and neither does anything measured about the rows: the
        /// heights already known simply belong to different indexes afterwards. Reporting the move
        /// as a removal followed by an insertion would instead discard the moved rows' heights and
        /// leave the estimator counting rows it has never seen.
        /// </remarks>
        /// <param name="oldStartIndex">The index the block starts at.</param>
        /// <param name="newStartIndex">The index the block ends up starting at.</param>
        /// <param name="count">The number of items in the block.</param>
        void OnItemsMoved(int oldStartIndex, int newStartIndex, int count)
        {
        }

        /// <summary>
        /// Creates a snapshot of the current estimation state for debugging.
        /// </summary>
        /// <returns>A diagnostic snapshot of the estimator state.</returns>
        RowHeightEstimatorDiagnostics GetDiagnostics();
    }

    /// <summary>
    /// Extends <see cref="IDataGridRowHeightEstimator"/> with state capture/restore support.
    /// </summary>
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    interface IDataGridRowHeightEstimatorStateful
    {
        /// <summary>
        /// Captures the current internal estimator state.
        /// </summary>
        /// <returns>A snapshot of the estimator state.</returns>
        RowHeightEstimatorState CaptureState();

        /// <summary>
        /// Restores the estimator state from a previous snapshot.
        /// </summary>
        /// <param name="state">The state snapshot to restore.</param>
        /// <returns>True if the state could be restored; otherwise, false.</returns>
        bool TryRestoreState(RowHeightEstimatorState state);
    }

    /// <summary>
    /// Base class for row height estimator state snapshots.
    /// </summary>
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    abstract class RowHeightEstimatorState
    {
        protected RowHeightEstimatorState(string estimatorType)
        {
            EstimatorType = estimatorType;
        }

        /// <summary>
        /// Gets the estimator type identifier for this state.
        /// </summary>
        public string EstimatorType { get; }
    }

    /// <summary>
    /// Diagnostic information about the row height estimator state.
    /// </summary>
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    class RowHeightEstimatorDiagnostics
    {
        /// <summary>
        /// Gets or sets the name of the estimator algorithm.
        /// </summary>
        public string AlgorithmName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current row height estimate.
        /// </summary>
        public double CurrentRowHeightEstimate { get; set; }

        /// <summary>
        /// Gets or sets the number of rows with cached heights.
        /// </summary>
        public int CachedHeightCount { get; set; }

        /// <summary>
        /// Gets or sets the total number of rows.
        /// </summary>
        public int TotalRowCount { get; set; }

        /// <summary>
        /// Gets or sets the estimated total height.
        /// </summary>
        public double EstimatedTotalHeight { get; set; }

        /// <summary>
        /// Gets or sets the minimum measured row height.
        /// </summary>
        public double MinMeasuredHeight { get; set; }

        /// <summary>
        /// Gets or sets the maximum measured row height.
        /// </summary>
        public double MaxMeasuredHeight { get; set; }

        /// <summary>
        /// Gets or sets the average measured row height.
        /// </summary>
        public double AverageMeasuredHeight { get; set; }

        /// <summary>
        /// Gets or sets additional algorithm-specific information.
        /// </summary>
        public string AdditionalInfo { get; set; } = string.Empty;
    }
}
