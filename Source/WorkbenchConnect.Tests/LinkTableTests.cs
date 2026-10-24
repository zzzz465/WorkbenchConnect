using System.Collections.Generic;
using System.Linq;
using WorkbenchConnect.Linking;
using Xunit;

namespace WorkbenchConnect.Tests;

/// <summary>
/// Drives <see cref="LinkTable{TMember,TItem}"/> the way the mod does: lists stand in for bill
/// stacks, and <see cref="Apply"/> carries out a plan the way the group does with game calls.
/// </summary>
public class LinkTableTests
{
    private sealed class Bench(string name)
    {
        public readonly List<Bill> Bills = new();
        public override string ToString() => name;
    }

    private sealed class Bill(string recipe, bool linkable = true)
    {
        public readonly string Recipe = recipe;
        public readonly bool Linkable = linkable;
        public int Count;
        public override string ToString() => Recipe;
    }

    private readonly LinkTable<Bench, Bill> table = new();
    private readonly Bench a = new("A");
    private readonly Bench b = new("B");
    private readonly Bench c = new("C");

    private LinkTable<Bench, Bill>.Plan Sync(Bench? orderFrom = null, Bench? selected = null)
    {
        orderFrom ??= table.FindReordered(bench => bench.Bills, bench => bench == selected);
        if (orderFrom != null)
            table.AdoptOrder(orderFrom.Bills);

        var plan = table.Reconcile(bench => bench.Bills, bill => bill.Linkable);
        Apply(plan);
        return plan;
    }

    private void Apply(LinkTable<Bench, Bill>.Plan plan)
    {
        foreach (var bill in plan.Delete)
        {
            foreach (var bench in new[] { a, b, c })
                bench.Bills.Remove(bill);
        }

        foreach (var (row, source, target) in plan.Clone)
        {
            var clone = new Bill(source.Recipe) { Count = source.Count };
            target.Bills.Add(clone);
            table.Attach(row, target, clone);
        }

        foreach (var member in table.Members)
        {
            var ordered = table.DesiredOrder(member, member.Bills);
            if (ordered == null)
                continue;
            member.Bills.Clear();
            member.Bills.AddRange(ordered);
        }
    }

    private void Link(params Bench[] benches)
    {
        foreach (var bench in benches)
            table.AddMember(bench);
        Sync();
    }

    private static string Recipes(Bench bench) => string.Join(",", bench.Bills.Select(bill => bill.Recipe));

    [Fact]
    public void Linking_pools_every_bench_bills_in_member_order()
    {
        a.Bills.AddRange([new Bill("a1"), new Bill("a2")]);
        b.Bills.Add(new Bill("b1"));

        Link(a, b);

        Assert.Equal("a1,a2,b1", Recipes(a));
        Assert.Equal("a1,a2,b1", Recipes(b));
        Assert.Equal(3, table.Rows.Count);
    }

    [Fact]
    public void Copies_are_separate_objects()
    {
        a.Bills.Add(new Bill("x"));
        Link(a, b);

        Assert.NotSame(a.Bills[0], b.Bills[0]);
        Assert.True(table.TryGetRow(b.Bills[0], out var row));
        Assert.Same(a.Bills[0], row.CopyFor(a));
    }

    [Fact]
    public void Added_bill_is_copied_to_every_member()
    {
        Link(a, b, c);

        b.Bills.Add(new Bill("new"));
        var plan = Sync();

        Assert.Equal(2, plan.Clone.Count);
        Assert.Equal("new", Recipes(a));
        Assert.Equal("new", Recipes(c));
    }

    [Fact]
    public void Deleted_bill_is_deleted_everywhere()
    {
        a.Bills.AddRange([new Bill("x"), new Bill("y")]);
        Link(a, b, c);

        b.Bills.RemoveAt(0);
        var plan = Sync();

        Assert.Equal(2, plan.Delete.Count);
        Assert.Equal("y", Recipes(a));
        Assert.Equal("y", Recipes(c));
        Assert.Single(table.Rows);
    }

