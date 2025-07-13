using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using WorkbenchConnect.Core;

namespace WorkbenchConnect.Patches
{
    public static class WorkGiver_DoBill_Patches
    {
        public static void Patch(Harmony harmony)
        {
            var jobOnThing_original = AccessTools.Method(typeof(WorkGiver_DoBill), "JobOnThing");
            var jobOnThing_postfix = AccessTools.Method(typeof(WorkGiver_DoBill_Patches), "JobOnThing_Postfix");
            harmony.Patch(jobOnThing_original, postfix: new HarmonyMethod(jobOnThing_postfix));

            var cleanup_original = AccessTools.Method(typeof(JobDriver), "Cleanup");
            var cleanup_postfix = AccessTools.Method(typeof(WorkGiver_DoBill_Patches), "Cleanup_Postfix");
            harmony.Patch(cleanup_original, postfix: new HarmonyMethod(cleanup_postfix));
        }

        // Patch the JobOnThing method to add bill reservation checks
        public static void JobOnThing_Postfix(WorkGiver_DoBill __instance, Pawn pawn, Thing thing, bool forced, ref Job __result)
        {
            try
            {
                // Only process if a job was returned and it's a DoBill job
                if (__result == null || __result.def != JobDefOf.DoBill)
                    return;
                
                // Only process if the thing is a workbench that's part of a group
                if (!(thing is Building_WorkTable workTable))
                    return;
                
                var member = Building_WorkTable_Patches.GetMemberData(workTable);
                if (member?.Group == null)
                    return;
                
                var group = member.Group;
                var bill = __result.bill;
                
                if (bill == null)
                    return;
                
                // Check if this bill is available for this pawn
                if (!group.CanPawnWorkOnBill(bill, pawn))
                {
                    if (FloatMenuMakerMap.makingFor == pawn)
                    {
                        JobFailReason.Is("WorkbenchConnect.BillReservedByOtherPawn".Translate(), bill.Label);
                    }
                    __result = null;
                    return;
                }
                
                // Try to reserve the bill
                if (!group.TryReserveBill(bill, pawn))
                {
                    if (FloatMenuMakerMap.makingFor == pawn)
                    {
                        JobFailReason.Is("WorkbenchConnect.BillReservedByOtherPawn".Translate(), bill.Label);
                    }
                    __result = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WorkbenchConnect] Error in JobOnThing_Postfix: {ex}");
            }
        }

        // Patch to release bill reservations when jobs end
        public static void Cleanup_Postfix(JobDriver __instance, JobCondition condition)
        {
            // Only handle JobDriver_DoBill instances
            if (__instance is not JobDriver_DoBill doBillDriver)
            {
                return;
            }
            
            ReleaseBillReservation(doBillDriver, doBillDriver.pawn);
        }
        
        private static void ReleaseBillReservation(JobDriver_DoBill jobDriver, Pawn pawn)
        {
            try
            {
                if (jobDriver?.job?.bill == null || pawn == null)
                    return;
                
                var billGiver = jobDriver.job.GetTarget(TargetIndex.A).Thing as Building_WorkTable;
                if (billGiver == null)
                    return;
                
                var member = Building_WorkTable_Patches.GetMemberData(billGiver);
                if (member?.Group == null)
                    return;
                
                member.Group.ReleaseBillReservation(jobDriver.job.bill, pawn);
            }
            catch (Exception ex)
            {
                Log.Error($"[WorkbenchConnect] Error in ReleaseBillReservation: {ex}");
            }
        }
    }
} 
