// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls.DataGridHierarchical;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Hierarchical;

public class ObservableRangeCollectionTests
{
    [Fact]
    public void AddRange_Raises_Add_With_All_Items()
    {
        var collection = new ObservableRangeCollection<int>();
        NotifyCollectionChangedEventArgs? args = null;
        var propertyChanges = new List<string?>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName);
        collection.CollectionChanged += (_, e) => args = e;

        collection.AddRange(new[] { 1, 2, 3 });

        Assert.NotNull(args);
        Assert.Equal(NotifyCollectionChangedAction.Add, args!.Action);
        Assert.NotNull(args.NewItems);
        Assert.Equal(3, args.NewItems!.Count);
        Assert.Equal(new[] { 1, 2, 3 }, args.NewItems.Cast<int>().ToArray());
        Assert.Equal(0, args.NewStartingIndex);
        Assert.Contains(nameof(collection.Count), propertyChanges);
        Assert.Contains("Item[]", propertyChanges);
        Assert.Equal(new[] { 1, 2, 3 }, collection.ToArray());
    }

    [Theory]
    // Forwards: the run travels right, everything it passes slides left.
    [InlineData(0, 2, 1, new[] { 2, 3, 1, 4, 5 })]
    [InlineData(0, 3, 2, new[] { 3, 4, 5, 1, 2 })]
    // Backwards.
    [InlineData(3, 1, 2, new[] { 1, 4, 5, 2, 3 })]
    [InlineData(4, 0, 1, new[] { 5, 1, 2, 3, 4 })]
    // The whole list, and a no-op.
    [InlineData(0, 0, 5, new[] { 1, 2, 3, 4, 5 })]
    public void MoveRange_Rotates_The_Span_It_Travels_Over(int oldIndex, int newIndex, int count, int[] expected)
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2, 3, 4, 5 });

        collection.MoveRange(oldIndex, newIndex, count);

        Assert.Equal(expected, collection.ToArray());
    }

    [Fact]
    public void MoveRange_Raises_One_Move_And_Not_A_Remove_And_An_Add()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2, 3, 4, 5 });
        var events = new List<NotifyCollectionChangedEventArgs>();
        var propertyChanges = new List<string?>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName);
        collection.CollectionChanged += (_, e) => events.Add(e);

        collection.MoveRange(0, 3, 2); // {1,2} to the end

        var args = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Move, args.Action);
        Assert.Equal(0, args.OldStartingIndex);
        Assert.Equal(3, args.NewStartingIndex);
        Assert.Equal(new[] { 1, 2 }, args.OldItems!.Cast<int>().ToArray());
        Assert.Equal(new[] { 1, 2 }, args.NewItems!.Cast<int>().ToArray());
        Assert.Equal(new[] { 3, 4, 5, 1, 2 }, collection.ToArray());

        // Nothing joined or left, so the count did not change and nothing should claim it did.
        Assert.DoesNotContain(nameof(collection.Count), propertyChanges);
        Assert.Contains("Item[]", propertyChanges);
    }

    [Fact]
    public void MoveRange_Raises_Nothing_When_Nothing_Moves()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2, 3 });
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        collection.MoveRange(1, 1, 2);
        collection.MoveRange(0, 2, 0);

        Assert.Empty(events);
        Assert.Equal(new[] { 1, 2, 3 }, collection.ToArray());
    }

    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(0, -1, 1)]
    [InlineData(2, 0, 2)] // run runs off the end
    [InlineData(0, 2, 2)] // destination runs off the end
    // Rejected on its own merits, not only when the run would have gone somewhere: the
    // destination being the origin says nothing about whether either one exists.
    [InlineData(-5, -5, 3)]
    [InlineData(99, 99, 3)]
    [InlineData(0, 0, -1)] // a negative run is not a no-op, it is nonsense
    public void MoveRange_Rejects_A_Run_That_Does_Not_Fit(int oldIndex, int newIndex, int count)
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2, 3 });

        Assert.Throws<ArgumentOutOfRangeException>(() => collection.MoveRange(oldIndex, newIndex, count));
    }

    [Fact]
    public void RemoveRange_Raises_Remove_With_All_Items()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2, 3, 4 });
        NotifyCollectionChangedEventArgs? args = null;
        var propertyChanges = new List<string?>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName);
        collection.CollectionChanged += (_, e) => args = e;

        collection.RemoveRange(1, 2);

        Assert.NotNull(args);
        Assert.Equal(NotifyCollectionChangedAction.Remove, args!.Action);
        Assert.NotNull(args.OldItems);
        Assert.Equal(2, args.OldItems!.Count);
        Assert.Equal(new[] { 2, 3 }, args.OldItems.Cast<int>().ToArray());
        Assert.Equal(1, args.OldStartingIndex);
        Assert.Contains(nameof(collection.Count), propertyChanges);
        Assert.Contains("Item[]", propertyChanges);
        Assert.Equal(new[] { 1, 4 }, collection.ToArray());
    }

    [Fact]
    public void AddRange_Throws_When_Items_Null()
    {
        var collection = new ObservableRangeCollection<int>();

        Assert.Throws<ArgumentNullException>(() => collection.AddRange(null!));
    }

    [Fact]
    public void AddRange_Empty_Does_Not_Raise_CollectionChanged()
    {
        var collection = new ObservableRangeCollection<int>();
        var raised = false;
        collection.CollectionChanged += (_, __) => raised = true;

        collection.AddRange(Array.Empty<int>());

        Assert.False(raised);
        Assert.Empty(collection);
    }

    [Fact]
    public void AddRange_Materializes_Enumerable_Items()
    {
        var collection = new ObservableRangeCollection<int>();
        IEnumerable<int> items = YieldItems();
        NotifyCollectionChangedEventArgs? args = null;
        collection.CollectionChanged += (_, e) => args = e;

        collection.AddRange(items);

        Assert.Equal(new[] { 1, 2, 3 }, collection.ToArray());
        Assert.NotNull(args);
        Assert.Equal(3, args!.NewItems!.Count);
    }

    [Fact]
    public void InsertRange_Inserts_At_Index_And_Raises_Add()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 4 });
        NotifyCollectionChangedEventArgs? args = null;
        collection.CollectionChanged += (_, e) => args = e;

        collection.InsertRange(1, new[] { 2, 3 });

        Assert.Equal(new[] { 1, 2, 3, 4 }, collection.ToArray());
        Assert.NotNull(args);
        Assert.Equal(NotifyCollectionChangedAction.Add, args!.Action);
        Assert.Equal(1, args.NewStartingIndex);
        Assert.Equal(new[] { 2, 3 }, args.NewItems!.Cast<int>().ToArray());
    }

    [Fact]
    public void InsertRange_Uses_List_For_Notify_When_Items_Are_GenericOnlyList()
    {
        var collection = new ObservableRangeCollection<int>();
        var items = new GenericOnlyList<int> { 7, 8 };
        NotifyCollectionChangedEventArgs? args = null;
        collection.CollectionChanged += (_, e) => args = e;

        collection.InsertRange(0, items);

        Assert.Equal(new[] { 7, 8 }, collection.ToArray());
        Assert.NotNull(args);
        Assert.NotNull(args!.NewItems);
        Assert.Equal(2, args.NewItems!.Count);
        Assert.Equal(new[] { 7, 8 }, args.NewItems.Cast<int>().ToArray());
    }

    [Fact]
    public void InsertRange_Empty_Does_Not_Raise_CollectionChanged()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });
        var raised = false;
        collection.CollectionChanged += (_, __) => raised = true;

        collection.InsertRange(1, Array.Empty<int>());

        Assert.False(raised);
        Assert.Equal(new[] { 1, 2 }, collection.ToArray());
    }

    [Fact]
    public void InsertRange_Throws_When_Items_Null()
    {
        var collection = new ObservableRangeCollection<int>();

        Assert.Throws<ArgumentNullException>(() => collection.InsertRange(0, null!));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void InsertRange_Throws_When_Index_Invalid(int index)
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });

        Assert.Throws<ArgumentOutOfRangeException>(() => collection.InsertRange(index, new[] { 3 }));
    }

    [Fact]
    public void GetRange_Returns_Copy_Of_Items()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2, 3, 4 });

        var range = collection.GetRange(1, 2);

        Assert.Equal(new[] { 2, 3 }, range.ToArray());
        range[0] = 99;
        Assert.Equal(2, collection[1]);
    }

    [Fact]
    public void GetRange_Throws_When_Index_Negative()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });

        Assert.Throws<ArgumentOutOfRangeException>(() => collection.GetRange(-1, 1));
    }

    [Fact]
    public void GetRange_Throws_When_Count_Negative()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });

        Assert.Throws<ArgumentOutOfRangeException>(() => collection.GetRange(0, -1));
    }

    [Fact]
    public void GetRange_Throws_When_End_Beyond_Count()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2, 3 });

        Assert.Throws<ArgumentOutOfRangeException>(() => collection.GetRange(2, 2));
    }

    [Fact]
    public void RemoveRange_Count_Zero_Does_Not_Raise_CollectionChanged()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });
        var raised = false;
        collection.CollectionChanged += (_, __) => raised = true;

        collection.RemoveRange(0, 0);

        Assert.False(raised);
        Assert.Equal(new[] { 1, 2 }, collection.ToArray());
    }

    [Fact]
    public void RemoveRange_Throws_When_Index_Negative()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });

        Assert.Throws<ArgumentOutOfRangeException>(() => collection.RemoveRange(-1, 1));
    }

    [Fact]
    public void RemoveRange_Throws_When_End_Beyond_Count()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });

        Assert.Throws<ArgumentOutOfRangeException>(() => collection.RemoveRange(1, 2));
    }

    [Fact]
    public void ResetWith_Throws_When_Items_Null()
    {
        var collection = new ObservableRangeCollection<int>();

        Assert.Throws<ArgumentNullException>(() => collection.ResetWith(null!));
    }

    [Fact]
    public void ResetWith_Raises_Reset_And_Replaces_Items()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });
        NotifyCollectionChangedEventArgs? args = null;
        var propertyChanges = new List<string?>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName);
        collection.CollectionChanged += (_, e) => args = e;

        collection.ResetWith(new[] { 3, 4 });

        Assert.Equal(new[] { 3, 4 }, collection.ToArray());
        Assert.NotNull(args);
        Assert.Equal(NotifyCollectionChangedAction.Reset, args!.Action);
        Assert.Contains(nameof(collection.Count), propertyChanges);
        Assert.Contains("Item[]", propertyChanges);
    }

    private sealed class GenericOnlyList<T> : IList<T>
    {
        private readonly List<T> _items = new();

        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            _items.Add(item);
        }

        public void Clear()
        {
            _items.Clear();
        }

        public bool Contains(T item)
        {
            return _items.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _items.CopyTo(array, arrayIndex);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        public int IndexOf(T item)
        {
            return _items.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            _items.Insert(index, item);
        }

        public bool Remove(T item)
        {
            return _items.Remove(item);
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }
    }

    private static IEnumerable<int> YieldItems()
    {
        yield return 1;
        yield return 2;
        yield return 3;
    }
}
