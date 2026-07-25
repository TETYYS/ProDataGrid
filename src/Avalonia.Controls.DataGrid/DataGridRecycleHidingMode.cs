// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Avalonia.Controls
{
    /// <summary>
    /// Controls how recycled containers are hidden when removed from the viewport.
    /// Both modes hide the container with an empty clip - which, unlike IsVisible, does not
    /// invalidate measure - and differ only in what happens to its arranged bounds.
    /// </summary>
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    enum DataGridRecycleHidingMode
    {
        /// <summary>
        /// Also arrange recycled containers far offscreen, so layout-sensitive logic cannot pick up
        /// stale bounds (default).
        /// </summary>
        MoveOffscreen = 0,

        /// <summary>
        /// Leave the last arranged bounds intact.
        /// </summary>
        KeepLastBounds = 1
    }
}
