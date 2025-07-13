using System.Collections.Generic;
using System.Linq;
using Verse;
using WorkbenchConnect.Utils;

namespace WorkbenchConnect.Core
{
    public class WorkbenchGroupManager : MapComponent, IExposable
    {
        private List<WorkbenchGroup> groups = [];
        private int nextGroupID = 1;

        public WorkbenchGroupManager(Map map) : base(map)
        {
        }

        public IEnumerable<WorkbenchGroup> Groups => groups.AsReadOnly();

        public WorkbenchGroup NewGroup(IWorkbenchGroupMember founder)
        {
            if (founder?.Map != map)
            {
                DebugHelper.Error($"Attempted to create workbench group on wrong map");
                return null;
            }

            var group = new WorkbenchGroup(founder)
            {
                loadID = nextGroupID++
            };

            groups.Add(group);
            DebugHelper.Log($"Created new workbench group {group.loadID} on map {map} with founder {founder}");
            DebugHelper.Log($"Total groups now: {groups.Count}");
            
            return group;
        }

        public WorkbenchGroup NewGroupWithBills(IWorkbenchGroupMember founder, List<RimWorld.Bill> allBills)
        {
            if (founder?.Map != map)
            {
                DebugHelper.Error($"Attempted to create workbench group on wrong map");
                return null;
            }

            var group = new WorkbenchGroup()
            {
                loadID = nextGroupID++
            };

            // Initialize the group with all collected bills
            group.InitializeWithBills(founder, allBills);

            groups.Add(group);
            DebugHelper.Log($"Created new workbench group {group.loadID} with {allBills.Count} bills on map {map} with founder {founder}");
            DebugHelper.Log($"Total groups now: {groups.Count}");
            
            return group;
        }

        public void Notify_GroupRemoved(WorkbenchGroup group)
        {
            if (groups.Contains(group))
            {
                groups.Remove(group);
                DebugHelper.Log($"Removed workbench group {group.loadID} from map {map}");
            }
        }

        public void Notify_MemberRemoved(IWorkbenchGroupMember member)
        {
            var group = member.Group;
            if (group != null && groups.Contains(group))
            {
                group.RemoveMember(member);
            }
        }

        public string NewWorkbenchGroupName()
        {
            int num = 1;
            string baseName = "WorkbenchConnect.GroupDefaultName".Translate();
            
            while (groups.Any(g => g.groupLabel == $"{baseName} {num}"))
            {
                num++;
            }
            
            return $"{baseName} {num}";
        }

        public WorkbenchGroup GetGroupByID(int id)
        {
            return groups?.FirstOrDefault(g => g.loadID == id);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                DebugHelper.Log($"Saving {groups.Count} workbench groups");
            }
            
            Scribe_Values.Look(ref nextGroupID, "nextGroupID", 1);
            Scribe_Collections.Look(ref groups, "groups", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                DebugHelper.Log($"After loading: Groups={groups?.Count ?? 0}, NextGroupID={nextGroupID}");
                
                if (groups == null)
                    groups = [];

                // Clean up invalid groups and update next ID
                int originalCount = groups.Count;
                groups.RemoveAll(g => g == null || !g.Valid);
                
                if (originalCount != groups.Count)
                {
                    DebugHelper.Log($"Removed {originalCount - groups.Count} invalid groups");
                }
                
                if (groups.Any())
                {
                    nextGroupID = groups.Max(g => g.loadID) + 1;
                }

                DebugHelper.Log($"Final loaded groups: {groups.Count}");
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
            // Periodic cleanup of invalid groups
            if (Find.TickManager.TicksGame % 2500 == 0) // Every ~40 seconds
            {
                CleanupInvalidGroups();
            }
        }

        private void CleanupInvalidGroups()
        {
            var invalidGroups = groups.Where(g => !g.Valid).ToList();
            foreach (var group in invalidGroups)
            {
                DebugHelper.Log($"Cleaning up invalid workbench group {group.loadID}");
                group.Dissolve();
            }
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            
            // Draw connection overlays for selected workbenches
            if (Find.Selector.SelectedObjects?.Any() == true)
            {
                foreach (var selected in Find.Selector.SelectedObjects)
                {
                    if (selected is IWorkbenchGroupMember member && member.Group != null)
                    {
                        WorkbenchGroupUtility.DrawSelectionOverlaysFor(member);
                    }
                }
            }
        }
    }
}
