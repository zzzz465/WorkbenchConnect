using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace WorkbenchConnect.Core;

/// <summary>
/// Reads saves made before 2.0. Those stored the group's bills once in the group, saved the
/// members' bill stacks empty, and wrote the group ID into each workbench's own save node.
/// </summary>
internal static class LegacySave
{
    private static readonly ConditionalWeakTable<Building_WorkTable, StrongBox<int>> groupIds = new();

    public static void ReadGroupId(Building_WorkTable bench)
    {
        if (Scribe.mode != LoadSaveMode.LoadingVars)
            return;

        int id = -1;
        Scribe_Values.Look(ref id, "workbenchGroupID_" + bench.thingIDNumber, -1);
        if (id > 0)
            groupIds.AddOrUpdate(bench, new StrongBox<int>(id));
    }

    public static void Migrate(Map map, List<WorkbenchGroup> groups)
    {
        var legacy = groups.Where(group => group.IsLegacy).ToList();
        if (legacy.Count == 0)
            return;

        var benches = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver)
            .OfType<Building_WorkTable>()
            .ToList();

        foreach (var group in legacy)
        {
            var members = benches
                .Where(bench => groupIds.TryGetValue(bench, out var id) && id.Value == group.loadID)
                .ToList();
            group.MigrateLegacy(members);
        }
    }
}
