using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Xunit;

namespace Avalonia.Controls.DataGridTests.Collections;

/// <summary>
/// What a user sees when they add or edit a row in a grid that is sorted, filtered or grouped.
/// The new row is typed into at the bottom of the list, and only once it is committed does the view
/// apply its sort, filter and grouping to it - so the row the user just filled in jumps to where it
/// belongs, or leaves the view entirely if the filter no longer wants it. Editing an existing row
/// behaves the same way: the row moves when the edit changes what the view orders or groups by.
/// </summary>
public class DataGridCollectionViewAddEditTransactionTests
{
    // ---- adding a row to a sorted view -----------------------------------------------------

    [Fact]
    public void New_Row_Is_Typed_In_At_The_Bottom_Of_A_Sorted_View()
    {
        var view = SortedByNameView(Item("Ann"), Item("Carl"));

        var added = Assert.IsType<Person>(view.AddNew());
        added.Name = "Bea";

        // Until the user commits, the row they are filling in stays put at the end. Sorting it
        // while it is half-typed would move the row out from under the caret.
        Assert.Equal(new[] { "Ann", "Carl", "Bea" }, Names(view));
        Assert.Equal(2, view.IndexOf(added));
    }

    [Fact]
    public void Committing_A_New_Row_Moves_It_To_Its_Sorted_Position()
    {
        var source = new ObservableCollection<Person> { Item("Ann"), Item("Carl") };
        var view = SortedByNameView(source);

        var added = Assert.IsType<Person>(view.AddNew());
        added.Name = "Bea";

        var changes = Record(view);
        view.CommitNew();

        Assert.Equal(new[] { "Ann", "Bea", "Carl" }, Names(view));
        Assert.Equal(1, view.IndexOf(added));
        Assert.Contains(added, source);

        // The row leaves the bottom of the list and reappears in the middle, which is what a grid
        // needs to be told to move the row it is displaying.
        Assert.Equal(
            new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add },
            changes.Select(c => c.Action).ToArray());
        Assert.Equal(2, changes[0].OldStartingIndex);
        Assert.Equal(1, changes[1].NewStartingIndex);
    }

    [Fact]
    public void Cancelling_A_New_Row_In_A_Sorted_View_Leaves_The_List_Untouched()
    {
        var source = new ObservableCollection<Person> { Item("Ann"), Item("Carl") };
        var view = SortedByNameView(source);

        var added = Assert.IsType<Person>(view.AddNew());
        added.Name = "Bea";

        var changes = Record(view);
        view.CancelNew();

        Assert.Equal(new[] { "Ann", "Carl" }, Names(view));
        Assert.DoesNotContain(added, source);
        Assert.False(view.IsAddingNew);

        var removed = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Remove, removed.Action);
        Assert.Equal(2, removed.OldStartingIndex);
    }

    // ---- adding a row that the filter rejects -----------------------------------------------

    [Fact]
    public void Committing_A_New_Row_The_Filter_Rejects_Removes_It_From_View_But_Keeps_It_In_The_Source()
    {
        var source = new ObservableCollection<Person> { Item("Ann", active: true) };
        var view = new DataGridCollectionView(source) { Filter = o => ((Person)o).Active };

        var added = Assert.IsType<Person>(view.AddNew());
        added.Name = "Bea";
        added.Active = false;

        var changes = Record(view);
        view.CommitNew();

        // The row the user filled in is not one this view shows, so it disappears - but the
        // application's collection keeps it. Clearing the filter would bring it back.
        Assert.Equal(new[] { "Ann" }, Names(view));
        Assert.Contains(added, source);
        Assert.Equal(-1, view.IndexOf(added));

        var removed = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Remove, removed.Action);
        Assert.Equal(1, removed.OldStartingIndex);
    }

    [Fact]
    public void A_New_Row_Is_Visible_While_Being_Typed_Even_Though_The_Filter_Would_Reject_It()
    {
        var view = new DataGridCollectionView(new ObservableCollection<Person> { Item("Ann", active: true) })
        {
            Filter = o => ((Person)o).Active
        };

        var added = Assert.IsType<Person>(view.AddNew());

        // A brand new item fails almost any filter before the user has typed anything into it.
        // Hiding it immediately would make adding a row impossible.
        Assert.False(added.Active);
        Assert.Equal(2, view.Count);
        Assert.Same(added, view.GetItemAt(1));
    }

    // ---- adding a row to a grouped view ------------------------------------------------------

    [Fact]
    public void Committing_A_New_Row_Files_It_Under_The_Group_The_User_Chose()
    {
        var source = new ObservableCollection<Person> { Item("Ann", "Sales"), Item("Carl", "Support") };
        var view = GroupedByTeamView(source);

        var added = Assert.IsType<Person>(view.AddNew());
        added.Name = "Bea";
        added.Team = "Sales";
        view.CommitNew();

        Assert.Equal(new[] { "Ann", "Bea", "Carl" }, Names(view));
        Assert.Equal(new[] { "Sales", "Support" }, GroupKeys(view));
        Assert.Equal(new[] { "Ann", "Bea" }, GroupItems(view, "Sales").Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Cancelling_A_New_Row_In_A_Grouped_View_Leaves_The_Groups_As_They_Were()
    {
        var source = new ObservableCollection<Person> { Item("Ann", "Sales"), Item("Carl", "Support") };
        var view = GroupedByTeamView(source);

        var added = Assert.IsType<Person>(view.AddNew());
        added.Team = "Sales";
        view.CancelNew();

        Assert.Equal(new[] { "Ann", "Carl" }, Names(view));
        Assert.Equal(new[] { "Sales", "Support" }, GroupKeys(view));
        Assert.DoesNotContain(added, source);
    }

    [Fact]
    public void Committing_A_New_Row_The_Filter_Rejects_Does_Not_Create_A_Group_For_It()
    {
        var source = new ObservableCollection<Person> { Item("Ann", "Sales", active: true) };
        var view = GroupedByTeamView(source);
        view.Filter = o => ((Person)o).Active;

        var added = Assert.IsType<Person>(view.AddNew());
        added.Name = "Bea";
        added.Team = "Support";
        added.Active = false;
        view.CommitNew();

        Assert.Equal(new[] { "Ann" }, Names(view));
        Assert.Equal(new[] { "Sales" }, GroupKeys(view));
        Assert.Contains(added, source);
    }

    // ---- editing an existing row -------------------------------------------------------------

    [Fact]
    public void Committing_An_Edit_That_Changes_The_Sort_Key_Moves_The_Row()
    {
        var moved = Item("Ann");
        var view = SortedByNameView(moved, Item("Bea"), Item("Carl"));

        view.EditItem(moved);
        moved.Name = "Dana";

        var changes = Record(view);
        view.CommitEdit();

        Assert.Equal(new[] { "Bea", "Carl", "Dana" }, Names(view));
        Assert.Equal(2, view.IndexOf(moved));

        Assert.Equal(
            new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add },
            changes.Select(c => c.Action).ToArray());
        Assert.Equal(0, changes[0].OldStartingIndex);
        Assert.Equal(2, changes[1].NewStartingIndex);
    }

    [Fact]
    public void Committing_An_Edit_That_Fails_The_Filter_Removes_The_Row_From_The_View()
    {
        var edited = Item("Ann", active: true);
        var source = new ObservableCollection<Person> { edited, Item("Bea", active: true) };
        var view = new DataGridCollectionView(source) { Filter = o => ((Person)o).Active };

        view.EditItem(edited);
        edited.Active = false;

        var changes = Record(view);
        view.CommitEdit();

        Assert.Equal(new[] { "Bea" }, Names(view));
        Assert.Contains(edited, source);

        var removed = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Remove, removed.Action);
        Assert.Equal(0, removed.OldStartingIndex);
    }

    [Fact]
    public void Committing_An_Edit_That_Changes_The_Group_Key_Moves_The_Row_Between_Groups()
    {
        var moved = Item("Ann", "Sales");
        var view = GroupedByTeamView(moved, Item("Carl", "Support"));

        view.EditItem(moved);
        moved.Team = "Support";
        view.CommitEdit();

        // The user retyped the team on a row; the row belongs under the other header now, and the
        // group it left behind is empty so it goes away. Within the group the rows keep the order
        // the underlying collection has them in, which is the order the view had before the edit.
        Assert.Equal(new[] { "Support" }, GroupKeys(view));
        Assert.Equal(new[] { "Ann", "Carl" }, GroupItems(view, "Support").Select(p => p.Name).ToArray());
    }

    [Fact]
    public void An_Edit_That_Changes_Nothing_The_View_Orders_By_Leaves_The_Row_Where_It_Is()
    {
        var edited = Item("Bea");
        var view = SortedByNameView(Item("Ann"), edited, Item("Carl"));

        view.EditItem(edited);
        edited.Team = "Support";

        var changes = Record(view);
        view.CommitEdit();

        Assert.Equal(new[] { "Ann", "Bea", "Carl" }, Names(view));
        Assert.Equal(1, view.IndexOf(edited));

        // The row is still re-announced at the same index: the view has no way to know an edit did
        // not touch the sort key, so it removes and re-inserts. What matters to the user is that
        // the row does not move.
        Assert.Equal(1, changes[0].OldStartingIndex);
        Assert.Equal(1, changes[^1].NewStartingIndex);
    }

    [Fact]
    public void Cancelling_An_Edit_Restores_The_Value_The_Row_Had_Before()
    {
        var edited = new EditablePerson { Name = "Bea" };
        var view = new DataGridCollectionView(
            new ObservableCollection<EditablePerson> { new() { Name = "Ann" }, edited });

        view.EditItem(edited);
        edited.Name = "Zoe";
        view.CancelEdit();

        Assert.Equal("Bea", edited.Name);
        Assert.False(view.IsEditingItem);
    }

    // ---- one transaction at a time -----------------------------------------------------------

    [Fact]
    public void Starting_A_Second_New_Row_Commits_The_First_One()
    {
        var source = new ObservableCollection<Person> { Item("Carl") };
        var view = SortedByNameView(source);

        var first = Assert.IsType<Person>(view.AddNew());
        first.Name = "Ann";

        // The user clicks the placeholder row again without leaving the one they were filling in.
        var second = Assert.IsType<Person>(view.AddNew());
        second.Name = "Bea";
        view.CommitNew();

        Assert.Equal(new[] { "Ann", "Bea", "Carl" }, Names(view));
        Assert.Contains(first, source);
        Assert.Contains(second, source);
    }

    [Fact]
    public void Clicking_Into_Another_Row_While_Adding_Commits_The_New_Row_First()
    {
        var existing = Item("Carl");
        var source = new ObservableCollection<Person> { existing };
        var view = SortedByNameView(source);

        var added = Assert.IsType<Person>(view.AddNew());
        added.Name = "Ann";

        view.EditItem(existing);

        Assert.False(view.IsAddingNew);
        Assert.True(view.IsEditingItem);
        Assert.Equal(new[] { "Ann", "Carl" }, Names(view));

        view.CommitEdit();
        Assert.Equal(new[] { "Ann", "Carl" }, Names(view));
    }

    [Fact]
    public void Editing_The_Row_Being_Added_Does_Not_End_The_Add()
    {
        var view = SortedByNameView(Item("Carl"));

        var added = Assert.IsType<Person>(view.AddNew());
        view.EditItem(added);

        Assert.True(view.IsAddingNew);
        Assert.False(view.IsEditingItem);
        Assert.Same(added, view.CurrentAddItem);
    }

    [Fact]
    public void A_Row_Cannot_Be_Removed_While_Another_Row_Is_Being_Added()
    {
        var view = SortedByNameView(Item("Ann"), Item("Carl"));
        view.AddNew();

        Assert.Throws<System.InvalidOperationException>(() => view.RemoveAt(0));
    }

    [Fact]
    public void Sorting_Cannot_Be_Changed_While_A_Row_Is_Being_Added()
    {
        var view = SortedByNameView(Item("Ann"), Item("Carl"));
        view.AddNew();

        Assert.Throws<System.InvalidOperationException>(view.Refresh);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static Person Item(string name, string team = "Sales", bool active = true)
        => new() { Name = name, Team = team, Active = active };

    private static Person Item(string name, bool active)
        => new() { Name = name, Team = "Sales", Active = active };

    private static DataGridCollectionView SortedByNameView(params Person[] items)
        => SortedByNameView(new ObservableCollection<Person>(items));

    private static DataGridCollectionView SortedByNameView(ObservableCollection<Person> source)
    {
        var view = new DataGridCollectionView(source);
        view.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(Person.Name), ListSortDirection.Ascending));
        return view;
    }

    private static DataGridCollectionView GroupedByTeamView(params Person[] items)
        => GroupedByTeamView(new ObservableCollection<Person>(items));

    private static DataGridCollectionView GroupedByTeamView(ObservableCollection<Person> source)
    {
        var view = new DataGridCollectionView(source);
        view.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(Person.Team)));
        return view;
    }

    private static List<NotifyCollectionChangedEventArgs> Record(DataGridCollectionView view)
    {
        var changes = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => changes.Add(e);
        return changes;
    }

    private static string[] Names(DataGridCollectionView view)
        => view.Cast<Person>().Select(p => p.Name).ToArray();

    private static string[] GroupKeys(DataGridCollectionView view)
        => view.Groups.Cast<DataGridCollectionViewGroup>().Select(g => (string)g.Key).ToArray();

    private static IEnumerable<Person> GroupItems(DataGridCollectionView view, string key)
        => view.Groups
            .Cast<DataGridCollectionViewGroup>()
            .Single(g => (string)g.Key == key)
            .Items
            .Cast<Person>();

    public class Person
    {
        public string Name { get; set; } = string.Empty;
        public string Team { get; set; } = string.Empty;
        public bool Active { get; set; }
    }

    public class EditablePerson : IEditableObject
    {
        private string _savedName = string.Empty;

        public string Name { get; set; } = string.Empty;

        public void BeginEdit() => _savedName = Name;

        public void CancelEdit() => Name = _savedName;

        public void EndEdit() => _savedName = Name;
    }
}
