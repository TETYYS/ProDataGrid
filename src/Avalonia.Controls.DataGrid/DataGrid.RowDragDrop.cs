// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using Avalonia;
using Avalonia.Controls.DataGridDragDrop;
using Avalonia.Controls.Utils;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Avalonia.Controls
{
#nullable enable
    partial class DataGrid
    {
        private DeferredRowDragSelection? _deferredRowDragSelection;

        public static readonly RoutedEvent<DataGridRowDragStartingEventArgs> RowDragStartingEvent =
            RoutedEvent.Register<DataGrid, DataGridRowDragStartingEventArgs>(
                nameof(RowDragStarting),
                RoutingStrategies.Bubble);

        public static readonly RoutedEvent<DataGridRowDragStartedEventArgs> RowDragStartedEvent =
            RoutedEvent.Register<DataGrid, DataGridRowDragStartedEventArgs>(
                nameof(RowDragStarted),
                RoutingStrategies.Bubble);

        public static readonly RoutedEvent<DataGridRowDragUpdatedEventArgs> RowDragUpdatedEvent =
            RoutedEvent.Register<DataGrid, DataGridRowDragUpdatedEventArgs>(
                nameof(RowDragUpdated),
                RoutingStrategies.Bubble);

        public static readonly RoutedEvent<DataGridRowDragCanceledEventArgs> RowDragCanceledEvent =
            RoutedEvent.Register<DataGrid, DataGridRowDragCanceledEventArgs>(
                nameof(RowDragCanceled),
                RoutingStrategies.Bubble);

        public static readonly RoutedEvent<DataGridRowDragCompletedEventArgs> RowDragCompletedEvent =
            RoutedEvent.Register<DataGrid, DataGridRowDragCompletedEventArgs>(
                nameof(RowDragCompleted),
                RoutingStrategies.Bubble);

        public event EventHandler<DataGridRowDragStartingEventArgs>? RowDragStarting
        {
            add => AddHandler(RowDragStartingEvent, value);
            remove => RemoveHandler(RowDragStartingEvent, value);
        }

        public event EventHandler<DataGridRowDragStartedEventArgs>? RowDragStarted
        {
            add => AddHandler(RowDragStartedEvent, value);
            remove => RemoveHandler(RowDragStartedEvent, value);
        }

        public event EventHandler<DataGridRowDragUpdatedEventArgs>? RowDragUpdated
        {
            add => AddHandler(RowDragUpdatedEvent, value);
            remove => RemoveHandler(RowDragUpdatedEvent, value);
        }

        public event EventHandler<DataGridRowDragCanceledEventArgs>? RowDragCanceled
        {
            add => AddHandler(RowDragCanceledEvent, value);
            remove => RemoveHandler(RowDragCanceledEvent, value);
        }

        public event EventHandler<DataGridRowDragCompletedEventArgs>? RowDragCompleted
        {
            add => AddHandler(RowDragCompletedEvent, value);
            remove => RemoveHandler(RowDragCompletedEvent, value);
        }

        internal void OnRowDragStarting(DataGridRowDragStartingEventArgs e)
        {
            e.RoutedEvent ??= RowDragStartingEvent;
            e.Source ??= this;
            RaiseEvent(e);
        }

        internal void OnRowDragStarted(DataGridRowDragStartedEventArgs e)
        {
            e.RoutedEvent ??= RowDragStartedEvent;
            e.Source ??= this;
            RaiseEvent(e);
        }

        internal void OnRowDragUpdated(DataGridRowDragUpdatedEventArgs e)
        {
            e.RoutedEvent ??= RowDragUpdatedEvent;
            e.Source ??= this;
            RaiseEvent(e);
        }

        internal void OnRowDragCanceled(DataGridRowDragCanceledEventArgs e)
        {
            e.RoutedEvent ??= RowDragCanceledEvent;
            e.Source ??= this;
            RaiseEvent(e);
        }

        internal void OnRowDragCompleted(DataGridRowDragCompletedEventArgs e)
        {
            e.RoutedEvent ??= RowDragCompletedEvent;
            e.Source ??= this;
            RaiseEvent(e);
        }

        internal void SetActiveRowDragSession(DataGridRowDragSession? session)
        {
            SetAndRaise(ActiveRowDragSessionProperty, ref _activeRowDragSession, session);
        }

        internal bool ShouldSuppressSelectionDragFromRowDragHandle(int columnIndex)
        {
            if (!CanStartRowDragGesture())
            {
                return false;
            }

            var options = _rowDragDropOptions ?? new DataGridRowDragDropOptions();
            if (!options.SuppressSelectionDragFromDragHandle)
            {
                return false;
            }

            if (columnIndex >= 0)
            {
                return RowDragHandle == DataGridRowDragHandle.Row ||
                       RowDragHandle == DataGridRowDragHandle.RowHeaderAndRow;
            }

            return RowDragHandle == DataGridRowDragHandle.RowHeader ||
                   RowDragHandle == DataGridRowDragHandle.RowHeaderAndRow;
        }

        internal bool CanStartRowDragGesture()
        {
            return CanUserReorderRows &&
                   !IsReadOnly &&
                   IsEnabled &&
                   EditingRow == null &&
                   DataConnection?.EditableCollectionView?.IsAddingNew != true &&
                   DataConnection?.EditableCollectionView?.IsEditingItem != true;
        }

        internal bool ShouldDeferSelectionForRowDrag(
            int columnIndex,
            int slot,
            bool isSelected,
            PointerPressedEventArgs pointerPressedEventArgs)
        {
            _deferredRowDragSelection = null;

            if (!ShouldSuppressSelectionDragFromRowDragHandle(columnIndex) ||
                SelectionUnit != DataGridSelectionUnit.FullRow ||
                SelectionMode != DataGridSelectionMode.Extended ||
                !isSelected ||
                slot < 0 ||
                _selectionModel.Count <= 1)
            {
                return false;
            }

            KeyboardHelper.GetMetaKeyState(this, pointerPressedEventArgs.KeyModifiers, out bool ctrl, out _);
            if (ctrl || pointerPressedEventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                return false;
            }

            _deferredRowDragSelection = new DeferredRowDragSelection(
                pointerPressedEventArgs.Pointer.Id,
                columnIndex,
                slot,
                pointerPressedEventArgs);
            return true;
        }

        internal void CompleteDeferredRowDragSelection(int pointerId)
        {
            DeferredRowDragSelection? deferred = TakeDeferredRowDragSelection(pointerId);
            if (deferred == null ||
                !GetRowSelection(deferred.Slot) ||
                _selectionModel.Count <= 1)
            {
                return;
            }

            UpdateStateOnMouseLeftButtonDown(
                deferred.TriggerEvent,
                deferred.ColumnIndex,
                deferred.Slot,
                allowEdit: false);
        }

        internal void CancelDeferredRowDragSelection(int pointerId)
        {
            TakeDeferredRowDragSelection(pointerId);
        }

        private DeferredRowDragSelection? TakeDeferredRowDragSelection(int pointerId)
        {
            if (_deferredRowDragSelection?.PointerId != pointerId)
            {
                return null;
            }

            DeferredRowDragSelection deferred = _deferredRowDragSelection;
            _deferredRowDragSelection = null;
            return deferred;
        }

        private void RefreshRowDragDropController()
        {
            _rowDragDropController?.Dispose();
            _rowDragDropController = null;
            SetActiveRowDragSession(null);

            if (!CanUserReorderRows)
            {
                UpdatePseudoClasses();
                return;
            }

            var handler = RowDropHandler
                ?? _rowDropHandler
                ?? (_hierarchicalRowsEnabled
                    ? new DataGridHierarchicalRowReorderHandler()
                    : new DataGridRowReorderHandler());
            _rowDropHandler = handler;

            var options = _rowDragDropOptions ?? new DataGridRowDragDropOptions();
            _rowDragDropOptions = options;

            var factory = RowDragDropControllerFactory ?? _rowDragDropControllerFactory;
            _rowDragDropController = factory?.Create(this, handler, options)
                ?? new DataGridRowDragDropController(this, handler, options);

            UpdatePseudoClasses();
        }

        private void OnCanUserReorderRowsChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_areHandlersSuspended)
            {
                return;
            }

            RefreshRowDragDropController();
        }

        private void OnRowDragHandleChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_areHandlersSuspended)
            {
                return;
            }

            UpdatePseudoClasses();
        }

        private void OnRowDragHandleVisibleChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_areHandlersSuspended)
            {
                return;
            }

            UpdatePseudoClasses();
        }

        private void OnRowDropHandlerChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_areHandlersSuspended)
            {
                return;
            }

            _rowDropHandler = e.NewValue as IDataGridRowDropHandler
                ?? (_hierarchicalRowsEnabled
                    ? new DataGridHierarchicalRowReorderHandler()
                    : new DataGridRowReorderHandler());
            RefreshRowDragDropController();
        }

        private void OnRowDragDropOptionsChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_areHandlersSuspended)
            {
                return;
            }

            RefreshRowDragDropController();
        }

        private void OnRowDragDropControllerFactoryChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_areHandlersSuspended)
            {
                return;
            }

            _rowDragDropControllerFactory = e.NewValue as IDataGridRowDragDropControllerFactory;
            RefreshRowDragDropController();
        }

        private sealed class DeferredRowDragSelection
        {
            public DeferredRowDragSelection(
                int pointerId,
                int columnIndex,
                int slot,
                PointerPressedEventArgs triggerEvent)
            {
                PointerId = pointerId;
                ColumnIndex = columnIndex;
                Slot = slot;
                TriggerEvent = triggerEvent;
            }

            public int PointerId { get; }

            public int ColumnIndex { get; }

            public int Slot { get; }

            public PointerPressedEventArgs TriggerEvent { get; }
        }
    }
#nullable restore
}
