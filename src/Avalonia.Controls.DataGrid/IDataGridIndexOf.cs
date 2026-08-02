// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

#nullable disable

using System;

namespace Avalonia.Controls
{
    /// <summary>
    /// Provides a fast item-to-index lookup path for <see cref="DataGrid"/> selection and currency operations.
    /// </summary>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    interface IDataGridIndexOf
    {
        /// <summary>
        /// Attempts to resolve the index of <paramref name="item"/> using reference semantics.
        /// </summary>
        /// <remarks>
        /// Implementations must resolve the index without scanning the collection - this is called from
        /// layout and selection hot paths that only take this route because it is expected to be cheap.
        /// Return <c>false</c> when no such lookup is available instead of falling back to a linear search;
        /// the caller has its own fallbacks and can pick a better one when told the fast path is unavailable.
        /// </remarks>
        /// <param name="item">The item instance to locate.</param>
        /// <param name="index">When this method returns <c>true</c>, the resolved index.</param>
        /// <returns><c>true</c> when an index was resolved cheaply; otherwise, <c>false</c>.</returns>
        bool TryGetReferenceIndex(object item, out int index);
    }
}
