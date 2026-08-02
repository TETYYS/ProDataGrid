// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia.Collections;
using Avalonia.Headless.XUnit;
using Xunit;

// The stub below implements an interface that predates nullable annotations; returning null from
// its members is the point, not an oversight.
#nullable disable

namespace Avalonia.Controls.DataGridTests.Collections;

/// <summary>
/// A collection view the grid cannot derive an order from is refused rather than half-supported.
/// </summary>
/// <remarks>
/// Selection is stored by item, but applying <see cref="DataGrid.SelectedItem"/> or
/// <see cref="DataGrid.SelectedIndex"/> means resolving an item to a position, and
/// <see cref="IDataGridCollectionView"/> carries nothing to resolve one with - no count, no indexer,
/// no IndexOf. Accepting the view anyway made every position come back as -1, which reads as "not in
/// the view": assigning SelectedItem cleared the selection instead of setting it, and left the
/// property reporting a value nothing backed. A binding did nothing and said nothing about why.
/// </remarks>
public class CustomCollectionViewRefusalTests
{
    [AvaloniaFact]
    public void A_Custom_CollectionView_Is_Refused_As_ItemsSource()
    {
        var grid = new DataGrid { AutoGenerateColumns = false };

        var error = Assert.Throws<NotSupportedException>(
            () => grid.ItemsSource = new MinimalCollectionView());

        Assert.Contains(nameof(IDataGridCollectionView), error.Message);
        Assert.Contains(nameof(DataGridCollectionView), error.Message);
    }

    [AvaloniaFact]
    public void A_DataGridCollectionView_And_A_Plain_Collection_Are_Both_Accepted()
    {
        var items = new ObservableCollection<string> { "a", "b" };

        // The plain collection is wrapped for the consumer, so the ordinary way in is unaffected -
        // only handing the grid a view it cannot order is refused.
        var wrapping = new DataGrid { AutoGenerateColumns = false };
        wrapping.ItemsSource = items;
        wrapping.SelectedItem = "b";
        Assert.Equal("b", wrapping.SelectedItem);

        var explicitView = new DataGrid { AutoGenerateColumns = false };
        explicitView.ItemsSource = new DataGridCollectionView(items);
        explicitView.SelectedItem = "b";
        Assert.Equal("b", explicitView.SelectedItem);
    }

    /// <summary>The least a type can do and still claim to be an <see cref="IDataGridCollectionView"/>.</summary>
    private sealed class MinimalCollectionView : IDataGridCollectionView
    {
        private readonly List<object> _items = new() { "a", "b" };

        // Never raised - the grid refuses the view before it could subscribe to anything.
#pragma warning disable CS0067
        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event EventHandler<DataGridCurrentChangingEventArgs> CurrentChanging;
        public event EventHandler CurrentChanged;
#pragma warning restore CS0067

        public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
        public Func<object, bool> Filter { get; set; }
        public IEnumerable SourceCollection => _items;
        public DataGridSortDescriptionCollection SortDescriptions { get; } = new();
        public IAvaloniaReadOnlyList<object> Groups => null;
        public bool CanFilter => false;
        public bool CanSort => false;
        public bool CanGroup => false;
        public bool IsGrouping => false;
        public int GroupingDepth => 0;
        public bool IsEmpty => _items.Count == 0;
        public object CurrentItem => null;
        public int CurrentPosition => -1;
        public bool IsCurrentAfterLast => false;
        public bool IsCurrentBeforeFirst => true;

        public string GetGroupingPropertyNameAtDepth(int level) => null;
        public bool Contains(object item) => _items.Contains(item);
        public void Refresh() { }
        public IDisposable DeferRefresh() => throw new NotSupportedException();
        public bool MoveCurrentToFirst() => false;
        public bool MoveCurrentToLast() => false;
        public bool MoveCurrentToNext() => false;
        public bool MoveCurrentToPrevious() => false;
        public bool MoveCurrentTo(object item) => false;
        public bool MoveCurrentToPosition(int position) => false;
        public IEnumerator GetEnumerator() => _items.GetEnumerator();
    }
}
