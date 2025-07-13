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
        private List<Bill> restoredBills = null;
        
        // Bill reservation system to prevent multiple pawns working on same bill
        private Dictionary<Bill, Pawn> billReservations = new Dictionary<Bill, Pawn>();
        
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

        // Check if a bill is available for a pawn to work on
        public bool CanPawnWorkOnBill(Bill bill, Pawn pawn)
        {
            if (bill == null || pawn == null) return false;
            
            // Clean up stale reservations first
            CleanupStaleReservations();
            
            // Check if bill is already reserved by another pawn
            if (billReservations.TryGetValue(bill, out Pawn reservedPawn))
            {
                return reservedPawn == pawn;
            }
            
            return true;
        }

        // Reserve a bill for a pawn
        public bool TryReserveBill(Bill bill, Pawn pawn)
        {
            if (bill == null || pawn == null) return false;
            
            CleanupStaleReservations();
            
            if (billReservations.TryGetValue(bill, out Pawn existingPawn))
            {
                return existingPawn == pawn;
            }
            
            billReservations[bill] = pawn;
            DebugHelper.Log($"Bill reserved: {bill.LabelCap} by {pawn.LabelShort}");
            return true;
        }

        // Release a bill reservation
        public void ReleaseBillReservation(Bill bill, Pawn pawn)
        {
            if (bill == null || pawn == null) return;
            
            if (billReservations.TryGetValue(bill, out Pawn reservedPawn) && reservedPawn == pawn)
            {
                billReservations.Remove(bill);
                DebugHelper.Log($"Bill reservation released: {bill.LabelCap} by {pawn.LabelShort}");
            }
        }

        // Clean up reservations for pawns that are no longer valid
        private void CleanupStaleReservations()
        {
            var keysToRemove = new List<Bill>();
            
            foreach (var kvp in billReservations)
            {
                var bill = kvp.Key;
                var pawn = kvp.Value;
                
                // Remove if bill is deleted or pawn is invalid
                if (bill.DeletedOrDereferenced || pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Dead)
                {
                    keysToRemove.Add(bill);
                }
                // Remove if pawn is no longer doing a DoBill job
                else if (pawn.CurJob?.def != JobDefOf.DoBill || pawn.CurJob?.bill != bill)
                {
                    keysToRemove.Add(bill);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                billReservations.Remove(key);
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
                
                // Restore saved bills if available
                if (restoredBills != null)
                {
                    foreach (var bill in restoredBills)
                    {
                        sharedBillStack.AddBill(bill);
                    }
                    restoredBills = null; // Clear after restoration
                }
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
            // Don't save members list - it will be reconstructed from individual workbenches
            
            // Save bills separately, not as a BillStack
            List<Bill> savedBills = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedBills = sharedBillStack?.Bills?.ToList();
            }
            Scribe_Collections.Look(ref savedBills, "savedBills", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (members == null)
                    members = [];
                
                // Initialize bill reservations dictionary
                if (billReservations == null)
                    billReservations = new Dictionary<Bill, Pawn>();
                
                // Restore bills when first member is added
                if (savedBills != null)
                {
                    // Store the saved bills for later restoration
                    restoredBills = savedBills;
                }
            }
        }
    }
}
