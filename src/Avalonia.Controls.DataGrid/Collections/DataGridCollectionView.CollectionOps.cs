// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

#nullable disable

using Avalonia.Controls;
using Avalonia.Controls.Utils;
using Avalonia.Utilities;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System;

namespace Avalonia.Collections
{
    sealed partial class DataGridCollectionView
    {
        /// <summary>
        /// Return true if the item belongs to this view.  No assumptions are
        /// made about the item. This method will behave similarly to IList.Contains().
        /// If the caller knows that the item belongs to the
        /// underlying collection, it is more efficient to call PassesFilter.
        /// </summary>
        /// <param name="item">The item we are checking to see whether it is within the collection</param>
        /// <returns>Boolean value of whether or not the collection contains the item</returns>
        public bool Contains(object item)
        {
            EnsureCollectionInSync();
            VerifyRefreshNotDeferred();
            return IndexOf(item) >= 0;
        }

        /// <summary>
        /// Enter a Defer Cycle.
        /// Defer cycles are used to coalesce changes to the ICollectionView.
        /// </summary>
        /// <returns>IDisposable used to notify that we no longer need to defer, when we dispose</returns>
        public IDisposable DeferRefresh()
        {
            if (IsAddingNew || IsEditingItem)
            {
                throw new InvalidOperationException(GetOperationNotAllowedDuringAddOrEditText(nameof(DeferRefresh)));
            }

            ++_deferLevel;
            return new DeferHelper(this);
        }

        /// <summary>
        /// Implementation of IEnumerable.GetEnumerator().
        /// This provides a way to enumerate the members of the collection
        /// without changing the currency.
        /// </summary>
        /// <returns>IEnumerator for the collection</returns>
        //TODO Paging
        public IEnumerator GetEnumerator()
        {
            EnsureCollectionInSync();
            VerifyRefreshNotDeferred();

            if (IsGrouping)
            {
                return RootGroup?.GetLeafEnumerator();
            }

            // if we are paging
            if (PageSize > 0)
            {
                List<object> list = new List<object>();

                // if we are in the middle of asynchronous load
                if (PageIndex < 0)
                {
                    return list.GetEnumerator();
                }

                // Built from the page's own indexes rather than copied out of a window onto the
                // internal list. While an add is pending the page gives its last slot to the new
                // item, so the row that slot displaced has already moved to the next page - but the
                // internal list still holds that row inside the window, and may hold the new item
                // outside it. Copying the window would hand back the displaced row as well as the
                // new one: more rows than Count promises, which is what the row is enumerated
                // through rather than indexed.
                int count = Count;
                for (int index = 0; index < count; index++)
                {
                    list.Add(ItemAtUngrouped(index));
                }

                // The new item is already in its place above, so there is nothing left for the
                // enumerator to position.
                return new NewItemAwareEnumerator(this, list.GetEnumerator(), newItem: null);
            }
            else
            {
                return new NewItemAwareEnumerator(this, InternalList.GetEnumerator(), CurrentAddItem);
            }
        }

        /// <summary>
        /// Retrieve item at the given zero-based index in this DataGridCollectionView, after the source collection
        /// is filtered, sorted, and paged.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if index is out of range
        /// </exception>
        /// <param name="index">Index of the item we want to retrieve</param>
        /// <returns>Item at specified index</returns>
        public object GetItemAt(int index)
        {
            EnsureCollectionInSync();
            VerifyRefreshNotDeferred();

            // for indices larger than the count
            if (index >= Count || index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (IsGrouping)
            {
                return RootGroup?.LeafAt(_isUsingTemporaryGroup ? ConvertToInternalIndex(index) : index);
            }

            return ItemAtUngrouped(index);
        }

        /// <summary>
        /// The item this view shows at <paramref name="index"/> while it is not grouping.
        /// </summary>
        /// <remarks>
        /// The one place that decides where a pending new item sits - the last slot the view has -
        /// so that the indexer and <see cref="GetEnumerator"/> cannot disagree about what the view
        /// is showing. They did once: the enumerator worked from the internal list, which puts the
        /// new item wherever the add happened to leave it rather than where the view shows it.
        /// </remarks>
        private object ItemAtUngrouped(int index)
        {
            if (IsAddingNew && UsesLocalArray && index == Count - 1)
            {
                return CurrentAddItem;
            }

            return InternalItemAt(ConvertToInternalIndex(index));
        }

        public Func<int, object> GetItemAtFx(Func<int> countFx)
        {
            EnsureCollectionInSync();
            VerifyRefreshNotDeferred();

            if (IsGrouping)
            {
                if (_isUsingTemporaryGroup)
                {
                    return (index) => {
                        // for indices larger than the count
                        if (index >= countFx() || index < 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(index));
                        }
                        return RootGroup?.LeafAt(ConvertToInternalIndex(index));
                    };
                }
                else
                {
                    return (index) => {
                        // for indices larger than the count
                        if (index >= countFx() || index < 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(index));
                        }
                        return RootGroup?.LeafAt(index);
                    };
                }
            }

