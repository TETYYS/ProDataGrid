// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// Factory hook for creating the selection model a <see cref="DataGrid"/> uses.
    /// Implementations can supply a custom model or a custom item comparer.
    /// </summary>
#if !DATAGRID_INTERNAL
    public
#else
    internal
#endif
    interface IDataGridSelectionModelFactory
    {
        /// <summary>Creates a selection model for a grid that has none assigned.</summary>
        DataGridSelectionModel Create();
    }
}
