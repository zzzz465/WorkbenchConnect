using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using WorkbenchConnect.Core;
using WorkbenchConnect.Utils;

namespace WorkbenchConnect.Patches
{
    [HarmonyPatch(typeof(Building_WorkTable))]
    public static class Building_WorkTable_Patches
    {
        private static readonly Dictionary<Building_WorkTable, WorkbenchGroupMemberData> memberData = 
            new Dictionary<Building_WorkTable, WorkbenchGroupMemberData>();

        private class WorkbenchGroupMemberData : IWorkbenchGroupMember
        {
            public Building_WorkTable workTable;
            public WorkbenchGroup group;

            public WorkbenchGroup Group 
            { 
                get => group; 
                set => group = value; 
            }

            public Map Map => workTable.Map;
            public IntVec3 Position => workTable.Position;
            public string WorkbenchGroupTag => workTable.def.defName;
            public bool DrawConnectionOverlay => true;

            public BillStack BillStack
            {
                get
                {
                    if (group != null)
                        return group.sharedBillStack;
                    return workTable.BillStack;
                }
            }

            public void Notify_GroupChanged()
            {
                DebugHelper.Log($"Workbench group changed for {workTable.def.defName} at {workTable.Position}");
            }

            public WorkbenchGroupMemberData(Building_WorkTable table)
            {
                workTable = table;
            }
        }

        public static IWorkbenchGroupMember GetMemberData(Building_WorkTable workTable)
        {
            if (!memberData.TryGetValue(workTable, out var data))
            {
                data = new WorkbenchGroupMemberData(workTable);
                memberData[workTable] = data;
            }
            return data;
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnSetup")]
        public static void SpawnSetup_Postfix(Building_WorkTable __instance)
        {
            var member = GetMemberData(__instance);
            DebugHelper.Log($"Workbench spawned: {__instance.def.defName} at {__instance.Position}");
        }

        [HarmonyPrefix]
        [HarmonyPatch("DeSpawn")]
        public static void DeSpawn_Prefix(Building_WorkTable __instance)
        {
            if (memberData.TryGetValue(__instance, out var member))
            {
                member.Group?.RemoveMember(member);
                memberData.Remove(__instance);
                DebugHelper.Log($"Workbench despawned: {__instance.def.defName} at {__instance.Position}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetGizmos")]
        public static IEnumerable<Gizmo> GetGizmos_Postfix(IEnumerable<Gizmo> __result, Building_WorkTable __instance)
        {
            foreach (var gizmo in __result)
                yield return gizmo;

            if (__instance.Faction == Faction.OfPlayer)
            {
                var member = GetMemberData(__instance);
                foreach (var gizmo in WorkbenchGroupUtility.WorkbenchGroupMemberGizmos(member))
                    yield return gizmo;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("get_BillStack")]
        public static bool BillStack_Prefix(Building_WorkTable __instance, ref BillStack __result)
        {
            if (memberData.TryGetValue(__instance, out var member) && member.Group != null)
            {
                __result = member.Group.sharedBillStack;
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("ExposeData")]
        public static void ExposeData_Postfix(Building_WorkTable __instance)
        {
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var member = GetMemberData(__instance);
                
                int groupID = member.Group?.loadID ?? -1;
                Scribe_Values.Look(ref groupID, $"workbenchGroupID_{__instance.thingIDNumber}", -1);

                if (Scribe.mode == LoadSaveMode.PostLoadInit && groupID != -1)
                {
                    var manager = __instance.Map?.GetComponent<WorkbenchGroupManager>();
                    var group = manager?.GetGroupByID(groupID);
                    if (group != null)
                    {
                        WorkbenchGroupUtility.SetWorkbenchGroup(member, group);
                    }
                }
            }
        }
    }
}