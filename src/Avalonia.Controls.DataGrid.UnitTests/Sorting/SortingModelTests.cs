// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls.DataGridSorting;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Sorting;

public class SortingModelTests
{
    [Fact]
    public void Toggle_Adds_Descriptor_When_Not_Present()
    {
        var model = new SortingModel();
        var descriptor = CreateDescriptor("col1", propertyPath: "Name");
        SortingChangedEventArgs? args = null;
        model.SortingChanged += (_, e) => args = e;

        model.Toggle(descriptor);

        var active = Assert.Single(model.Descriptors);
        Assert.Equal("col1", active.ColumnId);
        Assert.Equal(ListSortDirection.Ascending, active.Direction);
        Assert.Equal("Name", active.PropertyPath);

        Assert.NotNull(args);
        Assert.Empty(args!.OldDescriptors);
        Assert.Single(args.NewDescriptors);
    }

    [Fact]
    public void Toggle_Three_State_Cycle_Removes_On_Third_Click()
    {
        var model = new SortingModel { CycleMode = SortCycleMode.AscendingDescendingNone };
        var descriptor = CreateDescriptor("col1");

        model.Toggle(descriptor);
        Assert.Equal(ListSortDirection.Ascending, Assert.Single(model.Descriptors).Direction);

        model.Toggle(descriptor);
        Assert.Equal(ListSortDirection.Descending, Assert.Single(model.Descriptors).Direction);

        model.Toggle(descriptor);
        Assert.Empty(model.Descriptors);
    }

    [Fact]
    public void Toggle_Two_State_Cycle_Does_Not_Clear()
    {
        var model = new SortingModel { CycleMode = SortCycleMode.AscendingDescending };
        var descriptor = CreateDescriptor("col1");

        model.Toggle(descriptor);
        model.Toggle(descriptor);
        model.Toggle(descriptor);

        var active = Assert.Single(model.Descriptors);
        Assert.Equal(ListSortDirection.Ascending, active.Direction);
    }

    [Fact]
    public void Defaults_Are_MultiSort_And_Three_State_Cycle()
    {
        var model = new SortingModel();

        Assert.True(model.MultiSort);
        Assert.Equal(SortCycleMode.AscendingDescending, model.CycleMode);
        Assert.True(model.OwnsViewSorts);
        Assert.False(model.KeepSecondarySorts);
    }

    [Fact]
    public void Toggle_With_KeepSecondarySorts_Puts_New_Column_First_And_Keeps_Others()
    {
        var model = new SortingModel { KeepSecondarySorts = true };

        model.Toggle(CreateDescriptor("col1"));
        model.Toggle(CreateDescriptor("col2"));

        Assert.Equal(new object[] { "col2", "col1" }, model.Descriptors.Select(d => d.ColumnId).ToArray());
    }

    [Fact]
    public void Toggle_With_KeepSecondarySorts_Promotes_Existing_Column_And_Cycles_It()
    {
        var model = new SortingModel { KeepSecondarySorts = true };
        model.Toggle(CreateDescriptor("col1"));
        model.Toggle(CreateDescriptor("col2"));

        model.Toggle(CreateDescriptor("col1"));

        Assert.Equal(new object[] { "col1", "col2" }, model.Descriptors.Select(d => d.ColumnId).ToArray());
        Assert.Equal(ListSortDirection.Descending, model.Descriptors[0].Direction);
        Assert.Equal(ListSortDirection.Ascending, model.Descriptors[1].Direction);
    }

    [Fact]
    public void Toggle_With_KeepSecondarySorts_Clears_All_When_Primary_Cycles_Off()
    {
        var model = new SortingModel
        {
            KeepSecondarySorts = true,
            CycleMode = SortCycleMode.AscendingDescendingNone
        };
        model.Toggle(CreateDescriptor("col1"));
        model.Toggle(CreateDescriptor("col2"));

        model.Toggle(CreateDescriptor("col2"));
        model.Toggle(CreateDescriptor("col2"));

        Assert.Empty(model.Descriptors);
    }

    [Fact]
    public void Toggle_With_KeepSecondarySorts_And_Multi_Modifier_Appends()
    {
        var model = new SortingModel { KeepSecondarySorts = true };
        model.Toggle(CreateDescriptor("col1"));

        model.Toggle(CreateDescriptor("col2"), SortingModifiers.Multi);

        Assert.Equal(new object[] { "col1", "col2" }, model.Descriptors.Select(d => d.ColumnId).ToArray());
    }

    [Fact]
    public void SetOrUpdate_Replaces_Descriptor_With_Same_ColumnId()
    {
        var model = new SortingModel();
        model.Apply(new[] { CreateDescriptor("col1", ListSortDirection.Ascending, "Name") });

        model.SetOrUpdate(CreateDescriptor("col1", ListSortDirection.Descending, "Name"));

        var active = Assert.Single(model.Descriptors);
        Assert.Equal("col1", active.ColumnId);
        Assert.Equal(ListSortDirection.Descending, active.Direction);
    }

