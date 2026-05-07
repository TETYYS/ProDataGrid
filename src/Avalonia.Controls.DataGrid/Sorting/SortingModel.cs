// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;

namespace Avalonia.Controls.DataGridSorting
{
    [Flags]
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    enum SortingModifiers
    {
        None = 0,
        Multi = 1,
        Clear = 2
    }

    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    enum SortCycleMode
    {
        AscendingDescending,
        AscendingDescendingNone
    }

    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    sealed class SortingDescriptor : IEquatable<SortingDescriptor>
    {
        public SortingDescriptor(object columnId, ListSortDirection direction, string propertyPath = null, IComparer comparer = null, CultureInfo culture = null)
        {
            ColumnId = columnId ?? throw new ArgumentNullException(nameof(columnId));
            Direction = direction;
            PropertyPath = propertyPath;
            Comparer = comparer;
            Culture = culture;
        }

        public object ColumnId { get; }

        public string PropertyPath { get; }

        public IComparer Comparer { get; }

        public CultureInfo Culture { get; }

        public ListSortDirection Direction { get; }

        public bool HasPropertyPath => !string.IsNullOrEmpty(PropertyPath);

        public bool HasComparer => Comparer != null;

        public SortingDescriptor WithDirection(ListSortDirection direction)
        {
            if (direction == Direction)
            {
                return this;
            }

            return new SortingDescriptor(ColumnId, direction, PropertyPath, Comparer, Culture);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SortingDescriptor);
        }

        public bool Equals(SortingDescriptor other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null)
            {
                return false;
            }

