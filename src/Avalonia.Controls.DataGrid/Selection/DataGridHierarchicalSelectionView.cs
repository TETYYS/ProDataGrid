// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using Avalonia.Controls.DataGridHierarchical;

namespace Avalonia.Controls.DataGridSelection
{
    /// <summary>
    /// <see cref="IDataGridSelectionView"/> over an <see cref="IHierarchicalModel"/>'s flattened nodes.
    /// </summary>
    /// <remarks>
    /// Projects nodes to the items they carry, so the selection model stores the caller's items rather
    /// than grid-internal nodes. That projection used to live in a proxy wrapped around the selection
    /// model; doing it here instead means there is only ever one selection to keep straight, and
    /// expanding or collapsing a branch - which reorders and resizes the flattened list - needs no
    /// handling at all, because nothing indexes into it.
    /// </remarks>
    internal sealed class DataGridHierarchicalSelectionView : IDataGridSelectionView, IDisposable
    {
        private readonly IHierarchicalModel _model;
        private readonly Action? _onOrderChanged;
        private bool _disposed;

        public DataGridHierarchicalSelectionView(IHierarchicalModel model, Action? onOrderChanged = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _onOrderChanged = onOrderChanged;
            _model.FlattenedChanged += OnFlattenedChanged;
        }

        public IHierarchicalModel Model => _model;

        public int Count => _model.Count;

        public IEnumerable<object?> Items
        {
            get
            {
                foreach (var node in _model.Flattened)
                {
                    yield return Project(node);
                }
            }
        }

        public bool TryGetItemAt(int index, out object? item)
        {
            if (index < 0 || index >= _model.Count)
            {
                item = null;
                return false;
            }

            item = Project(_model.GetItem(index));
            return true;
        }

        public int IndexOf(object? item)
        {
            // Projected first, so that a caller who selected a node directly still gets an answer.
            var projected = Project(item);
            return projected is null ? -1 : _model.IndexOf(projected);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _model.FlattenedChanged -= OnFlattenedChanged;
            _disposed = true;
        }

        internal static object? Project(object? value) => value switch
        {
            HierarchicalNode node => node.Item,
            IHierarchicalNodeItem nodeItem => nodeItem.Item,
            _ => value
        };

        private void OnFlattenedChanged(object? sender, FlattenedChangedEventArgs e) => _onOrderChanged?.Invoke();
    }
}
