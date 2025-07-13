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

        private class WorkbenchGroupMemberData : IWorkbenchGroupMember, ILoadReferenceable
        {
            public Building_WorkTable workTable;
            public WorkbenchGroup group;
            public int savedGroupID = -1; // Store group ID separately for save/load

            public WorkbenchGroup Group
            {
                get => group;
                set
                {
                    if (group != value)
                    {
                        var oldGroup = group;
                        group = value;
                        UpdateBillStackReference(oldGroup);
                    }
                }
            }

            public Map Map => workTable.Map;
            public IntVec3 Position => workTable.Position;
            public string WorkbenchGroupTag => workTable.def.defName;
            public bool DrawConnectionOverlay => true;
            public Thing SelectableThing => workTable;

            public BillStack BillStack => workTable.billStack;

            public string GetUniqueLoadID() => $"WorkbenchGroupMemberData_{workTable.thingIDNumber}";

            private void UpdateBillStackReference(WorkbenchGroup oldGroup)
            {
                if (group != null)
                {
                    // Replace with shared bill stack
                    workTable.billStack = group.sharedBillStack;
                }
                else
                {
                    // Create new bill stack when ungrouping
                    var newBillStack = new BillStack(workTable);
                    
                    // Copy bills from shared stack if there was an old group
                    if (oldGroup?.sharedBillStack != null)
                    {
                        foreach (var bill in oldGroup.sharedBillStack.Bills.ToList())
                        {
                            newBillStack.AddBill(bill.Clone());
                        }
                    }
                    
                    workTable.billStack = newBillStack;
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



            var exposeData_original = AccessTools.Method(typeof(Building_WorkTable), "ExposeData");
            var exposeData_prefix = AccessTools.Method(typeof(Building_WorkTable_Patches), "ExposeData_Prefix");
            var exposeData_postfix = AccessTools.Method(typeof(Building_WorkTable_Patches), "ExposeData_Postfix");
            harmony.Patch(exposeData_original, prefix: new HarmonyMethod(exposeData_prefix), postfix: new HarmonyMethod(exposeData_postfix));

            var drawExtraSelectionOverlays_original = AccessTools.Method(typeof(Building), "DrawExtraSelectionOverlays");
            var drawExtraSelectionOverlays_postfix = AccessTools.Method(typeof(Building_WorkTable_Patches), "DrawExtraSelectionOverlays_Postfix");
            harmony.Patch(drawExtraSelectionOverlays_original, postfix: new HarmonyMethod(drawExtraSelectionOverlays_postfix));
        }

        public static void SpawnSetup_Postfix(Building_WorkTable __instance)
        {
            var member = GetMemberData(__instance) as WorkbenchGroupMemberData;
            DebugHelper.Log($"[DEBUG] Workbench spawned: {__instance.def.defName} at {__instance.Position}");
            
            // Try to restore to saved group if we have one
            if (member?.savedGroupID > 0)
            {
                DebugHelper.Log($"[DEBUG] Attempting to restore spawned workbench {__instance.def.defName} to group {member.savedGroupID}");
                var manager = __instance.Map?.GetComponent<WorkbenchGroupManager>();
                DebugHelper.Log($"[DEBUG] Manager found: {manager != null}");
                
                if (manager != null)
                {
                    var group = manager.GetGroupByID(member.savedGroupID);
                    DebugHelper.Log($"[DEBUG] Group {member.savedGroupID} found: {group != null}");
                    
                    if (group != null)
                    {
                        // Add this member to the group (this will set the shared billStack)
                        group.AddMember(member);
                        DebugHelper.Log($"[DEBUG] Successfully restored workbench {__instance.def.defName} to group {member.savedGroupID}");
                        
                        // Clear the saved group ID since we've successfully restored
                        member.savedGroupID = -1;
                    }
                    else
                    {
                        DebugHelper.Warning($"Could not find group {member.savedGroupID} for workbench {__instance.def.defName}");
                    }
                }
                else
                {
                    DebugHelper.Warning($"No WorkbenchGroupManager found on map {__instance.Map} for workbench {__instance.def.defName}");
                }
            }
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



        private static readonly Dictionary<Building_WorkTable, BillStack> temporaryBillStacks = [];

        public static void ExposeData_Prefix(Building_WorkTable __instance)
        {
            var member = GetMemberData(__instance);
            
            // During saving, temporarily create an empty billStack for grouped workbenches
            // This prevents the shared bills from being saved multiple times
            if (member.Group != null && Scribe.mode == LoadSaveMode.Saving)
            {
                // Store original billStack and create empty one for saving
                temporaryBillStacks[__instance] = __instance.billStack;
                __instance.billStack = new BillStack(__instance); // Empty bill stack
            }
        }

        public static void ExposeData_Postfix(Building_WorkTable __instance)
        {
            var member = GetMemberData(__instance) as WorkbenchGroupMemberData;

            // Restore original billStack after saving
            if (temporaryBillStacks.TryGetValue(__instance, out var originalBillStack))
            {
                __instance.billStack = originalBillStack;
                temporaryBillStacks.Remove(__instance);
                DebugHelper.Log($"Restored original billStack for {__instance.def.defName} after saving");
            }

            // Use the saved group ID during loading, current group ID during saving
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                member.savedGroupID = member.Group?.loadID ?? -1;
            }
            
            DebugHelper.Log($"[DEBUG] Workbench {__instance.def.defName} (ID: {__instance.thingIDNumber}) - Mode: {Scribe.mode}, SavedGroupID: {member.savedGroupID}");
            
            Scribe_Values.Look(ref member.savedGroupID, $"workbenchGroupID_{__instance.thingIDNumber}", -1);
            
            DebugHelper.Log($"After Scribe_Values.Look - SavedGroupID: {member.savedGroupID}");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Note: Group restoration is now handled in SpawnSetup_Postfix when workbench has proper map reference
                if (member.savedGroupID == -1)
                {
                    DebugHelper.Log($"Workbench {__instance.def.defName} is ungrouped");
                }
                else
                {
                    DebugHelper.Log($"Workbench {__instance.def.defName} will be restored to group {member.savedGroupID} on spawn");
                }
                
                // Ensure workbench has a proper billStack if not grouped
                if (__instance.billStack == null)
                {
                    __instance.billStack = new BillStack(__instance);
                }
            }
            else if (Scribe.mode == LoadSaveMode.Saving)
            {
                DebugHelper.Log($"Saving workbench {__instance.def.defName} with groupID {member.savedGroupID}");
            }
        }

        public static void DrawExtraSelectionOverlays_Postfix(Building __instance)
        {
            if (__instance is Building_WorkTable workTable)
            {
                var member = GetMemberData(workTable);
                WorkbenchGroupUtility.DrawSelectionOverlaysFor(member);
            }
        }
    }
}
