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
        public List<IWorkbenchGroupMember> members = new List<IWorkbenchGroupMember>();
        public BillStack sharedBillStack = new BillStack(null);
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
        }

        public WorkbenchGroup(IWorkbenchGroupMember founder)
        {
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
            
            // If member has existing bills, migrate them to shared stack
            if (member.BillStack?.Bills?.Any() == true)
            {
                var billsToMigrate = member.BillStack.Bills.ToList();
                foreach (var bill in billsToMigrate)
                {
                    member.BillStack.Delete(bill);
                    sharedBillStack.AddBill(bill);
                }
            }

            members.Add(member);
            member.Group = this;
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
            
            // If there's a remaining member, give them the shared bills
            var remainingMember = members.FirstOrDefault();
            if (remainingMember != null && sharedBillStack.Bills.Any())
            {
                var billsToMigrate = sharedBillStack.Bills.ToList();
                foreach (var bill in billsToMigrate)
                {
                    sharedBillStack.Delete(bill);
                    remainingMember.BillStack.AddBill(bill);
                }
            }

            // Clear all member references
            foreach (var member in members.ToList())
            {
                member.Group = null;
                member.Notify_GroupChanged();
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
                    members = new List<IWorkbenchGroupMember>();
                
                if (sharedBillStack == null)
                    sharedBillStack = new BillStack(null);

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