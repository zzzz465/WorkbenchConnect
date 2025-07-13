using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using WorkbenchConnect.Core;
using WorkbenchConnect.Utils;

namespace WorkbenchConnect.Patches
{
    public static class Building_WorkTable_Patches
    {
        private static readonly Dictionary<Building_WorkTable, WorkbenchGroupMemberData> memberData = [];

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

        public static void Patch(Harmony harmony)
        {
            var despawn_original = AccessTools.Method(typeof(Building), "DeSpawn");
            var despawn_prefix = AccessTools.Method(typeof(Building_WorkTable_Patches), "DeSpawn_Prefix");
            harmony.Patch(despawn_original, prefix: new HarmonyMethod(despawn_prefix));

            var spawnSetup_original = AccessTools.Method(typeof(Building_WorkTable), "SpawnSetup");
            var spawnSetup_postfix = AccessTools.Method(typeof(Building_WorkTable_Patches), "SpawnSetup_Postfix");
            harmony.Patch(spawnSetup_original, postfix: new HarmonyMethod(spawnSetup_postfix));

            var getGizmos_original = AccessTools.Method(typeof(Building), "GetGizmos");
            var getGizmos_postfix = AccessTools.Method(typeof(Building_WorkTable_Patches), "GetGizmos_Postfix");
            harmony.Patch(getGizmos_original, postfix: new HarmonyMethod(getGizmos_postfix));

            var billStack_original = AccessTools.PropertyGetter(typeof(Building_WorkTable), "BillStack");
            var billStack_prefix = AccessTools.Method(typeof(Building_WorkTable_Patches), "BillStack_Prefix");
            harmony.Patch(billStack_original, prefix: new HarmonyMethod(billStack_prefix));

            var exposeData_original = AccessTools.Method(typeof(Building_WorkTable), "ExposeData");
            var exposeData_postfix = AccessTools.Method(typeof(Building_WorkTable_Patches), "ExposeData_Postfix");
            harmony.Patch(exposeData_original, postfix: new HarmonyMethod(exposeData_postfix));
        }

        public static void SpawnSetup_Postfix(Building_WorkTable __instance)
        {
            var member = GetMemberData(__instance);
            DebugHelper.Log($"Workbench spawned: {__instance.def.defName} at {__instance.Position}");
        }

        public static void DeSpawn_Prefix(Building __instance)
        {
            // NOTE: Building_WorkTables 은 virtual method override 가 없으므로 Building 을 대신 패치함, 이를 고려할 것
            if (__instance is not Building_WorkTable workTable)
            {
                return;
            }

            if (memberData.TryGetValue(workTable, out var member))
            {   
                member.Group?.RemoveMember(member);
                memberData.Remove(workTable);
                DebugHelper.Log($"Workbench despawned: {workTable.def.defName} at {workTable.Position}");
            }
        }

        public static IEnumerable<Gizmo> GetGizmos_Postfix(IEnumerable<Gizmo> __result, Building __instance)
        {
            foreach (var gizmo in __result)
                yield return gizmo;

            if (__instance is Building_WorkTable workTable && workTable.Faction == Faction.OfPlayer)
            {
                var member = GetMemberData(workTable);
                foreach (var gizmo in WorkbenchGroupUtility.WorkbenchGroupMemberGizmos(member))
                    yield return gizmo;
            }
        }

        public static bool BillStack_Prefix(Building_WorkTable __instance, ref BillStack __result)
        {
            var member = GetMemberData(__instance);
            if (member.Group != null)
            {
                __result = member.Group.sharedBillStack;
                Log.Message($"[WorkbenchConnect] BillStack redirected to shared stack for {__instance.def.defName} at {__instance.Position}");
                return false;
            }
            return true;
        }

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
