using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using WorkbenchConnect.Linking;

namespace WorkbenchConnect.Core;

/// <summary>
/// Linked workbenches. Every member keeps its own copy of each shared bill, so pawns can work
/// the same bill at several benches at once (a bill object with an unfinished thing in progress
/// is closed to every other pawn). The group keeps the copies in step.
/// </summary>
// Saved inside WorkbenchGroupManager.groups; the type name and the loadID key are part of the save format.
public class WorkbenchGroup : IExposable
{
    public int loadID = -1;

    internal WorkbenchGroupManager? manager;
    internal int overlayFrame = -1;

    private LinkTable<Building_WorkTable, Bill> table = new();

    // Only used while saving and loading.
    private List<Building_WorkTable>? savedMembers;
    private List<LinkedBillRecord>? savedRows;

    // Saves from before 2.0 kept one shared list here and empty bill stacks on the members.
    private List<Bill>? legacyBills;

    public WorkbenchGroup()
    {
    }

    internal WorkbenchGroup(WorkbenchGroupManager manager, int loadID)
    {
        this.manager = manager;
        this.loadID = loadID;
    }

    public IReadOnlyList<Building_WorkTable> Members => table.Members;

    internal bool IsLegacy => legacyBills != null && table.Members.Count == 0;

    /// <summary>Links the benches; each ends up with every bill any of them had.</summary>
    internal void LinkAll(IEnumerable<Building_WorkTable> benches)
    {
        foreach (var bench in benches)
            AddMember(bench);
        Sync();
    }

    /// <summary>Adds a bench that drops its own bills and takes the group's.</summary>
    internal void Join(Building_WorkTable bench)
    {
        if (table.Contains(bench))
            return;

        using (BillSync.Suppress())
        {
            foreach (var bill in bench.billStack.Bills.ToList())
            {
                if (BillSync.Linkable(bill))
                    bench.billStack.Delete(bill);
            }
        }

        AddMember(bench);
        Sync();
    }

    /// <summary>Removes a bench. It keeps its copies as ordinary bills.</summary>
    internal void Leave(Building_WorkTable bench)
    {
        if (!table.Contains(bench))
            return;

        table.RemoveMember(bench);
        GroupRegistry.Clear(bench, this);
        DebugLog.Message($"{bench} left group {loadID}");

        if (table.Members.Count < 2)
            Dissolve();
    }

    internal void Dissolve()
    {
        foreach (var member in table.Members.ToList())
        {
            table.RemoveMember(member);
            GroupRegistry.Clear(member, this);
        }

        manager?.Remove(this);
        DebugLog.Message($"Group {loadID} dissolved");
    }

    /// <summary>
    /// Makes every member hold a copy of every shared bill, in the same order.
    /// </summary>
    /// <param name="orderFrom">A member whose bill order the others take, after the player reordered it.
    /// Without one, a member reordered behind the bill stack's back (e.g. by dragging in Nice Bill Tab) is used.</param>
    internal void Sync(Building_WorkTable? orderFrom = null)
    {
        orderFrom ??= ReorderedMember();
        if (orderFrom != null)
            table.AdoptOrder(orderFrom.billStack.Bills);

        var plan = table.Reconcile(member => member.billStack.Bills, BillSync.Linkable);
        int reordered = 0;
        using (BillSync.Suppress())
        {
            foreach (var bill in plan.Delete)
                bill.billStack?.Delete(bill);

            foreach (var (row, source, target) in plan.Clone)
                table.Attach(row, target, BillSync.CloneInto(source, target.billStack));

            foreach (var member in table.Members)
            {
                var ordered = table.DesiredOrder(member, member.billStack.Bills);
                if (ordered == null)
                    continue;

                var bills = member.billStack.Bills;
                bills.Clear();
                bills.AddRange(ordered);
                reordered++;
            }
        }

        if (!plan.IsEmpty || reordered > 0)
            DebugLog.Message($"Group {loadID}: deleted {plan.Delete.Count}, copied {plan.Clone.Count}, reordered {reordered}");
    }

    /// <summary>Spreads a reorder made behind the bill stack's back, so it shows on the other members while paused too.</summary>
    internal void SyncOrder()
    {
        if (ReorderedMember() is { } member)
            Sync(member);
    }

    /// <summary>Copies settings the player changed on one copy to the others.</summary>
    internal void SyncSettings()
    {
        foreach (var row in table.Rows)
            SyncSettings(row);
    }

    internal void SyncSettings(Bill bill)
    {
        if (table.TryGetRow(bill, out var row))
            SyncSettings(row);
    }

    /// <summary>A copy finished some repetitions; the others count them too.</summary>
    internal void OnRepeatsDone(Bill_Production bill, int done)
    {
        if (!table.TryGetRow(bill, out var row))
            return;

        foreach (var copy in row.Copies.Values)
        {
            if (copy != bill && copy is Bill_Production other && other.repeatMode == BillRepeatModeDefOf.RepeatCount)
                other.repeatCount = Math.Max(0, other.repeatCount - done);
        }

        table.Restamp(row, BillSettings.Stamp);
    }

