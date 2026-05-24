using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using WorkbenchConnect.Utils;

namespace WorkbenchConnect.Core
{
    public class WorkbenchGroup : IExposable
    {
        public int loadID = -1;
        public List<IWorkbenchGroupMember> members = [];
        public string groupLabel = "";
        private List<Bill> restoredBills = null; // Used for both serialization and restoration
        private Dictionary<IWorkbenchGroupMember, string> billFingerprints = new Dictionary<IWorkbenchGroupMember, string>();
        public static bool IsSynchronizingBills;
        
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
                    return true;
                }
                return members.Count > 1 && members.All(m => m?.Map != null);
            }
        }

        // Check if a bill requires reservation (has specific ownership requirements)
        private bool DoesBillRequireReservation(Bill bill)
        {
            if (bill == null) return false;
            
            // Only reserve bills that have ownership requirements
            // Bills like cooking simple meals don't need reservations
            return bill.PawnRestriction != null ||
                   bill.SlavesOnly ||
                   bill.MechsOnly ||
                   bill.NonMechsOnly;
        }

        // Check if a bill is available for a pawn to work on
        public bool CanPawnWorkOnBill(Bill bill, Pawn pawn)
        {
            if (bill == null || pawn == null) return false;
            
            // Skip reservation system for bills that don't require it
            if (!DoesBillRequireReservation(bill))
            {
                return true;
            }
            
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
            
            // Skip reservation system for bills that don't require it
            if (!DoesBillRequireReservation(bill))
            {
                return true;
            }
            
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
            
            // Skip reservation system for bills that don't require it
            if (!DoesBillRequireReservation(bill))
            {
                return;
            }
            
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
            var initialBills = founder.BillStack?.Bills?.ToList() ?? [];

            members.Add(founder);
            founder.Group = this;
            groupLabel = GetNewGroupLabel();
            ApplyBillsToMember(founder, initialBills);
            
            DebugHelper.Log($"Created new workbench group with founder: {founder}");
        }

        public void InitializeWithBills(IWorkbenchGroupMember founder, List<Bill> allBills)
        {
            allBills ??= [];

            members.Add(founder);
            founder.Group = this;
            groupLabel = GetNewGroupLabel();
            ApplyBillsToMember(founder, allBills);
            
            DebugHelper.Log($"Initialized new workbench group with founder: {founder} and {allBills.Count} bills");
        }

        public void AddMember(IWorkbenchGroupMember member)
        {
            if (members.Contains(member))
                return;

            DebugHelper.Log($"Adding member to workbench group: {member}");

            var canonicalBills = GetCanonicalBills();

            members.Add(member);
            member.Group = this;
            ApplyBillsToMember(member, canonicalBills);
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
            billFingerprints.Remove(member);
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
            var map = Map;
            
            foreach (var member in members.ToList())
            {
                member.Group = null;
                billFingerprints.Remove(member);
                member.Notify_GroupChanged();
            }

            billReservations.Clear();
            members.Clear();
            map?.GetComponent<WorkbenchGroupManager>()?.Notify_GroupRemoved(this);
        }

        public void SyncBillsFromMember(IWorkbenchGroupMember source)
        {
            if (IsSynchronizingBills || source?.Group != this || source.BillStack == null)
                return;

            var sourceBills = source.BillStack.Bills?.ToList() ?? [];
            bool previousSyncing = IsSynchronizingBills;
            IsSynchronizingBills = true;

            try
            {
                foreach (var member in members.ToList())
                {
                    if (member == null || member == source)
                        continue;

                    ApplyBillsToMember(member, sourceBills);
                }
            }
            finally
            {
                IsSynchronizingBills = previousSyncing;
            }

            RememberBillFingerprint(source);
            foreach (var member in members)
            {
                RememberBillFingerprint(member);
            }
        }

        public void SyncBillsFromMemberIfChanged(IWorkbenchGroupMember source)
        {
            if (IsSynchronizingBills || source?.Group != this || source.BillStack == null)
                return;

            string fingerprint = GetBillFingerprint(source);
            if (!billFingerprints.TryGetValue(source, out var knownFingerprint))
            {
                billFingerprints[source] = fingerprint;
                return;
            }

            if (knownFingerprint != fingerprint)
            {
                SyncBillsFromMember(source);
            }
        }

        private List<Bill> GetCanonicalBills()
        {
            var source = members.FirstOrDefault(m => m?.BillStack?.Bills != null);
            if (source?.BillStack?.Bills != null)
                return source.BillStack.Bills.ToList();

            return restoredBills?.ToList() ?? [];
        }

        private void ApplyBillsToMember(IWorkbenchGroupMember member, List<Bill> sourceBills)
        {
            if (member?.BillStack == null)
                return;

            var clonedBills = CloneBills(sourceBills);
            bool previousSyncing = IsSynchronizingBills;
            IsSynchronizingBills = true;

            try
            {
                member.BillStack.Clear();
                foreach (var bill in clonedBills)
                {
                    member.BillStack.AddBill(bill);
                }
            }
            finally
            {
                IsSynchronizingBills = previousSyncing;
            }

            RememberBillFingerprint(member);
        }

        private static List<Bill> CloneBills(IEnumerable<Bill> sourceBills)
        {
            var clonedBills = new List<Bill>();
            if (sourceBills == null)
                return clonedBills;

            foreach (var bill in sourceBills)
            {
                if (bill == null || bill.deleted)
                    continue;

                clonedBills.Add(bill.Clone());
            }

            return clonedBills;
        }

        private void RememberBillFingerprint(IWorkbenchGroupMember member)
        {
            if (member?.BillStack == null)
                return;

            billFingerprints[member] = GetBillFingerprint(member);
        }

        private static string GetBillFingerprint(IWorkbenchGroupMember member)
        {
            return GetBillFingerprint(member?.BillStack?.Bills);
        }

        private static string GetBillFingerprint(IEnumerable<Bill> bills)
        {
            var builder = new StringBuilder();
            if (bills == null)
                return "";

            foreach (var bill in bills)
            {
                AppendBillFingerprint(builder, bill);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void AppendBillFingerprint(StringBuilder builder, Bill bill)
        {
            if (bill == null)
            {
                builder.Append("<null>");
                return;
            }

            builder.Append(bill.GetType().FullName).Append('|');
            builder.Append(bill.recipe?.defName).Append('|');
            builder.Append(bill.suspended).Append('|');
            builder.Append(bill.ingredientSearchRadius).Append('|');
            builder.Append(bill.allowedSkillRange.min).Append('-').Append(bill.allowedSkillRange.max).Append('|');
            builder.Append(bill.PawnRestriction?.thingIDNumber ?? -1).Append('|');
            builder.Append(bill.SlavesOnly).Append('|').Append(bill.MechsOnly).Append('|').Append(bill.NonMechsOnly).Append('|');
            AppendThingFilterFingerprint(builder, bill.ingredientFilter);

            if (bill is Bill_Production production)
            {
                builder.Append('|').Append(production.repeatMode?.defName);
                builder.Append('|').Append(production.repeatCount);
                builder.Append('|').Append(production.targetCount);
                builder.Append('|').Append(production.pauseWhenSatisfied);
                builder.Append('|').Append(production.unpauseWhenYouHave);
                builder.Append('|').Append(production.includeEquipped);
                builder.Append('|').Append(production.includeTainted);
                builder.Append('|').Append(production.hpRange.min).Append('-').Append(production.hpRange.max);
                builder.Append('|').Append(production.qualityRange.min).Append('-').Append(production.qualityRange.max);
                builder.Append('|').Append(production.limitToAllowedStuff);
                builder.Append('|').Append(production.paused);
                builder.Append('|').Append(production.GetStoreMode()?.defName);

                var slotGroup = production.GetSlotGroup();
                builder.Append('|').Append(slotGroup?.GroupingLabel).Append(':').Append(slotGroup?.GroupingOrder ?? -1);

                var includeGroup = production.GetIncludeSlotGroup();
                builder.Append('|').Append(includeGroup?.GroupingLabel).Append(':').Append(includeGroup?.GroupingOrder ?? -1);
                builder.Append('|').Append(production.RenamableLabel);
            }
        }

        private static void AppendThingFilterFingerprint(StringBuilder builder, ThingFilter filter)
        {
            if (filter == null)
            {
                builder.Append("<filter:null>");
                return;
            }

            builder.Append("filter:");
            builder.Append(filter.Summary).Append(':');
            builder.Append(filter.AllowedDefCount).Append(':');
            builder.Append(filter.AllowedHitPointsPercents.min).Append('-').Append(filter.AllowedHitPointsPercents.max).Append(':');
            builder.Append(filter.AllowedQualityLevels.min).Append('-').Append(filter.AllowedQualityLevels.max).Append(':');

            foreach (var def in filter.AllowedThingDefs?.OrderBy(def => def.defName) ?? Enumerable.Empty<ThingDef>())
            {
                builder.Append(def.defName).Append(',');
            }
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
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                restoredBills = GetCanonicalBills();
                DebugHelper.Log($"Saving group {loadID} with {restoredBills?.Count ?? 0} bills");
            }
            Scribe_Collections.Look(ref restoredBills, "savedBills", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                DebugHelper.Log($"Loading group {loadID} - got {restoredBills?.Count ?? 0} bills");
                
                if (members == null)
                    members = [];
                
                // Initialize bill reservations dictionary
                if (billReservations == null)
                    billReservations = new Dictionary<Bill, Pawn>();
            }
        }
    }
}