    [Fact]
    public void Deleted_copies_are_not_picked_up_as_new_bills()
    {
        a.Bills.Add(new Bill("x"));
        Link(a, b);

        b.Bills.Clear();
        var plan = table.Reconcile(bench => bench.Bills, bill => bill.Linkable);

        // a's copy is still in its list until the caller deletes it; it must not become a new row.
        Assert.Single(plan.Delete);
        Assert.Empty(plan.Clone);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public void Reorder_on_one_member_reorders_the_others()
    {
        a.Bills.AddRange([new Bill("x"), new Bill("y"), new Bill("z")]);
        Link(a, b);

        var z = b.Bills[2];
        b.Bills.Remove(z);
        b.Bills.Insert(0, z);
        Sync(orderFrom: b);

        Assert.Equal("z,x,y", Recipes(a));
        Assert.Equal("z,x,y", Recipes(b));
    }

    [Fact]
    public void Reorder_written_straight_to_the_list_is_spread_instead_of_undone()
    {
        a.Bills.AddRange([new Bill("x"), new Bill("y")]);
        Link(a, b, c);

        // Nice Bill Tab's drag and drop does this, without going through BillStack.Reorder.
        var y = b.Bills[1];
        b.Bills.Remove(y);
        b.Bills.Insert(0, y);
        Assert.Same(b, table.FindReordered(bench => bench.Bills, _ => false));
        Sync();

        Assert.Equal("y,x", Recipes(a));
        Assert.Equal("y,x", Recipes(b));
        Assert.Equal("y,x", Recipes(c));
        Assert.Null(table.FindReordered(bench => bench.Bills, _ => false));
    }

    [Fact]
    public void Reorder_on_the_selected_member_wins()
    {
        a.Bills.AddRange([new Bill("x"), new Bill("y"), new Bill("z")]);
        Link(a, b);

        a.Bills.Reverse();
        var x = b.Bills[0];
        b.Bills.Remove(x);
        b.Bills.Add(x);
        Sync(selected: b);

        Assert.Equal("y,z,x", Recipes(a));
        Assert.Equal("y,z,x", Recipes(b));
    }

    [Fact]
    public void New_and_deleted_bills_alone_are_not_a_reorder()
    {
        a.Bills.AddRange([new Bill("x"), new Bill("y"), new Bill("z")]);
        Link(a, b);

        b.Bills.RemoveAt(1);
        b.Bills.Insert(0, new Bill("new"));

        Assert.Null(table.FindReordered(bench => bench.Bills, _ => false));
    }

    [Fact]
    public void Unlinkable_bills_stay_local_and_keep_their_slot()
    {
        a.Bills.AddRange([new Bill("x"), new Bill("local", linkable: false), new Bill("y")]);
        Link(a, b);

        Assert.Equal("x,local,y", Recipes(a));
        Assert.Equal("x,y", Recipes(b));

        var y = b.Bills[1];
        b.Bills.Remove(y);
        b.Bills.Insert(0, y);
        Sync(orderFrom: b);

        Assert.Equal("y,local,x", Recipes(a));
    }

    [Fact]
    public void Leaving_member_keeps_its_bills_and_others_are_untouched()
    {
        a.Bills.Add(new Bill("x"));
        Link(a, b, c);

        table.RemoveMember(c);
        var plan = Sync();

        Assert.True(plan.IsEmpty);
        Assert.Equal("x", Recipes(c));
        Assert.False(table.TryGetRow(c.Bills[0], out _));
        Assert.Equal(2, table.Rows[0].Copies.Count);
    }

    [Fact]
    public void Removing_every_member_empties_the_table()
    {
        a.Bills.Add(new Bill("x"));
        Link(a, b);

        table.RemoveMember(a);
        table.RemoveMember(b);

        Assert.Empty(table.Rows);
        Assert.Empty(table.Members);
    }

    [Fact]
    public void Joining_member_with_no_bills_gets_every_row()
    {
        a.Bills.AddRange([new Bill("x"), new Bill("y")]);
        Link(a, b);

        table.AddMember(c);
        Sync();

        Assert.Equal("x,y", Recipes(c));
    }

    [Fact]
    public void Bill_inserted_before_linked_ones_lands_before_them()
    {
        a.Bills.AddRange([new Bill("x"), new Bill("y")]);
        Link(a, b);

        a.Bills.Insert(0, new Bill("first"));
        Sync();

        Assert.Equal("first,x,y", Recipes(b));
    }

    [Fact]
    public void Restored_rows_need_no_changes()
    {
        var ax = new Bill("x");
        var bx = new Bill("x");
        a.Bills.Add(ax);
        b.Bills.Add(bx);
        table.AddMember(a);
        table.AddMember(b);
        table.AddRow([(a, ax), (b, bx)]);

        var plan = Sync();

        Assert.True(plan.IsEmpty);
        Assert.Single(table.Rows);
    }

    [Fact]
    public void Restored_row_ignores_copies_of_non_members()
    {
        var ax = new Bill("x");
        a.Bills.Add(ax);
        table.AddMember(a);

        var row = table.AddRow([(a, ax), (c, new Bill("x"))]);

        Assert.NotNull(row);
        Assert.Single(row.Copies);
    }

    [Fact]
    public void FindChanged_remembers_first_then_reports_the_moved_copy()
    {
        a.Bills.Add(new Bill("x"));
        Link(a, b);
        var row = table.Rows[0];

        Assert.Null(table.FindChanged(row, bill => bill.Count, _ => false));

        b.Bills[0].Count = 5;
        Assert.Same(b.Bills[0], table.FindChanged(row, bill => bill.Count, _ => false));

        table.Restamp(row, bill => bill.Count);
        Assert.Null(table.FindChanged(row, bill => bill.Count, _ => false));
    }

    [Fact]
    public void FindChanged_prefers_the_copy_the_player_is_looking_at()
    {
        a.Bills.Add(new Bill("x"));
        Link(a, b, c);
        var row = table.Rows[0];
        table.Restamp(row, bill => bill.Count);

        a.Bills[0].Count = 1;
        b.Bills[0].Count = 2;
        c.Bills[0].Count = 3;

        Assert.Same(b.Bills[0], table.FindChanged(row, bill => bill.Count, bill => bill == b.Bills[0]));
    }

    [Fact]
    public void DesiredOrder_is_null_when_already_in_order()
    {
        a.Bills.AddRange([new Bill("x"), new Bill("y")]);
        Link(a, b);

        Assert.Null(table.DesiredOrder(a, a.Bills));
        Assert.Null(table.DesiredOrder(b, b.Bills));
    }
}