            return Equals(ColumnId, other.ColumnId)
                && string.Equals(PropertyPath, other.PropertyPath, StringComparison.Ordinal)
                && Equals(Comparer, other.Comparer)
                && Equals(Culture, other.Culture)
                && Direction == other.Direction;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 23) + (ColumnId?.GetHashCode() ?? 0);
                hash = (hash * 23) + (PropertyPath?.GetHashCode() ?? 0);
                hash = (hash * 23) + (Comparer?.GetHashCode() ?? 0);
                hash = (hash * 23) + (Culture?.GetHashCode() ?? 0);
                hash = (hash * 23) + Direction.GetHashCode();
                return hash;
            }
        }
    }

    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    class SortingChangedEventArgs : EventArgs
    {
        public SortingChangedEventArgs(IReadOnlyList<SortingDescriptor> oldDescriptors, IReadOnlyList<SortingDescriptor> newDescriptors)
        {
            OldDescriptors = oldDescriptors ?? Array.Empty<SortingDescriptor>();
            NewDescriptors = newDescriptors ?? Array.Empty<SortingDescriptor>();
        }

        public IReadOnlyList<SortingDescriptor> OldDescriptors { get; }

        public IReadOnlyList<SortingDescriptor> NewDescriptors { get; }
    }

    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    class SortingChangingEventArgs : CancelEventArgs
    {
        public SortingChangingEventArgs(IReadOnlyList<SortingDescriptor> oldDescriptors, IReadOnlyList<SortingDescriptor> newDescriptors)
        {
            OldDescriptors = oldDescriptors ?? Array.Empty<SortingDescriptor>();
            NewDescriptors = newDescriptors ?? Array.Empty<SortingDescriptor>();
        }

        public IReadOnlyList<SortingDescriptor> OldDescriptors { get; }

        public IReadOnlyList<SortingDescriptor> NewDescriptors { get; }
    }

    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    interface ISortingModel
    {
        IReadOnlyList<SortingDescriptor> Descriptors { get; }

        bool MultiSort { get; set; }

        SortCycleMode CycleMode { get; set; }

        bool OwnsViewSorts { get; set; }

        bool KeepSecondarySorts { get; set; }

        event EventHandler<SortingChangedEventArgs> SortingChanged;

        event EventHandler<SortingChangingEventArgs> SortingChanging;

        void Toggle(SortingDescriptor descriptor, SortingModifiers modifiers = SortingModifiers.None);

        void Apply(IEnumerable<SortingDescriptor> descriptors);

        void Clear();

        bool Remove(object columnId);

        bool Move(object columnId, int newIndex);

        void SetOrUpdate(SortingDescriptor descriptor);

        void BeginUpdate();

        void EndUpdate();

        IDisposable DeferRefresh();
    }

    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    interface IDataGridSortingModelFactory
    {
        ISortingModel Create();
    }

    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    sealed class SortingModel : ISortingModel
    {
        private readonly List<SortingDescriptor> _descriptors = new();
        private readonly IReadOnlyList<SortingDescriptor> _readOnlyView;
        private int _updateNesting;
        private bool _hasPendingChange;
        private List<SortingDescriptor> _pendingOldDescriptors;

        public SortingModel()
        {
            MultiSort = true;
            CycleMode = SortCycleMode.AscendingDescending;
            OwnsViewSorts = true;
            _readOnlyView = _descriptors.AsReadOnly();
        }

        public IReadOnlyList<SortingDescriptor> Descriptors => _readOnlyView;

        public bool MultiSort { get; set; }

        public SortCycleMode CycleMode { get; set; }

        public bool OwnsViewSorts { get; set; }

        public bool KeepSecondarySorts { get; set; }

        public event EventHandler<SortingChangedEventArgs> SortingChanged;

        public event EventHandler<SortingChangingEventArgs> SortingChanging;

        public void Toggle(SortingDescriptor descriptor, SortingModifiers modifiers = SortingModifiers.None)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            bool multiGesture = MultiSort && modifiers.HasFlag(SortingModifiers.Multi);
            bool clearGesture = modifiers.HasFlag(SortingModifiers.Clear);

            var next = new List<SortingDescriptor>(_descriptors);
            int existingIndex = IndexOf(descriptor.ColumnId);

            if (!multiGesture)
            {
                if (KeepSecondarySorts)
                {
                    if (existingIndex > 0)
                    {
                        var item = next[existingIndex];
                        next.RemoveAt(existingIndex);
                        next.Insert(0, item);
                        existingIndex = 0;
                    }
                }
                else
                {
                    RemoveAllExcept(next, descriptor.ColumnId, ref existingIndex);
                }
            }

            if (clearGesture)
            {
                if (existingIndex >= 0)
                {
                    next.RemoveAt(existingIndex);
                }
                else if (!multiGesture)
                {
                    next.Clear();
                }

                ApplyState(next);
                return;
            }

            if (existingIndex >= 0)
            {
                var current = next[existingIndex];
                var nextDirection = GetNextDirection(current.Direction);

                if (nextDirection.HasValue)
                {
                    next[existingIndex] = current.WithDirection(nextDirection.Value);
                }
                else
                {
                    next.RemoveAt(existingIndex);
                    if (!multiGesture && KeepSecondarySorts)
                    {
                        next.Clear();
                    }
                }
            }
            else
            {
                if (!multiGesture && KeepSecondarySorts)
                    next.Insert(0, descriptor);
                else
                    next.Add(descriptor);
            }

            ApplyState(next);
        }

        public void Apply(IEnumerable<SortingDescriptor> descriptors)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            ApplyState(new List<SortingDescriptor>(descriptors));
        }

        public void Clear()
        {
            if (_descriptors.Count == 0)
            {
                return;
            }

            ApplyState(new List<SortingDescriptor>());
        }

        public bool Remove(object columnId)
        {
            if (columnId == null)
            {
                throw new ArgumentNullException(nameof(columnId));
            }

            int index = IndexOf(columnId);
            if (index < 0)
            {
                return false;
            }

            var next = new List<SortingDescriptor>(_descriptors);
            next.RemoveAt(index);
            ApplyState(next);
            return true;
        }

        public bool Move(object columnId, int newIndex)
        {
            if (columnId == null)
            {
                throw new ArgumentNullException(nameof(columnId));
            }

            if (newIndex < 0 || newIndex >= _descriptors.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(newIndex));
            }

            int oldIndex = IndexOf(columnId);
            if (oldIndex < 0 || oldIndex == newIndex)
            {
                return false;
            }

            var next = new List<SortingDescriptor>(_descriptors);
            var descriptor = next[oldIndex];
            next.RemoveAt(oldIndex);
            next.Insert(newIndex, descriptor);

            ApplyState(next);
            return true;
        }

        public void SetOrUpdate(SortingDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            var next = new List<SortingDescriptor>(_descriptors);
            int index = IndexOf(descriptor.ColumnId);
            if (index >= 0)
            {
                next[index] = descriptor;
            }
            else
            {
                next.Add(descriptor);
            }

            ApplyState(next);
        }

        public void BeginUpdate()
        {
            _updateNesting++;
        }

        public void EndUpdate()
        {
            if (_updateNesting == 0)
            {
                throw new InvalidOperationException("EndUpdate called without a matching BeginUpdate.");
            }

            _updateNesting--;

            if (_updateNesting == 0 && _hasPendingChange)
            {
                var oldDescriptors = _pendingOldDescriptors ?? new List<SortingDescriptor>();
                _pendingOldDescriptors = null;
                _hasPendingChange = false;
                RaiseSortingChanged(oldDescriptors, new List<SortingDescriptor>(_descriptors));
            }
        }

        public IDisposable DeferRefresh()
        {
            BeginUpdate();
            return new UpdateScope(this);
        }

        private void ApplyState(List<SortingDescriptor> next)
        {
            for (int i = 0; i < next.Count; i++)
            {
                if (next[i] == null)
                {
                    throw new ArgumentException("Sorting descriptors cannot contain null entries.", nameof(next));
                }
            }

            if (!MultiSort && next.Count > 1)
            {
                next = new List<SortingDescriptor> { next[0] };
            }

            EnsureUniqueColumns(next);

            if (SequenceEqual(_descriptors, next))
            {
                return;
            }

            var oldDescriptors = new List<SortingDescriptor>(_descriptors);

            if (!RaiseSortingChanging(oldDescriptors, new List<SortingDescriptor>(next)))
            {
                return;
            }

            _descriptors.Clear();
            _descriptors.AddRange(next);

            if (_updateNesting > 0)
            {
                if (!_hasPendingChange)
                {
                    _pendingOldDescriptors = oldDescriptors;
                }

                _hasPendingChange = true;
                return;
            }

            RaiseSortingChanged(oldDescriptors, new List<SortingDescriptor>(_descriptors));
        }

        private bool RaiseSortingChanging(IReadOnlyList<SortingDescriptor> oldDescriptors, IReadOnlyList<SortingDescriptor> newDescriptors)
        {
            var handler = SortingChanging;
            if (handler == null)
            {
                return true;
            }

            var args = new SortingChangingEventArgs(
                new List<SortingDescriptor>(oldDescriptors),
                new List<SortingDescriptor>(newDescriptors));
            handler(this, args);
            return !args.Cancel;
        }

        private void RaiseSortingChanged(IReadOnlyList<SortingDescriptor> oldDescriptors, IReadOnlyList<SortingDescriptor> newDescriptors)
        {
            SortingChanged?.Invoke(this, new SortingChangedEventArgs(oldDescriptors, newDescriptors));
        }

        private static bool SequenceEqual(List<SortingDescriptor> left, List<SortingDescriptor> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!Equals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private int IndexOf(object columnId)
        {
            for (int i = 0; i < _descriptors.Count; i++)
            {
                if (IsSameColumnId(_descriptors[i], columnId))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void RemoveAllExcept(List<SortingDescriptor> list, object columnId, ref int existingIndex)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!IsSameColumnId(list[i], columnId))
                {
                    list.RemoveAt(i);
                    if (i < existingIndex)
                    {
                        existingIndex--;
                    }
                }
            }
        }

        private static bool IsSameColumnId(SortingDescriptor descriptor, object columnId)
        {
            if (Equals(descriptor.ColumnId, columnId))
            {
                return true;
            }

            if (string.IsNullOrEmpty(descriptor.PropertyPath))
            {
                return false;
            }

            if (columnId is string path &&
                string.Equals(descriptor.PropertyPath, path, StringComparison.Ordinal))
            {
                return true;
            }

            if (columnId is DataGridColumn gridColumn)
            {
                var sortMemberPath = gridColumn.SortMemberPath;
                if (!string.IsNullOrEmpty(sortMemberPath) &&
                    string.Equals(descriptor.PropertyPath, sortMemberPath, StringComparison.Ordinal))
                {
                    return true;
                }

                var propertyPath = gridColumn.GetSortPropertyName();
                if (!string.IsNullOrEmpty(propertyPath) &&
                    string.Equals(descriptor.PropertyPath, propertyPath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private ListSortDirection? GetNextDirection(ListSortDirection current)
        {
            switch (CycleMode)
            {
                case SortCycleMode.AscendingDescending:
                    return current == ListSortDirection.Ascending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending;
                default:
                    return current == ListSortDirection.Ascending
                        ? ListSortDirection.Descending
                        : (ListSortDirection?)null;
            }
        }

        private static void EnsureUniqueColumns(List<SortingDescriptor> descriptors)
        {
            var set = new HashSet<object>();
            foreach (var descriptor in descriptors)
            {
                if (descriptor == null)
                {
                    continue;
                }

                if (!set.Add(descriptor.ColumnId))
                {
                    throw new ArgumentException("Sorting descriptors must have unique column identifiers.", nameof(descriptors));
                }
            }
        }

        private sealed class UpdateScope : IDisposable
        {
            private readonly SortingModel _owner;
            private bool _disposed;

            public UpdateScope(SortingModel owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _owner.EndUpdate();
                _disposed = true;
            }
        }
    }
}