            if (IsAddingNew && UsesLocalArray)
            {
                return (index) => {
                    // for indices larger than the count
                    if (index >= countFx() || index < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    if (index == countFx() - 1)
                    {
                        return CurrentAddItem;
                    }
                    return InternalItemAt(ConvertToInternalIndex(index));
                };
            }
            else
            {
                return (index) => {
                    // for indices larger than the count
                    if (index >= countFx() || index < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return InternalItemAt(ConvertToInternalIndex(index));
                };
            }
        }

        /// <summary>
        /// Return the index where the given item appears, or -1 if doesn't appear.
        /// </summary>
        /// <param name="item">Item we are searching for</param>
        /// <returns>Index of specified item</returns>
        //TODO Paging
        public int IndexOf(object item)
        {
            EnsureCollectionInSync();
            VerifyRefreshNotDeferred();

            if (IsGrouping)
            {
                return RootGroup?.LeafIndexOf(item) ?? -1;
            }
            if (IsAddingNew && Object.Equals(item, CurrentAddItem) && UsesLocalArray)
            {
                return Count - 1;
            }

            int internalIndex = InternalIndexOf(item);

            if (PageSize > 0 && internalIndex != -1)
            {
                if ((internalIndex >= (PageIndex * _pageSize)) &&
                (internalIndex < ((PageIndex + 1) * _pageSize)))
                {
                    return internalIndex - (PageIndex * _pageSize);
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                return internalIndex;
            }
        }

        /// <summary>
        /// Return true if the item belongs to this view.  The item is assumed to belong to the
        /// underlying DataCollection;  this method merely takes filters into account.
        /// It is commonly used during collection-changed notifications to determine if the added/removed
        /// item requires processing.
        /// Returns true if no filter is set on collection view.
        /// </summary>
        /// <param name="item">The item to compare against the Filter</param>
        /// <returns>Whether the item passes the filter</returns>
        public bool PassesFilter(object item)
        {
            if (Filter != null)
            {
                return Filter(item);
            }

            return true;
        }

        /// <summary>
        /// Re-create the view, using any SortDescriptions and/or Filters.
        /// </summary>
        public void Refresh()
        {
            if (this is IDataGridEditableCollectionView ecv && (ecv.IsAddingNew || ecv.IsEditingItem))
            {
                throw new InvalidOperationException(GetOperationNotAllowedDuringAddOrEditText(nameof(Refresh)));
            }

            RefreshInternal();
        }

        /// <summary>
        /// Remove the given item from the underlying collection. It
        /// needs to be in the current filtered, sorted, and paged view
        /// to call
        /// </summary>
        /// <param name="item">Item we want to remove</param>
        public void Remove(object item)
        {
            int index = IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        /// <summary>
        /// Remove the item at the given index from the underlying collection.
        /// The index is interpreted with respect to the view (filtered, sorted,
        /// and paged list).
        /// </summary>
        /// <param name="index">Index of the item we want to remove</param>
        //TODO Paging
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Index was out of range. Must be non-negative and less than the size of the collection.");
            }

            if (IsEditingItem || IsAddingNew)
            {
                throw new InvalidOperationException(GetOperationNotAllowedDuringAddOrEditText(nameof(RemoveAt)));
            }
            else if (!CanRemove)
            {
                throw new InvalidOperationException("Remove/RemoveAt is not supported.");
            }

            VerifyRefreshNotDeferred();

            // convert the index from "view-relative" to "list-relative"
            object item = GetItemAt(index);

            // before we remove the item, see if we are not on the last page
            // and will have to bring in a new item to replace it
            bool replaceItem = PageSize > 0 && !OnLastLocalPage;

            try
            {
                // temporarily disable the CollectionChanged event
                // handler so filtering, sorting, or grouping
                // doesn't get applied yet
                SetFlag(CollectionViewFlags.ShouldProcessCollectionChanged, false);

                if (SourceList != null)
                {
                    SourceList.Remove(item);
                }
            }
            finally
            {
                SetFlag(CollectionViewFlags.ShouldProcessCollectionChanged, true);
            }

            // Modify our _trackingEnumerator so that it shows that our collection is "up to date"
            // and will not refresh for now.
            _trackingEnumerator = _sourceCollection.GetEnumerator();

            if (!IsUsingSourceList)
            {
                Debug.Assert(index == IndexOf(item), "IndexOf returned unexpected value");

                // remove the item from the internal list
                _internalList.Remove(item);
            }

            if (IsGrouping)
            {
                if (PageSize > 0)
                {
                    _temporaryGroup.RemoveFromSubgroups(item);
                }
                _group.RemoveFromSubgroups(item);
            }

            object oldCurrentItem = CurrentItem;
            int oldCurrentPosition = CurrentPosition;
            bool oldIsCurrentAfterLast = IsCurrentAfterLast;
            bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;

            AdjustCurrencyForRemove(index);

            // fire remove notification
            OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove,
            item,
            index));

            RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);

            // if we removed all items from the current page,
            // move to the previous page. we do not need to
            // fire additional notifications, as moving the page will
            // trigger a reset.
            if (NeedToMoveToPreviousPage)
            {
                MoveToPreviousPage();
                return;
            }

