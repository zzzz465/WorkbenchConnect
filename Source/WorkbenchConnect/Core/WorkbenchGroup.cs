using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using WorkbenchConnect.Utils;

namespace WorkbenchConnect.Core
{
    public class WorkbenchGroup : IExposable
    {
        public int loadID = -1;
        public List<IWorkbenchGroupMember> members = [];
        public BillStack sharedBillStack;
        public string groupLabel = "";
        
        public Map Map
        {
            get
            {
                return members.FirstOrDefault()?.Map;
            }
        }

        public bool Valid
        {
            get
            {
                return members.Count > 1 && members.All(m => m?.Map != null);
            }
        }

        public WorkbenchGroup()
        {
            // Will be initialized when first member is added
        }

        public WorkbenchGroup(IWorkbenchGroupMember founder)
        {
            // Initialize shared bill stack with the founder as billGiver
            sharedBillStack = new BillStack(founder.SelectableThing as IBillGiver);
            
            members.Add(founder);
            founder.Group = this;
            groupLabel = GetNewGroupLabel();
            
            DebugHelper.Log($"Created new workbench group with founder: {founder}");
        }

        public void AddMember(IWorkbenchGroupMember member)
        {
            if (members.Contains(member))
                return;

            DebugHelper.Log($"Adding member to workbench group: {member}");
            
            // Initialize shared bill stack if not already done
            if (sharedBillStack == null)
            {
                sharedBillStack = new BillStack(member.SelectableThing as IBillGiver);
            }
            
            // Migrate existing bills to shared stack BEFORE setting Group property
            // (because setting Group will replace the billStack field)
            if (member.BillStack?.Bills?.Any() == true)
            {
                var billsToMigrate = member.BillStack.Bills.ToList();
                foreach (var bill in billsToMigrate)
                {
                    sharedBillStack.AddBill(bill);
                }
            }

            members.Add(member);
            member.Group = this; // This will replace member's billStack with sharedBillStack
            member.Notify_GroupChanged();
            
            NotifyMembersChanged();
        }

        public void RemoveMember(IWorkbenchGroupMember member)
        {
            if (!members.Contains(member))
                return;

            DebugHelper.Log($"Removing member from workbench group: {member}");

            members.Remove(member);
            member.Group = null;
            member.Notify_GroupChanged();

            if (members.Count <= 1)
            {
                DebugHelper.Log("Workbench group has 1 or fewer members, dissolving");
                Dissolve();
            }
            else
            {
                NotifyMembersChanged();
            }
        }

        public void Dissolve()
        {
            DebugHelper.Log($"Dissolving workbench group with {members.Count} members");
            
            // Clear all member references first (this will create individual bill stacks with copied bills)
            foreach (var member in members.ToList())
            {
                member.Group = null;
                member.Notify_GroupChanged();
            }
            
            // Clear the shared bill stack directly (avoid Delete() which needs valid billGiver)
            if (sharedBillStack != null)
            {
                sharedBillStack.Bills.Clear();
            }
            
            members.Clear();
            Map?.GetComponent<WorkbenchGroupManager>()?.Notify_GroupRemoved(this);
        }

        private void NotifyMembersChanged()
        {
            foreach (var member in members)
            {
                member.Notify_GroupChanged();
            }
        }

        private string GetNewGroupLabel()
        {
            var manager = Map?.GetComponent<WorkbenchGroupManager>();
            return manager?.NewWorkbenchGroupName() ?? "Workbench Group";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID", 0);
            Scribe_Values.Look(ref groupLabel, "groupLabel", "");
            Scribe_Collections.Look(ref members, "members", LookMode.Reference);
            Scribe_Deep.Look(ref sharedBillStack, "sharedBillStack");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (members == null)
                    members = [];
                
                if (sharedBillStack == null && members.Any())
                {
                    // Initialize with first available member as billGiver
                    var firstMember = members.First();
                    sharedBillStack = new BillStack(firstMember.SelectableThing as IBillGiver);
                }

                // Clean up null members
                members.RemoveAll(m => m == null);
                
                // Notify all members of their group
                foreach (var member in members)
                {
                    member.Group = this;
                    member.Notify_GroupChanged();
                }

                // Remove invalid groups
                if (!Valid)
                {
                    DebugHelper.Warning($"Loaded invalid workbench group, dissolving");
                    Dissolve();
                }
            }
        }
    }
}
