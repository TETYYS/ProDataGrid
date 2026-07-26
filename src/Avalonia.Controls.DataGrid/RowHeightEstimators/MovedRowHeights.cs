// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Avalonia.Controls
{
    /// <summary>
    /// Repositions per-slot cached heights when a block of rows moves.
    /// </summary>
    /// <remarks>
    /// Relocating a block within a list rotates the span between its old and its new position, so
    /// every height cached for a slot in that span belongs to a different slot afterwards. None of
    /// them changes value and none is discarded, which is the difference between this and reporting
    /// the move as a removal followed by an insertion: no total needs recomputing, and the moved
    /// rows keep the heights they were actually measured at.
    /// </remarks>
    internal static class MovedRowHeights
    {
        /// <summary>
        /// Applies a move to a slot-keyed height cache.
        /// </summary>
        /// <param name="heights">Cache to reindex, keyed by slot.</param>
        /// <param name="oldStartIndex">Slot the block starts at.</param>
        /// <param name="newStartIndex">Slot the block ends up starting at.</param>
        /// <param name="count">Number of rows in the block.</param>
        public static void Apply(Dictionary<int, double> heights, int oldStartIndex, int newStartIndex, int count)
        {
            if (heights.Count == 0)
            {
                return;
            }

            int lo = Math.Min(oldStartIndex, newStartIndex);
            int hi = Math.Max(oldStartIndex, newStartIndex) + count - 1;

            List<(int From, int To, double Height)>? moved = null;
            foreach (var entry in heights)
            {
                int slot = entry.Key;
                if (slot < lo || slot > hi)
                {
                    continue;
                }

                int target = newStartIndex > oldStartIndex
                    ? (slot < oldStartIndex + count ? slot + (newStartIndex - oldStartIndex) : slot - count)
                    : (slot < oldStartIndex ? slot + count : slot - (oldStartIndex - newStartIndex));

                (moved ??= new List<(int, int, double)>()).Add((slot, target, entry.Value));
            }

            if (moved == null)
            {
                return;
            }

            // Cleared before any is written back: the span maps onto itself, so a slot being
            // vacated is very often the one another entry is moving into.
            foreach (var entry in moved)
            {
                heights.Remove(entry.From);
            }

            foreach (var entry in moved)
            {
                heights[entry.To] = entry.Height;
            }
        }
    }
}