    [Fact]
    public void Toggle_With_Multi_Modifier_Preserves_Existing_Sorts()
    {
        var model = new SortingModel();
        var first = CreateDescriptor("col1");
        var second = CreateDescriptor("col2");

        model.Toggle(first);
        model.Toggle(second, SortingModifiers.Multi);

        Assert.Equal(new[] { "col1", "col2" }, model.Descriptors.Select(x => x.ColumnId).ToArray());
    }

    [Fact]
    public void Toggle_Without_Multi_Replaces_Previous_Sort()
    {
        var model = new SortingModel();
        var first = CreateDescriptor("col1");
        var second = CreateDescriptor("col2", ListSortDirection.Descending);

        model.Toggle(first);
        model.Toggle(second);

        var active = Assert.Single(model.Descriptors);
        Assert.Equal("col2", active.ColumnId);
        Assert.Equal(ListSortDirection.Descending, active.Direction);
    }

    [Fact]
    public void Toggle_Clear_Modifier_Removes_Descriptor()
    {
        var model = new SortingModel();
        var descriptor = CreateDescriptor("col1");

        model.Toggle(descriptor);
        model.Toggle(descriptor, SortingModifiers.Clear);

        Assert.Empty(model.Descriptors);
    }

    [Fact]
    public void SetOrUpdate_Replaces_Existing_Descriptor_By_Column()
    {
        var model = new SortingModel();
        var initial = CreateDescriptor("col1", ListSortDirection.Ascending, "Name");
        var replacement = CreateDescriptor("col1", ListSortDirection.Descending, "Name");

        model.SetOrUpdate(initial);
        model.SetOrUpdate(replacement);

        var active = Assert.Single(model.Descriptors);
        Assert.Equal(ListSortDirection.Descending, active.Direction);
        Assert.Equal("Name", active.PropertyPath);
    }

    [Fact]
    public void Move_Reorders_Descriptor()
    {
        var model = new SortingModel();
        var first = CreateDescriptor("a");
        var second = CreateDescriptor("b");
        var third = CreateDescriptor("c");
        model.Apply(new[] { first, second, third });
        SortingChangedEventArgs? args = null;
        model.SortingChanged += (_, e) => args = e;

        var moved = model.Move("c", 0);

        Assert.True(moved);
        Assert.Equal(new[] { "c", "a", "b" }, model.Descriptors.Select(x => x.ColumnId).ToArray());
        Assert.NotNull(args);
        Assert.Equal(new[] { "a", "b", "c" }, args!.OldDescriptors.Select(x => x.ColumnId).ToArray());
    }

    [Fact]
    public void BeginUpdate_Coalesces_SortingChanged()
    {
        var model = new SortingModel { CycleMode = SortCycleMode.AscendingDescendingNone };
        var descriptor = CreateDescriptor("col1");
        int changedCount = 0;
        model.SortingChanged += (_, __) => changedCount++;

        model.BeginUpdate();
        model.Toggle(descriptor);
        model.Toggle(descriptor);
        model.Toggle(descriptor);
        model.EndUpdate();

        Assert.Equal(1, changedCount);
        Assert.Empty(model.Descriptors);
    }

    [Fact]
    public void SortingChanging_Can_Cancel_Change()
    {
        var model = new SortingModel { CycleMode = SortCycleMode.AscendingDescendingNone };
        var descriptor = CreateDescriptor("col1");

        model.Toggle(descriptor);
        model.SortingChanging += (_, e) =>
        {
            if (e.NewDescriptors.Count == 0)
            {
                e.Cancel = true;
            }
        };

        model.Toggle(descriptor);
        Assert.Equal(ListSortDirection.Descending, Assert.Single(model.Descriptors).Direction);

        model.Toggle(descriptor);
        Assert.Equal(ListSortDirection.Descending, Assert.Single(model.Descriptors).Direction);
    }

    [Fact]
    public void Apply_Throws_On_Duplicate_Columns()
    {
        var model = new SortingModel();
        var first = CreateDescriptor("col1");
        var second = CreateDescriptor("col1", ListSortDirection.Descending);

        Assert.Throws<ArgumentException>(() => model.Apply(new[] { first, second }));
    }

    [Fact]
    public void Apply_Respects_Single_Sort_Mode()
    {
        var model = new SortingModel
        {
            MultiSort = false
        };

        var first = CreateDescriptor("col1");
        var second = CreateDescriptor("col2");

        model.Apply(new[] { first, second });

        var active = Assert.Single(model.Descriptors);
        Assert.Equal("col1", active.ColumnId);
    }

    [Fact]
    public void DeferRefresh_Raises_Change_On_Dispose()
    {
        var model = new SortingModel();
        var descriptor = CreateDescriptor("col1");
        int changes = 0;
        model.SortingChanged += (_, __) => changes++;

        using (model.DeferRefresh())
        {
            model.Toggle(descriptor);
        }

        Assert.Equal(1, changes);
    }

    private static SortingDescriptor CreateDescriptor(object columnId, ListSortDirection direction = ListSortDirection.Ascending, string? propertyPath = null)
    {
        return new SortingDescriptor(columnId, direction, propertyPath);
    }
}
