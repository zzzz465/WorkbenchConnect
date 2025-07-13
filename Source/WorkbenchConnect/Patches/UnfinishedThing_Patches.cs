using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchConnect.Patches
{
    public static class UnfinishedThing_Patches
    {
        public static void Patch(Harmony harmony)
        {
            var boundWorkTable_original = AccessTools.PropertyGetter(typeof(UnfinishedThing), "BoundWorkTable");
            var boundWorkTable_prefix = AccessTools.Method(typeof(UnfinishedThing_Patches), "BoundWorkTable_Prefix");
            harmony.Patch(boundWorkTable_original, prefix: new HarmonyMethod(boundWorkTable_prefix));
        }

        // Patch to handle null billStack in BoundWorkTable property
        public static bool BoundWorkTable_Prefix(UnfinishedThing __instance, ref Thing __result)
        {
            try
            {
                var boundBill = __instance.BoundBill;
                if (boundBill == null)
                {
                    __result = null;
                    return false; // Skip original method
                }

                // Handle null billStack case (can happen during save loading)
                if (boundBill.billStack == null)
                {
                    __result = null;
                    return false; // Skip original method
                }

                Thing thing = boundBill.billStack.billGiver as Thing;
                if (thing == null || thing.Destroyed)
                {
                    __result = null;
                    return false; // Skip original method
                }

                __result = thing;
                return false; // Skip original method
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WorkbenchConnect] Error in BoundWorkTable_Prefix: {ex}");
                // Fall back to original method if something goes wrong
                return true;
            }
        }
    }
} 