            // if we are paging, we may have to fire another notification for the item
            // that needs to replace the one we removed on this page.
            if (replaceItem)
            {
                // we first need to add the item into the current group
                if (IsGrouping)
                {
                    object newItem = _temporaryGroup.LeafAt((PageSize * (PageIndex + 1)) - 1);
                    if (newItem != null)
                    {
                        _group.AddToSubgroups(newItem, loading: false);
                    }
                }

                // fire the add notification
                OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                GetItemAt(PageSize - 1),
                PageSize - 1));
            }
        }

        /// <summary>
        /// Process an Add operation from an INotifyCollectionChanged event handler.
        /// </summary>
        /// <param name="addedItem">Item added to the source collection</param>
        /// <param name="addIndex">Index item was added into</param>
        //TODO Paging
        private void ProcessAddEvent(object addedItem, int addIndex)
        {
            if (IsUsingSourceList)
            {
                ProcessAddEventUsingSourceList(addedItem, addIndex);
                return;
            }

            // item to fire remove notification for if necessary
            object removeNotificationItem = null;
            if (PageSize > 0 && !IsGrouping)
            {
                removeNotificationItem = (Count == PageSize) ?
                GetItemAt(PageSize - 1) : null;
            }

            // process the add by filtering and sorting the item
            ProcessInsertToCollection(
            addedItem,
            addIndex);

            // next check if we need to add an item into the current group
            bool needsGrouping = false;
            if (Count == 1 && GroupDescriptions.Count > 0)
            {
                // if this is the first item being added
                // we want to setup the groups with the
                // correct element type comparer
                if (PageSize > 0)
                {
                    PrepareGroupingComparer(_temporaryGroup);
                }
                PrepareGroupingComparer(_group);
            }

            if (IsGrouping)
            {
                int leafIndex = -1;

                if (PageSize > 0)
                {
                    _temporaryGroup.AddToSubgroups(addedItem, false /*loading*/);
                    leafIndex = _temporaryGroup.LeafIndexOf(addedItem);
                }

                // if we are not paging, we should just be able to add the item.
                // otherwise, we need to validate that it is within the current page.
                if (PageSize == 0 || (PageIndex + 1) * PageSize > leafIndex)
                {
                    needsGrouping = true;

                    int pageStartIndex = PageIndex * PageSize;

                    // if the item was inserted on a previous page
                    if (pageStartIndex > leafIndex && PageSize > 0)
                    {
                        addedItem = _temporaryGroup.LeafAt(pageStartIndex);
                    }

                    // if we're grouping and have more items than the
                    // PageSize will allow, remove the last item
                    if (PageSize > 0 && _group.ItemCount == PageSize)
                    {
                        removeNotificationItem = _group.LeafAt(PageSize - 1);
                        _group.RemoveFromSubgroups(removeNotificationItem);
                    }
                }
            }

            // if we are paging, we may have to fire another notification for the item
            // that needs to be removed for the one we added on this page.
            if (PageSize > 0 && !OnLastLocalPage &&
            (((IsGrouping && removeNotificationItem != null) ||
            (!IsGrouping && (PageIndex + 1) * PageSize > InternalIndexOf(addedItem)))))
            {
                if (removeNotificationItem != null && removeNotificationItem != addedItem)
                {
                    AdjustCurrencyForRemove(PageSize - 1);

                    OnCollectionChanged(
                    new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove,
                    removeNotificationItem,
                    PageSize - 1));
                }
            }

            // if we need to add the item into the current group
            // that will be displayed
            if (needsGrouping)
            {
                this._group.AddToSubgroups(addedItem, false /*loading*/);
            }

            int addedIndex = IndexOf(addedItem);

            // if the item is within the current page
            if (addedIndex >= 0)
            {
                object oldCurrentItem = CurrentItem;
                int oldCurrentPosition = CurrentPosition;
                bool oldIsCurrentAfterLast = IsCurrentAfterLast;
                bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;

                AdjustCurrencyForAdd(null, addedIndex);

                // fire add notification
                OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                addedItem,
                addedIndex));

                RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);
            }
            else if (PageSize > 0)
            {
                // otherwise if the item was added into a previous page
                int internalIndex = InternalIndexOf(addedItem);

                if (internalIndex < ConvertToInternalIndex(0))
                {
                    // fire add notification for item pushed in
                    OnCollectionChanged(
                    new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    GetItemAt(0),
                    0));
                }
            }
        }

        private void ProcessAddEventUsingSourceList(object addedItem, int addIndex)
        {
            int addedIndex = addIndex;
            if (addedIndex < 0 || addedIndex >= Count)
            {
                addedIndex = IndexOf(addedItem);
            }

            if (addedIndex < 0)
            {
                return;
            }

            object oldCurrentItem = CurrentItem;
            int oldCurrentPosition = CurrentPosition;
            bool oldIsCurrentAfterLast = IsCurrentAfterLast;
            bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;

            AdjustCurrencyForAdd(null, addedIndex);

            OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            addedItem,
            addedIndex));

            RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);
        }

        /// <summary>
        /// Process CollectionChanged event on source collection
        /// that implements INotifyCollectionChanged.
        /// </summary>
        /// <param name="args">
        /// The NotifyCollectionChangedEventArgs to be processed.
        /// </param>
        private void ProcessCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            // if we do not want to handle the CollectionChanged event, return
            if (!CheckFlag(CollectionViewFlags.ShouldProcessCollectionChanged))
            {
                return;
            }

            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                if (!IsUsingSourceList)
                {
                    // if we have no items now, clear our own internal list
                    if (!SourceCollection.GetEnumerator().MoveNext())
                    {
                        _internalList.Clear();
                    }
                }

                // calling Refresh, will fire the collectionchanged event
                RefreshOrDefer();
                return;
            }

            if (args.Action == NotifyCollectionChangedAction.Move)
            {
                if (args.OldStartingIndex < 0 || args.NewStartingIndex < 0)
                {
                    RefreshOrDefer();
                    return;
                }

                if (args.OldItems is { Count: 1 })
                {
                    ProcessMoveEvent(args.OldItems[0], args.OldStartingIndex, args.NewStartingIndex);
                }
                else if (args.OldItems is { Count: > 1 })
                {
                    ProcessMoveRangeEvent(args.OldItems, args.OldStartingIndex, args.NewStartingIndex);
                }
                else
                {
                    RefreshOrDefer();
                }

                return;
            }

            if (args.Action == NotifyCollectionChangedAction.Replace &&
                args.OldItems != null &&
                args.NewItems != null &&
                ProcessReplaceEvent(args.OldItems, args.NewItems, args.OldStartingIndex))
            {
                return;
            }

            if (IsUsingSourceList &&
                (args.Action == NotifyCollectionChangedAction.Remove ||
                args.Action == NotifyCollectionChangedAction.Replace) &&
                args.OldStartingIndex < 0)
            {
                RefreshOrDefer();
                return;
            }

            // fire notifications for removes
            if (args.OldItems != null &&
            (args.Action == NotifyCollectionChangedAction.Remove ||
            args.Action == NotifyCollectionChangedAction.Replace))
            {
                var isReplace = args.Action == NotifyCollectionChangedAction.Replace;
                var oldStartingIndex = args.OldStartingIndex;

                if (oldStartingIndex >= 0 && args.OldItems.Count > 1)
                {
                    for (var i = args.OldItems.Count - 1; i >= 0; i--)
                    {
                        ProcessRemoveEvent(args.OldItems[i], isReplace, oldStartingIndex + i);
                    }
                }
                else
                {
                    for (var i = 0; i < args.OldItems.Count; i++)
                    {
                        var indexHint = oldStartingIndex >= 0 ? oldStartingIndex + i : (int?)null;
                        ProcessRemoveEvent(args.OldItems[i], isReplace, indexHint);
                    }
                }
            }

            // fire notifications for adds
            if (args.NewItems != null &&
            (args.Action == NotifyCollectionChangedAction.Add ||
            args.Action == NotifyCollectionChangedAction.Replace))
            {
                for (var i = 0; i < args.NewItems.Count; i++)
                {
                    if (Filter == null || PassesFilter(args.NewItems[i]))
                    {
                        ProcessAddEvent(args.NewItems[i], args.NewStartingIndex + i);
                    }
                }
            }
            if (args.Action != NotifyCollectionChangedAction.Replace)
            {
                OnPropertyChanged(nameof(ItemCount));
            }
        }

        /// <summary>
        /// Process a Replace operation from an INotifyCollectionChanged event handler, keeping it a
        /// replacement instead of splitting it into a removal and an insertion.
        /// </summary>
        /// <param name="oldItems">The items that gave up their positions.</param>
        /// <param name="newItems">The items that took them, paired off in order.</param>
        /// <param name="startingIndex">Where the run of replaced items starts.</param>
        /// <returns>
        /// False when the view cannot honestly report it as one replacement, in which case the
        /// caller falls back to the remove-then-add path.
        /// </returns>
        /// <remarks>
        /// The old path always split a Replace into per-item removes and adds - it even carried an
        /// isReplace flag through <see cref="ProcessRemoveEvent"/> to patch up the differences,
        /// which is the tell. Consumers were told the item had been taken away and an unrelated one
        /// put there, so anything keyed on the item lost its hold on the row: selection above all.
        /// </remarks>
        private bool ProcessReplaceEvent(IList oldItems, IList newItems, int startingIndex)
        {
            if (oldItems.Count == 0 || oldItems.Count != newItems.Count)
            {
                return false;
            }

            // A filter can admit one of the pair and not the other, and grouping and paging can
            // drop them in different buckets. In those cases it genuinely is a removal and an
            // insertion.
            if (GroupDescriptions.Count > 0 || Filter != null || PageSize > 0)
            {
                return false;
            }

            if (SortDescriptions.Count > 0)
            {
                // Sorted order comes from the comparer, so the replacement may not belong where the
                // replaced item stood. Still a replacement - just one that may have to move
                // afterwards. Source indexes mean nothing here, so the pairing has to be resolved
                // item by item and only a single replacement is worth untangling.
                return oldItems.Count == 1 && ProcessSortedReplaceEvent(oldItems[0], newItems[0]);
            }

            if (startingIndex < 0)
            {
                return false;
            }

            if (startingIndex + oldItems.Count > _internalList.Count)
            {
                return false;
            }

            if (IsUsingSourceList)
            {
                // The storage is the source collection, so it already holds the replacements.
                for (var i = 0; i < newItems.Count; i++)
                {
                    if (!Equals(_internalList[startingIndex + i], newItems[i]))
                    {
                        return false;
                    }
                }
            }
            else
            {
                for (var i = 0; i < oldItems.Count; i++)
                {
                    if (!Equals(_internalList[startingIndex + i], oldItems[i]))
                    {
                        // The view is not laid out the way the notification says it is.
                        return false;
                    }
                }

                for (var i = 0; i < newItems.Count; i++)
                {
                    _internalList[startingIndex + i] = newItems[i];
                }
            }

            object oldCurrentItem = _currentItem;
            int oldCurrentPosition = CurrentPosition;
            bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;
            bool oldIsCurrentAfterLast = IsCurrentAfterLast;

            // Currency is a position and the positions are untouched, but whoever is standing at the
            // current one may just have been swapped out from under it.
            if (oldCurrentPosition >= startingIndex && oldCurrentPosition < startingIndex + newItems.Count)
            {
                SetCurrent(newItems[oldCurrentPosition - startingIndex], oldCurrentPosition);
            }

            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace,
                    newItems,
                    oldItems,
                    startingIndex));

            RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);
            return true;
        }

        /// <summary>
        /// Replaces a single item while a sort is in force, repositioning the replacement when its
        /// sort key belongs somewhere other than where the replaced item stood.
        /// </summary>
        /// <remarks>
        /// Sorting is evaluated when an item is inserted, so it has to be evaluated here too. What
        /// it must not turn into is a removal and an insertion: a replacement followed by a move
        /// says exactly what happened and leaves the row - and anything keyed on it - intact, where
        /// remove-then-add reports the item as having left the collection.
        /// </remarks>
        private bool ProcessSortedReplaceEvent(object oldItem, object newItem)
        {
            var viewIndex = InternalIndexOf(oldItem);
            if (viewIndex < 0)
            {
                return false;
            }

            var itemType = ItemType;
            foreach (var sort in SortDescriptions)
            {
                sort.Initialize(itemType);
            }

            var comparer = new MergedComparer(this);

            object oldCurrentItem = _currentItem;
            int oldCurrentPosition = CurrentPosition;
            bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;
            bool oldIsCurrentAfterLast = IsCurrentAfterLast;

            _internalList[viewIndex] = newItem;

            if (oldCurrentPosition == viewIndex)
            {
                SetCurrent(newItem, viewIndex);
            }

            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace,
                    newItem,
                    oldItem,
                    viewIndex));

            // Where the sort puts it among the others. The rest of the list is in order, so the
            // run of items that belong ahead of it ends at the first one that does not.
            var target = 0;
            for (var i = 0; i < _internalList.Count; i++)
            {
                if (i == viewIndex)
                {
                    continue;
                }

                if (comparer.Compare(_internalList[i], newItem) > 0)
                {
                    break;
                }

                target++;
            }

            if (target != viewIndex)
            {
                RotateInternalList(viewIndex, target, 1);

                if (CurrentPosition == viewIndex)
                {
                    SetCurrent(newItem, target);
                }
                else
                {
                    // The rotation slid every item between the two indices along, and whoever was
                    // current may have been one of them. Currency is on an item, so the position
                    // has to follow it rather than stay where it was.
                    AdjustCurrencyForMove(oldCurrentItem);
                }

                OnCollectionChanged(
                    new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Move,
                        newItem,
                        target,
                        viewIndex));
            }

            RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);
            return true;
        }

        /// <summary>
        /// Relocates a run within <see cref="_internalList"/> by rotating the span it travels over.
        /// </summary>
        private void RotateInternalList(int oldIndex, int newIndex, int count)
        {
            if (count <= 0 || oldIndex == newIndex)
            {
                return;
            }

            var moved = new object[count];
            for (var i = 0; i < count; i++)
            {
                moved[i] = _internalList[oldIndex + i];
            }

            if (newIndex > oldIndex)
            {
                for (var i = 0; i < newIndex - oldIndex; i++)
                {
                    _internalList[oldIndex + i] = _internalList[oldIndex + count + i];
                }
            }
            else
            {
                for (var i = oldIndex - newIndex - 1; i >= 0; i--)
                {
                    _internalList[newIndex + count + i] = _internalList[newIndex + i];
                }
            }

            for (var i = 0; i < count; i++)
            {
                _internalList[newIndex + i] = moved[i];
            }
        }

        /// <summary>
        /// Process a Remove operation from an INotifyCollectionChanged event handler.
        /// </summary>
        /// <param name="removedItem">Item removed from the source collection</param>
        /// <param name="isReplace">Whether this was part of a Replace operation</param>
        //TODO Paging
        private void ProcessRemoveEvent(object removedItem, bool isReplace, int? oldIndexHint = null)
        {
            if (IsUsingSourceList)
            {
                ProcessRemoveEventUsingSourceList(removedItem, isReplace, oldIndexHint);
                return;
            }

            int internalRemoveIndex = -1;
            int removeIndex = -1;

            bool usingOriginalOrder = SortDescriptions.Count == 0 && Filter == null && GroupDescriptions.Count == 0;

            if (usingOriginalOrder && oldIndexHint.HasValue)
            {
                internalRemoveIndex = oldIndexHint.Value;

                if (PageSize > 0)
                {
                    int pageStartIndex = PageIndex * PageSize;
                    int pageEndIndex = (PageIndex + 1) * PageSize;
                    removeIndex = (internalRemoveIndex >= pageStartIndex && internalRemoveIndex < pageEndIndex) ?
                        internalRemoveIndex - pageStartIndex :
                        -1;
                }
                else
                {
                    removeIndex = internalRemoveIndex;
                }
            }
            else if (IsGrouping)
            {
                internalRemoveIndex = PageSize > 0 ? _temporaryGroup.LeafIndexOf(removedItem) :
                _group.LeafIndexOf(removedItem);
            }
            else
            {
                internalRemoveIndex = InternalIndexOf(removedItem);
            }

            if (removeIndex < 0)
            {
                removeIndex = IndexOf(removedItem);
            }

            // remove the item from the collection
            if (internalRemoveIndex >= 0 && internalRemoveIndex < _internalList.Count)
            {
                _internalList.RemoveAt(internalRemoveIndex);
            }
            else
            {
                _internalList.Remove(removedItem);
            }

            // only fire the remove if it was removed from either the current page, or a previous page
            bool needToRemove;
            if (PageSize == 0)
            {
                needToRemove = removeIndex >= 0;
            }
            else
            {
                needToRemove = internalRemoveIndex >= 0 && internalRemoveIndex < (PageIndex + 1) * PageSize;
            }

            if (IsGrouping)
            {
                if (PageSize > 0)
                {
                    _temporaryGroup.RemoveFromSubgroups(removedItem);
                }

                if (needToRemove)
                {
                    _group.RemoveFromSubgroups(removeIndex >= 0 ? removedItem : _group.LeafAt(0));
                }
            }

            if (needToRemove)
            {
                object oldCurrentItem = CurrentItem;
                int oldCurrentPosition = CurrentPosition;
                bool oldIsCurrentAfterLast = IsCurrentAfterLast;
                bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;

                AdjustCurrencyForRemove(removeIndex);

                // fire remove notification
                // if we removed from current page, remove from removeIndex,
                // if we removed from previous page, remove first item (index=0)
                OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove,
                removedItem,
                Math.Max(0, removeIndex)));

                RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);

                // if we removed all items from the current page,
                // move to the previous page. we do not need to
                // fire additional notifications, as moving the page will
                // trigger a reset.
                if (NeedToMoveToPreviousPage && !isReplace)
                {
                    MoveToPreviousPage();
                    return;
                }

                // if we are paging, we may have to fire another notification for the item
                // that needs to replace the one we removed on this page.
                if (PageSize > 0 && Count == PageSize)
                {
                    // we first need to add the item into the current group
                    if (IsGrouping)
                    {
                        object newItem = _temporaryGroup.LeafAt((PageSize * (PageIndex + 1)) - 1);
                        if (newItem != null)
                        {
                            _group.AddToSubgroups(newItem, false /*loading*/);
                        }
                    }

                    // fire the add notification
                    OnCollectionChanged(
                    new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    GetItemAt(PageSize - 1),
                    PageSize - 1));
                }
            }
        }

        private void ProcessRemoveEventUsingSourceList(object removedItem, bool isReplace, int? oldIndexHint)
        {
            int removeIndex = oldIndexHint ?? -1;
            if (removeIndex < 0)
            {
                RefreshOrDefer();
                return;
            }

            object oldCurrentItem = CurrentItem;
            int oldCurrentPosition = CurrentPosition;
            bool oldIsCurrentAfterLast = IsCurrentAfterLast;
            bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;

            AdjustCurrencyForRemove(removeIndex);

            OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove,
            removedItem,
            removeIndex));

            RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);

            if (NeedToMoveToPreviousPage && !isReplace)
            {
                MoveToPreviousPage();
            }
        }

        /// <summary>
        /// Process a Move operation from an INotifyCollectionChanged event handler.
        /// </summary>
        /// <param name="movedItem">Item moved in the source collection.</param>
        /// <param name="oldIndex">Original index in the source collection.</param>
        /// <param name="newIndex">New index in the source collection.</param>
        private void ProcessMoveEvent(object movedItem, int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex)
            {
                return;
            }

            if (IsUsingSourceList)
            {
                ProcessMoveEventUsingSourceList(movedItem, oldIndex, newIndex);
                return;
            }

            if (SortDescriptions.Count > 0)
            {
                // Sorted order comes from the comparer, and a move changes no item's sort key, so
                // the view is already showing the right thing - including any grouping or paging
                // derived from it. This used to refresh, which told consumers the whole collection
                // had been replaced in answer to a change that moved nothing they could see.
                return;
            }

            if (GroupDescriptions.Count > 0 || PageSize > 0)
            {
                RefreshOrDefer();
                return;
            }

            ProcessMoveEventUsingLocalArray(movedItem, newIndex);
        }

        /// <summary>
        /// Process a Move of a run of items from an INotifyCollectionChanged event handler.
        /// </summary>
        /// <param name="movedItems">The items that travelled together, in order.</param>
        /// <param name="oldIndex">Where the run started in the source collection.</param>
        /// <param name="newIndex">Where it starts in the source collection now.</param>
        /// <remarks>
        /// A run relocating is one move, and it is reported as one. It used to fall through to a
        /// full refresh, which told every consumer the whole collection had been replaced - the
        /// caller had gone to the trouble of saying precisely what happened and the view threw that
        /// away. A hierarchical row with expanded children moves as a run, so this was the ordinary
        /// case, not a corner of one.
        /// </remarks>
        private void ProcessMoveRangeEvent(IList movedItems, int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex || movedItems.Count == 0)
            {
                return;
            }

            if (!IsUsingSourceList)
            {
                if (SortDescriptions.Count > 0)
                {
                    // As with a single item: sorted order comes from the comparer and a move
                    // changes no sort key, so there is nothing for the view to do.
                    return;
                }

                // Grouping, filtering and paging break the correspondence between source order and
                // view order, so a run that is contiguous at the source need not be contiguous -
                // or even present - here. Those stay a refresh.
                if (Filter != null || GroupDescriptions.Count > 0 || PageSize > 0)
                {
                    RefreshOrDefer();
                    return;
                }

                if (oldIndex + movedItems.Count > _internalList.Count ||
                    newIndex + movedItems.Count > _internalList.Count)
                {
                    RefreshOrDefer();
                    return;
                }
            }

            object oldCurrentItem = CurrentItem;
            int oldCurrentPosition = CurrentPosition;
            bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;
            bool oldIsCurrentAfterLast = IsCurrentAfterLast;

            if (!IsUsingSourceList)
            {
                // View order tracks source order here, so the same rotation applies.
                RotateInternalList(oldIndex, newIndex, movedItems.Count);
            }

            AdjustCurrencyForMove(oldCurrentItem);

            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Move,
                    movedItems,
                    newIndex,
                    oldIndex));

            RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);
        }

        private void ProcessMoveEventUsingSourceList(object movedItem, int oldIndex, int newIndex)
        {
            object oldCurrentItem = CurrentItem;
            int oldCurrentPosition = CurrentPosition;
            bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;
            bool oldIsCurrentAfterLast = IsCurrentAfterLast;

            AdjustCurrencyForMove(oldCurrentItem);

            OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Move,
            movedItem,
            newIndex,
            oldIndex));

            RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);
        }

        private void ProcessMoveEventUsingLocalArray(object movedItem, int newSourceIndex)
        {
            if (Filter != null && !PassesFilter(movedItem))
            {
                return;
            }

            var oldViewIndex = InternalIndexOf(movedItem);
            if (oldViewIndex < 0)
            {
                RefreshOrDefer();
                return;
            }

            var newViewIndex = GetFilteredMoveIndex(newSourceIndex, movedItem);
            if (newViewIndex < 0)
            {
                RefreshOrDefer();
                return;
            }

            if (oldViewIndex == newViewIndex)
            {
                return;
            }

            object oldCurrentItem = CurrentItem;
            int oldCurrentPosition = CurrentPosition;
            bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;
            bool oldIsCurrentAfterLast = IsCurrentAfterLast;

            var item = _internalList[oldViewIndex];
            _internalList.RemoveAt(oldViewIndex);
            _internalList.Insert(newViewIndex, item);

            AdjustCurrencyForMove(oldCurrentItem);

            OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Move,
            movedItem,
            newViewIndex,
            oldViewIndex));

            RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);
        }

        private int GetFilteredMoveIndex(int newSourceIndex, object movedItem)
        {
            if (SourceList != null && newSourceIndex >= 0 && newSourceIndex < SourceList.Count)
            {
                return CountPassingItemsBeforeSourceIndex(newSourceIndex);
            }

            var index = 0;
            foreach (object item in SourceCollection)
            {
                if (IsMoveItemMatch(item, movedItem))
                {
                    return index;
                }

                if (Filter == null || PassesFilter(item))
                {
                    index++;
                }
            }

            return -1;
        }

        private int CountPassingItemsBeforeSourceIndex(int sourceIndex)
        {
            var index = 0;
            for (var i = 0; i < sourceIndex; i++)
            {
                var item = SourceList[i];
                if (Filter == null || PassesFilter(item))
                {
                    index++;
                }
            }

            return index;
        }

        private static bool IsMoveItemMatch(object item, object movedItem)
        {
            if (ReferenceEquals(item, movedItem))
            {
                return true;
            }

            return movedItem == null ? item == null : movedItem.Equals(item);
        }

        private void AdjustCurrencyForMove(object oldCurrentItem)
        {
            if (IsCurrentBeforeFirst || IsCurrentAfterLast)
            {
                return;
            }

            var newPosition = oldCurrentItem == null
                ? IndexOf(null)
                : ReferenceIndexOf(oldCurrentItem);

            if (newPosition < 0 && oldCurrentItem != null)
            {
                newPosition = IndexOf(oldCurrentItem);
            }

            if (newPosition >= 0 && (newPosition != CurrentPosition || !IsCurrentInSync))
            {
                OnCurrentChanging();
                SetCurrent(oldCurrentItem, newPosition);
            }
        }

        /// <summary>
        /// Handles adding an item into the collection, and applying sorting, filtering, grouping, paging.
        /// </summary>
        /// <param name="item">Item to insert in the collection</param>
        /// <param name="index">Index to insert item into</param>
        private void ProcessInsertToCollection(object item, int index)
        {
            // first check to see if it passes the filter
            if (Filter == null || PassesFilter(item))
            {
                if (SortDescriptions.Count > 0)
                {
                    var itemType = ItemType;
                    foreach (var sort in SortDescriptions)
                    sort.Initialize(itemType);

                    // create the SortFieldComparer to use
                    var sortFieldComparer = new MergedComparer(this);

                    // check if the item would be in sorted order if inserted into the specified index
                    // otherwise, calculate the correct sorted index.
                    // The second bound is Count, not Count - 1: the test is whether there is an item
                    // at index for the new one to be sitting in front of, and the last position has
                    // one just as much as any other. With Count - 1 an item landing there was never
                    // compared, so a replacement whose sort key put it last stayed where the removal
                    // had left it - {1,2,3} sorted ascending became {1,5,3} on replacing 2 with 5.
                    if (index < 0 || /* if item was not originally part of list */
                    (index > 0 && (sortFieldComparer.Compare(item, InternalItemAt(index - 1)) < 0)) || /* item has moved up in the list */
                    ((index < InternalList.Count) && (sortFieldComparer.Compare(item, InternalItemAt(index)) > 0))) /* item has moved down in the list */
                    {
                        index = sortFieldComparer.FindInsertIndex(item, _internalList);
                    }
                }

                // make sure that the specified insert index is within the valid range
                // otherwise, just add it to the end. the index can be set to an invalid
                // value if the item was originally not in the collection, on a different
                // page, or if it had been previously filtered out.
                if (index < 0 || index > _internalList.Count)
                {
                    index = _internalList.Count;
                }

                _internalList.Insert(index, item);
            }
        }

        /// <summary>
        /// Will call RefreshOverride and clear the NeedsRefresh flag
        /// </summary>
        private void RefreshInternal()
        {
            RefreshOverride();
            SetFlag(CollectionViewFlags.NeedsRefresh, false);
        }

        /// <summary>
        /// Refresh, or mark that refresh is needed when defer cycle completes.
        /// </summary>
        private void RefreshOrDefer()
        {
            if (IsRefreshDeferred)
            {
                SetFlag(CollectionViewFlags.NeedsRefresh, true);
            }
            else
            {
                RefreshInternal();
            }
        }

        /// <summary>
        /// Re-create the view, using any SortDescriptions.
        /// Also updates currency information.
        /// </summary>
        //TODO Paging
        private void RefreshOverride()
        {
            using var activity = DataGridDiagnostics.CollectionRefresh();
            using var _ = DataGridDiagnostics.BeginCollectionRefresh();
            activity?.SetTag(DataGridDiagnostics.Tags.UsesLocalArray, UsesLocalArray);
            activity?.SetTag(DataGridDiagnostics.Tags.FilterEnabled, Filter != null);
            activity?.SetTag(DataGridDiagnostics.Tags.SortDescriptions, SortDescriptions?.Count ?? 0);
            activity?.SetTag(DataGridDiagnostics.Tags.GroupDescriptions, GroupDescriptions?.Count ?? 0);
            activity?.SetTag(DataGridDiagnostics.Tags.PageSize, PageSize);
            activity?.SetTag(DataGridDiagnostics.Tags.PageIndex, PageIndex);

            object oldCurrentItem = CurrentItem;
            int oldCurrentPosition = CurrentPosition;
            bool oldIsCurrentAfterLast = IsCurrentAfterLast;
            bool oldIsCurrentBeforeFirst = IsCurrentBeforeFirst;

            // set IsGrouping to false
            _isGrouping = false;

            // force currency off the collection (gives user a chance to save dirty information)
            OnCurrentChanging();

            // if there's no sort/filter/paging/grouping, just use the collection's array
            if (UsesLocalArray)
            {
                try
                {
                    // apply filtering/sorting through the PrepareLocalArray method
                    _internalList = PrepareLocalArray(_sourceCollection);

                    // apply grouping
                    if (PageSize == 0)
                    {
                        PrepareGroups();
                    }
                    else
                    {
                        PrepareTemporaryGroups();
                        PrepareGroupsForCurrentPage();
                    }
                }
                catch (TargetInvocationException e)
                {
                    // If there's an exception while invoking PrepareLocalArray,
                    // we want to unwrap it and throw its inner exception
                    if (e.InnerException != null)
                    {
                        throw e.InnerException;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            else
            {
                ResetInternalList();
            }

            // check if PageIndex is still valid after filter/sort
            if (PageSize > 0 &&
            PageIndex > 0 &&
            PageIndex >= PageCount)
            {
                MoveToPage(PageCount - 1);
            }

            // reset currency values
            ResetCurrencyValues(oldCurrentItem, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);

            OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));

            // now raise currency changes at the end
            RaiseCurrencyChanges(false, oldCurrentItem, oldCurrentPosition, oldIsCurrentBeforeFirst, oldIsCurrentAfterLast);

            activity?.SetTag(DataGridDiagnostics.Tags.IsGrouping, IsGrouping);
        }

        /// <summary>
        ///     Notify listeners that this View has changed
        /// </summary>
        /// <remarks>
        ///     CollectionViews (and sub-classes) should take their filter/sort/grouping/paging
        ///     into account before calling this method to forward CollectionChanged events.
        /// </remarks>
        /// <param name="args">
        ///     The NotifyCollectionChangedEventArgs to be passed to the EventHandler
        /// </param>
        //TODO Paging
        private void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            unchecked
            {
                // invalidate enumerators because of a change
                ++_timestamp;
            }

            if (CollectionChanged != null)
            {
                if (args.Action != NotifyCollectionChangedAction.Add || PageSize == 0 || args.NewStartingIndex < Count)
                {
                    CollectionChanged(this, args);
                }
            }

            // Collection changes change the count unless an item is being
            // replaced or moved within the collection.
            if (args.Action != NotifyCollectionChangedAction.Replace &&
                args.Action != NotifyCollectionChangedAction.Move)
            {
                OnPropertyChanged(nameof(Count));
            }

            bool listIsEmpty = IsEmpty;
            if (listIsEmpty != CheckFlag(CollectionViewFlags.CachedIsEmpty))
            {
                SetFlag(CollectionViewFlags.CachedIsEmpty, listIsEmpty);
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

    }
}
