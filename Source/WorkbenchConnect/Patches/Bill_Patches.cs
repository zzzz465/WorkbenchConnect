using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchConnect.Patches
{
    public static class Bill_Patches
    {
        public static void Patch(Harmony harmony)
        {
            var deletedOrDereferenced_original = AccessTools.PropertyGetter(typeof(Bill), "DeletedOrDereferenced");
            var deletedOrDereferenced_prefix = AccessTools.Method(typeof(Bill_Patches), "DeletedOrDereferenced_Prefix");
            harmony.Patch(deletedOrDereferenced_original, prefix: new HarmonyMethod(deletedOrDereferenced_prefix));
        }

        // Patch to handle null billStack during save loading
        public static bool DeletedOrDereferenced_Prefix(Bill __instance, ref bool __result)
        {
            try
            {
                // If deleted flag is set, it's definitely deleted
                if (__instance.deleted)
                {
                    __result = true;
                    return false; // Skip original method
                }

                // Handle null billStack case (can happen during save loading)
                if (__instance.billStack == null)
                {
                    // During loading, bills are temporarily without billStack
                    // Don't consider them deleted just because of this
                    __result = false;
                    return false; // Skip original method
                }

                // If billGiver exists and is destroyed, consider deleted
                if (__instance.billStack.billGiver is Thing thing && thing.Destroyed)
                {
                    __result = true;
                    return false; // Skip original method
                }

                // Otherwise, not deleted
                __result = false;
                return false; // Skip original method
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WorkbenchConnect] Error in DeletedOrDereferenced_Prefix: {ex}");
                // Fall back to original method if something goes wrong
                return true;
            }
        }
    }
} 
