using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
        private List<Bill> restoredBills = null; // Used for both serialization and restoration
        
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
                // During loading, don't validate member count since members haven't been added yet
                if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.ResolvingCrossRefs || Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    DebugHelper.Log($"[DEBUG] Group {loadID} Valid check during {Scribe.mode} - returning true");
                    return true;
                }
                bool isValid = members.Count > 1 && members.All(m => m?.Map != null);
                DebugHelper.Log($"[DEBUG] Group {loadID} Valid check during {Scribe.mode} - members={members.Count}, returning {isValid}");
                return isValid;
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

        public void InitializeWithBills(IWorkbenchGroupMember founder, List<Bill> allBills)
        {
            // Initialize shared bill stack with the founder as billGiver
            sharedBillStack = new BillStack(founder.SelectableThing as IBillGiver);
            
            // Add all collected bills to shared stack (without cloning to preserve ownership)
            foreach (var bill in allBills)
            {
                sharedBillStack.AddBill(bill);
            }
            
            members.Add(founder);
            founder.Group = this;
            groupLabel = GetNewGroupLabel();
            
            DebugHelper.Log($"Initialized new workbench group with founder: {founder} and {allBills.Count} bills");
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
                DebugHelper.Log($"[DEBUG] Created new sharedBillStack for group {loadID}");
                
                // Restore saved bills if available (this happens during save loading)
                if (restoredBills != null)
                {
                    DebugHelper.Log($"[DEBUG] Restoring {restoredBills.Count} bills to group {loadID}");
                    foreach (var bill in restoredBills)
                    {
                        // Ensure bill has proper billStack reference to prevent null reference errors
                        sharedBillStack.AddBill(bill);
                        
                        // Ensure bill has proper billStack reference (essential during loading)
                        var billStackField = AccessTools.Field(typeof(Bill), "billStack");
                        billStackField?.SetValue(bill, sharedBillStack);
                        
                        DebugHelper.Log($"[DEBUG] Restored bill: {bill.Label} to group {loadID}");
                    }
                    DebugHelper.Log($"[DEBUG] After restoration, sharedBillStack has {sharedBillStack.Bills.Count} bills");
                    restoredBills = null; // Clear after restoration
                }
                else
                {
                    DebugHelper.Log($"[DEBUG] No restoredBills to restore for group {loadID}");
                }
            }
            else
            {
                // If sharedBillStack already exists, update its billGiver to this member
                // This can happen when multiple members are added to an existing group
                var billGiverField = AccessTools.Field(typeof(BillStack), "billGiver");
                if (billGiverField?.GetValue(sharedBillStack) == null)
                {
                    billGiverField.SetValue(sharedBillStack, member.SelectableThing as IBillGiver);
                }
            }
            
            // Migrate existing bills to shared stack BEFORE setting Group property
            // (because setting Group will replace the billStack field)
            // Check for duplicates to avoid adding bills that are already in the shared stack
            if (member.BillStack?.Bills?.Any() == true)
            {
                var billsToMigrate = member.BillStack.Bills.ToList();
                foreach (var bill in billsToMigrate)
                {
                    // Only add if not already in shared stack (to prevent duplicates)
                    if (!sharedBillStack.Bills.Contains(bill))
                    {
                        sharedBillStack.AddBill(bill);
                    }
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
            DebugHelper.Log($"[DEBUG] WorkbenchGroup.ExposeData() - Mode: {Scribe.mode}, LoadID: {loadID}, Label: '{groupLabel}', Members: {members?.Count ?? 0}, Map: {Map}");
            
            Scribe_Values.Look(ref loadID, "loadID", 0);
            Scribe_Values.Look(ref groupLabel, "groupLabel", "");
            // Don't save members list - it will be reconstructed from individual workbenches
            
            // Save bills separately, not as a BillStack
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                restoredBills = sharedBillStack?.Bills?.ToList();
                DebugHelper.Log($"[DEBUG] Saving group {loadID} with {restoredBills?.Count ?? 0} bills");
                if (restoredBills != null)
                {
                    foreach (var bill in restoredBills)
                    {
                        DebugHelper.Log($"[DEBUG] Saving bill: {bill.Label} in group {loadID}");
                    }
                }
            }
            Scribe_Collections.Look(ref restoredBills, "savedBills", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                DebugHelper.Log($"[DEBUG] During {Scribe.mode}, group {loadID} has {restoredBills?.Count ?? 0} bills");
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                DebugHelper.Log($"[DEBUG] Loading group {loadID} - got {restoredBills?.Count ?? 0} bills");
                
                if (members == null)
                    members = [];
                
                // Initialize bill reservations dictionary
                if (billReservations == null)
                    billReservations = new Dictionary<Bill, Pawn>();
                
                // Bills are already in restoredBills, ready for member restoration
                if (restoredBills != null)
                {
                    DebugHelper.Log($"[DEBUG] {restoredBills.Count} bills ready for restoration in group {loadID}");
                    foreach (var bill in restoredBills)
                    {
                        DebugHelper.Log($"[DEBUG] Loaded bill: {bill.Label} for group {loadID}");
                    }
                }
                else
                {
                    DebugHelper.Log($"[DEBUG] No bills loaded for group {loadID}");
                }
            }
            

        }
    }
}
