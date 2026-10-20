using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace WorkbenchConnect.Core;

/// <summary>
/// Which group a workbench belongs to. Weak keys, so workbenches from a previous game are
/// not kept alive and never need clearing.
/// </summary>
internal static class GroupRegistry
{
    private static readonly ConditionalWeakTable<Building_WorkTable, WorkbenchGroup> groups = new();

    public static WorkbenchGroup? Of(Building_WorkTable bench) => groups.TryGetValue(bench, out var group) ? group : null;

    public static WorkbenchGroup? Of(Bill bill) => bill.billStack?.billGiver is Building_WorkTable bench ? Of(bench) : null;

    public static void Set(Building_WorkTable bench, WorkbenchGroup group) => groups.AddOrUpdate(bench, group);

    public static void Clear(Building_WorkTable bench, WorkbenchGroup group)
    {
        if (Of(bench) == group)
            groups.Remove(bench);
    }
}
