using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using WorkbenchConnect.Core;
using WorkbenchConnect.UI;

namespace WorkbenchConnect.Patches;

/// <summary>Adding, deleting or reordering a bill on a member does the same on the other members.</summary>
[HarmonyPatch(typeof(BillStack))]
internal static class BillStackPatches
{
    [HarmonyPostfix, HarmonyPatch(nameof(BillStack.AddBill))]
    private static void AddBill(BillStack __instance) => Changed(__instance, reordered: false);

    [HarmonyPostfix, HarmonyPatch(nameof(BillStack.Delete))]
    private static void Delete(BillStack __instance) => Changed(__instance, reordered: false);

    [HarmonyPostfix, HarmonyPatch(nameof(BillStack.Clear))]
    private static void Clear(BillStack __instance) => Changed(__instance, reordered: false);

    [HarmonyPostfix, HarmonyPatch(nameof(BillStack.RemoveIncompletableBills))]
    private static void RemoveIncompletableBills(BillStack __instance) => Changed(__instance, reordered: false);

    [HarmonyPostfix, HarmonyPatch(nameof(BillStack.Reorder))]
    private static void Reorder(BillStack __instance) => Changed(__instance, reordered: true);

    private static void Changed(BillStack stack, bool reordered)
    {
        if (BillSync.Suppressed || stack.billGiver is not Building_WorkTable bench)
            return;

        GroupRegistry.Of(bench)?.Sync(reordered ? bench : null);
    }
}

[HarmonyPatch(typeof(Building))]
internal static class BuildingPatches
{
    // Building_WorkTable has no DeSpawn override of its own. Minifying, destroying and
    // gravship launches all pass through here; the bench keeps its copies.
    [HarmonyPrefix, HarmonyPatch(nameof(Building.DeSpawn))]
    private static void DeSpawn(Building __instance)
    {
        if (__instance is Building_WorkTable bench)
            GroupRegistry.Of(bench)?.Leave(bench);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(Building.GetGizmos))]
    private static IEnumerable<Gizmo> GetGizmos(IEnumerable<Gizmo> __result, Building __instance)
    {
        foreach (var gizmo in __result)
            yield return gizmo;

        if (__instance is Building_WorkTable bench && bench.Faction == Faction.OfPlayer)
        {
            foreach (var gizmo in GroupGizmos.For(bench))
                yield return gizmo;
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(Building.DrawExtraSelectionOverlays))]
    private static void DrawExtraSelectionOverlays(Building __instance)
    {
        if (__instance is Building_WorkTable bench)
            GroupOverlay.Draw(bench);
    }
}

[HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.ExposeData))]
internal static class WorkTableExposeDataPatch
{
    private static void Postfix(Building_WorkTable __instance) => LegacySave.ReadGroupId(__instance);
}

/// <summary>
/// "Do X times" counts across the group: a pawn does not start a copy when the other copies
/// already being made cover every remaining repetition.
/// </summary>
[HarmonyPatch(typeof(Bill), nameof(Bill.PawnAllowedToStartAnew))]
internal static class PawnAllowedToStartAnewPatch
{
    private static void Postfix(Bill __instance, Pawn p, ref bool __result)
    {
        if (!__result || __instance is not Bill_Production bill || bill.repeatMode != BillRepeatModeDefOf.RepeatCount)
            return;

        // Finishing your own unfinished thing is not a new start.
        if (bill is Bill_ProductionWithUft { BoundUft: not null })
            return;

        var group = GroupRegistry.Of(bill);
        if (group == null || group.InProgressElsewhere(bill, p) < bill.repeatCount)
            return;

        JobFailReason.Is("WorkbenchConnect.RepeatsInProgress".Translate(), bill.Label);
        __result = false;
    }
}

[HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.Notify_IterationCompleted))]
internal static class IterationCompletedPatch
{
    private static void Prefix(Bill_Production __instance, out int __state)
    {
        // Copy pending edits first, so the count below starts from the same number everywhere.
        GroupRegistry.Of(__instance)?.SyncSettings(__instance);
        __state = __instance.repeatCount;
    }

    private static void Postfix(Bill_Production __instance, int __state)
    {
        int done = __state - __instance.repeatCount;
        if (done > 0)
            GroupRegistry.Of(__instance)?.OnRepeatsDone(__instance, done);
    }
}
