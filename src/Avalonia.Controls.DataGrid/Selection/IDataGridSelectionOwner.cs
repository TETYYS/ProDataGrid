// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// The grid side of a <see cref="DataGridSelectionModel"/>.
    /// </summary>
    /// <remarks>
    /// A direct call, not an event, and deliberately so: the model invokes this synchronously at a
    /// known point in its own mutation, which is what lets the grid update visuals without a flag to
    /// tell it whether the change came from a click or from consumer code. Implementations must not
    /// mutate the model - doing so would reintroduce the two-way sync this replaces.
    /// </remarks>
    internal interface IDataGridSelectionOwner
    {
        /// <summary>
        /// Called after the model's state has changed and before consumers are notified.
        /// </summary>
        void OnSelectionModelChanged(DataGridSelectionModelChangedEventArgs e);

        /// <summary>
        /// Called when <see cref="DataGridSelectionModel.SingleSelect"/> changes, so that the control's
        /// selection mode follows it.
        /// </summary>
        void OnSelectionModelSingleSelectChanged(bool singleSelect);
    }
}