    /// <summary>How many other copies of the bill are being made right now.</summary>
    internal int InProgressElsewhere(Bill bill, Pawn claimant)
    {
        if (!table.TryGetRow(bill, out var row))
            return 0;

        int count = 0;
        foreach (var copy in row.Copies.Values)
        {
            if (copy != bill && InProgress(copy, claimant))
                count++;
        }
        return count;
    }

    /// <summary>Drops members that are gone, then registers and syncs the rest. Run once the map has loaded.</summary>
    internal void Revalidate(Map map)
    {
        foreach (var member in table.Members.ToList())
        {
            bool elsewhere = GroupRegistry.Of(member) is { } other && other != this;
            if (member.Destroyed || !member.Spawned || member.Map != map || elsewhere)
                table.RemoveMember(member);
        }

        if (table.Members.Count < 2)
        {
            Dissolve();
            return;
        }

        foreach (var member in table.Members)
            GroupRegistry.Set(member, this);
        Sync();
    }

    /// <summary>Gives each bench found for a pre-2.0 group its own copies of the group's bills.</summary>
    internal void MigrateLegacy(List<Building_WorkTable> benches)
    {
        var bills = legacyBills!.Where(bill => bill?.recipe != null).ToList();
        legacyBills = null;

        var copies = new Dictionary<Building_WorkTable, List<Bill>>();
        using (BillSync.Suppress())
        {
            foreach (var bench in benches)
                copies[bench] = bills.Select(bill => BillSync.CloneInto(bill, bench.billStack)).ToList();
        }

        // Nothing holds the old list any more. Marking it deleted unbinds unfinished things that
        // pointed at it instead of letting them reach for a bill stack it never had.
        foreach (var bill in bills)
            bill.deleted = true;

        foreach (var bench in benches)
            table.AddMember(bench);
        for (int i = 0; i < bills.Count; i++)
            table.AddRow(benches.Select(bench => (bench, copies[bench][i])));

        DebugLog.Message($"Migrated pre-2.0 group {loadID}: {bills.Count} bills on {benches.Count} workbenches");
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref loadID, "loadID", -1);

        if (Scribe.mode == LoadSaveMode.Saving)
        {
            savedMembers = table.Members.ToList();
            savedRows = table.Rows.Select(row => new LinkedBillRecord(row.Copies.Values)).ToList();
        }

        Scribe_Collections.Look(ref savedMembers, "members", LookMode.Reference);
        Scribe_Collections.Look(ref savedRows, "linkedBills", LookMode.Deep);
        if (Scribe.mode != LoadSaveMode.Saving)
            Scribe_Collections.Look(ref legacyBills, "savedBills", LookMode.Deep);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            RestoreTable();

        if (Scribe.mode == LoadSaveMode.Saving)
        {
            savedMembers = null;
            savedRows = null;
        }
    }

    private void RestoreTable()
    {
        table = new LinkTable<Building_WorkTable, Bill>();
        if (savedMembers != null)
        {
            foreach (var member in savedMembers)
            {
                if (member != null)
                    table.AddMember(member);
            }
        }

        if (savedRows != null)
        {
            foreach (var record in savedRows)
                table.AddRow(record.Copies());
        }

        savedMembers = null;
        savedRows = null;
    }

    private void AddMember(Building_WorkTable bench)
    {
        table.AddMember(bench);
        GroupRegistry.Set(bench, this);
    }

    private void SyncSettings(LinkTable<Building_WorkTable, Bill>.Row row)
    {
        var changed = table.FindChanged(row, BillSettings.Stamp, IsOnSelectedBench);
        if (changed == null)
            return;

        foreach (var copy in row.Copies.Values)
        {
            if (copy != changed)
                BillSettings.Copy(changed, copy);
        }
        table.Restamp(row, BillSettings.Stamp);
    }

    private Building_WorkTable? ReorderedMember() =>
        table.FindReordered(member => member.billStack.Bills, member => Find.Selector.IsSelected(member));

    private static bool IsOnSelectedBench(Bill bill) =>
        bill.billStack?.billGiver is Thing bench && Find.Selector.IsSelected(bench);

    private static bool InProgress(Bill copy, Pawn claimant)
    {
        // Same rule the game uses on one bench: a bound unfinished thing keeps the bill busy.
        if (copy is Bill_ProductionWithUft { BoundUft: not null })
            return true;

        if (copy.billStack?.billGiver is not Thing { Spawned: true } bench)
            return false;

        var worker = bench.Map.reservationManager.FirstRespectedReserver(bench, claimant);
        return worker?.CurJob?.bill == copy;
    }
}

/// <summary>One shared bill in a save: references to its copies, which the workbenches save themselves.</summary>
public class LinkedBillRecord : IExposable
{
    private List<Bill>? bills;

    public LinkedBillRecord()
    {
    }

    public LinkedBillRecord(IEnumerable<Bill> copies)
    {
        bills = copies.ToList();
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref bills, "bills", LookMode.Reference);
    }

    internal IEnumerable<(Building_WorkTable, Bill)> Copies()
    {
        if (bills == null)
            yield break;

        foreach (var bill in bills)
        {
            if (bill?.billStack?.billGiver is Building_WorkTable bench)
                yield return (bench, bill);
        }
    }
}
